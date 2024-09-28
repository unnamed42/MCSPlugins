using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using KBEngine;
using UnityEngine;
using Unnamed42.ModPatches.Utils;
using XYModLib;
using YSGame.Fight;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.埋久工具库)]
public class MCSCheat_ElementalMastery_Patch
{
    public static void Setup(Harmony h)
    {
        if (ElementalMastery.ElementalMastery.MAX == (int)LingQiType.Count)
        {
            PatchPlugin.Logger.LogInfo("未注册新类型灵气，跳过补丁");
            return;
        }
        {
            var patchTarget = AccessTools.Method(MCSCheat_Patch.cheat.GetType("MCSCheat.PageFight"), "ChangeLingQiGUI");
            var patch = AccessTools.Method(typeof(MCSCheat_ElementalMastery_Patch), nameof(ChangeLingQiGUI_Prefix));
            h.Patch(patchTarget, prefix: new HarmonyMethod(patch));
            PatchPlugin.Logger.LogInfo("已修补修改器战斗界面-灵气");
        }
        {
            var patchTarget = AccessTools.Method(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "LingGenGUI");
            var patch = AccessTools.Method(typeof(MCSCheat_ElementalMastery_Patch), nameof(LingGenGUI_Patch));
            h.Patch(patchTarget, transpiler: new HarmonyMethod(patch));
        }
    }

    public static bool ChangeLingQiGUI_Prefix(EnumSelectGUI ___lingQiSelectGUI)
    {
        var names = new Traverse(___lingQiSelectGUI).Field("EnumNames");
        var currValue = names.GetValue<string[]>();
        if (currValue.Length != 7) // 全部 金 木 水 火 土 魔
        {
            return true;
        }
        var max = ElementalMastery.ElementalMastery.MAX;
        var newNames = currValue.ToList();
        var elements = ElementalMastery.ElementalMastery.Inst.Elements;
        for (int i = (int)LingQiType.Count; i < max; i++)
        {
            newNames.Add(elements[i - (int)LingQiType.Count].name);
        }
        names.SetValue(newNames.ToArray());
        return true;
    }

    public static IEnumerable<CodeInstruction> LingGenGUI_Patch(IEnumerable<CodeInstruction> ins)
    {
        var endVertical = AccessTools.Method(typeof(GUILayout), nameof(GUILayout.EndVertical));
        foreach (var c in ins)
        {
            if (c.Is(OpCodes.Call, endVertical))
            {
                var baseWidthOption = AccessTools.Field(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "baseWidthOption");
                var baseWidth = AccessTools.Field(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "baseWidth");
                var patch = AccessTools.Method(typeof(MCSCheat_ElementalMastery_Patch), nameof(LingGenGUI_ExtraLingGen));
                yield return new CodeInstruction(OpCodes.Ldloc_0);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, baseWidth);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, baseWidthOption);
                yield return new CodeInstruction(OpCodes.Call, patch);
                PatchPlugin.Logger.LogInfo("已修补修改器灵根界面");
            }
            yield return c;
        }
    }

    public static void LingGenGUI_ExtraLingGen(Avatar player, int baseWidth, GUILayoutOption baseWidthOption)
    {
        var elements = ElementalMastery.ElementalMastery.Inst.Elements;
        // 魔灵根
        if (player.LingGeng.Count > 5)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("基础魔灵根", baseWidthOption);
            player.LingGeng[5] = GUIHelper.IntTextGUI(player.LingGeng[5], "player.LingGeng[5]", baseWidth, 0, 999);
            GUILayout.Label($" 加成后 {LingGenAddition(player.GetLingGeng, 5, player.LingGeng[5])}");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        // 异灵根
        var offset = (int)LingQiType.Count;
        for (int i = offset; i < player.LingGeng.Count; i++)
        {
            var element = elements.ElementAtOrDefault(i - offset);
            if (element == null) continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"基础{element.name}灵根", baseWidthOption);
            var value = GUIHelper.IntTextGUI(player.LingGeng[i], $"player.LingGeng[{i}]", baseWidth, 0, 999);
            player.LingGeng[i] = value;
            ElementalMastery.ElementalMastery.Inst.ExtraCardWeight.Weight[i] = value;
            GUILayout.Label($" 加成后 {LingGenAddition(player.GetLingGeng, i, player.LingGeng[i])}");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }

    private static int LingGenAddition(List<int> lingGen, int index, int fallback) =>
        lingGen.Count > index ? lingGen[index] : fallback;

}
