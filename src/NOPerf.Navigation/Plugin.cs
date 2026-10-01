using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using BepInEx;
using HarmonyLib;
using RoadPathfinding;

namespace NOPerformance;

[BepInPlugin(Id, "NO Performance: Road Membership", "0.1.0")]
public sealed class NavigationPlugin : BaseUnityPlugin
{
    public const string Id = "agent.noperf.navigation";
    private Harmony? harmony;
    private static int mainThread, depth, nullCount, observedCount;
    private static bool allowed;
    private static List<Node>? source;
    private static Node?[] identities = Array.Empty<Node?>();
    private static int[] counts = Array.Empty<int>();

    private void Awake()
    {
        if (!Config.Bind("General", "Enabled", false, "Experimental road membership cache; keeps original sort, ties and routes. Restart to apply.").Value || !BuildGuard.Check(Logger)) return;
        mainThread = Thread.CurrentThread.ManagedThreadId;
        var method = AccessTools.Method(typeof(RoadPathfinder), "TryPathfind");
        if (method == null || !BuildGuard.Unmodified(method, Logger)) return;
        harmony = new Harmony(Id);
        try
        {
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(NavigationPlugin), nameof(Begin)),
                transpiler: new HarmonyMethod(typeof(NavigationPlugin), nameof(Transpile)),
                finalizer: new HarmonyMethod(typeof(NavigationPlugin), nameof(End)));
            Logger.LogInfo("Patched road membership checks only; original sorting and route construction retained.");
        }
        catch (Exception ex) { harmony.UnpatchSelf(); Logger.LogError("Road patch rolled back: " + ex); }
    }
    private void OnDestroy() => harmony?.UnpatchSelf();

    private static void Begin(out bool __state)
    {
        __state = Thread.CurrentThread.ManagedThreadId == mainThread;
        if (!__state) return;
        if (++depth == 1) { allowed = true; source = null; nullCount = observedCount = 0; }
        else allowed = false; // The game's shared list may have changed during a nested call.
    }
    private static void End(bool __state)
    {
        if (!__state || --depth != 0) return;
        source = null; allowed = false;
        if (identities.Length > 4096) {identities=Array.Empty<Node?>();counts=Array.Empty<int>();}
        else {Array.Clear(identities,0,identities.Length);Array.Clear(counts,0,counts.Length);}
        nullCount = 0;
        observedCount = 0;
    }
    private static bool CanCache => Thread.CurrentThread.ManagedThreadId == mainThread && depth == 1 && allowed;

    private static bool Contains(List<Node> list, Node node)
    {
        if (!CanCache) return list.Contains(node);
        if (source == null)
        {
            // Build lazily after the first original removal. Sort changes order,
            // not membership. Counts retain List.Contains semantics for duplicates.
            // Shipped road nodes have dense IDs. Validate identities before using
            // them; unusual/colliding/sparse IDs use the original List operation.
            source = list; observedCount = list.Count;
            int maxId=-1;
            foreach(var item in list)
            {
                if(item==null)continue;
                if(item.id<0 || item.id>=65536) {allowed=false;return list.Contains(node);}
                maxId=Math.Max(maxId,item.id);
            }
            if(maxId>Math.Max(64L,(long)list.Count*4)) {allowed=false;return list.Contains(node);}
            if(identities.Length<=maxId)
            {
                int capacity=128;while(capacity<=maxId)capacity*=2;
                identities=new Node?[capacity];counts=new int[capacity];
            }
            foreach (var item in list)
            {
                if (item == null) { nullCount++; continue; }
                if(identities[item.id]!=null && !ReferenceEquals(identities[item.id],item))
                {allowed=false;return list.Contains(node);}
                identities[item.id]=item;counts[item.id]++;
            }
        }
        if (!ReferenceEquals(source, list) || observedCount != list.Count)
        {
            allowed = false; return list.Contains(node);
        }
        if(node==null)return nullCount!=0;
        int id=node.id;
        return id>=0 && id<identities.Length && ReferenceEquals(identities[id],node) && counts[id]!=0;
    }
    private static bool Remove(List<Node> list, Node node)
    {
        bool removed = list.Remove(node); // Retain the original removal and its order.
        if (!removed || !CanCache || !ReferenceEquals(source, list)) return removed;
        observedCount--;
        if (node == null) nullCount--;
        else if(node.id>=0 && node.id<identities.Length && ReferenceEquals(identities[node.id],node)) counts[node.id]--;
        else allowed = false;
        return removed;
    }
    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        int contains = 0, remove = 0;
        var result = new List<CodeInstruction>();
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) &&
                instruction.operand is MethodInfo method && method.DeclaringType == typeof(List<Node>) &&
                (method.Name == "Contains" || method.Name == "Remove"))
            {
                var replacement = new CodeInstruction(instruction) { opcode = OpCodes.Call,
                    operand = AccessTools.Method(typeof(NavigationPlugin), method.Name) };
                result.Add(replacement);
                if (method.Name == "Contains") contains++; else remove++;
            }
            else result.Add(instruction);
        }
        if (contains != 1 || remove != 1) throw new InvalidOperationException($"Expected one membership/removal call, got {contains}/{remove}.");
        return result;
    }
}
