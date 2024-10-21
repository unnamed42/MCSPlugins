using System.Collections.Generic;
using HarmonyLib;
using QXScene.MCS;
using QXScene.MCS.MYSceneManager;
using TierneyJohn.MiChangSheng.JTools.Manager;
using UnityEngine.SceneManagement;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.七星起源, ModId.JTools)]
public class QXScene_Patch
{

    // 不能直接patch QXSceneManager.CreateScene，其中引用了被干掉的旧版JTools，无法解析符号，会抛出TypeLoadException
    [HarmonyPrefix, HarmonyPatch(typeof(QXSceneManager), "Start")]
    public static bool Start_Patch(ref List<string> ___sceneNames, QXSceneManager __instance)
    {
        AssetBundleManager.Inst.SetAssetBundlePatch(typeof(Main), QXSceneManager.AssetBundlePath);
#pragma warning disable CS0618 // 类型或成员已过时
        ___sceneNames = AssetBundleManager.Inst.CreateAllScene();
#pragma warning restore CS0618 // 类型或成员已过时
        SceneManager.sceneLoaded += __instance.SceneLoaded;
        return false;
    }
}
