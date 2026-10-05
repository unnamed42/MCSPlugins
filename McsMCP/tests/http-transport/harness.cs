// Rehosts the REAL MCPHttpServer + MCPServer + MCPProtocol sources on the host runtime so the
// HTTP behaviour can be exercised over a real socket. The mod itself cannot run here (it needs
// Unity), but the transport layer touches no Unity type - which is exactly the property that
// makes it testable this way.
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

class Harness {
  // Called by Serve.Main for --self-test. Not an entry point itself, so the two Mains do not
  // collide (CS0017) and StartupObject stays unambiguous.
  public static int Run() {
    var port = 27999;
    var server = new McsMCP.Server.MCPServer(port);
    server.RegisterTool(new FakeTool());
    McsMCP.McsMCPPlugin.Instance.Server = server;  // mirrors what Awake does
    var http = new McsMCP.Server.MCPHttpServer(server, port, "/mcp");
    http.Start();
    Console.WriteLine("endpoint: " + http.EndpointUrl);
    Console.Out.Flush();

    var client = new HttpClient();
    var url = http.EndpointUrl;

    // 1. initialize with a supported version
    var init = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"probe\",\"version\":\"1\"}}}";
    var r = Post(client, url, init, "2025-11-25");
    Console.WriteLine($"[initialize] status={(int)r.StatusCode} body={Trunc(r.Body)}");
    var pv = JObject.Parse(r.Body)["result"]?["protocolVersion"]?.ToString();
    Console.WriteLine($"[initialize] negotiated protocolVersion={pv}");

    // 2. unsupported version -> must be 400 + UnsupportedProtocolVersion listing supported
    var r2 = Post(client, url, init, "2026-07-28");
    Console.WriteLine($"[unsupported version] status={(int)r2.StatusCode} body={Trunc(r2.Body)}");

    // 3. no header -> legacy 2025-03-26
    var r3 = Post(client, url, init, null);
    Console.WriteLine($"[no header] status={(int)r3.StatusCode} negotiated={JObject.Parse(r3.Body)["result"]?["protocolVersion"]}");

    // 4. notification -> 202, empty body
    var note = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
    var r4 = Post(client, url, note, "2025-11-25");
    Console.WriteLine($"[notification] status={(int)r4.StatusCode} bodyLen={r4.Body.Length}");

    // 5. tools/list
    var list = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}";
    var r5 = Post(client, url, list, "2025-11-25");
    Console.WriteLine($"[tools/list] status={(int)r5.StatusCode} body={Trunc(r5.Body)}");

    // 6. tools/call
    var call = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"probe_tool\",\"arguments\":{}}}";
    var r6 = Post(client, url, call, "2025-11-25");
    Console.WriteLine($"[tools/call] status={(int)r6.StatusCode} body={Trunc(r6.Body)}");

    // 7. unknown path -> 404
    var r7 = Post(client, url.Replace("/mcp", "/nope"), list, "2025-11-25");
    Console.WriteLine($"[unknown path] status={(int)r7.StatusCode}");

    // 8. GET -> 405 (no SSE stream offered)
    var gr = client.GetAsync(url).GetAwaiter().GetResult();
    Console.WriteLine($"[GET] status={(int)gr.StatusCode} allow={string.Join(",", gr.Content.Headers.ContentType?.MediaType)}");

    // 9. bad Origin -> 403
    var req = new HttpRequestMessage(HttpMethod.Post, url);
    req.Headers.Add("Origin", "http://evil.example");
    req.Headers.Add("MCP-Protocol-Version", "2025-11-25");
    req.Content = new StringContent(list, Encoding.UTF8, "application/json");
    var r9 = client.SendAsync(req).GetAwaiter().GetResult();
    Console.WriteLine($"[bad Origin] status={(int)r9.StatusCode} body={Trunc(r9.Content.ReadAsStringAsync().GetAwaiter().GetResult())}");

    // 10. loopback Origin -> allowed
    var req2 = new HttpRequestMessage(HttpMethod.Post, url);
    req2.Headers.Add("Origin", "http://localhost:5173");
    req2.Headers.Add("MCP-Protocol-Version", "2025-11-25");
    req2.Content = new StringContent(list, Encoding.UTF8, "application/json");
    var r10 = client.SendAsync(req2).GetAwaiter().GetResult();
    Console.WriteLine($"[loopback Origin] status={(int)r10.StatusCode}");

    // 11. batch -> rejected
    var r11 = Post(client, url, "[" + list + "]", "2025-11-25");
    Console.WriteLine($"[batch] status={(int)r11.StatusCode}");

    http.Stop();
    Console.WriteLine("stopped cleanly");
    return 0;
  }

  static string Trunc(string s) => s != null && s.Length > 220 ? s.Substring(0, 220) + "..." : s;

  static (System.Net.HttpStatusCode StatusCode, string Body) Post(HttpClient c, string url, string body, string ver) {
    var req = new HttpRequestMessage(HttpMethod.Post, url);
    if (ver != null) req.Headers.Add("MCP-Protocol-Version", ver);
    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
    var resp = c.SendAsync(req).GetAwaiter().GetResult();
    return (resp.StatusCode, resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
  }
}

class FakeTool : McsMCP.Tools.IToolDefinition {
  public string Name => "probe_tool";
  public string Description => "probe";
  public bool RequiresMainThread => false;
  public McsMCP.Server.ToolInfo GetInfo() => new McsMCP.Server.ToolInfo {
    Name = Name, Description = Description,
    InputSchema = new McsMCP.Server.ToolInputSchema { Properties = new System.Collections.Generic.Dictionary<string, McsMCP.Server.ToolPropertySchema>() } };
  public McsMCP.Server.CallToolResult Execute(System.Collections.Generic.Dictionary<string, JToken> a) {
    var r = new McsMCP.Server.CallToolResult();
    r.Content.Add(McsMCP.Server.ToolContent.TextContent("probe-ok"));
    return r;
  }
}
