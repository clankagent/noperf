using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NOPerformance;

internal static class BuildGuard
{
    internal const string GameHash = "df5bed594dd84912efb3e57faa75b37d7e327bf4c8f5418f50411ad0ff46e24a";
    internal static bool Check(ManualLogSource log)
    {
        if (!Application.isBatchMode)
        {
            log.LogInfo("Inactive: dedicated batch server required.");
            return false;
        }
        try
        {
            using var file = File.OpenRead(typeof(BattlefieldGrid).Assembly.Location);
            using var sha = SHA256.Create();
            var actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            if (actual != GameHash)
            {
                log.LogWarning("Inactive: unverified game assembly " + actual + ". Rebuild and validate for this update.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning("Inactive: unable to verify game assembly: " + ex.Message);
            return false;
        }
    }

    internal static bool Unmodified(System.Reflection.MethodBase method, ManualLogSource log)
    {
        var patches = Harmony.GetPatchInfo(method);
        if (patches == null || (patches.Prefixes.Count == 0 && patches.Transpilers.Count == 0)) return true;
        log.LogWarning("Inactive target: another mod already changes " + method.DeclaringType + "." + method.Name);
        return false;
    }
}
