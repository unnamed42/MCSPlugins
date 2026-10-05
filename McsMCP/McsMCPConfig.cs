using BepInEx.Configuration;

namespace McsMCP
{
    /// <summary>
    /// McsMCP's own BepInEx configuration entries.
    ///
    /// Kept deliberately small. The tools that let an agent READ AND WRITE other mods' configs
    /// (list_configs / get_config / set_config / reset_config) work off every ConfigFile in the
    /// process via reflection - see Tools/ConfigTools.cs - so nothing here needs to enumerate
    /// other plugins.
    ///
    /// The entries are registered through Config.Bind in the plugin's Awake, which is what puts
    /// them into BepInEx's in-memory ConfigFile model. That matters: the config tools resolve that
    /// same live model rather than parsing McsMCP.cfg from disk, for the same reason the
    /// MelonPreferences-based originals did - BepInEx rewrites the whole file from memory on
    /// Save(), so a hand-edited .cfg is silently lost the next time anything saves.
    /// </summary>
    public sealed class McsMCPConfig
    {
        private readonly ConfigEntry<int> _port;
        private readonly ConfigEntry<bool> _enableLogCapture;
        private readonly ConfigEntry<bool> _enableTcpTransport;
        private readonly ConfigEntry<bool> _enableHttpTransport;
        private readonly ConfigEntry<int> _httpPort;
        private readonly ConfigEntry<string> _httpPath;

        public const int DefaultPort = 27015;

        public McsMCPConfig(ConfigFile config)
        {
            _port = config.Bind(
                "Server",
                "Port",
                DefaultPort,
                new ConfigDescription(
                    "Port the MCP server listens on (both transports share it). Only loopback is "
                    + "bound, so this is not exposed to the network.",
                    new AcceptableValueRange<int>(1024, 65535)));

            _enableHttpTransport = config.Bind(
                "Transport",
                "EnableHttp",
                true,
                "Serve the MCP Streamable HTTP endpoint. This is the transport an MCP client "
                + "connects to DIRECTLY - configure it with the URL and no bridge script. "
                + "Recommended.");

            _httpPath = config.Bind(
                "Transport",
                "HttpPath",
                "/mcp",
                "Path of the MCP endpoint, relative to the HTTP port. A leading slash is "
                + "added if missing and a trailing slash is ignored.");

            // Deliberately its own port. Two listeners cannot bind the same port, so sharing the
            // TCP port would mean whichever transport started second simply failed - and since both
            // are enabled by default, that clash would be the COMMON case rather than an edge case.
            _httpPort = config.Bind(
                "Transport",
                "HttpPort",
                27016,
                new ConfigDescription(
                    "Port for the Streamable HTTP endpoint. Separate from the TCP transport port on "
                    + "purpose: they are independent listeners. Only loopback is bound.",
                    new AcceptableValueRange<int>(1024, 65535)));

            _enableTcpTransport = config.Bind(
                "Transport",
                "EnableTcp",
                true,
                "Serve the legacy newline-delimited JSON-RPC TCP port. Kept for the bridge "
                + "scripts; anything speaking Streamable HTTP does not need it, and turning it "
                + "off exposes a single port.");

            _enableLogCapture = config.Bind(
                "Logging",
                "EnableLogCapture",
                true,
                "Capture BepInEx log output into the buffer served by the read_logs tool. "
                + "Disabling this only stops NEW lines being buffered; it does not affect the "
                + "BepInEx console itself.");
        }

        public int Port => _port.Value;

        public bool EnableLogCapture => _enableLogCapture.Value;

        /// <summary>
        /// The newline-delimited-JSON-RPC TCP listener.
        ///
        /// ON by default so nothing that already works stops working, but this is the transport
        /// that NEEDS a bridge process, so a deployment using only Streamable-HTTP clients can turn
        /// it off and expose a single port.
        /// </summary>
        public bool EnableTcpTransport => _enableTcpTransport.Value;

        /// <summary>
        /// The Streamable HTTP endpoint - the one an MCP client can be pointed at directly, with no
        /// bridge. ON by default: it is the recommended way to connect.
        /// </summary>
        public bool EnableHttpTransport => _enableHttpTransport.Value;

        /// <summary>Port of the Streamable HTTP endpoint (independent of the TCP port).</summary>
        public int HttpPort => _httpPort.Value;

        public string HttpPath => _httpPath.Value;
    }
}
