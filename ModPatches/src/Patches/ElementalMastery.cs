using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.埋久工具库)]
public class ElementalMastery_SetChoiceSkill_Patch
{

    [HarmonyTargetMethod]
    public static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(ElementalMastery.ElementalMastery).Assembly.GetType("ElementalMastery.Patcher"), "RoundManager_SetChoiceSkill_Patch");

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> ins)
    {
        if (!PatchPlugin.Instance.Enable_ElementalMastery_Empty_Slot_Patch.Value)
        {
            PatchPlugin.Logger.LogInfo("异灵气空槽修复-关闭，跳过");
            return ins;
        }
        return new List<CodeInstruction> {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Call, typeof(ElementalMastery_SetChoiceSkill_Patch).GetMethod(nameof(SetChoiceSkill_Patch), BindingFlags.Static|BindingFlags.Public)),
            new(OpCodes.Ret),
        };
    }

    public static IEnumerable<CodeInstruction> SetChoiceSkill_Patch(IEnumerable<CodeInstruction> ins, ILGenerator gen)
    {
        var codes = ins.ToList();
        // 找到循环 for (int key = 0; key < 6; ++key)
        var skillCostLoopStart = codes.FindIndex((a, b, c) =>
            a.Is(OpCodes.Ldc_I4_0) && b.IsStLoc_S(8) && c.Is(OpCodes.Br));
        if (skillCostLoopStart == -1)
            return ins;
        // 定位循环末尾 ++key
        var skillLoopEnd = codes.FindIndex(skillCostLoopStart, (a, b, c) =>
            a.IsLdLoc_S(8) && b.Is(OpCodes.Ldc_I4_6) && c.Is(OpCodes.Blt));
        if (skillLoopEnd == -1)
            return ins;
        var max = typeof(ElementalMastery.ElementalMastery).GetField("MAX", BindingFlags.Static | BindingFlags.Public);
        // 更改循环上限 6 --> ElementalMastery.MAX
        codes[skillLoopEnd + 1] = new CodeInstruction(OpCodes.Ldsfld, max);
        // 插入到循环体开头 if(key >= jsonObject.Count) continue;
        codes.InsertRange(skillCostLoopStart + 3, new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldloc, 8).Also(it => codes[skillLoopEnd+2].MoveLabelsTo(it)),
            new(OpCodes.Ldloc, 5),
            new(OpCodes.Callvirt, typeof (JSONObject).GetMethod("get_Count", BindingFlags.Instance|BindingFlags.Public)),
            new(OpCodes.Bge, gen.DefineLabel().Also(label => codes[skillLoopEnd].labels.Add(label))),
        });
        // 找到 for(int index=0; index<6; index++) 的循环条件体
        var secondLoopCond = codes.FindIndex(skillLoopEnd, (a, b, c) =>
            a.IsLdLoc_S(12) && b.Is(OpCodes.Ldc_I4_6) && c.Is(OpCodes.Blt));
        if (secondLoopCond == -1)
            return ins;
        // 更改循环上限 6 --> ElementalMastery.MAX
        codes[secondLoopCond + 1] = new CodeInstruction(OpCodes.Ldsfld, max);
        PatchPlugin.Logger.LogInfo("ElementalMastery-SetChoiceSkill替换完成");
        return codes;
    }
}
