using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: NativeBoundaryProbe <repoRoot> <outJson> <scratchRoot>");
    return 2;
}

var repo = Path.GetFullPath(args[0]);
var outPath = Path.GetFullPath(args[1]);
var scratchRoot = Path.GetFullPath(args[2]);
Directory.CreateDirectory(scratchRoot);
Directory.SetCurrentDirectory(repo);

var nullableProducer = "§C{System.Environment.GetEnvironmentVariable} §A \"CALOR_N4_UNSET\" §/C";
var cases = new[]
{
    new ControlCase("direct-nullable-input-expression", Source("str", nullableProducer, expression: true)),
    new ControlCase("direct-nullable-input-statement", Source("str", nullableProducer, expression: false)),
    new ControlCase("nonnull-to-nullable-target-expression", Source("?str", "\"safe\"", expression: true)),
    new ControlCase("nonnull-to-nullable-target-statement", Source("?str", "\"safe\"", expression: false)),
    new ControlCase("already-applicable-object-alternative", Source("str", nullableProducer, expression: true, """
          §F{object:Take:pub} (object:value) -> i32
            §E{}
            §R 2
        """)),
    new ControlCase("old-numeric-ambiguity-preserved", Source("i64", "1", expression: true, """
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

var compilerAssembly = typeof(Binder).Assembly;
var results = cases.Select(c => Measure(repo, scratchRoot, c)).ToArray();
var payload = new
{
    ProbeVersion = "strict-v2-program-compile-and-cli",
    Repo = repo,
    GitHead = RunChecked("git", "rev-parse HEAD", repo).Trim(),
    GitBinderBlob = RunChecked("git", "rev-parse HEAD:src/Calor.Compiler/Binding/Binder.cs", repo).Trim(),
    GitScopeBlob = RunChecked("git", "rev-parse HEAD:src/Calor.Compiler/Binding/Scope.cs", repo).Trim(),
    DotnetVersion = RunChecked("dotnet", "--version", repo).Trim(),
    LoadedCompilerAssembly = new
    {
        compilerAssembly.GetName().Name,
        compilerAssembly.GetName().Version,
        Location = compilerAssembly.Location,
        Sha256 = ShaFile(compilerAssembly.Location)
    },
    Cases = results
};
File.WriteAllText(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n");
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
    if (string.IsNullOrEmpty(converted.CalorSource))
        throw new InvalidOperationException("ambiguous C# control converted to empty Calor source");
    // The baseline intentionally reports a generated-validation ambiguity for this fixture;
    // the nonempty Calor source is the sample under test.
    return converted.CalorSource.Replace("\r\n", "\n");
}

static string Source(string targetType, string argument, bool expression, string extraOverload = "") => expression
    ? $$"""
        §M{m1:NativeString}
          §F{take:Take:pub} ({{targetType}}:value) -> i32
            §E{}
            §R 1
        {{extraOverload}}
          §F{caller:Caller:pub} () -> i32
            §E{env}
            §R §C{Take} §A {{argument}} §/C
        """
    : $$"""
        §M{m1:NativeString}
          §F{take:Take:pub} ({{targetType}}:value) -> i32
            §E{}
            §R 1
        {{extraOverload}}
          §F{caller:Caller:pub} () -> i32
            §E{env}
            §C{Take} §A {{argument}} §/C
            §R 0
        """;

static object Measure(string repo, string scratchRoot, ControlCase c)
{
    var source = c.Source.Replace("\r\n", "\n");
    var parseDiagnostics = new DiagnosticBag();
    var module = new Parser(new Lexer(source, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics).Parse();
    if (parseDiagnostics.HasErrors)
        throw new InvalidOperationException($"Control {c.Name} failed to parse: "
            + string.Join("; ", parseDiagnostics.Errors.Select(d => $"{d.Code} {d.Span.Line}:{d.Span.Column} {d.Message}")));

    var bindDiagnostics = new DiagnosticBag();
    var bound = new Binder(bindDiagnostics).Bind(module);
    var propagated = new DiagnosticBag();
    BindingDiagnosticPolicy.PropagateCompilationErrors(bindDiagnostics, propagated);
    var all = bindDiagnostics.Errors.Select(d => ToRecord(d, source, propagated)).ToArray();
    var compile = Calor.Compiler.Program.Compile(source, c.Name + ".calr");
    var cli = RunCli(repo, scratchRoot, c.Name, source);

    return new
    {
        c.Name,
        SourceSha256 = Sha(source),
        RawBinderRouting = new
        {
            BindingDiagnostics = all,
            PropagatedDiagnostics = all.Where(d => d.Propagated).ToArray()
        },
        DefaultProgramCompile = new
        {
            compile.HasErrors,
            Diagnostics = compile.Diagnostics.Select(d => CompileRecord(d, source)).ToArray(),
            GeneratedCodeSha256 = Sha(compile.GeneratedCode ?? string.Empty),
            GeneratedCodeLength = compile.GeneratedCode?.Length ?? 0
        },
        DefaultCli = cli,
        Calls = Walk(bound).Select(n => n switch
        {
            BoundCallStatement s => CallRecord.FromStatement(s),
            BoundCallExpression e => CallRecord.FromExpression(e),
            _ => null
        }).Where(x => x is not null).ToArray()
    };
}

static object RunCli(string repo, string scratchRoot, string caseName, string source)
{
    var caseDir = Path.Combine(scratchRoot, Sanitize(caseName));
    Directory.CreateDirectory(caseDir);
    var modulePath = Path.Combine(caseDir, "module.calr");
    File.WriteAllText(modulePath, source);
    var cli = Path.Combine(repo, "src", "Calor.Compiler", "bin", "Debug", "net10.0", "calor.dll");
    if (!File.Exists(cli))
        throw new FileNotFoundException("Built calor.dll not found for CLI control.", cli);
    var start = new System.Diagnostics.ProcessStartInfo("dotnet")
    {
        WorkingDirectory = repo,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    start.ArgumentList.Add(cli);
    start.ArgumentList.Add("-i");
    start.ArgumentList.Add(modulePath);
    start.ArgumentList.Add("-o");
    start.ArgumentList.Add(Path.Combine(caseDir, "out.g.cs"));
    start.Environment["LC_ALL"] = "C";
    using var process = System.Diagnostics.Process.Start(start)
        ?? throw new InvalidOperationException("Failed to start default CLI control.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    var combined = stdout + stderr;
    return new
    {
        Invocation = $"dotnet {cli} -i {modulePath} -o {Path.Combine(caseDir, "out.g.cs")}",
        WorkingDirectory = repo,
        ModulePath = modulePath,
        CompilerDll = cli,
        CompilerDllSha256 = ShaFile(cli),
        process.ExitCode,
        Stdout = stdout,
        Stderr = stderr,
        Diagnostics = ExtractDiagnosticLines(combined).ToArray()
    };
}

static IEnumerable<string> ExtractDiagnosticLines(string text) =>
    text.Split('\n').Where(line => line.Contains("Calor", StringComparison.Ordinal));

static string Sanitize(string text) => new(text.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray());

static DiagRecord ToRecord(Diagnostic d, string source, DiagnosticBag propagated) => new(
    d.Code, d.Severity.ToString(), d.Span.Start, d.Span.End, d.Span.Line, d.Span.Column,
    d.Message, d.BindingContext?.Boundary.ToString(), d.BindingContext?.Shape.ToString(),
    ReplacesFlag(d.BindingContext), SafeDisposition(d), propagated.Errors.Any(p =>
        p.Code == d.Code && p.Span == d.Span && p.Message == d.Message && p.Severity == d.Severity),
    Snippet(source, d.Span));

static DiagRecord CompileRecord(Diagnostic d, string source) => new(
    d.Code, d.Severity.ToString(), d.Span.Start, d.Span.End, d.Span.Line, d.Span.Column,
    d.Message, d.BindingContext?.Boundary.ToString(), d.BindingContext?.Shape.ToString(),
    ReplacesFlag(d.BindingContext), d.BindingContext is null ? "NonBinderOrUnrouted" : SafeDisposition(d),
    d.IsError, Snippet(source, d.Span));

static bool ReplacesFlag(object? context)
{
    if (context is null) return false;
    var property = context.GetType().GetProperty("ReplacesNativeOverloadError");
    return property?.GetValue(context) as bool? ?? false;
}

static string SafeDisposition(Diagnostic d) =>
    BindingDiagnosticPolicy.GetRule(d.Code, d.BindingContext).Disposition.ToString();

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

static string ShaFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}

static string RunChecked(string fileName, string arguments, string workingDirectory)
{
    var start = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var process = System.Diagnostics.Process.Start(start)
        ?? throw new InvalidOperationException($"Failed to start {fileName} {arguments}.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Command failed ({process.ExitCode}): {fileName} {arguments}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    return stdout;
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
