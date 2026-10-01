using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NOPerformance;

public sealed partial class RuntimeTests
{
    // Opt-in lab driver: only offline host APIs and read-only query comparisons.
    // Never installed or enabled in the normal production plugin package.
    private async UniTask MissionReloadTest(bool optimizersExpected)
    {
        var h = new Harmony("agent.noperf.tests.reload");
        int exit = 2;
        try
        {
            Check(BuildGuard.Check(Logger), "reload exact build guard");
            Reverse(h, "GetUnitsInRangeNonAlloc", nameof(OriginalUnits));
            foreach (var id in new[] {"agent.noperf.spatial", "agent.noperf.wrecks", "agent.noperf.navigation", "agent.noperf.diagnostics"})
            {
                var path = BepInEx.Bootstrap.Chainloader.PluginInfos[id].Instance.GetType().Assembly.Location;
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                Write("PLUGIN_SHA256 " + Path.GetFileName(path) + "=" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant());
            }
            var missions = new[] { "Terminal Control", "Escalation", "Terminal Control", "Escalation" };
            for (int phase = 0; phase < missions.Length; phase++)
            {
                if (phase > 0)
                {
                    RequireOfflineHost();
                    MissionGroup.Init();
                    if (!MissionSaveLoad.TryLoad(new MissionKey(missions[phase], MissionGroup.BuiltIn), out var mission, out var error))
                        throw new InvalidOperationException("reload mission input unavailable: " + error);
                    Write("RELOAD_BEGIN phase=" + phase + " mission=" + missions[phase]);
                    await NetworkManagerNuclearOption.i.StopAsync(false);
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    MissionManager.SetMission(mission, false);
                    await NetworkManagerNuclearOption.i.StartHostAsync(new HostOptions(SocketType.Offline, GameState.Multiplayer, mission.MapKey) { MaxConnections = 1 });
                }
                await WaitForMission(missions[phase]);
                RequireOfflineHost();
                Check(GameManager.IsHeadless, "reload is headless");
                Check(UnitRegistry.allUnits.Count > 0, "reload populated world");
                Check(MissionManager.Runner != null, "reload runner exists");
                CheckOwner(typeof(BattlefieldGrid), "GetUnitsInRangeNonAlloc", "agent.noperf.spatial", optimizersExpected);
                CheckOwner(typeof(WreckCollector), "FindWreck", "agent.noperf.wrecks", optimizersExpected);
                CheckOwner(typeof(RoadPathfinder), "TryPathfind", "agent.noperf.navigation", optimizersExpected);
                Write("RELOAD_READY phase=" + phase + " mission=" + missions[phase] + " units=" + UnitRegistry.allUnits.Count + " fixed_delta=" + Time.fixedDeltaTime.ToString("R"));
                var settled = Stopwatch.StartNew();
                int rounds = 0;
                while (settled.Elapsed.TotalSeconds < 120)
                {
                    RequireOfflineHost();
                    Check(MissionManager.IsRunning, "reload mission stays running");
                    LiveQueryParity();
                    rounds++;
                    // Realtime delay: do not change simulation time or random seed.
                    await UniTask.Delay(5000, ignoreTimeScale: true);
                }
                Write("RELOAD_SETTLED phase=" + phase + " rounds=" + rounds + " units=" + UnitRegistry.allUnits.Count + " heap_bytes=" + GC.GetTotalMemory(false));
            }
            Write("RELOAD_PASS transitions=3 phases=4 optimizers=" + optimizersExpected + " assertions=" + assertions);
            exit = 0;
        }
        catch (Exception ex) { Write("RELOAD_FAIL " + ex); }
        finally
        {
            h.UnpatchSelf();
            var dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "data", "noperf");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "mission-reload-tests.txt"), report.ToString());
            Application.Quit(exit);
        }
    }

    private static void RequireOfflineHost()
    {
        var manager = NetworkManagerNuclearOption.i;
        if (manager == null || !manager.Server.Active || manager.Server.Listening)
            throw new InvalidOperationException("reload refused: active offline host required");
        if (UnitRegistry.playerLookup.Count > 1)
            throw new InvalidOperationException("reload refused: another player present");
    }

    private static async UniTask WaitForMission(string name)
    {
        var timer = Stopwatch.StartNew();
        while (!MissionManager.IsRunning || MissionManager.CurrentMission?.Name != name || UnitRegistry.allUnits.Count == 0)
        {
            if (timer.Elapsed.TotalSeconds > 150) throw new TimeoutException("reload mission not ready: expected=" + name
                + " actual=" + MissionManager.CurrentMission?.Name + " running=" + MissionManager.IsRunning
                + " units=" + UnitRegistry.allUnits.Count);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
    }

    private void CheckOwner(Type type, string method, string owner, bool expected)
    {
        var patches = Harmony.GetPatchInfo(AccessTools.Method(type, method));
        Check((patches?.Owners.Contains(owner) ?? false) == expected, "reload patch state " + owner);
    }

    private void LiveQueryParity()
    {
        var originals = new List<Unit>();
        var patched = new List<Unit>();
        var units = UnitRegistry.allUnits.Take(8).ToArray();
        var before = UnityEngine.Random.state;
        float fixedDelta = Time.fixedDeltaTime;
        foreach (var unit in units)
        {
            if (unit == null) continue;
            var position = unit.GlobalPosition();
            foreach (float range in new[] { 0f, 1000f, 8000f })
            {
                OriginalUnits(position, range, originals);
                BattlefieldGrid.GetUnitsInRangeNonAlloc(position, range, patched);
                Same(originals, patched, "reload live candidates");
            }
        }
        Check(before.Equals(UnityEngine.Random.state), "reload query RNG parity");
        Check(fixedDelta == Time.fixedDeltaTime, "reload query fixed step unchanged");
    }
}
