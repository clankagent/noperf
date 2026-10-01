using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NOPerformance;

[BepInPlugin("agent.noperf.tests", "NO Performance: Disposable Runtime Tests", "0.1.0")]
[BepInDependency("agent.noperf.spatial")]
[BepInDependency("agent.noperf.wrecks")]
[BepInDependency("agent.noperf.navigation")]
public sealed partial class RuntimeTests : BaseUnityPlugin
{
    private readonly StringBuilder report = new();
    private readonly List<GameObject> objects = new();
    private int assertions;
    private void Start()
    {
        if (Environment.GetEnvironmentVariable("NOPERF_WEAPONS_PROFILE") == "1")
        {
            if (BuildGuard.Check(Logger)) StartWeaponsProfile();
            return;
        }
        var reloadMode = Environment.GetEnvironmentVariable("NOPERF_RELOAD");
        if (reloadMode == "baseline" || reloadMode == "all")
        {
            Cysharp.Threading.Tasks.UniTaskExtensions.Forget(MissionReloadTest(reloadMode == "all"));
            return;
        }
        if (Environment.GetEnvironmentVariable("NOPERF_INVENTORY") == "1")
        {
            if (!BuildGuard.Check(Logger)) return;
            var dir = Path.Combine(Paths.BepInExRootPath, "data", "noperf");
            Directory.CreateDirectory(dir);
            NuclearOption.SavedMission.MissionGroup.Init();
            var missionNames = NuclearOption.SavedMission.MissionGroup.BuiltIn.GetMissions().Select(key => key.Name).ToArray();
            File.WriteAllLines(Path.Combine(dir, "builtin-mission-names.txt"), missionNames);
            Logger.LogInfo("Built-in mission inventory names=" + missionNames.Length);
            return;
        }
        if (Environment.GetEnvironmentVariable("NOPERF_TEST") != "1") return;
        if (!BuildGuard.Check(Logger) || UnitRegistry.allUnits.Count != 0) { Logger.LogError("Tests refused: unknown build or populated world."); return; }
        var harmony = new Harmony("agent.noperf.tests");
        var oldGrid = BattlefieldGrid.gridLookup;
        var names = new[] { "mapSize", "gridSize", "divisions" };
        var fields = names.Select(n => AccessTools.Field(typeof(BattlefieldGrid), n)).ToArray();
        var oldValues = fields.Select(f => f.GetValue(null)).ToArray();
        try
        {
            foreach (var pluginId in new[]{"agent.noperf.spatial","agent.noperf.wrecks","agent.noperf.navigation","agent.noperf.diagnostics"})
            {
                var location=BepInEx.Bootstrap.Chainloader.PluginInfos[pluginId].Instance.GetType().Assembly.Location;
                using var stream=File.OpenRead(location);using var sha=SHA256.Create();
                Write("PLUGIN_SHA256 "+Path.GetFileName(location)+"="+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant());
            }
            Reverse(harmony, "GetUnitsInRangeNonAlloc", nameof(OriginalUnits));
            Reverse(harmony, "GetWrecksInRangeNonAlloc", nameof(OriginalWrecks));
            harmony.CreateReversePatcher(AccessTools.Method(typeof(WreckCollector), "FindWreck"), new HarmonyMethod(typeof(RuntimeTests), nameof(OriginalFindWreck))).Patch();
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(BattlefieldGrid), "GetUnitsInRangeNonAlloc")).Owners.Contains("agent.noperf.spatial"), "Spatial patch active");
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(WreckCollector), "FindWreck")).Owners.Contains("agent.noperf.wrecks"), "Wreck patch active");
            GridParity();
            SpatialEdgeCases();
            SnapshotParity();
            CollectorParity();
            WreckEdgeCases();
            NavigationParity(harmony);
            RngParity();
            Benchmark();
            if (Environment.GetEnvironmentVariable("NOPERF_EMPTY_EXPERIMENT") == "1") EmptyWreckExperiment(harmony);
            Write("PASS assertions=" + assertions);
        }
        catch (Exception ex) { Write("FAIL " + ex); }
        finally
        {
            BattlefieldGrid.gridLookup = oldGrid;
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, oldValues[i]);
            // Clear scratch references; do not retain the disposable objects.
            ((List<GridSquare>)AccessTools.Field(typeof(BattlefieldGrid), "gridCache").GetValue(null)).Clear();
            ((List<Unit>)AccessTools.Field(typeof(BattlefieldGrid), "inRangeUnitsCache").GetValue(null)).Clear();
            ((List<Wreckage>)AccessTools.Field(typeof(BattlefieldGrid), "inRangeWrecksCache").GetValue(null)).Clear();
            foreach (var obj in objects) Object.Destroy(obj);
            var dir = Path.Combine(Paths.BepInExRootPath, "data", "noperf"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "runtime-tests.txt"), report.ToString());
            harmony.UnpatchSelf();
        }
    }
    private void Reverse(Harmony h, string method, string standin) => h.CreateReversePatcher(AccessTools.Method(typeof(BattlefieldGrid), method), new HarmonyMethod(typeof(RuntimeTests), standin)).Patch();
    [MethodImpl(MethodImplOptions.NoInlining)] private static void OriginalUnits(GlobalPosition pos, float range, List<Unit> output) => throw new NotImplementedException();
    [MethodImpl(MethodImplOptions.NoInlining)] private static void OriginalWrecks(GlobalPosition pos, float range, List<Wreckage> output) => throw new NotImplementedException();
    [MethodImpl(MethodImplOptions.NoInlining)] private static void OriginalFindWreck(WreckCollector owner) => throw new NotImplementedException();
    private T Component<T>() where T : Component
    {
        var obj = new GameObject("NOPerf disposable test"); obj.SetActive(false); objects.Add(obj);
        if (typeof(T) == typeof(Transform)) return (T)(Component)obj.transform;
        return obj.AddComponent<T>(); // Inactive: no Unit Awake or Wreckage Start.
    }
    private void Check(bool value, string label)
    {
        assertions++; if (!value) throw new InvalidOperationException(label);
    }
    private void Same<T>(List<T> a, List<T> b, string label) where T : class
    {
        Check(a.Count == b.Count, label + " count");
        for (int i = 0; i < a.Count; i++) Check(ReferenceEquals(a[i], b[i]), label + " order " + i);
    }
    private void Write(string line) { Logger.LogInfo(line); report.AppendLine(line); }
    private void Populate(int divisions)
    {
        BattlefieldGrid.GenerateGrid(divisions * 1000f, 1000f);
        var u = Enumerable.Range(0, 9).Select(_ => Component<Unit>()).ToArray();
        var w = Enumerable.Range(0, 9).Select(_ => Component<Wreckage>()).ToArray();
        var nonWreck = Component<Transform>();
        for (int i = 0; i < BattlefieldGrid.gridLookup.Length; i++)
        {
            var square = BattlefieldGrid.gridLookup[i];
            for (int j = 0; j < i % 7; j++) square.units.Add(u[(i + j) % u.Length]);
            if (i % 11 == 0) square.units.Add(null!);
            square.obstacles.Add(new Obstacle(nonWreck, 1, 1));
            if (i % 2 == 0) square.obstacles.Add(new Obstacle(w[i % w.Length].transform, 1, 1));
            if (i % 3 == 0) square.obstacles.Add(new Obstacle(w[(i + 1) % w.Length].transform, 1, 1));
        }
    }
    private void GridParity()
    {
        var random = new System.Random(71237);
        var a = new List<Unit>(); var b = new List<Unit>(); var c = new List<Wreckage>(); var d = new List<Wreckage>();
        foreach (int divisions in new[] { 1, 2, 7, 32 })
        {
            Populate(divisions);
            for (int i = 0; i < 1000; i++)
            {
                var p = new GlobalPosition((float)(random.NextDouble()-.5)*divisions*4000, 0, (float)(random.NextDouble()-.5)*divisions*4000);
                float r = i % 12 == 0 ? -1000 : i % 12 == 1 ? 0 : (float)random.NextDouble()*divisions*1200;
                OriginalUnits(p,r,a); BattlefieldGrid.GetUnitsInRangeNonAlloc(p,r,b); Same(a,b,"units");
                OriginalWrecks(p,r,c); BattlefieldGrid.GetWrecksInRangeNonAlloc(p,r,d); Same(c,d,"wrecks");
            }
            foreach (float x in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -divisions*500f, 0, divisions*500f })
            foreach (float z in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -divisions*500f, 0, divisions*500f })
            foreach (float r in new[] { 0f, .001f, 999.99f, 1000f, 1000.01f, divisions*1000f, -1000f })
            {
                var p = new GlobalPosition(x, float.NaN, z);
                OriginalUnits(p,r,a); BattlefieldGrid.GetUnitsInRangeNonAlloc(p,r,b); Same(a,b,"edge units");
                OriginalWrecks(p,r,c); BattlefieldGrid.GetWrecksInRangeNonAlloc(p,r,d); Same(c,d,"edge wrecks");
            }
        }
        BattlefieldGrid.Clear();
        OriginalUnits(default, 10, a); BattlefieldGrid.GetUnitsInRangeNonAlloc(default, 10, b); Same(a,b,"cleared grid");
        Write("Grid differential cases=5008 unit queries + 5008 wreck queries; identities, duplicates, nulls and order matched.");
    }
    private void SnapshotParity()
    {
        var owner = Component<WreckCollector>();
        var source = new List<Wreckage> { Component<Wreckage>(), Component<Wreckage>(), null! };
        var begin = AccessTools.Method(typeof(WrecksPlugin), "Begin"); var end = AccessTools.Method(typeof(WrecksPlugin), "End");
        object?[] args = { owner, null }; begin.Invoke(null,args); var state = args[1];
        try
        {
            var first = WrecksPlugin.Snapshot(source,owner); Same(source,first,"snapshot");
            source.Clear(); Check(first.Count == 3,"snapshot independent of shared source");
            begin.Invoke(null,args);
            var inner = WrecksPlugin.Snapshot(source,owner); Check(!ReferenceEquals(first,inner),"reentrant snapshot isolation");
            end.Invoke(null,new[]{args[1]}); Check(first.Count == 3,"nested cleanup preserves outer snapshot");
        }
        finally { end.Invoke(null,new[]{state}); }
        object?[] again = { owner, null }; begin.Invoke(null,again);
        try { Check(WrecksPlugin.Snapshot(source,owner).Count == 0,"no stale items after cleanup"); }
        finally { end.Invoke(null,new[]{again[1]}); }
        Write("Snapshot parity: independent, reentrant, and cleared between calls.");
    }
    private void Benchmark()
    {
        Populate(32);
        var units = new List<Unit>(5000); var wrecks = new List<Wreckage>(5000);
        foreach (float range in new[] { 1000f, 8000f, 32000f })
        {
            Measure("units original range="+range, () => OriginalUnits(default,range,units), 10000);
            Measure("units patched range="+range, () => BattlefieldGrid.GetUnitsInRangeNonAlloc(default,range,units), 10000);
            Measure("wrecks original range="+range, () => OriginalWrecks(default,range,wrecks), 1000);
            Measure("wrecks patched range="+range, () => BattlefieldGrid.GetWrecksInRangeNonAlloc(default,range,wrecks), 1000);
        }
        // Real FindWreck method on an empty grid, preserving its early return.
        BattlefieldGrid.GenerateGrid(32000f,1000f);
        var collector = Component<WreckCollector>();
        var target = AccessTools.Method(typeof(WreckCollector), "FindWreck");
        var current = (Action<WreckCollector>)Delegate.CreateDelegate(typeof(Action<WreckCollector>), target);
        Measure("FindWreck original empty", () => OriginalFindWreck(collector), 100000);
        Measure("FindWreck patched empty", () => current(collector), 100000);
        // Nonempty snapshots, without invoking any collector gameplay actions.
        var source = Enumerable.Range(0,128).Select(_=>Component<Wreckage>()).ToList();
        var begin = AccessTools.Method(typeof(WrecksPlugin),"Begin"); var end = AccessTools.Method(typeof(WrecksPlugin),"End");
        object?[] args = { collector, null }; begin.Invoke(null,args);
        try
        {
            Measure("snapshot original count=128",()=>source.ToList(),100000);
            Measure("snapshot reused count=128",()=>WrecksPlugin.Snapshot(source,collector),100000);
        }
        finally { end.Invoke(null,new[]{args[1]}); }
        // Include native transform access, candidate collection and patch scope
        // costs: helper-only savings cannot establish whole FindWreck benefit.
        BattlefieldGrid.GenerateGrid(32000f,1000f);
        AccessTools.Field(typeof(WreckCollector),"attachedUnit").SetValue(collector,Component<Unit>());
        for (int i=0;i<128;i++)
        {
            var wreck=source[i];wreck.transform.position=new Vector3(250+i,0,250);
            BattlefieldGrid.TryGetGridSquare(wreck.transform.GlobalPosition(),out var square);
            square.obstacles.Add(new Obstacle(wreck.transform,1,1));
        }
        Check(BattlefieldGrid.GetWrecksInRangeEnumerable(collector.transform.GlobalPosition(),1000).Count()==128,"collector benchmark candidate count");
        Measure("FindWreck original count=128",()=>OriginalFindWreck(collector),10000);
        Measure("FindWreck patched count=128",()=>current(collector),10000);
        Measure("FindWreck patched-repeat count=128",()=>current(collector),10000);
        Measure("FindWreck original-repeat count=128",()=>OriginalFindWreck(collector),10000);
    }
    private void CollectorParity()
    {
        BattlefieldGrid.GenerateGrid(32000f,1000f);
        var collector = Component<WreckCollector>();
        var attached = AccessTools.Field(typeof(WreckCollector),"attachedUnit");
        var range = AccessTools.Field(typeof(WreckCollector),"range");
        var current = AccessTools.Field(typeof(WreckCollector),"currentTarget");
        attached.SetValue(collector,Component<Unit>());
        var call = (Action<WreckCollector>)Delegate.CreateDelegate(typeof(Action<WreckCollector>),AccessTools.Method(typeof(WreckCollector),"FindWreck"));
        var random = new System.Random(9341);
        for(int trial=0;trial<200;trial++)
        {
            foreach(var square in BattlefieldGrid.gridLookup) square.obstacles.Clear();
            collector.transform.position=new Vector3((float)random.NextDouble()*100,0,(float)random.NextDouble()*100);
            range.SetValue(collector,(float)random.NextDouble()*100);
            for(int i=0;i<trial%31;i++)
            {
                var wreck=Component<Wreckage>();
                wreck.transform.position=new Vector3((float)random.NextDouble()*2500-1000,0,(float)random.NextDouble()*2500-1000);
                BattlefieldGrid.TryGetGridSquare(wreck.transform.GlobalPosition(),out var square);
                square.obstacles.Add(new Obstacle(wreck.transform,1,1));
            }
            current.SetValue(collector,null);
            collector.currentState=WreckCollector.CollectorState.Wait;
            OriginalFindWreck(collector);
            var originalTarget=current.GetValue(collector); var originalState=collector.currentState;
            current.SetValue(collector,null);
            collector.currentState=WreckCollector.CollectorState.Wait;
            call(collector);
            Check(ReferenceEquals(originalTarget,current.GetValue(collector)),"collector selected target parity");
            Check(originalState==collector.currentState,"collector state parity");
        }
        Write("Collector actual-method parity: 200 target selections/state comparisons.");
    }
    private void Measure(string label, Action action, int iterations)
    {
        for (int i=0;i<1000;i++) action();
        var allocMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);
        var allocated = allocMethod == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),allocMethod);
        if (allocated != null)
        {
            long calibration = allocated();
            var probe = new byte[4096]; GC.KeepAlive(probe);
            if (allocated() <= calibration) allocated = null; // Unity's Mono can expose a stub returning zero.
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int gc0 = GC.CollectionCount(0); long before = allocated?.Invoke() ?? 0;
        long start = Stopwatch.GetTimestamp();
        for(int i=0;i<iterations;i++) action();
        double ms = (Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
        long bytes = (allocated?.Invoke() ?? 0)-before;
        Write($"BENCH {label}: n={iterations} ns_per_call={ms*1000000/iterations:F1} bytes_per_call={(allocated==null?-1:(double)bytes/iterations):F1} gen0={GC.CollectionCount(0)-gc0}");
    }
}
