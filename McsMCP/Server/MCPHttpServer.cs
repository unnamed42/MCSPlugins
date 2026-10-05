using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace McsMCP.Server
{
    /// <summary>
    /// MCP "Streamable HTTP" transport, served straight out of the game process.
    ///
    /// WHY THIS EXISTS: the TCP transport in MCPServer speaks newline-delimited JSON-RPC, which is
    /// the stdio framing. No MCP client can connect to that directly - the stdio binding makes the
    /// CLIENT launch the server as a subprocess, so a TCP listener always needs a translation
    /// process in between (the mcp-bridge.py/js/ps1 scripts). Implementing the protocol's HTTP
    /// transport removes that process entirely: the client is configured with a URL and nothing else.
    ///
    /// WHAT IT REUSES: everything. This class owns only HTTP framing; the JSON-RPC dispatch, the
    /// tool registry and the main-thread marshalling all stay in MCPServer.HandleRequest. The TCP
    /// listener is deliberately kept as well, so both transports serve the same tools.
    ///
    /// ─── Spec conformance ────────────────────────────────────────────────────────────────────────
    /// Implements the Streamable HTTP transport as of protocol revisions 2025-03-26 … 2025-11-25:
    ///
    ///   • ONE endpoint path, POST only. A single JSON-RPC message per POST.
    ///   • A request gets `Content-Type: application/json` with a single JSON object. The SSE
    ///     alternative is optional - the client must support both, the server chooses one - and JSON
    ///     is the right choice here because no tool emits progress notifications or server-initiated
    ///     requests.
    ///   • A notification gets `202 Accepted` with NO body.
    ///   • `MCP-Protocol-Version` is validated; an unsupported value is rejected with 400 and an
    ///     `UnsupportedProtocolVersion` error advertising the supported list.
    ///   • `Origin` is validated on every request (DNS-rebinding defence); a present-but-untrusted
    ///     Origin gets 403.
    ///   • Loopback-only bind.
    ///
    /// NOT implemented, on purpose:
    ///   • SSE response streams and the standalone GET stream (`subscriptions/listen`). Both are
    ///     optional, and this server pushes nothing. A GET is answered 405 Method Not Allowed, which
    ///     is exactly what the spec prescribes for a server that offers no GET stream.
    ///   • Protocol-level sessions (`Mcp-Session-Id`). Introduced in 2025-03-26, they become optional
    ///     in the 2025-06-18 revision and are removed entirely in 2026-07-28. This server is
    ///     stateless and returns no session id; clients must treat that as "no session", which the
    ///     spec explicitly allows.
    ///   • `x-mcp-header` tool-parameter mirroring. It is a server MAY, and no tool here needs it.
    /// </summary>
    internal sealed class MCPHttpServer
    {
        private readonly MCPServer _server;
        private readonly int _port;
        private readonly string _path;

        /// <summary>
        /// Origin values accepted on incoming requests.
        ///
        /// MEASURED, and the reason this list is not just "reject everything": an MCP client that is
        /// a native process (the DSH harness, Claude Code, an SDK program) sends NO Origin header at
        /// all, while a browser-based client always does. Rejecting absent Origins would block the
        /// normal case; accepting arbitrary ones would leave the DNS-rebinding hole the spec warns
        /// about. So: absent is allowed, and present must be a loopback origin.
        /// </summary>
        private static readonly string[] AllowedOriginHosts = { "localhost", "127.0.0.1", "[::1]", "::1" };

        private HttpListener _listener;
        private CancellationTokenSource _cancellation;
        private Task _acceptLoop;

        public bool IsRunning { get; private set; }

        /// <summary>The URL an MCP client should be configured with.</summary>
        public string EndpointUrl => $"http://127.0.0.1:{_port}{_path}";

        public MCPHttpServer(MCPServer server, int port, string path)
        {
            _server = server;
            _port = port;

            // Normalize to a single leading slash and no trailing slash, so "/mcp", "mcp" and "/mcp/"
            // all configure the same endpoint. HttpListener matches on the exact prefix string, so an
            // un-normalized value silently stops matching rather than erroring.
            var p = string.IsNullOrWhiteSpace(path) ? "/mcp" : path.Trim();
            if (!p.StartsWith("/", StringComparison.Ordinal)) p = "/" + p;
            _path = p.TrimEnd('/');
            if (_path.Length == 0) _path = "/mcp";
        }

        public void Start()
        {
            if (IsRunning) return;

            // 127.0.0.1 rather than "+" or "*": the spec says bind loopback when running locally,
            // and this endpoint can rewrite any mod's config and execute arbitrary C#.
            var prefix = $"http://127.0.0.1:{_port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);

            // Throws HttpListenerException when the port is taken - surfaced to the caller rather
            // than swallowed, because "the transport silently did not start" is the worst outcome.
            _listener.Start();

            _cancellation = new CancellationTokenSource();
            IsRunning = true;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_listener, _cancellation.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            _cancellation?.Cancel();

            // Capture and null the field before stopping: the accept loop re-reads _listener, and
            // nulling it while the loop is parked in GetContextAsync turns the expected
            // HttpListenerException into a NullReferenceException that skips the clean exit.
            var listener = _listener;
            _listener = null;

            try { listener?.Stop(); } catch { }
            try { listener?.Close(); } catch { }

            var loop = _acceptLoop;
            _acceptLoop = null;
            if (loop != null)
            {
                try { loop.Wait(TimeSpan.FromSeconds(5)); }
                catch (Exception ex) { McsMCPPlugin.Log?.LogWarning($"HTTP accept loop did not stop cleanly: {ex.Message}"); }
            }

            _cancellation?.Dispose();
            _cancellation = null;
        }

        private async Task AcceptLoopAsync(HttpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break; // listener stopped
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!IsRunning) break;
                    McsMCPPlugin.Log?.LogWarning($"HTTP accept error: {ex.Message}");
                    continue;
                }

                // Handle off the accept loop so one slow tool call (a main-thread tool waiting on a
                // wedged game) cannot block every other client. Each request is independent because
                // the server is stateless.
                _ = Task.Run(() => HandleContextAsync(context));
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context)
        {
            try
            {
                await ProcessAsync(context);
            }
            catch (Exception ex)
            {
                McsMCPPlugin.Log?.LogWarning($"HTTP request failed: {ex.Message}");
                TryFail(context, 500, JsonRpcError.InternalError(ex.Message));
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private async Task ProcessAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // --- Origin validation (spec: MUST; DNS-rebinding defence) -------------------------
            var origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && !IsAllowedOrigin(origin))
            {
                // Spec: respond 403; the body MAY be an id-less JSON-RPC error response.
                McsMCPPlugin.Log?.LogWarning($"Rejected request with disallowed Origin '{origin}'.");
                await WriteJsonAsync(response, 403, new JsonRpcResponse
                {
                    Id = null,
                    Error = JsonRpcError.InvalidRequest(
                        $"Origin '{origin}' is not allowed. This server accepts loopback origins only.")
                });
                return;
            }

            // --- Path routing ------------------------------------------------------------------
            var path = request.Url?.AbsolutePath ?? "/";
            if (!string.Equals(path.TrimEnd('/'), _path, StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, 404, new JsonRpcResponse
                {
                    Id = null,
                    Error = JsonRpcError.MethodNotFound(
                        $"Unknown path '{path}'. The MCP endpoint is '{_path}'.")
                });
                return;
            }

            // --- Method routing ----------------------------------------------------------------
            // The GET stream is optional and unimplemented; 405 is the spec's prescribed answer, and
            // it is deliberately distinguishable from 404 (which would mean "wrong URL").
            if (request.HttpMethod == "GET")
            {
                response.StatusCode = 405;
                response.AddHeader("Allow", "POST");
                await WriteBodyAsync(response, "text/plain",
                    "This server does not offer an SSE stream on the MCP endpoint. Use POST.\n");
                return;
            }

            if (request.HttpMethod != "POST")
            {
                response.StatusCode = 405;
                response.AddHeader("Allow", "POST");
                await WriteBodyAsync(response, "text/plain", "Only POST is supported on the MCP endpoint.\n");
                return;
            }

            // --- Protocol version validation ---------------------------------------------------
            var protocolError = ValidateProtocolVersion(request, out var negotiatedVersion);
            if (protocolError != null)
            {
                await WriteJsonAsync(response, 400, protocolError);
                return;
            }

            // --- Body ---------------------------------------------------------------------------
            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                await WriteJsonAsync(response, 400, new JsonRpcResponse
                {
                    Id = null,
                    Error = JsonRpcError.ParseError("Empty request body.")
                });
                return;
            }

            // A single message per POST (spec). Reject a batch explicitly rather than silently
            // handling only the first element, which would look like data loss to the caller.
            var trimmed = body.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                await WriteJsonAsync(response, 400, new JsonRpcResponse
                {
                    Id = null,
                    Error = JsonRpcError.InvalidRequest(
                        "JSON-RPC batching is not supported on this endpoint; send one message per POST.")
                });
                return;
            }

            JsonRpcRequest rpcRequest;
            try
            {
                rpcRequest = JsonConvert.DeserializeObject<JsonRpcRequest>(body, MCPProtocol.JsonSettings);
            }
            catch (JsonException ex)
            {
                await WriteJsonAsync(response, 400, new JsonRpcResponse
                {
                    Id = null,
                    Error = JsonRpcError.ParseError($"Invalid JSON: {ex.Message}")
                });
                return;
            }

            if (rpcRequest == null || string.IsNullOrEmpty(rpcRequest.Method))
            {
                await WriteJsonAsync(response, 400, new JsonRpcResponse
                {
                    Id = null,
                    Error = JsonRpcError.InvalidRequest("Body is not a JSON-RPC request with a 'method'.")
                });
                return;
            }

            // --- Notification vs request -------------------------------------------------------
            // A JSON-RPC notification has no id. The spec requires 202 with no body for these.
            if (rpcRequest.Id == null)
            {
                try { _server.HandleRequest(rpcRequest); }
                catch (Exception ex) { McsMCPPlugin.Log?.LogWarning($"Notification '{rpcRequest.Method}' failed: {ex.Message}"); }

                response.StatusCode = 202;
                response.ContentLength64 = 0;
                return;
            }

            // --- Dispatch ----------------------------------------------------------------------
            // Tell the server which revision this request is under, so initialize echoes the
            // negotiated value instead of a hardcoded constant.
            _server.RequestProtocolVersion = negotiatedVersion;

            var rpcResponse = _server.HandleRequest(rpcRequest);

            // A request that produced no response would hang the client; synthesize an empty result
            // rather than returning 204 (which the client would read as a transport error).
            if (rpcResponse == null)
            {
                rpcResponse = new JsonRpcResponse { Id = rpcRequest.Id, Result = new { } };
            }

            await WriteJsonAsync(response, 200, rpcResponse);
        }

        /// <summary>
        /// Validates the `MCP-Protocol-Version` header against the versions this server speaks.
        ///
        /// Returns null when the request may proceed, and populates
        /// <paramref name="negotiatedVersion"/> with the revision to answer under.
        /// </summary>
        private JsonRpcResponse ValidateProtocolVersion(HttpListenerRequest request, out string negotiatedVersion)
        {
            var header = request.Headers["MCP-Protocol-Version"];

            if (string.IsNullOrWhiteSpace(header))
            {
                // Spec: a server MAY treat a missing header as 2025-03-26, for clients written before
                // the header existed. Doing so is strictly more compatible and cannot affect a client
                // that does send it.
                negotiatedVersion = MCPProtocol.LegacyVersionWithoutHeader;
                return null;
            }

            header = header.Trim();

            if (MCPProtocol.SupportedProtocolVersions.Contains(header, StringComparer.Ordinal))
            {
                negotiatedVersion = header;
                return null;
            }

            negotiatedVersion = null;

            // Spec: 400 + UnsupportedProtocolVersion listing supported versions, so the client can
            // retry with one of them. The error code is a positive JSON-RPC-reserved-space value
            // chosen by this server and mirrored in `data.supported`, which is what a client actually
            // reads - the code alone is not part of the JSON-RPC standard registry.
            return new JsonRpcResponse
            {
                Id = null,
                Error = new JsonRpcError
                {
                    Code = -32000,
                    Message = $"Unsupported MCP protocol version '{header}'.",
                    Data = new
                    {
                        type = "UnsupportedProtocolVersion",
                        requested = header,
                        supported = MCPProtocol.SupportedProtocolVersions
                    }
                }
            };
        }

        /// <summary>
        /// True when the Origin is a loopback origin.
        ///
        /// Only the HOST is checked, not the port or scheme: a local MCP client's Origin is not
        /// meaningful for routing, and the threat being defended against is a REMOTE page
        /// (http://evil.example) resolving a name to 127.0.0.1, which this rejects.
        /// </summary>
        private static bool IsAllowedOrigin(string origin)
        {
            try
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                return AllowedOriginHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
        {
            var json = JsonConvert.SerializeObject(payload, MCPProtocol.JsonSettings);
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            await WriteBodyAsync(response, null, json);
        }

        private static async Task WriteBodyAsync(HttpListenerResponse response, string contentType, string body)
        {
            if (contentType != null) response.ContentType = contentType;

            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            await response.OutputStream.FlushAsync();
        }

        /// <summary>
        /// Best-effort error reply used when something failed outside ProcessAsync. Never allowed to
        /// throw: the response may already be partly written, and an exception here would escape into
        /// the accept loop.
        /// </summary>
        private static void TryFail(HttpListenerContext context, int statusCode, JsonRpcError error)
        {
            try
            {
                var response = context.Response;
                if (response.OutputStream == null) return;

                var json = JsonConvert.SerializeObject(
                    new JsonRpcResponse { Id = null, Error = error }, MCPProtocol.JsonSettings);
                var bytes = Encoding.UTF8.GetBytes(json);

                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch
            {
                // Nothing useful left to do.
            }
        }
    }
}
