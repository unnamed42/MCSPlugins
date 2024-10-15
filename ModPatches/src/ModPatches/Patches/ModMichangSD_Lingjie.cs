using System.Reflection;
using HarmonyLib;
using ModMichangSD;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.新交易助手, ModId.灵界)]
public class ModMichangSD_Lingjie_Patch
{

    public class MixedTrade_Patch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PluginEntry).Assembly.GetType("ModMichangSD.MixedTrade"), "Start");

        [HarmonyPrefix]
        public static bool Start_Prefix()
        {
            // 38, 39 灵界新增的素材类型
            foreach (var type in new[] { 38, 39 })
            {
                PluginEntry.instance.m_toggleType.AddIfAbsent(type, false);
                PluginEntry.instance.m_toggleQual.AddIfAbsent(type, false);
                PluginEntry.instance.m_toggleWuxi.AddIfAbsent(type, true);
            }
            return true;
        }
    }
}
