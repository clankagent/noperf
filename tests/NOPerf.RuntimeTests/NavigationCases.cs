using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoadPathfinding;

namespace NOPerformance;

public sealed partial class RuntimeTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OriginalRoad(RoadNetwork? network, GlobalPosition start, GlobalPosition target, List<Node> result, out RoadPathfinder.PathfindResult status) => throw new NotImplementedException();

    private void NavigationParity(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(RoadPathfinder), "TryPathfind");
        Check(Harmony.GetPatchInfo(method).Owners.Contains(NavigationPlugin.Id), "Navigation patch active");
        harmony.CreateReversePatcher(method, new HarmonyMethod(typeof(RuntimeTests), nameof(OriginalRoad))).Patch();
        var random = new Random(6173);
        int cases = 0;
        foreach (int size in new[] {8, 32, 96})
        for (int graph = 0; graph < 16; graph++)
        {
            var network = RoadGraph(size, random, graph % 3 == 0);
            if (graph == 7) network.nodes.Add(network.nodes[2]); // Duplicate node entries.
            for (int query = 0; query < 10; query++)
            {
                var start = network.nodes[random.Next(size)].position;
                var target = network.nodes[random.Next(size)].position;
                CompareRoad(network, start, target); cases++;
            }
        }
        CompareRoad(new RoadNetwork(), default, default); cases++;
        CompareRoad(null!, default, default); cases++;
        NavigationMembership();
        Write("Road actual-method parity: " + cases + " route/status/parent/distance comparisons, including ties and disconnected graphs.");
        foreach (int size in new[] {32, 128, 512})
        {
            var network = RoadGraph(size, new Random(4811), false);
            var output = new List<Node>();
            int iterations = size == 32 ? 1000 : size == 128 ? 200 : 25;
            // Alternating order limits first-run temperature/GC bias.
            RoadBench("original", network, output, true, iterations);
            RoadBench("cached", network, output, false, iterations);
            RoadBench("cached-repeat", network, output, false, iterations);
            RoadBench("original-repeat", network, output, true, iterations);
        }
    }
    private static RoadNetwork RoadGraph(int size, Random random, bool disconnected)
    {
        var network = new RoadNetwork();
        for (int i = 0; i < size; i++) _ = new Node(network, new GlobalPosition(i % 8 * 50, 0, i / 8 * 50));
        void Connect(int a, int b)
        {
            var road = new Road {startNode=network.nodes[a], endNode=network.nodes[b], length=random.Next(1,5)};
            road.AddPoint(road.startNode.position); road.AddPoint(road.endNode.position);
            network.roads.Add(road);
            road.startNode.connectionsLookup.Add(road,road.endNode);
            road.endNode.connectionsLookup.Add(road,road.startNode);
        }
        for (int i = 1; i < size; i++) if (!disconnected || i != size/2) Connect(i-1,i);
        for (int i = 0; i < size*2; i++)
        {
            int a=random.Next(size),b=random.Next(size);
            if (a==b || disconnected && (a<size/2)!=(b<size/2)) continue;
            Connect(a,b);
        }
        return network;
    }
    private void CompareRoad(RoadNetwork? network, GlobalPosition start, GlobalPosition target)
    {
        var a=new List<Node>();var b=new List<Node>();
        if(network != null && network.nodes.Count>0) {a.Add(network.nodes[0]);b.Add(network.nodes[0]);}
        OriginalRoad(network,start,target,a,out var oldStatus);
        var parents=network?.nodes.Select(n=>n.parent).ToArray();
        var distances=network?.nodes.Select(n=>BitConverter.ToInt32(BitConverter.GetBytes(n.dist),0)).ToArray();
        RoadPathfinder.TryPathfind(network!,start,target,b,out var newStatus);
        Same(a,b,"road selected path");Check(oldStatus==newStatus,"road status");
        if(network==null)return;
        for(int i=0;i<network.nodes.Count;i++)
        {
            Check(ReferenceEquals(parents![i],network.nodes[i].parent),"road parent state");
            Check(distances![i]==BitConverter.ToInt32(BitConverter.GetBytes(network.nodes[i].dist),0),"road exact distance state");
        }
    }
    private void NavigationMembership()
    {
        var begin=AccessTools.Method(typeof(NavigationPlugin),"Begin");
        var end=AccessTools.Method(typeof(NavigationPlugin),"End");
        var contains=(Func<List<Node>,Node,bool>)Delegate.CreateDelegate(typeof(Func<List<Node>,Node,bool>),AccessTools.Method(typeof(NavigationPlugin),"Contains"));
        var remove=(Func<List<Node>,Node,bool>)Delegate.CreateDelegate(typeof(Func<List<Node>,Node,bool>),AccessTools.Method(typeof(NavigationPlugin),"Remove"));
        var network=new RoadNetwork();var n=new Node(network,default);var other=new Node(network,default);
        var list=new List<Node>{n,n,null!};object?[] state={null};begin.Invoke(null,state);
        try
        {
            Check(contains(list,n)&&contains(list,null!),"road duplicate/null membership");
            Check(remove(list,n)&&contains(list,n),"road duplicate remains after removal");
            Check(remove(list,n)&&!contains(list,n),"road last duplicate removed");
            Check(!remove(list,n),"road missing removal");
            Check(remove(list,null!)&&!contains(list,null!),"road null removal");
            list.Add(other);Check(contains(list,other),"road unexpected count mutation falls back");
        }
        finally {end.Invoke(null,state);}
        var foreign=new Node(new RoadNetwork(),default); // Same ID, different identity.
        state=new object?[]{null};begin.Invoke(null,state);
        try
        {
            list.Clear();list.Add(n);Check(contains(list,n)&&!contains(list,foreign),"road foreign same-ID identity");
        }
        finally {end.Invoke(null,state);}
        state=new object?[]{null};begin.Invoke(null,state);
        try
        {
            list.Clear();list.Add(n);list.Add(foreign);
            Check(contains(list,n)&&contains(list,foreign),"road ID collision falls back");
        }
        finally {end.Invoke(null,state);}
        foreach(int unusualId in new[]{-1,10000})
        {
            int oldId=n.id;n.id=unusualId;state=new object?[]{null};begin.Invoke(null,state);
            try {list.Clear();list.Add(n);Check(contains(list,n),"road unusual ID falls back");}
            finally {end.Invoke(null,state);n.id=oldId;}
        }
        state=new object?[]{null};begin.Invoke(null,state);
        try
        {
            list.Clear();list.Add(n);Check(contains(list,n),"road refreshed cache");
            object?[] nested={null};begin.Invoke(null,nested);
            try {list.Clear();Check(!contains(list,n),"nested road call falls back");}
            finally {end.Invoke(null,nested);}
            Check(!contains(list,n),"outer road cache invalidated after nested call");
        }
        finally {end.Invoke(null,state);}
    }
    private void RoadBench(string label, RoadNetwork network, List<Node> output, bool original, int iterations)
    {
        var start=network.nodes[0].position;var target=network.nodes[network.nodes.Count-1].position;
        void Run() {if(original)OriginalRoad(network,start,target,output,out _);else RoadPathfinder.TryPathfind(network,start,target,output,out _);}
        for(int i=0;i<20;i++)Run();
        long ticks=Stopwatch.GetTimestamp();for(int i=0;i<iterations;i++)Run();
        double ms=(Stopwatch.GetTimestamp()-ticks)*1000.0/Stopwatch.Frequency;
        Write($"ROADBENCH {label} nodes={network.nodes.Count} n={iterations} us_per_call={ms*1000/iterations:F2}");
    }
}
