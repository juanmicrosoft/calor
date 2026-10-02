using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.SoundnessRegistration;

/// <summary>
/// #1419 (0.24 R1) — the released-verifier soundness sweep registration is complete, frozen, and
/// executable before #1311 runs it. Positive controls run on the committed packet; each negative
/// control changes one fact and asserts its specific code (and only the co-firing codes it names).
/// Nothing here runs the verifier: the oracle tests execute registrant C# only, and the case
/// validity test compiles with VerifyContracts and VerifyRefinements off and reads only errors.
/// </summary>
public class SoundnessRegistrationTests
{
    private const string PacketDir = "docs/plans/evidence/r1-1419";
    private const string UpdateVariable = "CALOR_UPDATE_R1_CASE_INDEX";

    // Rows whose cases are only parsed in R1: full compilation would run the always-on inheritance
    // checker (Z3) or the simplifier under test, which would observe a registered row early.
    private static readonly string[] ParseOnlyRowPrefixes = ["IMPL-", "SIMP-", "CTRL-RETRO-845"];

    // ------------------------------------------------------------------
    // Positive controls
    // ------------------------------------------------------------------

    [Fact]
    public void CommittedRegistrationIsValid()
    {
        var violations = Validate(Registration(), Templates(), Manifests());
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void CaseIndexMatchesTheGenerator()
    {
        var cases = SweepCaseGenerator.Generate(Registration(), Templates());
        var index = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["generator"] = "tests/Calor.Compiler.Tests/SoundnessRegistration/SweepCaseGenerator.cs",
            ["masterSeed"] = Registration()["generation"]!["masterSeed"]!.GetValue<string>(),
            ["count"] = cases.Count,
            ["cases"] = new JsonArray(cases.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id,
                ["row"] = c.RowId,
                ["template"] = c.TemplateId,
                ["claim"] = c.Claim,
                ["exhaustive"] = c.Exhaustive,
                ["calorSha256"] = c.CalorSha256,
                ["primeSha256"] = c.CalorPrimeSource == null ? null : SweepCaseGenerator.Sha256(c.CalorPrimeSource),
                ["oracleSha256"] = c.OracleSha256,
            }).ToArray()),
        };
        var text = index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var path = Path.Combine(RepoRoot(), PacketDir, "cases-index.json");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            File.WriteAllText(path, text, new UTF8Encoding(false));
        Assert.True(File.Exists(path), $"{path} is missing; regenerate with {UpdateVariable}=1");
        Assert.True(File.ReadAllText(path).Replace("\r\n", "\n") == text,
            $"cases-index.json differs from the generator output. The case set is frozen; changing it needs a registration amendment ({UpdateVariable}=1 regenerates).");
    }

    [Fact]
    public void CaseIdsAreUniqueAndMatchEachManifest()
    {
        var cases = SweepCaseGenerator.Generate(Registration(), Templates());
        Assert.Equal(cases.Count, cases.Select(c => c.Id).Distinct().Count());
        foreach (var (id, manifest) in Manifests())
        {
            var manifestIds = manifest["rows"]!.AsArray().SelectMany(r => r!["caseIds"]!.AsArray().Select(c => c!.GetValue<string>()));
            Assert.True(manifestIds.SequenceEqual(cases.OrderBy(c => RowOrder(c.RowId)).Select(c => c.Id)), $"manifest {id} case ids differ");
        }
    }

    [Fact]
    public void OracleControlsProduceTheirRegisteredVerdicts()
    {
        var failures = new List<string>();
        foreach (var control in Templates()["oracleControls"]!.AsArray())
        {
            var template = control!["template"]!.DeepClone();
            template["id"] = control["id"]!.GetValue<string>();
            template["row"] = "ORACLE-CONTROL";
            template["calor"] = "";
            if (control["overflow"] is { } overflow)
                template["overflow"] = overflow.GetValue<string>();
            var testCase = SweepCaseGenerator.Expand(template, control["id"]!.GetValue<string>(), 0,
                new SweepCaseGenerator.SplitMix64(1419));
            var verdict = IndependentOracle.Evaluate(testCase.OracleSource);
            var expect = control["expect"]!.GetValue<string>();
            if (verdict.Kind != expect)
                failures.Add($"{control["id"]}: expected {expect}, got {verdict.Kind} ({verdict.Error})");
            if (control["witness"] is { } witness && verdict.Witness != witness.GetValue<string>())
                failures.Add($"{control["id"]}: expected witness {witness}, got {verdict.Witness}");
            if (control["violationKind"] is { } kind && verdict.ViolationKind != kind.GetValue<string>())
                failures.Add($"{control["id"]}: expected {kind}, got {verdict.ViolationKind}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EveryCaseOracleCompilesAgainstBclOnly()
    {
        var cases = SweepCaseGenerator.Generate(Registration(), Templates());
        Assert.DoesNotContain(IndependentOracle.BclReferences, r => (r.Display ?? "").Contains("Calor", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cases, c => c.OracleSource.Contains("Calor.", StringComparison.Ordinal));

        // One compilation with every case in its own namespace keeps this fast; each case still
        // compiles independently at run time in #1311.
        var parse = new CSharpParseOptions(LanguageVersion.CSharp14);
        var trees = cases.Select((c, i) => CSharpSyntaxTree.ParseText(
            ToNamespaced(c.OracleSource, $"Case{i:D4}"), parse, path: c.Id)).ToList();
        foreach (var mode in new[] { true, false })
        {
            var subset = trees.Where((_, i) => cases[i].OracleSource.Contains("Checked = true;", StringComparison.Ordinal) == mode);
            var compilation = CSharpCompilation.Create("R1OracleCheck", subset, IndependentOracle.BclReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: mode,
                    nullableContextOptions: NullableContextOptions.Enable));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Take(20).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => $"{e.Location.SourceTree?.FilePath}: {e}")));
        }
    }

    [Fact]
    public void EveryCaseIsValidCalorBeforeVerification()
    {
        // Front-end facts about the frozen cases, checked while this tree still has B1's front end (the
        // cases target B1 and N1, so a later front-end change ends the check rather than the freeze).
        // Every case lexes and parses cleanly, so a template typo cannot pass as a refusal. With
        // verification and refinements OFF (only errors are read): a refused row is rejected exactly
        // when it registers frontEndRefusalCodes, and only with those codes; every other case outside
        // the parse-only rows compiles.
        var b1 = Manifests()["B1"];
        if (b1["frontEndBlobs"]!.AsObject().Concat(b1["entryPoints"]!.AsObject().Select(e => KeyValuePair.Create(e.Value!["path"]!.GetValue<string>(), e.Value["blob"])))
            .Any(f => GitBlobId(Path.Combine(RepoRoot(), f.Key)) != f.Value!.GetValue<string>()))
            return;
        var refusalCodes = Registration()["denominator"]!["rows"]!.AsArray()
            .Where(r => r!["classification"]!.GetValue<string>() == "unsupported-refused")
            .ToDictionary(r => r!["id"]!.GetValue<string>(), r => r!["frontEndRefusalCodes"]!.AsArray().Select(c => c!.GetValue<string>()).ToHashSet());
        var failures = new List<string>();
        foreach (var c in SweepCaseGenerator.Generate(Registration(), Templates()))
        {
            foreach (var source in new[] { c.CalorSource, c.CalorPrimeSource }.OfType<string>())
            {
                var parseDiagnostics = new DiagnosticBag();
                new Parser(new Lexer(source, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics).Parse();
                if (parseDiagnostics.HasErrors)
                {
                    failures.Add($"{c.Id} parse: {string.Join("; ", parseDiagnostics.Errors.Select(d => d.Message))}");
                    continue;
                }
                if (ParseOnlyRowPrefixes.Any(p => c.RowId.StartsWith(p, StringComparison.Ordinal)))
                    continue;
                var result = Program.Compile(source, "r1-case.calr", new CompilationOptions
                {
                    VerifyContracts = false,
                    VerifyRefinements = false,
                    EnableVerificationAnalyses = false,
                });
                var codes = result.Diagnostics.Errors.Select(d => d.Code).ToHashSet();
                var allowed = refusalCodes.GetValueOrDefault(c.RowId) ?? [];
                if (codes.Count > 0 != allowed.Count > 0 || !codes.IsSubsetOf(allowed))
                    failures.Add($"{c.Id} compile: [{string.Join(",", codes)}] expected [{string.Join(",", allowed)}] {string.Join("; ", result.Diagnostics.Errors.Select(d => d.Message))}");
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} invalid cases:\n" + string.Join("\n", failures.Take(40)));
    }

    [Fact]
    public void FrozenWhitelistMatchesTheLiveWhitelistWhileTheTranslatorIsUnchangedSinceB1()
    {
        // The frozen denominator is B1's ModeledForms rendering. While ContractTranslator.cs is
        // byte-identical to B1's blob, it must equal the live rendering; after a later repair changes
        // the file, the B1 pin is checked by derive-manifests.py --check (git) in #1311's preflight.
        var path = Path.Combine(RepoRoot(), "src/Calor.Compiler/Verification/Z3/ContractTranslator.cs");
        var pinned = Manifests()["B1"]["entryPoints"]!["whitelist"]!["blob"]!.GetValue<string>();
        var frozen = Registration()["denominator"]!["frozenWhitelist"]!.AsArray().Select(l => l!.GetValue<string>()).ToList();
        Assert.Equal(8, frozen.Count);
        if (GitBlobId(path) != pinned)
            return;
        var live = Calor.Compiler.Verification.Z3.ModeledForms.RenderWhitelist().TrimEnd('\n').Split('\n');
        Assert.Equal(frozen, live);
    }

    [Fact]
    public void PacketHashesMatchSha256Manifest()
    {
        var root = RepoRoot();
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, PacketDir, "sha256.json")))!["files"]!.AsObject();
        foreach (var required in new[] { "registration.json", "templates.json", "cases-index.json", "manifest-B1.json",
                     "manifest-N1.json", "derive-manifests.py" })
            Assert.True(manifest.ContainsKey($"{PacketDir}/{required}"), $"sha256.json does not cover {required}");
        Assert.True(manifest.ContainsKey("docs/plans/v0.24-r1-soundness-registration.md"), "sha256.json does not cover the document");
        var mismatches = manifest.Where(kv => Sha256Lf(Path.Combine(root, kv.Key)) != kv.Value!.GetValue<string>())
            .Select(kv => $"\"{kv.Key}\": \"{Sha256Lf(Path.Combine(root, kv.Key))}\"").ToList();
        Assert.True(mismatches.Count == 0, "Re-hash:\n" + string.Join(",\n", mismatches));
    }

    // ------------------------------------------------------------------
    // Negative controls: one changed fact, one specific code
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("R001")]
    [InlineData("R002")]
    [InlineData("R003")]
    [InlineData("R004")]
    [InlineData("R005")]
    [InlineData("R006")]
    [InlineData("R007")]
    [InlineData("R008")]
    [InlineData("R009")]
    [InlineData("R010")]
    [InlineData("R011")]
    [InlineData("R012")]
    [InlineData("R013")]
    [InlineData("R014")]
    [InlineData("R015")]
    [InlineData("R016")]
    public void NegativeControlFailsClosed(string code)
    {
        var registration = Registration();
        var templates = Templates();
        var manifests = Manifests();
        JsonNode Row(string id) => registration["denominator"]!["rows"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == id)!;
        var coFiring = new HashSet<string>(StringComparer.Ordinal);
        switch (code)
        {
            case "R001": Row("NUM-SHIFT")["classification"] = "maybe-modeled"; break;
            case "R002": Row("XCL-KINDUCTION")["releaseCritical"] = true; break;
            case "R003": Row("NUM-SHIFT")["entryPoints"]!.AsArray().Add("no-such-entry-point"); break;
            case "R004":
                templates["templates"]!.AsArray().Add(new JsonObject { ["id"] = "T-X", ["row"] = "NO-SUCH-ROW", ["instances"] = 0,
                    ["calor"] = "", ["oracle"] = new JsonObject { ["prop"] = "true" } });
                break;
            case "R005": registration["budget"]!["allocation"]!["minimizationReserve"] = 10_000; break;
            case "R006": registration["baselines"]!.AsArray().Single(b => b!["id"]!.GetValue<string>() == "N1")!["commit"] = "b1d23d7d74aa17a460d6a7d429836ca8396bb182"; break;
            case "R007":
                // Pooling B1 and N1 into one path also breaks the N1 manifest's own result path (named co-fire).
                registration["noPooling"]!["resultPaths"]!["N1"] = registration["noPooling"]!["resultPaths"]!["B1"]!.GetValue<string>();
                coFiring.Add("R016");
                break;
            case "R008": registration["dispositionHandoff"]!["vocabulary"]!.AsArray().RemoveAt(4); break;
            case "R009":
                foreach (var row in registration["denominator"]!["rows"]!.AsArray())
                    row!["samplingDimensions"] = new JsonArray(row["samplingDimensions"]!.AsArray()
                        .Select(d => d!.GetValue<string>()).Where(d => d != "caches").Select(d => (JsonNode)d).ToArray());
                break;
            case "R010":
                var map = registration["denominator"]!["whitelistCoverageMap"]!.AsArray();
                map.Remove(map.Single(m => m!["member"]!.GetValue<string>() == "Modulo"));
                break;
            case "R011": registration["hypotheses"]![0]!["status"] = "executed"; break;
            case "R012": registration["controls"]!["retrospective845"]!["preFixParent"] = "0000000000000000000000000000000000000000"; break;
            case "R013": templates["templates"]![0]!["oracle"]!["prop"] = "Calor.Compiler.Program.Compile(null!, null, null!) != null"; break;
            case "R014": templates["templates"]![0]!["expectedOutcome"] = "Proven"; break;
            case "R015": registration["freeze"]!["decisionBearingInspectionBeforeFreeze"] = true; break;
            case "R016": manifests["N1"]["rows"]![0]!["id"] = "B1:NUM-ARITH-ADD"; break;
        }
        var codes = Validate(registration, templates, manifests).Select(v => v.Code).ToHashSet();
        Assert.Contains(code, codes);
        codes.Remove(code);
        codes.ExceptWith(coFiring);
        Assert.True(codes.Count == 0, $"unrelated codes fired: {string.Join(", ", codes)}");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IReadOnlyList<SoundnessRegistrationValidator.Violation> Validate(
        JsonNode registration, JsonNode templates, IReadOnlyDictionary<string, JsonNode> manifests) =>
        SoundnessRegistrationValidator.Validate(registration, templates, Contract(), manifests,
            SweepCaseGenerator.Generate(registration, templates));

    private static string ToNamespaced(string source, string ns)
    {
        var classStart = source.IndexOf("public static class R1Oracle", StringComparison.Ordinal);
        return source[..classStart] + $"namespace {ns} {{\n" + source[classStart..] + "}\n";
    }

    private static int RowOrder(string rowId) =>
        Registration()["denominator"]!["rows"]!.AsArray().Select(r => r!["id"]!.GetValue<string>()).ToList().IndexOf(rowId);

    private static JsonNode Registration() => Load(PacketDir + "/registration.json");

    private static JsonNode Templates() => Load(PacketDir + "/templates.json");

    private static JsonNode Contract() => Load("docs/plans/evidence/evidence-contract-1407/contract.json");

    private static Dictionary<string, JsonNode> Manifests() => new(StringComparer.Ordinal)
    {
        ["B1"] = Load(PacketDir + "/manifest-B1.json"),
        ["N1"] = Load(PacketDir + "/manifest-N1.json"),
    };

    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);

    private static JsonNode Load(string relative)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(relative, out var text))
                Cache[relative] = text = File.ReadAllText(Path.Combine(RepoRoot(), relative));
            return JsonNode.Parse(text)!;
        }
    }

    private static string Describe(IEnumerable<SoundnessRegistrationValidator.Violation> violations) =>
        string.Join("\n", violations.Select(v => $"{v.Code}: {v.Message}"));

    private static string GitBlobId(string path)
    {
        var content = File.ReadAllBytes(path);
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. content]));
    }

    private static string Sha256Lf(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        return Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
