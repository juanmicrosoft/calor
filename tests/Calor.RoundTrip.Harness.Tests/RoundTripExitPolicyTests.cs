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
            Comparison = new TestComparison { Status = ComparisonStatus.Pass }
        };

        Assert.False(RoundTripExitPolicy.IsFailure(report));
    }
}
