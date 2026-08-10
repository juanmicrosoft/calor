using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B1: the incomplete-fraction instrument (freeze registration F-2 as amended
/// 2026-08-10). Parses and binds every tracked `.calr` under the F-2 in-repo corpus roots
/// and counts Calor0259 (AnalysisIncomplete). The committed baseline is a RATCHET:
/// - the count may only move DOWN as family PRs land (update the baseline in the same PR);
/// - routing a bound construct back to the fallback RAISES the count and fails here —
///   the F-2 discriminating pin, running on every test invocation;
/// - corpus additions may legitimately raise it, in which case the PR updates the baseline
///   UP with the added files named (the F-2 amendment's stated exception).
/// Regenerate: CALOR_UPDATE_BINDER_BASELINE=1 dotnet test --filter BinderIncompleteRatchet
/// The conversion leg (the three A-1.5.3 subjects via `calor migrate`) activates in B2
/// with its submodule + caching machinery — disclosed in the baseline file.
/// </summary>
public class BinderIncompleteRatchetTests
{
    private static readonly string[] CorpusRoots = ["samples", "tests", "benchmarks",
        Path.Combine("src", "Calor.Compiler", "Resources", "SelfTest")];

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

        int incomplete = 0, parsedFiles = 0, parseFailures = 0;
        foreach (var file in files)
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(File.ReadAllText(file).Replace("\r\n", "\n"), diagnostics);
            var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
            var module = parser.Parse();
            if (diagnostics.HasErrors) { parseFailures++; continue; }
            parsedFiles++;

            var bindBag = new DiagnosticBag();
            new Binder(bindBag).Bind(module);
            incomplete += bindBag.Count(d => d.Code == DiagnosticCode.AnalysisIncomplete);
        }

        var measured = new Baseline(incomplete, parsedFiles, parseFailures,
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

        Assert.True(incomplete <= baseline.IncompleteCount,
            $"RATCHET: incomplete count rose from {baseline.IncompleteCount} to {incomplete} " +
            $"({parsedFiles} files). A bound construct regressed to the fallback, or new corpus " +
            "files use unbound constructs — if the latter, update the baseline UP in this PR " +
            "with the added files named (F-2 amendment exception); never silently.");
        Assert.True(incomplete == baseline.IncompleteCount,
            $"Incomplete count IMPROVED from {baseline.IncompleteCount} to {incomplete} — " +
            "record it: regenerate the baseline in this PR so the ratchet tracks reality.");
    }

    private sealed record Baseline(int IncompleteCount, int ParsedFiles, int ParseFailures, string Scope);
}
