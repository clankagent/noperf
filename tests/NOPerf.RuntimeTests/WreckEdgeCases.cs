using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace NOPerformance;

public sealed partial class RuntimeTests
{
    private void WreckEdgeCases()
    {
        var owner=Component<WreckCollector>();var other=Component<WreckCollector>();
        var wreck=Component<Wreckage>();var source=new List<Wreckage>{wreck};
        var begin=AccessTools.Method(typeof(WrecksPlugin),"Begin");var end=AccessTools.Method(typeof(WrecksPlugin),"End");
        object?[] state={owner,null};begin.Invoke(null,state);
        List<Wreckage> outer;
        try
        {
            outer=WrecksPlugin.Snapshot(source,owner);
            object?[] nested={other,null};begin.Invoke(null,nested);
            try {Check(!ReferenceEquals(outer,WrecksPlugin.Snapshot(source,other)),"different owner nesting isolated");}
            finally {end.Invoke(null,new[]{nested[1]});}
            Check(outer.Count==1 && ReferenceEquals(outer[0],wreck),"nested owner preserves outer refs");
        }
        finally {end.Invoke(null,new[]{state[1]});}
        Check(outer.Count==0,"outer refs released on exit");

        List<Wreckage>? threaded=null;Exception? threadError=null;
        var thread=new Thread(()=>{try {threaded=WrecksPlugin.Snapshot(source,owner);}catch(Exception ex){threadError=ex;}});
        thread.Start();thread.Join();
        Check(threadError==null && threaded!=null,"off-thread snapshot succeeds");
        Check(!ReferenceEquals(threaded,source) && !ReferenceEquals(threaded,outer),"off-thread snapshot allocated independently");
        source.Clear();Check(threaded!.Count==1,"off-thread snapshot independent of source");source.Add(wreck);

        var caches=(ConditionalWeakTable<WreckCollector,WrecksPlugin.Cache>)AccessTools.Field(typeof(WrecksPlugin),"caches").GetValue(null);
        Check(caches.TryGetValue(owner,out var cache),"nonempty owner cache exists");
        var items=(List<Wreckage>)AccessTools.Field(typeof(WrecksPlugin.Cache),"Items").GetValue(cache);
        state=new object?[]{owner,null};begin.Invoke(null,state);
        try
        {
            Exception? error=null;
            try {WrecksPlugin.Snapshot(ThrowAfter(wreck),owner);}catch(Exception ex){error=ex;}
            Check(error is InvalidOperationException,"enumerator failure preserved");
        }
        finally {end.Invoke(null,new[]{state[1]});}
        Check(items.Count==0,"partially filled snapshot cleared after failure");
        var large=new List<Wreckage>();for(int i=0;i<5000;i++)large.Add(wreck);
        state=new object?[]{owner,null};begin.Invoke(null,state);
        try {Check(WrecksPlugin.Snapshot(large,owner).Count==5000,"large snapshot retains duplicates");}
        finally {end.Invoke(null,new[]{state[1]});}
        Check(items.Count==0 && items.Capacity==0,"oversized storage released on exit");

        // The real shipped method throws after obtaining a nonempty snapshot
        // when a malformed collector has cargo but no attached unit. Compare
        // exception/state and confirm Harmony's finalizer clears its references.
        BattlefieldGrid.GenerateGrid(32000f,1000f);
        wreck.transform.position=new Vector3(250,0,250);
        BattlefieldGrid.TryGetGridSquare(wreck.transform.GlobalPosition(),out var square);
        square.obstacles.Add(new Obstacle(wreck.transform,1,1));
        AccessTools.Field(typeof(WreckCollector),"range").SetValue(owner,1000f);
        AccessTools.Field(typeof(WreckCollector),"currentWrecks").SetValue(owner,1);
        owner.currentState=WreckCollector.CollectorState.Wait;
        Exception? originalError=null,patchedError=null;
        try {OriginalFindWreck(owner);}catch(Exception ex){originalError=ex;}
        var originalState=owner.currentState;
        var call=(Action<WreckCollector>)Delegate.CreateDelegate(typeof(Action<WreckCollector>),AccessTools.Method(typeof(WreckCollector),"FindWreck"));
        try {call(owner);}catch(Exception ex){patchedError=ex;}
        Check(originalError is NullReferenceException && patchedError?.GetType()==originalError.GetType(),"actual collector exception type unchanged");
        Check(owner.currentState==originalState,"actual collector fault state unchanged");
        Check(items.Count==0,"actual Harmony finalizer releases snapshot on exception");
        Write("Wreck edge cases: different-owner nesting, off-thread isolation, throwing enumerator, capacity trim and real-method exception cleanup.");
    }
    private static IEnumerable<Wreckage> ThrowAfter(Wreckage wreck)
    {
        yield return wreck;
        throw new InvalidOperationException("Disposable test enumerator failure");
    }
}
