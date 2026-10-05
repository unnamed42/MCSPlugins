using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using McsMCP.Server;
using McsMCP.Tools;
using UnityEngine;

namespace McsMCP
{
    /// <summary>
    /// BepInEx 5 (Mono) entry point.
    ///
    /// Ported from the MelonLoader/IL2CPP McsMCP plugin. The shape of the class is deliberately
    /// kept close to the original so the two can be diffed, but three subsystems had no BepInEx
    /// counterpart and were rewritten rather than translated:
    ///
    ///  1. LOG CAPTURE - MelonLoader exposes static WarningCallbackHandler / ErrorCallbackHandler
    ///     events. BepInEx 5 exposes <see cref="ILogSource"/> instances and a static
    ///     <c>BepInEx.Logging.Logger.Sources</c> list, so this subscribes per-source and re-scans on a timer to
    ///     pick up mods that log for the first time later (see SubscribeToLogEvents).
    ///
    ///  2. SHUTDOWN SEMANTICS - MelonLoader had a hot-reload path worth preserving the server
    ///     across. BepInEx does not hot-reload managed plugins in a way that rebinds a TCP port,
    ///     so the elaborate _definiteQuit machinery is gone; OnDestroy releases the socket.
    ///
    ///  3. PAUSE/FOCUS BYPASS - the McsMCP patches existed because IL2CPP could not statically
    ///     reference Application.set_runInBackground. Under Mono it is a plain property, so the
    ///     patch is applied directly and the reflection hunt is unnecessary.
    /// </summary>
    [BepInPlugin(BuildInfo.Guid, BuildInfo.Name, BuildInfo.Version)]
    public class McsMCPPlugin : BaseUnityPlugin
    {
        public static McsMCPPlugin Instance { get; private set; }

        /// <summary>
        /// BepInEx log source for this plugin.
        ///
        /// Deliberately named `Log`, NOT `Logger`: BaseUnityPlugin declares a PROTECTED `Logger`
        /// property, and a public static shadowing it fails to compile at every call site outside
        /// this class with CS0122 ("Logger is inaccessible"). A differently-named public static is
        /// what lets Server/ and Tools/ log at all.
        ///
        /// Assigned in Awake from the protected instance property via `Log = Logger`.
        /// </summary>
        public static ManualLogSource Log { get; private set; }

        private MCPServer _server;
        private MCPHttpServer _httpServer;

        /// <summary>
        /// The JSON-RPC server, exposed so Server/ and Tools/ can reach transport state on it (the
        /// negotiated protocol version, the tool registry). Null before Awake and after teardown.
        /// </summary>
        internal MCPServer Server => _server;

        /// <summary>The Streamable HTTP endpoint URL, or null when that transport is disabled.</summary>
        internal string HttpEndpointUrl => _httpServer?.EndpointUrl;
        private readonly List<string> _logBuffer = new List<string>();
        private readonly object _logLock = new object();
        private const int MaxLogBufferSize = 1000;

        /// <summary>
        /// BepInEx log sources we are currently subscribed to. Tracked so a rescan does not
        /// double-subscribe (which would duplicate every line) and so teardown can detach.
        /// </summary>
        private readonly HashSet<ILogSource> _subscribedSources = new HashSet<ILogSource>();
        private float _logRescanTimer;
        private const float LogRescanIntervalSeconds = 2f;

        /// <summary>Set in OnDestroy so per-frame work stops once the plugin is torn down.</summary>
        private bool _deinitialized;

        // ---- runInBackground enforcement ------------------------------------------------
        // 觅长生 pauses the simulation when it loses focus. An MCP client driving the game from
        // another window therefore stalls unless this is held true. Application.runInBackground is
        // re-asserted for a few seconds after startup because games commonly reset it during their
        // own initialisation, after this plugin's Awake has already run.
        private const int RunInBackgroundCheckFrames = 300;
        private int _runInBackgroundCheckCounter;

        public void Awake()
        {
            Instance = this;
            Log = Logger;

            // ★ Subscribe BEFORE the first log line, not after.
            //
            // BepInEx raises LogEvent only to handlers attached at the time, so anything logged
            // before this point is not in the read_logs buffer. The ordering here is therefore
            // load-bearing: with the subscription placed after this LogInfo (as it originally was),
            // a live run showed `Registered 44 MCP tools` and the transport URLs missing from the
            // buffer - the exact lines needed to answer "did the mod load correctly?".
            SubscribeToLogEvents();

            Logger.LogInfo("McsMCP Initializing...");

            try
            {
                UnityMainThreadDispatcher.Initialize();

                var config = new McsMCPConfig(Config);
                int port = config.Port;
                int httpPort = config.HttpPort;

                _server = new MCPServer(port);
                RegisterTools();

                if (config.EnableTcpTransport)
                {
                    _server.Start();
                    Logger.LogInfo($"McsMCP TCP transport listening on tcp://localhost:{port}");
                }
                else
                {
                    Logger.LogInfo("McsMCP TCP transport disabled in config (Transport/EnableTcp).");
                }

                if (config.EnableHttpTransport)
                {
                    // Deliberately its OWN try/catch, not the surrounding one: the two transports
                    // are independent, and a failure on one must not silently take the other down.
                    // That independence is the whole reason both are offered.
                    try
                    {
                        _httpServer = new MCPHttpServer(_server, httpPort, config.HttpPath);
                        _httpServer.Start();
                        Logger.LogInfo($"McsMCP HTTP (Streamable) transport listening on {_httpServer.EndpointUrl}");
                        Logger.LogInfo("Point an MCP client at that URL - no bridge script needed.");
                    }
                    catch (Exception httpEx)
                    {
                        _httpServer = null;
                        Logger.LogError($"Failed to start the HTTP transport on port {httpPort}: {httpEx.Message}");
                        Logger.LogError("The TCP transport may still work via a bridge script.");
                    }
                }

                if (_httpServer == null && !config.EnableTcpTransport)
                {
                    Logger.LogError("Both transports are disabled or failed - no MCP client can connect.");
                }

                EnsureRunInBackground();
                ApplyFocusPatches();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to start McsMCP server: {ex}");

                // Do not leave a half-built server behind: if the port was already bound (another
                // instance not torn down), the listener may exist but never accept.
                try { _httpServer?.Stop(); } catch { }
                _httpServer = null;
                try { _server?.Stop(); } catch { }
                _server = null;
            }
        }

        public void Update()
        {
            if (_deinitialized) return;

            // Prove to any other thread that the main thread is still alive. This is the only
            // main-thread-side cost the watchdog adds, and it is three volatile writes.
            MainThreadWatchdog.Beat();

            UnityMainThreadDispatcher.ProcessQueue();

            FieldWatcher.Tick();

            RescanLogSources();

            _runInBackgroundCheckCounter++;
            if (_runInBackgroundCheckCounter <= RunInBackgroundCheckFrames
                && _runInBackgroundCheckCounter % 60 == 1)
            {
                EnsureRunInBackground();
            }
        }

        public void OnDestroy()
        {
            _deinitialized = true;

            // The HTTP listener first: it holds the port, and releasing the socket is what unblocks
            // its accept loop.
            try
            {
                _httpServer?.Stop();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error while stopping the MCP HTTP server: {ex.Message}");
            }
            _httpServer = null;

            try
            {
                _server?.Stop();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error while stopping MCP server: {ex.Message}");
            }
            _server = null;

            // BepInEx log sources are static and outlive this plugin. Leaving handlers attached
            // would keep this assembly alive and duplicate lines if the plugin is reloaded.
            try
            {
                foreach (var source in _subscribedSources)
                {
                    source.LogEvent -= OnBepInExLogEvent;
                }
                _subscribedSources.Clear();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error while unsubscribing log handlers: {ex.Message}");
            }

            UnityMainThreadDispatcher.Shutdown();
            FieldWatcher.Reset();
            MainThreadWatchdog.Reset();

            lock (_logLock)
            {
                _logBuffer.Clear();
            }

            if (ReferenceEquals(Instance, this)) Instance = null;

            Logger.LogInfo("McsMCP Server stopped");
        }

        private void RegisterTools()
        {
            // --- Log tools -------------------------------------------------------------
            _server.RegisterTool(new ReadLogsToolDefinition());
            _server.RegisterTool(new ClearLogsToolDefinition());
            // Main-thread liveness. Deliberately grouped with the log tools: these are the
            // operations that must keep working when the game itself has stopped responding.
            _server.RegisterTool(new MainThreadStatusToolDefinition());

            // --- Code execution --------------------------------------------------------
            _server.RegisterTool(new ExecuteCSharpToolDefinition());
            _server.RegisterTool(new EvaluateExpressionToolDefinition());

            // --- Unity inspection ------------------------------------------------------
            // ★ ENABLED in this port. Under IL2CPP every component surfaced as a bare
            // UnityEngine.Component proxy, so GetType().Name never matched and
            // list_components reported every component as "Component". That collapse is an
            // Il2CppInterop artefact and does not exist under Mono, where the real managed
            // component types are present.
            _server.RegisterTool(new GetSceneInfoToolDefinition());
            _server.RegisterTool(new ListGameObjectsToolDefinition());
            _server.RegisterTool(new FindGameObjectToolDefinition());
            _server.RegisterTool(new ListComponentsToolDefinition());
            _server.RegisterTool(new InspectComponentToolDefinition());

            // --- MonoBehaviour control -------------------------------------------------
            _server.RegisterTool(new ToggleBehaviourToolDefinition());
            _server.RegisterTool(new SetPropertyToolDefinition());
            _server.RegisterTool(new InvokeMethodToolDefinition());

            // --- Game state ------------------------------------------------------------
            _server.RegisterTool(new GetGameInfoToolDefinition());
            // take_screenshot works under Mono: the IL2CPP path failed because Texture2D
            // construction and ReadPixels went through generated proxies.
            _server.RegisterTool(new TakeScreenshotToolDefinition());
            _server.RegisterTool(new GetTimeInfoToolDefinition());

            // --- Resources -------------------------------------------------------------
            _server.RegisterTool(new ListAssembliesToolDefinition());
            _server.RegisterTool(new ListTypesToolDefinition());
            _server.RegisterTool(new GetTypeInfoToolDefinition());

            // --- Advanced --------------------------------------------------------------
            _server.RegisterTool(new FindObjectsOfTypeToolDefinition());
            _server.RegisterTool(new SetTimeScaleToolDefinition());
            _server.RegisterTool(new CursorControlToolDefinition());
            _server.RegisterTool(new LoadSceneToolDefinition());
            _server.RegisterTool(new InstantiateObjectToolDefinition());
            _server.RegisterTool(new CreatePrimitiveToolDefinition());
            _server.RegisterTool(new SetTransformToolDefinition());
            _server.RegisterTool(new InspectMaterialToolDefinition());
            _server.RegisterTool(new DestroyObjectToolDefinition());

            // --- Game knowledge --------------------------------------------------------
            _server.RegisterTool(new AddGameKnowledgeToolDefinition());
            _server.RegisterTool(new GetGameKnowledgeToolDefinition());
            _server.RegisterTool(new GetGameSummaryToolDefinition());
            _server.RegisterTool(new SetPseudocodePathToolDefinition());
            _server.RegisterTool(new SearchPseudocodeToolDefinition());
            _server.RegisterTool(new ReadPseudocodeFileToolDefinition());

            // --- Patch / hook debugging ------------------------------------------------
            // Still valuable under Mono, for a different reason than under IL2CPP: here the risk
            // is not an invalid detour but MOD CONFLICTS. 觅长生 runs a large mod stack, and
            // several of them patch the same methods (System.Enum.ToString, AvatarCtr
            // pricing, UI scaling). list_patches is how you find out who else is on a method
            // before blaming your own patch.
            //
            // NOTE: disasm / read_mem / resolve_jump are deliberately NOT ported. They existed
            // to read native IL2CPP code out of GameAssembly.dll; Mono has no such thing.
            _server.RegisterTool(new HookPatchInfoToolDefinition());
            _server.RegisterTool(new ListPatchesToolDefinition());
            _server.RegisterTool(new WatchFieldToolDefinition());
            _server.RegisterTool(new UnwatchFieldToolDefinition());

            _server.RegisterTool(new InspectUnityObjectToolDefinition());

            // --- UI ---------------------------------------------------------------------
            _server.RegisterTool(new DumpMenuStateToolDefinition());

            // --- Configuration ----------------------------------------------------------
            // Rewritten on BepInEx ConfigFile rather than MelonPreferences. Same design intent:
            // go through the live in-memory model, never hand-edit the .cfg.
            _server.RegisterTool(new ListConfigsToolDefinition());
            _server.RegisterTool(new GetConfigToolDefinition());
            _server.RegisterTool(new SetConfigToolDefinition());
            _server.RegisterTool(new ResetConfigToolDefinition());

            Logger.LogInfo($"Registered {_server.ToolCount} MCP tools");
        }

        /// <summary>
        /// Holds Application.runInBackground true so the game keeps simulating while the MCP client
        /// is in another window. Under MelonLoader this had to be done by reflection on IL2CPP
        /// proxies; under Mono it is a direct call.
        /// </summary>
        private void EnsureRunInBackground()
        {
            try
            {
                if (!Application.runInBackground)
                {
                    Application.runInBackground = true;
                    Logger.LogInfo("Set Application.runInBackground = true");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to set runInBackground: {ex.Message}");
            }
        }

        private static bool _focusPatchApplied;

        /// <summary>
        /// Blocks the game from switching runInBackground back off.
        ///
        /// MEASURED BEHAVIOUR driving this: 觅长生 (and the MelonLoader games this mod came from)
        /// reset the flag during their own startup, and at least one known mod toggles it when the
        /// window loses focus. Re-asserting it from Update() covers the startup case; this patch
        /// covers the "something else sets it to false at runtime" case, which Update() cannot
        /// distinguish from the user having asked for it.
        ///
        /// Failures are non-fatal on purpose: on a game that never touches the property this patch
        /// is simply never exercised.
        /// </summary>
        private void ApplyFocusPatches()
        {
            if (_focusPatchApplied) return;

            try
            {
                var setter = AccessTools.PropertySetter(typeof(Application), nameof(Application.runInBackground));
                if (setter == null)
                {
                    Logger.LogWarning("Could not find Application.set_runInBackground; the game may "
                        + "disable background running when it loses focus.");
                    return;
                }

                var prefix = new HarmonyMethod(
                    AccessTools.Method(typeof(McsMCPPlugin), nameof(BlockRunInBackgroundDisable)));
                var harmony = new Harmony(BuildInfo.Guid + ".focus");
                harmony.Patch(setter, prefix: prefix);

                _focusPatchApplied = true;
                Logger.LogInfo("Patched Application.set_runInBackground to block disabling it.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to patch set_runInBackground: {ex.Message}");
            }
        }

        /// <summary>
        /// Prefix for Application.set_runInBackground. Returning false skips the original setter.
        /// Only the false-direction is blocked: setting it true is always allowed through.
        /// </summary>
        private static bool BlockRunInBackgroundDisable(bool value)
        {
            if (!value)
            {
                Log?.LogInfo("Blocked an attempt to disable Application.runInBackground.");
                return false;
            }
            return true;
        }

        #region Log Capture

        /// <summary>
        /// BepInEx 5's logging model is a static list of ILogSource objects, each with its own
        /// LogEvent. There is no global "all messages" event, and - importantly - sources are
        /// created lazily, when a plugin first logs something. Subscribing once at Awake would
        /// therefore miss every source that has not spoken yet.
        ///
        /// So: subscribe to everything visible now, and rescan periodically
        /// (<see cref="RescanLogSources"/>) to attach to sources as they appear. The
        /// <see cref="_subscribedSources"/> set makes this idempotent.
        /// </summary>
        private void SubscribeToLogEvents()
        {
            try
            {
                AttachToAllKnownSources();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not subscribe to BepInEx log sources: {ex.Message}");
            }
        }

        private void RescanLogSources()
        {
            _logRescanTimer += Time.unscaledDeltaTime;
            if (_logRescanTimer < LogRescanIntervalSeconds) return;
            _logRescanTimer = 0f;

            // Cheap after the first few seconds: the source list stops growing once every plugin
            // has logged at least once.
            if (_subscribedSources.Count >= BepInEx.Logging.Logger.Sources.Count) return;

            try
            {
                AttachToAllKnownSources();
            }
            catch
            {
                // A logging rescan must never be able to break the update loop.
            }
        }

        private void AttachToAllKnownSources()
        {
            // Ours is attached FIRST and unconditionally.
            //
            // ★ BepInEx creates an ILogSource lazily, on that source's first log call, so by Awake
            // our source exists and would normally be found by the loop below. The trap is TIMING:
            // hooking it here only captures lines logged AFTER the hook. Measured live - `Client
            // connected` was captured (logged later, from the server thread) while `Registered 44
            // MCP tools` and the two transport URLs were silently missing, which are precisely the
            // lines a reader wants when asking "did this mod load correctly?".
            //
            // Awake therefore subscribes BEFORE its own first LogInfo, and this explicit attach
            // keeps that guarantee independent of list-enumeration order. _subscribedSources makes
            // both idempotent.
            AttachSource(Logger);

            foreach (var source in BepInEx.Logging.Logger.Sources)
            {
                AttachSource(source);
            }
        }

        /// <summary>Subscribes one source, once. Null-safe and idempotent.</summary>
        private void AttachSource(BepInEx.Logging.ILogSource source)
        {
            if (source == null) return;
            if (!_subscribedSources.Add(source)) return; // already attached

            source.LogEvent += OnBepInExLogEvent;
        }

        /// <summary>
        /// Per-source handler. BepInEx raises this with the level already resolved, so the level
        /// name comes from the LogLevel rather than being guessed from the message text.
        /// </summary>
        private void OnBepInExLogEvent(object sender, LogEventArgs args)
        {
            if (args == null) return;

            var level = args.Level switch
            {
                LogLevel.Fatal => "FATAL",
                LogLevel.Error => "ERROR",
                LogLevel.Warning => "WARNING",
                LogLevel.Message => "MSG",
                LogLevel.Info => "INFO",
                LogLevel.Debug => "DEBUG",
                _ => args.Level.ToString().ToUpperInvariant()
            };

            AddToLogBuffer($"[{DateTime.Now:HH:mm:ss}] [{level}] {args.Data}");
        }

        private void AddToLogBuffer(string log)
        {
            lock (_logLock)
            {
                _logBuffer.Add(log);
                if (_logBuffer.Count > MaxLogBufferSize)
                {
                    _logBuffer.RemoveAt(0);
                }
            }
        }

        public List<string> GetLogs(int count = 100, string filter = null)
        {
            lock (_logLock)
            {
                var logs = new List<string>();
                int start = Math.Max(0, _logBuffer.Count - count);

                for (int i = start; i < _logBuffer.Count; i++)
                {
                    if (string.IsNullOrEmpty(filter)
                        || _logBuffer[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        logs.Add(_logBuffer[i]);
                    }
                }

                return logs;
            }
        }

        public void ClearLogs()
        {
            lock (_logLock)
            {
                _logBuffer.Clear();
            }
        }

        #endregion
    }
}
