using System;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ventulus;
using JSONClass;
using UnityEngine;
using UniqueCream.MCSWorldExpand;
using UniqueCream.MCSWorldExpand.DataClass.NpcExpand;
using UniqueCream.MCSWorldExpand.NpcExpand.Ctr;
using UniqueCream.MCSWorldExpand.NpcExpand.Patch.NoteBookPatch;
using UniqueCream.MCSWorldExpand.DataClass.ItemXiShou;
using Unnamed42.ModPatches.Utils;
using UniqueCream.MCSWorldExpand.NpcExpand;
using System.Linq;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.更多NPC信息, ModId.世界拓展)]
public class MoreNpcInfo_McsWorldExpand_Patch
{

    public class MoreNpcInfo_Unpatch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod() =>
            AccessTools.Inner(typeof(MoreNPCInfo), "UINPCEventPanel_Patch").GetMethod("OnPanelShow_Postfix");

        [HarmonyPrefix]
        public static bool Unpatch() => false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ShowPanel), nameof(ShowPanel.UINPCEventPanel_OnPanelShow_PostPatch))]
    public static bool McsWorldExpand_EventPanel_Patch(UINPCEventPanel __0)
    {
        // 代码主体来自 世界拓展
        if (NPCEx.GetFavor(UINPCJiaoHu.Inst.NowJiaoHuNPC.ID) < 80)
        {
            __0.ContentRT.DestoryAllChild();
            UnityEngine.Object.Instantiate(__0.SVItemPrefab, __0.ContentRT).GetComponent<UINPCEventSVItem>().SetEvent("", "好感度达到80可以查看Npc事件");
        }
        else // 唯一区别在此
            MoreNPCInfo_NaiYaoInfo(__0);

        if (NPCEx.GetFavor(UINPCJiaoHu.Inst.NowJiaoHuNPC.ID) < 200)
        {
            GameObject gameObject = UnityEngine.Object.Instantiate(__0.SVItemPrefab, __0.ContentRT);
            gameObject.GetComponent<UINPCEventSVItem>().SetEvent("", "好感度达到200可以查看Npc天赋");
            gameObject.transform.SetAsFirstSibling();
        }
        else
        {
            try
            {
                var npcExpandData = NpcExpandControl.Instance.GetNpcExpandData(UINPCJiaoHu.Inst.NowJiaoHuNPC.ID);
                var stringBuilder = new StringBuilder();
                for (int index = 0; index < npcExpandData.TianFuData.TianFuID.Count; ++index)
                {
                    int key = npcExpandData.TianFuData.TianFuID[index];
                    stringBuilder.Append(string.Format("{0}:{1}\n", index + 1, CreateAvatarJsonData.DataDict[key].Title));
                }
                GameObject gameObject = UnityEngine.Object.Instantiate(__0.SVItemPrefab, __0.ContentRT);
                gameObject.GetComponent<UINPCEventSVItem>().SetEvent("Npc天赋\n等级：" + NpcEnhancedJsonData.DataDict[npcExpandData.BaseInfo.ZiZhiLevel].Title + "\n", stringBuilder.ToString());
                gameObject.transform.SetAsFirstSibling();
            }
            catch (Exception ex)
            {
                Main.LogError("解析UI显示Npc天赋列表出错，错误信息：" + ex?.ToString());
            }
        }
        return false;
    }

    // 从UniqueCream.MCSWorldExpand.ItemExpand.LianHua.NpcEffect.NpcEffects里找
    public static readonly Dictionary<int, string> 效果 = new()
    {
        // {24, "战斗"},
        // {21,"玩家buff"},
        {25,"天赋药"},
        {23,"悟道点药"},
        {26,"悟道经验药"},
        // {19,"年龄"},
        {18,"丹毒药"},
        {5,"丹田上限药"},
        {4,"丹田药"},
        {15,"遁速药"},
        {10,"修为药"},
        {13,"悟性药"},
        {12,"血量上限药"},
        {11,"血量药"},
        {7,"经脉上限药"},
        {6,"经脉药"},
        // {17,"灵感"},
        {8,"灵根资质药"},
        {1,"灵力药"},
        // {413,"满值药"},
        {14,"神识药"},
        {22,"寿元药"},
        // {13,"五行"},
        {16,"心境药"},
        {3,"血气上限药"},
        {2,"血气药"},
        // {9,"阴阳"},
        {20,"资质药"},
        {27,"灵力药"}, // 随机灵力
    };

    private static readonly Dictionary<int, string> 原版效果 = new()
    {
      { 0, "其他药" },
      { 4, "修为药" },
      { 5, "神识药" },
      { 6, "气血药" },
      { 7, "寿元药" },
      { 9, "资质药" },
      { 10, "悟性药" },
      { 11, "遁速药" },
      { 25, "悟道经验药" },
      { 26, "悟道点药" },
      { 37, "避劫丹" }
    };

    // 以下代码修改自 更多NPC信息
    private static void SetVanillaUseCount(UINPCData npc, Dictionary<string, string> usedInfo)
    {
        var usedItem = npc?.json?["useItem"];
        if (usedItem?.IsNull == true) return;
        var hasNaiYao = npc.json["wuDaoSkillList"].ToList().Contains(2131);
        foreach (var itemId in usedItem.keys)
        {
            if (!_ItemJsonData.DataDict.TryGetValue(int.Parse(itemId), out var item))
            {
                PatchPlugin.LogWarning($"未能找到物品{itemId}信息");
                continue;
            }
            var canUse = item.CanUse * (hasNaiYao ? 2 : 1);
            if (canUse > 0)
            {
                var seid = item.seid.ElementAtOrDefault(itemId == "5523" ? 1 : 0);
                var effectName = 原版效果.Get(seid, "未知药");
                var content = $"{item.name}({usedItem[itemId].I}/{canUse})  ";
                if (usedInfo.ContainsKey(effectName))
                    usedInfo[effectName] += content;
                else
                    usedInfo.Add(effectName, content);
            }
        }
    }

    private static void SetWorldExpandUseCount(NpcExpandData npc, Dictionary<string, string> usedInfo)
    {
        var xiShouItems = npc?.XiShouItemCountDic;
        if (xiShouItems?.Count == 0) return;
        foreach (var (itemId, used) in xiShouItems)
        {
            if (!ItemXiShouJsonData.DataDict.TryGetValue(itemId, out var item))
            {
                PatchPlugin.LogWarning($"未能找到世界拓展物品{itemId}信息");
                continue;
            }
            var effectName = item.Effect.FirstIn(效果) ?? "未知药";
            var canUse = npc.GetCanLianHuaCount(itemId) + used;
            var content = $"{item.Name}({used}/{canUse})  ";
            if (usedInfo.ContainsKey(effectName))
                usedInfo[effectName] += content;
            else
                usedInfo.Add(effectName, content);
        }
    }

    public static void MoreNPCInfo_NaiYaoInfo(UINPCEventPanel __instance)
    {
        if (!MoreNPCInfo.ShowNaiYaoInfo.Value)
            return;
        var usedInfo = new Dictionary<string, string>();
        var npc = UINPCJiaoHu.Inst.InfoPanel.npc;
        var npcExpandData = NpcExpandControl.Instance.GetNpcExpandData(npc.ID);
        SetVanillaUseCount(npc, usedInfo);
        SetWorldExpandUseCount(npcExpandData, usedInfo);
        if (usedInfo.Count > 0)
        {
            foreach (var (type, content) in usedInfo)
            {
                Transform transform = UnityEngine.Object.Instantiate(__instance.SVItemPrefab, __instance.ContentRT).transform;
                transform.name = "NaiYao";
                transform.SetAsFirstSibling();
                transform.GetComponent<UINPCEventSVItem>().SetEvent(type, content);
            }
        }
    }
}
