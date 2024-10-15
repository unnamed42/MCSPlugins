using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace Unnamed42.ModPatches.Utils;

public static class TranspilerExtensions
{
    public static bool Is(this CodeInstruction ins, OpCode code) => ins.opcode == code;

    public static bool IsStLoc_S(this CodeInstruction ins, int offset) =>
        ins.opcode == OpCodes.Stloc_S && offset == (ins.operand as LocalBuilder)?.LocalIndex;

    public static bool IsLdLoc_S(this CodeInstruction ins, int offset) =>
        ins.opcode == OpCodes.Ldloc_S && offset == (ins.operand as LocalBuilder)?.LocalIndex;

    public static int FindIndex(this IList<CodeInstruction> l, int start, Func<CodeInstruction, CodeInstruction, bool> pred)
    {
        for (int i = start; i < l.Count - 1; i++)
        {
            if (pred(l[i], l[i + 1])) return i;
        }
        return -1;
    }

    public static int FindIndex(this IList<CodeInstruction> l, int start, Func<CodeInstruction, CodeInstruction, CodeInstruction, bool> pred)
    {
        for (int i = start; i < l.Count - 2; i++)
        {
            if (pred(l[i], l[i + 1], l[i + 2])) return i;
        }
        return -1;
    }

    public static int FindIndex(this IList<CodeInstruction> l, Func<CodeInstruction, CodeInstruction, bool> pred) =>
        FindIndex(l, 0, pred);


    public static int FindIndex(this IList<CodeInstruction> l, Func<CodeInstruction, CodeInstruction, CodeInstruction, bool> pred) =>
        FindIndex(l, 0, pred);

}
