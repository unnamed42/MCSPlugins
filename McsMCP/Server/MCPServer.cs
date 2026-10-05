using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using McsMCP.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McsMCP.Server
{
    /// <summary>
    /// MCP Server implementation using TCP with JSON-RPC 2.0
    /// Supports multiple concurrent clients
    /// </summary>
    public class MCPServer
    {
        /// <summary>
        /// How long a main-thread tool may wait for the Unity main thread before giving up.
        ///
        /// Short on purpose. When the main thread is healthy, tools run in milliseconds, so this is
        /// never reached in normal use. When it is wedged, waiting longer buys nothing and costs the
        /// caller the ability to react - a 400ms liveness probe plus a clear verdict is worth far more
        /// than 30 seconds of silence followed by a misleading 'the game may be paused'.
        /// </summary>
        private const int MainThreadToolTimeoutSeconds = 3;

        private readonly int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cancellation;
        private readonly ConcurrentDictionary<Guid, MCPClientHandler> _clients = new ConcurrentDictionary<Guid, MCPClientHandler>();
        private readonly Dictionary<string, IToolDefinition> _tools = new Dictionary<string, IToolDefinition>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The background accept loop, tracked so Stop() can wait for it to unwind.</summary>
        private Task _acceptLoop;

        private bool _isRunning;

        public int ToolCount => _tools.Count;
        public bool IsRunning => _isRunning;

        /// <summary>
        /// The protocol revision the CURRENT request arrived under, set by the HTTP transport from
        /// the `MCP-Protocol-Version` header just before dispatch.
        ///
        /// This is a plain field rather than a parameter threaded through HandleRequest because the
        /// tool implementations (which is where `initialize` is answered) take only the request, and
        /// widening that signature would touch every tool for one value that is purely a transport
        /// concern.
        ///
        /// ★ It is safe precisely because the transports pump requests to HandleRequest on the main
        /// thread one at a time: the HTTP accept loop fans out to worker tasks, but every one of them
        /// ends up queued through the same main-thread path before a tool runs, so no two requests
        /// are inside HandleRequest concurrently. If that ever changes (parallel dispatch), this must
        /// become an AsyncLocal or an explicit parameter - a shared field would then leak one
        /// request's version into another's response.
        ///
        /// Null means "unset", which happens on the TCP transport and on any direct call.
        /// </summary>
        public string RequestProtocolVersion { get; internal set; }

        /// <summary>
        /// Picks the version to answer `initialize` with, in the client's order of preference:
        ///
        ///   1. the negotiated transport version (HTTP: the validated header);
        ///   2. otherwise the version the CLIENT proposed in params.protocolVersion, if this server
        ///      supports it - the TCP transport has no header, and echoing the client's own proposal
        ///      is both what the spec asks for and what keeps older clients working;
        ///   3. otherwise this server's newest supported version.
        ///
        /// Step 2 deliberately checks SUPPORT rather than echoing blindly: answering with a version
        /// the server cannot actually speak would be a lie that the client only discovers later.
        /// </summary>
        private static string ResolveNegotiatedVersion(JsonRpcRequest request)
        {
            var transport = McsMCPPlugin.Instance?.Server?.RequestProtocolVersion;
            if (!string.IsNullOrEmpty(transport)) return transport;

            try
            {
                var proposed = request.Params?["protocolVersion"]?.ToString();
                if (!string.IsNullOrEmpty(proposed)
                    && MCPProtocol.SupportedProtocolVersions.Contains(proposed, StringComparer.Ordinal))
                {
                    return proposed;
                }
            }
            catch
            {
                // A malformed params object must not turn a version echo into a failed initialize.
            }

            return MCPProtocol.MCP_VERSION;
        }

        public MCPServer(int port)
        {
            _port = port;
        }

        public void RegisterTool(IToolDefinition tool)
        {
            _tools[tool.Name] = tool;
            McsMCPPlugin.Log?.LogInfo($"Registered tool: {tool.Name}");
        }

        public void Start()
        {
            if (_isRunning) return;

            _cancellation = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _isRunning = true;

            // Start accepting clients in background
            _acceptLoop = Task.Run(AcceptClientsAsync);
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellation?.Cancel();

            // Disconnect all clients
            foreach (var client in _clients.Values)
            {
                client.Disconnect();
            }
            _clients.Clear();

            // Capture the listener in a local and give the accept loop its own reference to it.
            // AcceptClientsAsync reads _listener on every iteration, so nulling the field while the
            // loop is still parked in AcceptTcpClientAsync turns the expected ObjectDisposedException
            // into a NullReferenceException and skips the clean break.
            var listener = _listener;
            _listener = null;

            try
            {
                listener?.Stop();
            }
            catch { }

            // Wait for the accept loop to observe the shutdown. This is what actually makes the port
            // reusable: if a previous instance of this plugin is still shutting down and holds the
            // listener socket, the next Start() throws at TcpListener.Start() with
            // 'address already in use'. Waiting here is what makes an in-process reload safe.
            var loop = _acceptLoop;
            _acceptLoop = null;
            if (loop != null)
            {
                try
                {
                    loop.Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    McsMCPPlugin.Log?.LogWarning($"Accept loop did not shut down cleanly: {ex.Message}");
                }
            }

            _cancellation?.Dispose();
            _cancellation = null;
        }

        private async Task AcceptClientsAsync()
        {
            // Take a local reference to the listener: Stop() nulls the field before the loop is
            // guaranteed to have observed the cancellation, and we want the ObjectDisposedException
            // path below, not a NullReferenceException.
            var listener = _listener;

            while (_isRunning && _cancellation != null && !_cancellation.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    var clientId = Guid.NewGuid();
                    var handler = new MCPClientHandler(clientId, client, this);
                    _clients[clientId] = handler;

                    McsMCPPlugin.Log?.LogInfo($"Client connected: {clientId}");

                    // Handle client in background
                    _ = Task.Run(() => handler.HandleClientAsync(_cancellation.Token));
                }
                catch (ObjectDisposedException)
                {
                    // Server stopped
                    break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        McsMCPPlugin.Log?.LogWarning($"Error accepting client: {ex.Message}");
                    }
                }
            }
        }

        internal void OnClientDisconnected(Guid clientId)
        {
            _clients.TryRemove(clientId, out _);
            McsMCPPlugin.Log?.LogInfo($"Client disconnected: {clientId}");
        }

        internal JsonRpcResponse HandleRequest(JsonRpcRequest request)
        {
            try
            {
                return request.Method switch
                {
                    "initialize" => HandleInitialize(request),
                    "initialized" => HandleInitialized(request),
                    "ping" => HandlePing(request),
                    "tools/list" => HandleListTools(request),
                    "tools/call" => HandleCallTool(request),
                    "resources/list" => HandleListResources(request),
                    "resources/read" => HandleReadResource(request),
                    _ => new JsonRpcResponse
                    {
                        Id = request.Id,
                        Error = JsonRpcError.MethodNotFound($"Unknown method: {request.Method}")
                    }
                };
            }
            catch (Exception ex)
            {
                McsMCPPlugin.Log?.LogError($"Error handling request {request.Method}: {ex}");
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = JsonRpcError.InternalError(ex.Message)
                };
            }
        }

        private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
        {
            // Echo the version negotiated for THIS request rather than a fixed constant.
            //
            // The client proposes a version in initialize.params.protocolVersion and expects the
            // server to answer with the one it will use. Answering with a hardcoded value that the
            // client never proposed is what makes a client abort the connection: it reads the
            // mismatch as an incompatible server. The HTTP transport sets RequestProtocolVersion
            // from the MCP-Protocol-Version header; the TCP transport leaves it null, and then the
            // client's own proposal (or the default) is used.
            var result = new InitializeResult
            {
                ProtocolVersion = ResolveNegotiatedVersion(request),
                Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = false },
                    Resources = new ResourcesCapability { Subscribe = false, ListChanged = false },
                    Logging = new LoggingCapability()
                },
                ServerInfo = new ServerInfo
                {
                    Name = "McsMCP",
                    Version = BuildInfo.Version
                },
                Instructions = "McsMCP provides tools to interact with running Unity games through BepInEx (Mono). " +
                               "You can read logs, execute C# code at runtime, inspect and control MonoBehaviours, " +
                               "and explore the game's object hierarchy."
            };

            return new JsonRpcResponse { Id = request.Id, Result = result };
        }

        private JsonRpcResponse HandleInitialized(JsonRpcRequest request)
        {
            // No response needed for notification
            return null;
        }

        private JsonRpcResponse HandlePing(JsonRpcRequest request)
        {
            return new JsonRpcResponse { Id = request.Id, Result = new { } };
        }

        private JsonRpcResponse HandleListTools(JsonRpcRequest request)
        {
            var result = new ListToolsResult();

            foreach (var tool in _tools.Values)
            {
                result.Tools.Add(tool.GetInfo());
            }

            return new JsonRpcResponse { Id = request.Id, Result = result };
        }

        private JsonRpcResponse HandleCallTool(JsonRpcRequest request)
        {
            CallToolParams callParams;
            try
            {
                callParams = request.Params?.ToObject<CallToolParams>(JsonSerializer.Create(MCPProtocol.JsonSettings));
                if (callParams == null)
                {
                    return new JsonRpcResponse
                    {
                        Id = request.Id,
                        Error = JsonRpcError.InvalidParams("Missing tool call params")
                    };
                }
            }
            catch (Exception ex)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = JsonRpcError.InvalidParams($"Failed to parse tool call params: {ex.Message}")
                };
            }

            if (!_tools.TryGetValue(callParams.Name, out var tool))
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = JsonRpcError.MethodNotFound($"Tool not found: {callParams.Name}")
                };
            }

            try
            {
                CallToolResult result = null;
                Exception toolException = null;

                // Check if tool needs main thread
                if (tool.RequiresMainThread)
                {
                    // Execute tool on main thread for Unity access
                    var resetEvent = new ManualResetEventSlim(false);

                    // Queue work on Unity main thread
                    UnityMainThreadDispatcher.Enqueue(() =>
                    {
                        try
                        {
                            result = tool.Execute(callParams.Arguments ?? new Dictionary<string, JToken>());
                        }
                        catch (Exception ex)
                        {
                            toolException = ex;
                        }
                        finally
                        {
                            resetEvent.Set();
                        }
                    });

                    // Wait for completion with timeout. The timeout is deliberately short: a
                    // main-thread tool that has not run within a couple of seconds is not going to
                    // run soon, and the caller can decide what to do far better if it gets an answer
                    // quickly than if it blocks for half a minute.
                    if (!resetEvent.Wait(TimeSpan.FromSeconds(MainThreadToolTimeoutSeconds)))
                    {
                        // Do not guess. Ask the watchdog whether frames are still completing, which
                        // cleanly separates 'busy/paused' (retry) from 'wedged' (go to the OS).
                        MainThreadStatus status;
                        try { status = MainThreadWatchdog.Probe(400); }
                        catch (Exception probeEx) { status = null; McsMCPPlugin.Log?.LogWarning($"Watchdog probe failed: {probeEx.Message}"); }

                        var sb = new System.Text.StringBuilder();
                        sb.Append($"Tool '{callParams.Name}' needs the Unity main thread and did not run within {MainThreadToolTimeoutSeconds}s.");
                        if (status != null)
                        {
                            sb.Append($" Main-thread verdict: {status.Verdict}");
                            sb.Append($" ({status.FramesDuringWindow} frame(s) in {status.ProbedForMs}ms).");
                            if (status.StallSeconds > 0)
                            {
                                sb.Append($" Last frame was {status.StallSeconds:F1}s ago.");
                            }
                            sb.Append(' ').Append(status.Explanation);
                            if (!string.IsNullOrEmpty(status.NativeHint))
                            {
                                sb.Append(' ').Append(status.NativeHint);
                            }
                        }

                        return new JsonRpcResponse
                        {
                            Id = request.Id,
                            Result = new CallToolResult
                            {
                                Content = new List<ToolContent> { ToolContent.TextContent(sb.ToString()) },
                                IsError = true
                            }
                        };
                    }
                }
                else
                {
                    // Execute directly - tool doesn't need main thread
                    try
                    {
                        result = tool.Execute(callParams.Arguments ?? new Dictionary<string, JToken>());
                    }
                    catch (Exception ex)
                    {
                        toolException = ex;
                    }
                }

                if (toolException != null)
                {
                    return new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = new CallToolResult
                        {
                            Content = new List<ToolContent> { ToolContent.TextContent($"Tool execution error: {toolException.Message}\n{toolException.StackTrace}") },
                            IsError = true
                        }
                    };
                }

                return new JsonRpcResponse { Id = request.Id, Result = result };
            }
            catch (Exception ex)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = new CallToolResult
                    {
                        Content = new List<ToolContent> { ToolContent.TextContent($"Error: {ex.Message}") },
                        IsError = true
                    }
                };
            }
        }

        private JsonRpcResponse HandleListResources(JsonRpcRequest request)
        {
            var result = new ListResourcesResult
            {
                Resources = new List<ResourceInfo>
                {
                    new ResourceInfo
                    {
                        Uri = "mcsmcp://logs/melon",
                        Name = "BepInEx Logs",
                        Description = "Recent BepInEx log output",
                        MimeType = "text/plain"
                    },
                    new ResourceInfo
                    {
                        Uri = "mcsmcp://scene/hierarchy",
                        Name = "Scene Hierarchy",
                        Description = "Current Unity scene object hierarchy",
                        MimeType = "application/json"
                    },
                    new ResourceInfo
                    {
                        Uri = "mcsmcp://game/info",
                        Name = "Game Information",
                        Description = "Information about the running game",
                        MimeType = "application/json"
                    }
                }
            };

            return new JsonRpcResponse { Id = request.Id, Result = result };
        }

        private JsonRpcResponse HandleReadResource(JsonRpcRequest request)
        {
            ReadResourceParams readParams;
            try
            {
                readParams = request.Params?.ToObject<ReadResourceParams>(JsonSerializer.Create(MCPProtocol.JsonSettings));
                if (readParams == null)
                {
                    return new JsonRpcResponse
                    {
                        Id = request.Id,
                        Error = JsonRpcError.InvalidParams("Missing resource read params")
                    };
                }
            }
            catch (Exception ex)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = JsonRpcError.InvalidParams($"Failed to parse resource read params: {ex.Message}")
                };
            }

            var result = new ReadResourceResult();

            switch (readParams.Uri)
            {
                case "mcsmcp://logs/melon":
                    var logs = McsMCPPlugin.Instance.GetLogs(200);
                    result.Contents.Add(new ResourceContent
                    {
                        Uri = readParams.Uri,
                        MimeType = "text/plain",
                        Text = string.Join("\n", logs)
                    });
                    break;

                case "mcsmcp://scene/hierarchy":
                case "mcsmcp://game/info":
                    // Execute on main thread
                    string content = null;
                    var resetEvent = new ManualResetEventSlim(false);

                    UnityMainThreadDispatcher.Enqueue(() =>
                    {
                        try
                        {
                            content = readParams.Uri == "mcsmcp://scene/hierarchy"
                                ? UnityHelper.GetSceneHierarchyJson()
                                : UnityHelper.GetGameInfoJson();
                        }
                        finally
                        {
                            resetEvent.Set();
                        }
                    });

                    resetEvent.Wait(TimeSpan.FromSeconds(10));

                    result.Contents.Add(new ResourceContent
                    {
                        Uri = readParams.Uri,
                        MimeType = "application/json",
                        Text = content ?? "{}"
                    });
                    break;

                default:
                    return new JsonRpcResponse
                    {
                        Id = request.Id,
                        Error = JsonRpcError.InvalidParams($"Unknown resource: {readParams.Uri}")
                    };
            }

            return new JsonRpcResponse { Id = request.Id, Result = result };
        }
    }

    /// <summary>
    /// Handles individual MCP client connections
    /// </summary>
    internal class MCPClientHandler
    {
        private readonly Guid _clientId;
        private readonly TcpClient _client;
        private readonly MCPServer _server;
        private NetworkStream _stream;

        public MCPClientHandler(Guid clientId, TcpClient client, MCPServer server)
        {
            _clientId = clientId;
            _client = client;
            _server = server;
        }

        public async Task HandleClientAsync(CancellationToken cancellationToken)
        {
            try
            {
                _stream = _client.GetStream();
                // net472 has no StreamReader(Stream, Encoding, bool) constructor - that overload
                // with leaveOpen arrived in .NET Core. Passing the full argument list explicitly is
                // the net472-equivalent: detectEncodingFromByteOrderMarks:true (the BOM behaviour the
                // 3-arg overload defaults to) and bufferSize:1024, with leaveOpen:true because this
                // reader is disposed by `using` while _stream must stay open for the response writer.
                using var reader = new StreamReader(_stream, Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);

                while (!cancellationToken.IsCancellationRequested && _client.Connected)
                {
                    // Read line (JSON-RPC messages are newline-delimited)
                    var line = await reader.ReadLineAsync();
                    if (line == null) break; // Connection closed

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var request = JsonConvert.DeserializeObject<JsonRpcRequest>(line, MCPProtocol.JsonSettings);
                        var response = _server.HandleRequest(request);

                        if (response != null)
                        {
                            await SendResponseAsync(response);
                        }
                    }
                    catch (JsonException ex)
                    {
                        var errorResponse = new JsonRpcResponse
                        {
                            Id = null,
                            Error = JsonRpcError.ParseError($"Invalid JSON: {ex.Message}")
                        };
                        await SendResponseAsync(errorResponse);
                    }
                }
            }
            catch (Exception ex)
            {
                McsMCPPlugin.Log?.LogWarning($"Client handler error: {ex.Message}");
            }
            finally
            {
                Disconnect();
                _server.OnClientDisconnected(_clientId);
            }
        }

        private async Task SendResponseAsync(JsonRpcResponse response)
        {
            try
            {
                var json = JsonConvert.SerializeObject(response, MCPProtocol.JsonSettings);
                var bytes = Encoding.UTF8.GetBytes(json + "\n");
                await _stream.WriteAsync(bytes, 0, bytes.Length);
                await _stream.FlushAsync();
            }
            catch (Exception ex)
            {
                McsMCPPlugin.Log?.LogWarning($"Failed to send response: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }
        }
    }
}
