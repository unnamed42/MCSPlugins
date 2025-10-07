using System.Collections.Generic;
using HarmonyLib;
using MCS_NPCManager;
using UnityEngine;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.NPC属性修改)]
public class McsNpcManager_Patch
{

    public class UIScaling_Patch
    {

        [HarmonyPrefix, HarmonyPatch(typeof(MainWindow), nameof(MainWindow.OnGUI))]
        public static bool ScalingPrefix(out Matrix4x4 __state)
        {
            __state = GUI.matrix;
            if (PatchPlugin.Instance.Enable_GUILayout_Scaling.Value)
            {
                if (UIUtils.GetScalingMatrix() is Matrix4x4 matrix)
                    GUI.matrix = matrix;
            }
            return true;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(MainWindow), nameof(MainWindow.OnGUI))]
        public static void ScalingPostfix(Matrix4x4 __state)
        {
            // 恢复缩放比例
            GUI.matrix = __state;
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MainWindow), nameof(MainWindow.Start))]
    public static void Start_Postfix(List<string> ___levelNames)
    {
        ___levelNames.Add("炼虚初期");
        ___levelNames.Add("炼虚中期");
        ___levelNames.Add("炼虚后期");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(MainWindow), nameof(MainWindow.WindowFunc))]
    public static bool WindowFunc_Prefix(ref int ___currentNpcId, ref JSONObject ___currentNpc, ref JSONObject ___currentNpcRandom)
    {
        var dead = NpcJieSuanManager.inst.IsDeath(___currentNpcId);
        if (dead && ___currentNpc != null)
        {
            ___currentNpcId = 0;
            ___currentNpc = null;
            ___currentNpcRandom = null;
        }
        return true;
    }
}
