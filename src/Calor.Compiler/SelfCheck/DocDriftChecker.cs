using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3.Cache;
using Calor.Compiler.Effects;

namespace Calor.Compiler.SelfCheck;

/// <summary>
/// A documentation file to check: a display path and its full content.
/// </summary>
public sealed record DocFile(string Path, string Content);

/// <summary>
/// Inputs to <see cref="DocDriftChecker"/>: the implementation-derived ground
/// truth (keywords, diagnostic codes, effect codes, version) plus the doc
/// contents to verify against it. Tests construct this directly with fake doc
/// strings; the CLI builds it from the repository via
/// <see cref="DocDriftChecker.LoadFromRepository"/>.
/// </summary>
public sealed class DocDriftInputs
{
    /// <summary>Current compiler version (from Directory.Build.props).</summary>
    public required string Version { get; init; }

    /// <summary>Every §-keyword the lexer accepts.</summary>
    public required IReadOnlyCollection<string> LexerKeywords { get; init; }

    /// <summary>Every diagnostic code defined in <see cref="DiagnosticCode"/>.</summary>
    public required IReadOnlyCollection<string> DiagnosticCodes { get; init; }

    /// <summary>Every compact effect code the compiler knows (including legacy forms).</summary>
    public required IReadOnlyCollection<string> KnownEffectCodes { get; init; }

    /// <summary>The non-legacy compact effect codes that must be documented.</summary>
    public required IReadOnlyCollection<string> DocumentedEffectCodes { get; init; }

    /// <summary>Docs whose §-keyword references must all exist in the lexer.</summary>
    public IReadOnlyList<DocFile> KeywordDocs { get; init; } = [];

    /// <summary>Docs whose CalorNNNN citations must all exist in <see cref="DiagnosticCode"/>.</summary>
    public IReadOnlyList<DocFile> DiagnosticCodeDocs { get; init; } = [];

    /// <summary>
    /// Docs whose fenced <c>```calor</c> examples are parse-checked with the
    /// real lexer and parser. Only blocks whose first non-blank line starts
    /// with §M are checked (the complete-program convention); anything else is
    /// treated as a deliberate fragment and skipped.
    /// </summary>
    public IReadOnlyList<DocFile> ParseExampleDocs { get; init; } = [];

    /// <summary>
    /// The effect-code reference doc (docs/syntax-reference/effects.md): its
    /// "Effect Codes" table is checked in both directions (unknown codes flagged,
    /// and every documented effect code must be present).
    /// </summary>
    public DocFile? EffectsReferenceDoc { get; init; }

    /// <summary>
    /// Additional docs with an "Effect Codes" table checked forward-only
    /// (listed codes must exist; the table need not be complete).
    /// </summary>
    public IReadOnlyList<DocFile> EffectDocsForwardOnly { get; init; } = [];

    /// <summary>
    /// The CLI structured-output doc: its Calor1300-band table must list every
    /// implemented 1300-band code and vice versa.
    /// </summary>
    public DocFile? CliCodesDoc { get; init; }

    /// <summary>Docs scanned for a hardcoded current version string.</summary>
    public IReadOnlyList<DocFile> VersionScanDocs { get; init; } = [];

    /// <summary>Current language semantics, independently of the compiler package version.</summary>
    public string SemanticsVersion { get; init; } = global::Calor.Compiler.SemanticsVersion.VersionString;

    /// <summary>Normative pages whose current semantics-version claims must match the implementation.</summary>
    public IReadOnlyList<DocFile> SemanticsVersionDocs { get; init; } = [];

    /// <summary>
    /// Mirror docs that must equal a deterministic transform of a single source
    /// (e.g. AGENTS.md is CLAUDE.md with the title swapped). Guards against the
    /// two agent manuals silently diverging (#708).
    /// </summary>
    public IReadOnlyList<MirrorDoc> MirrorDocs { get; init; } = [];

    /// <summary>
    /// The agent syntax exemplar. Its complete §M programs are compiled all the
    /// way to C# (Roslyn-semantic-checked, not just parsed) and its copyable
    /// fragment lines are linted for the array-vs-collection trap (#712). See
    /// <see cref="ExemplarCompileChecker"/>.
    /// </summary>
    public DocFile? ExemplarDoc { get; init; }
    public DocFile? AgentTaskReferenceDoc { get; init; }

    /// <summary>
    /// Website pages (<c>website/content/**/*.mdx</c>, historical records excluded) whose
    /// examples, negative-example annotations and quoted output are checked by
    /// <see cref="WebsiteExampleChecker"/> (#1143).
    /// </summary>
    public IReadOnlyList<DocFile> WebsiteDocs { get; init; } = [];
}

/// <summary>
/// A doc that is a generated derivative of a single source. <see cref="Expected"/>
/// is the source run through its transform; <see cref="Actual"/> is what is on
/// disk (or null if the mirror file is missing).
/// </summary>
public sealed record MirrorDoc(string MirrorPath, string SourcePath, string? Actual, string Expected);

/// <summary>
/// Machine-checks agent-facing documentation against the implementation
/// (Phase 1 item 6: spec single-sourcing + drift detection). Every finding is
/// a <see cref="Diagnostic"/> in the Calor1320-1329 band.
/// </summary>
public static class DocDriftChecker
{
    // §KEYWORD or §/KEYWORD references in docs. Keywords are case-sensitive
    // (e.g. §Pf); a reference is the longest run of identifier characters
    // after '§' (attributes like {id:...} start at '{' and are excluded).
    private static readonly Regex KeywordRef = new(@"§(/?[A-Za-z][A-Za-z0-9_]*)", RegexOptions.Compiled);

    // A diagnostic-band citation like "Calor0001–0099", "Calor1300-Calor1399",
    // or "`Calor1300`–`Calor1399`". Range endpoints need not exist as concrete
    // codes, but the band must contain at least one implemented code.
    private static readonly Regex CodeRange = new(
        @"Calor(?<lo>\d{4})`?\s*[–—-]\s*`?(?:Calor)?(?<hi>\d{4})", RegexOptions.Compiled);

    private static readonly Regex CodeRef = new(@"Calor\d{4}", RegexOptions.Compiled);

    // A markdown table row whose first cell is a backticked effect code,
    // e.g. "| `fs:rw` | Filesystem read/write | ... |".
    private static readonly Regex EffectTableRow = new(
        @"^\|\s*`(?<code>[a-z][a-z0-9]*(?::[a-z0-9]+)*)`\s*\|", RegexOptions.Compiled);

    // A markdown table row whose first cell is a backticked diagnostic code.
    private static readonly Regex CliCodeTableRow = new(
        @"^\|\s*`(?<code>Calor\d{4})`\s*\|", RegexOptions.Compiled);

    /// <summary>
    /// Inline suppression marker (meta-notation escape). A line containing
    /// this marker suppresses all drift findings on the <em>next</em> line —
    /// use it for intentional placeholders like <c>§/X</c> or hypothetical
    /// diagnostic codes. See docs/cli/self-check.md.
    /// </summary>
    public const string SuppressionMarker = "<!-- drift:ignore -->";

    /// <summary>
    /// Runs every drift check and returns the findings (empty list = no drift).
    /// </summary>
    public static List<Diagnostic> Check(DocDriftInputs inputs)
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var doc in inputs.KeywordDocs)
        {
            CheckKeywords(doc, inputs.LexerKeywords, diagnostics);
        }

        foreach (var doc in inputs.DiagnosticCodeDocs)
        {
            CheckDiagnosticCodes(doc, inputs.DiagnosticCodes, diagnostics);
        }

        foreach (var doc in inputs.ParseExampleDocs)
        {
            CheckCalorExamples(doc, diagnostics);
        }

        if (inputs.EffectsReferenceDoc is { } effectsDoc)
        {
            CheckEffectCodes(effectsDoc, inputs, requireComplete: true, diagnostics);
        }

        foreach (var doc in inputs.EffectDocsForwardOnly)
        {
            CheckEffectCodes(doc, inputs, requireComplete: false, diagnostics);
        }

        if (inputs.CliCodesDoc is { } cliDoc)
        {
            CheckCliCodeTable(cliDoc, inputs.DiagnosticCodes, diagnostics);
        }

        foreach (var doc in inputs.VersionScanDocs)
        {
            CheckHardcodedVersion(doc, inputs.Version, diagnostics);
        }

        foreach (var doc in inputs.SemanticsVersionDocs)
        {
            CheckSemanticsVersion(doc, inputs.SemanticsVersion, diagnostics);
        }

        foreach (var mirror in inputs.MirrorDocs)
        {
            CheckMirror(mirror, diagnostics);
        }

        if (inputs.ExemplarDoc is { } exemplar)
        {
            diagnostics.AddRange(ExemplarCompileChecker.Check(exemplar));
        }
        if (inputs.AgentTaskReferenceDoc is { } agentReference)
        {
            diagnostics.AddRange(ExemplarCompileChecker.CheckAgentTaskReference(agentReference));
        }

        diagnostics.AddRange(WebsiteExampleChecker.Check(inputs.WebsiteDocs));

        return diagnostics;
    }

    /// <summary>
    /// Builds checker inputs from a repository root, reporting missing files as
    /// <see cref="DiagnosticCode.DocDriftMissingInput"/> findings.
    /// </summary>
    public static DocDriftInputs LoadFromRepository(string root, List<Diagnostic> loadErrors)
    {
        var version = ReadVersion(Path.Combine(root, "Directory.Build.props"), loadErrors);

        var claudeMd = LoadDoc(root, "CLAUDE.md", loadErrors);
        // The repository's own GitHub Copilot instructions (issue #710): a
        // hand-maintained agent-facing dev guide that byte-copied CLAUDE.md and
        // silently rotted (it taught closer-form syntax that now hard-errors).
        // Folding it into the scan set is the guardrail that keeps it honest —
        // "byte-copy outside drift checks" is the measured rot failure mode.
        var copilotInstructions = LoadDoc(root, Path.Combine(".github", "copilot-instructions.md"), loadErrors);
        var syntaxIndex = LoadDoc(root, Path.Combine("docs", "syntax-reference", "index.md"), loadErrors);
        var effectsDoc = LoadDoc(root, Path.Combine("docs", "syntax-reference", "effects.md"), loadErrors);
        var cliCodesDoc = LoadDoc(root, Path.Combine("docs", "cli", "structured-output.md"), loadErrors);

        var syntaxDocs = LoadDocsInDirectory(root, Path.Combine("docs", "syntax-reference"), loadErrors);
        var cliDocs = LoadDocsInDirectory(root, Path.Combine("docs", "cli"), loadErrors);
        var semanticsDocs = LoadDocsInDirectory(root, Path.Combine("docs", "semantics"), loadErrors)
            .Where(doc => !Regex.IsMatch(Path.GetFileName(doc.Path), @"^\d{4}-\d{2}-\d{2}"))
            .ToList();
        if (!semanticsDocs.Any(doc => Path.GetFileName(doc.Path) == "README.md"))
            loadErrors.Add(Drift(DiagnosticCode.DocDriftMissingInput,
                "Required normative semantics overview is missing",
                Path.Combine("docs", "semantics", "README.md"), 1, 1));
        // Exemplar sheets are load-bearing agent infrastructure (E1a: agents
        // copy their lines verbatim) and get full drift treatment.
        var exemplarDoc = LoadDoc(root, Path.Combine("src", "Calor.Compiler", "Resources", "agent-syntax-exemplar.md"), loadErrors);
        var agentReferenceDoc = LoadDoc(root, ExemplarCompileChecker.AgentTaskReferencePath, loadErrors);

        // The scanned set for the keyword and diagnostic-code checks:
        // CLAUDE.md + .github/copilot-instructions.md + every docs/syntax-reference/*.md
        // + every docs/cli/*.md + the exemplar sheet.
        var scannedDocs = NonNull(claudeMd)
            .Concat(NonNull(copilotInstructions))
            .Concat(syntaxDocs)
            .Concat(cliDocs)
            .Concat(semanticsDocs)
            .Concat(NonNull(exemplarDoc))
            .ToList();

        // Version scan covers all agent-facing docs. Dated planning/experiment
        // records legitimately cite historical versions and are excluded.
        string[] versionScanExclusions = ["plans", "experiments", "design", "process"];
        var versionDocs = LoadDocsInDirectory(root, "docs", loadErrors, recursive: true)
            .Where(d => !versionScanExclusions.Any(x =>
                d.Path.StartsWith(Path.Combine("docs", x) + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .ToList();

        // AGENTS.md is a generated mirror of CLAUDE.md (title-swapped) so the two
        // agent manuals cannot drift (#708). It is NOT in the keyword/diagnostic
        // scan sets — that would double-report every finding already raised on
        // CLAUDE.md; the mirror check covers it instead.
        // The public website (#1143). Historical records keep the syntax, versions and
        // codes that shipped; WebsiteExampleChecker.HistoricalExclusions names each one.
        var websiteDocs = LoadDocsInDirectory(root, WebsiteExampleChecker.ContentRelativePath, loadErrors,
                recursive: true, pattern: "*.mdx")
            .Where(doc => !WebsiteExampleChecker.IsHistorical(doc.Path))
            .ToList();

        var mirrorDocs = new List<MirrorDoc>();
        var inventory = LoadAstInventoryMirror(root, loadErrors);
        if (inventory != null)
            mirrorDocs.Add(inventory);
        if (claudeMd is { } claude)
        {
            if (TryAgentsMdFromClaudeMd(claude.Content, out var expected))
            {
                var agentsPath = Path.Combine(root, MirrorAgentsRelativePath);
                var actual = File.Exists(agentsPath) ? File.ReadAllText(agentsPath) : null;
                mirrorDocs.Add(new MirrorDoc(MirrorAgentsRelativePath, "CLAUDE.md", actual, expected));
            }
            else
            {
                // The transform anchors on CLAUDE.md's H1; if that changed, the
                // mirror can't be generated. Surface it loudly (never a silent guess).
                loadErrors.Add(Drift(
                    DiagnosticCode.DocDriftMirrorOutOfSync,
                    $"CLAUDE.md H1 no longer matches the expected anchor '{ClaudeTitleAnchor}' — " +
                    "the AGENTS.md mirror transform cannot run. Restore the H1 or update the anchor " +
                    "in DocDriftChecker.",
                    "CLAUDE.md", 1, 1));
            }
        }

        return new DocDriftInputs
        {
            Version = version,
            LexerKeywords = Parsing.Lexer.KeywordNames,
            DiagnosticCodes = GetImplementedDiagnosticCodes(),
            KnownEffectCodes = Effects.EffectCodes.KnownCompactCodes,
            DocumentedEffectCodes = Effects.EffectCodes.DocumentedCompactCodes,
            KeywordDocs = scannedDocs.Concat(websiteDocs).ToList(),
            DiagnosticCodeDocs = scannedDocs.Concat(websiteDocs).ToList(),
            // The exemplar is excluded from the parse-only example check (Calor1328)
            // because ExemplarCompileChecker gives it a strictly stronger compile
            // check (Calor1330) whose stage 1 already reports any parse failure —
            // leaving it here would double-report the same defect under two codes.
            ParseExampleDocs = scannedDocs
                .Where(d => d.Path != ExemplarCompileChecker.RelativePath).ToList(),
            EffectsReferenceDoc = effectsDoc,
            // Website pages with an "Effect Codes" table are checked forward-only (every listed code exists).
            EffectDocsForwardOnly = NonNull(syntaxIndex)
                .Concat(websiteDocs.Where(doc => Regex.IsMatch(doc.Content, @"^#{1,6}\s+Effect Codes\s*$", RegexOptions.Multiline)))
                .ToList(),
            CliCodesDoc = cliCodesDoc,
            VersionScanDocs = NonNull(claudeMd).Concat(NonNull(copilotInstructions)).Concat(versionDocs)
                .Concat(websiteDocs).ToList(),
            WebsiteDocs = websiteDocs,
            SemanticsVersionDocs = semanticsDocs,
            MirrorDocs = mirrorDocs,
            ExemplarDoc = exemplarDoc,
            AgentTaskReferenceDoc = agentReferenceDoc,
        };
    }

    /// <summary>Repo-relative path of the generated agent-manual mirror.</summary>
    public const string MirrorAgentsRelativePath = "AGENTS.md";

    public static string AstInventoryRelativePath => Path.Combine("docs", "semantics", "inventory.md");

    public static string GenerateAstInventory(string schemaJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        if (schema.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported AST schema version");
        var nodes = schema.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(node => (Name: node.GetProperty("name").GetString()!, Source: node.GetProperty("source").GetString()!))
            .OrderBy(node => node.Name, StringComparer.Ordinal).ToArray();
        if (nodes.Length == 0 || nodes.Select(node => node.Name).Distinct(StringComparer.Ordinal).Count() != nodes.Length
            || nodes.Any(node => node.Name == null || !Regex.IsMatch(node.Name, @"^[A-Za-z_][A-Za-z0-9_]*$")
                || node.Source == null || !Regex.IsMatch(node.Source, @"^[A-Za-z0-9_.-]+\.cs$")))
            throw new InvalidDataException("AST schema must contain unique named nodes and C# source filenames");
        var text = new StringBuilder();
        text.Append("# Calor AST Construct Inventory\n\n");
        text.Append("<!-- Generated from eng/ast-schema.json by calor self-check docs --fix. Do not edit by hand. -->\n\n");
        text.Append($"The AST schema contains **{nodes.Length}** node types. This is a structural inventory, not a claim that every node supports every compiler stage.\n\n");
        text.Append("Regenerate with `calor self-check docs --fix`. The drift gate compares this entire page with the schema-derived output.\n\n");
        text.Append("| Node type | Source file |\n|---|---|\n");
        foreach (var node in nodes)
            text.Append($"| `{node.Name}` | [`{node.Source}`](../../src/Calor.Compiler/Ast/{node.Source}) |\n");
        return text.ToString();
    }

    private static MirrorDoc? LoadAstInventoryMirror(string root, List<Diagnostic> diagnostics)
    {
        var schemaPath = Path.Combine("eng", "ast-schema.json");
        var schema = LoadDoc(root, schemaPath, diagnostics);
        if (schema == null)
            return null;
        try
        {
            var expected = GenerateAstInventory(schema.Content);
            var path = Path.Combine(root, AstInventoryRelativePath);
            return new MirrorDoc(AstInventoryRelativePath, schemaPath,
                File.Exists(path) ? File.ReadAllText(path) : null, expected);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or FormatException
            or InvalidOperationException or KeyNotFoundException)
        {
            diagnostics.Add(Drift(DiagnosticCode.DocDriftMissingInput,
                $"Cannot generate AST inventory: {exception.Message}", schemaPath, 1, 1));
            return null;
        }
    }

    public static bool RegenerateAstInventory(string root, List<Diagnostic> diagnostics)
    {
        var mirror = LoadAstInventoryMirror(root, diagnostics);
        if (mirror == null)
            return false;
        var path = Path.Combine(root, mirror.MirrorPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (mirror.Actual?.Replace("\r\n", "\n") != mirror.Expected)
            File.WriteAllText(path, mirror.Expected);
        return true;
    }

    /// <summary>The CLAUDE.md H1 the AGENTS.md transform anchors on.</summary>
    public const string ClaudeTitleAnchor = "# CLAUDE.md — Calor Compiler";

    private const string AgentsHead =
        "# AGENTS.md — Calor Compiler\n" +
        "<!-- Generated from CLAUDE.md by `calor self-check docs --fix`. Edit CLAUDE.md, not this file. -->";

    /// <summary>
    /// The single-source transform: AGENTS.md is CLAUDE.md with the H1 title
    /// swapped and a generated-file banner, so editing CLAUDE.md and running
    /// <c>calor self-check docs --fix</c> keeps the two agent manuals identical
    /// in content. Deterministic and idempotent. Returns false (no silent guess)
    /// when CLAUDE.md's H1 does not match <see cref="ClaudeTitleAnchor"/>.
    /// </summary>
    public static bool TryAgentsMdFromClaudeMd(string claudeContent, out string result)
    {
        var normalized = claudeContent.Replace("\r\n", "\n");
        if (!normalized.StartsWith(ClaudeTitleAnchor, StringComparison.Ordinal))
        {
            result = "";
            return false;
        }
        result = AgentsHead + normalized[ClaudeTitleAnchor.Length..];
        return true;
    }

    /// <summary>
    /// Throwing convenience wrapper over <see cref="TryAgentsMdFromClaudeMd"/>.
    /// </summary>
    public static string AgentsMdFromClaudeMd(string claudeContent) =>
        TryAgentsMdFromClaudeMd(claudeContent, out var result)
            ? result
            : throw new InvalidOperationException(
                $"CLAUDE.md H1 does not match the expected anchor '{ClaudeTitleAnchor}'.");

    /// <summary>Outcome of regenerating the AGENTS.md mirror from CLAUDE.md.</summary>
    public enum MirrorRegenResult { AlreadyInSync, Written, SourceMissing, AnchorMismatch }

    /// <summary>
    /// Regenerates AGENTS.md from CLAUDE.md under <paramref name="root"/>. Idempotent:
    /// writes only when the on-disk mirror differs from the transform. Does not write
    /// on <see cref="MirrorRegenResult.SourceMissing"/> or <see cref="MirrorRegenResult.AnchorMismatch"/>.
    /// </summary>
    public static MirrorRegenResult RegenerateAgentsMd(string root)
    {
        var claudePath = Path.Combine(root, "CLAUDE.md");
        if (!File.Exists(claudePath))
        {
            return MirrorRegenResult.SourceMissing;
        }
        if (!TryAgentsMdFromClaudeMd(File.ReadAllText(claudePath), out var expected))
        {
            return MirrorRegenResult.AnchorMismatch;
        }
        var agentsPath = Path.Combine(root, MirrorAgentsRelativePath);
        var current = File.Exists(agentsPath) ? File.ReadAllText(agentsPath).Replace("\r\n", "\n") : null;
        if (current == expected)
        {
            return MirrorRegenResult.AlreadyInSync;
        }
        File.WriteAllText(agentsPath, expected);
        return MirrorRegenResult.Written;
    }

    private static void CheckMirror(MirrorDoc mirror, List<Diagnostic> diagnostics)
    {
        var normalizedActual = mirror.Actual?.Replace("\r\n", "\n");
        if (normalizedActual == mirror.Expected)
        {
            return;
        }
        var reason = mirror.Actual == null ? "is missing" : "is out of sync with";
        diagnostics.Add(Drift(
            DiagnosticCode.DocDriftMirrorOutOfSync,
            $"{mirror.MirrorPath} {reason} its single source {mirror.SourcePath} — " +
            $"regenerate it with `calor self-check docs --fix` (do not hand-edit; edit {mirror.SourcePath}).",
            mirror.MirrorPath, 1, 1));
    }

    /// <summary>
    /// Every diagnostic code declared as a constant on <see cref="DiagnosticCode"/>.
    /// </summary>
    public static IReadOnlyCollection<string> GetImplementedDiagnosticCodes()
    {
        return typeof(DiagnosticCode)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => CodeRef.IsMatch(v))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void CheckKeywords(
        DocFile doc, IReadOnlyCollection<string> keywords, List<Diagnostic> diagnostics)
    {
        var keywordSet = keywords as ISet<string> ?? new HashSet<string>(keywords, StringComparer.Ordinal);
        foreach (var line in ClassifyLines(doc))
        {
            if (line.InForeignFence || line.Suppressed)
            {
                continue;
            }

            foreach (Match match in KeywordRef.Matches(line.Text))
            {
                var name = match.Groups[1].Value;
                // `§/X` is the website's standard placeholder for "any closer" (docs/ suppress it per line).
                if (!keywordSet.Contains(name) && !(IsMdx(doc) && name == "/X"))
                {
                    diagnostics.Add(Drift(
                        DiagnosticCode.DocDriftUnknownKeyword,
                        $"Documented keyword '§{name}' does not exist in the lexer's keyword table " +
                        $"(if this is intentional meta-notation, put '{MarkerFor(doc)}' on the preceding line)",
                        doc.Path, line.Number, match.Index + 1));
                }
            }
        }
    }

    private static void CheckDiagnosticCodes(
        DocFile doc, IReadOnlyCollection<string> codes, List<Diagnostic> diagnostics)
    {
        var codeSet = codes as ISet<string> ?? new HashSet<string>(codes, StringComparer.Ordinal);
        var codeNumbers = codes
            .Select(c => int.Parse(c["Calor".Length..]))
            .ToArray();

        foreach (var line in ClassifyLines(doc))
        {
            if (line.InForeignFence || line.Suppressed)
            {
                continue;
            }

            // Band citations first: "Calor0800–0899", "Calor1300-Calor1399".
            var rangeSpans = new List<(int Start, int End)>();
            foreach (Match range in CodeRange.Matches(line.Text))
            {
                rangeSpans.Add((range.Index, range.Index + range.Length));
                var lo = int.Parse(range.Groups["lo"].Value);
                var hi = int.Parse(range.Groups["hi"].Value);
                if (!codeNumbers.Any(n => n >= lo && n <= hi))
                {
                    diagnostics.Add(Drift(
                        DiagnosticCode.DocDriftEmptyDiagnosticRange,
                        $"Documented diagnostic band Calor{lo:D4}-Calor{hi:D4} contains no implemented diagnostic codes",
                        doc.Path, line.Number, range.Index + 1));
                }
            }

            // Standalone citations (not part of a band citation) must exist exactly.
            foreach (Match match in CodeRef.Matches(line.Text))
            {
                if (rangeSpans.Any(s => match.Index >= s.Start && match.Index < s.End))
                {
                    continue;
                }

                if (!codeSet.Contains(match.Value))
                {
                    diagnostics.Add(Drift(
                        DiagnosticCode.DocDriftUnknownDiagnosticCode,
                        $"Documented diagnostic code '{match.Value}' is not defined in DiagnosticCode " +
                        $"(if this is intentional meta-notation, put '{MarkerFor(doc)}' on the preceding line)",
                        doc.Path, line.Number, match.Index + 1));
                }
            }
        }
    }

    /// <summary>
    /// Parse-checks fenced <c>```calor</c> examples with the real lexer and
    /// parser. A block whose first non-blank line starts with §M declares a
    /// complete program and must parse cleanly; any other block is a fragment
    /// and is skipped. A <see cref="SuppressionMarker"/> on the line before
    /// the opening fence exempts the whole block.
    /// </summary>
    private static void CheckCalorExamples(DocFile doc, List<Diagnostic> diagnostics)
    {
        foreach (var block in FindCalorFences(doc))
        {
            var firstContent = block.Lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (block.Suppressed ||
                firstContent == null ||
                !Regex.IsMatch(firstContent.TrimStart(), @"^§M(?:\s|\{|$)"))
            {
                continue;
            }

            var source = string.Join("\n", block.Lines) + "\n";
            var bag = new DiagnosticBag();
            try
            {
                var lexer = new Parsing.Lexer(source, bag);
                var tokens = lexer.TokenizeAllForParser();
                if (!bag.HasErrors)
                {
                    _ = new Parsing.Parser(tokens, bag).Parse();
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(Drift(
                    DiagnosticCode.DocDriftExampleParseError,
                    $"Fenced calor example crashed the parser: {ex.Message}",
                    doc.Path, block.FirstContentLine, 1));
                continue;
            }

            foreach (var error in bag.Errors)
            {
                diagnostics.Add(Drift(
                    DiagnosticCode.DocDriftExampleParseError,
                    $"Fenced calor example no longer parses: {error.Code}: {error.Message}",
                    doc.Path,
                    block.FirstContentLine + Math.Max(error.Span.Line - 1, 0),
                    Math.Max(error.Span.Column, 1)));
            }
        }
    }

    /// <summary>A fenced ```calor block: its content lines (CR stripped), the
    /// document line number of the first content line, and whether the line
    /// before the opening fence carried the suppression marker.</summary>
    private sealed record CalorFence(List<string> Lines, int FirstContentLine, bool Suppressed);

    private static List<CalorFence> FindCalorFences(DocFile doc)
    {
        var fences = new List<CalorFence>();
        var lines = doc.Content.Split('\n');
        string? fenceInfo = null;
        CalorFence? current = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (fenceInfo == null)
                {
                    fenceInfo = trimmed[3..].Trim();
                    if (fenceInfo == "calor")
                    {
                        var suppressed = i > 0 &&
                            WebsiteExampleChecker.IsSuppressionLine(lines[i - 1]);
                        current = new CalorFence([], i + 2, suppressed);
                    }
                }
                else
                {
                    if (current != null)
                    {
                        fences.Add(current);
                        current = null;
                    }

                    fenceInfo = null;
                }

                continue;
            }

            current?.Lines.Add(lines[i].TrimEnd('\r'));
        }

        return fences;
    }

    private static void CheckEffectCodes(
        DocFile doc, DocDriftInputs inputs, bool requireComplete, List<Diagnostic> diagnostics)
    {
        var known = inputs.KnownEffectCodes as ISet<string>
            ?? new HashSet<string>(inputs.KnownEffectCodes, StringComparer.Ordinal);

        var (rows, sectionLine) = FindEffectTableRows(doc);
        if (sectionLine == 0)
        {
            diagnostics.Add(Drift(
                DiagnosticCode.DocDriftMissingInput,
                "No 'Effect Codes' section with a code table was found",
                doc.Path, 1, 1));
            return;
        }

        var documented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (code, lineNumber, column, suppressed) in rows)
        {
            documented.Add(code);
            if (!known.Contains(code) && !suppressed)
            {
                diagnostics.Add(Drift(
                    DiagnosticCode.DocDriftUnknownEffectCode,
                    $"Documented effect code '{code}' is unknown to the compiler's effect-code registry",
                    doc.Path, lineNumber, column));
            }
        }

        if (requireComplete)
        {
            foreach (var code in inputs.DocumentedEffectCodes)
            {
                if (!documented.Contains(code))
                {
                    diagnostics.Add(Drift(
                        DiagnosticCode.DocDriftUndocumentedEffectCode,
                        $"Implemented effect code '{code}' is missing from the Effect Codes table",
                        doc.Path, sectionLine, 1));
                }
            }
        }
    }

    private static void CheckCliCodeTable(
        DocFile doc, IReadOnlyCollection<string> codes, List<Diagnostic> diagnostics)
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var tableLine = 0;
        ForEachLine(doc, (line, lineNumber) =>
        {
            var match = CliCodeTableRow.Match(line);
            if (match.Success)
            {
                listed.Add(match.Groups["code"].Value);
                if (tableLine == 0)
                {
                    tableLine = lineNumber;
                }
            }
        });

        if (tableLine == 0)
        {
            diagnostics.Add(Drift(
                DiagnosticCode.DocDriftMissingInput,
                "No CLI diagnostic-code table (rows starting with a backticked Calor13xx code) was found",
                doc.Path, 1, 1));
            return;
        }

        // Every implemented 1300-band code must be listed. (Codes in the table
        // that do not exist are caught by the standalone-citation check, which
        // also scans this file.)
        foreach (var code in codes.OrderBy(c => c, StringComparer.Ordinal))
        {
            var number = int.Parse(code["Calor".Length..]);
            if (number is >= 1300 and <= 1399 && !listed.Contains(code))
            {
                diagnostics.Add(Drift(
                    DiagnosticCode.DocDriftUndocumentedCliCode,
                    $"CLI diagnostic code '{code}' is not listed in the CLI diagnostic-code table",
                    doc.Path, tableLine, 1));
            }
        }
    }

    private static void CheckSemanticsVersion(DocFile doc, string expected, List<Diagnostic> diagnostics)
    {
        var claim = new Regex(@"^\s*(?:#{1,6}\s+)?(?:(?:Semantics|Current)\s+)?Version\s*:\s*(?<version>\S*)",
            RegexOptions.IgnoreCase);
        var foundClaim = false;
        foreach (var line in ClassifyLines(doc))
        {
            if (line.InForeignFence)
                continue;
            var normalized = line.Text;
            string previous;
            do
            {
                previous = normalized;
                normalized = Regex.Replace(normalized, @"(\*\*|__|\*|_|`)(.*?)\1", "$2");
            } while (normalized != previous);
            var match = claim.Match(normalized);
            foundClaim |= match.Success;
            if (match.Success && match.Groups["version"].Value != expected)
                diagnostics.Add(Drift(DiagnosticCode.DocDriftHardcodedVersion,
                    $"Normative semantics version '{match.Groups["version"].Value}' differs from implemented semantics '{expected}' (not the compiler package version)",
                    doc.Path, line.Number, match.Groups["version"].Index + 1));
        }
        if (!foundClaim && doc.Path.Replace('\\', '/') == "docs/semantics/README.md")
            diagnostics.Add(Drift(DiagnosticCode.DocDriftHardcodedVersion,
                $"Normative overview must declare Semantics Version: {expected}", doc.Path, 1, 1));
    }

    private static void CheckHardcodedVersion(
        DocFile doc, string version, List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        // The version scan deliberately looks inside all fenced blocks (a
        // hardcoded version in an install snippet is exactly the drift it
        // exists to catch); only the suppression marker exempts a line.
        var pattern = new Regex($@"(?<![0-9.]){Regex.Escape(version)}(?![0-9.])");
        foreach (var line in ClassifyLines(doc))
        {
            if (line.Suppressed)
            {
                continue;
            }

            foreach (Match match in pattern.Matches(line.Text))
            {
                diagnostics.Add(Drift(
                    DiagnosticCode.DocDriftHardcodedVersion,
                    $"Doc hardcodes the current compiler version '{version}'; version is single-sourced in Directory.Build.props",
                    doc.Path, line.Number, match.Index + 1));
            }
        }
    }

    private static (List<(string Code, int Line, int Column, bool Suppressed)> Rows, int SectionLine) FindEffectTableRows(DocFile doc)
    {
        var rows = new List<(string, int, int, bool)>();
        var sectionLine = 0;
        var inSection = false;

        var lines = doc.Content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Regex.IsMatch(line, @"^#{1,6}\s+Effect Codes\s*$"))
            {
                inSection = true;
                sectionLine = i + 1;
                continue;
            }

            if (inSection && Regex.IsMatch(line, @"^#{1,6}\s"))
            {
                inSection = false;
                continue;
            }

            if (inSection)
            {
                var match = EffectTableRow.Match(line);
                if (match.Success)
                {
                    var suppressed = i > 0 && WebsiteExampleChecker.IsSuppressionLine(lines[i - 1]);
                    rows.Add((match.Groups["code"].Value, i + 1, match.Groups["code"].Index + 1, suppressed));
                }
            }
        }

        return (rows, sectionLine);
    }

    private static void ForEachLine(DocFile doc, Action<string, int> action)
    {
        var lines = doc.Content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            action(lines[i], i + 1);
        }
    }

    /// <summary>
    /// One markdown line with its scan classification: whether keyword /
    /// diagnostic-code scanning applies (false inside fenced code blocks whose
    /// info string is something other than <c>calor</c>, e.g. ```csharp or
    /// ```text — bare ``` fences and ```calor fences are scanned), and whether
    /// the preceding line carried the <see cref="SuppressionMarker"/>.
    /// </summary>
    private sealed record ScannedLine(string Text, int Number, bool InForeignFence, bool Suppressed);

    private static List<ScannedLine> ClassifyLines(DocFile doc)
    {
        var lines = doc.Content.Split('\n');
        var result = new List<ScannedLine>(lines.Length);
        string? fenceInfo = null; // non-null while inside a fenced code block
        var previousHadMarker = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimEnd('\r').TrimStart();
            var isFenceDelimiter = trimmed.StartsWith("```", StringComparison.Ordinal);
            var inForeignFence = fenceInfo is not (null or "" or "calor");

            if (isFenceDelimiter)
            {
                fenceInfo = fenceInfo == null
                    ? trimmed[3..].Trim()   // opening fence: capture the info string
                    : null;                 // closing fence
                // The opening delimiter line itself is skipped for foreign
                // fences (its info string is not Calor syntax either way).
                inForeignFence = inForeignFence || fenceInfo is not (null or "" or "calor");
            }

            result.Add(new ScannedLine(line, i + 1, inForeignFence, previousHadMarker));
            previousHadMarker = WebsiteExampleChecker.IsSuppressionLine(line);
        }

        return result;
    }

    private static bool IsMdx(DocFile doc) => doc.Path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase);

    // MDX rejects HTML comments, so website pages use the JSX-comment form of the marker.
    private static string MarkerFor(DocFile doc) =>
        IsMdx(doc) ? WebsiteExampleChecker.MdxSuppressionMarker : SuppressionMarker;

    private static Diagnostic Drift(string code, string message, string path, int line, int column)
        => new(code, DiagnosticSeverity.Error, message, path, line, column);

    private static string ReadVersion(string propsPath, List<Diagnostic> loadErrors)
    {
        if (!File.Exists(propsPath))
        {
            loadErrors.Add(Drift(
                DiagnosticCode.DocDriftMissingInput,
                $"Directory.Build.props not found at '{propsPath}'",
                propsPath, 1, 1));
            return "";
        }

        var match = Regex.Match(File.ReadAllText(propsPath), @"<Version>\s*([^<\s]+)\s*</Version>");
        if (!match.Success)
        {
            loadErrors.Add(Drift(
                DiagnosticCode.DocDriftMissingInput,
                $"No <Version> element found in '{propsPath}'",
                propsPath, 1, 1));
            return "";
        }

        return match.Groups[1].Value;
    }

    private static DocFile? LoadDoc(string root, string relativePath, List<Diagnostic> loadErrors)
    {
        var fullPath = Path.Combine(root, relativePath);
        if (!File.Exists(fullPath))
        {
            loadErrors.Add(Drift(
                DiagnosticCode.DocDriftMissingInput,
                $"Expected doc file not found: '{relativePath}'",
                relativePath, 1, 1));
            return null;
        }

        return new DocFile(relativePath, File.ReadAllText(fullPath));
    }

    private static List<DocFile> LoadDocsInDirectory(
        string root, string relativeDir, List<Diagnostic> loadErrors, bool recursive = false, string pattern = "*.md")
    {
        var fullDir = Path.Combine(root, relativeDir);
        if (!Directory.Exists(fullDir))
        {
            loadErrors.Add(Drift(
                DiagnosticCode.DocDriftMissingInput,
                $"Expected docs directory not found: '{relativeDir}'",
                relativeDir, 1, 1));
            return [];
        }

        return Directory
            .EnumerateFiles(fullDir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new DocFile(Path.GetRelativePath(root, p), File.ReadAllText(p)))
            .ToList();
    }

    private static List<DocFile> NonNull(params DocFile?[] docs)
        => docs.Where(d => d != null).Select(d => d!).ToList();
}

/// <summary>
/// Checks the public website's MDX pages (<c>website/content/**/*.mdx</c>) against the
/// compiler's actual behaviour (#1143). Fence convention:
/// <list type="bullet">
/// <item>A <c>```calor</c> fence whose first non-blank line starts with <c>§M</c> is a
/// complete program. It is compiled exactly as <c>calor --input file.calr</c> compiles it
/// (CLI defaults, generated C# validated by Roslyn) and must report no errors.</item>
/// <item><c>```calor expect=Calor0272</c> marks an intended negative example. Its error and
/// warning codes must equal the listed set, and the adjacent prose (from the nearest heading
/// to the next fence or heading) must cite every listed code. A prose claim of the form
/// "line N, column M" before the fence must match a reported location.</item>
/// <item><c>group=name</c> on several complete programs of one page compiles them together,
/// as <c>calor --input a.calr --input b.calr</c> does (cross-module effect checks).</item>
/// <item>A fence labelled <c>output</c> quotes the diagnostics of the nearest preceding
/// complete program (or its group); the quoted <c>CalorNNNN: message</c> entries must equal
/// the actual error and warning diagnostics. A fence labelled <c>illustrative</c> is
/// explicitly not real output. A fence that looks like tool output but carries neither
/// label is a finding.</item>
/// </list>
/// Pages listed in <see cref="HistoricalExclusions"/> are records of released versions
/// and keep the syntax and codes that shipped.
/// </summary>
public static class WebsiteExampleChecker
{
    /// <summary>Repository-relative root of the website's MDX content.</summary>
    public static readonly string ContentRelativePath = Path.Combine("website", "content");

    /// <summary>
    /// Website pages excluded from every website check, with the reason. Each one is a
    /// historical record whose old syntax, versions and codes are correct for its time.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> HistoricalExclusions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["changelog.mdx"] = "release history: keeps the syntax, versions and diagnostic codes that shipped",
        };

    /// <summary>MDX cannot hold HTML comments, so the drift suppression marker takes JSX-comment form.</summary>
    public const string MdxSuppressionMarker = "{/* drift:ignore */}";

    private static readonly Regex CompleteProgramStart = new(@"^§M(?:\s|\{|$)", RegexOptions.Compiled);
    private static readonly Regex CodePattern = new(@"^Calor\d{4}$", RegexOptions.Compiled);
    private static readonly Regex GroupPattern = new(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^#{1,6}\s", RegexOptions.Compiled);
    private static readonly Regex LocationClaim = new(@"\bline (?<line>\d+), column (?<column>\d+)\b", RegexOptions.Compiled);

    // One quoted diagnostic in an output fence: an optional "path(l,c): " prefix, an optional
    // severity word, the code, and the message (continuation lines are appended).
    private static readonly Regex QuotedDiagnostic = new(
        @"^(?:\S+\((?<line>\d+),(?<column>\d+)\):\s*)?(?:(?<severity>error|warning)\s+)?(?<code>Calor\d{4}):\s*(?<message>.*)$",
        RegexOptions.Compiled);

    // Lines that make a fence read as real tool output.
    private static readonly Regex OutputShaped = new(
        @"Calor\d{4}:|^\s*(?:error|warning)\b|\.calr[:(]\d+[:,]\d+|^\s*===.*===\s*$|^\s*BLOCKED:",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> OutputLanguages = new(StringComparer.Ordinal) { "", "text", "console", "plaintext" };

    // A JSON fence whose top-level keys are those of a CLI envelope or MCP tool response.
    private static readonly Regex OutputShapedJson = new(
        @"""(?:success|diagnostics|schemaVersion|suggestions|obligations|guards|patches|isError|decision)""\s*:",
        RegexOptions.Compiled);

    // A fence opener: optional indentation, then three or more backticks or tildes.
    private static readonly Regex FenceOpen = new(
        @"^(?<ws>[ \t]*)(?<fence>`{3,}|~{3,})(?<info>[^`]*)$", RegexOptions.Compiled);

    // A fence opened on a list-item or blockquote line. CommonMark ends such a fence when its
    // container ends (possibly implicitly), which a line scanner cannot follow, so the website
    // convention forbids it and the check rejects it rather than guessing.
    private static readonly Regex FenceInContainer = new(
        @"^[ \t]*(?:>|[-*+][ \t]|\d{1,9}[.)][ \t])[ \t>]*(?:[-*+][ \t]|\d{1,9}[.)][ \t])?[ \t>]*(?:`{3,}|~{3,})", RegexOptions.Compiled);

    /// <summary>A fenced block of an MDX page.</summary>
    public sealed record Fence(
        string Language,
        IReadOnlyList<string> Tokens,
        IReadOnlyList<string> Lines,
        int OpenLine,
        int CloseLine)
    {
        public int FirstContentLine => OpenLine + 1;
        public string Source => string.Join("\n", Lines) + "\n";

        public bool IsCompleteProgram => Language == "calor"
            && Lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) is { } first
            && CompleteProgramStart.IsMatch(first.TrimStart());

        public string? Value(string key) => Tokens
            .Where(t => t.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(t => t[(key.Length + 1)..])
            .FirstOrDefault();

        public bool Has(string flag) => Tokens.Contains(flag, StringComparer.Ordinal);
    }

    /// <summary>Counts reported by <see cref="Check"/> (used by tests and the evidence record).</summary>
    public sealed class Coverage
    {
        public int Pages { get; set; }
        public int CompletePrograms { get; set; }
        public int NegativePrograms { get; set; }
        public int GroupedPrograms { get; set; }
        public int CheckedOutputs { get; set; }
        public int IllustrativeOutputs { get; set; }
    }

    /// <summary>True when the page is a historical record excluded from website checks.</summary>
    public static bool IsHistorical(string path) =>
        HistoricalExclusions.ContainsKey(Path.GetRelativePath(ContentRelativePath, path).Replace('\\', '/'));

    /// <summary>
    /// Splits an MDX page into its fenced blocks (1-based line numbers): backtick or tilde
    /// fences of three or more characters, optionally indented (e.g. under a list item or in a
    /// JSX element). Content lines lose the opener's indentation. Fence forms whose extent
    /// depends on a container (list-marker or blockquote lines, indented bodies that dedent,
    /// unclosed fences) are reported by the checker instead of being guessed.
    /// </summary>
    public static List<Fence> ParseFences(string content) => ParseFences(content, null);

    private static List<Fence> ParseFences(string content, List<int>? rejectedLines)
    {
        var fences = new List<Fence>();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var open = FenceOpen.Match(lines[i]);
            if (!open.Success)
            {
                if (FenceInContainer.IsMatch(lines[i]))
                    rejectedLines?.Add(i + 1);
                continue;
            }
            var indent = open.Groups["ws"].Length;
            var marker = open.Groups["fence"].Value;
            var info = open.Groups["info"].Value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var body = new List<string>();
            var close = i + 1;
            var dedented = false;
            while (close < lines.Length && !IsCloser(lines[close], marker))
            {
                // An indented fence (inside a list item or JSX element) whose body dedents past
                // the opener may have been closed implicitly by its container.
                if (indent > 0 && lines[close].Trim().Length > 0
                    && lines[close].Length - lines[close].TrimStart().Length < indent)
                    dedented = true;
                body.Add(RemoveIndent(lines[close++], indent));
            }
            if (dedented || close >= lines.Length)
                rejectedLines?.Add(i + 1);
            fences.Add(new Fence(info.Length > 0 ? info[0] : "", info.Skip(1).ToList(), body, i + 1,
                Math.Min(close, lines.Length - 1) + 1));
            i = close;
        }
        return fences;
    }

    private static bool IsCloser(string line, string marker)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= marker.Length && trimmed.All(c => c == marker[0]);
    }

    private static string RemoveIndent(string line, int width)
    {
        var remove = 0;
        while (remove < width && remove < line.Length && (line[remove] == ' ' || line[remove] == '\t'))
            remove++;
        return line[remove..];
    }

    internal static bool IsSuppressionLine(string line) =>
        line.Contains(DocDriftChecker.SuppressionMarker, StringComparison.Ordinal)
        || line.Contains(MdxSuppressionMarker, StringComparison.Ordinal);

    /// <summary>Runs every website check over the given pages.</summary>
    public static List<Diagnostic> Check(IReadOnlyList<DocFile> pages, Coverage? coverage = null)
    {
        var diagnostics = new List<Diagnostic>();
        coverage ??= new Coverage();
        foreach (var page in pages)
        {
            coverage.Pages++;
            CheckPage(page, diagnostics, coverage);
        }
        return diagnostics;
    }

    private sealed record Unit(List<Fence> Members, Dictionary<Fence, List<Diagnostic>> Actual);

    private static void CheckPage(DocFile page, List<Diagnostic> diagnostics, Coverage coverage)
    {
        var lines = page.Content.Replace("\r\n", "\n").Split('\n');
        var rejected = new List<int>();
        var fences = ParseFences(page.Content, rejected);
        var programs = new List<Fence>();
        foreach (var line in rejected)
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                "Unsupported fence form: a fence on a list-item or blockquote line, an indented fence whose body " +
                "dedents past its opener, or an unclosed fence. Use a plain fence (indentation only) so the check " +
                "sees exactly the block the site renders", page.Path, line));

        // The drift:ignore marker exempts prose from the keyword and code scans only; it never
        // exempts an executable example or quoted output from these checks.
        foreach (var fence in fences)
        {
            if (!ValidateAnnotations(page, fence, diagnostics))
                continue;
            if (fence.IsCompleteProgram)
                programs.Add(fence);
        }

        // Compile every unit: an ungrouped program alone, each group's members together.
        // A program may belong to several groups (e.g. one callee module shared by two
        // caller examples); its expectations must then hold in every group.
        var units = programs.ToDictionary(p => p, _ => new List<Unit>());
        foreach (var members in programs
            .SelectMany(f => Groups(f).Select(g => (Group: g, Fence: f)))
            .GroupBy(x => x.Group, StringComparer.Ordinal)
            .Select(g => g.Select(x => x.Fence).ToList()))
        {
            var unit = new Unit(members, Compile(page, members, diagnostics));
            foreach (var member in members)
                units[member].Add(unit);

            // A member that fails to compile can stop generated-C# validation for the whole
            // group, which would hide errors in the others. Compile every member that reported
            // no error again without the failing ones (warning-only negatives stay, so their
            // dependants still resolve); none of them may report an error.
            var clean = members.Where(m => unit.Actual.TryGetValue(m, out var a) && !a.Any(d => d.IsError)).ToList();
            if (clean.Count > 0 && clean.Count < members.Count)
            {
                foreach (var (member, actual) in Compile(page, clean, diagnostics))
                {
                    foreach (var error in actual.Where(d => d.IsError))
                        diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteExampleMismatch,
                            $"Complete calor example does not compile once its group's failing examples are removed (a failing file can stop generated-C# validation for the whole group): {error.Code}: {error.Message}",
                            page.Path, member.FirstContentLine + Math.Max(error.Span.Line - 1, 0)));
                }
            }
        }

        foreach (var program in programs)
        {
            coverage.CompletePrograms++;
            if (program.Value("group") != null)
                coverage.GroupedPrograms++;
            if (ExpectedCodes(program).Count > 0)
                coverage.NegativePrograms++;
            foreach (var unit in units[program])
                CheckProgram(page, lines, fences, program, unit, diagnostics);
        }

        foreach (var fence in fences.Where(f => f.Language != "calor"))
        {
            if (fence.Has("output"))
            {
                coverage.CheckedOutputs++;
                var source = programs.LastOrDefault(p => p.CloseLine < fence.OpenLine);
                if (source != null && units[source].Count > 1)
                    diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                        "`output` fence follows an example that belongs to several groups; it is ambiguous which compile it quotes",
                        page.Path, fence.OpenLine));
                else
                    CheckOutput(page, fence, source == null ? null : units[source].Single(), diagnostics);
            }
            else if (fence.Has("illustrative"))
            {
                coverage.IllustrativeOutputs++;
            }
            else if ((OutputLanguages.Contains(fence.Language) && OutputShaped.IsMatch(string.Join("\n", fence.Lines)))
                || (fence.Language == "json" && OutputShapedJson.IsMatch(string.Join("\n", fence.Lines))))
            {
                diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteOutputMismatch,
                    "Fence looks like tool output but is labelled neither `output` (checked against the preceding " +
                    "example) nor `illustrative` (not real output); add one to the fence info string",
                    page.Path, fence.OpenLine));
            }
        }
    }

    private static IEnumerable<string> Groups(Fence fence) =>
        fence.Value("group") is { } groups
            ? groups.Split(',')
            : [$"\0{fence.OpenLine}"];

    private static void CheckProgram(DocFile page, string[] lines, List<Fence> fences, Fence program, Unit unit,
        List<Diagnostic> diagnostics)
    {
        if (!unit.Actual.TryGetValue(program, out var actual))
            return; // compile crashed; already reported
        var expected = ExpectedCodes(program);
        if (expected.Count == 0)
        {
            foreach (var error in actual.Where(d => d.IsError))
                diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteExampleMismatch,
                    $"Complete calor example does not compile: {error.Code}: {error.Message} " +
                    "(fix the example; an intended negative example declares its codes with `expect=`)",
                    page.Path, program.FirstContentLine + Math.Max(error.Span.Line - 1, 0)));
            return;
        }

        var actualCodes = actual.Select(d => d.Code).ToHashSet(StringComparer.Ordinal);
        if (!actualCodes.SetEquals(expected))
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteExampleMismatch,
                $"Negative calor example declares expect={string.Join(",", expected.Order(StringComparer.Ordinal))} " +
                $"but the compiler reports [{string.Join(", ", actual.Select(d => $"{d.Severity.ToString().ToLowerInvariant()} {d.Code}: {d.Message}"))}]",
                page.Path, program.OpenLine));

        CheckProseClaims(page, lines, fences, program, expected, actual, diagnostics);
    }

    private static bool ValidateAnnotations(DocFile page, Fence fence, List<Diagnostic> diagnostics)
    {
        var allowed = fence.Language == "calor"
            ? new[] { "expect=", "group=" }
            : new[] { "output", "illustrative" };
        var ok = true;
        foreach (var token in fence.Tokens)
        {
            if (!allowed.Any(a => a.EndsWith('=') ? token.StartsWith(a, StringComparison.Ordinal) : token == a))
            {
                diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                    $"Unknown or misplaced fence annotation '{token}' on a ```{fence.Language} fence " +
                    $"(allowed here: {string.Join(", ", allowed.Select(a => a.TrimEnd('=')))})",
                    page.Path, fence.OpenLine));
                ok = false;
            }
        }

        foreach (var key in fence.Tokens.Select(t => t.Contains('=') ? t[..t.IndexOf('=')] : t)
            .GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                $"Fence annotation '{key}' is repeated; list every code in one expect= (or group in one group=)",
                page.Path, fence.OpenLine));
            ok = false;
        }

        if (fence.Has("output") && fence.Has("illustrative"))
        {
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                "A fence cannot be both `output` (checked) and `illustrative`", page.Path, fence.OpenLine));
            ok = false;
        }

        if (fence.Language == "calor" && fence.Tokens.Count > 0 && !fence.IsCompleteProgram)
        {
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                "`expect=`/`group=` apply only to complete programs (first line starts with §M); " +
                "on a fragment they would be silently ignored", page.Path, fence.OpenLine));
            ok = false;
        }

        if (fence.Value("expect") is { } expect
            && (expect.Length == 0 || expect.Split(',').Any(code => !CodePattern.IsMatch(code))))
        {
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                $"Malformed expect={expect}: list Calor diagnostic codes separated by commas", page.Path, fence.OpenLine));
            ok = false;
        }

        if (fence.Value("group") is { } group && group.Split(',').Any(g => !GroupPattern.IsMatch(g)))
        {
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                $"Malformed group={group}", page.Path, fence.OpenLine));
            ok = false;
        }

        // A non-calor fence holding a complete program would bypass every check above.
        if (fence.Language != "calor"
            && fence.Lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) is { } first
            && CompleteProgramStart.IsMatch(first.TrimStart()))
        {
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                $"A complete Calor program (starts with §M) is fenced as ```{fence.Language}; label it ```calor so it is checked",
                page.Path, fence.OpenLine));
            ok = false;
        }

        return ok;
    }

    private static HashSet<string> ExpectedCodes(Fence fence) =>
        (fence.Value("expect") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Compiles the members exactly as <c>calor --input m1.calr [--input m2.calr ...]</c> does
    /// with default options, and returns each member's error and warning diagnostics.
    /// </summary>
    private static Dictionary<Fence, List<Diagnostic>> Compile(DocFile page, List<Fence> members, List<Diagnostic> diagnostics)
    {
        var result = new Dictionary<Fence, List<Diagnostic>>();
        var directory = Path.Combine(Path.GetTempPath(), "calor-website-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var files = new List<FileInfo>();
            for (var i = 0; i < members.Count; i++)
            {
                var path = Path.Combine(directory, $"example{i + 1}.calr");
                File.WriteAllText(path, members[i].Source);
                files.Add(new FileInfo(path));
            }

            var sink = new DiagnosticBag();
            CompilationDriver.CompileAll(
                files,
                file => new CompilationOptions
                {
                    StatusWriter = TextWriter.Null,
                    ProjectDirectory = directory,
                    VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                },
                crossModuleEnforcement: true,
                crossModulePolicy: UnknownCallPolicy.Strict,
                diagnosticSink: sink);

            foreach (var member in members)
                result[member] = [];
            foreach (var diagnostic in sink.Where(d => d.IsError || d.IsWarning))
            {
                var index = diagnostic.FilePath == null ? 0
                    : files.FindIndex(f => string.Equals(Path.GetFullPath(f.FullName),
                        Path.GetFullPath(diagnostic.FilePath), StringComparison.Ordinal));
                result[members[Math.Max(index, 0)]].Add(diagnostic);
            }
        }
        catch (Exception exception)
        {
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteExampleMismatch,
                $"Complete calor example crashed the compiler: {exception.Message}", page.Path, members[0].OpenLine));
            result.Clear();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    /// <summary>
    /// The adjacent prose of a negative example must cite every expected code (so the page
    /// says what the compiler says), and a "line N, column M" claim before the fence must
    /// match a reported location.
    /// </summary>
    private static void CheckProseClaims(DocFile page, string[] lines, List<Fence> fences, Fence program,
        HashSet<string> expected, List<Diagnostic> actual, List<Diagnostic> diagnostics)
    {
        var before = ProseBefore(lines, fences, program);
        // A checked `output` fence quoting this example's diagnostics is part of its claim,
        // even when a heading separates the two.
        var linkedOutputs = fences
            .Where(f => f.Language != "calor" && f.Has("output")
                && fences.LastOrDefault(p => p.IsCompleteProgram && p.CloseLine < f.OpenLine) == program)
            .SelectMany(f => f.Lines);
        var prose = ProseAfter(lines, fences, program);
        var after = prose + "\n" + string.Join("\n", linkedOutputs);
        foreach (var code in expected.Order(StringComparer.Ordinal))
        {
            if (!Regex.IsMatch(before + "\n" + after, $@"\b{code}\b"))
                diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                    $"Negative example expects {code} but the adjacent prose (nearest heading to the next fence) never cites it",
                    page.Path, program.OpenLine));
        }

        foreach (Match claim in LocationClaim.Matches(before + "\n" + prose))
        {
            var line = int.Parse(claim.Groups["line"].Value);
            var column = int.Parse(claim.Groups["column"].Value);
            if (!actual.Any(d => d.Span.Line == line && d.Span.Column == column))
                diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteAnnotation,
                    $"Prose claims a diagnostic at line {line}, column {column}, but the compiler reports " +
                    $"[{string.Join(", ", actual.Select(d => $"{d.Code} at line {d.Span.Line}, column {d.Span.Column}"))}]",
                    page.Path, program.OpenLine));
        }
    }

    private static string ProseBefore(string[] lines, List<Fence> fences, Fence fence)
    {
        var start = fences.Where(f => f.CloseLine < fence.OpenLine).Select(f => f.CloseLine).DefaultIfEmpty(0).Max();
        for (var i = fence.OpenLine - 2; i >= start; i--)
        {
            if (Heading.IsMatch(lines[i]))
            {
                start = i;
                break;
            }
        }
        return string.Join("\n", lines[start..(fence.OpenLine - 1)]);
    }

    private static string ProseAfter(string[] lines, List<Fence> fences, Fence fence)
    {
        // Up to the next calor fence or heading; non-calor fences in between (e.g. the
        // quoted output) count as part of the claim.
        var end = fences.Where(f => f.OpenLine > fence.CloseLine && f.Language == "calor")
            .Select(f => f.OpenLine - 1).DefaultIfEmpty(lines.Length).Min();
        var text = new List<string>();
        for (var i = fence.CloseLine; i < end && i < lines.Length; i++)
        {
            if (Heading.IsMatch(lines[i]) && !fences.Any(f => i + 1 > f.OpenLine && i + 1 < f.CloseLine))
                break;
            text.Add(lines[i]);
        }
        return string.Join("\n", text);
    }

    private static void CheckOutput(DocFile page, Fence output, Unit? unit, List<Diagnostic> diagnostics)
    {
        if (unit == null)
        {
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteOutputMismatch,
                "`output` fence has no preceding complete calor example on this page to check it against",
                page.Path, output.OpenLine));
            return;
        }

        var quoted = new List<(string? Severity, string Code, string Message, string? Location)>();
        foreach (var line in output.Lines)
        {
            var match = QuotedDiagnostic.Match(line.Trim());
            if (match.Success)
                quoted.Add((match.Groups["severity"].Success ? match.Groups["severity"].Value : null,
                    match.Groups["code"].Value, match.Groups["message"].Value,
                    match.Groups["line"].Success ? $"{match.Groups["line"].Value},{match.Groups["column"].Value}" : null));
            else if (quoted.Count > 0 && line.Trim().Length > 0)
                quoted[^1] = quoted[^1] with { Message = quoted[^1].Message + " " + line.Trim() };
            else if (line.Trim().Length > 0)
                diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteOutputMismatch,
                    $"`output` fence line is not a quoted diagnostic and cannot be checked: '{line.Trim()}'",
                    page.Path, output.OpenLine));
        }

        var remaining = unit.Members.SelectMany(m => unit.Actual.GetValueOrDefault(m) ?? []).ToList();
        foreach (var (severity, code, message, location) in quoted)
        {
            var match = remaining.FirstOrDefault(d => d.Code == code
                && Normalize(d.Message) == Normalize(message)
                && (severity == null || string.Equals(severity, d.Severity.ToString(), StringComparison.OrdinalIgnoreCase))
                && (location == null || location == $"{d.Span.Line},{d.Span.Column}"));
            if (match == null)
            {
                diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteOutputMismatch,
                    $"`output` fence quotes {(location == null ? "" : $"({location}) ")}{(severity == null ? "" : severity + " ")}{code}: '{Normalize(message)}', which the " +
                    $"preceding example does not produce. Actual: [{string.Join(" | ", remaining.Select(d => $"({d.Span.Line},{d.Span.Column}) {d.Severity.ToString().ToLowerInvariant()} {d.Code}: {d.Message}"))}]",
                    page.Path, output.OpenLine));
                continue;
            }
            remaining.Remove(match);
        }

        foreach (var missing in remaining)
            diagnostics.Add(Finding(DiagnosticCode.DocDriftWebsiteOutputMismatch,
                $"`output` fence omits {missing.Severity.ToString().ToLowerInvariant()} {missing.Code}: {missing.Message}, " +
                "which the preceding example also reports",
                page.Path, output.OpenLine));
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static Diagnostic Finding(string code, string message, string path, int line) =>
        new(code, DiagnosticSeverity.Error, message, path, line, 1);
}
