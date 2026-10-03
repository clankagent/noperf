using System;
using NOPerformance;

// Test doubles exercise output semantics; they do not establish Unity API availability.
namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public string Last = "";
        public void LogInfo(string value) => Last = value;
    }
}

namespace UnityEngine.Profiling
{
    public static class Profiler
    {
        public static long GetTotalAllocatedMemoryLong() => 123;
        public static long GetTotalReservedMemoryLong() => 0;
        public static long GetTotalUnusedReservedMemoryLong() => throw new NotSupportedException();
        public static long GetMonoHeapSizeLong() => 456;
        public static long GetMonoUsedSizeLong() => 789;
    }
}

namespace NOPerformance
{
    internal static class BuildGuard
    {
        internal const string GameHash = "TEST_ONLY";
    }
}

public static class Program
{
    private static void Require(bool value, string reason)
    {
        if (!value) throw new Exception(reason);
    }

    public static void Main()
    {
        Require(MemoryCounters.Read("x", () => 0) == "x_bytes=UNKNOWN x_availability=unavailable_nonpositive", "zero is unavailable");
        Require(MemoryCounters.Read("x", () => -1).Contains("UNKNOWN"), "negative is unavailable");
        Require(MemoryCounters.Read("x", () => throw new MissingMethodException()).Contains("error_MissingMethodException"), "unsupported API is explicit");
        Require(MemoryCounters.Read("x", () => long.MaxValue).Contains(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)), "64-bit value retained");
        var log = new BepInEx.Logging.ManualLogSource();
        MemoryCounters.Report(log, 120);
        Require(log.Last.Contains("unity_allocator_reserved_bytes=UNKNOWN") && log.Last.Contains("mono_used_bytes=789"), "one unavailable scope does not erase other scopes");
        Require(log.Last.Contains("scopes_overlap=true rss=EXTERNAL retained_class_budget=UNKNOWN") && !log.Last.Contains("total_budget"), "output is nonadditive");
        Require(log.Last.Contains("read_body_ms=") && log.Last.Contains("utc=") && log.Last.Contains("batch_mode_verified=true dedicated_server_identity=EXTERNAL_FIXTURE") && !log.Last.Contains("batch_server=true"), "context retained without claiming dedicated-server identity");
        Console.WriteLine("7 actual-source output semantics checks PASS; Unity test doubles only, no runtime activation");
    }
}
