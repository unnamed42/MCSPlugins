using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.埋久工具库)]
public class ElementalMastery_EmptySlot_Patch
{

    public static void Setup()
    {
        PatchPlugin.Instance.Enable_ElementalMastery_EmptySolt_Patch.SettingChanged += (_, e) =>
        {
            HarmonyUtils.ReapplyPatch(typeof(RoundManager), nameof(RoundManager.SetChoiceSkill));
        };
    }

    [HarmonyTargetMethod]
    public static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(ElementalMastery.ElementalMastery).Assembly.GetType("ElementalMastery.Patcher"), "RoundManager_SetChoiceSkill_Patch");

    [HarmonyPrefix]
    public static bool ReplacePatch(IEnumerable<CodeInstruction> __0, ILGenerator __1, ref IEnumerable<CodeInstruction> __result)
    {
        if (!PatchPlugin.Instance.Enable_ElementalMastery_EmptySolt_Patch.Value)
        {
            PatchPlugin.LogInfo("异灵气空槽修复-关闭，跳过");
            return true;
        }
        __result = SetChoiceSkill_Patch(__0, __1);
        return false;
    }

    public static IEnumerable<CodeInstruction> SetChoiceSkill_Patch(IEnumerable<CodeInstruction> ins, ILGenerator gen)
    {
        var codes = ins.ToList();
        // 找到循环 for (int key = 0; key < 6; ++key)
        var skillLoopInit = codes.FindIndex((a, b, c) =>
            a.Is(OpCodes.Ldc_I4_0) && b.IsStLoc_S(8) && c.Is(OpCodes.Br));
        if (skillLoopInit == -1)
            return ins;
        // 定位循环自增 ++key
        var skillLoopIncr = codes.FindIndex(skillLoopInit, (a, b, c) =>
            a.IsLdLoc_S(8) && b.Is(OpCodes.Ldc_I4_1) && c.Is(OpCodes.Add));
        if (skillLoopIncr == -1)
            return ins;
        // 定位循环条件 key < 6
        var skillLoopEnd = codes.FindIndex(skillLoopInit, (a, b, c) =>
            a.IsLdLoc_S(8) && b.Is(OpCodes.Ldc_I4_6) && c.Is(OpCodes.Blt));
        if (skillLoopEnd == -1)
            return ins;
        var max = typeof(ElementalMastery.ElementalMastery).GetField("MAX", BindingFlags.Static | BindingFlags.Public);
        // 更改循环上限 6 --> ElementalMastery.MAX
        codes[skillLoopEnd + 1].Set(OpCodes.Ldsfld, max);
        // 插入到循环体开头 if(key >= jsonObject.Count) continue;
        codes.InsertRange(skillLoopInit + 3, new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldloc_S, 8).Also(it => codes[skillLoopInit+3].MoveLabelsTo(it)), // 插入到循环开头，需要调整循环开头的跳转位置
            new(OpCodes.Ldloc_S, 5),
            new(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(JSONObject), "Count")),
            new(OpCodes.Bge_S, gen.DefineLabel().Also(it => codes[skillLoopIncr].labels.Add(it))),
        });
        // 找到 for (int index = 0; index < 6; ++index) 的循环条件体
        var secondLoopCond = codes.FindIndex(skillLoopEnd, (a, b, c) =>
            a.IsLdLoc_S(12) && b.Is(OpCodes.Ldc_I4_6) && c.Is(OpCodes.Blt));
        if (secondLoopCond == -1)
            return ins;
        // 更改循环上限 6 --> ElementalMastery.MAX
        codes[secondLoopCond + 1].Set(OpCodes.Ldsfld, max);
        PatchPlugin.LogInfo("ElementalMastery-SetChoiceSkill替换完成");
        return codes;
    }
}
