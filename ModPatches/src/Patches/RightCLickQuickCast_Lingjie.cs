using HarmonyLib;
using RightClickQuickCast;
using Unnamed42.ModPatches.Utils;
using YSGame.Fight;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.灵界, ModId.埋久工具库)]
public class RightClickQuickCast_Lingjie_Patch
{

    [HarmonyPrefix]
    [HarmonyPatch(typeof(QuickCast), nameof(QuickCast.ClickSkillPrefix))]
    public static bool ClickSkillPrefix_Patch(UIFightSkillItem __0, ref bool __result, out int __state)
    {
        __state = 0;
        __result = true;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(QuickCast), nameof(QuickCast.ClickSkillPostfix))]
    public static bool ClickSkillPostfix_Patch(UIFightSkillItem __0, int __state)
    {
        return false;
    }

}
