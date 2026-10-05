using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace McsMCP.Server
{
    /// <summary>
    /// MCP Protocol message types and structures
    /// Implements JSON-RPC 2.0 based MCP protocol
    /// </summary>
    public static class MCPProtocol
    {
        public const string JSONRPC_VERSION = "2.0";

        /// <summary>
        /// Protocol versions this server can speak, NEWEST FIRST.
        ///
        /// 2025-11-25 is what the current MCP TypeScript SDK prefers (its
        /// LATEST_PROTOCOL_VERSION), and 2024-11-05 was the MelonMCP ancestor's only version.
        /// Everything between is included because the differences that matter for THIS server -
        /// the tool/resource surface, and the JSON-RPC framing - are stable across all of them.
        ///
        /// ★ Note what is deliberately NOT here: 2026-07-28. That revision removes protocol-level
        /// sessions and the GET stream, and introduces the `_meta`-mirrored header contract plus
        /// header/body consistency validation. Accepting it would mean advertising a revision whose
        /// validation rules this server does not implement. Declining it is the honest answer, and
        /// the spec has an explicit answer for that case: respond 400 with an
        /// UnsupportedProtocolVersion error listing what IS supported, and the client negotiates
        /// down. See docs/mcp-streamable-http.md.
        /// </summary>
        public static readonly string[] SupportedProtocolVersions =
        {
            "2025-11-25",
            "2025-06-18",
            "2025-03-26",
            "2024-11-05",
        };

        /// <summary>The newest version this server speaks; the default when a client is silent.</summary>
        public const string MCP_VERSION = "2025-11-25";

        /// <summary>
        /// Version assumed for a request that carries no `MCP-Protocol-Version` header at all.
        ///
        /// The spec permits (does not require) treating a header-less request as 2025-03-26,
        /// because clients written against versions before 2025-06-18 never sent the header. Doing
        /// so keeps older clients working without a config flag, and no client that DOES send the
        /// header is affected.
        /// </summary>
        public const string LegacyVersionWithoutHeader = "2025-03-26";

        public static JsonSerializerSettings JsonSettings { get; } = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) }
        };
    }

    #region Base Message Types

    public class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = MCPProtocol.JSONRPC_VERSION;

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JToken Params { get; set; }
    }

    public class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = MCPProtocol.JSONRPC_VERSION;

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("error")]
        public JsonRpcError Error { get; set; }
    }

    public class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public object Data { get; set; }

        public static JsonRpcError ParseError(string message) => new JsonRpcError { Code = -32700, Message = message };
        public static JsonRpcError InvalidRequest(string message) => new JsonRpcError { Code = -32600, Message = message };
        public static JsonRpcError MethodNotFound(string message) => new JsonRpcError { Code = -32601, Message = message };
        public static JsonRpcError InvalidParams(string message) => new JsonRpcError { Code = -32602, Message = message };
        public static JsonRpcError InternalError(string message) => new JsonRpcError { Code = -32603, Message = message };
    }

    public class JsonRpcNotification
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = MCPProtocol.JSONRPC_VERSION;

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public object Params { get; set; }
    }

    #endregion

    #region MCP Initialize

    public class InitializeParams
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public ClientCapabilities Capabilities { get; set; }

        [JsonProperty("clientInfo")]
        public ClientInfo ClientInfo { get; set; }
    }

    public class ClientCapabilities
    {
        [JsonProperty("roots")]
        public RootsCapability Roots { get; set; }

        [JsonProperty("sampling")]
        public SamplingCapability Sampling { get; set; }
    }

    public class RootsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class SamplingCapability { }

    public class ClientInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public class InitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; } = MCPProtocol.MCP_VERSION;

        [JsonProperty("capabilities")]
        public ServerCapabilities Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public ServerInfo ServerInfo { get; set; }

        [JsonProperty("instructions")]
        public string Instructions { get; set; }
    }

    public class ServerCapabilities
    {
        [JsonProperty("tools")]
        public ToolsCapability Tools { get; set; }

        [JsonProperty("resources")]
        public ResourcesCapability Resources { get; set; }

        [JsonProperty("logging")]
        public LoggingCapability Logging { get; set; }
    }

    public class ToolsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class ResourcesCapability
    {
        [JsonProperty("subscribe")]
        public bool Subscribe { get; set; }

        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class LoggingCapability { }

    public class ServerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    #endregion

    #region Tools

    public class ListToolsResult
    {
        [JsonProperty("tools")]
        public List<ToolInfo> Tools { get; set; } = new List<ToolInfo>();
    }

    public class ToolInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public ToolInputSchema InputSchema { get; set; }
    }

    public class ToolInputSchema
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "object";

        [JsonProperty("properties")]
        public Dictionary<string, ToolPropertySchema> Properties { get; set; } = new Dictionary<string, ToolPropertySchema>();

        [JsonProperty("required")]
        public List<string> Required { get; set; } = new List<string>();

        [JsonProperty("additionalProperties")]
        public bool AdditionalProperties { get; set; } = false;
    }

    public class ToolPropertySchema
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("enum")]
        public List<string> Enum { get; set; }

        [JsonProperty("default")]
        public object Default { get; set; }

        [JsonProperty("items")]
        public ToolPropertySchema Items { get; set; }

        [JsonProperty("minimum")]
        public int? Minimum { get; set; }

        [JsonProperty("maximum")]
        public int? Maximum { get; set; }
    }

    public class CallToolParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public Dictionary<string, JToken> Arguments { get; set; }
    }

    public class CallToolResult
    {
        [JsonProperty("content")]
        public List<ToolContent> Content { get; set; } = new List<ToolContent>();

        [JsonProperty("isError")]
        public bool? IsError { get; set; }
    }

    public class ToolContent
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("data")]
        public string Data { get; set; }

        public static ToolContent TextContent(string text) => new ToolContent { Type = "text", Text = text };
        public static ToolContent ImageContent(string base64Data, string mimeType = "image/png") =>
            new ToolContent { Type = "image", Data = base64Data, MimeType = mimeType };
    }

    #endregion

    #region Resources

    public class ListResourcesResult
    {
        [JsonProperty("resources")]
        public List<ResourceInfo> Resources { get; set; } = new List<ResourceInfo>();
    }

    public class ResourceInfo
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }
    }

    public class ReadResourceParams
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }
    }

    public class ReadResourceResult
    {
        [JsonProperty("contents")]
        public List<ResourceContent> Contents { get; set; } = new List<ResourceContent>();
    }

    public class ResourceContent
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("blob")]
        public string Blob { get; set; }
    }

    #endregion

    #region Logging

    public class LoggingMessageParams
    {
        [JsonProperty("level")]
        public string Level { get; set; }

        [JsonProperty("logger")]
        public string Logger { get; set; }

        [JsonProperty("data")]
        public object Data { get; set; }
    }

    #endregion
}
