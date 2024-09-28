// using System.Collections.Generic;
// using HarmonyLib;
// using Ventulus;

// namespace Unnamed42.ModPatches.Patches;

// [ModDependency(ModId.更好的传送, ModId.灵界)]
// public class BetterChuanSong_Lingjie_Patch
// {

//     [HarmonyPrefix]
//     [HarmonyPatch(typeof(BetterChuansong), "Start")]
//     public static bool Start_Prefix(
//         object ___DataList, // List<BetterChuansong.IdShiliScene>
//         object ___Dongfu2Data, // List<BetterChuansong.Dongfu2Pos>
//         List<string> ___NewNingzhouWarp,
//         Dictionary<string, int> ___SeaSceneToIndex,
//         Dictionary<string, string> ___SeaSceneToSea)
//     {
//         return true;
//     }

// }
