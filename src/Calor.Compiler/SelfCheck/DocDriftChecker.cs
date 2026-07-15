using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;

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
}

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
        var syntaxIndex = LoadDoc(root, Path.Combine("docs", "syntax-reference", "index.md"), loadErrors);
        var effectsDoc = LoadDoc(root, Path.Combine("docs", "syntax-reference", "effects.md"), loadErrors);
        var cliCodesDoc = LoadDoc(root, Path.Combine("docs", "cli", "structured-output.md"), loadErrors);

        var cliDocs = LoadDocsInDirectory(root, Path.Combine("docs", "cli"), loadErrors);

        // Version scan covers all agent-facing docs. Dated planning/experiment
        // records legitimately cite historical versions and are excluded.
        string[] versionScanExclusions = ["plans", "experiments", "design", "process"];
        var versionDocs = LoadDocsInDirectory(root, "docs", loadErrors, recursive: true)
            .Where(d => !versionScanExclusions.Any(x =>
                d.Path.StartsWith(Path.Combine("docs", x) + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .ToList();

        return new DocDriftInputs
        {
            Version = version,
            LexerKeywords = Parsing.Lexer.KeywordNames,
            DiagnosticCodes = GetImplementedDiagnosticCodes(),
            KnownEffectCodes = Effects.EffectCodes.KnownCompactCodes,
            DocumentedEffectCodes = Effects.EffectCodes.DocumentedCompactCodes,
            KeywordDocs = NonNull(claudeMd, syntaxIndex),
            DiagnosticCodeDocs = NonNull(claudeMd).Concat(cliDocs).ToList(),
            EffectsReferenceDoc = effectsDoc,
            EffectDocsForwardOnly = NonNull(syntaxIndex),
            CliCodesDoc = cliCodesDoc,
            VersionScanDocs = NonNull(claudeMd).Concat(versionDocs).ToList(),
        };
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
        ForEachLine(doc, (line, lineNumber) =>
        {
            foreach (Match match in KeywordRef.Matches(line))
            {
                var name = match.Groups[1].Value;
                if (!keywordSet.Contains(name))
                {
                    diagnostics.Add(Drift(
                        DiagnosticCode.DocDriftUnknownKeyword,
                        $"Documented keyword '§{name}' does not exist in the lexer's keyword table",
                        doc.Path, lineNumber, match.Index + 1));
                }
            }
        });
    }

    private static void CheckDiagnosticCodes(
        DocFile doc, IReadOnlyCollection<string> codes, List<Diagnostic> diagnostics)
    {
        var codeSet = codes as ISet<string> ?? new HashSet<string>(codes, StringComparer.Ordinal);
        var codeNumbers = codes
            .Select(c => int.Parse(c["Calor".Length..]))
            .ToArray();

        ForEachLine(doc, (line, lineNumber) =>
        {
            // Band citations first: "Calor0800–0899", "Calor1300-Calor1399".
            var rangeSpans = new List<(int Start, int End)>();
            foreach (Match range in CodeRange.Matches(line))
            {
                rangeSpans.Add((range.Index, range.Index + range.Length));
                var lo = int.Parse(range.Groups["lo"].Value);
                var hi = int.Parse(range.Groups["hi"].Value);
                if (!codeNumbers.Any(n => n >= lo && n <= hi))
                {
                    diagnostics.Add(Drift(
                        DiagnosticCode.DocDriftEmptyDiagnosticRange,
                        $"Documented diagnostic band Calor{lo:D4}-Calor{hi:D4} contains no implemented diagnostic codes",
                        doc.Path, lineNumber, range.Index + 1));
                }
            }

            // Standalone citations (not part of a band citation) must exist exactly.
            foreach (Match match in CodeRef.Matches(line))
            {
                if (rangeSpans.Any(s => match.Index >= s.Start && match.Index < s.End))
                {
                    continue;
                }

                if (!codeSet.Contains(match.Value))
                {
                    diagnostics.Add(Drift(
                        DiagnosticCode.DocDriftUnknownDiagnosticCode,
                        $"Documented diagnostic code '{match.Value}' is not defined in DiagnosticCode",
                        doc.Path, lineNumber, match.Index + 1));
                }
            }
        });
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
        foreach (var (code, lineNumber, column) in rows)
        {
            documented.Add(code);
            if (!known.Contains(code))
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

    private static void CheckHardcodedVersion(
        DocFile doc, string version, List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        var pattern = new Regex($@"(?<![0-9.]){Regex.Escape(version)}(?![0-9.])");
        ForEachLine(doc, (line, lineNumber) =>
        {
            foreach (Match match in pattern.Matches(line))
            {
                diagnostics.Add(Drift(
                    DiagnosticCode.DocDriftHardcodedVersion,
                    $"Doc hardcodes the current compiler version '{version}'; version is single-sourced in Directory.Build.props",
                    doc.Path, lineNumber, match.Index + 1));
            }
        });
    }

    private static (List<(string Code, int Line, int Column)> Rows, int SectionLine) FindEffectTableRows(DocFile doc)
    {
        var rows = new List<(string, int, int)>();
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
                    rows.Add((match.Groups["code"].Value, i + 1, match.Groups["code"].Index + 1));
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
        string root, string relativeDir, List<Diagnostic> loadErrors, bool recursive = false)
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
            .EnumerateFiles(fullDir, "*.md", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new DocFile(Path.GetRelativePath(root, p), File.ReadAllText(p)))
            .ToList();
    }

    private static List<DocFile> NonNull(params DocFile?[] docs)
        => docs.Where(d => d != null).Select(d => d!).ToList();
}
