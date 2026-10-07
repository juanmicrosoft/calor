using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Tests.EvidenceContract;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1427 (0.25 D1): the committed ref/out contract registry passes its validator, and each negative
/// control removes or changes one registered fact and must fail with its own code (fail closed on gaps).
/// </summary>
public class RefOutContractTests
{
    /// <summary>SHA-256 over the 1.0.0 rules and case denominator. Changing either needs an amendment and a new pin.</summary>
    private const string FrozenSeal = "d15b423600acdebf23221bfce643196ffccc27e723cbfc3e2d549e0c1c28e942";

    [Fact]
    public void CommittedContractPasses()
    {
        var violations = Run(Contract());
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    [Fact]
    public void FrozenDenominatorMatchesThePinnedSeal()
    {
        var seal = RefOutContractValidator.Seal(Contract());
        Assert.True(seal == FrozenSeal, $"rules or cases changed (seal {seal}); record an amendment and re-pin");
    }

    [Fact]
    public void VacuousWitnessSubstitutionPassesTheValidatorButBreaksTheSeal()
    {
        // Round-1 review: a case whose source no longer passes anything by reference, with a mutant that
        // still "changes" the output, satisfies D003/D006; only the seal catches it.
        var contract = Contract();
        var c = Case(contract, "D1-CS-02");
        c["source"] = new JsonArray("public static class Probe { public static string Run() { return \"9\"; } }");
        c["mutants"] = new JsonArray(new JsonObject { ["kind"] = "copy", ["find"] = "return \"9\";", ["replace"] = "return \"0\";" });
        Assert.Empty(Run(contract));
        Assert.NotEqual(FrozenSeal, RefOutContractValidator.Seal(contract));
    }

    [Fact]
    public void ObservationUpdatesDoNotChangeTheSeal()
    {
        var contract = Contract();
        Case(contract, "D1-EFF-01")["observed"] = new JsonObject { ["errors"] = new JsonArray("Calor0410") };
        Case(contract, "D1-EFF-01")["status"] = "holds";
        contract["diagnostics"]![0]!["code"] = "Calor0299";
        Assert.Empty(Run(contract, symbol => symbol == "InvalidByReferenceOperand" ? "Calor0299" : null));
        Assert.Equal(FrozenSeal, RefOutContractValidator.Seal(contract));
    }

    [Fact]
    public void AllocatedSymbolResolvesToItsCode()
    {
        // As if #943 had added DiagnosticCode.InvalidByReferenceOperand = "Calor0299".
        var contract = Contract();
        contract["diagnostics"]![0]!["code"] = "Calor0299";
        Case(contract, "D1-LV-01")["observed"] = new JsonObject { ["errors"] = new JsonArray("Calor0299") };
        Case(contract, "D1-LV-01")["status"] = "holds";
        Assert.Empty(Run(contract, symbol => symbol == "InvalidByReferenceOperand" ? "Calor0299" : null));
    }

    [Fact]
    public void AllocationThatReusesAnExistingCodeFails()
    {
        // Round-2 review: mapping the symbol to Calor0200 (UndefinedReference) would turn D1-DECL-03's
        // accidental refusal into a pass. The code must be the compiler's own constant for the symbol.
        var contract = Contract();
        contract["diagnostics"]![0]!["code"] = "Calor0200";
        Case(contract, "D1-DECL-03")["status"] = "holds";
        Assert.Contains(Run(contract), v => v.Code == "D007");
    }

    public static IEnumerable<object[]> NegativeControls => Negatives.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(NegativeControls))]
    public void NegativeControlFailsWithItsCode(string name)
    {
        var (code, mutate) = Negatives[name];
        var contract = Contract();
        mutate(contract);
        var codes = Run(contract).Select(v => v.Code).Distinct().ToList();
        Assert.True(codes.SequenceEqual([code]), $"{name}: expected only {code}, got [{string.Join(", ", codes)}]");
    }

    private static readonly Dictionary<string, (string Code, Action<JsonNode> Mutate)> Negatives = new()
    {
        ["0.22 revision changed"] = ("D001", c => Consumed(c, "nullability-0.22")["revision"] = new string('0', 40)),
        ["R-OBL dropped"] = ("D001", c => c["consumes"]!.AsArray().Remove(Consumed(c, "r-obl"))),
        ["rule left without a case"] = ("D002", c =>
        {
            foreach (var x in Cases(c)) x["rules"] = new JsonArray(Rules(x).Where(r => r != "RO-ORD-2").Select(r => (JsonNode)r).ToArray());
            foreach (var x in Cases(c).Where(x => x["rules"]!.AsArray().Count == 0).ToList()) x["rules"] = new JsonArray("RO-ORD-1");
        }),
        ["area left without a rule"] = ("D002", c =>
        {
            var rules = c["rules"]!.AsArray();
            foreach (var r in rules.Where(r => r!["area"]!.GetValue<string>() == "representation").ToList()) rules.Remove(r);
            foreach (var x in Cases(c)) x["rules"] = new JsonArray(Rules(x).Where(r => r != "RO-REP-1").DefaultIfEmpty("RO-NULL-5").Select(r => (JsonNode)r).ToArray());
        }),
        ["case cites an unknown rule"] = ("D003", c => Case(c, "D1-LV-01")["rules"]!.AsArray().Add("RO-LV-99")),
        ["case without a source"] = ("D003", c => Case(c, "D1-CS-01")["source"] = new JsonArray()),
        ["required shape without a case"] = ("D004", c =>
        {
            foreach (var x in Cases(c).Where(x => x["shape"]!.GetValue<string>() == "struct-element").ToList()) x["shape"] = "struct-element-field";
        }),
        ["area without a negative case"] = ("D004", c =>
        {
            foreach (var x in Cases(c).Where(x => x["area"]!.GetValue<string>() == "lvalue")) x["polarity"] = "positive";
        }),
        ["R0 row unmapped"] = ("D005", c => c["cases"]!.AsArray().Remove(Case(c, "D1-R0-08"))),
        ["R0 expectation weakened"] = ("D005", c => Case(c, "D1-R0-08")["conversion"] = "native-or-preserved"),
        ["R0 analysis row unmapped"] = ("D005", c =>
        {
            foreach (var x in Cases(c)) x.AsObject().Remove("r0Rows");
        }),
        ["native witness without a mutant"] = ("D006", c => Case(c, "D1-CS-02")["mutants"] = new JsonArray()),
        ["mutant text absent"] = ("D006", c => Case(c, "D1-CS-02")["mutants"]![0]!["find"] = "Reset(ref pts[1]);"),
        ["refusal by generated-C# failure"] = ("D007", c =>
        {
            Case(c, "D1-LV-01")["expected"]!["diagnostic"] = "Calor1002";
            Case(c, "D1-LV-01")["status"] = "holds";
        }),
        ["refusal by unregistered symbol"] = ("D007", c => Case(c, "D1-LV-01")["expected"]!["diagnostic"] = "SomethingElse"),
        ["status disagrees with observation"] = ("D008", c => Case(c, "D1-EFF-01")["status"] = "holds"),
        ["version without amendment"] = ("D009", c => c["contractVersion"] = "1.1.0"),
        ["independence claim"] = ("D010", c => Case(c, "D1-LV-01")["note"] = "Independently reviewed."),
        ["allocated code outside the range"] = ("D007", c => c["diagnostics"]![0]!["code"] = "Calor0999"),
        ["proof observation removed"] = ("D003", c =>
        {
            Case(c, "D1-ANA-01")["observed"]!.AsObject().Remove("proof");
            Case(c, "D1-ANA-01")["status"] = "violates";
        }),
        ["proof status weakened to timeout"] = ("D008", c =>
        {
            Case(c, "D1-ANA-02")["observed"] = new JsonObject { ["errors"] = new JsonArray(), ["proof"] = "Timeout" };
        }),
    };

    private static IReadOnlyList<ContractViolation> Run(JsonNode contract, Func<string, string?>? compilerConstant = null)
        => RefOutContractValidator.Validate(contract, JsonNode.Parse(File.ReadAllText(Path.Combine(Root, RefOutContractValidator.R0ScopePath)))!,
            ReadRepoFile, compilerConstant);

    internal static byte[]? ReadRepoFile(string path)
    {
        var full = Path.Combine(Root, path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    internal static readonly string Root = EvidenceContractTests.RepoRoot();
    internal static JsonNode Contract() => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, RefOutContractValidator.ContractPath)))!;
    private static IEnumerable<JsonNode> Cases(JsonNode c) => c["cases"]!.AsArray().Select(x => x!);
    private static JsonNode Case(JsonNode c, string id) => Cases(c).Single(x => x["id"]!.GetValue<string>() == id);
    private static IEnumerable<string> Rules(JsonNode x) => x["rules"]!.AsArray().Select(r => r!.GetValue<string>());
    private static JsonNode Consumed(JsonNode c, string id) => c["consumes"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == id)!;
}

/// <summary>
/// #1427 (0.25 D1): executes the registered cases. Calor cases compile at the current source and must
/// reproduce their recorded observation (so #943 updates `observed`/`status` visibly when it changes
/// behavior). C# cases run `Probe.Run()`: the original must print `expectedOutput`, and every mutant (a
/// copy, dropped modifier, reorder or hoist) must print something else, so the witness can detect it.
/// </summary>
public class RefOutContractCaseTests
{
    public static IEnumerable<object[]> CalorCases => Ids("calor");
    public static IEnumerable<object[]> CSharpCases => Ids("csharp").Concat(Ids("r0-fixture"));

    [Theory]
    [MemberData(nameof(CalorCases))]
    public void CalorCaseReproducesItsObservation(string id)
    {
        var contract = RefOutContractTests.Contract();
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
            Assert.False(Program.Compile(warmSource, id + ".warmup.calr", warmOptions).HasErrors);
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
        var c = Find(RefOutContractTests.Contract(), id);
        var source = RefOutContractValidator.SourceText(c, RefOutContractTests.ReadRepoFile)!;
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

    private static IEnumerable<object[]> Ids(string kind) => RefOutContractTests.Contract()["cases"]!.AsArray()
        .Where(c => c!["kind"]!.GetValue<string>() == kind).Select(c => new object[] { c!["id"]!.GetValue<string>() });

    private static JsonNode Find(JsonNode contract, string id)
        => contract["cases"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;
}
