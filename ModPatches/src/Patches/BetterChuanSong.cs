using System.Collections.Generic;
using HarmonyLib;
using Unnamed42.ModPatches.Utils;
using Ventulus;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.更好的传送, ModId.灵界)]
public class BetterChuanSong_Lingjie_Patch
{

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BetterChuansong), "UpdateCanWarpSceneNameList")]
    public static void AddLingjieNode(List<string> ___CanWarpSceneNameList)
    {
        ___CanWarpSceneNameList.Add("天阳城");
    }

    public static void Finalizer (System.Exception __exception) {
        var trace = new System.Diagnostics.StackTrace(__exception, true);
        var frame = trace.GetFrame(0);
        UnityExplorer.ExplorerCore.Log($"{frame.GetFileName()}{frame.GetFileLineNumber()}");
    }

}
