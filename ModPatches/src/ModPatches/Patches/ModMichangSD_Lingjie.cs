using System.Reflection;
using HarmonyLib;
using ModMichangSD;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.新交易助手, ModId.灵界)]
public class ModMichangSD_Lingjie_Patch
{

    [HarmonyTargetMethod]
    public static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(PluginEntry).Assembly.GetType("ModMichangSD.MixedTrade"), "Start");

    [HarmonyPrefix]
    public static bool Start_Prefix()
    {
        // 38, 39 灵界新增的素材类型
        PluginEntry.instance.m_toggleWuxi.AddIfAbsent(38, true);
        PluginEntry.instance.m_toggleWuxi.AddIfAbsent(39, true);
        return true;
    }

}
