using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using QXScene.MCS;
using QXScene.MCS.MYSceneManager;
using TierneyJohn.MiChangSheng.JTools.Manager;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.七星起源, ModId.JTools)]
public class QXScene_Patch
{

    [HarmonyTranspiler, HarmonyPatch(typeof(QXSceneManager), "Start")]
    public static IEnumerable<CodeInstruction> Start_Patch(IEnumerable<CodeInstruction> ins)
    {
        var codes = ins.ToList();
        var createSceneCall = SymbolExtensions.GetMethodInfo(() => ((QXSceneManager)null).CreateScene());
        var createSceneIdx = codes.FindIndex((a, b) =>
            a.Is(OpCodes.Ldarg_0) && b.Is(OpCodes.Call, createSceneCall));
        if (createSceneIdx == -1)
            return ins;
        // 移除 this.CreateScene()
        // 不能直接patch CreateScene，里面使用的旧版JTools符号没有加载，会触发TypeLoadException
        codes.RemoveRange(createSceneIdx, 2);
        // 替换代码
        codes.InsertRange(createSceneIdx, new List<CodeInstruction> {
            // AssetBundleManager.Inst.SetAssetBundlePatch(typeof(Main), "/AssetBundle/Scene")
            new(OpCodes.Ldsfld, AccessTools.Field(typeof(AssetBundleManager), nameof(AssetBundleManager.Inst))),
            new(OpCodes.Ldtoken, typeof(Main)),
            new(OpCodes.Call, AccessTools.Method(typeof(Type), nameof(Type.GetTypeFromHandle))),
            new(OpCodes.Ldstr, "/AssetBundle/Scene"),
            new(OpCodes.Callvirt, AccessTools.Method(typeof(AssetBundleManager), nameof(AssetBundleManager.SetAssetBundlePatch), new[] {typeof(Type), typeof(string)})),
            // this.sceneNames = AssetBundleManager.Inst.CreateAllScene()
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldsfld, AccessTools.Field(typeof(AssetBundleManager), nameof(AssetBundleManager.Inst))),
            new(OpCodes.Callvirt, AccessTools.Method(typeof(AssetBundleManager), nameof(AssetBundleManager.CreateAllScene))),
            new(OpCodes.Stfld, AccessTools.Field(typeof(QXSceneManager), "sceneNames")),
        });
        PatchPlugin.LogInfo("已修补七星起源-JTools");
        return codes;
    }
}
