using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Calor.Compiler.Tests.SoundnessRegistration;

/// <summary>
/// #1419 (0.24 R1) — the independent behavioral oracle (O1). It compiles a case's reference C# against
/// BCL assemblies ONLY and executes it over the case's input domain under the registered culture and
/// overflow mode, never using the verifier's translator, simplifier, emitter, or solver.
/// </summary>
internal static class IndependentOracle
{
    internal const string RuntimeCulture = "en-US";
    internal static readonly TimeSpan EvaluationBudget = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Kinds: forall claims → <c>violated</c>, <c>holds-exhaustive</c>, <c>holds-sampled</c>,
    /// <c>vacuous-in-domain</c>; exists claims → <c>witness-found</c>, <c>no-witness-exhaustive</c>,
    /// <c>no-witness-sampled</c>; either → <c>oracle-invalid</c> (does not compile, or over budget).
    /// </summary>
    internal sealed record Verdict(
        string Kind,
        int Inputs,
        int HypothesisHeld,
        int Reached,
        int HypothesisThrew,
        int BodyThrew,
        int Violations,
        string? ViolationKind,
        string? Witness,
        string? Error,
        IReadOnlyList<string>? ReachedSample = null);

    /// <summary>Reached inputs (domain order) that R1-O2 replays in addition to every witness.</summary>
    internal const int ReplaySampleSize = 16;

    internal static IReadOnlyList<MetadataReference> BclReferences { get; } =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return name is "System.Runtime" or "System.Private.CoreLib" or "System.Collections"
                    or "System.Linq" or "System.Runtime.Extensions" or "System.Runtime.Numerics" or "netstandard";
            })
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

    internal static Verdict Evaluate(string oracleSource)
    {
        var tree = CSharpSyntaxTree.ParseText(oracleSource, new CSharpParseOptions(LanguageVersion.CSharp14));
        var checkedMode = !oracleSource.Contains("public const bool Checked = false;", StringComparison.Ordinal);
        var compilation = CSharpCompilation.Create(
            "R1Oracle_" + Guid.NewGuid().ToString("N"),
            [tree],
            BclReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                checkOverflow: checkedMode,
                nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            var errors = string.Join("; ", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            return new Verdict("oracle-invalid", 0, 0, 0, 0, 0, 0, null, null, errors);
        }

        stream.Position = 0;
        var context = new AssemblyLoadContext("R1Oracle", isCollectible: true);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(RuntimeCulture);
            var type = context.LoadFromStream(stream).GetType("R1Oracle")!;
            return Run(type);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            context.Unload();
        }
    }

    private static Verdict Run(Type type)
    {
        var claim = (string)type.GetField("Claim")!.GetValue(null)!;
        var exhaustive = (bool)type.GetField("Exhaustive")!.GetValue(null)!;
        var hasBody = (bool)type.GetProperty("HasBody")!.GetValue(null)!;
        var hyp = type.GetMethod("HypO")!;
        var body = type.GetMethod("BodyO")!;
        var prop = type.GetMethod("PropO")!;
        var inputs = (IEnumerable<object?[]>)type.GetMethod("Inputs")!.Invoke(null, null)!;

        int total = 0, held = 0, reached = 0, hypThrew = 0, bodyThrew = 0, violations = 0, satisfied = 0;
        var reachedSample = new List<string>();
        string? violationKind = null, witness = null;
        var clock = Stopwatch.StartNew();
        foreach (var input in inputs)
        {
            if (clock.Elapsed > EvaluationBudget)
            {
                // A concrete witness found before the budget ran out stays conclusive; only an
                // inconclusive partial enumeration is oracle-invalid.
                var partial = claim == "exists" ? (satisfied > 0 ? "witness-found" : "oracle-invalid")
                    : violations > 0 ? "violated" : "oracle-invalid";
                return new Verdict(partial, total, held, reached, hypThrew, bodyThrew, violations,
                    violationKind, witness, "evaluation budget exceeded (partial enumeration)", reachedSample);
            }
            total++;
            if (!TryInvoke(hyp, [input], out var hypValue))
            {
                hypThrew++;
                continue;
            }
            if (!(bool)hypValue!)
                continue;
            held++;
            // Rendered before Body runs: a body may mutate (aliased) inputs, and replay needs entry values.
            var entry = Render(input);
            object? result = null;
            if (hasBody && !TryInvoke(body, [input], out result))
            {
                bodyThrew++;
                continue;
            }
            reached++;
            if (reachedSample.Count < ReplaySampleSize)
                reachedSample.Add(entry);
            var ok = TryInvoke(prop, [input, result], out var propValue);
            var holds = ok && (bool)propValue!;
            if (claim == "exists")
            {
                if (holds)
                {
                    satisfied++;
                    witness ??= entry;
                }
                continue;
            }
            if (!holds)
            {
                violations++;
                if (witness == null)
                {
                    witness = entry + (hasBody ? $" -> result={Render([result])}" : "");
                    violationKind = ok ? "prop-false" : "prop-throws";
                }
            }
        }

        var kind = claim == "exists"
            ? satisfied > 0 ? "witness-found" : exhaustive ? "no-witness-exhaustive" : "no-witness-sampled"
            : violations > 0 ? "violated"
            : reached == 0 ? "vacuous-in-domain"
            : exhaustive ? "holds-exhaustive" : "holds-sampled";
        return new Verdict(kind, total, held, reached, hypThrew, bodyThrew, violations, violationKind, witness, null, reachedSample);
    }

    private static bool TryInvoke(MethodInfo method, object?[] args, out object? value)
    {
        try
        {
            value = method.Invoke(null, args);
            return true;
        }
        catch (TargetInvocationException)
        {
            value = null;
            return false;
        }
    }

    private static string Render(object?[] values) => "(" + string.Join(", ", values.Select(v => v switch
    {
        null => "null",
        string s => "\"" + string.Concat(s.Select(c => c < 32 || c > 126 ? $"\\u{(int)c:x4}" : c.ToString())) + "\"",
        Array a => "[" + string.Join(", ", a.Cast<object?>().Select(e => Convert.ToString(e, CultureInfo.InvariantCulture))) + "]",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture),
    })) + ")";
}
