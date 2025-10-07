using HarmonyLib;
using UnityEngine;
using Unnamed42.ModPatches.Utils;
using XYModLib;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.宵夜工具库)]
public class XYModLib_Scaling_Patch
{

    public class UIWindow_Position_Fix
    {
        [HarmonyPostfix, HarmonyPatch(typeof(UIWindow), MethodType.Constructor, typeof(string))]
        public static void PositionPostfix(UIWindow __instance)
        {
            if (!PatchPlugin.Instance.Enable_GUILayout_Scaling.Value)
                return;
            int width = Screen.width, height = Screen.height;
            // 只针对大于1080P的屏幕做缩放
            if (width <= 1920 || height <= 1080)
                return;
            var factorX = width / 1920f;
            var factorY = height / 1080f;
            __instance.WindowRect = new Rect((Screen.width / 2f - 400)/factorX, (Screen.height / 2f - 300)/factorY, 800f, 600f);
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(UIWindow), nameof(UIWindow.OnGUI))]
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

    [HarmonyPostfix, HarmonyPatch(typeof(UIWindow), nameof(UIWindow.OnGUI))]
    public static void ScalingPostfix(Matrix4x4 __state)
    {
        // 恢复缩放比例
        GUI.matrix = __state;
    }
}
