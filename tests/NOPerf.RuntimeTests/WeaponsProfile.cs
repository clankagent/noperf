using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace NOPerformance;

// Disposable lab instrumentation only. No gameplay replacement or production DLL change.
public sealed partial class RuntimeTests
{
    private sealed class WeaponStat
    {
        internal string Label = "";
        internal int Stride;
        internal long Calls, Samples, Ticks, Max;
    }
    private struct WeaponSample { internal WeaponStat? Stat; internal long Start; }
    private static readonly Dictionary<MethodBase, WeaponStat> weaponStats = new();
    private static int weaponsThread;
    private Harmony? weaponsHarmony;
    private double weaponsLastReport;
    private void StartWeaponsProfile()
    {
        weaponsThread = Thread.CurrentThread.ManagedThreadId;
        weaponsHarmony = new Harmony("agent.noperf.tests.weapons");
        var targets = new[] {
            (typeof(DamageEffects), "BlastFrag", 1),
            (typeof(DamageEffects), "ArmorPenetrate", 1),
            (typeof(DamageEffects), "FragTrace", 64),
            (typeof(BulletSim), "FixedUpdate", 64),
            (typeof(BulletSim.Bullet), "TrajectoryTrace", 64) };
        foreach (var target in targets)
        {
            var method = AccessTools.Method(target.Item1, target.Item2);
            if (method == null) throw new InvalidOperationException("Missing weapons profile target " + target);
            weaponStats[method] = new WeaponStat { Label = target.Item1.Name + "." + target.Item2, Stride = target.Item3 };
            weaponsHarmony.Patch(method,
                prefix: new HarmonyMethod(typeof(RuntimeTests), nameof(WeaponBegin)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(RuntimeTests), nameof(WeaponEnd)) { priority = Priority.Last });
            Logger.LogInfo("WEAPONS_INSTALLED " + weaponStats[method].Label);
        }
        weaponsLastReport = UnityEngine.Time.realtimeSinceStartupAsDouble;
        Logger.LogInfo("WEAPONS_PROFILE_READY targets=5 inclusive=true instrumentation_has_overhead=true");
    }
    private static void WeaponBegin(MethodBase __originalMethod, out WeaponSample __state)
    {
        __state = default;
        if (Thread.CurrentThread.ManagedThreadId != weaponsThread) return;
        var stat = weaponStats[__originalMethod];
        if (++stat.Calls % stat.Stride != 0) return;
        __state.Stat = stat;
        __state.Start = Stopwatch.GetTimestamp();
    }
    private static void WeaponEnd(WeaponSample __state)
    {
        if (__state.Stat == null) return;
        long ticks = Stopwatch.GetTimestamp() - __state.Start;
        __state.Stat.Samples++;
        __state.Stat.Ticks += ticks;
        if (ticks > __state.Stat.Max) __state.Stat.Max = ticks;
    }
    private void Update()
    {
        if (weaponsHarmony == null) return;
        double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (now - weaponsLastReport < 30) return;
        weaponsLastReport = now;
        foreach (var stat in weaponStats.Values)
        {
            double scale = 1000.0 / Stopwatch.Frequency;
            if (stat.Samples == 0)
                Logger.LogInfo($"WEAPONS {stat.Label} calls={stat.Calls} sampled=0 timing=unavailable");
            else
                Logger.LogInfo($"WEAPONS {stat.Label} calls={stat.Calls} sampled={stat.Samples} sample_mean_ms={stat.Ticks*scale/stat.Samples:F5} sample_max_ms={stat.Max*scale:F3} estimated_inclusive_ms={stat.Ticks*scale*stat.Calls/stat.Samples:F1}");
            stat.Calls = stat.Samples = stat.Ticks = stat.Max = 0;
        }
    }
    private void OnDestroy()
    {
        weaponsHarmony?.UnpatchSelf();
        weaponStats.Clear();
    }
}
