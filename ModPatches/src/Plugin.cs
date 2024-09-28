using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unnamed42.ModPatches.Patches;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches;

[BepInPlugin("Unnamed42.ModPatches", "多mod兼容补丁", "0.0.9")]
public class PatchPlugin : BaseUnityPlugin
{
    public static PatchPlugin Instance;
    internal static new ManualLogSource Logger;

    internal ConfigEntry<bool> Enable_Lingjie_LingQi_Patch;
    internal ConfigEntry<bool> Enable_ElementalMastery_Empty_Slot_Patch;

    private PatchPlugin InitConfig()
    {
        this.Enable_Lingjie_LingQi_Patch = Config.Bind("开关", "灵界-roll灵气补丁.测试", false, "【即时生效】埋久工具库将魔气和鬼气也加入同系灵气计算，此补丁将其移除");
        this.Enable_ElementalMastery_Empty_Slot_Patch = Config.Bind("开关", "埋久工具库-灵气空槽.测试", false, "【重启生效】尝试修复异灵气导致左键roll灵气出现空槽问题");
        return this;
    }

    private void Awake()
    {
        Instance = this.InitConfig();
        Logger = base.Logger;
        (new Type[] {
            typeof(McsNpcManager_Patch),
            typeof(MoreNpcInfo_McsWorldExpand_Patch),
            // typeof(BetterChuanSong_Lingjie_Patch),
            typeof(ExpectedDate_Lingjie_Patch),
            typeof(ModMichangSD_Lingjie_Patch),
            typeof(McsWorldExpand_Patch),
            typeof(Lingjie_LingQi_Patch),
            typeof(ElementalMastery_SetChoiceSkill_Patch),
            // typeof(RightClickQuickCast_Lingjie_Patch),
            typeof(MCSCheat_ElementalMastery_Patch),
        }).ForEach(type =>
        {
            var (tooltip, canApply) = CanApplyPatch(type);
            if (canApply)
            {
                var h = new Harmony($"Unnamed42.ModPatches.{type.Name}");
                type.Also(h.PatchAll).GetNestedTypes().ForEach(h.PatchAll);
            }
            Logger.LogInfo(tooltip);
        });
        Logger.LogInfo($"多mod兼容补丁加载完毕!");
    }

    private static (string, bool) CanApplyPatch(Type type)
    {
        var deps = type.GetCustomAttributes(typeof(ModDependency), false).FirstOrDefault() as ModDependency;
        var conflicts = type.GetCustomAttributes(typeof(ModConflicts), false).FirstOrDefault() as ModConflicts;
        var depsSatisfied = deps?.Requires?.All(mod => mod.IsActive()) == true;
        var hasConflicts = conflicts?.Conflicts?.Any(mod => mod.IsActive()) == true;
        if (depsSatisfied && !hasConflicts)
        {
            return ($"检测到 {FormatMods(deps.Requires)}，已应用补丁 {type.Name}", true);
        }
        var tooltip = depsSatisfied ? "" : $"未满足全部依赖 {FormatMods(deps.Requires)}";
        if (hasConflicts)
        {
            if (string.IsNullOrEmpty(tooltip))
            {
                tooltip = "，";
            }
            tooltip += $"存在冲突mod {FormatMods(conflicts.Conflicts)}";
        }
        return (tooltip + $"，跳过补丁 {type.Name}", false);
    }

    private static string FormatMods(ModId[] mods) =>
        mods?.Select(mod => $"{mod}({(long)mod})").Join(",");
}
