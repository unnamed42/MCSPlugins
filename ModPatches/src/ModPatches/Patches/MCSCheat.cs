using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MCSCheat;
using Unnamed42.ModPatches.Utils;
using XYModLib;

namespace Unnamed42.ModPatches.Patches;

[ModDependency(ModId.宵夜工具库, ModId.宵夜修改器)]
public class MCSCheat_Patch
{
    internal static Assembly cheat;

    [HarmonyTargetMethod]
    public static MethodBase TargetMethod()
    {
        var cheatInit = AccessTools.Method(typeof(CheatLoader), "Init");
        var stateMachine = cheatInit.GetCustomAttribute<AsyncStateMachineAttribute>();
        return AccessTools.Method(stateMachine.StateMachineType, "MoveNext");
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> StatePatch(IEnumerable<CodeInstruction> ins)
    {
        var load = SymbolExtensions.GetMethodInfo(() => ((AppDomain)null).Load((byte[])null));
        var codes = ins.ToList();
        var idx = codes.FindIndex((a, b) => a.Calls(load) && b.IsStLoc_S(10));
        if (idx != -1)
            codes.InsertRange(idx + 2, new[] {
                new CodeInstruction(OpCodes.Ldloc_S, 10),
                CodeInstruction.Call(() => SetupPatches(null)),
            });
        return codes;
    }

    public static void SetupPatches(Assembly cheat)
    {
        MCSCheat_Patch.cheat = cheat;
        var h = new Harmony($"Unnamed42.ModPatches.{nameof(MCSCheat_Patch)}");
        try
        {
            (new Type[] {
                typeof(MCSCheat_ElementalMastery_Patch),
                typeof(MCSCheat_Lingjie_Patch),
                typeof(MCSCheat_WorldExpand_Patch),
            }).ForEach(type =>
            {
                var (tooltip, can) = ModIdMethods.CanApplyPatch(type);
                if (can)
                    type.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { h });
                PatchPlugin.LogInfo($"已应用宵夜修改器补丁{type.Name}");
            });
        }
        catch (Exception e)
        {
            PatchPlugin.LogError(e);
        }
    }

    internal static void AddEnumSelection(EnumSelectGUI gui, IEnumerable<string> options) {
        var name = new Traverse(gui).Field<string[]>("EnumNames");
        var newNames = name.Value.ToList().Also(it => it.AddRange(options));
        name.Value = newNames.ToArray();
    }

    internal static void AddEnumSelection(EnumSelectGUI gui, params string[] options) =>
        AddEnumSelection(gui, options.AsEnumerable());

}
