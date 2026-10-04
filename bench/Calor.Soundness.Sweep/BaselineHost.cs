using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;

namespace Calor.Soundness.Sweep;

// One released compiler loaded in-process (registration channels.invocation): the baseline's own calor.dll, Microsoft.Z3.dll, native libz3, and
// Calor.Runtime.dll in a collectible load context. Program.Compile and CompilationOptions are bound by name; nothing from the current tree is used.
internal sealed class BaselineHost : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly Type _options;
    private readonly MethodInfo _compile;

    public string Id { get; }
    public string Directory { get; }
    public string? NativeZ3Path { get; private set; }
    public Assembly Compiler { get; }

    public BaselineHost(string id, string directory) : base("baseline-" + id, isCollectible: true)
    {
        Id = id;
        Directory = Path.GetFullPath(directory);
        _resolver = new AssemblyDependencyResolver(Path.Combine(Directory, "calor.dll"));
        Compiler = LoadFromAssemblyPath(Path.Combine(Directory, "calor.dll"));
        _options = Compiler.GetType("Calor.Compiler.CompilationOptions", throwOnError: true)!;
        _compile = Compiler.GetType("Calor.Compiler.Program", throwOnError: true)!.GetMethod("Compile", [typeof(string), typeof(string), _options])
            ?? throw new InvalidOperationException($"{id}: Program.Compile(string, string, CompilationOptions) not found");
    }

    protected override Assembly? Load(AssemblyName name)
    {
        var path = _resolver.ResolveAssemblyToPath(name) ?? Path.Combine(Directory, name.Name + ".dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var file = unmanagedDllName.EndsWith(".dylib", StringComparison.Ordinal) ? unmanagedDllName : unmanagedDllName + ".dylib";
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName)
            ?? new[] { Path.Combine(Directory, "runtimes", "osx-arm64", "native", file), Path.Combine(Directory, file) }.FirstOrDefault(File.Exists);
        if (path == null) return IntPtr.Zero;
        if (unmanagedDllName.Contains("z3", StringComparison.OrdinalIgnoreCase)) NativeZ3Path = path;
        return LoadUnmanagedDllFromPath(path);
    }

    // The loaded ContractTranslator.SemanticsVersion, or null when the baseline has none (P845).
    public string? TranslatorSemanticsVersion => Compiler.GetType("Calor.Compiler.Verification.Z3.ContractTranslator")
        ?.GetField("SemanticsVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;

    public bool? Z3Available => Compiler.GetType("Calor.Compiler.Verification.Z3.Z3ContextFactory")
        ?.GetProperty("IsAvailable", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as bool?;

    // Compiles one source text with the registered channel options and returns every observation.
    public JsonObject Compile(string source, bool verifyRefinements, bool elide, string? cacheDirectory)
    {
        var options = Activator.CreateInstance(_options)!;
        foreach (var (name, value) in new (string, object)[] { ("VerifyContracts", true), ("VerifyRefinements", verifyRefinements), ("EnableTypeChecking", true),
            ("ContractMode", Enum.Parse(Compiler.GetType("Calor.Compiler.ContractMode", true)!, "Debug")), ("VerificationTimeoutMs", 5000u), ("ElideProvenGuards", elide), ("StatusWriter", TextWriter.Null) })
            Set(options, name, value);
        var cache = Activator.CreateInstance(Compiler.GetType("Calor.Compiler.Verification.Z3.Cache.VerificationCacheOptions", true)!)!;
        Set(cache, "Enabled", cacheDirectory != null);
        if (cacheDirectory != null) Set(cache, "ProjectDirectory", cacheDirectory);
        Set(options, "VerificationCacheOptions", cache);
        var result = _compile.Invoke(null, [source, "case.calr", options])!;
        var diagnostics = Each(Get(result, "Diagnostics"), d => new JsonObject
        {
            ["code"] = Str(d, "Code"), ["severity"] = Get(d, "Severity")!.ToString(), ["line"] = (int)Get(Get(d, "Span")!, "Line")!, ["message"] = Str(d, "Message"),
        });
        return new JsonObject
        {
            ["hasErrors"] = (bool)Get(result, "HasErrors")!,
            ["diagnostics"] = diagnostics,
            ["contracts"] = Get(options, "VerificationResults") is { } module ? Each(Get(module, "Functions"), f => new JsonObject
            {
                ["functionId"] = Str(f, "FunctionId"), ["functionName"] = Str(f, "FunctionName"),
                ["preconditions"] = Results(Get(f, "PreconditionResults")), ["postconditions"] = Results(Get(f, "PostconditionResults")),
            }) : null,
            ["obligations"] = Get(options, "ObligationResults") is { } tracker ? Each(Get(tracker, "Obligations"), o => new JsonObject
            {
                ["id"] = Str(o, "Id"), ["kind"] = Get(o, "Kind")!.ToString(), ["functionId"] = Str(o, "FunctionId"), ["status"] = Get(o, "Status")!.ToString(),
                ["description"] = (string?)Get(o, "Description"), ["counterexampleDescription"] = (string?)Get(o, "CounterexampleDescription"),
                ["parameterName"] = TryGet(o, "ParameterName") as string, ["sourceProofId"] = TryGet(o, "SourceProofId") as string,
                ["outcome"] = Outcome(TryGet(o, "Outcome")),
            }) : null,
            ["emitted"] = (string?)Get(result, "GeneratedCode") ?? "",
        };
    }

    private static JsonArray Results(object? list) => Each(list, r => new JsonObject
    {
        ["legacyStatus"] = Get(r, "Status")!.ToString(), ["translatorSemanticsVersion"] = TryGet(r, "TranslatorSemanticsVersion") as string,
        ["outcome"] = Outcome(Get(r, "EffectiveOutcome")),
    });

    private static JsonObject? Outcome(object? outcome) => outcome == null ? null : new JsonObject
    {
        ["status"] = Get(outcome, "Status")!.ToString(),
        ["isVacuous"] = (bool)Get(outcome, "IsVacuous")!,
        ["assumptions"] = new JsonArray(((IEnumerable<string>)Get(outcome, "Assumptions")!).Select(a => (JsonNode)a).ToArray()),
        ["reason"] = (string?)Get(outcome, "Reason"),
        ["counterexample"] = Get(outcome, "Counterexample") is { } cex
            ? Each(Get(cex, "Bindings"), b => new JsonObject { ["name"] = Str(b, "Name"), ["value"] = Str(b, "Value") }) : null,
    };

    private static JsonArray Each(object? list, Func<object, JsonNode> map) => new(((IEnumerable)list!).Cast<object>().Select(map).ToArray());

    private static string Str(object target, string name) => (string)Get(target, name)!;

    private static void Set(object target, string name, object? value) =>
        (target.GetType().GetProperty(name) ?? throw new MissingMemberException(target.GetType().FullName, name)).SetValue(target, value);

    private static object? Get(object target, string name) =>
        (target.GetType().GetProperty(name) ?? throw new MissingMemberException(target.GetType().FullName, name)).GetValue(target);

    private static object? TryGet(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target);
}
