using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace NOPerformance;

public sealed partial class RuntimeTests
{
    private static readonly List<Wreckage> emptyExperimentSnapshot = new();
    // A smaller lab-only alternative: share only an empty List snapshot. The
    // shipped method immediately returns for zero candidates; nonempty copies
    // retain the original allocation and lifetime without scope wrappers.
    private static List<Wreckage> EmptyExperimentSnapshot(IEnumerable<Wreckage> source)
        => source is List<Wreckage> list && list.Count == 0 ? emptyExperimentSnapshot : source.ToList();

    private static IEnumerable<CodeInstruction> EmptyExperimentTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        int replacements = 0;
        foreach (var code in instructions)
        {
            if (code.opcode == OpCodes.Call && code.operand is MethodInfo method && method.DeclaringType == typeof(Enumerable)
                && method.Name == "ToList" && method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(Wreckage))
            {
                code.operand = AccessTools.Method(typeof(RuntimeTests), nameof(EmptyExperimentSnapshot));
                replacements++;
            }
            yield return code;
        }
        if (replacements != 1) throw new InvalidOperationException("Empty experiment requires exactly one wreck snapshot call");
    }

    private void EmptyWreckExperiment(Harmony h)
    {
        var method = AccessTools.Method(typeof(WreckCollector), "FindWreck");
        h.Unpatch(method, HarmonyPatchType.All, WrecksPlugin.Id);
        h.Patch(method, transpiler: new HarmonyMethod(typeof(RuntimeTests), nameof(EmptyExperimentTranspiler)));
        var call = (Action<WreckCollector>)Delegate.CreateDelegate(typeof(Action<WreckCollector>), method);
        CollectorParity();
        BattlefieldGrid.GenerateGrid(32000f, 1000f);
        var collector = Component<WreckCollector>();
        var attached = Component<Unit>();
        AccessTools.Field(typeof(WreckCollector), "attachedUnit").SetValue(collector, attached);
        foreach (int candidates in new[] { 0, 1, 16, 128 })
        {
            foreach (var square in BattlefieldGrid.gridLookup) square.obstacles.Clear();
            for (int i = 0; i < candidates; i++)
            {
                var wreck = Component<Wreckage>();
                wreck.transform.position = new Vector3(250 + i, 0, 250);
                BattlefieldGrid.TryGetGridSquare(wreck.transform.GlobalPosition(), out var square);
                square.obstacles.Add(new Obstacle(wreck.transform, 1, 1));
            }
            Check(BattlefieldGrid.GetWrecksInRangeEnumerable(collector.transform.GlobalPosition(), 1000).Count() == candidates, "empty experiment candidate count");
            int n = candidates == 0 ? 100000 : 10000;
            Measure("empty-only original count=" + candidates, () => OriginalFindWreck(collector), n);
            Measure("empty-only candidate count=" + candidates, () => call(collector), n);
            Measure("empty-only candidate-repeat count=" + candidates, () => call(collector), n);
            Measure("empty-only original-repeat count=" + candidates, () => OriginalFindWreck(collector), n);
        }
        Check(emptyExperimentSnapshot.Count == 0, "shared experimental snapshot never mutated");
        Write("EMPTY_EXPERIMENT_PASS collector_cases=200 candidate_counts=0,1,16,128 production_plugin_unchanged=true");
    }
}
