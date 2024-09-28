using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.灵界, ModId.埋久工具库)]
public class Lingjie_LingQi_Patch
{

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.CalcTongLingQiKeNeng))]
    public static IEnumerable<CodeInstruction> CalcTongLingQiKeNeng_Patch(IEnumerable<CodeInstruction> ins, ILGenerator gen)
    {
        var codes = ins.ToList();
        // 找到循环体开头 dictionary.Add(index, avatar.cardMag.getCardTypeNum(index));
        var loopStart = codes.FindIndex((a, b, c) =>
            a.Is(OpCodes.Ldloc_0) && b.IsLdLoc_S(4) && c.Is(OpCodes.Ldarg_1));
        if (loopStart == -1)
            return codes;
        // 找到 for循环的 ++index
        var loopIncrement = codes.FindIndex(loopStart, (a, b, c) =>
            a.IsLdLoc_S(4) && b.Is(OpCodes.Ldc_I4_1) && c.Is(OpCodes.Add));
        if (loopIncrement == -1)
            return codes;
        // 插入 if(SkipIndex(index, dictionary)) continue;
        codes.InsertRange(loopStart, new List<CodeInstruction>{
            // index
            new CodeInstruction(OpCodes.Ldloc_S, 4).Also(it => codes[loopStart].MoveLabelsTo(it)),
            // dictionary
            new(OpCodes.Ldloc_0),
            // SkipIndex(index, dictionary)
            new(OpCodes.Call, typeof(Lingjie_LingQi_Patch).GetMethod(nameof(SkipIndex), BindingFlags.Public|BindingFlags.Static)),
            // if true continue
            new(OpCodes.Brtrue, gen.DefineLabel().Also(label => codes[loopIncrement].labels.Add(label))),
        });
        PatchPlugin.Logger.LogInfo($"已修补灵界-同系灵气计算跳过鬼魔灵气");
        return codes;
    }

    public static bool SkipIndex(int index, Dictionary<int, int> dict)
    {
        if (!PatchPlugin.Instance.Enable_Lingjie_LingQi_Patch.Value)
            return false;
        var skip = (!ModId.魔气作为同系灵气.IsActive() && index == 5) // 魔气
            || (index >= 6 && ElementalMastery.ElementalMastery.Inst.Elements.ElementAtOrDefault(index - 6)?.name == "鬼");
        if (skip) dict.Add(index, 0);
        return skip;
    }

}
