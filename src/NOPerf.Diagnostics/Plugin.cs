using System;
using BepInEx;
using NuclearOption.Debugging;
using UnityEngine;

namespace NOPerformance;

[BepInPlugin("agent.noperf.diagnostics", "NO Performance: Diagnostics", "0.1.0")]
[BepInDependency("agent.noperf.spatial", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("agent.noperf.wrecks", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("agent.noperf.navigation", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class DiagnosticsPlugin : BaseUnityPlugin
{
    private bool subscribed;
    private bool runtimeSettled;
    private double next;
    private double sum, fixedSum, receiveSum, sendSum;
    private float max;
    private long count;
    private float interval;
    private MethodProfiler? profiler;
    private bool requestProfiler;
    private bool requestMissionProfiler;
    private bool requestJobProfiler;
    private bool requestNetworkProfiler;
    private bool requestAssetMemory;
    private bool requestMemoryCounters;
    private double nextMemoryCounters;
    private float assetMemoryInterval;
    private AssetMemoryScanner? assetMemory;
    private bool requestNative;
    private NativeMarkers? native;
    private bool requestPhases;
    private PhaseProfiler? phases;
    private int gc0, gc1, gc2;
    private readonly int[] histogram = new int[8];
    private static readonly float[] bounds = { 1f, 2f, 4f, 8f, 16f, 33f, 66f, float.PositiveInfinity };
    private void Awake()
    {
        if (!Config.Bind("General", "Enabled", false, "Log aggregate timings from the game's existing tracker; adds observation overhead.").Value || !BuildGuard.Check(Logger)) return;
        interval = Mathf.Clamp(Config.Bind("General", "IntervalSeconds", 30f, "Aggregation interval, 5-300 seconds.").Value, 5f, 300f);
        PlayerLoopPerformanceTracker.OnFrameFinished += Frame;
        subscribed = true;
        next = Time.realtimeSinceStartupAsDouble + interval;
        gc0=GC.CollectionCount(0);gc1=GC.CollectionCount(1);gc2=GC.CollectionCount(2);
        requestProfiler = Config.Bind("Profiling", "Methods", false, "Instrument selected managed methods, sampling 1/64 calls. Adds overhead; restart to apply.").Value;
        requestMissionProfiler = Config.Bind("Profiling", "MissionMethods", false, "Instrument selected mission objective methods, sampling 1/64 calls. Adds overhead; restart to apply.").Value;
        requestJobProfiler = Config.Bind("Profiling", "JobMethods", false, "Instrument job scheduling, input collection and result application separately. Inclusive main-thread timings include waits; sampling adds overhead. Restart to apply.").Value;
        requestNetworkProfiler = Config.Bind("Profiling", "NetworkMethods", false, "Instrument transform eligibility, serialization, batching and transport separately. Inclusive main-thread timings overlap and sampling adds overhead. Restart to apply.").Value;
        requestAssetMemory = Config.Bind("Profiling", "AssetMemory", false, "Read-only typed asset memory and mesh-reference inventory in main-thread slices. Enumeration calls cannot be sliced; reports may overlap or be unavailable. Adds overhead, not total RSS or reclaimable memory. Restart to apply.").Value;
        requestMemoryCounters = Config.Bind("Profiling", "MemoryCounters", false, "Read scalar GC/Unity/Mono memory counters after 120 seconds, at aggregation boundaries at least 30 seconds apart. Overlapping scopes, not RSS or reclaimable memory; zero reports unavailable. Adds observation overhead; restart to apply.").Value;
        nextMemoryCounters = Time.realtimeSinceStartupAsDouble + 120;
        assetMemoryInterval = Mathf.Clamp(Config.Bind("Profiling", "AssetMemoryIntervalSeconds", 120f, "Seconds between completed asset inventories, 30-600; first scan starts after 120 seconds. Restart to apply.").Value, 30f, 600f);
        requestNative = Config.Bind("Profiling", "NativeMarkers", false, "Record available Unity main-thread physics/job markers. Adds overhead; restart to apply.").Value;
        requestPhases = Config.Bind("Profiling", "PlayerLoopPhases", false, "Observe native physics and script phase timings through adjacent player-loop probes; restart to apply.").Value;
        Logger.LogInfo("Diagnostics observing existing game frame timings; frame/AI/physics rates unchanged.");
        Logger.LogInfo($"Runtime workers={Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount} worker_max={Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerMaximumCount} logical_cpus={SystemInfo.processorCount} fixed_delta_seconds={Time.fixedDeltaTime}");
        Logger.LogInfo($"Physics reuse_collision_callbacks={Physics.reuseCollisionCallbacks} auto_sync_transforms={Physics.autoSyncTransforms}");
    }
    private void Frame(NuclearOption.Debugging.FrameTiming timing)
    {
        float total = timing.UpdateTime + timing.FixedUpdateTime + timing.LateUpdateTime;
        count++; sum += total; fixedSum += timing.FixedUpdateTime;
        receiveSum += timing.ReceiveTime; sendSum += timing.SendTime;
        max = Mathf.Max(max, total);
        native?.Frame();
        for (int i = 0; i < bounds.Length; i++) if (total <= bounds[i]) { histogram[i]++; break; }
    }
    private void Update()
    {
        if (requestAssetMemory)
        {
            requestAssetMemory = false;
            assetMemory = new AssetMemoryScanner(Logger, assetMemoryInterval);
        }
        assetMemory?.Update();
        if (subscribed && !runtimeSettled && Time.realtimeSinceStartupAsDouble > 10)
        {
            runtimeSettled = true;
            Logger.LogInfo($"Runtime settled workers={Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount} worker_max={Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerMaximumCount} logical_cpus={SystemInfo.processorCount} fixed_delta_seconds={Time.fixedDeltaTime}");
            Logger.LogInfo($"Physics settled reuse_collision_callbacks={Physics.reuseCollisionCallbacks} auto_sync_transforms={Physics.autoSyncTransforms}");
        }
        if ((requestProfiler || requestMissionProfiler || requestJobProfiler || requestNetworkProfiler) && Time.realtimeSinceStartupAsDouble > 5)
        {
            bool generalMethods = requestProfiler, missionMethods = requestMissionProfiler, jobMethods = requestJobProfiler, networkMethods = requestNetworkProfiler;
            requestProfiler = requestMissionProfiler = requestJobProfiler = requestNetworkProfiler = false;
            try { profiler = new MethodProfiler(Logger, generalMethods, missionMethods, jobMethods, networkMethods); }
            catch (Exception ex) { new HarmonyLib.Harmony("agent.noperf.diagnostics.methods").UnpatchSelf(); Logger.LogError("Method profiler disabled: " + ex); }
        }
        if (requestNative && Time.realtimeSinceStartupAsDouble > 20)
        {
            requestNative = false;
            try { native = new NativeMarkers(Logger); }
            catch(Exception ex) { native?.Dispose(); native=null; Logger.LogWarning("Native markers unavailable: "+ex.Message); }
        }
        if (requestPhases && Time.realtimeSinceStartupAsDouble > 20)
        {
            requestPhases=false;
            try {phases=new PhaseProfiler(Logger);}
            catch(Exception ex){Logger.LogWarning("Phase profiling unavailable: "+ex.Message);}
        }
        if (!subscribed || Time.realtimeSinceStartupAsDouble < next) return;
        next = Time.realtimeSinceStartupAsDouble + interval;
        if (requestMemoryCounters && Time.realtimeSinceStartupAsDouble >= nextMemoryCounters)
        {
            MemoryCounters.Report(Logger, Time.realtimeSinceStartupAsDouble);
            nextMemoryCounters = Time.realtimeSinceStartupAsDouble + 30;
        }
        profiler?.Report();
        native?.Report();
        phases?.Report();
        if (count == 0) { Logger.LogInfo("Diagnostics: no game timing events yet."); return; }
        long cumulative = 0; float p95 = 0;
        for (int i = 0; i < histogram.Length; i++) { cumulative += histogram[i]; if (cumulative >= Math.Ceiling(count * .95)) { p95 = bounds[i]; break; } }
        int now0=GC.CollectionCount(0),now1=GC.CollectionCount(1),now2=GC.CollectionCount(2);
        Logger.LogInfo($"frames={count} loop_mean_ms={sum/count:F3} loop_max_ms={max:F3} loop_p95_bucket_ms={p95} fixed_mean_ms={fixedSum/count:F3} receive_mean_ms={receiveSum/count:F3} send_mean_ms={sendSum/count:F3} units={UnitRegistry.allUnits.Count} heap_bytes={GC.GetTotalMemory(false)} gc0={now0-gc0} gc1={now1-gc1} gc2={now2-gc2}");
        gc0=now0;gc1=now1;gc2=now2;
        count = 0; sum = fixedSum = receiveSum = sendSum = 0; max = 0;
        Array.Clear(histogram, 0, histogram.Length);
    }
    private void OnDestroy()
    {
        if (subscribed) PlayerLoopPerformanceTracker.OnFrameFinished -= Frame;
        subscribed = false;
        profiler?.Dispose();
        native?.Dispose();
        phases?.Dispose();
        assetMemory?.Dispose();
    }
}
