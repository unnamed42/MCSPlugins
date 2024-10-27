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
            PatchPlugin.LogInfo("未注册新类型灵气，跳过补丁");
            return;
        }
        {
            var patchTarget = AccessTools.Constructor(MCSCheat_Patch.cheat.GetType("MCSCheat.PageFight"));
            var patch = AccessTools.Method(typeof(MCSCheat_ElementalMastery_Patch), nameof(PageFight_Postfix));
            h.Patch(patchTarget, postfix: new HarmonyMethod(patch));
            PatchPlugin.LogInfo("已修补修改器战斗界面-灵气");
        }
        {
            var patchTarget = AccessTools.Method(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "LingGenGUI");
            var patch = AccessTools.Method(typeof(MCSCheat_ElementalMastery_Patch), nameof(LingGenGUI_Patch));
            h.Patch(patchTarget, transpiler: new HarmonyMethod(patch));
        }
    }

    public static void PageFight_Postfix(EnumSelectGUI ___lingQiSelectGUI) =>
        MCSCheat_Patch.AddEnumSelection(___lingQiSelectGUI,
            ElementalMastery.ElementalMastery.Inst.Elements.Select(e => e.name));

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
                PatchPlugin.LogInfo("已修补修改器灵根界面");
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
