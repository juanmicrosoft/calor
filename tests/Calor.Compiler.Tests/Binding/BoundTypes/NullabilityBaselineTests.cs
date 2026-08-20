using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// F-1 baseline pin for v0.14 nullability enforcement (issue #875).
/// Baseline lives at bench/phase0-agent-native/nullability-info-baseline.json
/// and freezes at S1 merge. Additive PRs cannot raise total_bindings;
/// subtractive amendments require a supersession entry in the baseline JSON.
///
/// See docs/plans/v0.14-nullability-enforcement-scoping.md §4 F-1.
/// </summary>
public class NullabilityBaselineTests
{
    private static readonly Regex StringBindRegex = new(
        @"§B\{[^}]+:string\}",
        RegexOptions.Compiled);

    [Fact]
    public void CorpusStringBindings_MatchS1Baseline()
    {
        var repoRoot = CliTestHarness.FindRepoRoot();
        var baselinePath = Path.Combine(repoRoot,
            "bench", "phase0-agent-native", "nullability-info-baseline.json");
        Assert.True(File.Exists(baselinePath),
            $"F-1 baseline not found: {baselinePath}. This file freezes at S1's merge.");

        using var stream = File.OpenRead(baselinePath);
        var baseline = JsonSerializer.Deserialize<JsonElement>(stream);
        var expectedTotal = baseline.GetProperty("total_bindings").GetInt32();

        var count = 0;
        foreach (var root in new[] { "samples", "benchmarks" })
        {
            var rootPath = Path.Combine(repoRoot, root);
            if (!Directory.Exists(rootPath)) continue;
            foreach (var file in Directory.EnumerateFiles(rootPath, "*.calr", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (StringBindRegex.IsMatch(line))
                    {
                        count++;
                    }
                }
            }
        }

        Assert.True(count <= expectedTotal,
            $"F-1 ratchet violated: found {count} §B{{...:string}} bindings in samples/+benchmarks/, " +
            $"S1 baseline is {expectedTotal}. New string-typed bindings without a matching " +
            "baseline supersession entry are forbidden. To lower the baseline (permitted), " +
            "migrate bindings to :?string as appropriate and update the baseline JSON in the same PR.");
    }
}
