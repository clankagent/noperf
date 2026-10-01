using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace NOPerformance;

[BepInPlugin(Id, "NO Performance: Spatial Queries", "0.1.0")]
public sealed class SpatialPlugin : BaseUnityPlugin
{
    public const string Id = "agent.noperf.spatial";
    private Harmony? harmony;
    private static int mainThread;
    private void Awake()
    {
        if (!Config.Bind("General", "Enabled", true, "Fuse battlefield query traversal; restart to apply.").Value || !BuildGuard.Check(Logger)) return;
        mainThread = Thread.CurrentThread.ManagedThreadId;
        harmony = new Harmony(Id);
        try
        {
            Patch("GetUnitsInRangeNonAlloc", "Units", typeof(List<Unit>));
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            Logger.LogError("Spatial patches rolled back: " + ex);
        }
    }
    private void Patch(string name, string prefix, Type output)
    {
        var method = AccessTools.Method(typeof(BattlefieldGrid), name, new[] { typeof(GlobalPosition), typeof(float), output });
        if (method == null) throw new MissingMethodException(name);
        if (!BuildGuard.Unmodified(method, Logger)) return;
        harmony!.Patch(method, prefix: new HarmonyMethod(typeof(SpatialPlugin), prefix));
        Logger.LogInfo("Patched " + name);
    }
    private void OnDestroy() => harmony?.UnpatchSelf();

    // Keep the shipped cell rectangle exactly, including its exclusive upper
    // bounds. Changing coverage here would change sensor/weapon decisions.
    private static bool Bounds(GlobalPosition coord, float range, float mapSize, float gridSize, int divisions,
        out int x0, out int x1, out int z0, out int z1)
    {
        x0 = x1 = z0 = z1 = 0;
        if (float.IsNaN(coord.x) || float.IsInfinity(coord.x) || float.IsNaN(coord.z) || float.IsInfinity(coord.z)) return false;
        int x = (int)Mathf.Clamp((coord.x + mapSize * 0.5f) / gridSize, 0f, divisions - 1);
        int z = (int)Mathf.Clamp((coord.z + mapSize * 0.5f) / gridSize, 0f, divisions - 1);
        int radius = Mathf.CeilToInt(range * (float)divisions / mapSize);
        x0 = Mathf.Max(x - radius, 0); x1 = Mathf.Min(x + radius, divisions);
        z0 = Mathf.Max(z - radius, 0); z1 = Mathf.Min(z + radius, divisions);
        return true;
    }
    private static bool Units(GlobalPosition coord, float range, List<Unit> units,
        float ___mapSize, float ___gridSize, int ___divisions)
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThread || BattlefieldGrid.gridLookup == null || ___divisions <= 0) return true;
        units.Clear();
        if (!Bounds(coord, range, ___mapSize, ___gridSize, ___divisions, out int x0, out int x1, out int z0, out int z1)) return false;
        for (int x = x0; x < x1; x++)
        for (int z = z0; z < z1; z++)
        {
            var source = BattlefieldGrid.gridLookup[z * ___divisions + x].units;
            for (int i = source.Count - 1; i >= 0; i--) units.Add(source[i]);
        }
        return false;
    }
}
