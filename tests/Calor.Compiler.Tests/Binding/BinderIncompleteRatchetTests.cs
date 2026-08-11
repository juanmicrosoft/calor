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
        "both F-2 legs; conversion leg skips when corpus submodules are absent";

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
                measured with { Conversion = existing?.Conversion },
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
        // F-2's second leg (activated in B2 as the amendment staged): the three A-1.5.3
        // conversion subjects at their pinned submodule commits, converted in-process
        // (the DS15 pattern — no CLI, no caching machinery needed at this speed) and
        // their outputs bound. This leg exercises exactly the C#-shaped expression forms
        // the in-repo leg under-represents. Skips when submodules are absent (plain CI
        // test jobs); runs locally and in submodule-initialized jobs.
        var root = RepoRoot();
        var subjects = new[] { "MediatR", "serilog", "FluentValidation" }
            .Select(s => Path.Combine(root, "bench", "corpus", s, "src"))
            .ToList();
        Skip.IfNot(subjects.All(Directory.Exists), "corpus submodules not initialized");

        int incomplete = 0, expressionsBound = 0, convertedAndBound = 0;
        int convertExceptions = 0, emptyOutput = 0, outputParseFailures = 0;
        foreach (var srcDir in subjects)
        {
            var csFiles = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .OrderBy(f => f, StringComparer.Ordinal);
            foreach (var cs in csFiles)
            {
                Compiler.Migration.ConversionResult conv;
                try
                {
                    conv = new Compiler.Migration.CSharpToCalorConverter(
                        new Compiler.Migration.ConversionOptions
                        {
                            ModuleName = "Leg2",
                            GracefulFallback = true,
                            AutoGenerateIds = true
                        }).Convert(File.ReadAllText(cs), Path.GetFileName(cs));
                }
                catch { convertExceptions++; continue; }
                if (string.IsNullOrEmpty(conv.CalorSource)) { emptyOutput++; continue; }

                var diagnostics = new DiagnosticBag();
                var lexer = new Lexer(conv.CalorSource.Replace("\r\n", "\n"), diagnostics);
                var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
                var module = parser.Parse();
                if (diagnostics.HasErrors) { outputParseFailures++; continue; }

                var bindBag = new DiagnosticBag();
                var binder = new Binder(bindBag);
                binder.Bind(module);
                expressionsBound += binder.ExpressionsBound;
                incomplete += bindBag.Count(d => d.Code == DiagnosticCode.AnalysisIncomplete);
                convertedAndBound++;
            }
        }

        // Three DISTINCT failure modes recorded separately (review Major 3): the single
        // NotConverted bucket hid that all 59 were converter round-trip validity
        // failures — Calor the converter emitted that Calor's own parser rejects (#903).
        var measured = new ConversionLeg(incomplete, expressionsBound, convertedAndBound,
            convertExceptions, emptyOutput, outputParseFailures);

        if (Environment.GetEnvironmentVariable("CALOR_UPDATE_BINDER_BASELINE") == "1")
        {
            var baseline = File.Exists(BaselinePath())
                ? JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath()))!
                : new Baseline(0, 0, 0, 0, ScopeText);
            File.WriteAllText(BaselinePath(), JsonSerializer.Serialize(
                baseline with { Conversion = measured, Scope = ScopeText },
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return;
        }

        var recorded = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath()))!.Conversion;
        Assert.NotNull(recorded);
        Assert.True(measured == recorded,
            $"Conversion-leg movement: measured {measured} vs baseline {recorded}. Binder family " +
            "PRs and converter changes must regenerate the baseline IN THIS PR with the change " +
            "named — never silently (same ratchet discipline as the in-repo leg; the subjects " +
            "are at pinned submodule commits, so drift here is OUR code, not theirs).");
    }

    private sealed record ConversionLeg(
        int Incomplete, int ExpressionsBound, int ConvertedAndBound,
        int ConvertExceptions, int EmptyOutput, int OutputParseFailures);

    private sealed record Baseline(
        int IncompleteCount, int ParsedFiles, int ParseFailures, int ExpressionsBound, string Scope,
        ConversionLeg? Conversion = null);
}
