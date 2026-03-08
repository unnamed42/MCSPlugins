using HarmonyLib;
using MaiJiu.MCS.LingJie.Patch.ScenePatch;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.灵界, ModId.减缓时间流逝)]
public class Lingjie_SlowTime_Patch
{
    [HarmonyPrefix, HarmonyPatch(typeof(ToolsPatch), nameof(ToolsPatch.CalcLingWuOrTuPoTime_Postfix))]
    public static bool LingjieCalcLingWuOrTuPoTimeSkip()
    {
        if (!PatchPlugin.Instance.Enable_Lingjie_SlowTime_Patch.Value)
            return true;
        // 灵界前置加了个跳过，后面跟了个postfix重新计算了突破时间
        return Slow_Time.Slow_Time.CapsLockStatus;
    }
}
