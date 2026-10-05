// Long-running variant: starts the real transport and stays up so an external MCP client
// (the actual TypeScript SDK) can connect to it.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

class Serve {
  static int Main(string[] a) {
    // Entry point for the whole harness.
    //   --self-test  -> run the built-in probe in harness.cs, print results, exit (quick check)
    //   <port>       -> start the long-running server the SDK probes connect to
    if (a.Length > 0 && a[0] == "--self-test") return Harness.Run();

    var port = a.Length > 0 ? int.Parse(a[0]) : 27999;
    var server = new McsMCP.Server.MCPServer(port);
    server.RegisterTool(new FakeTool());
    server.RegisterTool(new SecondTool());
    McsMCP.McsMCPPlugin.Instance.Server = server;
    var http = new McsMCP.Server.MCPHttpServer(server, port, "/mcp");
    http.Start();
    Console.WriteLine("listening: " + http.EndpointUrl);
    Console.Out.Flush();
    System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
    return 0;
  }
}

class SecondTool : McsMCP.Tools.IToolDefinition {
  public string Name => "echo_args";
  public string Description => "echoes its arguments";
  public bool RequiresMainThread => false;
  public McsMCP.Server.ToolInfo GetInfo() => new McsMCP.Server.ToolInfo {
    Name = Name, Description = Description,
    InputSchema = new McsMCP.Server.ToolInputSchema {
      Properties = new Dictionary<string, McsMCP.Server.ToolPropertySchema> {
        ["text"] = new McsMCP.Server.ToolPropertySchema { Type = "string", Description = "text to echo" }
      }
    }
  };
  public McsMCP.Server.CallToolResult Execute(Dictionary<string, JToken> a) {
    var r = new McsMCP.Server.CallToolResult();
    r.Content.Add(McsMCP.Server.ToolContent.TextContent(a.TryGetValue("text", out var v) ? v.ToString() : "(none)"));
    return r;
  }
}
