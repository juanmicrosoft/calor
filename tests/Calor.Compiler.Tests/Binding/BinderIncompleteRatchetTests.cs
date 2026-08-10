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
/// The conversion leg (the three A-1.5.3 subjects via `calor migrate`) activates in B2
/// with its submodule + caching machinery — disclosed in the baseline file.
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
            "in-repo leg only; conversion leg (A-1.5.3 subjects) activates in B2 — see F-2 amendment");

        if (Environment.GetEnvironmentVariable("CALOR_UPDATE_BINDER_BASELINE") == "1")
        {
            File.WriteAllText(BaselinePath(),
                JsonSerializer.Serialize(measured, new JsonSerializerOptions { WriteIndented = true }) + "\n");
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

    private sealed record Baseline(
        int IncompleteCount, int ParsedFiles, int ParseFailures, int ExpressionsBound, string Scope);
}
