using System;
using System.Reflection;
using HarmonyLib;

namespace Unnamed42.ModPatches.Utils;

public static class HarmonyUtils
{
    public static readonly Harmony Patcher = new Harmony("Unnamed42.ModPatches.PatcherUtil");

    // 一个简单的prefix用于重新patch一次harmony
    private static bool Prefix()
    {
        return true;
    }

    public static void ReapplyPatch(MethodBase target)
    {
        var patch = AccessTools.Method(typeof(HarmonyUtils), nameof(Prefix));
        Patcher.Patch(target, prefix: new HarmonyMethod(patch));
        Patcher.Unpatch(target, patch);
    }

    public static void ReapplyPatch(Type type, string method)
    {
        var target = AccessTools.Method(type, method);
        ReapplyPatch(target);
    }
}
