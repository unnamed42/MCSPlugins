using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using script.NewLianDan;
using script.NewLianDan.LianDan;
using Unnamed42.ModPatches.Utils;
using Ventulus;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.预计日期, ModId.灵界, ModId.微风的工具库)]
public class ExpectedDate_Lingjie_Patch
{
    [HarmonyTargetMethod]
    public static MethodBase TargetMethod() =>
        AccessTools.Inner(typeof(ExpectedDate), "LianDanSelect_Patch").GetMethod("UpdateUI_Postfix");

    [HarmonyPrefix]
    public static bool ExpectedDate_LianDan_Replace(LianDanSelect __0)
    {
        // 直接用灵界patch过的方法
        var costTime = LianDanUIMag.Instance.LianDanPanel.GetCostTime(__0.CurNum);
        var match = Regex.Match(costTime, @"^((?<Year>\d+)年)?((?<Month>\d+)月)?((?<Day>\d+)日)?$");
        if (!match.Success)
        {
            PatchPlugin.LogWarning($"未能正确解析炼丹消耗日期：{costTime}");
            return false;
        }
        int y = GetDatePart(match, "Year"), m = GetDatePart(match, "Month"), d = GetDatePart(match, "Day");
        var date = VTools.NowTime.AddDays(y * 365 + m * 30 + d);
        __0.Content.AddText($" 预计日期{date.Year}年{date.Month}月{date.Day}日");
        return false;
    }

    private static int GetDatePart(Match m, string name)
    {
        var g = m.Groups[name];
        if (g == null)
            return 0;
        return int.TryParse(g.Value, out var res) ? res : 0;
    }
}
