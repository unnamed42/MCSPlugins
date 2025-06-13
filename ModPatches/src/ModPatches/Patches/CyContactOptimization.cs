using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.Events;
using UnityEngine.UI;
using Unnamed42.ModPatches.Utils;
using Ventulus;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.传音符联系人优化, ModId.微风的工具库)]
public class CyContactOptimization_Patch
{
    public class CyContactOptimization_UpdateStatePatch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod() =>
            AccessTools.Inner(typeof(CyContactOptimization), "CyFriendCell_Patch").GetMethod("updateState_Postfix");

        [HarmonyPrefix]
        public static bool MoreButton_updateState(CyFriendCell __0)
        {
            var talkBtn = __0.transform.Find("对话");
            if (talkBtn)
                talkBtn.gameObject.SetActive(__0.isSelect && !__0.isDeath && !__0.IsFly);
            return true;
        }
    }

    [HarmonyTargetMethod]
    public static MethodBase TargetMethod() =>
            AccessTools.Inner(typeof(CyContactOptimization), "CyFriendCell_Patch").GetMethod("Init_Postfix");

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> InsertButton(IEnumerable<CodeInstruction> ins, ILGenerator gen)
    {
        var codes = ins.ToList();
        var startPos = codes.FindIndex((a, b) => a.Is(OpCodes.Ldloc_0) && b.Is(OpCodes.Ldstr, "查看"));
        if (startPos == -1)
        {
            PatchPlugin.LogError("未能找到查看按钮代码地址，跳过");
            return ins;
        }
        var addListener = SymbolExtensions.GetMethodInfo(() => ((UnityEvent)null).AddListener(null));
        var endPos = codes.FindIndex(startPos + 1, a => a.Calls(addListener));
        if (endPos == -1)
        {
            PatchPlugin.LogError("未能找到查看按钮代码地址，跳过");
            return ins;
        }

        codes.InsertRange(startPos, new[] {
            // if (PatchPlugin.Instance.Enable_CyContactOptimization_MoreButton.Value) { 替换查看按钮为对话 } else { 使用原本逻辑 }
            CodeInstruction.LoadField(typeof(PatchPlugin), nameof(PatchPlugin.Instance)),
            CodeInstruction.LoadField(typeof(PatchPlugin), nameof(PatchPlugin.Instance.Enable_CyContactOptimization_MoreButton)),
            new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(PatchPlugin.Instance.Enable_CyContactOptimization_MoreButton.GetType(), "Value")),
            new CodeInstruction(OpCodes.Brfalse, gen.DefineLabel().Also(it => codes[startPos].labels.Add(it))),
            // var talkBtn = MakeNewBiaoQian("对话")
            new CodeInstruction(OpCodes.Ldloc_0),
            new CodeInstruction(OpCodes.Ldstr, "对话"),
            codes[startPos+2].Clone(), // MakeNewBiaoQian是local method，神仙写法，生怕被hack了
            // SetupTalkButton(talkBtn, npcID)
            new CodeInstruction(OpCodes.Ldarg_1), // npcID
            CodeInstruction.Call(() => SetupTalkButton(null, 0)),
            new CodeInstruction(OpCodes.Br, gen.DefineLabel().Also(it => codes[endPos+1].labels.Add(it))),
        });
        return codes;
    }

    public static void SetupTalkButton(UnityEngine.GameObject talkBtn, int npcID)
    {
        talkBtn.GetComponent<BtnCell>().mouseUp.AddListener(() => ClickTalk(npcID));
    }

    public static UnityAction ClickTalk(int npcID)
    {
        if (NpcJieSuanManager.inst.IsDeath(npcID) || NpcJieSuanManager.inst.IsFly(npcID))
            return null;
        var logger = new Traverse(CyContactOptimization.Instance).Property("Logger").GetValue<ManualLogSource>();
        logger.LogInfo("对话按钮被点击了" + VTools.MakeNPCIdStr(npcID));
        var npc = new UINPCData(npcID);
        npc.RefreshData();
        UINPCJiaoHu.Inst.HideJiaoHuPop();
        UINPCJiaoHu.Inst.NowJiaoHuNPC = npc;
        new Traverse(CyContactOptimization.Instance).Field("scrollPosition").SetValue(CyUIMag.inst.npcList.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition);
        CyUIMag.inst.Close();
        UINPCJiaoHu.Inst.ShowJiaoHuPop();
        new Traverse(CyContactOptimization.Instance).Field("cyOpenInfoPanel").SetValue(npcID);
        logger.LogInfo(new Traverse(CyContactOptimization.Instance).Field("cyOpenInfoPanel").GetValue());
        return null;
    }
}
