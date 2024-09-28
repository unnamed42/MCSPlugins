﻿using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unnamed42.ModPatches.Debug;
using Unnamed42.ModPatches.Patches;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches;

[BepInPlugin("Unnamed42.ModPatches", "多mod兼容补丁", "0.0.10")]
public class PatchPlugin : BaseUnityPlugin
{
    public static PatchPlugin Instance;
    internal static new ManualLogSource Logger;

    internal ConfigEntry<bool> Enable_Lingjie_LingQi_Patch;
    internal ConfigEntry<bool> Enable_ElementalMastery_Empty_Slot_Patch;
    internal ConfigEntry<bool> Enable_Debug;

    private PatchPlugin InitConfig()
    {
        this.Enable_Lingjie_LingQi_Patch = Config.Bind("开关", "灵界-roll灵气补丁.测试", false, "【即时生效】埋久工具库将魔气和鬼气也加入同系灵气计算，此补丁将其移除");
        this.Enable_ElementalMastery_Empty_Slot_Patch = Config.Bind("开关", "埋久工具库-灵气空槽", true, "【重启生效】尝试修复异灵气导致左键roll灵气出现空槽问题");
        this.Enable_Debug = Config.Bind("开关", "输出更多debug信息", false, "【重启生效】输出更多debug信息，开发用。依赖于UnityExplorer");
        return this;
    }

    private void Awake()
    {
        Instance = this.InitConfig();
        Logger = base.Logger;
        var patches = new List<Type> {
            typeof(McsNpcManager_Patch),
            typeof(MoreNpcInfo_McsWorldExpand_Patch),
            // typeof(BetterChuanSong_Lingjie_Patch),
            typeof(ExpectedDate_Lingjie_Patch),
            typeof(ModMichangSD_Lingjie_Patch),
            typeof(McsWorldExpand_Patch),
            typeof(Lingjie_LingQi_Patch),
            typeof(ElementalMastery_SetChoiceSkill_Patch),
            // typeof(RightClickQuickCast_Lingjie_Patch),
            typeof(MCSCheat_Patch),
        };
        if (this.Enable_Debug.Value)
        {
            patches.AddRange(new Type[] {
                typeof(SetChoiceSkill_Debug),
            });
        }
        patches.ForEach(type =>
        {
            var (tooltip, canApply) = ModIdMethods.CanApplyPatch(type);
            if (canApply)
            {
                var h = new Harmony($"Unnamed42.ModPatches.{type.Name}");
                type.Also(h.PatchAll).GetNestedTypes().ForEach(h.PatchAll);
            }
            Logger.LogInfo(tooltip);
        });
        Logger.LogInfo($"多mod兼容补丁加载完毕!");
    }
}
