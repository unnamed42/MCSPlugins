# HTTP transport test harness

Compiles the **real** `Server/MCPHttpServer.cs`, `Server/MCPServer.cs` and `Server/MCPProtocol.cs`
(not copies) into a standalone console app, so the MCP Streamable HTTP transport can be exercised
without launching the game.

See [`docs/mcp-streamable-http.md`](../../../docs/mcp-streamable-http.md) §8 for why this works at
all: the transport layer references no Unity type, which is what makes it separable. Only the plugin
entry point and `FieldWatcher` are stubbed (`stubs.cs`).

## Run

```bash
# Build
dotnet build -c Release

# Quick check: built-in probe, exercises the transport and exits
mono bin/Release/net472/harness.exe --self-test

# Or start a long-running server on a spare port (leave it running)
mono bin/Release/net472/harness.exe 27995

# In another shell: drive it with the REAL MCP client SDK that DSH uses.
# node_modules is gitignored; point NODE_PATH at an existing install instead of
# downloading anything, e.g. the harness shipped with DSH:
DSH_NM=/usr/lib/node_modules/@deepseek-ai/dsh/node_modules
mkdir -p node_modules/@modelcontextprotocol
cp -r $DSH_NM/@modelcontextprotocol/{client,core} node_modules/@modelcontextprotocol/
for p in $(ls $DSH_NM | grep -v '^@'); do [ -d "node_modules/$p" ] || cp -r "$DSH_NM/$p" node_modules/ 2>/dev/null; done

node sdkprobe.mjs  http://127.0.0.1:27995/mcp   # connect + tools/list + tools/call
node sdkprobe2.mjs http://127.0.0.1:27995/mcp   # argument round-trip + inputSchema exposure
```

## Against a running game

`live-check.mjs` runs the same SDK against the REAL in-game server and asserts the behaviour that
matters for this port (19 checks). It is read-only: the one `set_config` probe uses `save:false` and
restores the previous value, so it writes nothing to any `.cfg`.

```bash
node live-check.mjs http://127.0.0.1:27016/mcp
```

`shot-test.mjs` covers `take_screenshot` specifically: full capture, crop, crop+downscale,
downscale-only, an offset crop, and the three error paths (relative path, out-of-bounds crop,
unwritable target). It decodes the PNG **IHDR header** to read the REAL pixel dimensions rather than
trusting the dimensions the tool reports about itself.

```bash
node shot-test.mjs          # writes to ../../output/shots/
```

`level-filter-check.mjs` is a regression check for the `read_logs` `level` + `filter` bug
(docs §11.2): before the fix that combination always returned 0 lines.

All are useful when changing the transport or any tool — they catch the class of bug a
hand-written HTTP probe misses (see below).

> `harness.exe` needs `Newtonsoft.Json.dll` and `BepInEx.dll` next to it. The build copies them from
> the game/BepInEx paths in `test.csproj`; if you run it from elsewhere, copy those two alongside.

`harness.cs`'s `Main` is itself a self-contained probe: running it with no arguments checks status
codes, version negotiation, Origin handling, the notification/request split and batching rejection in
one pass, then exits. `serve.cs`'s `Serve.Main` is the long-running variant that the SDK probes above
connect to.

## Why the real SDK, not just curl

A hand-written HTTP probe passed every check while `tools/list` was still broken — it does not
validate the response against the MCP schema. The official SDK caught a missing `inputSchema` that
would have broken tool discovery for every tool at once.
