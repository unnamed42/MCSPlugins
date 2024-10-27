using System.Collections.Generic;
using HarmonyLib;
using script.NewLianDan.DanFang;
using UnityEngine;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.灵界)]
public class Lingjie_Danfang_Patch
{

    [HarmonyPostfix, HarmonyPatch(typeof(DanFangPanel), "GetFilterData")]
    public static void GetFilterData_Postfix(Dictionary<int, string> __result)
    {
        __result.Add(7, "七品");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(DanFangPanel), MethodType.Constructor, typeof(GameObject))]
    public static void PanelResize(DanFangPanel __instance)
    {
        var transform = Get(__instance, "品阶选择界面/Mask").transform as RectTransform;
        if (transform != null)
            // 品阶选择按钮长度64，但是选择界面是一个pivot=(0.5, 0.5)的列表，直接+64展示不全
            transform.sizeDelta = new Vector2(transform.sizeDelta.x + 128, transform.sizeDelta.y);
    }

    private static GameObject Get(UIBase inst, string path)
    {
        var get = AccessTools.Method(typeof(UIBase), "Get", new[] { typeof(string), typeof(bool) });
        return (GameObject)get.Invoke(inst, new object[] { path, true });
    }

}
