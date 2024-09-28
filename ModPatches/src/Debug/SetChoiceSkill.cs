using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityExplorer;
using Unnamed42.ModPatches.Utils;
using YSGame.Fight;

namespace Unnamed42.ModPatches.Debug;

[ModDependency(ModId.UnityExplorer)]
public class SetChoiceSkill_Debug {

    public static void Print(string str, Dictionary<int, int> skillCost)
    {
        ExplorerCore.Log($"灵气计算=[{str}] 灵气消耗={skillCost.Select(pair => $"{LingQi(pair.Key)}={pair.Value}").Join(" ")}");
    }

    private static string LingQi(int type)
    {
        var max = (int)LingQiType.Count;
        if (type < max)
        {
            return ((LingQiType)type).ToString();
        }
        return ElementalMastery.ElementalMastery.Inst.Elements.ElementAt(type - max)?.name;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SetChoiceSkill))]
    public static IEnumerable<CodeInstruction> AddLog(IEnumerable<CodeInstruction> ins, ILGenerator gen)
    {
        var codes = ins.ToList();
        var print = typeof(SetChoiceSkill_Debug).GetMethod(nameof(Print), BindingFlags.Public | BindingFlags.Static);
        var insertion = codes.FindIndex((a, b) => a.Is(OpCodes.Ldc_I4_1) && b.IsStLoc_S(20));
        if (insertion != -1)
        {
            codes.InsertRange(insertion, new List<CodeInstruction> {
                new(OpCodes.Ldloc_S, 22),
                new(OpCodes.Ldloc_S, 17),
                new(OpCodes.Call, print),
            });
        }
        var insertion2 = codes.FindIndex((a, b) => a.Is(OpCodes.Ldc_I4_1) && b.IsStLoc_S(4));
        if (insertion2 != -1)
        {
            codes.InsertRange(insertion2, new List<CodeInstruction> {
                new(OpCodes.Ldstr, "固定灵气"),
                new(OpCodes.Ldloc_3),
                new(OpCodes.Call, print),
                //
                new(OpCodes.Ldstr, "同系灵气"),
                new(OpCodes.Ldloc_S, 6),
                new(OpCodes.Call, print),
            });
        }
        if (insertion != -1 && insertion2 != -1)
            PatchPlugin.Logger.LogInfo("override add debug info");
        return codes;
    }
}
