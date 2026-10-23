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
/// #1419 (0.24 R1): the soundness sweep's case packet (<c>tests/TestData/SoundnessSweep/</c>, read by
/// <c>bench/Calor.Soundness.Sweep</c>) is executable. The generator reproduces the committed case index,
/// the oracle controls give their expected verdicts, every case oracle compiles against the BCL only, and
/// every case is valid Calor. Nothing here runs the verifier: the oracle tests execute C# only, and the
/// case validity test compiles with VerifyContracts and VerifyRefinements off and reads only errors.
/// </summary>
public class SoundnessRegistrationTests
{
    private const string PacketDir = "tests/TestData/SoundnessSweep";
    private const string UpdateVariable = "CALOR_UPDATE_R1_CASE_INDEX";

    // Rows whose cases are only parsed in R1: full compilation would run the always-on inheritance
    // checker (Z3) or the simplifier under test, which would observe a registered row early.
    private static readonly string[] ParseOnlyRowPrefixes = ["IMPL-", "SIMP-", "CTRL-RETRO-845"];

    // ------------------------------------------------------------------
    // The packet is executable
    // ------------------------------------------------------------------

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
            $"cases-index.json differs from the generator output. The sweep's preflight compares the same index; regenerate it with {UpdateVariable}=1 and review the changed cases.");
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

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string ToNamespaced(string source, string ns)
    {
        var classStart = source.IndexOf("public static class R1Oracle", StringComparison.Ordinal);
        return source[..classStart] + $"namespace {ns} {{\n" + source[classStart..] + "}\n";
    }

    private static int RowOrder(string rowId) =>
        Registration()["denominator"]!["rows"]!.AsArray().Select(r => r!["id"]!.GetValue<string>()).ToList().IndexOf(rowId);

    private static JsonNode Registration() => Load(PacketDir + "/registration.json");

    private static JsonNode Templates() => Load(PacketDir + "/templates.json");

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

    private static string GitBlobId(string path)
    {
        var content = File.ReadAllBytes(path);
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. content]));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
