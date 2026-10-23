using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

var repo = Path.GetFullPath(args[0]);
var outPath = args.Length > 1 ? args[1] : null;
var cases = new[]
{
    new ControlCase("direct-nullable-input", Source("str", "§C{System.Environment.GetEnvironmentVariable} §A \"CALOR_N4_UNSET\" §/C")),
    new ControlCase("nonnull-to-nullable-target", Source("?str", "\"safe\"")),
    new ControlCase("already-applicable-object-alternative", Source("str", "§C{System.Environment.GetEnvironmentVariable} §A \"CALOR_N4_UNSET\" §/C", """
          §F{object:Take:pub} (object:value) -> i32
            §E{}
            §R 2
        """)),
    new ControlCase("old-numeric-ambiguity-preserved", Source("i64", "1", """
          §F{double:Take:pub} (f64:value) -> i32
            §E{}
            §R 2
        """)),
    new ControlCase("old-nullable-string-ambiguity-handoff", ConvertAmbiguousCSharp()),
    new ControlCase("invisible-argument-suppression", """
        §M{m1:NativeString}
          §F{take:Take:pub} (str:value,Invisible:other) -> i32
            §E{}
            §R 1
          §F{caller:Caller:pub} (?str:value,Invisible:other) -> i32
            §E{}
            §R §C{Take} §A value §A other §/C
        """)
};

var results = cases.Select(c => Measure(repo, c)).ToArray();
var payload = new
{
    Repo = repo,
    Commit = RunGit(repo, "rev-parse HEAD"),
    BinderBlob = RunGit(repo, "rev-parse HEAD:src/Calor.Compiler/Binding/Binder.cs"),
    ScopeBlob = RunGit(repo, "rev-parse HEAD:src/Calor.Compiler/Binding/Scope.cs"),
    Dotnet = Run("dotnet", "--version", repo),
    Cases = results
};
var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
if (outPath is null) Console.WriteLine(json); else File.WriteAllText(outPath, json + "\n");
return 0;

static string ConvertAmbiguousCSharp()
{
    var converted = new Calor.Compiler.Migration.CSharpToCalorConverter().Convert("""
        public static class NativeString
        {
            public static int Take(string value) => 1;
            public static int Take(object value, int count = 0) => 2;
            public static int Take(object value, bool enabled = false) => 3;
            public static int Caller(string? value) => Take(value);
        }
        """);
    if (converted.CalorSource is null)
        throw new InvalidOperationException("ambiguous C# control did not convert");
    return converted.CalorSource;
}

static string Source(string targetType, string argument, string extraOverload = "") => $$"""
    §M{m1:NativeString}
      §F{take:Take:pub} ({{targetType}}:value) -> i32
        §E{}
        §R 1
    {{extraOverload}}
      §F{caller:Caller:pub} () -> i32
        §E{env}
        §R §C{Take} §A {{argument}} §/C
    """;

static object Measure(string repo, ControlCase c)
{
    var source = c.Source.Replace("\r\n", "\n");
    var parseDiagnostics = new DiagnosticBag();
    var module = new Parser(new Lexer(source, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics).Parse();
    var bindDiagnostics = new DiagnosticBag();
    var bound = new Binder(bindDiagnostics).Bind(module);
    var propagated = new DiagnosticBag();
    BindingDiagnosticPolicy.PropagateCompilationErrors(bindDiagnostics, propagated);
    var all = bindDiagnostics.Errors.Select(d => ToRecord(d, source, propagated)).ToArray();
    return new
    {
        c.Name,
        SourceSha256 = Sha(source),
        ParseDiagnostics = parseDiagnostics.Select(d => new { d.Code, d.Message, d.Span.Start, d.Span.End, d.Span.Line, d.Span.Column }).ToArray(),
        BindingDiagnostics = all,
        PropagatedDiagnostics = all.Where(d => d.Propagated).ToArray(),
        Calls = Walk(bound).Select(n => n switch
        {
            BoundCallStatement s => CallRecord.FromStatement(s),
            BoundCallExpression e => CallRecord.FromExpression(e),
            _ => null
        }).Where(x => x is not null).ToArray()
    };
}

static DiagRecord ToRecord(Diagnostic d, string source, DiagnosticBag propagated) => new(
    d.Code, d.Severity.ToString(), d.Span.Start, d.Span.End, d.Span.Line, d.Span.Column,
    d.Message, d.BindingContext?.Boundary.ToString(), d.BindingContext?.Shape.ToString(),
    ReplacesFlag(d.BindingContext), SafeDisposition(d), propagated.Errors.Any(p =>
        p.Code == d.Code && p.Span == d.Span && p.Message == d.Message && p.Severity == d.Severity),
    Snippet(source, d.Span));

static bool ReplacesFlag(object? context)
{
    if (context is null) return false;
    var property = context.GetType().GetProperty("ReplacesNativeOverloadError");
    return property?.GetValue(context) as bool? ?? false;
}

static string SafeDisposition(Diagnostic d)
{
    try { return BindingDiagnosticPolicy.GetRule(d.Code, d.BindingContext).Disposition.ToString(); }
    catch { return "<uncataloged>"; }
}

static IEnumerable<BoundNode> Walk(BoundNode root)
{
    var stack = new Stack<BoundNode>();
    stack.Push(root);
    while (stack.TryPop(out var node))
    {
        yield return node;
        foreach (var child in node.ChildNodes.Reverse()) stack.Push(child);
    }
}

static string Snippet(string text, TextSpan span) =>
    span.Start >= 0 && span.End <= text.Length && span.Length >= 0
        ? text.Substring(span.Start, span.Length).Replace("\n", "\\n") : "";

static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

static string RunGit(string repo, string arguments) => Run("git", arguments, repo).Trim();

static string Run(string fileName, string arguments, string workingDirectory)
{
    var start = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var p = System.Diagnostics.Process.Start(start)!;
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    return stdout + stderr;
}

sealed record ControlCase(string Name, string Source);
sealed record DiagRecord(string Code, string Severity, int Start, int End, int Line, int Column,
    string Message, string? Boundary, string? Shape, bool ReplacesNativeOverloadError,
    string Disposition, bool Propagated, string Snippet);

sealed record CallRecord(string Kind, string Target, int Start, int End, string ResultOrVoid,
    List<ArgumentRecord> Arguments, List<string?>? ArgumentNames, List<string?>? ArgumentModifiers,
    List<string>? TypeArguments, List<MatchRecord> SelectedOverloadMatches)
{
    public static CallRecord FromStatement(BoundCallStatement s) => new("statement", s.Target, s.Span.Start, s.Span.End, "void",
        s.Arguments.Select((a, i) => ArgumentRecord.From(a, i)).ToList(), s.ArgumentNames?.ToList(),
        s.ArgumentModifiers?.ToList(), s.TypeArguments?.ToList(), s.SelectedOverloadMatches.Select(MatchRecord.From).ToList());
    public static CallRecord FromExpression(BoundCallExpression e) => new("expression", e.Target, e.Span.Start, e.Span.End, e.Type.DisplayString,
        e.Arguments.Select((a, i) => ArgumentRecord.From(a, i)).ToList(), e.ArgumentNames?.ToList(),
        e.ArgumentModifiers?.ToList(), e.TypeArguments?.ToList(), e.SelectedOverloadMatches.Select(MatchRecord.From).ToList());
}
sealed record ArgumentRecord(int Index, int Start, int End, string Type, string Kind)
{
    public static ArgumentRecord From(BoundExpression e, int i) => new(i, e.Span.Start, e.Span.End, e.Type.DisplayString, e.GetType().Name);
}
sealed record MatchRecord(string FunctionName, string DisplaySignature, string ReturnType,
    List<string> TypeParameters, string? ContainingTypeName, List<ParameterRecord> Parameters,
    List<MappingRecord> Mappings)
{
    public static MatchRecord From(ResolvedOverloadMatch m) => new(m.Function.Name, m.Function.DisplaySignature, m.ReturnType,
        m.Function.TypeParameters.ToList(), m.Function.ContainingTypeName,
        m.Function.Parameters.Select((p, i) => ParameterRecord.From(p, i)).ToList(),
        m.Arguments.Select(a => new MappingRecord(a.ArgumentIndex, a.ParameterIndex, a.ParameterType, a.IsExpandedParams)).ToList());
}
sealed record ParameterRecord(int Index, string Name, string TypeName, string Modifier, bool IsOptional)
{
    public static ParameterRecord From(VariableSymbol p, int i) => new(i, p.Name, p.TypeName, p.Modifier.ToString(), p.IsOptional);
}
sealed record MappingRecord(int ArgumentIndex, int ParameterIndex, string EffectiveParameterType, bool IsExpandedParams);
