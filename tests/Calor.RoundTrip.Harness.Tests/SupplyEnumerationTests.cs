using Calor.RoundTrip.Harness;
using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Unit tests for the v0.12 S1 enumeration-only pre-pass. The conversion step is injected, so these
/// exercise the classification, the counting, and — most importantly — the D-5 decision rule and its
/// undefined case, without needing the corpus.
/// </summary>
public class SupplyEnumerationTests
{
    private static RoundTripConfig Project(string name) => new()
    {
        ProjectName = name,
        OriginalProjectPath = "/nonexistent",
        LibrarySourceRelativePath = "src",
        SolutionOrProjectFile = "x.sln",
    };

    private static FileConversionResult File(string path, FileStatus status, int losses) =>
        new() { FilePath = path, Status = status, LossCount = losses };

    /// <summary>A source with exactly one EffectViolation-eligible method (int-returning, block body).</summary>
    private const string OneEffectCandidate = """
        using System;
        namespace S;
        public class C
        {
            private int _n;
            public int Next() { Console.WriteLine("x"); return _n + 1; }
        }
        """;

    private const string NoCandidates = """
        namespace S;
        public class Empty { }
        """;

    private static Task<SupplyEnumeration.Result> Run(
        IReadOnlyList<RoundTripConfig> projects,
        Dictionary<string, List<FileConversionResult>> ledger,
        Dictionary<string, string> sources) =>
        SupplyEnumeration.RunAsync(
            projects,
            p => Task.FromResult<IReadOnlyList<FileConversionResult>>(ledger[p.ProjectName]),
            (_, rel) => Task.FromResult<string?>(sources.GetValueOrDefault(rel)));

    [Fact]
    public async Task Classifies_native_versus_withloss_by_the_loss_ledger()
    {
        var result = await Run(
            [Project("P")],
            new() { ["P"] = [File("a.cs", FileStatus.Replaced, 0), File("b.cs", FileStatus.Replaced, 3)] },
            new() { ["a.cs"] = OneEffectCandidate, ["b.cs"] = OneEffectCandidate });

        var p = Assert.Single(result.Projects);
        Assert.Equal(1, p.NativeFiles);
        Assert.Equal(1, p.WithLossFiles);
        Assert.Equal(1, p.SupplyNative);
        Assert.Equal(1, p.SupplyWithLoss);
    }

    [Fact]
    public async Task Reverted_and_failed_files_are_in_neither_population()
    {
        // Only Replaced files were actually converted; anything else must not inflate either count.
        var result = await Run(
            [Project("P")],
            new()
            {
                ["P"] =
                [
                    File("kept.cs", FileStatus.Replaced, 0),
                    File("reverted.cs", FileStatus.Reverted, 0),
                    File("excluded.cs", FileStatus.Excluded, 0),
                ]
            },
            new() { ["kept.cs"] = OneEffectCandidate, ["reverted.cs"] = OneEffectCandidate, ["excluded.cs"] = OneEffectCandidate });

        var p = Assert.Single(result.Projects);
        Assert.Equal(1, p.NativeFiles);
        Assert.Equal(0, p.WithLossFiles);
        Assert.Equal(1, p.SupplyNative);
        Assert.Equal(0, p.SupplyWithLoss);
    }

    [Fact]
    public async Task D5_accepts_at_or_above_the_threshold()
    {
        // 2 native candidates, 1 with-loss → ratio 0.5, exactly the pre-committed boundary.
        var result = await Run(
            [Project("P")],
            new() { ["P"] = [File("a.cs", FileStatus.Replaced, 0), File("b.cs", FileStatus.Replaced, 0), File("c.cs", FileStatus.Replaced, 1)] },
            new() { ["a.cs"] = OneEffectCandidate, ["b.cs"] = OneEffectCandidate, ["c.cs"] = OneEffectCandidate });

        Assert.Equal(0.5, result.PooledRatio);
        Assert.StartsWith("ACCEPT", result.D5Verdict);
    }

    [Fact]
    public async Task D5_rejects_below_the_threshold()
    {
        var result = await Run(
            [Project("P")],
            new()
            {
                ["P"] =
                [
                    File("a.cs", FileStatus.Replaced, 0), File("b.cs", FileStatus.Replaced, 0),
                    File("c.cs", FileStatus.Replaced, 0), File("d.cs", FileStatus.Replaced, 2),
                ]
            },
            new()
            {
                ["a.cs"] = OneEffectCandidate, ["b.cs"] = OneEffectCandidate,
                ["c.cs"] = OneEffectCandidate, ["d.cs"] = OneEffectCandidate,
            });

        Assert.Equal(1.0 / 3.0, result.PooledRatio!.Value, 5);
        Assert.StartsWith("REJECT", result.D5Verdict);
    }

    [Fact]
    public async Task D5_is_UNDECIDABLE_not_reject_when_native_supply_is_zero()
    {
        // The failure this guards: 0 native supply makes the ratio undefined. Reporting it as 0.0 —
        // and therefore "REJECT" — would let a corpus with NO supply silently decide a design
        // question it cannot speak to.
        var result = await Run(
            [Project("P")],
            new() { ["P"] = [File("a.cs", FileStatus.Replaced, 4)] },
            new() { ["a.cs"] = OneEffectCandidate });

        var p = Assert.Single(result.Projects);
        Assert.Equal(0, p.SupplyNative);
        Assert.Equal(1, p.SupplyWithLoss);
        Assert.Null(p.WithLossOverNativeRatio);
        Assert.Null(result.PooledRatio);
        Assert.StartsWith("UNDECIDABLE", result.D5Verdict);
    }

    [Fact]
    public async Task Files_without_candidates_do_not_appear_in_the_clustering_table()
    {
        var result = await Run(
            [Project("P")],
            new() { ["P"] = [File("has.cs", FileStatus.Replaced, 0), File("none.cs", FileStatus.Replaced, 0)] },
            new() { ["has.cs"] = OneEffectCandidate, ["none.cs"] = NoCandidates });

        var p = Assert.Single(result.Projects);
        Assert.Equal(["has.cs"], p.NativeByFile.Keys);
        Assert.Equal(1, p.SupplyNative);
    }

    [Fact]
    public async Task Missing_source_is_skipped_rather_than_throwing()
    {
        var result = await Run(
            [Project("P")],
            new() { ["P"] = [File("gone.cs", FileStatus.Replaced, 0)] },
            new());

        var p = Assert.Single(result.Projects);
        Assert.Equal(0, p.SupplyNative);
    }

    [Fact]
    public async Task Totals_pool_across_projects_and_per_project_ratios_survive()
    {
        var result = await Run(
            [Project("A"), Project("B")],
            new()
            {
                ["A"] = [File("a.cs", FileStatus.Replaced, 0)],
                ["B"] = [File("b.cs", FileStatus.Replaced, 1)],
            },
            new() { ["a.cs"] = OneEffectCandidate, ["b.cs"] = OneEffectCandidate });

        Assert.Equal(1, result.TotalSupplyNative);
        Assert.Equal(1, result.TotalSupplyWithLoss);
        Assert.Equal(1.0, result.PooledRatio);
        // A pooled ACCEPT must not hide that B alone has no native denominator.
        Assert.Null(result.Projects.Single(p => p.ProjectName == "B").WithLossOverNativeRatio);
    }

    [Fact]
    public void Report_renders_the_upper_bound_caveat_and_the_verdict()
    {
        var result = new SupplyEnumeration.Result
        {
            Projects =
            [
                new SupplyEnumeration.ProjectSupply
                {
                    ProjectName = "P", NativeFiles = 1, WithLossFiles = 1,
                    SupplyNative = 2, SupplyWithLoss = 1,
                    NativeByOperator = new Dictionary<string, int> { ["EffectViolation"] = 2 },
                    WithLossByOperator = new Dictionary<string, int> { ["EffectViolation"] = 1 },
                    NativeByFile = new Dictionary<string, int> { ["a.cs"] = 2 },
                }
            ]
        };

        var md = SupplyEnumerationReport.Render(result);

        Assert.Contains("Upper bound, not realized supply", md);
        Assert.Contains("before* the A-1.5 freeze", md);
        Assert.Contains("ACCEPT", md);
        Assert.Contains("lexicographic prefix", md);
    }
}
