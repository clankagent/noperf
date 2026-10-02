using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace NOPerformance;

// Read-only inventory for disposable profiling runs, never a memory optimizer.
// Typed queries run on separate frames. Their native enumeration calls cannot
// be preempted, and Unity reports can overlap or omit native allocations.
internal sealed class AssetMemoryScanner : IDisposable
{
    private sealed class Group { internal long Count, Bytes, ZeroReports; }
    private static readonly Type[] Types = {
        typeof(MeshCollider), typeof(SkinnedMeshRenderer), typeof(MeshFilter),
        typeof(Mesh), typeof(Texture), typeof(AudioClip), typeof(TerrainData),
        typeof(AnimationClip), typeof(Material), typeof(Shader), typeof(ComputeShader)
    };
    private readonly ManualLogSource log;
    private readonly double interval;
    private readonly Dictionary<string, Group> groups = new();
    private readonly HashSet<int> colliderMeshes = new(), skinnedMeshes = new(), filterMeshes = new();
    private Object[]? objects;
    private int index, scan, stage = -1;
    private double next = 120, started;
    private long workTicks, maxSliceTicks, maxItemTicks, skipped, failures, discovered;
    private long readable, readableBytes, unreadableBytes, colliderCount, colliderBytes;
    private long readableColliderCount, readableColliderBytes, skinnedCount, skinnedBytes;
    private long filterCount, filterBytes, unreferencedCount, unreferencedBytes;

    internal AssetMemoryScanner(ManualLogSource log, double interval)
    {
        this.log = log; this.interval = interval;
        log.LogInfo("Asset memory inventory enabled: typed asset and mesh-reference queries on separate frames. Read-only reports may overlap or be zero/unavailable; probes and temporary references add overhead. Not total RSS or reclaimable memory.");
    }
    internal void Update()
    {
        if (stage < 0)
        {
            if (Time.realtimeSinceStartupAsDouble < next || GameManager.gameState != GameState.Multiplayer) return;
            stage = 0; scan++; started = Time.realtimeSinceStartupAsDouble;
            workTicks = maxSliceTicks = maxItemTicks = skipped = failures = discovered = 0;
            readable = readableBytes = unreadableBytes = colliderCount = colliderBytes = 0;
            readableColliderCount = readableColliderBytes = skinnedCount = skinnedBytes = 0;
            filterCount = filterBytes = unreferencedCount = unreferencedBytes = 0;
            groups.Clear(); colliderMeshes.Clear(); skinnedMeshes.Clear(); filterMeshes.Clear();
            log.LogInfo($"ASSET_MEMORY_START scan={scan} realtime_seconds={started:F2} scope=typed_assets_and_mesh_references");
        }
        if (objects == null)
        {
            if (stage == Types.Length) { Finish(); return; }
            long begin = Stopwatch.GetTimestamp();
            try { objects = Resources.FindObjectsOfTypeAll(Types[stage]); }
            catch (Exception ex)
            {
                failures++; stage++;
                log.LogWarning("Asset query unavailable: " + ex.GetType().Name);
                RecordSlice(begin); return;
            }
            discovered += objects.Length; index = 0;
            long ticks = Stopwatch.GetTimestamp() - begin;
            workTicks += ticks; maxSliceTicks = Math.Max(maxSliceTicks, ticks);
            log.LogInfo($"ASSET_MEMORY_QUERY scan={scan} type={Types[stage].Name} count={objects.Length} enumeration_ms={ticks*1000.0/Stopwatch.Frequency:F3}");
            return;
        }
        long sliceStart = Stopwatch.GetTimestamp();
        int processed = 0;
        // The budget bounds work between native calls, never individual calls.
        while (index < objects.Length && processed++ < 64)
        {
            Object obj = objects[index++];
            if (!obj) { skipped++; continue; }
            long itemStart = Stopwatch.GetTimestamp();
            try
            {
                if (obj is MeshCollider collider) Remember(collider.sharedMesh, colliderMeshes);
                else if (obj is SkinnedMeshRenderer skinned) Remember(skinned.sharedMesh, skinnedMeshes);
                else if (obj is MeshFilter filter) Remember(filter.sharedMesh, filterMeshes);
                else
                {
                    long bytes = Math.Max(0, Profiler.GetRuntimeMemorySizeLong(obj));
                    string category = Category(obj);
                    if (!groups.TryGetValue(category, out var group)) groups[category] = group = new Group();
                    group.Count++; group.Bytes += bytes;
                    if (bytes == 0) group.ZeroReports++;
                    if (obj is Mesh mesh) ObserveMesh(mesh, bytes);
                }
            }
            catch (Exception) { failures++; }
            maxItemTicks = Math.Max(maxItemTicks, Stopwatch.GetTimestamp() - itemStart);
            if (Stopwatch.GetTimestamp() - sliceStart >= Stopwatch.Frequency / 1000) break;
        }
        RecordSlice(sliceStart);
        if (index == objects.Length) { objects = null; stage++; }
    }
    private static void Remember(Mesh mesh, HashSet<int> references)
    {
        if (mesh) references.Add(mesh.GetInstanceID());
    }
    private void ObserveMesh(Mesh mesh, long bytes)
    {
        int id = mesh.GetInstanceID(); bool canRead = mesh.isReadable;
        if (canRead) { readable++; readableBytes += bytes; } else unreadableBytes += bytes;
        if (colliderMeshes.Contains(id))
        {
            colliderCount++; colliderBytes += bytes;
            if (canRead) { readableColliderCount++; readableColliderBytes += bytes; }
        }
        if (skinnedMeshes.Contains(id)) { skinnedCount++; skinnedBytes += bytes; }
        if (filterMeshes.Contains(id)) { filterCount++; filterBytes += bytes; }
        if (!colliderMeshes.Contains(id) && !skinnedMeshes.Contains(id) && !filterMeshes.Contains(id))
        { unreferencedCount++; unreferencedBytes += bytes; }
    }
    private void RecordSlice(long begin)
    {
        long ticks = Stopwatch.GetTimestamp() - begin;
        workTicks += ticks; maxSliceTicks = Math.Max(maxSliceTicks, ticks);
    }
    private void Finish()
    {
        foreach (var pair in groups.OrderByDescending(p => p.Value.Bytes))
            log.LogInfo($"ASSET_MEMORY scan={scan} group={pair.Key} count={pair.Value.Count} unity_reported_bytes={pair.Value.Bytes} zero_reports={pair.Value.ZeroReports}");
        // Sets were observed before meshes, at different moments. Categories
        // overlap and missing references do NOT imply an asset is unused: game
        // fields, native systems and future prefab spawns may still need it.
        log.LogInfo($"ASSET_MESH_REFERENCES scan={scan} readable_count={readable} readable_reported_bytes={readableBytes} unreadable_reported_bytes={unreadableBytes} collider_count={colliderCount} collider_reported_bytes={colliderBytes} readable_collider_count={readableColliderCount} readable_collider_reported_bytes={readableColliderBytes} skinned_count={skinnedCount} skinned_reported_bytes={skinnedBytes} filter_count={filterCount} filter_reported_bytes={filterBytes} absent_from_observed_reference_sets_count={unreferencedCount} absent_from_observed_reference_sets_reported_bytes={unreferencedBytes}");
        double scale = 1000.0 / Stopwatch.Frequency;
        log.LogInfo($"ASSET_MEMORY_END scan={scan} discovered={discovered} elapsed_seconds={Time.realtimeSinceStartupAsDouble-started:F3} work_ms={workTicks*scale:F3} max_slice_ms={maxSliceTicks*scale:F3} max_item_ms={maxItemTicks*scale:F3} destroyed_skipped={skipped} lookup_failures={failures} heap_bytes={GC.GetTotalMemory(false)} units={UnitRegistry.allUnits.Count}");
        objects = null; stage = -1; groups.Clear(); colliderMeshes.Clear(); skinnedMeshes.Clear(); filterMeshes.Clear();
        next = Time.realtimeSinceStartupAsDouble + interval;
    }
    private static string Category(Object obj) => obj switch
    {
        Mesh => "meshes", Texture => "textures", AudioClip => "audio_clips",
        TerrainData => "terrain_data", AnimationClip => "animation_clips",
        Material => "materials", Shader => "shaders", ComputeShader => "compute_shaders", _ => "other_assets"
    };
    public void Dispose()
    {
        objects = null; stage = -1; groups.Clear(); colliderMeshes.Clear(); skinnedMeshes.Clear(); filterMeshes.Clear();
    }
}
