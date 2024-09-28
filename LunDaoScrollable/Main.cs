using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Unnamed42.LunDaoScroll;

[BepInPlugin("Unnamed42.LunDaoScroll", "滚动论道条", "0.0.1")]
public class LunDaoScroll : BaseUnityPlugin
{
    private static LunDaoScroll instance;

    internal static new ManualLogSource Logger;

    private void Awake()
    {
        // Plugin startup logic
        instance = this;
        Harmony.CreateAndPatchAll(typeof(LunDaoScroll));
        Logger = base.Logger;
        Logger.LogInfo($"Plugin 滚动论道条 is loaded!");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(LunDaoPanel), nameof(LunDaoPanel.Show))]
    public static bool Show_Prefix()
    {
        var panel = LunDaoManager.inst.lunDaoPanel;
        var lunDaoQiuList = panel.transform.Find("LunDaoQiuList") as RectTransform;
        if (lunDaoQiuList != null)
        {
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.vertical = false;
            scroll.content = lunDaoQiuList;
        }
        return true;
    }
}
