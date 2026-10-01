using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using BepInEx;
using HarmonyLib;

namespace NOPerformance;

[BepInPlugin(Id, "NO Performance: Wreck Searches", "0.1.0")]
public sealed class WrecksPlugin : BaseUnityPlugin
{
    public const string Id = "agent.noperf.wrecks";
    private Harmony? harmony;
    private static int mainThread;
    private static readonly ConditionalWeakTable<WreckCollector, Cache> caches = new();
    private static readonly ConditionalWeakTable<WreckCollector, Cache>.CreateValueCallback create = _ => new Cache();
    private static readonly List<Wreckage> empty = new();
    private static Scope current;
    public sealed class Cache
    {
        internal readonly List<Wreckage> Items = new();
    }
    private struct Scope {internal WreckCollector? Owner;internal Cache? Cache;internal int Depth;}
    private struct ScopeState {internal Scope Previous;internal bool Entered;}
    private void Awake()
    {
        if (!Config.Bind("General", "Enabled", false, "Experimental snapshot pooling; workload-dependent benefit, disabled by default. Restart to apply.").Value || !BuildGuard.Check(Logger)) return;
        mainThread = Thread.CurrentThread.ManagedThreadId;
        var method = AccessTools.Method(typeof(WreckCollector), "FindWreck");
        if (method == null || !BuildGuard.Unmodified(method, Logger)) return;
        harmony = new Harmony(Id);
        try
        {
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(WrecksPlugin), "Begin"),
                transpiler: new HarmonyMethod(typeof(WrecksPlugin), "Transpile"), finalizer: new HarmonyMethod(typeof(WrecksPlugin), "End"));
            Logger.LogInfo("Patched WreckCollector.FindWreck snapshot allocation only.");
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            Logger.LogError("Wreck patch rolled back: " + ex);
        }
    }
    private void OnDestroy() => harmony?.UnpatchSelf();
    private static void Begin(WreckCollector __instance, out ScopeState __state)
    {
        __state = default;
        if (Thread.CurrentThread.ManagedThreadId != mainThread) return;
        __state.Previous=current;__state.Entered=true;
        current=new Scope {Owner=__instance,Depth=current.Depth+1};
    }
    private static void End(ScopeState __state)
    {
        if (!__state.Entered) return;
        var cache=current.Cache;
        if(cache!=null)
        {
            cache.Items.Clear(); // Release Unity references even when the original throws.
            if(cache.Items.Capacity>4096)cache.Items.Capacity=0;
        }
        current=__state.Previous;
    }
    public static List<Wreckage> Snapshot(IEnumerable<Wreckage> source, WreckCollector owner)
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThread) return source.ToList();
        if(current.Depth!=1 || !ReferenceEquals(current.Owner,owner))return source.ToList();
        // The verified FindWreck body returns immediately on an empty snapshot.
        // No per-owner table lookup or allocation is needed on that common path.
        if(source is ICollection<Wreckage> collection && collection.Count==0)
        {empty.Clear();return empty;}
        var cache=current.Cache ?? (current.Cache=caches.GetValue(owner,create));
        cache.Items.Clear();
        cache.Items.AddRange(source);
        return cache.Items;
    }
    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var result = new List<CodeInstruction>();
        int replaced = 0;
        var replacement = AccessTools.Method(typeof(WrecksPlugin), nameof(Snapshot));
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
                method.DeclaringType == typeof(Enumerable) && method.Name == "ToList" &&
                method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(Wreckage))
            {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                owner.labels.AddRange(instruction.labels);
                owner.blocks.AddRange(instruction.blocks);
                result.Add(owner);
                result.Add(new CodeInstruction(OpCodes.Call, replacement));
                replaced++;
            }
            else result.Add(instruction);
        }
        if (replaced != 1) throw new InvalidOperationException("Expected one wreck snapshot call, got " + replaced);
        return result;
    }
}
