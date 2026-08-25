using System.Reflection;
using System.Text.Json;
using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// End-to-end shape of the CI failures on jobs 97677469389 / 97668600448 (flake
/// regressed in the round-trip leg) and 97670944824 (flake failed in the
/// baseline leg): real comparison, real exit policy, real report generator, with
/// the MediatR allowlist entry in its canonical form. The gate must not block on
/// either leg, and the report must still name the ignored flake.
/// </summary>
public class UpstreamFlakeGateTests
{
    private const string Flake =
        "MediatR.Tests.GenericRequestHandlerTests.ShouldThrowExceptionWhenTimeoutOccurs";
    private const string Other = "MediatR.Tests.Other.SomethingElse";

    [Fact]
    public void FlakeRegressesInRoundTripLeg_GateDoesNotBlock_ReportNamesIt()
    {
        var report = BuildReport(
            baseline: Run((Flake, "Passed"), (Other, "Passed")),
            roundTrip: Run((Flake, "Failed"), (Other, "Passed")));

        Assert.Equal(1, report.RoundTripTests!.ExitCode);
        Assert.Equal(ComparisonStatus.Pass, report.Comparison!.Status);
        Assert.Empty(RoundTripExitPolicy.GetFailureReasons(report));

        var md = ReportGenerator.GenerateMarkdown(report);
        Assert.Contains("| Regressions | **0** (1 ignored upstream flake) |", md);
        Assert.Contains("- Regressions: 0 (1 ignored upstream flake); new passes: 0; status: Pass", md);
        Assert.Contains("## Upstream Flake Regressions (Not Blocking)", md);
        Assert.Contains(Flake, md);
        Assert.DoesNotContain("## Regressions\n", md);
        Assert.DoesNotContain("Regressions: 1", md);

        var json = JsonDocument.Parse(ReportGenerator.GenerateJson(report)).RootElement;
        Assert.Equal("pass", json.GetProperty("verdict").GetString());
        Assert.Equal(0, json.GetProperty("regressions").GetInt32());
        var tests = json.GetProperty("fidelity").GetProperty("tests");
        Assert.Equal(0, tests.GetProperty("regressions").GetInt32());
        Assert.Equal(1, tests.GetProperty("ignored_flaky_regressions").GetInt32());
        var ignored = json.GetProperty("ignored_upstream_flakes");
        Assert.Equal(Flake, Assert.Single(ignored.GetProperty("regressions").EnumerateArray()).GetString());
        Assert.Empty(ignored.GetProperty("baseline_failures").EnumerateArray());
    }

    [Fact]
    public void FlakeFailsInBaselineLeg_GateDoesNotBlock_ReportNamesIt()
    {
        var report = BuildReport(
            baseline: Run((Flake, "Failed"), (Other, "Passed")),
            roundTrip: Run((Flake, "Passed"), (Other, "Passed")));

        Assert.Equal(1, report.Baseline!.ExitCode);
        Assert.Equal(ComparisonStatus.Pass, report.Comparison!.Status);
        Assert.Empty(RoundTripExitPolicy.GetFailureReasons(report));

        var md = ReportGenerator.GenerateMarkdown(report);
        Assert.Contains("## Upstream Flake Baseline Failures (Not Blocking)", md);
        Assert.Contains("| Baseline failures ignored as upstream flakes | 1 |", md);
        Assert.Contains(Flake, md);

        var json = JsonDocument.Parse(ReportGenerator.GenerateJson(report)).RootElement;
        Assert.Equal("pass", json.GetProperty("verdict").GetString());
        Assert.Equal(1, json.GetProperty("fidelity").GetProperty("tests")
            .GetProperty("ignored_flaky_baseline_failures").GetInt32());
        Assert.Equal(Flake, Assert.Single(json.GetProperty("ignored_upstream_flakes")
            .GetProperty("baseline_failures").EnumerateArray()).GetString());
    }

    [Fact]
    public void UnlistedFailure_StillBlocksOnBothLegs()
    {
        // The allowlist must not widen into "ignore any red": an unlisted failure
        // keeps both the exit-code gates and the comparison verdict blocking.
        var roundTripLeg = BuildReport(
            baseline: Run((Flake, "Passed"), (Other, "Passed")),
            roundTrip: Run((Flake, "Passed"), (Other, "Failed")));
        var reasons = RoundTripExitPolicy.GetFailureReasons(roundTripLeg);
        Assert.Equal(ComparisonStatus.MajorRegressions, roundTripLeg.Comparison!.Status);
        Assert.Contains("test comparison is MajorRegressions", reasons);
        Assert.Contains("round-trip tests exited with 1", reasons);

        var baselineLeg = BuildReport(
            baseline: Run((Flake, "Passed"), (Other, "Failed")),
            roundTrip: Run((Flake, "Passed"), (Other, "Passed")));
        reasons = RoundTripExitPolicy.GetFailureReasons(baselineLeg);
        Assert.Contains("test comparison is Incomplete", reasons);
        Assert.Contains("baseline tests exited with 1", reasons);
    }

    [Theory]
    [InlineData(-1)]   // process timeout
    [InlineData(2)]    // anything that is not "some tests failed"
    public void NonTestFailureExitCode_IsNotExplainedByFlakes(int exitCode)
    {
        var run = new TestRunResult
        {
            ExitCode = exitCode, TotalTests = 2, Passed = 1, Failed = 1,
            Results = [Result(Flake, "Failed"), Result(Other, "Passed")],
        };
        var flakes = new List<TestResult> { run.Results[0] };

        Assert.False(RoundTripExitPolicy.IsTestExitExplainedByKnownFlakes(run, flakes));
    }

    [Fact]
    public void ConsoleFallbackOrParseErrors_AreNotExplainedByFlakes()
    {
        var fallback = new TestRunResult
        {
            ExitCode = 1, TotalTests = 2, Passed = 1, Failed = 1, UsedConsoleFallback = true,
        };
        var unparseable = new TestRunResult
        {
            ExitCode = 1, TotalTests = 2, Passed = 1, Failed = 1, ParseErrors = ["x.trx: bad"],
            Results = [Result(Flake, "Failed"), Result(Other, "Passed")],
        };
        var flakes = new List<TestResult> { Result(Flake, "Failed") };

        Assert.False(RoundTripExitPolicy.IsTestExitExplainedByKnownFlakes(fallback, flakes));
        Assert.False(RoundTripExitPolicy.IsTestExitExplainedByKnownFlakes(unparseable, flakes));
        Assert.False(RoundTripExitPolicy.IsTestExitExplainedByKnownFlakes(null, flakes));
        Assert.False(RoundTripExitPolicy.IsTestExitExplainedByKnownFlakes(unparseable, null));
    }

    private static RoundTripReport BuildReport(TestRunResult baseline, TestRunResult roundTrip)
    {
        var config = new RoundTripConfig
        {
            ProjectName = "MediatR",
            OriginalProjectPath = "/nonexistent",
            LibrarySourceRelativePath = "src",
            SolutionOrProjectFile = "test.csproj",
            ExpectedFlakyTestFullyQualifiedNames = [Flake],
        };
        var compare = typeof(RoundTripPipeline).GetMethod(
            "CompareTestResults", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(compare);
        var build = new BuildResult { Succeeded = true, ExitCode = 0 };
        var comparison = (TestComparison)compare!.Invoke(
            null, [config, baseline, roundTrip, build])!;

        var report = new RoundTripReport
        {
            ProjectName = "MediatR",
            CalorVersion = "0.14.3",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            FinishedAt = DateTimeOffset.UtcNow,
            BaselineBuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            BuildResult = build,
            Baseline = baseline,
            RoundTripTests = roundTrip,
            FileResults = [new() { FilePath = "Lib/A.cs", Status = FileStatus.Replaced, ConversionRate = 100 }],
            Comparison = comparison,
        };
        report.Fidelity = ProjectFidelity.Compute(report);
        return report;
    }

    private static TestRunResult Run(params (string Fqn, string Outcome)[] entries)
    {
        var results = entries.Select(e => Result(e.Fqn, e.Outcome)).ToList();
        return new TestRunResult
        {
            // `dotnet test` exits 1 when any test fails.
            ExitCode = results.Any(r => r.Outcome == "Failed") ? 1 : 0,
            TotalTests = results.Count,
            Passed = results.Count(r => r.Outcome == "Passed"),
            Failed = results.Count(r => r.Outcome == "Failed"),
            Results = results,
            TrxFiles = ["TestResults/roundtrip.trx"],
        };
    }

    private static TestResult Result(string fqn, string outcome) => new()
    {
        TestName = fqn,
        FullyQualifiedName = fqn,
        ClassName = fqn[..fqn.LastIndexOf('.')],
        Assembly = "MediatR.Tests.dll",
        ExecutorUri = "executor://xunit/VsTestRunner2/netcoreapp",
        TestCaseId = fqn.GetHashCode().ToString(),
        Outcome = outcome,
        ErrorMessage = outcome == "Failed" ? "Shouldly.ShouldAssertException" : null,
    };
}
