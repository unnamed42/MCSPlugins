using System;
using System.Linq;
using HarmonyLib;
using Unnamed42.ModPatches.Utils;
using XYModLib;
using YSGame.Fight;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.埋久工具库, ModId.宵夜工具库, ModId.宵夜修改器)]
public class MCSCheat_ElementalMastery_Patch
{

    private static bool executed = false;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainUIMag), "Start")]
    public static void Start_Postfix()
    {
        if (executed) return;
        if (ElementalMastery.ElementalMastery.MAX == (int)LingQiType.Count)
        {
            PatchPlugin.Logger.LogInfo("未注册新类型灵气，跳过补丁");
            return;
        }
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .Where(asm => asm.GetName().Name == "MCSCheat" && asm.GetType("MCSCheat.MCSCheat") != null)
            .FirstOrDefault();
        var patchTarget = AccessTools.Method(assembly.GetType("MCSCheat.PageFight"), "ChangeLingQiGUI");
        var patch = AccessTools.Method(typeof(MCSCheat_ElementalMastery_Patch), nameof(ChangeLingQiGUI_Prefix));
        new Harmony($"Unnamed42.ModPatches.{nameof(MCSCheat_ElementalMastery_Patch)}")
            .Patch(patchTarget, prefix: new HarmonyMethod(patch));
        PatchPlugin.Logger.LogInfo("已修补修改器战斗界面-灵气");
        executed = true;
    }

    public static bool ChangeLingQiGUI_Prefix(EnumSelectGUI ___lingQiSelectGUI)
    {
        var names = new Traverse(___lingQiSelectGUI).Field("EnumNames");
        var currValue = names.GetValue<string[]>();
        if (currValue.Length != 7) // 全部 金 木 水 火 土 魔
        {
            return true;
        }
        var max = ElementalMastery.ElementalMastery.MAX;
        var newNames = currValue.ToList();
        var elements = ElementalMastery.ElementalMastery.Inst.Elements;
        for (int i = (int)LingQiType.Count; i < max; i++)
        {
            newNames.Add(elements[i - (int)LingQiType.Count].name);
        }
        names.SetValue(newNames.ToArray());
        return true;
    }

}
