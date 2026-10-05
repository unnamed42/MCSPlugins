# McsMCP

An in-game [Model Context Protocol](https://modelcontextprotocol.io/) server for **觅长生 (MCS)**,
running on **BepInEx 5 + Unity Mono**.

It is a port of **MelonMCP** (a MelonLoader/IL2CPP mod) to this game's runtime. The tool surface is
deliberately kept close to the original so the two can be compared, but a Mono game is not an IL2CPP
game and several subsystems were rewritten rather than translated — see
[Migration notes](#migration-notes-from-melonmcp) for exactly what changed and why.

## Requirements

| Component | Version | Source |
|---|---|---|
| BepInEx | 5.4.17 | `steamapps/workshop/content/1189490/2824349934/BepInEx` |
| HarmonyX | 2.5.5 | bundled with the above |
| .NET Framework | 4.7.2 | target framework |
| Newtonsoft.Json | 12.0.0 | the copy the **game** ships |

## Build

```bash
cd McsMCP
dotnet build -c Release
```

Output is a **single file**, `bin/Release/net472/McsMCP.dll`. Mono.CSharp (`mcs.dll`) is merged into
it by ILRepack as a post-build step, so there is no second DLL to copy.

> **The merge runs in Debug builds too**, on purpose. It used to be Release-only in MelonMCP, which
> made `dotnet build -c Debug` succeed while silently producing an assembly with no `Mono.CSharp` in
> it — `execute_csharp` then failed at type-initializer time with a message pointing nowhere near the
> real cause. A build that emits a broken artifact on success is worse than one that fails loudly.

> **`nuget.config` redirects the package folder** to `McsMCP/.nuget/packages`. On this machine the
> user-level `~/.nuget/packages` is on a read-only mount, so restoring `ILRepack.Lib.MSBuild.Task`
> into it fails with a misleading `Read-only file system` error. Same approach as
> `LongYinMods/MelonMCP/nuget.config`.

## Install

Copy `McsMCP.dll` to either:

- `steamapps/workshop/content/1189490/2824349934/BepInEx/plugins/` — the normal location, or
- `steamapps/common/觅长生/本地Mod测试/` — for local mod testing.

Then launch the game. BepInEx logs:

```
McsMCP Initializing...
Registered N MCP tools
McsMCP Server started on port 27015
```

## Connecting an MCP client

Two transports are served, on separate ports. **Use HTTP - it needs no bridge.**

### Streamable HTTP (recommended, no bridge process)

```json
{
  "mcpServers": {
    "mcsmcp": {
      "type": "http",
      "url": "http://127.0.0.1:27016/mcp"
    }
  }
}
```

That is the whole configuration - no `command`, no `args`, no child process. It works with any client
that speaks the MCP Streamable HTTP transport (DeepSeek Harness, Claude Code, Codex, ...).

For **DeepSeek Harness** specifically, add to the profile's `cordis.patch.yml`:

```yaml
- insert:
    - id: mcp-mcs
      name: "@deepseek-ai/dsh-mcp-client"
      config:
        serverName: mcs
        transport: streamable-http
        url: http://127.0.0.1:27016/mcp
```

Tools then appear to the model as `mcp__mcs__<tool>` - e.g. `mcp__mcs__read_logs`.

### TCP + bridge (legacy)

The original newline-delimited-JSON-RPC TCP port is still served, for clients that cannot do HTTP.
It needs one of the bundled bridge scripts:

```json
{
  "mcpServers": {
    "mcsmcp": {
      "command": "python",
      "args": ["/path/to/McsMCP/mcp-bridge.py", "--host", "localhost", "--port", "27015"]
    }
  }
}
```

`mcp-bridge.js` (Node) and `mcp-bridge.ps1` (Windows PowerShell, no dependencies) take the same
arguments.

### Configuration

All under `BepInEx/config/McsMCP.cfg`:

| Section | Key | Default | Meaning |
|---|---|---|---|
| `Transport` | `EnableHttp` | `true` | Serve the Streamable HTTP endpoint |
| `Transport` | `HttpPort` | `27016` | HTTP port |
| `Transport` | `HttpPath` | `/mcp` | Endpoint path |
| `Transport` | `EnableTcp` | `true` | Serve the legacy TCP port |
| `Server` | `Port` | `27015` | TCP port |

Both transports share one `MCPServer`, so they expose the same tools. Only **loopback** is bound on
either, so neither port is reachable from the network.

If you only use HTTP clients, set `EnableTcp = false` to expose a single port.

> Implementation notes - spec conformance, the protocol-version negotiation trap, and how this was
> verified without launching the game - are in [docs/mcp-streamable-http.md](../docs/mcp-streamable-http.md).

## Tools

### Diagnostics

| Tool | Notes |
|---|---|
| `read_logs` | Captured BepInEx log lines, filterable; `group_by=prefix` counts by tag |
| `clear_logs` | Clear the capture buffer |
| `main_thread_status` | Is the Unity main thread still completing frames? |
| `get_game_info` | Game/Unity/BepInEx versions, scene, screen, paths |
| `get_time_info` | Unity time, time scale, frame count |

### Code execution

| Tool | Notes |
|---|---|
| `execute_csharp` | Full C# via embedded Mono.CSharp; state persists between calls |
| `evaluate_expression` | Single-expression form of the above |

### Unity inspection and control

| Tool | Notes |
|---|---|
| `get_scene_info`, `list_game_objects` | Scene and hierarchy |
| `find_game_object`, `list_components`, `inspect_component` | **enabled in this port** |
| `toggle_behaviour`, `set_property`, `invoke_method` | **enabled in this port** |
| `find_objects_of_type` | Includes inactive objects on request |
| `inspect_unity_object` | Reads named field paths across instances |
| `inspect_material` | **enabled in this port** |
| `instantiate_object`, `create_primitive`, `destroy_object` | Object lifecycle |
| `set_transform`, `set_time_scale`, `cursor_control`, `load_scene` | Manipulation |
| `take_screenshot` | **enabled in this port.** Writes a PNG to an absolute `path`; never returns the image inline. Optional crop + downscale. |
| `dump_menu_state` | Button enabled/interactable/active flags |

### Reflection, patching and config

| Tool | Notes |
|---|---|
| `list_assemblies`, `list_types`, `get_type_info` | Type system exploration |
| `hook_patch_info`, `list_patches` | Who patched what — see the note below |
| `watch_field`, `unwatch_field` | Poll a field per-frame and record every change |
| `list_configs`, `get_config`, `set_config`, `reset_config` | BepInEx config files |
| `add_game_knowledge`, `get_game_knowledge`, `get_game_summary` | Persisted game notes |
| `set_pseudocode_path`, `search_pseudocode`, `read_pseudocode_file` | Decompiled-source search |

## Migration notes from MelonMCP

### What was removed

The MelonMCP toolset included `disasm`, `read_mem` and `resolve_jump`, backed by **Iced** and by
`Server/NativeMemory.cs` and `Server/DisassemblyHelper.cs`. **All of it is gone from this port**, and
no Iced reference remains.

Those tools existed to answer one IL2CPP-specific question: *"is the native detour actually
installed?"* Under IL2CPP the managed `MethodBase` is a wrapper around code the CLR never runs,
Il2CppInterop rewrites native method entries to `ff 25 <disp32>` jumps, and the real code lives in
`GameAssembly.dll` — so reading the *running* process's memory was the only way to tell an installed
detour from a reported-but-dead one.

A Mono game has none of that:

- There is no `GameAssembly.dll` and no IL2CPP metadata to decode.
- HarmonyX patches **managed method bodies** directly. The native entry does not change when a patch
  is applied, so entry bytes do not answer "is my patch attached".
- Keeping the tools would have been actively misleading: an unchanged prologue would look like "not
  patched" on a method whose Harmony patch is attached and working — the exact false conclusion the
  IL2CPP version was written to prevent, inverted.

`hook_patch_info` therefore reports the patcher type, its validity and the full patch list, and
returns `entryBytesUnavailable` explaining why entry bytes do not apply. `list_patches` is unchanged
and is the tool that matters here.

### What was rewritten

**`execute_csharp` uses the net35 `mcs.dll`, not the net6 one.** This is the single most important
change in the port. Measured with `monodis`:

| Assembly | References |
|---|---|
| `lib/net6/mcs.dll` (MelonMCP's) | `System.Runtime 6.0.0.0`, `System.Collections 6.0.0.0`, +14 more contract assemblies |
| `lib/net35/mcs.dll` (**this port**) | `mscorlib 2.0.0.0`, `System 2.0.0.0`, `System.Core 3.5.0.0`, `MonoMod.RuntimeDetour` |

A Unity Mono 4.x profile has none of those contract assemblies, so loading the net6 build fails at
type-resolution time and surfaces only as "`execute_csharp` is broken". The net35 build references
only assemblies this game actually has — which is also why UnityExplorer ships a net35 `mcs.dll` for
its BepInEx/Mono builds.

`lib/net35/mcs.dll` is **committed to the repository** (1.5 MB) rather than downloaded: it is not on
NuGet, its upstream ([sinai-dev/mcs-unity](https://github.com/sinai-dev/mcs-unity)) ships source only
and carries no license, and the mod's behaviour depends on this exact build's language level and
`MonoMod.RuntimeDetour` requirement. Rationale is recorded in full at the reference in
[`McsMCP.csproj`](McsMCP.csproj). Integrity: `md5 7927cff498e7c67bca6678bb634e736f`
(byte-identical to UnityExplorer's copy).

**Log capture.** MelonLoader exposes static `WarningCallbackHandler` / `ErrorCallbackHandler` events.
BepInEx 5 has no global "all messages" event: it has a static `Logger.Sources` list of `ILogSource`
objects, each with its own `LogEvent`. Sources are also created **lazily**, so subscribing once at
`Awake` misses every mod that has not logged yet. This port subscribes to all known sources and
re-scans every 2 s, guarded by a `HashSet` so a rescan cannot double-subscribe.

**Config tools** are rebuilt on BepInEx `ConfigFile`. BepInEx keeps no public registry of live config
files, so they are found through `Chainloader.Plugins` — each `BaseUnityPlugin` carries its own
`Config`. A plugin GUID is the category identifier, and entries are addressed as `Section.Key`, since
BepInEx section names are free-form and two mods may both have a `General` section. The design intent
is unchanged: go through the live in-memory model, because `Save()` rewrites the whole file from
memory and a hand-edited `.cfg` is silently lost.

**Shutdown and pause handling.** MelonLoader's `_definiteQuit` machinery existed to keep the server
alive across a hot reload so a hung IL2CPP teardown could still be diagnosed; it has no BepInEx
analogue and is gone. The `runInBackground` patch is now applied directly via `AccessTools` instead
of hunting for IL2CPP proxy types at runtime.

### What was enabled

Eight tools were compiled out under IL2CPP with `#if MELONMCP_ENABLE_BROKEN_*` flags, because
Il2CppInterop collapsed every component to a bare `UnityEngine.Component` proxy — so
`component.GetType().Name` never matched and the tools could only report `"Component 'X' not found"`.
**That collapse does not exist under Mono**, and all eight are now registered unconditionally:

`find_game_object`, `list_components`, `inspect_component`, `toggle_behaviour`, `set_property`,
`invoke_method`, `inspect_material`, `take_screenshot`.

> `take_screenshot` needed a second change beyond being re-enabled: returning the PNG inline cost
> ~3M tokens per call (12.3 MB of base64, measured live), larger than any model's context window.
> It now writes to a caller-supplied absolute path, with optional crop and downscale. See
> [docs/mcp-streamable-http.md §10](../docs/mcp-streamable-http.md).

### net472 / Mono portability fixes

These all compiled on the IL2CPP/net6 ancestor and fail on net472. Each is called out in a comment at
its site:

| Issue | Fix |
|---|---|
| `Environment.TickCount64` is .NET Core 3.0+ | `Environment.TickCount` (int), with unchecked subtraction across the 49.7-day wrap |
| `string.Contains(string, StringComparison)` is .NET Core 2.1+ | `IndexOf(...) >= 0` |
| `Path.GetRelativePath` is .NET Core 2.0+ | `GameKnowledgeTools.MakeRelativePath` |
| `StreamReader(Stream, Encoding, bool)` is .NET Core | full 5-arg overload |
| `GetComponentsInChildren<T>()` returns `T[]`, not `Il2CppReferenceArray<T>` | index/`.Length`, not `.Count` |
| `BaseUnityPlugin.Logger` is protected | `public static new ManualLogSource Log` |
| No `UnityEngine.SceneManagementModule.dll` in this build | `SceneManager` lives in `CoreModule` |

## Architecture

```
  MCP client (DeepSeek Harness / Claude Code / ...)
        │
        │  Streamable HTTP  POST http://127.0.0.1:27016/mcp
        │  (no bridge process)
        ▼
  ┌────────────────────────────────────────────┐
  │  MCPHttpServer   ──┐                       │
  │                    ├──▶ MCPServer          │
  │  TCP listener    ──┘     .HandleRequest()  │
  │  :27015 (legacy)         │                 │
  └──────────────────────────┼─────────────────┘
                             │ UnityMainThreadDispatcher
                             ▼
                     ┌──────────────┐
                     │ Unity (Mono) │
                     └──────────────┘
```

Both transports converge on the same `MCPServer.HandleRequest`, so the HTTP layer adds framing and
nothing else - the tool registry, JSON-RPC dispatch and main-thread marshalling are shared and
unchanged.

The MCP server runs on a thread-pool thread. Every tool that touches a Unity object is queued onto
the main thread through `UnityMainThreadDispatcher` and waited on with a **short** timeout; on
timeout, `main_thread_status` is consulted to distinguish "busy" from "wedged". Tools that never
touch Unity (`read_logs`, `list_patches`, `hook_patch_info`, the config tools) run directly on the
calling thread and therefore keep working when the main thread is stuck.

## Security

`execute_csharp` is arbitrary code execution, and the config tools can rewrite any mod's settings.
The listener binds **loopback only**. Do not expose the port.

## License

MIT — see [LICENSE](LICENSE).

## Acknowledgements

- **MelonMCP** — the original MelonLoader/IL2CPP mod this was ported from
- [BepInEx](https://github.com/BepInEx/BepInEx) — the mod framework
- [HarmonyX](https://github.com/BepInEx/HarmonyX) — runtime patching
- [UnityExplorer](https://github.com/sinai-dev/UnityExplorer) — reference for the net35 `mcs.dll` choice
- [Model Context Protocol](https://modelcontextprotocol.io/)
