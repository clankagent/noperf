using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using UnityEngine.LowLevel;

namespace NOPerformance;

// Observe native player-loop phases without copying native/unsafe game IL.
// Existing systems retain their exact relative order and update functions.
internal sealed class PhaseProfiler : IDisposable
{
    private sealed class Before { }
    private sealed class After { }
    private sealed class Stat
    {
        internal string Name = "";
        internal long Start, Calls, Ticks, Max;
        internal void Begin() => Start = Stopwatch.GetTimestamp();
        internal void End() { long elapsed=Stopwatch.GetTimestamp()-Start; Calls++; Ticks+=elapsed; if(elapsed>Max)Max=elapsed; }
    }
    private readonly List<Stat> stats = new();
    private readonly ManualLogSource log;
    internal PhaseProfiler(ManualLogSource log)
    {
        this.log=log;
        var loop=PlayerLoop.GetCurrentPlayerLoop();
        Install(ref loop);
        if(stats.Count>0)PlayerLoop.SetPlayerLoop(loop);
        log.LogInfo("Native player-loop phase probes installed="+stats.Count+"; observation overhead applies.");
    }
    private void Install(ref PlayerLoopSystem loop)
    {
        if(loop.subSystemList==null)return;
        var children=new List<PlayerLoopSystem>();
        foreach(var child in loop.subSystemList)
        {
            var updated=child;
            var name=child.type?.FullName;
            bool selected=name=="UnityEngine.PlayerLoop.FixedUpdate+PhysicsFixedUpdate" ||
                name=="UnityEngine.PlayerLoop.FixedUpdate+ScriptRunBehaviourFixedUpdate" ||
                name=="UnityEngine.PlayerLoop.Update+ScriptRunBehaviourUpdate" ||
                name=="UnityEngine.PlayerLoop.PreLateUpdate+ScriptRunBehaviourLateUpdate";
            if(selected)
            {
                var stat=new Stat {Name=child.type!.Name}; stats.Add(stat);
                children.Add(new PlayerLoopSystem {type=typeof(Before),updateDelegate=stat.Begin});
                children.Add(updated);
                children.Add(new PlayerLoopSystem {type=typeof(After),updateDelegate=stat.End});
            }
            else {Install(ref updated);children.Add(updated);}
        }
        loop.subSystemList=children.ToArray();
    }
    internal void Report()
    {
        foreach(var stat in stats)
        {
            if(stat.Calls>0) log.LogInfo($"PHASE {stat.Name} calls={stat.Calls} mean_ms={stat.Ticks*1000.0/Stopwatch.Frequency/stat.Calls:F4} max_ms={stat.Max*1000.0/Stopwatch.Frequency:F3}");
            stat.Calls=stat.Ticks=stat.Max=0;
        }
    }
    private static void Remove(ref PlayerLoopSystem loop)
    {
        if(loop.subSystemList==null)return;
        var children=new List<PlayerLoopSystem>();
        foreach(var child in loop.subSystemList)
        {
            if(child.type==typeof(Before)||child.type==typeof(After))continue;
            var updated=child; Remove(ref updated);children.Add(updated);
        }
        loop.subSystemList=children.ToArray();
    }
    public void Dispose() {var loop=PlayerLoop.GetCurrentPlayerLoop();Remove(ref loop);PlayerLoop.SetPlayerLoop(loop);stats.Clear();}
}
