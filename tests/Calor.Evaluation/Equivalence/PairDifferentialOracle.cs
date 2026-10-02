using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CalorCompilationOptions = Calor.Compiler.CompilationOptions;

namespace Calor.Evaluation.Equivalence;

/// <summary>
/// The #1276 (0.24 B1) registered differential oracle. Its rules are pinned in
/// <c>docs/plans/evidence/b1-1276/registration/registration.json</c>; changing one is a #1407
/// amendment, and the registration tests fail when this file's hash drifts. Every step treats both
/// arms identically except the Calor-to-C# compile. EQUIVALENT means no disagreement on the
/// registered finite input set, not a proof.
/// </summary>
public static class PairDifferentialOracle
{
    public const string OracleId = "b1-1276-pair-oracle";
    public const string OracleVersion = "1";
    public const string InputGeneratorVersion = "b1-1276-signature-inputs-v1";
    public const int InvocationTimeoutMs = 2000;
    public const int RandomTuplesPerMember = 64;
    public const int CartesianCap = 256;
    public const int MaxWitnesses = 5;

    /// <summary>The implicit global usings of Microsoft.NET.Sdk, given to both arms.</summary>
    public const string SdkImplicitUsings =
        "global using global::System;\nglobal using global::System.Collections.Generic;\n" +
        "global using global::System.IO;\nglobal using global::System.Linq;\n" +
        "global using global::System.Net.Http;\nglobal using global::System.Threading;\n" +
        "global using global::System.Threading.Tasks;\n";

    private static readonly Lazy<List<MetadataReference>> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList());

    /// <summary>Runs the registered check on one pair. Never throws for pair-level problems.</summary>
    public static OracleVerdict Evaluate(OraclePairInput pair)
    {
        // 1. Identity: the bytes must be the registered bytes, or the pair is not the registered pair.
        var calorSha = Sha256Hex(pair.CalorBytes);
        var csharpSha = Sha256Hex(pair.CSharpBytes);
        if (calorSha != pair.ExpectedCalorSha256 || csharpSha != pair.ExpectedCSharpSha256)
            return Verdict(pair, "UNCLASSIFIED", "IDENTITY_MISMATCH",
                $"calor {calorSha} csharp {csharpSha} differ from the registered hashes");

        // 2. Build. The only arm-specific step: Calor -> C# with the compiler's default options.
        var calorText = Decode(pair.CalorBytes);
        var csharpText = Decode(pair.CSharpBytes);
        string generated;
        try
        {
            var result = Calor.Compiler.Program.Compile(calorText, pair.CalorPath,
                new CalorCompilationOptions { StatusWriter = TextWriter.Null });
            if (result.HasErrors)
                return Verdict(pair, "NOT-EQUIVALENT", "BUILD_FAILED_CALOR",
                    string.Join(" | ", result.Diagnostics.Errors.Take(3).Select(d => d.Message)));
            generated = result.GeneratedCode;
        }
        catch (Exception ex)
        {
            return Verdict(pair, "NOT-EQUIVALENT", "BUILD_FAILED_CALOR", ex.GetType().Name + ": " + ex.Message);
        }

        var calorAsm = CompileCSharp(generated, "CalorArm", out var calorErrors);
        var csharpAsm = CompileCSharp(csharpText, "CSharpArm", out var csharpErrors);
        if (calorAsm is null || csharpAsm is null)
        {
            var reason = calorAsm is null && csharpAsm is null ? "BUILD_FAILED_BOTH"
                : calorAsm is null ? "BUILD_FAILED_CALOR" : "BUILD_FAILED_CSHARP";
            return Verdict(pair, "NOT-EQUIVALENT", reason, string.Join(" | ", calorErrors.Concat(csharpErrors).Take(3)));
        }

        var calorContext = new AssemblyLoadContext($"calor-{pair.PairId}", isCollectible: true);
        var csharpContext = new AssemblyLoadContext($"csharp-{pair.PairId}", isCollectible: true);
        try
        {
            var calorSurface = Surface(calorContext.LoadFromStream(new MemoryStream(calorAsm)));
            var csharpSurface = Surface(csharpContext.LoadFromStream(new MemoryStream(csharpAsm)));
            return Compare(pair, calorSurface, csharpSurface);
        }
        finally
        {
            calorContext.Unload();
            csharpContext.Unload();
        }
    }

    private static OracleVerdict Compare(OraclePairInput pair, ArmSurface calor, ArmSurface csharp)
    {
        // 3. Surface: the same observable members, matched by case-insensitive name and exact types.
        var keys = calor.Members.Keys.Union(csharp.Members.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (calor.Ambiguous.Count > 0 || csharp.Ambiguous.Count > 0)
            return Verdict(pair, "NOT-EQUIVALENT", "SURFACE_AMBIGUOUS",
                string.Join(", ", calor.Ambiguous.Concat(csharp.Ambiguous)), keys);
        var onlyOne = keys.Where(k => !calor.Members.ContainsKey(k) || !csharp.Members.ContainsKey(k)).ToList();
        if (onlyOne.Count > 0)
            return Verdict(pair, "NOT-EQUIVALENT", "SURFACE_MISMATCH",
                string.Join(", ", onlyOne.Select(k => (calor.Members.ContainsKey(k) ? "calor-only " : "csharp-only ") + k)), keys);
        if (keys.Count == 0)
            return Verdict(pair, "UNCLASSIFIED", "SURFACE_EMPTY", "no public observable member in either arm", keys);
        var instance = keys.Where(k => k.StartsWith("instance ", StringComparison.Ordinal)).ToList();
        if (instance.Count > 0)
            return Verdict(pair, "UNCLASSIFIED", "UNSUPPORTED_SURFACE",
                "v1 drives static members only: " + string.Join(", ", instance.Take(3)), keys);
        var unsupported = keys.Where(k => !InputGenerator.Supports(calor.Members[k])).ToList();
        if (unsupported.Count > 0)
            return Verdict(pair, "UNCLASSIFIED", "UNSUPPORTED_TYPE", string.Join(", ", unsupported.Take(3)), keys);

        // 4-5. Identical inputs, each invoked twice per arm, compared observation by observation.
        var transcript = new StringBuilder();
        var witnesses = new List<string>();
        var inputCount = 0;
        foreach (var key in keys)
        {
            foreach (var args in InputGenerator.Generate(pair.PairId, key, calor.Members[key]))
            {
                inputCount++;
                var observed = new Observation[4];
                observed[0] = Invoke(calor.Members[key], args);
                observed[1] = Invoke(csharp.Members[key], args);
                observed[2] = Invoke(calor.Members[key], args);
                observed[3] = Invoke(csharp.Members[key], args);
                var label = $"{key} {InputGenerator.Render(args)}";
                if (observed.Any(o => o.Kind == "timeout"))
                    return Verdict(pair, "UNCLASSIFIED", "TIMEOUT", label, keys, inputCount);
                if (!observed[0].SameAs(observed[2]) || !observed[1].SameAs(observed[3]))
                    return Verdict(pair, "UNCLASSIFIED", "NONDETERMINISTIC", label, keys, inputCount);
                transcript.Append(label).Append(" => ").Append(observed[0]).Append(" || ").Append(observed[1]).Append('\n');
                if (!observed[0].SameAs(observed[1]) && witnesses.Count < MaxWitnesses)
                    witnesses.Add($"{label}: calor {observed[0]} / csharp {observed[1]}");
            }
        }

        var transcriptSha = Sha256Hex(Encoding.UTF8.GetBytes(transcript.ToString()));
        return witnesses.Count > 0
            ? new OracleVerdict(pair.PairId, "NOT-EQUIVALENT", "OBSERVATION_MISMATCH", keys, inputCount, transcriptSha, witnesses, "")
            : new OracleVerdict(pair.PairId, "EQUIVALENT", "AGREE", keys, inputCount, transcriptSha, [], "");
    }

    /// <summary>Same Roslyn settings, references, and global usings for both arms.</summary>
    internal static byte[]? CompileCSharp(string source, string assemblyName, out List<string> errors)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText(SdkImplicitUsings, parse), CSharpSyntaxTree.ParseText(source, parse)],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        errors = emit.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{assemblyName}: {d.Id} {d.GetMessage(CultureInfo.InvariantCulture)}").ToList();
        return emit.Success ? stream.ToArray() : null;
    }

    /// <summary>
    /// Public, non-special methods of exported types. Static members are keyed without their
    /// declaring type (the arms may name their containers differently); instance members keep the
    /// type's simple name. Constructors are not keyed: v1 never constructs an object.
    /// </summary>
    internal static ArmSurface Surface(Assembly assembly)
    {
        var members = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        var ambiguous = new List<string>();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance;
            foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName && !IsObjectOverride(m)))
            {
                var key = !method.IsStatic ? "instance " + type.Name + "." + Signature(method)
                    : IsEntryPoint(method) ? EntryKeyPrefix + TypeKey(method.ReturnType) : Signature(method);
                if (!members.TryAdd(key, method))
                    ambiguous.Add(key);
            }
        }
        return new ArmSurface(members, ambiguous);
    }

    private static bool IsObjectOverride(MethodInfo m) =>
        m.Name is "ToString" or "Equals" or "GetHashCode" && m.GetBaseDefinition().DeclaringType == typeof(object);

    /// <summary>C# entry points <c>Main()</c> and <c>Main(string[])</c> are one member, run once with no arguments.</summary>
    internal const string EntryKeyPrefix = "main(entry)->";

    private static bool IsEntryPoint(MethodInfo m) =>
        m.Name == "Main" && m.GetParameters() is var ps && (ps.Length == 0 || (ps.Length == 1 && ps[0].ParameterType == typeof(string[])));

    private static string Signature(MethodInfo m) =>
        $"{m.Name.ToLowerInvariant()}{(m.IsGenericMethodDefinition ? "<>" : "")}({string.Join(",", m.GetParameters().Select(p => (p.ParameterType.IsByRef ? "ref " : "") + TypeKey(p.ParameterType)))})->{TypeKey(m.ReturnType)}";

    private static string TypeKey(Type t) =>
        t.Assembly == typeof(object).Assembly || t.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
            ? t.ToString()
            : "arm-defined:" + t.Name;

    private static Observation Invoke(MethodInfo method, object?[] args)
    {
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        var originalOut = Console.Out;
        var originalIn = Console.In;
        Observation? result = null;
        var thread = new Thread(() =>
        {
            try
            {
                var actual = args.Length == 0 && method.GetParameters().Length == 1 ? new object?[] { Array.Empty<string>() } : InputGenerator.Clone(args);
                var value = method.Invoke(null, actual);
                result = new Observation("return", method.ReturnType == typeof(void) ? "void" : InputGenerator.Render(value), stdout.ToString(), "");
            }
            catch (TargetInvocationException tie)
            {
                result = new Observation("throw", "", stdout.ToString(), (tie.InnerException ?? tie).GetType().FullName ?? "?");
            }
            catch (Exception ex)
            {
                result = new Observation("throw", "", stdout.ToString(), ex.GetType().FullName ?? "?");
            }
        }, 16 * 1024 * 1024) { IsBackground = true, CurrentCulture = CultureInfo.InvariantCulture, CurrentUICulture = CultureInfo.InvariantCulture };
        Console.SetOut(stdout);
        Console.SetIn(new StringReader(""));
        try
        {
            thread.Start();
            return thread.Join(InvocationTimeoutMs) && result is not null ? result : new Observation("timeout", "", "", "");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetIn(originalIn);
        }
    }

    private static OracleVerdict Verdict(OraclePairInput pair, string disposition, string reason, string detail,
        IReadOnlyList<string>? surface = null, int inputs = 0) =>
        new(pair.PairId, disposition, reason, surface ?? [], inputs, null, [], detail);

    internal static string Decode(byte[] bytes) => new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

public sealed record OraclePairInput(
    string PairId, string CalorPath, byte[] CalorBytes, string ExpectedCalorSha256,
    string CSharpPath, byte[] CSharpBytes, string ExpectedCSharpSha256);

public sealed record OracleVerdict(
    string PairId, string Disposition, string Reason, IReadOnlyList<string> Surface, int InputCount,
    string? ObservationsSha256, IReadOnlyList<string> Witnesses, string Detail);

internal sealed record ArmSurface(Dictionary<string, MethodInfo> Members, List<string> Ambiguous);

/// <summary>
/// One observation. Failure is compared by class (return vs throw); the exception type is recorded
/// but, under oracle v1, not compared (registration.json oracle.failureBehavior).
/// </summary>
internal sealed record Observation(string Kind, string Value, string Stdout, string ExceptionType)
{
    public bool SameAs(Observation other) => Kind == other.Kind && Value == other.Value && Stdout == other.Stdout;

    public override string ToString() =>
        $"{Kind}{(Kind == "throw" ? "(" + ExceptionType + ")" : "")} {Value} stdout={InputGenerator.Render(Stdout)}";
}
