using System;
using System.Diagnostics;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine.Profiling;

namespace NOPerformance;

// Scalar observations overlap: never add these values into an RSS or heap budget.
internal static class MemoryCounters
{
    internal static string Read(string name, Func<long> getter)
    {
        try
        {
            long value = getter();
            return value > 0
                ? name + "_bytes=" + value.ToString(CultureInfo.InvariantCulture) + " " + name + "_availability=available"
                : name + "_bytes=UNKNOWN " + name + "_availability=unavailable_nonpositive";
        }
        catch (Exception ex)
        {
            return name + "_bytes=UNKNOWN " + name + "_availability=error_" + ex.GetType().Name;
        }
    }

    internal static void Report(ManualLogSource log, double uptime)
    {
        long start = Stopwatch.GetTimestamp();
        string utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        string values = Read("managed_gc", () => GC.GetTotalMemory(false)) + " "
            + Read("unity_allocator_allocated", Profiler.GetTotalAllocatedMemoryLong) + " "
            + Read("unity_allocator_reserved", Profiler.GetTotalReservedMemoryLong) + " "
            + Read("unity_allocator_unused_reserved", Profiler.GetTotalUnusedReservedMemoryLong) + " "
            + Read("mono_heap_capacity", Profiler.GetMonoHeapSizeLong) + " "
            + Read("mono_used", Profiler.GetMonoUsedSizeLong);
        double milliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        log.LogInfo("memory_scalars utc=" + utc + " uptime_seconds=" + uptime.ToString("F3", CultureInfo.InvariantCulture)
            + " read_body_ms=" + milliseconds.ToString("F6", CultureInfo.InvariantCulture)
            + " build_guard=verified batch_mode_verified=true dedicated_server_identity=EXTERNAL_FIXTURE assembly_sha256=" + BuildGuard.GameHash
            + " scopes_overlap=true rss=EXTERNAL retained_class_budget=UNKNOWN mono_used_includes_noncollected=true forced_gc=false " + values);
    }
}
