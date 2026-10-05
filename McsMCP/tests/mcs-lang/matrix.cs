// The full C# feature matrix for this mcs build. Run with: dotnet build -c Release && mono .../mcsprobe.exe matrix
// Kept separate from probe.cs (the switch-pattern narrowing) so both can run.
using System; using System.IO; using System.Reflection; using Mono.CSharp;

class Matrix {
  static Evaluator _ev; static StringWriter _diag; static int _p, _f;
  static void Main() {
    var bepCore = Environment.GetEnvironmentVariable("MCS_BEPINEX_CORE")
        ?? "/home/huang/project/MCSPlugins/steamapps/workshop/content/1189490/2824349934/BepInEx/core";
    Assembly.LoadFrom(Path.Combine(bepCore, "MonoMod.RuntimeDetour.dll"));
    var st = new CompilerSettings { Version = LanguageVersion.Experimental, StdLib = true, Target = Target.Library, Unsafe = true };
    _diag = new StringWriter();
    _ev = new Evaluator(new CompilerContext(st, new StreamReportPrinter(_diag)));
    foreach (var a in new[]{"mscorlib","System","System.Core"}) { try { _ev.ReferenceAssembly(Assembly.Load(a)); } catch {} }
    foreach (var u in new[]{"System","System.Collections.Generic","System.Linq","System.Text"}) try { _ev.Run("using "+u+";"); } catch {}
    _ev.Run("static class H { public static object R; }");

    Console.WriteLine("--- supported ---");
    D("C# 3  auto-property",        "class A { public int V { get; set; } }",        "H.R = new A{V=3}.V;");
    D("C# 3  LINQ (lambda form)",   null, "H.R = System.Linq.Enumerable.Count(System.Linq.Enumerable.Where(new[]{1,2,3}, n => n > 1));");
    D("C# 4  dynamic",              null, "H.R = ((dynamic)5) + 1;");
    D("C# 6  expression-bodied",    "class B { public int V => 6; }",               "H.R = new B().V;");
    D("C# 6  auto-prop initializer","class C { public int V { get; set; } = 60; }",  "H.R = new C().V;");
    D("C# 6  interpolation",        null, "var n=1; H.R = $\"n={n}\";");
    D("C# 6  null-conditional",     null, "string s=null; H.R = s?.Length ?? -1;");
    D("C# 6  nameof",               null, "H.R = nameof(System.String);");
    D("C# 7  tuples",               null, "var t=(a:1,b:2); H.R = t.a + t.b;");
    D("C# 7  deconstruction",       null, "var (p,q)=(3,4); H.R = p+q;");
    D("C# 7  out var",              null, "if (int.TryParse(\"5\", out var v)) H.R=v; else H.R=0;");
    D("C# 7  is-pattern",           null, "object o=\"s\"; if (o is string str) H.R=str.Length; else H.R=0;");
    D("C# 7  throw expression",     "class E { public int M(int a) => a>0 ? a : throw new Exception(\"x\"); }", "H.R=new E().M(1);");
    D("C# 7.1 default literal",     null, "int d = default; H.R = d;");
    D("C# 7.2 readonly struct",     "struct S { public readonly int X; public S(int x){X=x;} }", "H.R=new S(7).X;");

    Console.WriteLine("\n--- NOT supported (note the failure KIND) ---");
    D("C# 7  case int i:",          null, "switch ((object)5) { case int i: H.R=i; break; default: H.R=0; break; }");
    D("C# 7  case var x:",          null, "switch ((object)5) { case var x: H.R=1; break; }");
    D("C# 7  when clause",          null, "switch ((object)5) { case int i when i>3: H.R=1; break; default: H.R=0; break; }");
    D("C# 7  local function",       null, "int F(int a) { return a+1; } H.R = F(1);");
    D("C# 7.2 in-parameter",        "class I { public void M(in int a) {} }", "int z=1; new I().M(in z); H.R=z;");
    D("C# 8  switch expression",    null, "H.R = 1 switch { 1 => 2, _ => 0 };");
    D("C# 8  using declaration",    null, "using var sw = new StringWriter(); H.R = 1;");
    D("C# 9  target-typed new",     null, "System.Collections.Generic.List<int> l = new(); H.R = l.Count;");
    C("C# 3  LINQ query syntax",    "H.R = System.Linq.Enumerable.Count(from n in new[]{1,2,3} where n > 1 select n);");

    Console.WriteLine($"\npass={_p} fail={_f}");
  }
  static void D(string l, string decl, string use) {
    if (decl != null) { _diag.GetStringBuilder().Length = 0; try { _ev.Run(decl); } catch {}
      if (_diag.ToString().Contains("error")) { _f++; Console.WriteLine($"  FAIL  {l,-28} => declaration rejected"); return; } }
    C(l, use);
  }
  static void C(string l, string code) {
    _diag.GetStringBuilder().Length = 0;
    try { _ev.Run(code); var d=_diag.ToString();
      if (d.Contains("error")) { _f++; var s=d.Split('\n')[0].Trim(); Console.WriteLine($"  FAIL  {l,-28} => {s.Substring(0,Math.Min(80,s.Length))}"); return; }
      _p++; Console.WriteLine($"  OK    {l,-28} => {_ev.Evaluate("H.R")}");
    } catch (Exception ex) { _f++; var m=ex.Message.Split('\n')[0]; Console.WriteLine($"  FAIL  {l,-28} => {m.Substring(0,Math.Min(80,m.Length))}"); }
  }
}
