using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;

namespace NOPerformance;

internal sealed class MethodProfiler : IDisposable
{
    internal sealed class Stat
    {
        internal string Label = "";
        internal int Stride = 64;
        internal long Calls, Samples, Ticks, Max;
    }
    internal struct Sample { internal Stat? Stat; internal long Start; }
    private static readonly Dictionary<MethodBase, Stat> stats = new();
    private static int mainThread;
    private readonly Harmony harmony = new("agent.noperf.diagnostics.methods");
    private readonly ManualLogSource log;
    internal MethodProfiler(ManualLogSource log, bool generalMethods, bool missionMethods)
    {
        this.log = log;
        mainThread = Thread.CurrentThread.ManagedThreadId;
        var targets = new List<(Type, string)>();
        if (generalMethods) targets.AddRange(new[] {
            (typeof(BattlefieldGrid), "GetUnitsInRangeNonAlloc"),
            (typeof(TargetDetector), "VisualCheck"), (typeof(Radar), "RadarCheck"),
            (typeof(NuclearOption.Jobs.JobManager), "FixedUpdateEarly"),
            (typeof(NuclearOption.Jobs.JobManager), "FinishJobs"),
            (typeof(Pilot), "Update"), (typeof(FactionHQ), "Update"),
            (typeof(ThreatTracker), "CheckThreats"),
            (typeof(CombatAI), "GetSafeStandoffDist"),
            (typeof(CombatAI), "LookForMissileTargets"),
            (typeof(RoadPathfinder), "TryPathfind"),
            (typeof(RoadPathfinding.RoadNetwork), "TryGetNearestRoad"),
            (typeof(PathfindingAgent), "GetSteerpoint"),
            (typeof(WreckCollector), "FindWreck"),
            (typeof(MissionManager), "Update"),
            (typeof(NuclearOption.NetworkTransforms.SendTransformBatcher), "LateUpdate") });
        if (missionMethods) targets.AddRange(new[] {
            (typeof(MissionRunner), "Update"),
            (typeof(NuclearOption.SavedMission.Objectives.DestroyUnitObjective), "CheckComplete"),
            (typeof(NuclearOption.SavedMission.Objectives.CaptureAirbaseObjective), "CheckComplete"),
            (typeof(NuclearOption.SavedMission.Objectives.ReachWaypointsObjective), "CheckComplete"),
            (typeof(NuclearOption.SavedMission.Objectives.ReachUnitsObjective), "UpdateAndCheck"),
            (typeof(NuclearOption.SavedMission.Objectives.SuccessfulSortieObjective), "UpdateAndCheck"),
            (typeof(NuclearOption.SavedMission.Objectives.CrashAircraftObjective), "UpdateAndCheck"),
            (typeof(NuclearOption.SavedMission.Objectives.NoObjective), "UpdateAndCheck") });
        foreach (var target in targets)
        {
            var method = AccessTools.Method(target.Item1, target.Item2);
            if (method == null) { log.LogWarning("Profile target unavailable: " + target); continue; }
            stats[method] = new Stat { Label = target.Item1.Name + "." + target.Item2, Stride = target.Item2 == "TryPathfind" ? 1 : 64 };
            log.LogInfo("Installing sampled profile: " + target.Item1.Name + "." + target.Item2);
            File.AppendAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, "method-profiler-startup.txt"), target.Item1.Name + "." + target.Item2 + Environment.NewLine);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(MethodProfiler), nameof(Begin)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(MethodProfiler), nameof(End)) { priority = Priority.Last });
        }
        log.LogInfo("Inclusive method profiler enabled: rare TryPathfind calls fully timed, other methods sampled 1/64; instrumentation has overhead.");
    }
    private static void Begin(MethodBase __originalMethod, out Sample __state)
    {
        __state = default;
        if (Thread.CurrentThread.ManagedThreadId != mainThread) return;
        var stat = stats[__originalMethod];
        if (++stat.Calls % stat.Stride != 0) return;
        __state.Stat = stat; __state.Start = Stopwatch.GetTimestamp();
    }
    private static void End(Sample __state)
    {
        if (__state.Stat == null) return;
        long ticks = Stopwatch.GetTimestamp() - __state.Start;
        __state.Stat.Samples++; __state.Stat.Ticks += ticks;
        if (ticks > __state.Stat.Max) __state.Stat.Max = ticks;
    }
    internal void Report()
    {
        foreach (var stat in stats.Values.OrderByDescending(s => s.Samples == 0 ? 0 : (double)s.Ticks*s.Calls/s.Samples))
        {
            if (stat.Samples > 0)
            {
                double scale = 1000.0/Stopwatch.Frequency;
                log.LogInfo($"PROFILE {stat.Label} calls={stat.Calls} sampled={stat.Samples} sample_mean_ms={stat.Ticks*scale/stat.Samples:F5} sample_max_ms={stat.Max*scale:F3} estimated_inclusive_ms={stat.Ticks*scale*stat.Calls/stat.Samples:F1}");
            }
            else if (stat.Calls > 0) log.LogInfo($"PROFILE {stat.Label} calls={stat.Calls} sampled=0 timing=unavailable");
            stat.Calls = stat.Samples = stat.Ticks = stat.Max = 0;
        }
    }
    public void Dispose() { harmony.UnpatchSelf(); stats.Clear(); }
}
