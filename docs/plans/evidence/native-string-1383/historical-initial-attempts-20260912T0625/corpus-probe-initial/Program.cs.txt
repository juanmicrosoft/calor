using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: CorpusProbe <repoRoot> <repoRelativeCorpusFile> [outJson]");
    return 2;
}

var repo = Path.GetFullPath(args[0]);
var relative = args[1].Replace('\\', '/');
var file = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
var source = File.ReadAllText(file).Replace("\r\n", "\n");
var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
    Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
    Microsoft.CodeAnalysis.DocumentationMode.Parse,
    Microsoft.CodeAnalysis.SourceCodeKind.Regular,
    preprocessorSymbols: Array.Empty<string>());
var conversion = new Calor.Compiler.Migration.CSharpToCalorConverter(
    new Calor.Compiler.Migration.ConversionOptions
    {
        Fidelity = Calor.Compiler.Migration.ConversionFidelity.Lossy,
        PreprocessorMode = Calor.Compiler.Migration.PreprocessorConversionMode.SelectActiveBranchLossy,
        ParseOptions = parseOptions,
        DefinedSymbols = Array.Empty<string>(),
        ModuleName = "Leg2",
        GracefulFallback = true,
        AutoGenerateIds = true
    }).Convert(source, Path.GetFileName(file));
var calor = (conversion.CalorSource ?? "").Replace("\r\n", "\n");
var parseDiagnostics = new DiagnosticBag();
var module = new Parser(new Lexer(calor, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics).Parse();
var bindDiagnostics = new DiagnosticBag();
var binder = new Binder(bindDiagnostics);
var bound = binder.Bind(module);
var propagated = new DiagnosticBag();
BindingDiagnosticPolicy.PropagateCompilationErrors(bindDiagnostics, propagated);
var propagatedKeys = propagated.Errors.Select(DiagKey).ToHashSet(StringComparer.Ordinal);

var effectDiagnostics = new DiagnosticBag();
bool effectRan = false, effectFaulted = false;
if (!propagated.HasErrors)
{
    try
    {
        new Calor.Compiler.Effects.EffectEnforcementPass(effectDiagnostics, resolver: HermeticResolver()).Enforce(module);
        effectRan = true;
    }
    catch
    {
        effectFaulted = true;
    }
}

var allCalls = Walk(bound).Select(n => n switch
{
    BoundCallStatement s => CallRecord.FromStatement(s),
    BoundCallExpression e => CallRecord.FromExpression(e),
    _ => null
}).Where(c => c is not null).Cast<CallRecord>().ToList();

var diagRecords = bindDiagnostics.Errors.Select(d => new DiagRecord(
    d.Code,
    d.Severity.ToString(),
    d.Span.Start,
    d.Span.End,
    d.Span.Length,
    d.Span.Line,
    d.Span.Column,
    d.Message,
    d.BindingContext?.Boundary.ToString(),
    d.BindingContext?.Shape.ToString(),
    ReplacesFlag(d.BindingContext),
    SafeDisposition(d),
    propagatedKeys.Contains(DiagKey(d)),
    Snippet(calor, d.Span),
    allCalls.Where(c => OverlapsOrContains(c.Start, c.End, d.Span.Start, d.Span.End)).ToList()
)).ToList();

var result = new
{
    Repo = repo,
    Commit = RunGit(repo, "rev-parse HEAD"),
    File = relative,
    SourceSha256 = Sha(source),
    Conversion = new
    {
        conversion.Success,
        CalorLength = calor.Length,
        Losses = conversion.Losses.Select(l => new { l.Kind, l.Line, l.Feature, l.Description }).ToArray(),
        Issues = conversion.Issues.Select(i => new { i.Severity, i.Message, i.Line }).ToArray()
    },
    ParseDiagnostics = parseDiagnostics.Select(d => new { d.Code, d.Message, d.Span.Start, d.Span.End, d.Span.Line, d.Span.Column }).ToArray(),
    BinderExpressionsBound = binder.ExpressionsBound,
    Diagnostics = diagRecords,
    PropagatedDiagnostics = diagRecords.Where(d => d.Propagated).ToList(),
    EffectRan = effectRan,
    EffectFaulted = effectFaulted,
    EffectDiagnostics = effectDiagnostics.Select(d => new { d.Code, d.Severity, d.Span.Start, d.Span.End, d.Span.Line, d.Span.Column, d.Message, Snippet = Snippet(calor, d.Span) }).ToArray(),
    Calls = allCalls,
    CalorSource = calor
};
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
if (args.Length >= 3)
    File.WriteAllText(args[2], json + "\n");
else
    Console.WriteLine(json);
return 0;

static Calor.Compiler.Effects.EffectResolver HermeticResolver()
{
    var resolver = new Calor.Compiler.Effects.EffectResolver(
        new Calor.Compiler.Effects.Manifests.ManifestLoader(loadUserLevelManifests: false));
    resolver.Initialize(projectDirectory: null, solutionDirectory: null);
    return resolver;
}

static string DiagKey(Diagnostic d) => $"{d.Code}:{d.Span.Start}:{d.Span.End}:{d.Message}:{d.Severity}";

static bool ReplacesFlag(object? context)
{
    if (context is null) return false;
    var property = context.GetType().GetProperty("ReplacesNativeOverloadError");
    return property?.GetValue(context) as bool? ?? false;
}

static string SafeDisposition(Diagnostic d)
{
    try
    {
        return BindingDiagnosticPolicy.GetRule(d.Code, d.BindingContext).Disposition.ToString();
    }
    catch
    {
        return "<uncataloged>";
    }
}

static bool OverlapsOrContains(int a0, int a1, int b0, int b1) =>
    a0 <= b1 && b0 <= a1 || (b0 <= a0 && a1 <= b1) || (a0 <= b0 && b1 <= a1);

static IEnumerable<BoundNode> Walk(BoundNode root)
{
    var stack = new Stack<BoundNode>();
    stack.Push(root);
    while (stack.TryPop(out var node))
    {
        yield return node;
        foreach (var child in node.ChildNodes.Reverse())
            stack.Push(child);
    }
}

static string Snippet(string text, TextSpan span)
{
    if (span.Start < 0 || span.End > text.Length || span.Length < 0) return "";
    return text.Substring(span.Start, span.Length).Replace("\n", "\\n");
}

static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

static string RunGit(string repo, string arguments)
{
    var start = new System.Diagnostics.ProcessStartInfo("git", arguments)
    {
        WorkingDirectory = repo,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var p = System.Diagnostics.Process.Start(start)!;
    var s = p.StandardOutput.ReadToEnd().Trim();
    p.WaitForExit();
    return s;
}

sealed record DiagRecord(
    string Code,
    string Severity,
    int Start,
    int End,
    int Length,
    int Line,
    int Column,
    string Message,
    string? Boundary,
    string? Shape,
    bool ReplacesNativeOverloadError,
    string Disposition,
    bool Propagated,
    string Snippet,
    List<CallRecord> RelatedCalls);

sealed record CallRecord(
    string Kind,
    string Target,
    int Start,
    int End,
    string ResultOrVoid,
    List<ArgumentRecord> Arguments,
    List<string?>? ArgumentNames,
    List<string?>? ArgumentModifiers,
    List<string>? TypeArguments,
    List<MatchRecord> SelectedOverloadMatches,
    string? ResolvedTypeName,
    string? ResolvedMethodName,
    List<string>? ResolvedParameterTypes)
{
    public static CallRecord FromStatement(BoundCallStatement s) => new(
        "statement", s.Target, s.Span.Start, s.Span.End, "void",
        s.Arguments.Select((a, i) => ArgumentRecord.From(a, i)).ToList(),
        s.ArgumentNames?.ToList(), s.ArgumentModifiers?.ToList(), s.TypeArguments?.ToList(),
        s.SelectedOverloadMatches.Select(MatchRecord.From).ToList(),
        s.ResolvedTypeName, s.ResolvedMethodName, s.ResolvedParameterTypes?.ToList());

    public static CallRecord FromExpression(BoundCallExpression e) => new(
        "expression", e.Target, e.Span.Start, e.Span.End, e.Type.DisplayString,
        e.Arguments.Select((a, i) => ArgumentRecord.From(a, i)).ToList(),
        e.ArgumentNames?.ToList(), e.ArgumentModifiers?.ToList(), e.TypeArguments?.ToList(),
        e.SelectedOverloadMatches.Select(MatchRecord.From).ToList(),
        e.ResolvedTypeName, e.ResolvedMethodName, e.ResolvedParameterTypes?.ToList());
}

sealed record ArgumentRecord(int Index, int Start, int End, string Type, string Kind)
{
    public static ArgumentRecord From(BoundExpression e, int i) =>
        new(i, e.Span.Start, e.Span.End, e.Type.DisplayString, e.GetType().Name);
}

sealed record MatchRecord(
    string FunctionName,
    string DisplaySignature,
    string ReturnType,
    List<string> TypeParameters,
    string? ContainingTypeName,
    List<ParameterRecord> Parameters,
    List<MappingRecord> Mappings)
{
    public static MatchRecord From(ResolvedOverloadMatch m) => new(
        m.Function.Name,
        m.Function.DisplaySignature,
        m.ReturnType,
        m.Function.TypeParameters.ToList(),
        m.Function.ContainingTypeName,
        m.Function.Parameters.Select((p, i) => ParameterRecord.From(p, i)).ToList(),
        m.Arguments.Select(a => new MappingRecord(a.ArgumentIndex, a.ParameterIndex, a.ParameterType, a.IsExpandedParams)).ToList());
}

sealed record ParameterRecord(int Index, string Name, string TypeName, string Modifier, bool IsOptional, string? DefaultValue)
{
    public static ParameterRecord From(VariableSymbol p, int i) =>
        new(i, p.Name, p.TypeName, p.Modifier.ToString(), p.IsOptional, p.DefaultValue?.ToString());
}

sealed record MappingRecord(int ArgumentIndex, int ParameterIndex, string EffectiveParameterType, bool IsExpandedParams);
