using System;
using System.Collections.Generic;
using System.Linq;

namespace Unnamed42.ModPatches.Utils;

public enum ModId : long
{
    灵界 = 2968233692, // Lingjie
    更多NPC信息 = 2929474347, // MoreNpcInfo
    NPC属性修改 = 2902348517, // NpcManager
    更好的传送 = 2897451176, // BetterChuanSong
    世界拓展 = 2838215105, // MCSWorldExpand
    宵夜修改器 = 2825114586, // MCSCheat
    预计日期 = 2893246439, // ExpectedDate
    微风的工具库 = 2946826370, // 预计日期前置
    新交易助手 = 3282436282, // ModMichangSD 新上传的交易助手
    埋久工具库 = 3018994631, // 需要修补ElementalMastery
    右键施法 = 2916366424, // RightClickQuickCast
    UnityExplorer = 2824615654,
    魔气作为同系灵气 = 2927207638,
    宵夜工具库 = 2824332242, // XYModLib
}

public static class ModIdMethods
{
    private static Dictionary<ModId, bool> subscribed;

    static ModIdMethods()
    {
        subscribed = new Dictionary<ModId, bool>();
        var subscribedMods = WorkshopTool.GetAllModDirectory().Select(mod => mod.Name).ToList();
        foreach (var value in (ModId[])Enum.GetValues(typeof(ModId)))
        {
            var modId = ((long)value).ToString();
            subscribed.Add(value, subscribedMods.Contains(modId) && IsActive(modId));
        }
    }

    private static bool IsActive(string modId) =>
        WorkshopTool.GetAllModDirectory()
            .Where(dir => dir.Name == modId)
            .Any(mod => !WorkshopTool.CheckModIsDisable(modId));

    public static bool IsActive(this ModId id) =>
        subscribed.TryGetValue(id, out var value) && value;

    public static (string, bool) CanApplyPatch(Type type)
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

[AttributeUsage(AttributeTargets.Class)]
class ModDependency : Attribute
{
    public ModId[] Requires { get; private set; }

    public ModDependency(params ModId[] modIds)
    {
        Requires = modIds;
    }
}

[AttributeUsage(AttributeTargets.Class)]
class ModConflicts : Attribute
{
    public ModId[] Conflicts { get; private set; }

    public ModConflicts(params ModId[] modIds)
    {
        Conflicts = modIds;
    }
}
