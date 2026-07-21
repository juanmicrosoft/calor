using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the docs-drift checker behind <c>calor self-check docs</c>
/// (Phase 1 item 6: spec single-sourcing). Each test injects a fake drifted
/// doc string and asserts the drift is detected — or that clean docs pass.
/// </summary>
public class DocDriftCheckerTests
{
    private static DocDriftInputs BaseInputs(
        DocFile[]? keywordDocs = null,
        DocFile[]? diagnosticCodeDocs = null,
        DocFile[]? parseExampleDocs = null,
        DocFile? effectsReferenceDoc = null,
        DocFile[]? effectDocsForwardOnly = null,
        DocFile? cliCodesDoc = null,
        DocFile[]? versionScanDocs = null,
        MirrorDoc[]? mirrorDocs = null)
    {
        return new DocDriftInputs
        {
            Version = "0.9.9",
            LexerKeywords = ["M", "F", "EACH", "/C", "IV", "Pf", "PP", "/PP"],
            DiagnosticCodes = ["Calor0001", "Calor0830", "Calor1300", "Calor1310", "Calor1320"],
            KnownEffectCodes = ["cw", "mut", "mut:col", "fw"],
            DocumentedEffectCodes = ["cw", "mut", "mut:col"],
            KeywordDocs = keywordDocs ?? [],
            DiagnosticCodeDocs = diagnosticCodeDocs ?? [],
            ParseExampleDocs = parseExampleDocs ?? [],
            EffectsReferenceDoc = effectsReferenceDoc,
            EffectDocsForwardOnly = effectDocsForwardOnly ?? [],
            CliCodesDoc = cliCodesDoc,
            VersionScanDocs = versionScanDocs ?? [],
            MirrorDocs = mirrorDocs ?? [],
        };
    }

    // --- Mirror-doc drift (AGENTS.md single-sourced from CLAUDE.md, #708) ---

    private const string SampleClaude =
        "# CLAUDE.md — Calor Compiler\n\nBuild with `dotnet build`. Use `§F{id:Name:pub}`.\n";

    [Fact]
    public void AgentsMdTransform_SwapsTitle_AddsBanner_IsIdempotent()
    {
        var once = DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude);
        Assert.StartsWith("# AGENTS.md — Calor Compiler\n", once);
        Assert.Contains("Generated from CLAUDE.md", once);
        Assert.DoesNotContain("# CLAUDE.md — Calor Compiler", once);
        // Body is preserved verbatim after the swapped head.
        Assert.Contains("Build with `dotnet build`. Use `§F{id:Name:pub}`.", once);
        // Regenerating from CLAUDE.md yields the same bytes (deterministic).
        Assert.Equal(once, DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude));
    }

    [Fact]
    public void MirrorInSync_PassesClean()
    {
        var expected = DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude);
        var mirror = new MirrorDoc("AGENTS.md", "CLAUDE.md", expected, expected);
        Assert.Empty(DocDriftChecker.Check(BaseInputs(mirrorDocs: [mirror])));
    }

    [Fact]
    public void MirrorOutOfSync_IsDetected()
    {
        var expected = DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude);
        var mirror = new MirrorDoc("AGENTS.md", "CLAUDE.md", expected + "hand edit\n", expected);
        var finding = Assert.Single(DocDriftChecker.Check(BaseInputs(mirrorDocs: [mirror])));
        Assert.Equal(DiagnosticCode.DocDriftMirrorOutOfSync, finding.Code);
        Assert.Contains("out of sync", finding.Message);
    }

    [Fact]
    public void MirrorMissing_IsDetected()
    {
        var expected = DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude);
        var mirror = new MirrorDoc("AGENTS.md", "CLAUDE.md", Actual: null, expected);
        var finding = Assert.Single(DocDriftChecker.Check(BaseInputs(mirrorDocs: [mirror])));
        Assert.Equal(DiagnosticCode.DocDriftMirrorOutOfSync, finding.Code);
        Assert.Contains("is missing", finding.Message);
    }

    [Fact]
    public void AgentsMdTransform_ThrowsOnAnchorMismatch()
    {
        Assert.False(DocDriftChecker.TryAgentsMdFromClaudeMd("# Something Else\n\nbody\n", out _));
        Assert.Throws<InvalidOperationException>(() =>
            DocDriftChecker.AgentsMdFromClaudeMd("# Something Else\n"));
    }

    // --- RegenerateAgentsMd file-IO path (--fix) ---

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "calor-mirror-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Regenerate_WritesWhenMissingOrStale_ThenIsIdempotent()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), SampleClaude);
            var agents = Path.Combine(dir, "AGENTS.md");

            // Missing → written.
            Assert.Equal(DocDriftChecker.MirrorRegenResult.Written, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.Equal(DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude), File.ReadAllText(agents));

            // Already in sync → no write (idempotent).
            var before = File.GetLastWriteTimeUtc(agents);
            Assert.Equal(DocDriftChecker.MirrorRegenResult.AlreadyInSync, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.Equal(before, File.GetLastWriteTimeUtc(agents));

            // Stale → rewritten.
            File.WriteAllText(agents, "hand edited\n");
            Assert.Equal(DocDriftChecker.MirrorRegenResult.Written, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.Equal(DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude), File.ReadAllText(agents));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Regenerate_SourceMissing_DoesNotWrite()
    {
        var dir = NewTempDir();
        try
        {
            Assert.Equal(DocDriftChecker.MirrorRegenResult.SourceMissing, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.False(File.Exists(Path.Combine(dir, "AGENTS.md")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Regenerate_AnchorMismatch_DoesNotWrite()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), "# Renamed Title\n\nbody\n");
            Assert.Equal(DocDriftChecker.MirrorRegenResult.AnchorMismatch, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.False(File.Exists(Path.Combine(dir, "AGENTS.md")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- Keyword drift (the §FOREACH-vs-§EACH class) ---

    [Fact]
    public void UnknownKeywordIsDetected()
    {
        var doc = new DocFile("fake.md", "Use `§FOREACH{id}` to loop over collections.");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUnknownKeyword, finding.Code);
        Assert.Contains("§FOREACH", finding.Message);
        Assert.Equal("fake.md", finding.FilePath);
        Assert.Equal(1, finding.Span.Line);
    }

    [Fact]
    public void KnownKeywordsPass()
    {
        var doc = new DocFile("fake.md",
            "`§M{Name}` opens a module, `§EACH` iterates, `§/C` closes calls,\n" +
            "`§IV (expr)` declares an invariant, `§Pf` prints, `§PP{X}`...`§/PP{X}` wraps.");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void UnknownClosingKeywordIsDetected()
    {
        var doc = new DocFile("fake.md", "Close with `§/FOREACH`.");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUnknownKeyword, finding.Code);
        Assert.Contains("§/FOREACH", finding.Message);
    }

    // --- Diagnostic-code drift (the Calor0820-vs-0830 class) ---

    [Fact]
    public void UnknownDiagnosticCodeIsDetected()
    {
        var doc = new DocFile("fake.md", "Closer tags raise `Calor0820`.");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUnknownDiagnosticCode, finding.Code);
        Assert.Contains("Calor0820", finding.Message);
    }

    [Fact]
    public void KnownDiagnosticCodeAndPopulatedRangePass()
    {
        var doc = new DocFile("fake.md",
            "Closer tags raise `Calor0830`. Bands: Calor0001–0099 (lexer), `Calor1300`–`Calor1399` (CLI).");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void EmptyDiagnosticRangeIsDetected()
    {
        var doc = new DocFile("fake.md", "Reserved: Calor5000–5099 (future).");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftEmptyDiagnosticRange, finding.Code);
    }

    [Fact]
    public void RangeEndpointsAreNotRequiredToExistIndividually()
    {
        // Calor1399 does not exist as a concrete code, but the 1300 band is populated.
        var doc = new DocFile("fake.md", "CLI codes are Calor1300-Calor1399.");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc]));

        Assert.Empty(findings);
    }

    // --- Effect-code drift (the undocumented-mut class) ---

    private const string EffectsDocHeader = "# Effects\n\n## Effect Codes\n\n| Code | Effect |\n|:-----|:-------|\n";

    [Fact]
    public void UndocumentedEffectCodeIsDetected()
    {
        // 'mut' and 'mut:col' are implemented but missing from the table.
        var doc = new DocFile("effects.md", EffectsDocHeader + "| `cw` | Console write |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(DiagnosticCode.DocDriftUndocumentedEffectCode, f.Code));
        Assert.Contains(findings, f => f.Message.Contains("'mut'"));
        Assert.Contains(findings, f => f.Message.Contains("'mut:col'"));
    }

    [Fact]
    public void UnknownEffectCodeIsDetected()
    {
        var doc = new DocFile("effects.md", EffectsDocHeader +
            "| `cw` | Console write |\n| `mut` | Mutation |\n| `mut:col` | Collection mutation |\n| `zap` | Not real |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUnknownEffectCode, finding.Code);
        Assert.Contains("'zap'", finding.Message);
    }

    [Fact]
    public void CompleteEffectTablePasses()
    {
        var doc = new DocFile("effects.md", EffectsDocHeader +
            "| `cw` | Console write |\n| `mut` | Mutation |\n| `mut:col` | Collection mutation |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        Assert.Empty(findings);
    }

    [Fact]
    public void LegacyEffectCodesAreAcceptedButNotRequired()
    {
        // 'fw' is legacy: allowed in the table, but its absence is not drift.
        var doc = new DocFile("effects.md", EffectsDocHeader +
            "| `cw` | Console write |\n| `mut` | Mutation |\n| `mut:col` | Collection mutation |\n| `fw` | Legacy file write |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        Assert.Empty(findings);
    }

    [Fact]
    public void ForwardOnlyEffectDocNeedNotBeComplete()
    {
        var doc = new DocFile("index.md", EffectsDocHeader + "| `cw` | Console write |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectDocsForwardOnly: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void MissingEffectCodesSectionIsDetected()
    {
        var doc = new DocFile("effects.md", "# Effects\n\nNo table here.\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftMissingInput, finding.Code);
    }

    // --- CLI code-table completeness ---

    [Fact]
    public void MissingCliCodeTableEntryIsDetected()
    {
        var doc = new DocFile("structured-output.md",
            "| Code | Meaning |\n|:-----|:--------|\n| `Calor1300` | Lint finding |\n| `Calor1310` | Input not found |\n");
        var findings = DocDriftChecker.Check(BaseInputs(cliCodesDoc: doc));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUndocumentedCliCode, finding.Code);
        Assert.Contains("Calor1320", finding.Message);
    }

    [Fact]
    public void CompleteCliCodeTablePasses()
    {
        var doc = new DocFile("structured-output.md",
            "| `Calor1300` | Lint |\n| `Calor1310` | Input |\n| `Calor1320` | Drift |\n");
        var findings = DocDriftChecker.Check(BaseInputs(cliCodesDoc: doc));

        Assert.Empty(findings);
    }

    // --- Meta-notation escapes: foreign fences and the suppression marker ---

    [Fact]
    public void FencedBlocksWithForeignInfoStringAreNotScanned()
    {
        var doc = new DocFile("fake.md",
            "Prose.\n\n```text\n§FOREACH is hypothetical here.\n```\n\n" +
            "```csharp\n// mentions Calor9876 in C# code\nvar s = \"§BOGUS\";\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc], diagnosticCodeDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void BareAndCalorFencesAreStillScanned()
    {
        var doc = new DocFile("fake.md",
            "```\n§FOREACH{x} items\n```\n\n```calor\n§FOREACH{y} items\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(DiagnosticCode.DocDriftUnknownKeyword, f.Code));
        Assert.Equal(new[] { 2, 6 }, findings.Select(f => f.Span.Line).OrderBy(l => l).ToArray());
    }

    [Fact]
    public void SuppressionMarkerCoversTheNextLineOnly()
    {
        var doc = new DocFile("fake.md",
            "<!-- drift:ignore -->\nAn explicit `§/X` raises an error.\nBut `§/Y` here is drift.\n");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Contains("§/Y", finding.Message);
        Assert.Equal(3, finding.Span.Line);
    }

    [Fact]
    public void SuppressionMarkerMayTrailThePrecedingLine()
    {
        var doc = new DocFile("fake.md",
            "| Ops | `(+ a b)` | <!-- drift:ignore -->\n| Block end | _(no `§/X` needed)_ |\n");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void SuppressionMarkerAppliesToDiagnosticCodeAndVersionScans()
    {
        var doc = new DocFile("fake.md",
            "<!-- drift:ignore -->\nA hypothetical `Calor9876` code.\n" +
            "<!-- drift:ignore -->\nHistoric example: version 0.9.9 shipped it.\n");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc], versionScanDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void UnknownKeywordMessageMentionsTheSuppressionMarker()
    {
        var doc = new DocFile("fake.md", "Use `§BOGUS` here.");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Contains(DocDriftChecker.SuppressionMarker, finding.Message);
    }

    // --- Parse-the-examples (the rotted-snippet class) ---

    private const string FizzBuzzExample =
        "§M{m001:FizzBuzz}\n" +
        "  §F{f001:Main:pub} () -> void\n" +
        "    §E{cw}\n" +
        "    §L{for1:i:1:100:1}\n" +
        "      §IF{if1} (== (% i 15) 0)\n" +
        "        §P \"FizzBuzz\"\n" +
        "      §EI (== (% i 3) 0)\n" +
        "        §P \"Fizz\"\n" +
        "      §EL\n" +
        "        §P i\n";

    [Fact]
    public void CompleteProgramExampleThatParsesPasses()
    {
        var doc = new DocFile("fake.md", "Example:\n\n```calor\n" + FizzBuzzExample + "```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void RottedCompleteProgramExampleIsDetected()
    {
        // §FOREACH does not lex; a doc example using it has rotted.
        var doc = new DocFile("fake.md",
            "Example:\n\n```calor\n§M{m1:Loops}\n  §F{f1:Sum:pub} (i32[]:items) -> void\n    §FOREACH{e1:x} items\n      §P x\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(DiagnosticCode.DocDriftExampleParseError, f.Code));
        var finding = findings[0];
        Assert.Equal("fake.md", finding.FilePath);
        Assert.Equal(6, finding.Span.Line); // §FOREACH sits on doc line 6
    }

    [Fact]
    public void CalorFragmentsNotStartingWithModuleAreSkipped()
    {
        // No §M on the first non-blank line => deliberate fragment, not parsed.
        var doc = new DocFile("fake.md",
            "```calor\n// Before (inconsistent)\n§FOREACH{e1:x} items\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void SuppressionMarkerBeforeFenceExemptsTheExample()
    {
        var doc = new DocFile("fake.md",
            "<!-- drift:ignore -->\n```calor\n§M{m1:Hypothetical}\n  §FUTURE syntax sketch\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void NonCalorFencesAreNotParseChecked()
    {
        var doc = new DocFile("fake.md",
            "```\n§M{m1:Bare}\n  §FOREACH{e1:x} items\n```\n\n```text\n§M{m2:Text}\n  §FOREACH{e2:x} items\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.Empty(findings);
    }

    // --- Hardcoded version (the stale-0.3.5 class) ---

    [Fact]
    public void HardcodedCurrentVersionIsDetected()
    {
        var doc = new DocFile("fake.md", "Install calor 0.9.9 from NuGet.");
        var findings = DocDriftChecker.Check(BaseInputs(versionScanDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftHardcodedVersion, finding.Code);
    }

    [Fact]
    public void OtherVersionStringsAreNotFlagged()
    {
        var doc = new DocFile("fake.md",
            "Historic release 0.3.5; unrelated 10.9.9 and 0.9.9.1 and v0.9.90 are fine.");
        var findings = DocDriftChecker.Check(BaseInputs(versionScanDocs: [doc]));

        Assert.Empty(findings);
    }

    // --- Ground truth wiring: the real registries are enumerable ---

    [Fact]
    public void LexerExposesKeywordsIncludingSpecialForms()
    {
        Assert.Contains("EACH", Lexer.KeywordNames);
        Assert.Contains("IV", Lexer.KeywordNames);
        Assert.Contains("PP", Lexer.KeywordNames);
        Assert.Contains("/PP", Lexer.KeywordNames);
        Assert.Contains("PPE", Lexer.KeywordNames);
        Assert.Contains("CS", Lexer.KeywordNames);
        Assert.Contains("RAW", Lexer.KeywordNames);
        Assert.Contains("/RAW", Lexer.KeywordNames);
        Assert.Contains("CSHARP", Lexer.KeywordNames);
        Assert.Contains("/CSHARP", Lexer.KeywordNames);
        Assert.DoesNotContain("FOREACH", Lexer.KeywordNames);
        Assert.DoesNotContain("INV", Lexer.KeywordNames);
    }

    [Fact]
    public void EffectCodeRegistryPreservesToCompactBehavior()
    {
        Assert.Equal("cw", EffectCodes.ToCompact("io", "console_write"));
        Assert.Equal("fs:r", EffectCodes.ToCompact("IO", "Filesystem_Read"));
        Assert.Equal("mut:col", EffectCodes.ToCompact("mutation", "collection"));
        Assert.Equal("db:r", EffectCodes.ToCompact("io", "dbr"));
        Assert.Equal("something_else", EffectCodes.ToCompact("io", "something_else"));

        Assert.Contains("mut", EffectCodes.DocumentedCompactCodes);
        Assert.Contains("mut:col", EffectCodes.DocumentedCompactCodes);
        Assert.DoesNotContain("fw", EffectCodes.DocumentedCompactCodes);
        Assert.Contains("fw", EffectCodes.KnownCompactCodes);
    }

    [Fact]
    public void ImplementedDiagnosticCodesIncludeKnownConstants()
    {
        var codes = DocDriftChecker.GetImplementedDiagnosticCodes();
        Assert.Contains("Calor0830", codes);
        Assert.Contains("Calor0702", codes); // verification-pass code, registered via constant
        Assert.Contains("Calor1320", codes);
        Assert.DoesNotContain("Calor9876", codes);
    }

    // --- Coverage probes: LoadFromRepository must scan the full doc set.
    // Reviewer cases: §BOGUS planted in docs/syntax-reference/structure-tags.md
    // and Calor9876 planted in docs/syntax-reference/index.md must be detected.

    private static string CreateFakeRepo(
        string structureTagsContent = "# Structure Tags\n",
        string syntaxIndexContent = "# Syntax Reference\n")
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-drift-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "syntax-reference"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "cli"));
        File.WriteAllText(
            Path.Combine(root, "Directory.Build.props"),
            "<Project><PropertyGroup><Version>9.9.9</Version></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "# CLAUDE\n");
        File.WriteAllText(Path.Combine(root, "docs", "syntax-reference", "index.md"), syntaxIndexContent);
        File.WriteAllText(Path.Combine(root, "docs", "syntax-reference", "structure-tags.md"), structureTagsContent);
        File.WriteAllText(Path.Combine(root, "docs", "syntax-reference", "effects.md"), "# Effects\n");
        File.WriteAllText(Path.Combine(root, "docs", "cli", "structured-output.md"), "# Structured Output\n");
        return root;
    }

    private static List<Diagnostic> CheckFakeRepo(string root)
    {
        try
        {
            var loadErrors = new List<Diagnostic>();
            var inputs = DocDriftChecker.LoadFromRepository(root, loadErrors);
            var findings = DocDriftChecker.Check(inputs);
            findings.AddRange(loadErrors);
            return findings;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepositoryScanDetectsUnknownKeywordPlantedInStructureTagsDoc()
    {
        var root = CreateFakeRepo(
            structureTagsContent: "# Structure Tags\n\nClose every block with `§BOGUS`.\n");
        var findings = CheckFakeRepo(root);

        var finding = Assert.Single(findings, f => f.Code == DiagnosticCode.DocDriftUnknownKeyword);
        Assert.Contains("§BOGUS", finding.Message);
        Assert.Contains("structure-tags.md", finding.FilePath);
        Assert.Equal(3, finding.Span.Line);
    }

    [Fact]
    public void RepositoryScanDetectsUnknownDiagnosticCodePlantedInSyntaxIndex()
    {
        var root = CreateFakeRepo(
            syntaxIndexContent: "# Syntax Reference\n\nViolations raise `Calor9876`.\n");
        var findings = CheckFakeRepo(root);

        var finding = Assert.Single(findings, f => f.Code == DiagnosticCode.DocDriftUnknownDiagnosticCode);
        Assert.Contains("Calor9876", finding.Message);
        Assert.Contains("index.md", finding.FilePath);
        Assert.Equal(3, finding.Span.Line);
    }
}
