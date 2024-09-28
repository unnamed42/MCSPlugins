using System;
using System.Text;
using System.Collections.Generic;
using System.Reflection.Emit;
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

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.更多NPC信息, ModId.世界拓展)]
public class MoreNpcInfo_McsWorldExpand_Patch
{

    [HarmonyPatch]
    public class MoreNpcInfo_Unpatch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod() =>
            AccessTools.Inner(typeof(MoreNPCInfo), "UINPCEventPanel_Patch").GetMethod("OnPanelShow_Postfix");

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Unpatch(IEnumerable<CodeInstruction> ins)
        {
            PatchPlugin.Logger.LogInfo("注销更多NPC信息-NPC面板");
            return new List<CodeInstruction> { new(OpCodes.Ret) };
        }
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ShowPanel), nameof(ShowPanel.UINPCEventPanel_OnPanelShow_PostPatch))]
    public static IEnumerable<CodeInstruction> McsWorldExpand_Unpatch(IEnumerable<CodeInstruction> ins)
    {
        PatchPlugin.Logger.LogInfo("替换世界拓展-NPC面板");
        return new List<CodeInstruction> {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Call, typeof(MoreNpcInfo_McsWorldExpand_Patch).GetMethod(nameof(OnPanelShow_Patched), BindingFlags.Static|BindingFlags.Public)),
            new(OpCodes.Ret),
        };
    }

    // 从UniqueCream.MCSWorldExpand.ItemExpand.LianHua.NpcEffect.NpcEffects里找就行
    public static Dictionary<int, string> 效果 = new Dictionary<int, string> {
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

    // 以下代码修改自 更多NPC信息
    public static void MoreNPCInfo_NaiYaoInfo(UINPCEventPanel __instance)
    {
        if (!MoreNPCInfo.ShowNaiYaoInfo.Value)
            return;
        var dictionary = new Dictionary<string, string>();
        var npc = UINPCJiaoHu.Inst.InfoPanel.npc;
        var xiShouItems = NpcExpandControl.Instance.GetNpcExpandData(npc.ID)?.XiShouItemCountDic;
        if (xiShouItems?.Count == 0)
            return;
        var hasNaiYao = npc.json["wuDaoSkillList"].ToList().Contains(2131);
        foreach (var pair in xiShouItems)
        {
            var item = ItemXiShouJsonData.DataDict.Get(pair.Key);
            if (item == null)
            {
                PatchPlugin.Logger.LogWarning($"未能找到物品{pair.Key}信息");
                continue;
            }
            var effectName = item.Effect.FirstIn(效果) ?? "未知";
            var content = $"{item.Name}({pair.Value}/{item.NpcCanUse * (hasNaiYao ? 2 : 1)})  ";
            if (dictionary.ContainsKey(effectName))
            {
                dictionary[effectName] += content;
            }
            else
            {
                dictionary[effectName] = content;
            }
        }
        if (dictionary.Count > 0)
        {
            // var DanYaoSeidToCN = AccessTools.FieldRefAccess<Dictionary<int, string>>(typeof(MoreNPCInfo), "DanYaoSeidToCN").Invoke();
            foreach (var pair in dictionary)
            {
                Transform transform = UnityEngine.Object.Instantiate(__instance.SVItemPrefab, __instance.ContentRT).transform;
                transform.name = "NaiYao";
                transform.SetAsFirstSibling();
                transform.GetComponent<UINPCEventSVItem>().SetEvent(pair.Key, pair.Value);
            }
        }
    }

    public static void OnPanelShow_Patched(UINPCEventPanel __instance)
    {

        // 代码主体来自 世界拓展
        if (NPCEx.GetFavor(UINPCJiaoHu.Inst.NowJiaoHuNPC.ID) < 80)
        {
            __instance.ContentRT.DestoryAllChild();
            UnityEngine.Object.Instantiate(__instance.SVItemPrefab, __instance.ContentRT).GetComponent<UINPCEventSVItem>().SetEvent("", "好感度达到80可以查看Npc事件");
        }
        else
            MoreNPCInfo_NaiYaoInfo(__instance);

        if (NPCEx.GetFavor(UINPCJiaoHu.Inst.NowJiaoHuNPC.ID) < 200)
        {
            GameObject gameObject = UnityEngine.Object.Instantiate(__instance.SVItemPrefab, __instance.ContentRT);
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
                GameObject gameObject = UnityEngine.Object.Instantiate(__instance.SVItemPrefab, __instance.ContentRT);
                gameObject.GetComponent<UINPCEventSVItem>().SetEvent("Npc天赋\n等级：" + NpcEnhancedJsonData.DataDict[npcExpandData.BaseInfo.ZiZhiLevel].Title + "\n", stringBuilder.ToString());
                gameObject.transform.SetAsFirstSibling();
            }
            catch (Exception ex)
            {
                Main.LogError("解析UI显示Npc天赋列表出错，错误信息：" + ex?.ToString());
            }
        }
    }
}
