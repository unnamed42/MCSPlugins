using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PaiMai;

namespace Unnamed42.FastPaimai;

[BepInPlugin("Unnamed42.FastPaimai", "极速拍卖", "0.0.1")]
public class FastPaimai : BaseUnityPlugin
{
    private static FastPaimai instance;

    internal static new ManualLogSource Logger;

    internal ConfigEntry<float> Wait_Time;

    public void Awake()
    {
        instance = this;
        Logger = base.Logger;
        InitConfig();
        Harmony.CreateAndPatchAll(typeof(FastPaimai));
        Logger.LogInfo($"Plugin 极速拍卖 is loaded!");
    }

    private void InitConfig()
    {
        Wait_Time = Config.Bind("极速拍卖", "发言等待时间", 0.01f, "npc拍卖间隔等待时间");
    }

    [HarmonyPatch(typeof(AvatarCtr), "AvatarAddPrice"), HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> AddAvatarPrice_Patch(IEnumerable<CodeInstruction> ins)
    {
        foreach (var code in ins)
        {
            if (code.Is(OpCodes.Ldc_R4, 0.75f))
                yield return new CodeInstruction(OpCodes.Ldc_R4, FastPaimai.instance.Wait_Time.Value);
            else
                yield return code;
        }
    }
}
