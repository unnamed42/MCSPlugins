using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using McsMCP.Server;
using Newtonsoft.Json.Linq;

namespace McsMCP.Tools
{
    /// <summary>
    /// Reads and writes BepInEx configuration (the .cfg files under BepInEx/config).
    ///
    /// WHY THIS IS NOT "just edit the cfg file": a ConfigFile keeps every entry in memory and
    /// rewrites the whole file from that model on Save(). Editing a .cfg on disk while the game runs
    /// is therefore silently lost the next time any mod (or the user) triggers a save - the
    /// in-memory value wins. These tools go through the live ConfigFile objects so the write lands
    /// in the same model the game is actually using, then explicitly persists it.
    ///
    /// WHY IT DOES NOT DEPEND ON ConfigurationManager: that mod provides a human-facing in-game UI.
    /// An agent cannot open an F1 window, and requiring that mod to be installed would make these
    /// tools useless on any other setup. Everything here is built on BepInEx's own ConfigFile,
    /// which ships with BepInEx itself and is present in every BepInEx game.
    ///
    /// HOW THE LIVE CONFIG FILES ARE FOUND: BepInEx 5 keeps no public registry of ConfigFile
    /// instances - there is no static equivalent of MelonLoader's MelonPreferences.Categories. The
    /// live objects are reachable through Chainloader.Plugins, which returns every loaded
    /// BaseUnityPlugin, each carrying its own Config ConfigFile. That is the authoritative set: a
    /// plugin's config is exactly the file it bound its entries to. Our own ConfigFile is included
    /// by the same path, because McsMCPPlugin is itself one of those plugins.
    ///
    /// WHY THE PLUGIN-GUID IS THE CATEGORY IDENTIFIER: BepInEx sections are free-form strings chosen
    /// by each mod, so "Server" or "General" is ambiguous across mods and cannot address one file.
    /// The entry points below therefore treat `mod` as a plugin GUID (or its filename stem), which
    /// is unique by construction, and report the file's sections as part of each entry.
    ///
    /// WHAT THIS DELIBERATELY DOES NOT DO: judge whether a change takes effect immediately.
    /// Most config values are read as `entry.Value` at the point of use, so they apply at once - but
    /// values consumed once during Awake (typically anything that INSTALLS a native hook, opens a
    /// socket, or applies a Harmony patch) are already baked into the running process and cannot be
    /// changed without a restart. Only the mod itself knows which is which, and guessing would
    /// reproduce the exact trap this project keeps hitting: reading `true` back and assuming it took
    /// effect. So set_config reports the write and says plainly that restart semantics are the
    /// caller's to determine.
    /// </summary>
    internal static class ConfigIntrospection
    {
        /// <summary>
        /// Every loaded plugin paired with its live ConfigFile.
        ///
        /// Resolved lazily and deliberately NOT cached: BepInEx finishes loading plugins over
        /// several frames, so a list captured during our Awake would be missing every mod that
        /// loaded after us. The lookup is a few dozen dictionary reads, which is nothing next to an
        /// MCP round trip.
        /// </summary>
        private static List<KeyValuePair<string, BepInEx.Configuration.ConfigFile>> LoadConfigFiles()
        {
            var result = new List<KeyValuePair<string, BepInEx.Configuration.ConfigFile>>();

            try
            {
                // PluginInfos rather than the obsoleted Chainloader.Plugins (CS0618 in BepInEx
                // 5.4.17). Each PluginInfo carries the live plugin INSTANCE, which is where the
                // ConfigFile hangs off - so this is the same set of objects, reached through the
                // supported accessor.
                foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                {
                    var plugin = info?.Instance;
                    if (plugin == null) continue;

                    var config = plugin.Config;
                    if (config == null) continue;

                    // Prefer the plugin GUID: it is the stable identity an agent sees in list_configs
                    // and in BepInEx's own logs. Fall back to the file stem, which is what a human
                    // would recognise on disk (e.g. "Unnamed42.FastPaimai.cfg" -> "Unnamed42.FastPaimai").
                    var id = info.Metadata?.GUID;
                    if (string.IsNullOrEmpty(id))
                    {
                        try { id = System.IO.Path.GetFileNameWithoutExtension(config.ConfigFilePath); }
                        catch { id = null; }
                    }
                    if (string.IsNullOrEmpty(id)) continue;

                    // A plugin can appear twice if it was loaded twice; first wins and the duplicate
                    // would otherwise make list_configs report the same file under two keys.
                    if (result.Any(kv => string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    result.Add(new KeyValuePair<string, BepInEx.Configuration.ConfigFile>(id, config));
                }
            }
            catch (Exception ex)
            {
                McsMCPPlugin.Log?.LogWarning($"Could not enumerate BepInEx config files: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// BepInEx is required for the mod to be loaded at all, so this is effectively always true.
        /// It stays as a guard because the tool definitions call it before every operation and a
        /// null-deref inside a tool is a much worse failure than a clean "unavailable" message.
        /// </summary>
        public static bool Available => true;

        /// <summary>
        /// All known config files keyed by plugin GUID. This is the CATEGORY list: the shape the
        /// tool definitions were written against is "categories, each with entries", and a BepInEx
        /// plugin maps onto exactly that.
        /// </summary>
        public static IList Categories
        {
            get
            {
                var list = new ArrayList();
                foreach (var kv in LoadConfigFiles()) list.Add(kv);
                return list;
            }
        }

        /// <summary>Resolves the `mod` argument to a config file, or null when nothing matches.</summary>
        public static object FindCategory(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;

            var files = LoadConfigFiles();

            foreach (var kv in files)
            {
                if (string.Equals(kv.Key, identifier, StringComparison.OrdinalIgnoreCase))
                    return kv;
            }

            // Accept the file stem too, so an agent that read "McsMCP.cfg" off disk can address it
            // without knowing the GUID.
            foreach (var kv in files)
            {
                try
                {
                    var stem = System.IO.Path.GetFileNameWithoutExtension(kv.Value.ConfigFilePath);
                    if (string.Equals(stem, identifier, StringComparison.OrdinalIgnoreCase))
                        return kv;
                }
                catch { }
            }

            return null;
        }

        public static string CategoryIdentifier(object category)
        {
            return category is KeyValuePair<string, BepInEx.Configuration.ConfigFile> kv ? kv.Key : null;
        }

        /// <summary>
        /// The ConfigFile behind a category handle. Returned as object so the tool definitions stay
        /// agnostic about the concrete config backend.
        /// </summary>
        private static BepInEx.Configuration.ConfigFile FileOf(object category)
        {
            return category is KeyValuePair<string, BepInEx.Configuration.ConfigFile> kv ? kv.Value : null;
        }

        /// <summary>
        /// Every entry in the file.
        ///
        /// ConfigFile implements IEnumerable&lt;KeyValuePair&lt;ConfigDefinition, ConfigEntryBase&gt;&gt;,
        /// which is the only PUBLIC way to enumerate everything: `Entries` is protected (CS0122) and
        /// GetConfigEntries() is obsoleted (CS0618) in BepInEx 5.4.17. Sorted by Section then Key so
        /// the output is stable across calls - enumeration order is not guaranteed, and an agent
        /// diffing two list_configs results should not see rows shuffle.
        /// </summary>
        public static IList EntriesOf(object category)
        {
            var file = FileOf(category);
            if (file == null) return null;

            try
            {
                var list = new List<object>();
                foreach (var kv in file) list.Add(kv.Value);

                list.Sort(CompareEntries);
                return new ArrayList(list);
            }
            catch (Exception ex)
            {
                McsMCPPlugin.Log?.LogWarning($"Could not read entries from a config file: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Orders entries by "Section.Key". A named method rather than a lambda because ArrayList's
        /// Sort takes an IComparer, not a Comparison delegate (CS1660), and this keeps the ordering
        /// rule in one place.
        /// </summary>
        private static int CompareEntries(object a, object b)
            => string.CompareOrdinal(
                   EntryIdentifier(a) ?? string.Empty,
                   EntryIdentifier(b) ?? string.Empty);

        /// <summary>
        /// Finds one entry by key. The key is matched against the entry's full identity
        /// ("Section.Key") first and then against the bare key, because section names are chosen by
        /// each mod and a caller reading a .cfg naturally types the bare key.
        /// </summary>
        public static object FindEntry(object category, string identifier)
        {
            var entries = EntriesOf(category);
            if (entries == null) return null;

            foreach (var e in entries)
            {
                if (string.Equals(EntryIdentifier(e), identifier, StringComparison.OrdinalIgnoreCase))
                    return e;
            }

            foreach (var e in entries)
            {
                var key = e is BepInEx.Configuration.ConfigEntryBase b ? b.Definition?.Key : null;
                if (key != null && string.Equals(key, identifier, StringComparison.OrdinalIgnoreCase))
                    return e;
            }

            return null;
        }

        /// <summary>
        /// "Section.Key" - the form a user sees in the .cfg file and the only unambiguous way to
        /// name an entry, since two sections may legitimately share a key name.
        /// </summary>
        public static string EntryIdentifier(object entry)
        {
            if (!(entry is BepInEx.Configuration.ConfigEntryBase b) || b.Definition == null) return null;
            return $"{b.Definition.Section}.{b.Definition.Key}";
        }

        public static object Value(object entry)
            => entry is BepInEx.Configuration.ConfigEntryBase b ? b.BoxedValue : null;

        public static object DefaultValue(object entry)
            => entry is BepInEx.Configuration.ConfigEntryBase b ? b.DefaultValue : null;

        /// <summary>
        /// The entry's human-readable description, with any value constraint appended.
        ///
        /// HOW THIS IS OBTAINED is worth stating, because the obvious implementation is WRONG and
        /// fails silently. ConfigEntryBase.Description is a ConfigDescription OBJECT, and that class
        /// does NOT override ToString() - so `b.Description.ToString()` returns the literal string
        /// "BepInEx.Configuration.ConfigDescription" (measured in-game: every entry reported exactly
        /// that). The text lives in two separate fields instead:
        ///
        ///   <Description>       - the string the mod passed to ConfigDescription's constructor
        ///   <AcceptableValues>  - an AcceptableValueBase (e.g. AcceptableValueRange), whose own
        ///                         ToString() DOES render usefully ("Range: 1024 to 65535")
        ///
        /// Both are surfaced: the constraint is what stops a caller writing a value that would be
        /// silently clamped, so dropping it would make the tool look like it accepted the write.
        ///
        /// Read reflectively so a future BepInEx that renames these fields degrades to "no
        /// description" rather than throwing inside a config read.
        /// </summary>
        public static string Description(object entry)
        {
            if (!(entry is BepInEx.Configuration.ConfigEntryBase b)) return null;

            try
            {
                var description = b.Description;
                if (description == null) return null;

                var type = description.GetType();
                const System.Reflection.BindingFlags Flags =
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance;

                var text = type.GetProperty("Description", Flags)?.GetValue(description) as string;
                var acceptable = type.GetProperty("AcceptableValues", Flags)?.GetValue(description);

                // AcceptableValueRange/AcceptableValueList implement ToString() meaningfully; the
                // base type returns its own type name, which is filtered out rather than shown.
                string constraint = null;
                if (acceptable != null)
                {
                    var rendered = acceptable.ToString();
                    if (!string.IsNullOrEmpty(rendered)
                        && rendered != acceptable.GetType().FullName
                        && rendered != acceptable.GetType().Name)
                    {
                        constraint = rendered;
                    }
                }

                if (string.IsNullOrEmpty(text)) return constraint;
                if (string.IsNullOrEmpty(constraint)) return text;
                return text + " (" + constraint + ")";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// BepInEx has no separate display name - the key IS the name. Kept as a distinct member so
        /// the tool definitions do not have to special-case a backend that lacks one.
        /// </summary>
        public static string DisplayName(object entry)
        {
            if (!(entry is BepInEx.Configuration.ConfigEntryBase b) || b.Definition == null) return null;
            return b.Definition.Key;
        }

        /// <summary>
        /// The declared T of ConfigEntry&lt;T&gt;.
        ///
        /// ConfigEntryBase.SettingType carries this directly, which is more reliable than walking
        /// generic base types: it is populated for every entry, including one whose current value is
        /// null or default and would otherwise report as "Object" and be impossible to coerce.
        /// </summary>
        public static Type EntryValueType(object entry)
        {
            if (entry is BepInEx.Configuration.ConfigEntryBase b && b.SettingType != null)
                return b.SettingType;

            return Value(entry)?.GetType();
        }

        /// <summary>
        /// Writes through BoxedValue, which exists on the non-generic ConfigEntryBase and is
        /// writable. This is what makes a runtime `Type` usable as a write target without
        /// MakeGenericMethod - important here, because a runtime-generic call per entry inside a
        /// loop is the documented way to overflow the small stack the game runs tools on.
        /// </summary>
        public static void SetValue(object entry, object value)
        {
            if (!(entry is BepInEx.Configuration.ConfigEntryBase b))
                throw new InvalidOperationException("not a BepInEx config entry");

            b.BoxedValue = value;
        }

        /// <summary>
        /// Persists every config file we know about.
        ///
        /// Deliberately saves ALL of them rather than only the edited one: ConfigFile.Save() rewrites
        /// a whole file from memory, and BepInEx's own save path is not transactional across files,
        /// so narrowing the save here would buy nothing while risking a write that the caller
        /// believes was persisted. Failures are reported rather than swallowed, because "the write
        /// silently did not reach disk" is the single most confusing outcome for this tool.
        /// </summary>
        public static void Save()
        {
            var failures = new List<string>();

            foreach (var kv in LoadConfigFiles())
            {
                try
                {
                    kv.Value.Save();
                }
                catch (Exception ex)
                {
                    failures.Add($"{kv.Key}: {ex.Message}");
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "some config files could not be saved - " + string.Join("; ", failures));
            }
        }

        /// <summary>
        /// Convert a JSON-ish value from the MCP request into the entry's declared type.
        /// Enum names are accepted (the cfg file stores e.g. KeyCode as a bare name), which matters
        /// because several mods here store KeyCode and writing an int would be silently wrong.
        /// </summary>
        public static object Coerce(JToken token, Type targetType, out string error)
        {
            error = null;
            if (targetType == null) { error = "entry value type could not be determined"; return null; }

            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                if (underlying == typeof(bool)) return token.Value<bool>();
                if (underlying == typeof(string)) return token.Value<string>();
                if (underlying == typeof(int)) return token.Value<int>();
                if (underlying == typeof(long)) return token.Value<long>();
                if (underlying == typeof(float)) return token.Value<float>();
                if (underlying == typeof(double)) return token.Value<double>();
                if (underlying == typeof(byte)) return token.Value<byte>();
                if (underlying == typeof(short)) return token.Value<short>();

                if (underlying.IsEnum)
                {
                    // Accept both "LeftAlt" and 5 - the cfg uses names, the schema may not.
                    if (token.Type == JTokenType.String)
                        return Enum.Parse(underlying, token.Value<string>(), ignoreCase: true);
                    return Enum.ToObject(underlying, token.Value<long>());
                }

                return token.ToObject(underlying);
            }
            catch (Exception ex)
            {
                error = $"cannot convert '{token}' to {underlying.Name}: {ex.Message}";
                return null;
            }
        }

        /// <summary>Render a value for display, keeping enum names readable and nulls explicit.</summary>
        public static string Render(object v)
        {
            if (v == null) return "null";
            if (v is string s) return s;
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Opens with no arguments: nothing to supply, so the schema stays absent.</summary>
    public class ListConfigsToolDefinition : ToolDefinitionBase
    {
        public override string Name => "list_configs";

        // Reads the BepInEx ConfigFile object graph only - no Unity object involved.
        public override bool RequiresMainThread => false;


        public override string Description => @"List BepInEx config files and their entries with current value, default, type and description.

Reads the LIVE in-memory model, not the .cfg file - the two can differ if something changed a value
without saving. Use it to find the exact mod + key for get_config / set_config.

A value shown here is what the process intends to use, not proof the setting is in effect: one
consumed during Awake (opening a socket, installing a Harmony patch) was baked in at startup.";

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["mod"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Only this category, case-insensitive (e.g. 'FriendlyNoclip'). "
                                    + "Omit to list every category."
                    },
                    ["changedOnly"] = new ToolPropertySchema
                    {
                        Type = "boolean",
                        Description = "Only entries whose current value differs from the default. "
                                    + "Default false.",
                        Default = false
                    },
                    ["includeHidden"] = new ToolPropertySchema
                    {
                        Type = "boolean",
                        Description = "Include entries a mod marked hidden. Default true, because "
                                    + "hidden entries are still real and settable.",
                        Default = true
                    }
                }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            if (!ConfigIntrospection.Available)
                return ErrorResult("BepInEx config files are unavailable in this process.");

            var modFilter = GetStringArg(arguments, "mod");
            var changedOnly = GetBoolArg(arguments, "changedOnly");
            var includeHidden = GetBoolArg(arguments, "includeHidden", true);

            var cats = ConfigIntrospection.Categories;
            var result = new List<object>();
            int totalEntries = 0, totalChanged = 0;

            foreach (var cat in cats)
            {
                var identifier = ConfigIntrospection.CategoryIdentifier(cat);

                if (!string.IsNullOrEmpty(modFilter)
                    && !string.Equals(identifier, modFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var entries = ConfigIntrospection.EntriesOf(cat);
                var rows = new List<object>();
                int catChanged = 0;

                foreach (var e in entries)
                {
                    totalEntries++;

                    var hidden = e.GetType().GetProperty("IsHidden")?.GetValue(e) as bool? ?? false;
                    if (hidden && !includeHidden) continue;

                    var current = ConfigIntrospection.Value(e);
                    var def = ConfigIntrospection.DefaultValue(e);

                    // Compare rendered forms: it is stable across boxed-types and enums, and the
                    // question here is only "did someone change this away from its default".
                    var changed = !string.Equals(
                        ConfigIntrospection.Render(current),
                        ConfigIntrospection.Render(def),
                        StringComparison.Ordinal);

                    if (changed) { catChanged++; totalChanged++; }
                    if (changedOnly && !changed) continue;

                    rows.Add(new
                    {
                        key = ConfigIntrospection.EntryIdentifier(e),
                        type = ConfigIntrospection.EntryValueType(e)?.Name,
                        value = ConfigIntrospection.Render(current),
                        @default = ConfigIntrospection.Render(def),
                        changed,
                        hidden = hidden ? true : (bool?)null,
                        display = ConfigIntrospection.DisplayName(e),
                        description = ConfigIntrospection.Description(e)
                    });
                }

                if (rows.Count > 0 || string.IsNullOrEmpty(modFilter))
                {
                    result.Add(new
                    {
                        mod = identifier,
                        entryCount = entries?.Count ?? 0,
                        changedCount = catChanged,
                        entries = rows
                    });
                }
            }

            return JsonResult(new
            {
                categoryCount = result.Count,
                totalEntries,
                changedEntries = totalChanged,
                categories = result
            });
        }
    }

    /// <summary>Reads one entry, or a whole category when the key is omitted.</summary>
    public class GetConfigToolDefinition : ToolDefinitionBase
    {
        public override string Name => "get_config";

        // ConfigFile lookup; independent of the Unity main thread.
        public override bool RequiresMainThread => false;


        public override string Description => @"Read one BepInEx config entry, or every entry in a
category when 'key' is omitted.

Values come from the live in-memory model. 'changed' tells you whether the current value differs
from the declared default - useful before deciding whether a reset is warranted.";

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["mod"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Category identifier, case-insensitive (e.g. 'FriendlyNoclip')."
                    },
                    ["key"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Entry identifier. Omit to return the whole category."
                    }
                },
                Required = new List<string> { "mod" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            if (!ConfigIntrospection.Available)
                return ErrorResult("BepInEx config files are unavailable in this process.");

            var mod = GetStringArg(arguments, "mod");
            var key = GetStringArg(arguments, "key");

            if (string.IsNullOrEmpty(mod))
                return ErrorResult("'mod' is required.");

            var cat = ConfigIntrospection.FindCategory(mod);
            if (cat == null)
            {
                var known = ConfigIntrospection.Categories
                    .Cast<object>()
                    .Select(ConfigIntrospection.CategoryIdentifier)
                    .Where(x => x != null)
                    .OrderBy(x => x);
                return ErrorResult($"no category '{mod}'. Known: {string.Join(", ", known)}");
            }

            if (string.IsNullOrEmpty(key))
            {
                var entries = ConfigIntrospection.EntriesOf(cat);
                var all = new List<object>();
                foreach (var e in entries)
                {
                    var cur = ConfigIntrospection.Value(e);
                    var def = ConfigIntrospection.DefaultValue(e);
                    all.Add(new
                    {
                        key = ConfigIntrospection.EntryIdentifier(e),
                        type = ConfigIntrospection.EntryValueType(e)?.Name,
                        value = ConfigIntrospection.Render(cur),
                        @default = ConfigIntrospection.Render(def),
                        changed = !string.Equals(ConfigIntrospection.Render(cur),
                                                 ConfigIntrospection.Render(def),
                                                 StringComparison.Ordinal),
                        description = ConfigIntrospection.Description(e)
                    });
                }
                return JsonResult(new { mod, entryCount = all.Count, entries = all });
            }

            var entry = ConfigIntrospection.FindEntry(cat, key);
            if (entry == null)
            {
                var known = ConfigIntrospection.EntriesOf(cat)
                    .Cast<object>()
                    .Select(ConfigIntrospection.EntryIdentifier)
                    .Where(x => x != null)
                    .OrderBy(x => x);
                return ErrorResult($"no entry '{key}' in category '{mod}'. Known: {string.Join(", ", known)}");
            }

            var value = ConfigIntrospection.Value(entry);
            var dflt = ConfigIntrospection.DefaultValue(entry);

            return JsonResult(new
            {
                mod,
                key = ConfigIntrospection.EntryIdentifier(entry),
                type = ConfigIntrospection.EntryValueType(entry)?.Name,
                value = ConfigIntrospection.Render(value),
                @default = ConfigIntrospection.Render(dflt),
                changed = !string.Equals(ConfigIntrospection.Render(value),
                                         ConfigIntrospection.Render(dflt),
                                         StringComparison.Ordinal),
                display = ConfigIntrospection.DisplayName(entry),
                description = ConfigIntrospection.Description(entry)
            });
        }
    }

    /// <summary>Writes one entry and persists it. Deliberately silent about restart semantics.</summary>
    public class SetConfigToolDefinition : ToolDefinitionBase
    {
        public override string Name => "set_config";

        // Writes a BepInEx config entry. Off the main thread on purpose: a mod's own setting is
        // often the thing you want to flip (e.g. to bisect) while the main thread is stuck.
        public override bool RequiresMainThread => false;


        public override string Description => @"Set one BepInEx config entry and save it to disk.

Writes through the live ConfigFile, not the .cfg file: ConfigFile.Save rewrites the whole file from
memory, so a hand-edited .cfg is lost on the next save.

Restart semantics are yours to determine. A value read at its point of use takes effect now; one
consumed during Awake (opening a socket, installing a Harmony patch) only changes the NEXT launch.
Reading it back proves it was stored, not that behaviour changed. Returns the previous value.";

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["mod"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Category identifier, case-insensitive."
                    },
                    ["key"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Entry identifier."
                    },
                    ["value"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "New value. JSON type matters: pass true/false for booleans, "
                                    + "numbers unquoted for numerics, and bare names for enums "
                                    + "(e.g. \"LeftAlt\" for a KeyCode entry)."
                    },
                    ["confirm"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Must equal \"<mod>.<key>\" exactly. Guards against writing to "
                                    + "the wrong entry."
                    },
                    ["save"] = new ToolPropertySchema
                    {
                        Type = "boolean",
                        Description = "Persist to the .cfg file (default true). Set false to "
                                    + "change only the running process - useful for a temporary "
                                    + "experiment that should not survive a restart.",
                        Default = true
                    }
                },
                Required = new List<string> { "mod", "key", "value", "confirm" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            if (!ConfigIntrospection.Available)
                return ErrorResult("BepInEx config files are unavailable in this process.");

            var mod = GetStringArg(arguments, "mod");
            var key = GetStringArg(arguments, "key");
            var confirm = GetStringArg(arguments, "confirm");
            var save = GetBoolArg(arguments, "save", true);

            if (string.IsNullOrEmpty(mod) || string.IsNullOrEmpty(key))
                return ErrorResult("'mod' and 'key' are required.");

            var expected = $"{mod}.{key}";
            if (!string.Equals(confirm, expected, StringComparison.Ordinal))
            {
                return ErrorResult(
                    $"'confirm' must be exactly '{expected}' (got '{(confirm ?? "<null>")}'). " +
                    "This guard exists so a mistargeted write cannot land silently.");
            }

            if (!arguments.TryGetValue("value", out var rawValue))
                return ErrorResult("'value' is required.");

            var cat = ConfigIntrospection.FindCategory(mod);
            if (cat == null)
                return ErrorResult($"no category '{mod}'.");

            var entry = ConfigIntrospection.FindEntry(cat, key);
            if (entry == null)
                return ErrorResult($"no entry '{key}' in category '{mod}'.");

            var targetType = ConfigIntrospection.EntryValueType(entry);
            var coerced = ConfigIntrospection.Coerce(rawValue, targetType, out var convError);
            if (convError != null) return ErrorResult(convError);

            var before = ConfigIntrospection.Value(entry);
            var beforeText = ConfigIntrospection.Render(before);
            var afterText = ConfigIntrospection.Render(coerced);

            if (string.Equals(beforeText, afterText, StringComparison.Ordinal))
            {
                // Still persist if asked: the caller may be re-asserting a value after a cfg edit.
                if (save) ConfigIntrospection.Save();
                return JsonResult(new
                {
                    mod,
                    key,
                    type = targetType?.Name,
                    previousValue = beforeText,
                    value = afterText,
                    changed = false,
                    saved = save,
                    note = "Value already had this setting; nothing changed."
                });
            }

            try
            {
                ConfigIntrospection.SetValue(entry, coerced);
            }
            catch (Exception ex)
            {
                return ErrorResult($"writing '{expected}' threw {ex.GetType().Name}: {ex.Message}");
            }

            string readBack = null;
            try { readBack = ConfigIntrospection.Render(ConfigIntrospection.Value(entry)); }
            catch { /* a validator may have rejected it; reported below */ }

            if (save)
            {
                try { ConfigIntrospection.Save(); }
                catch (Exception ex)
                {
                    return ErrorResult(
                        $"value was set in memory to {afterText}, but saving failed: " +
                        $"{ex.GetType().Name}: {ex.Message}. It will be lost on exit.");
                }
            }

            var applied = string.Equals(readBack, afterText, StringComparison.Ordinal);

            return JsonResult(new
            {
                mod,
                key,
                type = targetType?.Name,
                previousValue = beforeText,
                value = readBack,
                changed = true,
                saved = save,
                applied = applied,
                restartRequired = "unknown - see tool description",
                note = applied
                    ? (save
                        ? "Set and saved. Whether this affects the running process depends on when "
                        + "the mod reads the value; if it installs a native hook or Harmony patch "
                        + "during Awake, a restart is required."
                        : "Set in memory only (save=false); will not survive a restart.")
                    : "Value was written but reads back differently, so a validator or setter "
                      + "transformed it. Treat the reported value as authoritative."
            });
        }
    }

    /// <summary>Restores defaults, the natural counterpart to a bisection experiment.</summary>
    public class ResetConfigToolDefinition : ToolDefinitionBase
    {
        public override string Name => "reset_config";

        // ConfigFile write; no Unity object access.
        public override bool RequiresMainThread => false;


        public override string Description => @"Restore BepInEx config entries to their declared
defaults.

Use this to clean up after a bisection experiment. Scope it with 'key' for a single entry, or omit
'key' to reset a whole category. Pass 'dryRun' to see exactly what would change first.

Same restart caveat as set_config: resetting a value that a mod consumed during Awake
does not undo anything already installed in the running process.";

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["mod"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Category identifier, case-insensitive."
                    },
                    ["key"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Single entry to reset. Omit to reset every entry in the "
                                    + "category."
                    },
                    ["dryRun"] = new ToolPropertySchema
                    {
                        Type = "boolean",
                        Description = "Report what would change without writing anything. "
                                    + "Default false.",
                        Default = false
                    },
                    ["save"] = new ToolPropertySchema
                    {
                        Type = "boolean",
                        Description = "Persist to the .cfg file (default true).",
                        Default = true
                    }
                },
                Required = new List<string> { "mod" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            if (!ConfigIntrospection.Available)
                return ErrorResult("BepInEx config files are unavailable in this process.");

            var mod = GetStringArg(arguments, "mod");
            var key = GetStringArg(arguments, "key");
            var dryRun = GetBoolArg(arguments, "dryRun");
            var save = GetBoolArg(arguments, "save", true);

            if (string.IsNullOrEmpty(mod))
                return ErrorResult("'mod' is required.");

            var cat = ConfigIntrospection.FindCategory(mod);
            if (cat == null)
                return ErrorResult($"no category '{mod}'.");

            IEnumerable<object> targets;
            if (!string.IsNullOrEmpty(key))
            {
                var single = ConfigIntrospection.FindEntry(cat, key);
                if (single == null)
                    return ErrorResult($"no entry '{key}' in category '{mod}'.");
                targets = new[] { single };
            }
            else
            {
                targets = ConfigIntrospection.EntriesOf(cat).Cast<object>();
            }

            var changes = new List<object>();
            var failures = new List<string>();

            foreach (var e in targets)
            {
                var cur = ConfigIntrospection.Render(ConfigIntrospection.Value(e));
                var def = ConfigIntrospection.Render(ConfigIntrospection.DefaultValue(e));

                if (string.Equals(cur, def, StringComparison.Ordinal))
                {
                    changes.Add(new
                    {
                        key = ConfigIntrospection.EntryIdentifier(e),
                        from = cur,
                        to = def,
                        changed = false
                    });
                    continue;
                }

                if (!dryRun)
                {
                    try
                    {
                        ConfigIntrospection.SetValue(e, ConfigIntrospection.DefaultValue(e));
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{ConfigIntrospection.EntryIdentifier(e)}: " +
                                     $"{ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                }

                changes.Add(new
                {
                    key = ConfigIntrospection.EntryIdentifier(e),
                    from = cur,
                    to = def,
                    changed = true
                });
            }

            var changedCount = changes.Count(c => (bool)c.GetType().GetProperty("changed").GetValue(c));

            if (!dryRun && changedCount > 0 && save)
            {
                try { ConfigIntrospection.Save(); }
                catch (Exception ex)
                {
                    return ErrorResult(
                        $"reset applied in memory but saving failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return JsonResult(new
            {
                mod,
                key = string.IsNullOrEmpty(key) ? null : key,
                dryRun,
                saved = !dryRun && save,
                changedCount,
                changes,
                failures = failures.Count > 0 ? failures : null,
                note = "Resetting a value does not retract anything already installed in the "
                     + "running process; restart if the mod consumed it during Awake."
            });
        }
    }
}
