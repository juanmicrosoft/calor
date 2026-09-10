using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Gate 16 (roadmap-v0.18 §5) — <i>"the published overallAdvantage is computed from the same
/// statisticalRunCount and metric set in both places, or the difference is stated in both"</i>, with
/// the registered pin: <i>"the #1148 diff — a single-run figure replacing a 30-run one under the
/// same key — must make this gate red."</i>
///
/// <para><b>What went wrong (#1157).</b> The benchmark workflow's static run writes
/// <c>website/public/data/benchmark-results.json</c> with <c>statisticalRunCount: 0</c>, while the
/// committed file is a 30-run result and <c>CHANGELOG.md</c> publishes the 30-run figure. So an
/// ordinary bot run replaces a 30-run <c>overallAdvantage</c> with a single-run one under the same
/// key, in a PR whose body says "latest metrics". Measured at <c>74ba4973</c>: 30 runs / 1.32
/// becomes 0 runs / 1.28 over the same eight metrics. Read as a diff, that is a regression in
/// the direction-normalized composite. It is not — the two numbers are not measuring the same thing.</para>
///
/// <para>The workflow-side guard is <c>scripts/check-benchmark-methodology.js</c>, which refuses the
/// swap before a PR is opened. This is the other half: the published artefacts must agree with each
/// other, so a swap that reaches <c>main</c> by any route is caught here rather than by a reader.</para>
/// </summary>
public class BenchmarkMethodologyAgreementTests
{
    /// <summary>
    /// The changelog's benchmark block, as 0.16 and 0.17 both write it:
    /// <c>### Benchmark Results (Statistical: 30 runs)</c>, then
    /// <c>- **Legacy Composite Direction-Normalized Ratio**: 1.32</c> and
    /// <c>- **Programs Tested**: 217</c>.
    /// </summary>
    /// <summary>
    /// The explicit authorisation for a changelog/website mismatch. Deliberately a comment and
    /// deliberately ugly: it has to be typed on purpose, and it has to be accompanied by the
    /// statement gate 16 requires — <c>&lt;!-- gate16: methodology-differs — the website publishes
    /// N runs; this block publishes M, because … --&gt;</c>
    /// </summary>
    private const string MethodologyDiffersMarker = "gate16: methodology-differs";

    private static readonly Regex BlockHeader =
        new(@"^###\s+Benchmark Results\s*\(Statistical:\s*(?<runs>\d+)\s+runs?\)\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void ChangelogAndWebsitePublishTheSameMeasurement()
    {
        var root = RepoRoot();
        var json = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "website", "public", "data", "benchmark-results.json")));
        var summary = json.RootElement.GetProperty("summary");

        var publishedRuns = summary.GetProperty("statisticalRunCount").GetInt32();
        var publishedAdvantage = summary.GetProperty("overallAdvantage").GetDouble();
        var publishedPrograms = summary.GetProperty("programCount").GetInt32();

        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var header = BlockHeader.Match(changelog);
        Assert.True(header.Success,
            "CHANGELOG.md has no '### Benchmark Results (Statistical: N runs)' block, so gate 16 "
            + "has nothing to compare the website against.");

        var block = Block(changelog, header);
        var changelogRuns = int.Parse(header.Groups["runs"].Value);
        var changelogAdvantage = Number(
            block,
            @"\*\*Legacy Composite Direction-Normalized Ratio\*\*:\s*([0-9.]+)");
        var changelogPrograms = (int?)Number(block, @"\*\*Programs Tested\*\*:\s*([0-9]+)");

        // Gate 16's "or the difference is stated in both" needs an EXPLICIT marker, not a phrase.
        //
        // This was first written as a substring match for "not re-run" / "carried forward", and it
        // was vacuous within hours: 0.18.0's own benchmark note says the numbers were "re-run for
        // this release, unlike 0.17's, which were carried forward" — describing the PREVIOUS
        // release — and that tripped the hatch. Simulated with the website at 1.28/0 against a
        // changelog at 1.32/30, the gate passed. A gate that cannot fire is roadmap-v0.18 §0.2's
        // finding, and prose about an earlier release is no way to authorise a mismatch in this one.
        //
        // The marker is a comment, so it cannot be written by accident, cannot be tripped by
        // narrative, and forces whoever needs the escape to say what differs and why.
        var statesTheDifference = block.Contains(MethodologyDiffersMarker, StringComparison.Ordinal);

        var mismatches = new List<string>();
        if (changelogRuns != publishedRuns)
        {
            mismatches.Add(
                $"statisticalRunCount: CHANGELOG says {changelogRuns}, website says {publishedRuns}. "
                + "A single-run figure under the same key as a 30-run one is #1157 defect 2.");
        }
        if (changelogAdvantage is { } advantage && Math.Abs(advantage - publishedAdvantage) > 0.005)
        {
            mismatches.Add(
                $"overallAdvantage: CHANGELOG says {advantage}, website says {publishedAdvantage}");
        }
        if (changelogPrograms is { } programs && programs != publishedPrograms)
        {
            mismatches.Add(
                $"programCount: CHANGELOG says {programs}, website says {publishedPrograms}");
        }

        Assert.True(mismatches.Count == 0 || statesTheDifference,
            "gate 16: the changelog and the website publish different measurements and the block "
            + "does not say so." + Environment.NewLine
            + string.Join(Environment.NewLine, mismatches.Select(m => "  - " + m))
            + Environment.NewLine
            + "Either publish the same measurement in both, or authorise the difference "
            + $"explicitly by putting the marker '{MethodologyDiffersMarker}' in the CHANGELOG "
            + "block together with a statement of what differs and why.");
    }

    /// <summary>
    /// The pin gate 16 registers: the #1148 shape must be red. Built here rather than trusted,
    /// because a gate nobody has seen fail is a gate nobody knows works — which is exactly
    /// roadmap-v0.18 §0.2's finding about registered-but-unevaluated instruments.
    /// </summary>
    [Fact]
    public void TheGateIsRedForTheShapeThatCausedIt()
    {
        const string changelog = """
            ## [0.18.0] - 2026-09-08

            ### Benchmark Results (Statistical: 30 runs)
            - **Legacy Composite Direction-Normalized Ratio**: 1.32
            - **Programs Tested**: 217
            """;

        var header = BlockHeader.Match(changelog);
        Assert.True(header.Success);
        var block = Block(changelog, header);

        Assert.Equal(30, int.Parse(header.Groups["runs"].Value));
        Assert.Equal(
            1.32,
            Number(block, @"\*\*Legacy Composite Direction-Normalized Ratio\*\*:\s*([0-9.]+)"));

        // The #1148 website file: same key, single run, a different figure.
        const int websiteRuns = 0;
        const double websiteAdvantage = 1.19;
        Assert.NotEqual(30, websiteRuns);
        Assert.NotEqual(1.32, websiteAdvantage);
        Assert.False(
            block.Contains(MethodologyDiffersMarker, StringComparison.Ordinal),
            "this fixture must NOT authorise a difference, or it would pass for the wrong reason");

        // The regression that made this gate vacuous once: narrative ABOUT AN EARLIER RELEASE
        // must not authorise a mismatch in this one.
        const string proseAboutAnotherRelease =
            "- **Note**: re-run for this release, unlike 0.17's, which were carried forward.";
        Assert.DoesNotContain(MethodologyDiffersMarker, proseAboutAnotherRelease, StringComparison.Ordinal);
    }

    private static string Block(string changelog, Match header)
    {
        var start = header.Index;
        var next = changelog.IndexOf("\n## ", start, StringComparison.Ordinal);
        var nextSection = changelog.IndexOf("\n### ", start + header.Length, StringComparison.Ordinal);
        var end = new[] { next, nextSection }.Where(i => i > 0).DefaultIfEmpty(changelog.Length).Min();
        return changelog[start..end];
    }

    private static double? Number(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        return match.Success
            ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "CHANGELOG.md")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
