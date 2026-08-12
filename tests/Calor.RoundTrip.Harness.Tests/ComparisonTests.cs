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

    [Fact]
    public void ZeroTestRun_ReturnsIncomplete()
    {
        var empty = new TestRunResult { ExitCode = 0 };

        var result = Compare(empty, empty, new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.Incomplete, result.Status);
    }

    [Fact]
    public void AbortedTesthostWithoutFailures_ReturnsIncomplete()
    {
        var baseline = MakeTestRun("Test1:Passed");
        var aborted = new TestRunResult { ExitCode = 1 };

        var result = Compare(baseline, aborted, new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.Incomplete, result.Status);
    }

    [Fact]
    public void NonzeroExitWithCompleteStructuredFailures_ReportsRegressions()
    {
        var baseline = MakeTestRun("Test1:Passed", "Test2:Passed");
        var roundTrip = new TestRunResult
        {
            ExitCode = 1,
            TotalTests = 2,
            Passed = 1,
            Failed = 1,
            Results =
            [
                new TestResult { TestName = "Test1", Outcome = "Passed" },
                new TestResult { TestName = "Test2", Outcome = "Failed" },
            ],
        };

        var result = Compare(
            baseline,
            roundTrip,
            new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.MajorRegressions, result.Status);
        Assert.Equal("Test2", Assert.Single(result.Regressions).TestName);
    }

    [Fact]
    public void ReducedTestInventory_ReturnsIncomplete()
    {
        var baseline = MakeTestRun("Test1:Passed", "Test2:Passed");
        var roundTrip = MakeTestRun("Test1:Passed");

        var result = Compare(baseline, roundTrip, new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.Incomplete, result.Status);
    }

    [Fact]
    public void PassingTestThatBecomesSkipped_IsARegression()
    {
        var baseline = MakeTestRun("Test1:Passed", "Test2:Passed");
        var roundTrip = MakeTestRun("Test1:Passed", "Test2:Skipped");

        var result = Compare(baseline, roundTrip, new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.MajorRegressions, result.Status);
        Assert.Equal("Skipped", Assert.Single(result.Regressions).Outcome);
    }

    [Fact]
    public void IdentitylessConsoleFallback_IsIncomplete()
    {
        var baseline = new TestRunResult
        {
            TotalTests = 1,
            Passed = 1,
            UsedConsoleFallback = true,
        };
        var roundTrip = new TestRunResult
        {
            TotalTests = 1,
            Passed = 1,
            UsedConsoleFallback = true,
        };

        var result = Compare(baseline, roundTrip, new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.Incomplete, result.Status);
    }

    [Fact]
    public void DuplicateTheoryIdentities_AreIncomplete()
    {
        var baseline = MakeRun(
            ("tests.dll", "Suite", "SameTheoryRow", "Passed"),
            ("tests.dll", "Suite", "SameTheoryRow", "Passed"));
        var roundTrip = MakeRun(
            ("tests.dll", "Suite", "SameTheoryRow", "Passed"),
            ("tests.dll", "Suite", "SameTheoryRow", "Failed"));

        var result = Compare(baseline, roundTrip, new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.Incomplete, result.Status);
        Assert.Empty(result.Regressions);
    }

    [Fact]
    public void SameTheoryDisplayName_WithDistinctCaseIds_DetectsRegression()
    {
        var baseline = MakeTheoryRun(("row-1", "Passed"), ("row-2", "Passed"));
        var roundTrip = MakeTheoryRun(("row-1", "Passed"), ("row-2", "Failed"));

        var result = Compare(
            baseline,
            roundTrip,
            new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.MajorRegressions, result.Status);
        Assert.Equal("row-2", Assert.Single(result.Regressions).TestCaseId);
    }

    [Fact]
    public void SkippedTestThatBecomesFailed_IsARegression()
    {
        var baseline = MakeTestRun("Test1:Skipped");
        var roundTrip = MakeTestRun("Test1:Failed");

        var result = Compare(baseline, roundTrip, new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.MajorRegressions, result.Status);
        Assert.Equal("Failed", Assert.Single(result.Regressions).Outcome);
    }

    [Fact]
    public void FailedTestThatBecomesSkipped_IsARegression()
    {
        var baseline = MakeTestRun("Test1:Failed");
        var roundTrip = MakeTestRun("Test1:Skipped");

        var result = Compare(baseline, roundTrip, new BuildResult { Succeeded = true });

        Assert.Equal(ComparisonStatus.MajorRegressions, result.Status);
        Assert.Equal("Skipped", Assert.Single(result.Regressions).Outcome);
    }

    [Fact]
    public void DuplicateDisplayNames_AcrossAssemblies_NotConflated()
    {
        // Same display name "SharedName" in two assemblies: passing in alpha,
        // pre-existing failure in beta. Name-only matching would flag a false
        // regression (passed-in-baseline ∩ failed-in-roundtrip on display name).
        var baseline = MakeRun(
            ("alpha.tests.dll", "Alpha.Suite", "SharedName", "Passed"),
            ("beta.tests.dll", "Beta.Suite", "SharedName", "Failed"));
        var roundTrip = MakeRun(
            ("alpha.tests.dll", "Alpha.Suite", "SharedName", "Passed"),
            ("beta.tests.dll", "Beta.Suite", "SharedName", "Failed"));
        var build = new BuildResult { Succeeded = true };

        var result = Compare(baseline, roundTrip, build);

        Assert.Empty(result.Regressions);
        Assert.Equal(ComparisonStatus.Pass, result.Status);
        Assert.Equal(1, result.PreExistingFailures);
    }

    [Fact]
    public void RegressionInOneAssembly_DetectedByIdentity()
    {
        var baseline = MakeRun(
            ("alpha.tests.dll", "Alpha.Suite", "SharedName", "Passed"),
            ("beta.tests.dll", "Beta.Suite", "SharedName", "Passed"));
        var roundTrip = MakeRun(
            ("alpha.tests.dll", "Alpha.Suite", "SharedName", "Failed"),
            ("beta.tests.dll", "Beta.Suite", "SharedName", "Passed"));
        var build = new BuildResult { Succeeded = true };

        var result = Compare(baseline, roundTrip, build);

        var regression = Assert.Single(result.Regressions);
        Assert.Equal("alpha.tests.dll", regression.Assembly);
    }

    private static TestRunResult MakeRun(params (string Assembly, string ClassName, string Name, string Outcome)[] entries)
    {
        var results = entries.Select(e => new TestResult
        {
            TestName = e.Name,
            Assembly = e.Assembly,
            ClassName = e.ClassName,
            ExecutorUri = "executor://xunit",
            Outcome = e.Outcome,
        }).ToList();

        return new TestRunResult
        {
            ExitCode = 0,
            TotalTests = results.Count,
            Passed = results.Count(r => r.Outcome == "Passed"),
            Failed = results.Count(r => r.Outcome == "Failed"),
            Results = results,
        };
    }

    private static TestRunResult MakeTheoryRun(
        params (string TestCaseId, string Outcome)[] entries)
    {
        var results = entries.Select(entry => new TestResult
        {
            Project = "Tests",
            Assembly = "tests.dll",
            ExecutorUri = "executor://xunit",
            FullyQualifiedName = "Suite.Theory",
            TestCaseId = entry.TestCaseId,
            TestName = "Suite.Theory(value: duplicate)",
            Outcome = entry.Outcome,
        }).ToList();
        return new TestRunResult
        {
            ExitCode = results.Any(result => result.Outcome == "Failed") ? 1 : 0,
            TotalTests = results.Count,
            Passed = results.Count(result => result.Outcome == "Passed"),
            Failed = results.Count(result => result.Outcome == "Failed"),
            Results = results,
        };
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
            ExitCode = 0,
            TotalTests = results.Count,
            Passed = results.Count(r => r.Outcome == "Passed"),
            Failed = results.Count(r => r.Outcome == "Failed"),
            Skipped = results.Count(r => r.Outcome is "NotExecuted" or "Skipped"),
            Results = results,
        };
    }
}
