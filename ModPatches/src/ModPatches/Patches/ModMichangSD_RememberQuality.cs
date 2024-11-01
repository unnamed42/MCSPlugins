using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ModMichangSD;
using UnityEngine.UI;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.新交易助手)]
public class ModMichangSD_RememberQuality_Patch
{

    [HarmonyTargetMethod]
    public static MethodBase TargetMethod()
    {
        var start = AccessTools.Method(typeof(PluginEntry).Assembly.GetType("ModMichangSD.MixedTrade"), "Start");
        var enumerator = start.GetCustomAttribute<IteratorStateMachineAttribute>();
        return AccessTools.Method(enumerator.StateMachineType, "MoveNext");
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> RememberQualitySelection(IEnumerable<CodeInstruction> ins)
    {
        var setIsOn = AccessTools.PropertySetter(typeof(Toggle), nameof(Toggle.isOn));
        var setDict = AccessTools.Method(typeof(Dictionary<int, bool>), "set_Item");
        var lastQualAll = AccessTools.Field(typeof(PluginEntry).Assembly.GetType("ModMichangSD.MixedTrade"), "m_LastQualAll");
        var onValueChanged = AccessTools.Field(typeof(Toggle), nameof(Toggle.onValueChanged));

        var codes = ins.ToList();
        // component.isOn = key == 0
        var start = codes.FindIndex((a, b, c, d, e) =>
            a.IsLdLoc_S(5) && b.IsLdLoc_S(6) && c.Is(OpCodes.Ldc_I4_0) && d.Is(OpCodes.Ceq) && e.Calls(setIsOn));
        if (start == -1)
            return ins;
        // component.onValueChanged
        var end = codes.FindIndex(start + 5, (a, b) =>
            a.IsLdLoc_S(5) && b.Is(OpCodes.Ldfld, onValueChanged));
        if (end == -1)
            return ins;
        codes.RemoveRange(start, end - start);
        codes.InsertRange(start, new[] {
            // component.isOn = PluginEntry.instance.m_toggleQual[key]
            new CodeInstruction(OpCodes.Ldloc_S, 5),
            new CodeInstruction(OpCodes.Ldsfld,  AccessTools.Field(typeof(PluginEntry), nameof(PluginEntry.instance))),
            new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PluginEntry), nameof(PluginEntry.m_toggleQual))),
            new CodeInstruction(OpCodes.Ldloc_S, 6),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Dictionary<int, bool>), "get_Item")),
            new CodeInstruction(OpCodes.Callvirt, setIsOn),
        });

        var lastQualIdx = codes.FindIndex(start+1, (a, b) =>
            a.Is(OpCodes.Ldc_I4_1) && b.Is(OpCodes.Stfld, lastQualAll));
        if(lastQualIdx == -1)
            return ins;
        codes.RemoveRange(lastQualIdx, 1);
        codes.InsertRange(lastQualIdx, new [] {
            // close.m_LastQualAll = PluginEntry.instance.m_togggleQual[0]
            // 最后的stfld保留，没有替换
            new CodeInstruction(OpCodes.Ldsfld,  AccessTools.Field(typeof(PluginEntry), nameof(PluginEntry.instance))),
            new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PluginEntry), nameof(PluginEntry.m_toggleQual))),
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Dictionary<int, bool>), "get_Item")),
        });
        PatchPlugin.LogInfo("已修补交易助手-记忆品阶筛选");
        return codes;
    }

}
