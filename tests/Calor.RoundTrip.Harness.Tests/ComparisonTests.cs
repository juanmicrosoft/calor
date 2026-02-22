using System.Reflection;
using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

public class ComparisonTests
{
    // Access the private static method via reflection
    private static TestComparison Compare(TestRunResult? baseline, TestRunResult? roundTrip, BuildResult? build)
    {
        var method = typeof(RoundTripPipeline).GetMethod(
            "CompareTestResults",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (TestComparison)method!.Invoke(null, [baseline, roundTrip, build])!;
    }

    [Fact]
    public void NoRegressions_ReturnsPass()
    {
        var baseline = MakeTestRun("Test1:Passed", "Test2:Passed", "Test3:Passed");
        var roundTrip = MakeTestRun("Test1:Passed", "Test2:Passed", "Test3:Passed");
        var build = new BuildResult { Succeeded = true };

        var result = Compare(baseline, roundTrip, build);

        Assert.Equal(ComparisonStatus.Pass, result.Status);
        Assert.Empty(result.Regressions);
        Assert.Equal(3, result.BaselinePassed);
        Assert.Equal(3, result.RoundTripPassed);
    }

    [Fact]
    public void OneRegression_MinorRegressions()
    {
        var baseline = MakeTestRun(
            "Test1:Passed", "Test2:Passed", "Test3:Passed",
            "Test4:Passed", "Test5:Passed", "Test6:Passed",
            "Test7:Passed", "Test8:Passed", "Test9:Passed",
            "Test10:Passed", "Test11:Passed", "Test12:Passed",
            "Test13:Passed", "Test14:Passed", "Test15:Passed",
            "Test16:Passed", "Test17:Passed", "Test18:Passed",
            "Test19:Passed", "Test20:Passed", "Test21:Passed");

        // 1 out of 21 = ~4.8% < 5%
        var roundTrip = MakeTestRun(
            "Test1:Passed", "Test2:Passed", "Test3:Passed",
            "Test4:Passed", "Test5:Passed", "Test6:Passed",
            "Test7:Passed", "Test8:Passed", "Test9:Passed",
            "Test10:Passed", "Test11:Passed", "Test12:Passed",
            "Test13:Passed", "Test14:Passed", "Test15:Passed",
            "Test16:Passed", "Test17:Passed", "Test18:Passed",
            "Test19:Passed", "Test20:Passed", "Test21:Failed");
        var build = new BuildResult { Succeeded = true };

        var result = Compare(baseline, roundTrip, build);

        Assert.Equal(ComparisonStatus.MinorRegressions, result.Status);
        Assert.Single(result.Regressions);
        Assert.Equal("Test21", result.Regressions[0].TestName);
    }

    [Fact]
    public void ManyRegressions_MajorRegressions()
    {
        // 2 out of 3 = 66% > 5%
        var baseline = MakeTestRun("Test1:Passed", "Test2:Passed", "Test3:Passed");
        var roundTrip = MakeTestRun("Test1:Passed", "Test2:Failed", "Test3:Failed");
        var build = new BuildResult { Succeeded = true };

        var result = Compare(baseline, roundTrip, build);

        Assert.Equal(ComparisonStatus.MajorRegressions, result.Status);
        Assert.Equal(2, result.Regressions.Count);
    }

    [Fact]
    public void BuildFailed_ReturnsBuildFailed()
    {
        var baseline = MakeTestRun("Test1:Passed");
        var build = new BuildResult { Succeeded = false };

        var result = Compare(baseline, null, build);

        Assert.Equal(ComparisonStatus.BuildFailed, result.Status);
    }

    [Fact]
    public void PreExistingFailures_NotCountedAsRegressions()
    {
        var baseline = MakeTestRun("Test1:Passed", "Test2:Failed");
        var roundTrip = MakeTestRun("Test1:Passed", "Test2:Failed");
        var build = new BuildResult { Succeeded = true };

        var result = Compare(baseline, roundTrip, build);

        Assert.Equal(ComparisonStatus.Pass, result.Status);
        Assert.Empty(result.Regressions);
        Assert.Equal(1, result.PreExistingFailures);
    }

    [Fact]
    public void NewPasses_AreDetected()
    {
        var baseline = MakeTestRun("Test1:Passed", "Test2:Failed");
        var roundTrip = MakeTestRun("Test1:Passed", "Test2:Passed");
        var build = new BuildResult { Succeeded = true };

        var result = Compare(baseline, roundTrip, build);

        Assert.Equal(ComparisonStatus.Pass, result.Status);
        Assert.Single(result.NewPasses);
        Assert.Equal("Test2", result.NewPasses[0]);
    }

    [Fact]
    public void NullBaseline_ReturnsIncomplete()
    {
        var result = Compare(null, null, null);
        Assert.Equal(ComparisonStatus.Incomplete, result.Status);
    }

    private static TestRunResult MakeTestRun(params string[] entries)
    {
        var results = entries.Select(e =>
        {
            var parts = e.Split(':');
            return new TestResult { TestName = parts[0], Outcome = parts[1] };
        }).ToList();

        return new TestRunResult
        {
            ExitCode = results.Any(r => r.Outcome == "Failed") ? 1 : 0,
            TotalTests = results.Count,
            Passed = results.Count(r => r.Outcome == "Passed"),
            Failed = results.Count(r => r.Outcome == "Failed"),
            Skipped = results.Count(r => r.Outcome is "NotExecuted" or "Skipped"),
            Results = results,
        };
    }
}
