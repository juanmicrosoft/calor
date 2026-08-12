using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

public class RoundTripExitPolicyTests
{
    [Fact]
    public void TypeInvalidRoundTripBuild_IsBlockingEvenWithoutRecordedRegressions()
    {
        var report = new RoundTripReport
        {
            ProjectName = "type-invalid-fixture",
            BuildResult = new BuildResult
            {
                Succeeded = false,
                ExitCode = 1,
                Errors = ["Broken.cs(1,1): error CS0029: Cannot implicitly convert type 'string' to 'int'"]
            },
            Comparison = new TestComparison
            {
                Status = ComparisonStatus.BuildFailed,
                Regressions = []
            }
        };

        Assert.True(RoundTripExitPolicy.IsFailure(report));
    }

    [Theory]
    [InlineData(ComparisonStatus.MinorRegressions)]
    [InlineData(ComparisonStatus.MajorRegressions)]
    [InlineData(ComparisonStatus.BuildFailed)]
    [InlineData(ComparisonStatus.Incomplete)]
    public void EveryNonPassVerdict_IsBlocking(ComparisonStatus status)
    {
        var report = new RoundTripReport
        {
            ProjectName = "fixture",
            Comparison = new TestComparison { Status = status }
        };

        Assert.True(RoundTripExitPolicy.IsFailure(report));
    }

    [Fact]
    public void PassVerdict_IsNotBlocking()
    {
        var report = new RoundTripReport
        {
            ProjectName = "fixture",
            BaselineBuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            Baseline = Run(),
            RoundTripTests = Run(),
            Comparison = new TestComparison { Status = ComparisonStatus.Pass },
        };
        report.Fidelity = ProjectFidelity.Compute(report);

        Assert.False(RoundTripExitPolicy.IsFailure(report));
    }

    [Fact]
    public void PassingTests_WithInsufficientCoverage_IsBlocking()
    {
        var report = new RoundTripReport
        {
            ProjectName = "fixture",
            MinimumCoverageFraction = 0.75,
            MinimumNativeFraction = 0.50,
            BaselineBuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            Baseline = Run(),
            RoundTripTests = Run(),
            FileResults =
            [
                new() { FilePath = "A.cs", Status = FileStatus.Replaced },
                new() { FilePath = "B.cs", Status = FileStatus.Reverted },
            ],
            Comparison = new TestComparison { Status = ComparisonStatus.Pass },
        };
        report.Fidelity = ProjectFidelity.Compute(report);

        Assert.True(RoundTripExitPolicy.IsFailure(report));
        Assert.Contains(
            RoundTripExitPolicy.GetFailureReasons(report),
            reason => reason.Contains("coverage", StringComparison.Ordinal));
    }

    [Fact]
    public void BaselineBuildFailure_IsBlockingEvenWhenLaterDimensionsPass()
    {
        var report = PassingReport();
        report.BaselineBuildResult = new BuildResult { Succeeded = false, ExitCode = 1 };

        Assert.True(RoundTripExitPolicy.IsFailure(report));
    }

    [Fact]
    public void MissingOrUnparseableTrx_IsBlocking()
    {
        var report = PassingReport();
        report.RoundTripTests = new TestRunResult
        {
            ExitCode = 0,
            TotalTests = 1,
            Passed = 1,
            ParseErrors = ["broken.trx: malformed XML"],
        };
        report.Fidelity = ProjectFidelity.Compute(report);

        var reasons = RoundTripExitPolicy.GetFailureReasons(report);

        Assert.Contains(reasons, reason => reason.Contains("no parseable TRX", StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.Contains("unparseable", StringComparison.Ordinal));
    }

    [Fact]
    public void ReducedInventory_IsBlocking()
    {
        var report = PassingReport();
        report.Baseline = Run(total: 2);
        report.RoundTripTests = Run(total: 1);
        report.Fidelity = ProjectFidelity.Compute(report);

        Assert.Contains(
            RoundTripExitPolicy.GetFailureReasons(report),
            reason => reason.Contains("inventory shrank", StringComparison.Ordinal));
    }

    [Fact]
    public void ConversionTimeout_IsBlockingEvenWhenCoverageThresholdPasses()
    {
        var report = PassingReport();
        report.FileResults =
        [
            new() { FilePath = "A.cs", Status = FileStatus.Replaced },
            new() { FilePath = "B.cs", Status = FileStatus.ConversionTimedOut },
        ];
        report.Fidelity = ProjectFidelity.Compute(report);

        Assert.Contains(
            RoundTripExitPolicy.GetFailureReasons(report),
            reason => reason.Contains("conversion(s) timed out", StringComparison.Ordinal));
    }

    [Fact]
    public void RevertedBuildBreakingFile_IsAlwaysBlocking()
    {
        var report = PassingReport();
        report.FileResults =
        [
            new()
            {
                FilePath = "Broken.cs",
                Status = FileStatus.Reverted,
                RevertReason = "build-recovery round 1"
            },
        ];
        report.Fidelity = ProjectFidelity.Compute(report);

        Assert.Contains(
            RoundTripExitPolicy.GetFailureReasons(report),
            reason => reason.Contains("were reverted", StringComparison.Ordinal));
    }

    private static RoundTripReport PassingReport()
    {
        var report = new RoundTripReport
        {
            ProjectName = "fixture",
            BaselineBuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            Baseline = Run(),
            RoundTripTests = Run(),
            Comparison = new TestComparison { Status = ComparisonStatus.Pass },
        };
        report.Fidelity = ProjectFidelity.Compute(report);
        return report;
    }

    private static TestRunResult Run(int total = 1) => new()
    {
        ExitCode = 0,
        TotalTests = total,
        Passed = total,
        TrxFiles = ["results.trx"],
    };
}
