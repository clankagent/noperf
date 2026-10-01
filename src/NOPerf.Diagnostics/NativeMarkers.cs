using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace NOPerformance;

internal sealed class NativeMarkers : IDisposable
{
    private sealed class Entry
    {
        internal string Name = "";
        internal ProfilerRecorder Recorder;
        internal double Sum;
        internal long Max;
    }
    private readonly List<Entry> entries = new();
    private readonly ManualLogSource log;
    private long frames;
    internal NativeMarkers(ManualLogSource log)
    {
        this.log = log;
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        var description = new StringBuilder();
        foreach (var handle in handles)
        {
            var info = ProfilerRecorderHandle.GetDescription(handle);
            description.AppendLine(info.Name + " unit=" + info.UnitType);
            if (info.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
            if (!(info.Name.Contains("Physics") || info.Name.Contains("JobManager") || info.Name.Contains("Job ") ||
                info.Name.StartsWith("GroundVehicle ") || info.Name == "GC.Collect" || info.Name.Contains("Schedule") || info.Name.Contains("Finish"))) continue;
            var recorder = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.StartImmediately |
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                ProfilerRecorderOptions.SumAllSamplesInFrame);
            if (recorder.Valid) entries.Add(new Entry { Name = info.Name, Recorder = recorder });
            else recorder.Dispose();
        }
        var path = Path.Combine(Paths.BepInExRootPath,"data","noperf"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path,"available-markers.txt"),description.ToString());
        log.LogInfo("Native marker recorders active="+entries.Count+". Main-thread inclusive time; instrumentation overhead applies.");
    }
    internal void Frame()
    {
        frames++;
        foreach (var entry in entries)
        {
            long value = entry.Recorder.LastValue;
            entry.Sum += value; if (value>entry.Max) entry.Max = value;
        }
    }
    internal void Report()
    {
        if (frames == 0) return;
        foreach (var entry in entries.OrderByDescending(e=>e.Sum))
        {
            if(entry.Sum>0) log.LogInfo($"NATIVE {entry.Name} frames={frames} mean_ms={entry.Sum/frames/1000000:F4} max_ms={entry.Max/1000000.0:F3}");
            entry.Sum=0; entry.Max=0;
        }
        frames=0;
    }
    public void Dispose() { foreach(var entry in entries) entry.Recorder.Dispose(); entries.Clear(); }
}
