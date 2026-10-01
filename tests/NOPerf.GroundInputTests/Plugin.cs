using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;

namespace NOPerformance;

[BepInPlugin("agent.noperf.groundinputtests","NO Performance: Disposable Ground Traversal Tests","0.1.0")]
[BepInDependency(GroundInputsPlugin.Id)]
public sealed class GroundInputTests:BaseUnityPlugin
{
    private long assertions;private int sink;
    private readonly List<string> report=new();
    private void Start()
    {
        bool disabled=Environment.GetEnvironmentVariable("NOPERF_GROUND_INPUT_DISABLED_TEST")=="1";
        if(Environment.GetEnvironmentVariable("NOPERF_GROUND_INPUT_TEST")!="1" && !disabled)return;
        if(!BuildGuard.Check(Logger)||UnitRegistry.allUnits.Count!=0){Logger.LogError("Ground tests refused: unknown or populated world");return;}
        try
        {
            if(disabled)
            {
                var patches=Harmony.GetPatchInfo(AccessTools.Method(typeof(NuclearOption.Jobs.GroundVehicleJobSettings),"SetArgs_Update"));
                Check(patches==null || !patches.Owners.Contains(GroundInputsPlugin.Id),"disabled mode leaves original unpatched");
                report.Add("PASS assertions="+assertions+" disabled mode: original ground scan unpatched");
            }
            else
            {
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(NuclearOption.Jobs.GroundVehicleJobSettings),"SetArgs_Update")).Owners.Contains(GroundInputsPlugin.Id),"actual traversal transpiler active");
            var conflict=new Harmony("agent.noperf.groundinputtests.conflict");
            foreach(string targetName in new[]{"SetArgs_Update","ShouldRunInputs"})
            {
                new Harmony(GroundInputsPlugin.Id).UnpatchSelf();
                try
                {
                    conflict.Patch(AccessTools.Method(typeof(NuclearOption.Jobs.GroundVehicleJobSettings),targetName),prefix:new HarmonyMethod(typeof(GroundInputTests),nameof(ConflictProbe)));
                    AccessTools.Method(typeof(GroundInputsPlugin),"Awake").Invoke(BepInEx.Bootstrap.Chainloader.PluginInfos[GroundInputsPlugin.Id].Instance,null);
                    var active=Harmony.GetPatchInfo(AccessTools.Method(typeof(NuclearOption.Jobs.GroundVehicleJobSettings),"SetArgs_Update"));
                    Check(active==null || !active.Owners.Contains(GroundInputsPlugin.Id),"refuses existing collection or eligibility patch");
                }
                finally{conflict.UnpatchSelf();}
                AccessTools.Method(typeof(GroundInputsPlugin),"Awake").Invoke(BepInEx.Bootstrap.Chainloader.PluginInfos[GroundInputsPlugin.Id].Instance,null);
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(NuclearOption.Jobs.GroundVehicleJobSettings),"SetArgs_Update")).Owners.Contains(GroundInputsPlugin.Id),"reactivated on unmodified targets");
            }
            var random=new Random(72419);
            var ticks=new List<int>{int.MinValue,int.MinValue+1,-100,-13,-12,-1,0,1,11,12,13,int.MaxValue-12,int.MaxValue-11,int.MaxValue-1,int.MaxValue};
            for(int t=0;t<300;t++)ticks.Add(random.Next());
            for(int t=0;t<300;t++)ticks.Add(-random.Next());
            foreach(int tick in ticks)
            foreach(int count in new[]{0,1,11,12,13,128,440,2048})
            {
                var original=new List<int>();for(int i=0;i<count;i++)if(NuclearOption.Jobs.GroundVehicleJobSettings.ShouldRunInputs(i,tick))original.Add(i);
                var candidate=new List<int>();for(int i=GroundInputsPlugin.First(tick);i<count;i=GroundInputsPlugin.Next(i,tick))candidate.Add(i);
                Check(original.SequenceEqual(candidate),"eligibility/order parity");
            }
            // Check sentinel and signed-addition wrap near the far end of Count.
            foreach(int tick in ticks)
            for(int start=int.MaxValue-40;start<int.MaxValue-12;start++)
            {
                if(!NuclearOption.Jobs.GroundVehicleJobSettings.ShouldRunInputs(start,tick))continue;
                int expected=int.MaxValue;
                for(long n=(long)start+1;n<int.MaxValue;n++)if(NuclearOption.Jobs.GroundVehicleJobSettings.ShouldRunInputs((int)n,tick)){expected=(int)n;break;}
                Check(GroundInputsPlugin.Next(start,tick)==expected,"near-limit next index");
            }
            for(int trial=0;trial<5;trial++)
            {
                double original,candidate;
                if(trial%2==0){original=Bench(false);candidate=Bench(true);}else{candidate=Bench(true);original=Bench(false);}
                report.Add($"BENCH trial={trial} original_ms={original:F4} candidate_ms={candidate:F4} count=440 ticks=200000");
            }
            report.Add("PASS assertions="+assertions+"; bookkeeping benchmark only, not whole-server gain");
            }
        }
        catch(Exception ex){report.Add("FAIL "+ex);}
        var dir=Path.Combine(Paths.BepInExRootPath,"data","noperf");Directory.CreateDirectory(dir);File.WriteAllLines(Path.Combine(dir,"ground-input-tests.txt"),report);
        foreach(var line in report)Logger.LogInfo(line);
    }
    private double Bench(bool candidate)
    {
        var watch=Stopwatch.StartNew();int total=0;
        for(int tick=0;tick<200000;tick++)
        {
            if(candidate){for(int i=GroundInputsPlugin.First(tick);i<440;i=GroundInputsPlugin.Next(i,tick))if(NuclearOption.Jobs.GroundVehicleJobSettings.ShouldRunInputs(i,tick))total=unchecked(total+i);}
            else {for(int i=0;i<440;i++)if(NuclearOption.Jobs.GroundVehicleJobSettings.ShouldRunInputs(i,tick))total=unchecked(total+i);}
        }
        watch.Stop();sink=total;return watch.Elapsed.TotalMilliseconds;
    }
    private void Check(bool value,string label){assertions++;if(!value)throw new InvalidOperationException(label);}
    private static void ConflictProbe() { }
}
