using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unnamed42.ModPatches.Utils;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.宵夜工具库, ModId.宵夜修改器)]
public class MCSCheat_Patch
{

    private static bool executed = false;
    internal static Assembly cheat;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainUIMag), "Start")]
    public static void Start_Postfix()
    {
        if (executed) return;

        cheat = AppDomain.CurrentDomain.GetAssemblies()
            .Where(asm => asm.GetName().Name == "MCSCheat" && asm.GetType("MCSCheat.MCSCheat") != null)
            .FirstOrDefault();
        var h = new Harmony($"Unnamed42.ModPatches.{nameof(MCSCheat_Patch)}");
        (new Type[] {
            typeof(MCSCheat_ElementalMastery_Patch),
            typeof(MCSCheat_Lingjie_Patch),
        }).ForEach(type =>
        {
            var (tooltip, can) = ModIdMethods.CanApplyPatch(type);
            if (can)
                type.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { h });
            PatchPlugin.Logger.LogInfo($"已应用宵夜修改器补丁{type.Name}");
        });
        executed = true;
    }

}
