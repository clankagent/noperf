using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;

namespace NOPerformance;

[BepInPlugin(Id, "NO Performance: Ground Input Traversal", "0.1.0")]
public sealed class GroundInputsPlugin : BaseUnityPlugin
{
    public const string Id = "agent.noperf.groundinputs";
    private Harmony? harmony;
    private void Awake()
    {
        if (!Config.Bind("General", "Enabled", false, "Experimental traversal of already-scheduled ground inputs. Original cadence and order retained; restart to apply.").Value || !BuildGuard.Check(Logger)) return;
        var method=AccessTools.Method(typeof(NuclearOption.Jobs.GroundVehicleJobSettings),"SetArgs_Update");
        foreach(var target in new[]{method,AccessTools.Method(typeof(NuclearOption.Jobs.GroundVehicleJobSettings),"ShouldRunInputs")})
        {
            var info=Harmony.GetPatchInfo(target);
            if(info!=null && info.Prefixes.Count+info.Postfixes.Count+info.Transpilers.Count+info.Finalizers.Count!=0)
            {Logger.LogWarning("Inactive: another patch changes ground input collection or eligibility.");return;}
        }
        harmony=new Harmony(Id);
        try {harmony.Patch(method,transpiler:new HarmonyMethod(typeof(GroundInputsPlugin),nameof(Transpile)));Logger.LogInfo("Patched two ground input traversal loops; original eligibility predicate, input callbacks, order and tick offset retained.");}
        catch(Exception ex){harmony.UnpatchSelf();Logger.LogError("Ground input traversal rolled back: "+ex);}
    }
    private void OnDestroy()=>harmony?.UnpatchSelf();

    // The shipped predicate checks unchecked(i + tick) % 12 == 0. Handle the
    // signed addition wrap instead of assuming mission ticks remain positive.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int First(int tick)
    {
        long residue=(-(long)tick)%12;if(residue<0)residue+=12;
        if(tick>0 && residue+tick>int.MaxValue) return FirstAfterWrap(tick);
        return (int)residue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(int index,int tick)
    {
        long next=(long)index+12;
        if(next>=int.MaxValue)return int.MaxValue; // Sentinel is outside any valid Count.
        if(tick>0 && (long)index+tick<=int.MaxValue && next+tick>int.MaxValue)return FirstAfterWrap(tick);
        return (int)next;
    }
    private static int FirstAfterWrap(int tick)
    {
        long boundary=(long)int.MaxValue-tick+1;
        long residue=(4294967296L-tick)%12;
        long next=boundary+(residue-boundary%12+12)%12;
        return next>=int.MaxValue?int.MaxValue:(int)next;
    }
    private static int? Local(CodeInstruction i,bool store)
    {
        var op=i.opcode;
        if(store)
        {if(op==OpCodes.Stloc_0)return 0;if(op==OpCodes.Stloc_1)return 1;if(op==OpCodes.Stloc_2)return 2;if(op==OpCodes.Stloc_3)return 3;}
        else
        {if(op==OpCodes.Ldloc_0)return 0;if(op==OpCodes.Ldloc_1)return 1;if(op==OpCodes.Ldloc_2)return 2;if(op==OpCodes.Ldloc_3)return 3;}
        if(op==(store?OpCodes.Stloc:OpCodes.Ldloc)||op==(store?OpCodes.Stloc_S:OpCodes.Ldloc_S))
            return i.operand is LocalBuilder b?b.LocalIndex:Convert.ToInt32(i.operand);
        return null;
    }
    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var list=new List<CodeInstruction>(instructions);
        var predicate=AccessTools.Method(typeof(NuclearOption.Jobs.GroundVehicleJobSettings),"ShouldRunInputs");
        var starts=new Dictionary<int,CodeInstruction>();var increments=new Dictionary<int,CodeInstruction>();int found=0;
        for(int call=2;call<list.Count;call++)
        {
            if(!list[call].Calls(predicate))continue;
            int? counter=Local(list[call-2],false),tick=Local(list[call-1],false);
            if(counter==null||tick==null)throw new InvalidOperationException("Unexpected input predicate locals");
            int init=-1,increment=-1;
            for(int j=call-3;j>=1;j--)
                if(Local(list[j],true)==counter){if(list[j-1].opcode==OpCodes.Ldc_I4_0)init=j-1;break;}
            for(int j=call+1;j<list.Count-3;j++)
                if(Local(list[j],false)==counter && list[j+1].opcode==OpCodes.Ldc_I4_1 && list[j+2].opcode==OpCodes.Add && Local(list[j+3],true)==counter){increment=j+1;break;}
            if(init<0||increment<0||starts.ContainsKey(init)||increments.ContainsKey(increment))throw new InvalidOperationException("Unexpected ground input loop shape");
            // Preserve labels/exception blocks on the existing instructions.
            starts.Add(init,new CodeInstruction(list[init]){opcode=list[call-1].opcode,operand=list[call-1].operand});
            increments.Add(increment,new CodeInstruction(list[increment]){opcode=list[call-1].opcode,operand=list[call-1].operand});
            found++;
        }
        if(found!=2)throw new InvalidOperationException("Expected two input loops, found "+found);
        var result=new List<CodeInstruction>();
        for(int i=0;i<list.Count;i++)
        {
            if(starts.TryGetValue(i,out var start)){result.Add(start);result.Add(new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(GroundInputsPlugin),nameof(First))));}
            else if(increments.TryGetValue(i,out var tickLoad))
            {
                if(list[i+1].labels.Count!=0||list[i+1].blocks.Count!=0)throw new InvalidOperationException("Unexpected metadata on loop addition");
                result.Add(tickLoad);result.Add(new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(GroundInputsPlugin),nameof(Next))));i++;
            }
            else result.Add(list[i]);
        }
        return result;
    }
}
