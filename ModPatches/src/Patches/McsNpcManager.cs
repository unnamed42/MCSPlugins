using System.Collections.Generic;
using HarmonyLib;
using MCS_NPCManager;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[HarmonyPatch(typeof(MainWindow))]
[ModDependency(ModId.NPC属性修改)]
public class McsNpcManager_Patch {

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MainWindow.Start))]
    public static void Start_Postfix(List<string> ___levelNames) {
        ___levelNames.Add("炼虚初期");
        ___levelNames.Add("炼虚中期");
        ___levelNames.Add("炼虚后期");
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MainWindow.WindowFunc))]
    public static bool WindowFunc_Prefix(ref int ___currentNpcId, ref JSONObject ___currentNpc, ref JSONObject ___currentNpcRandom) {
        var dead = NpcJieSuanManager.inst.IsDeath(___currentNpcId);
        if(dead && ___currentNpc != null) {
            ___currentNpcId = 0;
            ___currentNpc = null;
            ___currentNpcRandom = null;
        }
        return true;
    }
}
