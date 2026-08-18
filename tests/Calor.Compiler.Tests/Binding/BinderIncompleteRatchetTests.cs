using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B1: the incomplete-fraction instrument (freeze registration F-2 as amended
/// 2026-08-10). Parses and binds every `.calr` under the F-2 in-repo corpus roots and
/// counts Calor0259 (AnalysisIncomplete) against the bound-expression denominator.
/// The committed baseline is a RATCHET, and parse coverage is asserted too:
/// - the incomplete count may only move DOWN as family PRs land (update the baseline in
///   the same PR — an unrecorded improvement also fails, so the baseline tracks reality);
/// - routing a bound construct back to the fallback RAISES the count and fails here —
///   the F-2 discriminating pin, running on every test invocation;
/// - corpus additions may legitimately move counts, in which case the PR updates the
///   baseline with the added files named (the F-2 amendment's stated exception);
/// - a parse failure is allowed ONLY for files on the explicit list below — anything
///   else fails by NAME (review C2: without this, a parser regression removes files from
///   the denominator, the count "improves", and the failure message invites laundering
///   the regression into the baseline).
/// Regenerate: CALOR_UPDATE_BINDER_BASELINE=1 dotnet test --filter BinderIncompleteRatchet
/// BOTH F-2 legs are active as of B2: the conversion leg (three A-1.5.3 subjects at pinned
/// submodule commits, converted in-process) skips only where submodules are absent.
/// </summary>
public class BinderIncompleteRatchetTests
{
    private static readonly string[] CorpusRoots = ["samples", "tests", "benchmarks",
        Path.Combine("src", "Calor.Compiler", "Resources", "SelfTest")];

    /// <summary>
    /// The ONLY files allowed to fail parsing, each with its registered reason.
    /// Repo-relative, forward slashes. Deliberate error fixtures stay; the known-stale
    /// benchmark subjects are tracked by #901 and leave this list as they are repaired.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedParseFailures =
        new Dictionary<string, string>
        {
            // Deliberately-invalid lint fixtures (they exist to fail):
            ["tests/TestData/LintScenarios/10_error_cases/syntax_error.calr"] = "error fixture",
            ["tests/TestData/LintScenarios/10_error_cases/unterminated_string.calr"] = "error fixture",
            ["tests/TestData/LintScenarios/10_error_cases/mismatched_ids.calr"] =
                "error fixture (NOTE: currently fails on Calor0830 legacy closers, not the " +
                "mismatched ids it was built for — stale in its own way, see #901's pattern)",
            // Known-stale intended-valid benchmark subjects — #901, list shrinks as repaired:
            ["benchmarks/arithmetic/div-by-zero.calr"] = "#901 multi-generation stale",
            ["benchmarks/loops/bounds-violation.calr"] = "#901 multi-generation stale",
            ["benchmarks/null-safety/null-deref.calr"] = "#901 multi-generation stale",
            ["benchmarks/security/command-injection.calr"] = "#901 multi-generation stale",
            ["benchmarks/security/path-traversal.calr"] = "#901 multi-generation stale",
            ["benchmarks/security/sql-injection.calr"] = "#901 multi-generation stale",
        };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string BaselinePath() => Path.Combine(RepoRoot(),
        "bench", "phase0-agent-native", "binder-incomplete-baseline.json");

    // ONE scope string — the two regen writers previously hardcoded different texts,
    // making the committed file depend on writer order (review minor 5).
    private const string ScopeText =
        "in-repo F-2 plus selected-active native conversion and preserve-all opaque coverage; "
        + "Roslyn-selected conversion uses genuinely empty default symbols with "
        + "C# Preview/regular/parse options; legacy source-order 18005 is informational";

    [Fact]
    public void InRepoCorpus_IncompleteCount_DoesNotExceedBaseline()
    {
        var root = RepoRoot();
        var files = CorpusRoots
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.calr", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);

        int incomplete = 0, parsedFiles = 0, expressionsBound = 0;
        var parseFailures = new List<string>();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(File.ReadAllText(file).Replace("\r\n", "\n"), diagnostics);
            var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
            var module = parser.Parse();
            if (diagnostics.HasErrors) { parseFailures.Add(rel); continue; }
            parsedFiles++;

            var bindBag = new DiagnosticBag();
            var binder = new Binder(bindBag);
            binder.Bind(module);
            expressionsBound += binder.ExpressionsBound;
            incomplete += bindBag.Count(d => d.Code == DiagnosticCode.AnalysisIncomplete);
        }

        // Parse failures outside the registered list fail BY NAME — never a silent
        // denominator shrink (review C2 / the F-2 anti-vacuity rule).
        var unexpected = parseFailures.Where(f => !AllowedParseFailures.ContainsKey(f)).ToList();
        Assert.True(unexpected.Count == 0,
            "Files failed to parse that are NOT on the registered allowed list — a parser " +
            "regression or an unregistered stale file; fix the parse or register with a " +
            $"reason and an issue:\n  {string.Join("\n  ", unexpected)}");
        var recovered = AllowedParseFailures.Keys.Except(parseFailures).ToList();
        Assert.True(recovered.Count == 0,
            "Files on the allowed-parse-failure list now PARSE — remove them from the list " +
            $"(and the F-2 amendment) in this PR:\n  {string.Join("\n  ", recovered)}");

        var measured = new Baseline(incomplete, parsedFiles, parseFailures.Count, expressionsBound,
            ScopeText);

        if (Environment.GetEnvironmentVariable("CALOR_UPDATE_BINDER_BASELINE") == "1")
        {
            // Preserve the conversion-leg section — the two regen writers run in
            // nondeterministic order under one test invocation.
            var existing = File.Exists(BaselinePath())
                ? JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath()))
                : null;
            File.WriteAllText(BaselinePath(), JsonSerializer.Serialize(
                measured with
                {
                    Conversion = existing?.Conversion,
                    PreserveCoverage = existing?.PreserveCoverage
                },
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return;
        }

        Assert.True(File.Exists(BaselinePath()),
            "Baseline missing — run once with CALOR_UPDATE_BINDER_BASELINE=1");
        var baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath()))!;

        Assert.True(parsedFiles == baseline.ParsedFiles && parseFailures.Count == baseline.ParseFailures,
            $"Parse coverage moved: {parsedFiles} parsed/{parseFailures.Count} failed vs baseline " +
            $"{baseline.ParsedFiles}/{baseline.ParseFailures}. Corpus additions/repairs must " +
            "regenerate the baseline IN THIS PR with the change named — never silently.");
        Assert.True(incomplete <= baseline.IncompleteCount,
            $"RATCHET: incomplete count rose from {baseline.IncompleteCount} to {incomplete} " +
            $"({parsedFiles} files, {expressionsBound} expressions). A bound construct regressed " +
            "to the fallback, or new corpus files use unbound constructs — if the latter, update " +
            "the baseline in this PR with the added files named (F-2 amendment exception).");
        Assert.True(incomplete == baseline.IncompleteCount,
            $"Incomplete count IMPROVED from {baseline.IncompleteCount} to {incomplete} — " +
            "record it: regenerate the baseline in this PR so the ratchet tracks reality.");
    }

    [SkippableFact]
    public void ConversionLeg_IncompleteCount_MatchesBaseline()
    {
        var root = RepoRoot();
        var subjects = new[] { "MediatR", "serilog", "FluentValidation" }
            .Select(subject => Path.Combine(root, "bench", "corpus", subject, "src"))
            .ToList();
        Skip.IfNot(subjects.All(Directory.Exists), "corpus submodules not initialized");

        var files = subjects
            .SelectMany(directory => Directory.EnumerateFiles(
                directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        var preserveParseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());

        var native = new NativeConversionCoverage();
        var preserve = new PreserveConversionCoverage();
        var opaqueIdentities = new HashSet<string>(StringComparer.Ordinal);
        var unconvertedIdentities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            MeasureNative(file, source, native);
            MeasurePreserve(
                file,
                source,
                preserveParseOptions,
                preserve,
                opaqueIdentities,
                unconvertedIdentities);
        }
        preserve.OpaqueIdentityCount =
            opaqueIdentities.Count;
        preserve.UnconvertedIdentityCount =
            unconvertedIdentities.Count;

        var measuredNative = native.ToRecord();
        var measuredPreserve = preserve.ToRecord();
        if (Environment.GetEnvironmentVariable("CALOR_UPDATE_BINDER_BASELINE") == "1")
        {
            var baseline = File.Exists(BaselinePath())
                ? JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath()))!
                : new Baseline(0, 0, 0, 0, ScopeText);
            File.WriteAllText(BaselinePath(), JsonSerializer.Serialize(
                baseline with
                {
                    Conversion = measuredNative,
                    PreserveCoverage = measuredPreserve,
                    Scope = ScopeText
                },
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return;
        }

        var recorded = JsonSerializer.Deserialize<Baseline>(
            File.ReadAllText(BaselinePath()))!;
        Assert.NotNull(recorded.Conversion);
        Assert.NotNull(recorded.PreserveCoverage);
        Assert.Equal(
            LegacySourceOrderAttempted,
            measuredNative.LegacySourceOrderAttempted);
        Assert.True(
            measuredNative.RoslynSelectedAttempted
                >= recorded.Conversion.RoslynSelectedAttempted,
            $"Roslyn-selected attempted count regressed: "
            + $"{measuredNative.RoslynSelectedAttempted} < "
            + $"{recorded.Conversion.RoslynSelectedAttempted}.");
        Assert.Equal(recorded.Conversion, measuredNative);
        Assert.Equal(0, measuredPreserve.OpaqueUnmapped);
        Assert.Equal(
            measuredPreserve.OpaqueBoundaries,
            measuredPreserve.OpaqueIdentityCount);
        Assert.Equal(
            measuredPreserve.UnconvertedFiles,
            measuredPreserve.UnconvertedIdentityCount);
        Assert.True(
            measuredPreserve.OpaqueBoundaries <= recorded.PreserveCoverage.OpaqueBoundaries
            && measuredPreserve.OpaqueExpressions <= recorded.PreserveCoverage.OpaqueExpressions
            && measuredPreserve.UnconvertedFiles <= recorded.PreserveCoverage.UnconvertedFiles,
            $"Preserve-mode opaque coverage regressed: measured {measuredPreserve} "
            + $"vs baseline {recorded.PreserveCoverage}.");
        Assert.Equal(recorded.PreserveCoverage, measuredPreserve);
    }

    [Fact]
    public void SelectedBranchMode_UsesRoslynBooleanConditions()
    {
        const string source = """
            #if A && !B
            public class SelectedAB { }
            #elif C || D
            public class SelectedCD { }
            #else
            public class SelectedFallback { }
            #endif
            """;

        AssertSelection([], "SelectedFallback");
        AssertSelection(["A"], "SelectedAB");
        AssertSelection(["A", "B"], "SelectedFallback");
        AssertSelection(["C"], "SelectedCD");
        AssertSelection(["D"], "SelectedCD");

        static void AssertSelection(
            IReadOnlyList<string> symbols,
            string expected)
        {
            var parseOptions =
                new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                    Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
                    Microsoft.CodeAnalysis.DocumentationMode.Parse,
                    Microsoft.CodeAnalysis.SourceCodeKind.Regular,
                    preprocessorSymbols: symbols);
            var result =
                new Compiler.Migration.CSharpToCalorConverter(
                    new Compiler.Migration.ConversionOptions
                    {
                        Fidelity =
                            Compiler.Migration.ConversionFidelity.Lossy,
                        PreprocessorMode =
                            Compiler.Migration.PreprocessorConversionMode
                                .SelectActiveBranchLossy,
                        ParseOptions = parseOptions,
                        ModuleName = "Selection"
                    }).Convert(source, "Selection.cs");
            Assert.True(
                result.Success,
                string.Join("; ", result.Issues.Select(issue => issue.Message)));
            Assert.Contains(expected, result.CalorSource);
            foreach (var other in new[]
                     {
                         "SelectedAB",
                         "SelectedCD",
                         "SelectedFallback"
                     }.Where(name => name != expected))
                Assert.DoesNotContain(other, result.CalorSource);
        }
    }

    private static void MeasureNative(
        string file,
        string source,
        NativeConversionCoverage coverage)
    {
        coverage.FilesSeen++;
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());
        Compiler.Migration.ConversionResult conversion;
        try
        {
            conversion = new Compiler.Migration.CSharpToCalorConverter(
                new Compiler.Migration.ConversionOptions
                {
                    Fidelity = Compiler.Migration.ConversionFidelity.Lossy,
                    PreprocessorMode = Compiler.Migration.PreprocessorConversionMode
                        .SelectActiveBranchLossy,
                    ParseOptions = parseOptions,
                    DefinedSymbols = Array.Empty<string>(),
                    ModuleName = "Leg2",
                    GracefulFallback = true,
                    AutoGenerateIds = true
                }).Convert(source, Path.GetFileName(file));
        }

        catch
        {
            coverage.ConvertExceptions++;
            return;
        }
        if (string.IsNullOrEmpty(conversion.CalorSource))
        {
            coverage.EmptyOutput++;
            return;
        }
        var diagnostics = new DiagnosticBag();
        var module = new Parser(
            new Lexer(conversion.CalorSource.Replace("\r\n", "\n"), diagnostics)
                .TokenizeAllForParser(),
            diagnostics).Parse();
        if (diagnostics.HasErrors)
        {
            coverage.OutputParseFailures++;
            return;
        }
        var bindDiagnostics = new DiagnosticBag();
        var binder = new Binder(bindDiagnostics);
        binder.Bind(module);
        coverage.ConvertedAndBound++;
        coverage.RoslynSelectedAttempted +=
            binder.ExpressionsBound;
        coverage.Incomplete += bindDiagnostics.Count(diagnostic =>
            diagnostic.Code == DiagnosticCode.AnalysisIncomplete);
    }

    private static void MeasurePreserve(
        string file,
        string source,
        Microsoft.CodeAnalysis.CSharp.CSharpParseOptions parseOptions,
        PreserveConversionCoverage coverage,
        HashSet<string> opaqueIdentities,
        HashSet<string> unconvertedIdentities)
    {
        coverage.FilesSeen++;
        Compiler.Migration.ConversionResult? conversion = null;
        var unconverted = false;
        try
        {
            conversion = new Compiler.Migration.CSharpToCalorConverter(
                new Compiler.Migration.ConversionOptions
                {
                    Fidelity = Compiler.Migration.ConversionFidelity.Lossless,
                    PreprocessorMode = Compiler.Migration.PreprocessorConversionMode
                        .PreserveAllBranches,
                    ParseOptions = parseOptions,
                    DefinedSymbols = Array.Empty<string>(),
                    ModuleName = "Leg2",
                    GracefulFallback = true,
                    AutoGenerateIds = true,
                    ValidateRoundTripCSharp = false
                }).Convert(source, Path.GetFileName(file));
        }
        catch
        {
            coverage.ConvertExceptions++;
            unconverted = true;
        }

        if (conversion?.Ast != null)
        {
            var spans = CollectOpaqueSpans(conversion.Ast, source, file, coverage);
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, parseOptions);
            var root = tree.GetRoot();
            foreach (var span in spans)
            {
                var identity = $"{Path.GetFullPath(file)}:{span.Start}:{span.End}";
                Assert.True(opaqueIdentities.Add(identity),
                    $"Duplicate opaque identity: {identity}");
                coverage.OpaqueBoundaries++;
                var target = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    span.Start, span.End);
                coverage.OpaqueExpressions += root.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax>()
                    .Count(expression => target.Contains(expression.Span));
            }
        }

        if (string.IsNullOrEmpty(conversion?.CalorSource))
        {
            coverage.EmptyOutput++;
            unconverted = true;
        }
        else
        {
            var diagnostics = new DiagnosticBag();
            _ = new Parser(
                new Lexer(conversion.CalorSource, diagnostics).TokenizeAllForParser(),
                diagnostics).Parse();
            if (diagnostics.HasErrors)
            {
                coverage.OutputParseFailures++;
                unconverted = true;
            }
        }
        if (conversion is { Success: false })
            unconverted = true;
        if (unconverted)
        {
            coverage.UnconvertedFiles++;
            var identity = $"{Path.GetFullPath(file)}:0:{source.Length}";
            Assert.True(unconvertedIdentities.Add(identity),
                $"Duplicate unconverted identity: {identity}");
        }
    }

    private static IReadOnlyList<(int Start, int End)> CollectOpaqueSpans(
        Calor.Compiler.Ast.ModuleNode module,
        string source,
        string file,
        PreserveConversionCoverage coverage)
    {
        var entries = new List<(Calor.Compiler.Ast.AstNode Node, string Code)>();
        var stack = new Stack<Calor.Compiler.Ast.AstNode>();
        var seen = new HashSet<Calor.Compiler.Ast.AstNode>(
            ReferenceEqualityComparer.Instance);
        stack.Push(module);
        while (stack.TryPop(out var node))
        {
            if (!seen.Add(node))
                continue;
            switch (node)
            {
                case Calor.Compiler.Ast.CSharpInteropBlockNode interop:
                    entries.Add((node, interop.CSharpCode));
                    break;
                case Calor.Compiler.Ast.RawCSharpNode raw:
                    entries.Add((node, raw.CSharpCode));
                    break;
            }
            foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker
                         .GetAllChildren(node))
                stack.Push(child);
        }

        var used = new List<(int Start, int End)>();
        foreach (var entry in entries
                     .OrderByDescending(entry => entry.Node.Span.Length)
                     .ThenByDescending(entry => entry.Code.Length))
        {
            var mapped = TryMapOpaqueSpan(entry.Node, entry.Code, source, used);
            if (mapped == null)
            {
                coverage.OpaqueUnmapped++;
                continue;
            }
            if (used.Any(existing => mapped.Value.Start >= existing.Start
                && mapped.Value.End <= existing.End))
                continue;
            Assert.False(used.Any(existing => mapped.Value.Start < existing.End
                && mapped.Value.End > existing.Start),
                $"Partially overlapping opaque spans in {file}: "
                + $"{mapped.Value.Start}..{mapped.Value.End}");
            used.Add(mapped.Value);
        }
        return used.OrderBy(span => span.Start).ToArray();
    }

    private static (int Start, int End)? TryMapOpaqueSpan(
        Calor.Compiler.Ast.AstNode node,
        string code,
        string source,
        IReadOnlyList<(int Start, int End)> used)
    {
        if (node.Span.Length > 0
            && node.Span.Start >= 0
            && node.Span.End <= source.Length)
        {
            var span = (node.Span.Start, node.Span.End);
            return span;
        }
        foreach (var candidate in new[] { code, code.Trim() }
                     .Where(candidate => candidate.Length > 0)
                     .Distinct(StringComparer.Ordinal))
        {
            var search = 0;
            while (search <= source.Length - candidate.Length)
            {
                var start = source.IndexOf(candidate, search, StringComparison.Ordinal);
                if (start < 0)
                    break;
                var span = (Start: start, End: start + candidate.Length);
                if (!used.Any(existing =>
                        span.Start < existing.End
                        && span.End > existing.Start)
                    || used.Any(existing =>
                        span.Start >= existing.Start
                        && span.End <= existing.End))
                    return span;
                search = start + 1;
            }
        }
        return null;
    }

    private const int LegacySourceOrderAttempted = 18005;

    private sealed class NativeConversionCoverage
    {
        public int Incomplete;
        public int RoslynSelectedAttempted;
        public int FilesSeen;
        public int ConvertedAndBound;
        public int ConvertExceptions;
        public int EmptyOutput;
        public int OutputParseFailures;
        public NativeConversionLeg ToRecord() => new(
            Incomplete,
            LegacySourceOrderAttempted,
            RoslynSelectedAttempted,
            FilesSeen,
            ConvertedAndBound,
            ConvertExceptions,
            EmptyOutput,
            OutputParseFailures);
    }

    private sealed class PreserveConversionCoverage
    {
        public int FilesSeen;
        public int OpaqueBoundaries;
        public int OpaqueExpressions;
        public int OpaqueUnmapped;
        public int OpaqueIdentityCount;
        public int UnconvertedFiles;
        public int UnconvertedIdentityCount;
        public int ConvertExceptions;
        public int EmptyOutput;
        public int OutputParseFailures;
        public PreserveCoverageLeg ToRecord() => new(
            FilesSeen,
            OpaqueBoundaries,
            OpaqueExpressions,
            OpaqueUnmapped,
            OpaqueIdentityCount,
            UnconvertedFiles,
            UnconvertedIdentityCount,
            ConvertExceptions,
            EmptyOutput,
            OutputParseFailures);
    }

    private sealed record NativeConversionLeg(
        int Incomplete,
        int LegacySourceOrderAttempted,
        int RoslynSelectedAttempted,
        int FilesSeen,
        int ConvertedAndBound,
        int ConvertExceptions,
        int EmptyOutput,
        int OutputParseFailures);

    private sealed record PreserveCoverageLeg(
        int FilesSeen,
        int OpaqueBoundaries,
        int OpaqueExpressions,
        int OpaqueUnmapped,
        int OpaqueIdentityCount,
        int UnconvertedFiles,
        int UnconvertedIdentityCount,
        int ConvertExceptions,
        int EmptyOutput,
        int OutputParseFailures);

    private sealed record Baseline(
        int IncompleteCount,
        int ParsedFiles,
        int ParseFailures,
        int ExpressionsBound,
        string Scope,
        NativeConversionLeg? Conversion = null,
        PreserveCoverageLeg? PreserveCoverage = null);
}
