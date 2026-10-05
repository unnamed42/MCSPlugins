using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HarmonyLib.Public.Patching;

namespace McsMCP.Server
{
    /// <summary>
    /// Reflection helpers over Harmony's patch bookkeeping.
    ///
    /// The reason this exists rather than plain Harmony calls: the piece of information that
    /// actually answers "is my patch going to fire?" is the patcher kind and its IsValid flag, and
    /// both live on types that are not part of the public surface.
    ///
    ///  - <c>HarmonyLib.Public.Patching.MethodPatcher</c> is public but has NO IsValid member; the
    ///    flag is declared on the concrete subclass.
    ///  - For IL2CPP the concrete subclass is
    ///    <c>Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher</c>, which is <c>internal</c>,
    ///    and its <c>IsValid</c> is an <c>internal</c> property.
    ///
    /// So everything below goes through reflection deliberately, and the code is written to degrade
    /// to "unknown" rather than throw when the game's Harmony/Il2CppInterop version does not match
    /// what was compiled against.
    /// </summary>
    public static class PatchIntrospection
    {
        private static readonly string[] PatcherTypeNames =
        {
            "Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher",
            "HarmonyLib.Public.Patching.NativeDetourMethodPatcher",
            "HarmonyLib.Public.Patching.ManagedMethodPatcher"
        };

        /// <summary>
        /// Describes the runtime patch state of a single method.
        /// </summary>
        public sealed class MethodPatchState
        {
            public string TargetType { get; set; }
            public string TargetMethod { get; set; }
            public string TargetSignature { get; set; }
            public string TargetIlAddress { get; set; }
            public string PatcherType { get; set; }
            public bool? PatcherIsValid { get; set; }
            public List<PatchEntry> Prefixes { get; set; } = new List<PatchEntry>();
            public List<PatchEntry> Postfixes { get; set; } = new List<PatchEntry>();
            public List<PatchEntry> Transpilers { get; set; } = new List<PatchEntry>();
            public List<PatchEntry> Finalizers { get; set; } = new List<PatchEntry>();
            public List<PatchEntry> IlManipulators { get; set; } = new List<PatchEntry>();
            public string EntryBytes { get; set; }
            public List<string> Notes { get; set; } = new List<string>();
        }

        public sealed class PatchEntry
        {
            public string Owner { get; set; }
            public int Priority { get; set; }
            public string PatchMethod { get; set; }
            public string DeclaringAssembly { get; set; }
            public bool IsStatic { get; set; }
            public string State { get; set; }
        }

        /// <summary>
        /// Reads the PatchInfo for a method, if it has any patches at all.
        /// </summary>
        public static PatchInfo GetPatchInfo(MethodBase method)
        {
            try
            {
                return method.GetPatchInfo();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Names the patcher Harmony actually selected for this method, and reads IsValid off it.
        ///
        /// This is the datum the whole design hinges on: Il2CppDetourMethodPatcher only sets IsValid
        /// when it successfully located the generated method's NativeMethodInfoPtr_* field. When it
        /// cannot, it leaves IsValid false and logs nothing, Harmony silently falls back to a
        /// managed IL patch, and a patch that "applied successfully" never sees a native call.
        /// </summary>
        public static void DescribePatcher(MethodBase method, MethodPatchState state)
        {
            MethodPatcher patcher = null;
            try
            {
                patcher = method.GetMethodPatcher();
            }
            catch (Exception ex)
            {
                state.Notes.Add($"GetMethodPatcher threw: {ex.GetType().Name}: {ex.Message}");
            }

            if (patcher == null)
            {
                state.PatcherType = null;
                state.Notes.Add("No MethodPatcher resolved; Harmony is using the plain managed path.");
                return;
            }

            var type = patcher.GetType();
            state.PatcherType = type.FullName;

            // IsValid is internal on Il2CppDetourMethodPatcher, so it is not reachable through the
            // public MethodPatcher base type. Walk the concrete type, then its bases.
            var prop = FindProperty(type, "IsValid");
            if (prop != null)
            {
                try
                {
                    state.PatcherIsValid = prop.GetValue(patcher) as bool?;
                }
                catch (Exception ex)
                {
                    state.Notes.Add($"IsValid getter threw: {ex.GetType().Name}");
                }
            }
            else
            {
                // Not every patcher has the flag; ManagedMethodPatcher legitimately does not.
                state.PatcherIsValid = null;
            }

            if (type.FullName != null && type.FullName.Contains("Il2CppDetourMethodPatcher")
                && state.PatcherIsValid == false)
            {
                state.Notes.Add(
                    "Il2CppDetourMethodPatcher.IsValid is FALSE: this is the dangerous case. The native "
                    + "detour was NOT installed, so native callers bypass the patch entirely while "
                    + "Harmony still reports the patch as applied. The usual cause is that the generated "
                    + "method has no NativeMethodInfoPtr_* field to hook.");
            }
        }

        /// <summary>
        /// Reads the method's entry bytes as they exist at runtime.
        ///
        /// ★ REWRITTEN FOR MONO. The MelonMCP original resolved an IL2CPP proxy's
        /// NativeMethodInfoPtr_* field, followed it to the Il2CppMethodInfo, and read methodPointer
        /// out of offset 0 to find the native entry - because on IL2CPP the managed MethodBase is a
        /// wrapper around code the CLR never runs, and the interesting question was whether
        /// Il2CppInterop's ff 25 detour was installed there.
        ///
        /// None of that exists on Mono, and reimplementing it would be actively misleading:
        ///
        ///  1. There is no Il2CppMethodInfo and no generated NativeMethodInfoPtr_* fields. The old
        ///     lookup would report "no NativeMethodInfoPtr_* field ... not an IL2CPP proxy method"
        ///     for EVERY method, which reads like a detection failure rather than "wrong runtime".
        ///
        ///  2. More importantly, the bytes at the native entry are the WRONG THING TO LOOK AT here.
        ///     HarmonyX on Mono patches the managed method body at the IL level (or installs a
        ///     MonoMod detour), and "is my patch attached" is answered authoritatively by
        ///     PatchInfo - which this class already reads in full. Reporting entry bytes would
        ///     invite exactly the false conclusion the IL2CPP version was written to prevent, just
        ///     inverted: seeing an unmodified prologue would look like "not patched" on a method
        ///     whose Harmony patch is attached and working.
        ///
        /// So this returns null with a reason that says why, and the tool reports patcher/patch
        /// information without an entryBytes field. Callers that print this already treat null as
        /// "unknown" rather than as "not patched".
        /// </summary>
        public static string ReadEntryBytes(MethodBase method, int count, out long entryAddress, out string how)
        {
            entryAddress = 0;
            how = "Entry-byte inspection is IL2CPP-specific and is not available on Mono. "
                + "Under Mono, Harmony patches managed method bodies, so the native entry does not "
                + "change when a patch is applied - the patch list below is the authoritative answer. "
                + "(The disasm / read_mem / resolve_jump tools were removed in this port for the same "
                + "reason.)";
            return null;
        }

        /// <summary>
        /// Every patched method in the process, including other mods'. Enumerating this is the only
        /// reliable way to answer "how many patches are on this method".
        /// </summary>
        public static IEnumerable<MethodBase> GetAllPatchedMethods()
        {
            try
            {
                // PatchProcessor is where the enumerator actually lives; Harmony.GetAllPatchedMethods
                // is only a thin passthrough and is not resolvable from every 0Harmony build.
                return PatchProcessor.GetAllPatchedMethods().ToList();
            }
            catch
            {
                return Enumerable.Empty<MethodBase>();
            }
        }

        public static List<PatchEntry> ToEntries(Patch[] patches)
        {
            var list = new List<PatchEntry>();
            if (patches == null) return list;

            foreach (var p in patches)
            {
                var entry = new PatchEntry
                {
                    Owner = p.owner,
                    Priority = p.priority
                };

                try
                {
                    var mi = p.PatchMethod;
                    if (mi != null)
                    {
                        entry.PatchMethod = DescribeMethod(mi);
                        entry.IsStatic = mi.IsStatic;
                        entry.DeclaringAssembly = mi.DeclaringType?.Assembly?.GetName()?.Name;
                    }
                }
                catch (Exception ex)
                {
                    entry.PatchMethod = $"<unresolved: {ex.GetType().Name}>";
                }

                list.Add(entry);
            }

            return list;
        }

        /// <summary>
        /// Human-readable "ReturnType Type.Method(paramType paramName, ...)" including parameter
        /// names, because Harmony binds prefixes by parameter name and a wrong name silently
        /// produces a no-op patch.
        /// </summary>
        public static string DescribeMethod(MethodBase method)
        {
            if (method == null) return "<null>";

            try
            {
                var pars = string.Join(", ", method.GetParameters()
                    .Select(p => $"{SimpleName(p.ParameterType)} {p.Name}"));

                var ret = method is MethodInfo mi ? SimpleName(mi.ReturnType) : (method is ConstructorInfo ? "ctor" : "?");
                var decl = method.DeclaringType != null ? SimpleName(method.DeclaringType) + "." : "";
                return $"{ret} {decl}{method.Name}({pars})";
            }
            catch
            {
                return method.Name;
            }
        }

        public static string SimpleName(Type t)
        {
            if (t == null) return "?";

            if (t.IsByRef) return SimpleName(t.GetElementType()) + "&";
            if (t.IsArray) return SimpleName(t.GetElementType()) + "[]";

            if (t.IsGenericType)
            {
                var name = t.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0) name = name.Substring(0, tick);
                return name + "<" + string.Join(", ", t.GetGenericArguments().Select(SimpleName)) + ">";
            }

            return t.Name;
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            for (var t = type; t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(name, flags);
                if (prop != null) return prop;
            }
            return null;
        }

        /// <summary>
        /// Formats a MethodBase as "full type name::method name" for matching against tool args.
        /// </summary>
        public static string FullTargetName(MethodBase method)
        {
            if (method == null) return null;
            var decl = method.DeclaringType?.FullName;
            return decl == null ? method.Name : $"{decl}.{method.Name}";
        }

    }
}
