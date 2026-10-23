using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1427 (0.25 D1): executes the by-reference cases in
/// <c>tests/TestData/InteropScope/D1/refout-cases.json</c>. Calor cases compile at the current source and must
/// reproduce their recorded observation (so #943 updates `observed`/`status` visibly when it changes
/// behavior). C# cases run `Probe.Run()`: the original must print `expectedOutput`, and every mutant (a
/// copy, dropped modifier, reorder or hoist) must print something else, so the witness can detect it.
/// </summary>
public class RefOutContractCaseTests
{
    public const string CasesPath = "tests/TestData/InteropScope/D1/refout-cases.json";

    internal static JsonNode Contract() => JsonNode.Parse(File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), CasesPath)))!;

    /// <summary>A csharp case's inline source, or an r0-fixture case's committed fixture file.</summary>
    internal static string SourceText(JsonNode c)
    {
        if (c["kind"]!.GetValue<string>() == "csharp")
            return string.Join("\n", c["source"]!.AsArray().Select(l => l!.GetValue<string>()));
        return File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), c["fixture"]!.GetValue<string>()));
    }

    public static IEnumerable<object[]> CalorCases => Ids("calor");
    public static IEnumerable<object[]> CSharpCases => Ids("csharp").Concat(Ids("r0-fixture"));

    [Theory]
    [MemberData(nameof(CalorCases))]
    public void CalorCaseReproducesItsObservation(string id)
    {
        var contract = Contract();
        var c = Find(contract, id);
        var source = string.Join("\n", c["source"]!.AsArray().Select(l => l!.GetValue<string>()).Prepend(Prelude(contract, c))) + "\n";
        var verify = c["verify"]?.GetValue<bool>() == true;
        if (verify) Assert.True(Z3ContextFactory.IsAvailable, "proof cases need Z3");
        var cacheDir = c["warmup"] is null ? null : Directory.CreateTempSubdirectory("refout-cache-").FullName;
        CompilationOptions Options() => new()
        {
            VerifyContracts = verify,
            VerifyRefinements = verify,
            ContractMode = verify ? ContractMode.Debug : new CompilationOptions().ContractMode,
            VerificationCacheOptions = cacheDir is null
                ? new VerificationCacheOptions { Enabled = false }
                : new VerificationCacheOptions { Enabled = true, CacheDirectory = cacheDir },
        };
        if (c["warmup"] is JsonArray warm)
        {
            // RO-ANA-4: the same body passing `§A y` by value first, into the same cache directory.
            var warmOptions = Options();
            var warmSource = string.Join("\n", warm.Select(l => l!.GetValue<string>()).Prepend(Prelude(contract, c))) + "\n";
            Assert.False(Program.Compile(warmSource, id + ".calr", warmOptions).HasErrors); // same file identity as the case
            Assert.Equal(ObligationStatus.Discharged,
                Assert.Single(warmOptions.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation).Status);
        }
        var options = Options();
        var result = Program.Compile(source, id + ".calr", options);
        var errors = result.Diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var observed = c["observed"]!;
        Assert.Equal(observed["errors"]!.AsArray().Select(e => e!.GetValue<string>()).OrderBy(x => x, StringComparer.Ordinal), errors);
        if (c["expected"]?["proof"] is not null)
        {
            var proof = Assert.IsAssignableFrom<JsonNode>(observed["proof"]);
            var status = Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation).Status;
            Assert.Equal(proof.GetValue<string>(), status.ToString());
            // RO-ANA-3: an Unsupported obligation is visible (Calor1124) and keeps its runtime guard.
            const string guard = "Proof obligation [pr1] violated";
            if (status == ObligationStatus.Unsupported)
            {
                Assert.Contains(result.Diagnostics, d => d.Code == "Calor1124");
                Assert.Contains(guard, result.GeneratedCode);
            }
        }
        if (errors.Count == 0 && c["status"]!.GetValue<string>() == "holds" && c["expected"]?["emits"] is JsonArray emits)
        {
            foreach (var e in emits) Assert.Contains(e!.GetValue<string>(), result.GeneratedCode);
        }
    }

    [Theory]
    [MemberData(nameof(CSharpCases))]
    public void CSharpWitnessDiscriminates(string id)
    {
        var c = Find(Contract(), id);
        var source = SourceText(c);
        var expected = c["expectedOutput"]!.GetValue<string>();
        Assert.Equal(expected, Execute(source));
        foreach (var m in c["mutants"]!.AsArray())
        {
            var mutated = source.Replace(m!["find"]!.GetValue<string>(), m["replace"]!.GetValue<string>(), StringComparison.Ordinal);
            var output = Execute(mutated);
            Assert.False(output == expected, $"{id}: mutant '{m["kind"]}' still prints {expected}");
        }
    }

    /// <summary>Compiles C# with nullable enabled, runs static Probe.Run(); an exception prints "!Type".</summary>
    private static string Execute(string source)
    {
        var compilation = CSharpCompilation.Create("RefOut_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var run = context.LoadFromStream(stream).GetTypes().Single(t => t.Name == "Probe")
                .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            try { return run.Invoke(null, null)?.ToString() ?? "<null>"; }
            catch (TargetInvocationException e) { return "!" + e.InnerException!.GetType().Name; }
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Prelude(JsonNode contract, JsonNode c)
        => string.Join("\n", contract["preludes"]![c["prelude"]!.GetValue<string>()]!.AsArray().Select(l => l!.GetValue<string>()));

    private static IEnumerable<object[]> Ids(string kind) => Contract()["cases"]!.AsArray()
        .Where(c => c!["kind"]!.GetValue<string>() == kind).Select(c => new object[] { c!["id"]!.GetValue<string>() });

    private static JsonNode Find(JsonNode contract, string id)
        => contract["cases"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;
}
