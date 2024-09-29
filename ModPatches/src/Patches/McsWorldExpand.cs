using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using JSONClass;
using UniqueCream.MCSWorldExpand.AvatarExpand;
using UniqueCream.MCSWorldExpand.BattleExpand.Patch;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.世界拓展)]
public class McsWorldExpand_Patch
{

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(UToolTipSkillTriggerPatch), nameof(UToolTipSkillTriggerPatch.OnPointerEnter_Patch))]
    public static IEnumerable<CodeInstruction> PatchToolTip(IEnumerable<CodeInstruction> ins)
    {
        var codes = new List<CodeInstruction>(ins);
        // 提示文字拼接的代码起点 string text = strings.Format("经脉：{0}/{1}\n") ...
        var replaceStart = codes.FindIndex(ins => ins.Is(OpCodes.Ldstr, "经脉：{0}/{1}\n"));
        if (replaceStart == -1)
            return codes;
        var skillID = typeof(UTooltipSkillTrigger).GetField("SkillID", BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
        // 提示文字拼接的代码终点 if(__instance.SkillID > 0)
        var replaceEnd = codes.FindIndex(replaceStart, (a, b) =>
            a.Is(OpCodes.Ldarg_0) && b.Is(OpCodes.Ldfld, skillID));
        if (replaceEnd == -1)
            return codes;
        codes.RemoveRange(replaceStart, replaceEnd - replaceStart);
        codes.InsertRange(replaceStart, new List<CodeInstruction>() {
            // avatarData
            new(OpCodes.Ldloc_1),
            // value = GetToopTip(avatarData)
            new(OpCodes.Call, typeof(McsWorldExpand_Patch).GetMethod("GetToolTip", BindingFlags.Public | BindingFlags.Static)),
            // var text = value
            new(OpCodes.Stloc_2),
        });
        PatchPlugin.Logger.LogInfo("已替换世界拓展战斗界面-经脉血气提示");
        return codes;
    }

    public static string GetToolTip(AvatarData avatarData)
    {
        var sb = new StringBuilder();
        if (avatarData.JingMai.Now != 0 && avatarData.JingMai.Now >= avatarData.JingMai.Max * 0.8)
        {
            sb.AppendFormat("经脉：<color=green>{0}</color>/{1}\n", avatarData.JingMai.Now, avatarData.JingMai.Max);
        }
        else
        {
            sb.AppendFormat("经脉：{0}/{1}\n", avatarData.JingMai.Now, avatarData.JingMai.Max);
        }
        sb.AppendFormat("丹田：{0}/{1}\n", avatarData.DanTian.Now, avatarData.DanTian.Max);
        if (avatarData.XueQi.Now != 0 && avatarData.XueQi.Now >= avatarData.XueQi.Max * 0.8)
        {
            sb.AppendFormat("血气：<color=green>{0}</color>/{1}", avatarData.XueQi.Now, avatarData.XueQi.Max);
        }
        else
        {
            sb.AppendFormat("血气：{0}/{1}", avatarData.XueQi.Now, avatarData.XueQi.Max);
        }
        return sb.ToString();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NPCUseItem), nameof(NPCUseItem.UseItem))]
    public static IEnumerable<CodeInstruction> UseItem_OutOfBound_Fix(IEnumerable<CodeInstruction> ins)
    {
        if (!PatchPlugin.Instance.Enable_WorldExpand_OutOfBound_Patch.Value)
        {
            PatchPlugin.Logger.LogInfo("禁用世界拓展-越界异常修复，跳过");
            return ins;
        }
        var codes = ins.ToList();
        var seid = AccessTools.Field(typeof(_ItemJsonData), nameof(_ItemJsonData.seid));
        List<int> _ = null;
        var elementAtOrDefault = SymbolExtensions.GetMethodInfo(() => _.ElementAtOrDefault(0));
        var listGetItem = AccessTools.Method(typeof(List<int>), "get_Item");
        var start = -1;
        do
        {
            // 将 itemJsonData.seid[0 or 1] 替换成 itemJsonData.seid.ElementAtOrDefault(0 or 1)
            // 世界拓展新增的资质丹/属性丹都没有seid，世界拓展用了prefix拦截UseDanYao，这类物品会跳过。
            // 但是函数入参求值 this.UseDanYao(npcID, item, itemJsonData.seid[0]) 不会跳过，导致越界异常
            start = codes.FindIndex(start + 1, (a, b, c) =>
                a.Is(OpCodes.Ldfld, seid) && c.Is(OpCodes.Callvirt, listGetItem));
            if (start != -1)
            {
                codes[start + 2].operand = OpCodes.Call;
                codes[start + 2].operand = elementAtOrDefault;
            }
        } while (start != -1);
        PatchPlugin.Logger.LogInfo("已修补世界拓展-丹药使用");
        return codes;
    }
}
