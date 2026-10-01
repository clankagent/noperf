using System;
using System.Collections.Generic;
using HarmonyLib;
using RoadPathfinding;
using UnityEngine;

namespace NOPerformance;

public sealed partial class RuntimeTests
{
    private void RngParity()
    {
        var saved=UnityEngine.Random.state;
        float fixedStep=Time.fixedDeltaTime;
        try
        {
            _=UnityEngine.Random.value;
            Check(!UnityEngine.Random.state.Equals(saved),"Unity RNG state comparison calibration");
            UnityEngine.Random.state=saved;
            var units=new List<Unit>();
            CompareRng(()=>OriginalUnits(default,8000,units),()=>BattlefieldGrid.GetUnitsInRangeNonAlloc(default,8000,units),"unit query RNG");
            var collector=Component<WreckCollector>();
            AccessTools.Field(typeof(WreckCollector),"attachedUnit").SetValue(collector,Component<Unit>());
            var call=(Action<WreckCollector>)Delegate.CreateDelegate(typeof(Action<WreckCollector>),AccessTools.Method(typeof(WreckCollector),"FindWreck"));
            CompareRng(()=>OriginalFindWreck(collector),()=>call(collector),"wreck search RNG");
            var road=RoadGraph(32,new System.Random(6471),false);
            var route=new List<Node>();
            CompareRng(()=>OriginalRoad(road,road.nodes[0].position,road.nodes[31].position,route,out _),
                ()=>RoadPathfinder.TryPathfind(road,road.nodes[0].position,road.nodes[31].position,route,out _),"road search RNG");
            Check(Time.fixedDeltaTime.Equals(fixedStep),"optimizers preserve fixed-step setting");
            Write("RNG parity: actual unit/wreck/road methods preserve identical Unity RNG state; fixed step unchanged.");
        }
        finally {UnityEngine.Random.state=saved;}
    }
    private void CompareRng(Action original,Action patched,string label)
    {
        var before=UnityEngine.Random.state;
        original();var after=UnityEngine.Random.state;
        UnityEngine.Random.state=before;
        patched();Check(UnityEngine.Random.state.Equals(after),label);
    }
}
