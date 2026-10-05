# mcs language-capability probe

Answers "which C# features does the embedded Mono.CSharp compiler actually accept?" empirically,
instead of inferring it from the assembly version.

This exists because the obvious inference is wrong in two ways:

1. `Mono.CSharp.LanguageVersion` tops out at `V_7_2` — but the parser does not gate every feature
   on it, so the enum alone does not tell you what works.
2. `mcs.dll`'s `SkipVisibilityExt` static constructor hard-requires `MonoMod.RuntimeDetour
   22.3.23.4` while this game's BepInEx ships `21.9.19.1`. Without that assembly loaded FIRST,
   **every** `Evaluate()` throws and the probe reports "nothing is supported" — a false result that
   looks like a language problem. `probe.cs` preloads it from the game's BepInEx core.

## Run

```bash
dotnet build -c Release
mono bin/Release/net472/mcsprobe.exe
```

Findings and the full feature matrix: [`docs/mcp-streamable-http.md` §12](../../../docs/mcp-streamable-http.md).

## Why the test types are declared inside the snippet

mcs's REPL rejects a type declaration followed by another statement in a single submission
("Unexpected symbol"). `probe.cs` therefore splits each case into a declaration step and a use
step. Getting this wrong produces spurious failures that look like missing language features.
