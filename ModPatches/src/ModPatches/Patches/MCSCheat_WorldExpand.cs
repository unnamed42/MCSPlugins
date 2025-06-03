using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Unnamed42.ModPatches.Utils;
using UniqueCream.MCSWorldExpand.ExpandMethod;
using KBEngine;
using XYModLib;
using System;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.世界拓展)]
public class MCSCheat_WorldExpand_Patch
{
    public static void Setup(Harmony h)
    {
        var patchTarget = AccessTools.Method(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "BaseDataGUI");
        var patch = AccessTools.Method(typeof(MCSCheat_WorldExpand_Patch), nameof(BaseDataGUI_Patch));
        h.Patch(patchTarget, transpiler: new HarmonyMethod(patch));
    }

    public static IEnumerable<CodeInstruction> BaseDataGUI_Patch(IEnumerable<CodeInstruction> ins)
    {
        var endVertical = AccessTools.Method(typeof(GUILayout), nameof(GUILayout.EndVertical));
        foreach (var c in ins)
        {
            if (c.Is(OpCodes.Call, endVertical))
            {
                var baseWidthOption = AccessTools.Field(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "baseWidthOption");
                var baseWidth = AccessTools.Field(MCSCheat_Patch.cheat.GetType("MCSCheat.PagePlayer"), "baseWidth");
                var patch = AccessTools.Method(typeof(MCSCheat_WorldExpand_Patch), nameof(BaseData_WorldExpand));
                yield return new CodeInstruction(OpCodes.Ldloc_0);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, baseWidth);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, baseWidthOption);
                yield return new CodeInstruction(OpCodes.Call, patch);
                PatchPlugin.LogInfo("已修补觅长生世界拓展-基础信息界面");
            }
            yield return c;
        }
    }

    public static void BaseData_WorldExpand(Avatar player, int baseWidth, GUILayoutOption baseWidthOption)
    {
        var expandData = player.ExpandData();
        if (expandData == null) return;
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("经脉/经脉上限", baseWidthOption);
            int curr = expandData.JingMai.Now;
            float currMax = expandData.JingMai.Max;
            int inputCurr = GUIHelper.IntTextGUI(curr, "player.JingMai.Now", baseWidth, 0, (int)Math.Floor(currMax));
            GUILayout.Label("/");
            float inputMax = GUIHelper.FloatTextGUI(currMax, "player.JingMai.Max", baseWidth, 1, 999999);
            expandData.JingMai.ChangeNow(inputCurr - curr);
            expandData.JingMai.ChangeMax(inputMax - currMax);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("丹田/丹田上限", baseWidthOption);
            int curr = expandData.DanTian.Now;
            float currMax = expandData.DanTian.Max;
            int inputCurr = GUIHelper.IntTextGUI(curr, "player.DanTian.Now", baseWidth, 0, (int)Math.Floor(currMax));
            GUILayout.Label("/");
            float inputMax = GUIHelper.FloatTextGUI(currMax, "player.DanTian.Max", baseWidth, 1, 999999);
            expandData.DanTian.ChangeNow(inputCurr - curr);
            expandData.DanTian.ChangeMax(inputMax - currMax);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("血气/血气上限", baseWidthOption);
            int curr = expandData.XueQi.Now;
            float currMax = expandData.XueQi.Max;
            int inputCurr = GUIHelper.IntTextGUI(curr, "player.XueQi.Now", baseWidth, 0, (int)Math.Floor(currMax));
            GUILayout.Label("/");
            float inputMax = GUIHelper.FloatTextGUI(currMax, "player.XueQi.Max", baseWidth, 1, 999999);
            expandData.XueQi.ChangeNow(inputCurr - curr);
            expandData.XueQi.ChangeMax(inputMax - currMax);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }

}
