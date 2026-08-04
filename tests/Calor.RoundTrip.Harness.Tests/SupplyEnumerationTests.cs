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

    /// <summary>A null-guard removal candidate — the operator that supplies most of D-5's numerator.</summary>
    private const string OneNullDerefCandidate = """
        namespace S;
        public class C
        {
            public string Use(string? s)
            {
                if (s != null)
                {
                    return s.Trim();
                }
                return "";
            }
        }
        """;

    [Fact]
    public async Task Guard_removal_candidates_flow_through_the_pre_pass()
    {
        // Every other behavioural test uses an EffectViolation fixture. NullDeref supplies 6 of the
        // 7 with-loss candidates in the real run and DECIDES D-5, so it needs its own path covered.
        var result = await Run(
            [Project("P")],
            new() { ["P"] = [File("a.cs", FileStatus.Replaced, 0), File("b.cs", FileStatus.Replaced, 2)] },
            new() { ["a.cs"] = OneNullDerefCandidate, ["b.cs"] = OneNullDerefCandidate });

        var p = Assert.Single(result.Projects);
        Assert.Equal(1, p.SupplyNative);
        Assert.Equal(1, p.SupplyWithLoss);
        Assert.Contains("NullDeref", p.NativeByOperator.Keys);
    }

    [Theory]
    // These are the site-predicate boundaries that produce the real corpus ceiling. Pinning them as
    // DELIBERATE makes the operator's narrowness visible to anyone reading a supply number, rather
    // than something a reviewer has to rediscover with an out-of-tree probe.
    [InlineData("public int F() => _n + 1;", "expression-bodied methods are not visited")]
    [InlineData("public int P { get { return _n; } }", "property getters are not visited")]
    [InlineData("public string F() { System.Console.WriteLine(\"x\"); return \"s\"; }", "non-int/long return types are out of scope")]
    public async Task Site_predicate_boundaries_yield_no_candidates(string member, string why)
    {
        var source = $$"""
            namespace S;
            public class C
            {
                private int _n;
                {{member}}
            }
            """;

        var result = await Run(
            [Project("P")],
            new() { ["P"] = [File("a.cs", FileStatus.Replaced, 0)] },
            new() { ["a.cs"] = source });

        Assert.Equal(0, Assert.Single(result.Projects).SupplyNative);
        Assert.True(true, why);
    }

    [Fact]
    public async Task Unconverted_files_are_counted_as_supply_lost_to_the_converter()
    {
        // The distinction the whole ceiling turns on: a candidate in a file the converter could not
        // handle is lost to CONVERTER FIDELITY, not absent from the corpus.
        var result = await Run(
            [Project("P")],
            new()
            {
                ["P"] =
                [
                    File("native.cs", FileStatus.Replaced, 0),
                    File("broken.cs", FileStatus.CompileError, 0),
                    File("reverted.cs", FileStatus.Reverted, 0),
                ]
            },
            new()
            {
                ["native.cs"] = OneEffectCandidate,
                ["broken.cs"] = OneEffectCandidate,
                ["reverted.cs"] = OneEffectCandidate,
            });

        var p = Assert.Single(result.Projects);
        Assert.Equal(1, p.SupplyNative);
        Assert.Equal(2, p.SupplyLostToConversion);
        Assert.Equal(3, p.SupplyTotal);
        Assert.Equal(2, p.UnconvertedFiles);
    }

    [Fact]
    public async Task Unreadable_sources_are_counted_not_silently_dropped()
    {
        var result = await Run(
            [Project("P")],
            new() { ["P"] = [File("gone.cs", FileStatus.Replaced, 0)] },
            new());

        Assert.Equal(1, Assert.Single(result.Projects).UnreadableSources);
    }

    [Fact]
    public async Task D5_reports_SPLIT_when_one_operator_carries_the_pooled_verdict()
    {
        // Mirrors the real run: the pooled ratio says ACCEPT, but only because one operator supplies
        // the numerator. Excluding it flips the verdict, so the pre-committed threshold has not
        // actually settled the question.
        var result = await Run(
            [Project("P")],
            new()
            {
                ["P"] =
                [
                    File("eff-native1.cs", FileStatus.Replaced, 0), File("eff-native2.cs", FileStatus.Replaced, 0),
                    File("null-loss1.cs", FileStatus.Replaced, 1), File("null-loss2.cs", FileStatus.Replaced, 1),
                ]
            },
            new()
            {
                ["eff-native1.cs"] = OneEffectCandidate, ["eff-native2.cs"] = OneEffectCandidate,
                ["null-loss1.cs"] = OneNullDerefCandidate, ["null-loss2.cs"] = OneNullDerefCandidate,
            });

        Assert.Equal(1.0, result.PooledRatio);          // pooled → ACCEPT
        Assert.Equal("NullDeref", result.DominantNumeratorOperator!.Value.Operator);
        Assert.Equal(0.0, result.RatioExcludingDominant); // without it → REJECT
        Assert.StartsWith("SPLIT", result.D5Verdict);
    }

    [Fact]
    public void Report_renders_the_UNDECIDABLE_path_without_a_ratio()
    {
        var result = new SupplyEnumeration.Result
        {
            Projects =
            [
                new SupplyEnumeration.ProjectSupply
                {
                    ProjectName = "P", NativeFiles = 0, WithLossFiles = 1, UnconvertedFiles = 0,
                    SupplyNative = 0, SupplyWithLoss = 2, SupplyLostToConversion = 0, SupplyTotal = 2,
                    UnreadableSources = 0,
                    NativeByOperator = new Dictionary<string, int>(),
                    WithLossByOperator = new Dictionary<string, int> { ["EffectViolation"] = 2 },
                    NativeByFile = new Dictionary<string, int>(),
                }
            ]
        };

        var md = SupplyEnumerationReport.Render(result);

        Assert.Contains("UNDECIDABLE", md);
        Assert.Contains("n/a", md);
    }

    [Fact]
    public void Report_warns_conspicuously_about_unreadable_sources()
    {
        var result = new SupplyEnumeration.Result
        {
            Projects =
            [
                new SupplyEnumeration.ProjectSupply
                {
                    ProjectName = "P", NativeFiles = 1, WithLossFiles = 0, UnconvertedFiles = 0,
                    SupplyNative = 1, SupplyWithLoss = 0, SupplyLostToConversion = 0, SupplyTotal = 1,
                    UnreadableSources = 3,
                    NativeByOperator = new Dictionary<string, int> { ["EffectViolation"] = 1 },
                    WithLossByOperator = new Dictionary<string, int>(),
                    NativeByFile = new Dictionary<string, int> { ["a.cs"] = 1 },
                }
            ]
        };

        Assert.Contains("3 source file(s) could not be read", SupplyEnumerationReport.Render(result));
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
                    ProjectName = "P", NativeFiles = 1, WithLossFiles = 1, UnconvertedFiles = 0,
                    SupplyNative = 2, SupplyWithLoss = 1, SupplyLostToConversion = 0, SupplyTotal = 3,
                    UnreadableSources = 0,
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
