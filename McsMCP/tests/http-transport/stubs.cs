// Stand-ins ONLY for the pieces MCPServer/MCPHttpServer reference that are not part of the
// transport layer (the plugin entry point, which needs Unity/BepInEx; and the field watcher, which
// needs Unity). Everything transport-relevant - MCPServer, MCPHttpServer, MCPProtocol,
// UnityMainThreadDispatcher, MainThreadWatchdog - is the REAL source, compiled unmodified.
using System;
using System.Collections.Generic;

namespace McsMCP {
  public static class BuildInfo { public const string Version = "test"; public const string Name = "McsMCP"; }
  public class McsMCPPlugin {
    public static McsMCPPlugin Instance = new McsMCPPlugin();
    public static BepInEx.Logging.ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("test");
    internal McsMCP.Server.MCPServer Server;
    public List<string> GetLogs(int c = 100, string f = null) => new List<string>();
    public void ClearLogs() { }
  }
}

namespace McsMCP.Tools {
  internal static class FieldWatcher { public static void Tick() { } public static void Reset() { } }
}

namespace McsMCP.Server {
  // Only the two methods MCPServer's resources/read path calls. The real UnityHelper needs Unity.
  public static class UnityHelper {
    public static string GetSceneHierarchyJson() => "{}";
    public static string GetGameInfoJson() => "{}";
  }
}
