namespace Unnamed42.ModPatches.Debug;

// 收集一些debug常用代码
// UnityExplorer Console用using不方便，用全称引用

#if false

public static class ExplorerSnippets
{

    public static void GetPatchDllNames()
    {
        var patches = HarmonyLib.Harmony.GetPatchInfo(HarmonyLib.AccessTools.Method(typeof(LianQiResultManager), "addLianQiTime"));
        var lists = new[] { patches.Prefixes, patches.Postfixes, patches.Transpilers };
        var sb = new System.Text.StringBuilder();
        foreach (var p in lists)
        {
            if (p == null || p.Count == 0)
                continue;
            foreach (var p2 in p)
            {
                sb.AppendLine(p2.PatchMethod.Module.FullyQualifiedName);
            }
        }
        sb.ToString();
    }

}

#endif
