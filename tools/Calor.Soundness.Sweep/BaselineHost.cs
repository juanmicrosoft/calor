using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;

namespace Calor.Soundness.Sweep;

/// <summary>
/// One released compiler loaded in-process (registration channels.invocation): the baseline's own
/// calor.dll, Microsoft.Z3.dll, native libz3, and Calor.Runtime.dll in a collectible load context.
/// Program.Compile and CompilationOptions are bound by name; nothing from the current tree is used.
/// </summary>
internal sealed class BaselineHost : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly Type _program;
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
        var calor = Path.Combine(Directory, "calor.dll");
        _resolver = new AssemblyDependencyResolver(calor);
        Compiler = LoadFromAssemblyPath(calor);
        _program = Compiler.GetType("Calor.Compiler.Program", throwOnError: true)!;
        _options = Compiler.GetType("Calor.Compiler.CompilationOptions", throwOnError: true)!;
        _compile = _program.GetMethod("Compile", [typeof(string), typeof(string), _options])
            ?? throw new InvalidOperationException($"{id}: Program.Compile(string, string, CompilationOptions) not found");
    }

    protected override Assembly? Load(AssemblyName name)
    {
        var path = _resolver.ResolveAssemblyToPath(name);
        if (path == null)
        {
            var local = Path.Combine(Directory, name.Name + ".dll");
            if (File.Exists(local))
                path = local;
        }
        return path == null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path == null)
        {
            var file = unmanagedDllName.EndsWith(".dylib", StringComparison.Ordinal) ? unmanagedDllName : unmanagedDllName + ".dylib";
            foreach (var candidate in new[] { Path.Combine(Directory, "runtimes", "osx-arm64", "native", file), Path.Combine(Directory, file) })
                if (File.Exists(candidate)) { path = candidate; break; }
        }
        if (path == null)
            return IntPtr.Zero;
        if (unmanagedDllName.Contains("z3", StringComparison.OrdinalIgnoreCase))
            NativeZ3Path = path;
        return LoadUnmanagedDllFromPath(path);
    }

    /// <summary>The loaded ContractTranslator.SemanticsVersion, or null when the baseline has none (P845).</summary>
    public string? TranslatorSemanticsVersion =>
        Compiler.GetType("Calor.Compiler.Verification.Z3.ContractTranslator")
            ?.GetField("SemanticsVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;

    public bool? Z3Available =>
        Compiler.GetType("Calor.Compiler.Verification.Z3.Z3ContextFactory")
            ?.GetProperty("IsAvailable", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as bool?;

    /// <summary>Compiles one source text with the registered channel options and returns every observation.</summary>
    public JsonObject Compile(string source, bool verifyRefinements, bool elide, string? cacheDirectory)
    {
        var options = Activator.CreateInstance(_options)!;
        Set(options, "VerifyContracts", true);
        Set(options, "VerifyRefinements", verifyRefinements);
        Set(options, "EnableTypeChecking", true);
        Set(options, "ContractMode", Enum.Parse(Compiler.GetType("Calor.Compiler.ContractMode", true)!, "Debug"));
        Set(options, "VerificationTimeoutMs", 5000u);
        Set(options, "ElideProvenGuards", elide);
        Set(options, "StatusWriter", TextWriter.Null);
        var cacheType = Compiler.GetType("Calor.Compiler.Verification.Z3.Cache.VerificationCacheOptions", true)!;
        var cache = Activator.CreateInstance(cacheType)!;
        Set(cache, "Enabled", cacheDirectory != null);
        if (cacheDirectory != null)
            Set(cache, "ProjectDirectory", cacheDirectory);
        Set(options, "VerificationCacheOptions", cache);

        var result = _compile.Invoke(null, [source, "case.calr", options])!;
        var diagnostics = new JsonArray();
        foreach (var d in (IEnumerable)Get(result, "Diagnostics")!)
        {
            var span = Get(d, "Span")!;
            diagnostics.Add(new JsonObject
            {
                ["code"] = (string)Get(d, "Code")!,
                ["severity"] = Get(d, "Severity")!.ToString(),
                ["line"] = (int)Get(span, "Line")!,
                ["message"] = (string)Get(d, "Message")!,
            });
        }
        return new JsonObject
        {
            ["hasErrors"] = (bool)Get(result, "HasErrors")!,
            ["diagnostics"] = diagnostics,
            ["contracts"] = Contracts(Get(options, "VerificationResults")),
            ["obligations"] = Obligations(Get(options, "ObligationResults")),
            ["emitted"] = (string?)Get(result, "GeneratedCode") ?? "",
        };
    }

    private static JsonArray? Contracts(object? module)
    {
        if (module == null)
            return null;
        var functions = new JsonArray();
        foreach (var f in (IEnumerable)Get(module, "Functions")!)
        {
            functions.Add(new JsonObject
            {
                ["functionId"] = (string)Get(f, "FunctionId")!,
                ["functionName"] = (string)Get(f, "FunctionName")!,
                ["preconditions"] = Results(Get(f, "PreconditionResults")!),
                ["postconditions"] = Results(Get(f, "PostconditionResults")!),
            });
        }
        return functions;
    }

    private static JsonArray Results(object list)
    {
        var array = new JsonArray();
        foreach (var r in (IEnumerable)list)
        {
            var outcome = Get(r, "EffectiveOutcome")!;
            array.Add(new JsonObject
            {
                ["legacyStatus"] = Get(r, "Status")!.ToString(),
                ["translatorSemanticsVersion"] = TryGet(r, "TranslatorSemanticsVersion") as string,
                ["outcome"] = Outcome(outcome),
            });
        }
        return array;
    }

    private static JsonObject? Outcome(object? outcome)
    {
        if (outcome == null)
            return null;
        var cex = Get(outcome, "Counterexample");
        JsonArray? bindings = null;
        if (cex != null)
        {
            bindings = [];
            foreach (var b in (IEnumerable)Get(cex, "Bindings")!)
                bindings.Add(new JsonObject { ["name"] = (string)Get(b, "Name")!, ["value"] = (string)Get(b, "Value")! });
        }
        return new JsonObject
        {
            ["status"] = Get(outcome, "Status")!.ToString(),
            ["isVacuous"] = (bool)Get(outcome, "IsVacuous")!,
            ["assumptions"] = new JsonArray(((IEnumerable<string>)Get(outcome, "Assumptions")!).Select(a => (JsonNode)a).ToArray()),
            ["reason"] = (string?)Get(outcome, "Reason"),
            ["counterexample"] = bindings,
        };
    }

    private static JsonArray? Obligations(object? tracker)
    {
        if (tracker == null)
            return null;
        var array = new JsonArray();
        foreach (var o in (IEnumerable)Get(tracker, "Obligations")!)
        {
            array.Add(new JsonObject
            {
                ["id"] = (string)Get(o, "Id")!,
                ["kind"] = Get(o, "Kind")!.ToString(),
                ["functionId"] = (string)Get(o, "FunctionId")!,
                ["status"] = Get(o, "Status")!.ToString(),
                ["description"] = (string?)Get(o, "Description"),
                ["counterexampleDescription"] = (string?)Get(o, "CounterexampleDescription"),
                ["parameterName"] = TryGet(o, "ParameterName") as string,
                ["sourceProofId"] = TryGet(o, "SourceProofId") as string,
                ["outcome"] = Outcome(TryGet(o, "Outcome")),
            });
        }
        return array;
    }

    private static void Set(object target, string name, object? value) =>
        (target.GetType().GetProperty(name) ?? throw new MissingMemberException(target.GetType().FullName, name)).SetValue(target, value);

    private static object? Get(object target, string name) =>
        (target.GetType().GetProperty(name) ?? throw new MissingMemberException(target.GetType().FullName, name)).GetValue(target);

    private static object? TryGet(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target);
}
