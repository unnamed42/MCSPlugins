using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using McsMCP.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McsMCP.Tools
{
    /// <summary>
    /// Data structure for storing game-specific knowledge
    /// </summary>
    public class GameKnowledge
    {
        [JsonProperty("gameName")]
        public string GameName { get; set; }

        [JsonProperty("unityVersion")]
        public string UnityVersion { get; set; }

        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonProperty("pseudocodePath")]
        public string PseudocodePath { get; set; }

        [JsonProperty("discoveries")]
        public Dictionary<string, List<KnowledgeEntry>> Discoveries { get; set; } = new Dictionary<string, List<KnowledgeEntry>>();
    }

    public class KnowledgeEntry
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("example")]
        public string Example { get; set; }

        [JsonProperty("addedAt")]
        public DateTime AddedAt { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new List<string>();
    }

    /// <summary>
    /// Manages the game knowledge cache
    /// </summary>
    public static class GameKnowledgeManager
    {
        private static Dictionary<string, GameKnowledge> _knowledgeBase = new Dictionary<string, GameKnowledge>();
        private static string _knowledgeFilePath;
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;

            // Store under BepInEx's config directory, alongside McsMCP.cfg.
            // BepInEx.Paths.ConfigPath is the direct replacement for MelonLoader's
            // MelonEnvironment.UserDataDirectory: both are the per-install writable folder that
            // survives a plugin reload.
            var userDataPath = Path.Combine(BepInEx.Paths.ConfigPath, "McsMCP");
            Directory.CreateDirectory(userDataPath);
            _knowledgeFilePath = Path.Combine(userDataPath, "game_knowledge.json");

            LoadKnowledge();
            _initialized = true;
        }

        private static void LoadKnowledge()
        {
            try
            {
                if (File.Exists(_knowledgeFilePath))
                {
                    var json = File.ReadAllText(_knowledgeFilePath);
                    _knowledgeBase = JsonConvert.DeserializeObject<Dictionary<string, GameKnowledge>>(json)
                        ?? new Dictionary<string, GameKnowledge>();
                    McsMCPPlugin.Log?.LogInfo($"[GameKnowledge] Loaded knowledge for {_knowledgeBase.Count} games");
                }
            }
            catch (Exception ex)
            {
                McsMCPPlugin.Log?.LogError($"[GameKnowledge] Failed to load knowledge: {ex.Message}");
                _knowledgeBase = new Dictionary<string, GameKnowledge>();
            }
        }

        private static void SaveKnowledge()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_knowledgeBase, Formatting.Indented);
                File.WriteAllText(_knowledgeFilePath, json);
            }
            catch (Exception ex)
            {
                McsMCPPlugin.Log?.LogError($"[GameKnowledge] Failed to save knowledge: {ex.Message}");
            }
        }

        public static string GetCurrentGameName()
        {
            try
            {
                return UnityEngine.Application.productName ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        public static string GetCurrentUnityVersion()
        {
            try
            {
                return UnityEngine.Application.unityVersion ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        public static GameKnowledge GetOrCreateGameKnowledge(string gameName = null)
        {
            Initialize();
            gameName = gameName ?? GetCurrentGameName();

            if (!_knowledgeBase.TryGetValue(gameName, out var knowledge))
            {
                knowledge = new GameKnowledge
                {
                    GameName = gameName,
                    UnityVersion = GetCurrentUnityVersion(),
                    LastUpdated = DateTime.UtcNow
                };
                _knowledgeBase[gameName] = knowledge;
            }

            return knowledge;
        }

        public static void AddDiscovery(string category, KnowledgeEntry entry, string gameName = null)
        {
            var knowledge = GetOrCreateGameKnowledge(gameName);

            if (!knowledge.Discoveries.TryGetValue(category, out var entries))
            {
                entries = new List<KnowledgeEntry>();
                knowledge.Discoveries[category] = entries;
            }

            // Check for duplicate keys and update if exists
            var existing = entries.FirstOrDefault(e => e.Key == entry.Key);
            if (existing != null)
            {
                entries.Remove(existing);
            }

            entry.AddedAt = DateTime.UtcNow;
            entries.Add(entry);
            knowledge.LastUpdated = DateTime.UtcNow;

            SaveKnowledge();
            McsMCPPlugin.Log?.LogInfo($"[GameKnowledge] Added discovery: {category}/{entry.Key}");
        }

        public static List<KnowledgeEntry> GetDiscoveries(string category, string gameName = null)
        {
            var knowledge = GetOrCreateGameKnowledge(gameName);
            return knowledge.Discoveries.TryGetValue(category, out var entries) ? entries : new List<KnowledgeEntry>();
        }

        public static Dictionary<string, GameKnowledge> GetAllKnowledge()
        {
            Initialize();
            return _knowledgeBase;
        }

        public static GameKnowledge GetGameKnowledge(string gameName)
        {
            Initialize();
            return _knowledgeBase.TryGetValue(gameName, out var knowledge) ? knowledge : null;
        }

        public static string GetKnowledgeFilePath()
        {
            Initialize();
            return _knowledgeFilePath;
        }

        public static void SetPseudocodePath(string path, string gameName = null)
        {
            var knowledge = GetOrCreateGameKnowledge(gameName);
            knowledge.PseudocodePath = path;
            knowledge.LastUpdated = DateTime.UtcNow;
            SaveKnowledge();
            McsMCPPlugin.Log?.LogInfo($"[GameKnowledge] Set pseudocode path: {path}");
        }

        public static string GetPseudocodePath(string gameName = null)
        {
            var knowledge = GetOrCreateGameKnowledge(gameName);
            return knowledge.PseudocodePath;
        }
    }

    /// <summary>
    /// Tool to add a discovery to the game knowledge base
    /// </summary>
    public class AddGameKnowledgeToolDefinition : ToolDefinitionBase
    {
        public override string Name => "add_game_knowledge";
        public override string Description => @"Record knowledge about the GAME ITSELF - world, lore, rules, characters, items, factions,
mechanics - i.e. things true of the game as a product, independent of any mod.

DO NOT store mod-development knowledge here: no tool usage, build recipes, decompilation workflow,
gotchas, addresses, offsets or patch designs. Tooling goes in AGENTS.md, game-specific measured
behaviour in docs/<project>.md.

This lives in the GAME directory, unversioned and lost on reinstall, and other agents cannot see it.";

        /// <summary>
        /// Reminder surfaced in the schema too, because the description alone is easy to skim past.
        /// </summary>
        private const string ScopeReminder =
            "Game lore/setting/rules ONLY. Mod-development knowledge (tooling, addresses, offsets, "
            + "build recipes, gotchas) belongs in AGENTS.md or docs/<project>.md - NOT here.";

        public override bool RequiresMainThread => false;

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["category"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Category. Prefer game-content categories: 'lore', 'world', 'characters', "
                                    + "'items', 'factions', 'mechanics', 'rules', 'numeric_systems'. " + ScopeReminder
                    },
                    ["key"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Unique identifier for this discovery within the category"
                    },
                    ["value"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "The knowledge itself. NOTE: " + ScopeReminder
                    },
                    ["description"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Human-readable description of what this does"
                    },
                    ["example"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Example usage or expected output (optional)"
                    },
                    ["tags"] = new ToolPropertySchema
                    {
                        Type = "array",
                        Description = "Tags for searchability (optional)",
                        Items = new ToolPropertySchema { Type = "string" }
                    }
                },
                Required = new List<string> { "category", "key", "value", "description" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var category = GetStringArg(arguments, "category");
            var key = GetStringArg(arguments, "key");
            var value = GetStringArg(arguments, "value");
            var description = GetStringArg(arguments, "description");
            var example = GetStringArg(arguments, "example", "");
            var tags = GetArg<List<string>>(arguments, "tags", new List<string>());

            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            {
                return ErrorResult("category, key, and value are required");
            }

            var entry = new KnowledgeEntry
            {
                Key = key,
                Value = value,
                Description = description,
                Example = example,
                Tags = tags ?? new List<string>()
            };

            GameKnowledgeManager.AddDiscovery(category, entry);

            return JsonResult(new
            {
                success = true,
                message = $"Added discovery to {category}: {key}",
                gameName = GameKnowledgeManager.GetCurrentGameName(),
                filePath = GameKnowledgeManager.GetKnowledgeFilePath()
            });
        }
    }

    /// <summary>
    /// Tool to query the game knowledge base
    /// </summary>
    public class GetGameKnowledgeToolDefinition : ToolDefinitionBase
    {
        public override string Name => "get_game_knowledge";
        public override string Description => @"Query the knowledge recorded about the GAME ITSELF - world, lore, rules, characters, items,
factions, mechanics.

Holds no mod-development knowledge by design: tooling, build recipes and decompilation notes live in
the repository's AGENTS.md and docs/<project>.md. Look there for those.";

        public override bool RequiresMainThread => false;

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["category"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Specific category to retrieve (optional, returns all if not specified)"
                    },
                    ["search"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Search term to filter results by key, value, description, or tags (optional)"
                    },
                    ["gameName"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Game name to query (optional, defaults to current game)"
                    },
                    ["listGames"] = new ToolPropertySchema
                    {
                        Type = "boolean",
                        Description = "If true, list all games with stored knowledge instead of querying"
                    }
                }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var category = GetStringArg(arguments, "category");
            var search = GetStringArg(arguments, "search");
            var gameName = GetStringArg(arguments, "gameName");
            var listGames = GetBoolArg(arguments, "listGames", false);

            if (listGames)
            {
                var allKnowledge = GameKnowledgeManager.GetAllKnowledge();
                var gameList = allKnowledge.Select(kvp => new
                {
                    gameName = kvp.Key,
                    unityVersion = kvp.Value.UnityVersion,
                    lastUpdated = kvp.Value.LastUpdated,
                    categoryCount = kvp.Value.Discoveries.Count,
                    totalEntries = kvp.Value.Discoveries.Values.Sum(l => l.Count)
                }).ToList();

                return JsonResult(new
                {
                    games = gameList,
                    totalGames = gameList.Count,
                    knowledgeFilePath = GameKnowledgeManager.GetKnowledgeFilePath()
                });
            }

            var knowledge = gameName != null
                ? GameKnowledgeManager.GetGameKnowledge(gameName)
                : GameKnowledgeManager.GetOrCreateGameKnowledge();

            if (knowledge == null)
            {
                return JsonResult(new
                {
                    found = false,
                    message = $"No knowledge found for game: {gameName}"
                });
            }

            // Filter by category if specified
            Dictionary<string, List<KnowledgeEntry>> results;
            if (!string.IsNullOrEmpty(category))
            {
                results = new Dictionary<string, List<KnowledgeEntry>>();
                if (knowledge.Discoveries.TryGetValue(category, out var entries))
                {
                    results[category] = entries;
                }
            }
            else
            {
                results = knowledge.Discoveries;
            }

            // Apply search filter if specified
            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLowerInvariant();
                results = results.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Where(e =>
                        (e.Key?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                        (e.Value?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                        (e.Description?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                        (e.Tags?.Any(t => t.ToLowerInvariant().Contains(searchLower)) ?? false)
                    ).ToList()
                ).Where(kvp => kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            return JsonResult(new
            {
                gameName = knowledge.GameName,
                unityVersion = knowledge.UnityVersion,
                lastUpdated = knowledge.LastUpdated,
                discoveries = results,
                totalEntries = results.Values.Sum(l => l.Count)
            });
        }
    }

    /// <summary>
    /// Tool to get a quick summary of what's known about the current game
    /// </summary>
    public class GetGameSummaryToolDefinition : ToolDefinitionBase
    {
        public override string Name => "get_game_summary";
        public override string Description => @"Get a quick summary of stored knowledge for the current game:
categorized counts and key entries. Use this first when connecting to a game.

Reminder: this store is for the GAME's own lore/setting/rules. Mod-development knowledge lives in the
repository's AGENTS.md and docs/<project>.md, not here.";

        public override bool RequiresMainThread => false;

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>()
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var gameName = GameKnowledgeManager.GetCurrentGameName();
            var knowledge = GameKnowledgeManager.GetOrCreateGameKnowledge();

            var summary = new Dictionary<string, object>
            {
                ["gameName"] = gameName,
                ["unityVersion"] = knowledge.UnityVersion,
                ["lastUpdated"] = knowledge.LastUpdated,
                ["pseudocodePath"] = knowledge.PseudocodePath,
                ["hasKnowledge"] = knowledge.Discoveries.Count > 0,
                ["categories"] = knowledge.Discoveries.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        count = kvp.Value.Count,
                        keys = kvp.Value.Select(e => e.Key).ToList()
                    }
                )
            };

            // Add quick reference for most useful items
            var quickRef = new Dictionary<string, string>();

            // Get first few console commands
            if (knowledge.Discoveries.TryGetValue("console_commands", out var commands))
            {
                foreach (var cmd in commands.Take(5))
                {
                    quickRef[$"cmd:{cmd.Key}"] = cmd.Value;
                }
            }

            // Get static accessors
            if (knowledge.Discoveries.TryGetValue("static_accessors", out var accessors))
            {
                foreach (var acc in accessors.Take(5))
                {
                    quickRef[$"accessor:{acc.Key}"] = acc.Value;
                }
            }

            // Get cheats
            if (knowledge.Discoveries.TryGetValue("cheats", out var cheats))
            {
                foreach (var cheat in cheats.Take(5))
                {
                    quickRef[$"cheat:{cheat.Key}"] = cheat.Value;
                }
            }

            summary["quickReference"] = quickRef;

            return JsonResult(summary);
        }
    }

    /// <summary>
    /// Tool to set the pseudocode/decompiled source path for a game
    /// </summary>
    public class SetPseudocodePathToolDefinition : ToolDefinitionBase
    {
        public override string Name => "set_pseudocode_path";
        public override string Description => @"Set the path to decompiled/pseudocode source for the current game.
This allows the AI to look up type implementations when exploring the game.
The path should point to a directory containing decompiled .cs files (e.g., from ILSpy, dnSpy, or Cpp2IL).";

        public override bool RequiresMainThread => false;

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["path"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Absolute path to the pseudocode/decompiled source directory"
                    }
                },
                Required = new List<string> { "path" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var path = GetStringArg(arguments, "path");

            if (string.IsNullOrEmpty(path))
            {
                return ErrorResult("path is required");
            }

            // Validate the path exists
            if (!Directory.Exists(path))
            {
                return ErrorResult($"Directory does not exist: {path}");
            }

            GameKnowledgeManager.SetPseudocodePath(path);

            // Count files to give feedback
            int csFileCount = 0;
            try
            {
                csFileCount = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Length;
            }
            catch { }

            return JsonResult(new
            {
                success = true,
                message = $"Set pseudocode path for {GameKnowledgeManager.GetCurrentGameName()}",
                path = path,
                csFileCount = csFileCount,
                knowledgeFilePath = GameKnowledgeManager.GetKnowledgeFilePath()
            });
        }
    }

    /// <summary>
    /// Tool to search pseudocode files for type definitions
    /// </summary>
    public class SearchPseudocodeToolDefinition : ToolDefinitionBase
    {
        public override string Name => "search_pseudocode";
        public override string Description => @"Search the game's pseudocode/decompiled source for type definitions, methods, or code patterns.
Requires pseudocode_path to be set first via set_pseudocode_path.
Returns matching file paths and content snippets.";

        public override bool RequiresMainThread => false;

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["query"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Search query - can be a type name, method name, or text pattern"
                    },
                    ["filePattern"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Optional file name pattern to filter (e.g., '*Console*.cs'). Default: '*.cs' (also searches *.cpp if no .cs files found)"
                    },
                    ["maxResults"] = new ToolPropertySchema
                    {
                        Type = "integer",
                        Description = "Maximum number of results to return (default: 10)",
                        Default = 10,
                        Maximum = 50
                    },
                    ["contextLines"] = new ToolPropertySchema
                    {
                        Type = "integer",
                        Description = "Number of context lines to include around matches (default: 3)",
                        Default = 3,
                        Maximum = 20
                    }
                },
                Required = new List<string> { "query" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var query = GetStringArg(arguments, "query");
            var filePattern = GetStringArg(arguments, "filePattern", "*.cs");
            // Both of these are bounded in CODE, not only in the schema.
            //
            // Same reasoning as read_pseudocode_file's lineCount: a schema Maximum is a hint to the
            // client and cannot be relied on. These two MULTIPLY - maxResults x (2*contextLines+1)
            // lines of real decompiled source come back in a single response - so an unclamped pair
            // is the cheapest way to blow a caller's context. 50 results x 9 context lines is already
            // ~450 lines of source per call.
            const int MaxResultsCap = 50;
            const int MaxContextLines = 10;

            var maxResults = GetIntArg(arguments, "maxResults", 10);
            var contextLines = GetIntArg(arguments, "contextLines", 3);

            if (maxResults > MaxResultsCap) maxResults = MaxResultsCap;
            if (maxResults < 1) maxResults = 1;
            if (contextLines > MaxContextLines) contextLines = MaxContextLines;
            if (contextLines < 0) contextLines = 0;

            if (string.IsNullOrEmpty(query))
            {
                return ErrorResult("query is required");
            }

            var pseudocodePath = GameKnowledgeManager.GetPseudocodePath();
            if (string.IsNullOrEmpty(pseudocodePath))
            {
                return ErrorResult("Pseudocode path not set. Use set_pseudocode_path first.");
            }

            if (!Directory.Exists(pseudocodePath))
            {
                return ErrorResult($"Pseudocode directory not found: {pseudocodePath}");
            }

            var results = new List<object>();

            try
            {
                string[] files;

                // If using default pattern, search both .cs and .cpp (Cpp2IL output)
                if (filePattern == "*.cs")
                {
                    var csFiles = Directory.GetFiles(pseudocodePath, "*.cs", SearchOption.AllDirectories);
                    var cppFiles = Directory.GetFiles(pseudocodePath, "*.cpp", SearchOption.AllDirectories);
                    files = csFiles.Concat(cppFiles).ToArray();
                }
                else
                {
                    files = Directory.GetFiles(pseudocodePath, filePattern, SearchOption.AllDirectories);
                }

                foreach (var file in files)
                {
                    if (results.Count >= maxResults) break;

                    try
                    {
                        var lines = File.ReadAllLines(file);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Get context
                                int start = Math.Max(0, i - contextLines);
                                int end = Math.Min(lines.Length - 1, i + contextLines);

                                var contextList = new List<string>();
                                for (int j = start; j <= end; j++)
                                {
                                    var prefix = j == i ? ">>> " : "    ";
                                    contextList.Add($"{j + 1:D4}{prefix}{lines[j]}");
                                }

                                results.Add(new
                                {
                                    file = MakeRelativePath(pseudocodePath, file),
                                    line = i + 1,
                                    matchedLine = lines[i].Trim(),
                                    context = string.Join("\n", contextList)
                                });

                                if (results.Count >= maxResults) break;

                                // Skip ahead to avoid duplicate matches from same area
                                i += contextLines;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                return ErrorResult($"Search failed: {ex.Message}");
            }

            return JsonResult(new
            {
                query = query,
                pseudocodePath = pseudocodePath,
                resultsCount = results.Count,
                results = results
            });
        }

        /// <summary>
        /// Path.GetRelativePath is .NET Core 2.0+ / .NET Standard 2.1 and does NOT exist in net472
        /// (CS0117). This is the equivalent for the one thing it is used for here: shortening a
        /// decompiled-source path for display, so a search result does not repeat a long root
        /// directory on every row.
        ///
        /// Returns the original path unchanged when it is not actually under the root, which is the
        /// case for the absolute paths a caller may pass in - dropping that case would make the
        /// displayed path wrong rather than merely long.
        /// </summary>
        internal static string MakeRelativePath(string root, string fullPath)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(fullPath)) return fullPath;

            try
            {
                var rootFull = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var pathFull = Path.GetFullPath(fullPath);

                if (pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    return pathFull.Substring(rootFull.Length);
                }

                return fullPath;
            }
            catch
            {
                // A malformed path must not break the search that produced it.
                return fullPath;
            }
        }
    }

    /// <summary>
    /// Tool to read a specific pseudocode file
    /// </summary>
    public class ReadPseudocodeFileToolDefinition : ToolDefinitionBase
    {
        public override string Name => "read_pseudocode_file";
        public override string Description => @"Read the contents of a specific pseudocode/decompiled source file.
Use search_pseudocode first to find relevant files, then use this to read the full content.";

        public override bool RequiresMainThread => false;

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["relativePath"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "Relative path to the file within the pseudocode directory"
                    },
                    ["startLine"] = new ToolPropertySchema
                    {
                        Type = "integer",
                        Description = "Optional starting line number (1-based)"
                    },
                    ["lineCount"] = new ToolPropertySchema
                    {
                        Type = "integer",
                        Description = "Optional number of lines to read (default: entire file, max: 500)",
                        Default = 500,
                        Maximum = 500
                    }
                },
                Required = new List<string> { "relativePath" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var relativePath = GetStringArg(arguments, "relativePath");
            var startLine = GetIntArg(arguments, "startLine", 1);
            var lineCount = GetIntArg(arguments, "lineCount", 500);

            if (string.IsNullOrEmpty(relativePath))
            {
                return ErrorResult("relativePath is required");
            }

            var pseudocodePath = GameKnowledgeManager.GetPseudocodePath();
            if (string.IsNullOrEmpty(pseudocodePath))
            {
                return ErrorResult("Pseudocode path not set. Use set_pseudocode_path first.");
            }

            var fullPath = Path.Combine(pseudocodePath, relativePath);

            // ---- path containment -------------------------------------------------------------
            //
            // TWO defects were fixed here, both found by inspection rather than by a failure.
            //
            // (a) The original compared with a bare StartsWith(base), which is a PREFIX test, not a
            //     containment test. "/decomp" is a prefix of "/decompiled-secret", so a sibling
            //     directory whose name merely starts with the same characters passed the check. The
            //     base path is now compared WITH a trailing separator, which makes it a real
            //     directory-boundary test.
            //
            // (b) The comparison was case-SENSITIVE (StartsWith defaults to Ordinal). On Windows -
            //     which is where this mod runs - the filesystem is case-insensitive, so a caller
            //     passing "Il2Cpp/UICamera.cs" against a base of "...\decomp\il2cpp" would be
            //     rejected even though the file is genuinely inside. Matching the filesystem's own
            //     semantics is the correct behaviour here.
            //
            // The trailing-separator form also handles the base directory itself, because the
            // caller must name a FILE and a file path is always longer than its directory prefix.
            string normalizedBase;
            string normalizedTarget;
            try
            {
                normalizedBase = Path.GetFullPath(pseudocodePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                normalizedTarget = Path.GetFullPath(fullPath);
            }
            catch (Exception pathEx)
            {
                return ErrorResult($"Could not resolve '{relativePath}': {pathEx.Message}");
            }

            if (!normalizedTarget.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                return ErrorResult($"Invalid path - '{relativePath}' resolves outside the pseudocode "
                    + $"directory ({pseudocodePath}).");
            }

            if (!File.Exists(fullPath))
            {
                return ErrorResult($"File not found: {relativePath}");
            }

            try
            {
                // ★ lineCount was declared max 500 in the SCHEMA but never clamped in CODE.
                //
                // The schema's Maximum is a hint to the client, not something this server can rely
                // on: a hand-written request, a different client, or an older schema-cache can all
                // send lineCount=100000. Line 860 previously clamped only against the file size
                // (Math.Min(lineCount, totalLines - start)), so a large lineCount read the WHOLE
                // file. Measured on a real ILSpy tree, a single decompiled file runs ~560 KB /
                // ~140k tokens, which would blow a caller's context in one call - the same failure
                // mode take_screenshot had.
                //
                // Clamped here so the contract holds regardless of what the client sent.
                const int MaxLines = 500;
                if (lineCount > MaxLines) lineCount = MaxLines;
                if (lineCount < 1) lineCount = 1;
                if (startLine < 1) startLine = 1;

                var allLines = File.ReadAllLines(fullPath);
                var totalLines = allLines.Length;

                // Adjust indices (1-based to 0-based)
                int start = Math.Max(0, startLine - 1);
                int count = Math.Min(lineCount, Math.Max(0, totalLines - start));
                var selectedLines = allLines.Skip(start).Take(count).ToList();

                // Add line numbers
                var numberedLines = new List<string>();
                for (int i = 0; i < selectedLines.Count; i++)
                {
                    numberedLines.Add($"{start + i + 1,5}: {selectedLines[i]}");
                }

                // Report truncation explicitly. A caller that asked for the whole file and got 500
                // lines must be told, or it will treat a partial read as the complete file - the
                // classic silent-truncation trap. `truncatedBy` says how many lines were withheld.
                int endLine = start + count;
                int truncatedBy = Math.Max(0, totalLines - endLine);

                return JsonResult(new
                {
                    file = relativePath,
                    totalLines = totalLines,
                    startLine = start + 1,
                    endLine = endLine,
                    returnedLines = count,
                    truncated = truncatedBy > 0 || endLine < totalLines,
                    truncatedBy = truncatedBy,
                    content = string.Join("\n", numberedLines),
                    note = truncatedBy > 0
                        ? $"File has {totalLines} lines; showing {start + 1}-{endLine}. "
                          + $"Pass startLine={endLine + 1} to continue (max {MaxLines} lines per call)."
                        : null
                });
            }
            catch (Exception ex)
            {
                return ErrorResult($"Failed to read file: {ex.Message}");
            }
        }
    }
}
