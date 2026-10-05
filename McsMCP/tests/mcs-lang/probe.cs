// Narrows the switch-pattern behaviour: which pattern forms actually work?
using System; using System.IO; using System.Reflection; using Mono.CSharp;
class Probe4 {
  static Evaluator _ev; static StringWriter _diag; static int _p,_f;
  static void Main() {
    // ★ Preload MonoMod.RuntimeDetour before the first Evaluate().
    // mcs.dll's SkipVisibilityExt..cctor hooks AppDomain.GetAssemblies via MonoMod and hard-requires
    // version 22.3.23.4, while this game's BepInEx ships 21.9.19.1. Without it loaded FIRST every
    // Evaluate() dies with TypeInitializationException -> FileNotFoundException, which reads as
    // "this compiler supports nothing". In the real game BepInEx/HarmonyX have already loaded it.
    var bepCore = Environment.GetEnvironmentVariable("MCS_BEPINEX_CORE")
        ?? "/home/huang/project/MCSPlugins/steamapps/workshop/content/1189490/2824349934/BepInEx/core";
    var mmrd = Path.Combine(bepCore, "MonoMod.RuntimeDetour.dll");
    if (!File.Exists(mmrd)) {
      Console.WriteLine("FATAL: MonoMod.RuntimeDetour.dll not found at " + mmrd);
      Console.WriteLine("Set MCS_BEPINEX_CORE to the game's BepInEx/core directory.");
      return;
    }
    Assembly.LoadFrom(mmrd);
    var st = new CompilerSettings { Version = LanguageVersion.Experimental, StdLib = true, Target = Target.Library, Unsafe = true };
    _diag = new StringWriter();
    _ev = new Evaluator(new CompilerContext(st, new StreamReportPrinter(_diag)));
    foreach (var a in new[]{"mscorlib","System","System.Core"}) { try { _ev.ReferenceAssembly(Assembly.Load(a)); } catch {} }
    foreach (var u in new[]{"System","System.Collections.Generic","System.Linq"}) try { _ev.Run("using "+u+";"); } catch {}
    _ev.Run("static class H { public static object R; }");

    C("switch (int literal)",     "switch (2) { case 1: H.R=\"one\"; break; case 2: H.R=\"two\"; break; default: H.R=\"other\"; break; }");
    C("switch (string literal)",  "switch (\"b\") { case \"a\": H.R=1; break; case \"b\": H.R=2; break; default: H.R=0; break; }");
    C("case int i (type patt)",   "switch ((object)5) { case int i: H.R=i; break; default: H.R=0; break; }");
    C("is T x  (expr pattern)",   "object o=\"s\"; if (o is string str) H.R=str.Length; else H.R=0;");
    C("is T (no binding)",        "object o=5; H.R = o is int;");
    C("case var x",               "switch ((object)5) { case var x: H.R=\"var\"; break; }");
    C("when clause",              "switch ((object)5) { case int i when i>3: H.R=\"big\"; break; default: H.R=\"small\"; break; }");
    Console.WriteLine($"\npass={_p} fail={_f}");
  }
  static void C(string l, string code) {
    _diag.GetStringBuilder().Length = 0;
    try { _ev.Run(code); var d=_diag.ToString();
      if (d.Contains("error")) { _f++; var s=d.Split('\n')[0].Trim(); Console.WriteLine($"  FAIL  {l,-26} => {s.Substring(0,Math.Min(76,s.Length))}"); return; }
      _p++; Console.WriteLine($"  OK    {l,-26} => {_ev.Evaluate("H.R")}");
    } catch (Exception ex) { _f++; var m=ex.Message.Split('\n')[0]; Console.WriteLine($"  FAIL  {l,-26} => {m.Substring(0,Math.Min(76,m.Length))}"); }
  }
}
