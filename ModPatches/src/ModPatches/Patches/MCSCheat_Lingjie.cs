using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using KBEngine;
using UnityEngine;
using Unnamed42.ModPatches.Utils;
using XYModLib;

namespace Unnamed42.ModPatches.Patches;

[ModDependecyAnyOf(ModId.灵界, ModId.更多仙器神器圣器)]
public class MCSCheat_Lingjie_Patch
{

    public static void Setup(Harmony h)
    {
        {
            var patchTarget = AccessTools.Method(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "MiscGUI");
            var patch = AccessTools.Method(typeof(MCSCheat_Lingjie_Patch), nameof(MiscGUI_Patch));
            h.Patch(patchTarget, transpiler: new HarmonyMethod(patch));
        }
        {
            var patchTarget = AccessTools.Constructor(MCSCheat_Patch.cheat.GetType("MCSCheat.PageItem"));
            var patch = AccessTools.Method(typeof(MCSCheat_Lingjie_Patch), nameof(PageItem_Postfix));
            h.Patch(patchTarget, postfix: new HarmonyMethod(patch));
            PatchPlugin.LogInfo("已增加更多物品品阶筛选");
        }
        {
            var patchTarget = AccessTools.Constructor(MCSCheat_Patch.cheat.GetType("MCSCheat.PageGongFa"));
            var patch = AccessTools.Method(typeof(MCSCheat_Lingjie_Patch), nameof(PageGongFa_Postfix));
            h.Patch(patchTarget, postfix: new HarmonyMethod(patch));
            PatchPlugin.LogInfo("已增加更多功法品阶筛选");
        }
    }

    #region 灵界声望

    public static IEnumerable<CodeInstruction> MiscGUI_Patch(IEnumerable<CodeInstruction> ins)
    {
        var endVertical = AccessTools.Method(typeof(GUILayout), nameof(GUILayout.EndVertical));
        foreach (var c in ins)
        {
            if (c.Is(OpCodes.Call, endVertical))
            {
                var baseWidthOption = AccessTools.Field(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "baseWidthOption");
                var baseWidth = AccessTools.Field(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "baseWidth");
                var patch = AccessTools.Method(typeof(MCSCheat_Lingjie_Patch), nameof(MiscGUI_ExtraShengWang));
                yield return new CodeInstruction(OpCodes.Ldloc_0);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, baseWidth);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, baseWidthOption);
                yield return new CodeInstruction(OpCodes.Call, patch);
                PatchPlugin.LogInfo("已修补修改器杂项界面");
            }
            yield return c;
        }
    }

    public static void MiscGUI_ExtraShengWang(Avatar player, int baseWidth, GUILayoutOption baseWidthOption)
    {
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("灵界声望", baseWidthOption);
            int value = PlayerEx.GetShengWang(660);
            int input = GUIHelper.IntTextGUI(value, "lingjieShengWang", baseWidth, -999999, 999999);
            if (value != input)
                player.MenPaiHaoGanDu.SetField("660", input);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("月池国声望", baseWidthOption);
            int value = PlayerEx.GetShengWang(380);
            int input = GUIHelper.IntTextGUI(value, "yuechiShengWang", baseWidth, -999999, 999999);
            if (value != input)
                player.MenPaiHaoGanDu.SetField("380", input);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }

    #endregion 灵界声望

    #region 品阶筛选

    public static void PageItem_Postfix(EnumSelectGUI ___qualitySelectGUI) =>
        MCSCheat_Patch.AddEnumSelection(___qualitySelectGUI, "七品", "八品", "九品");

    public static void PageGongFa_Postfix(EnumSelectGUI ___qualitySelectGUI) =>
        MCSCheat_Patch.AddEnumSelection(___qualitySelectGUI, "仙阶");

    #endregion 品阶筛选
}
