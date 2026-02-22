using System.Text.Json;
using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

public class ReportGeneratorTests
{
    [Fact]
    public void GenerateMarkdown_PassVerdict_ContainsPassText()
    {
        var report = CreatePassingReport();
        var md = ReportGenerator.GenerateMarkdown(report);

        Assert.Contains("PASS", md);
        Assert.Contains("0 regressions", md);
        Assert.Contains("## Pipeline Summary", md);
        Assert.Contains("## File-by-File Results", md);
    }

    [Fact]
    public void GenerateMarkdown_BuildFailed_ShowsBuildErrors()
    {
        var report = CreateBuildFailedReport();
        var md = ReportGenerator.GenerateMarkdown(report);

        Assert.Contains("build failed", md);
        Assert.Contains("## Build Errors", md);
        Assert.Contains("CS0246", md);
    }

    [Fact]
    public void GenerateMarkdown_WithRegressions_ShowsRegressionDetails()
    {
        var report = CreateRegressionReport();
        var md = ReportGenerator.GenerateMarkdown(report);

        Assert.Contains("## Regressions", md);
        Assert.Contains("FailingTest", md);
        Assert.Contains("Expected 5 but got 6", md);
    }

    [Fact]
    public void GenerateJson_ProducesValidJson()
    {
        var report = CreatePassingReport();
        var json = ReportGenerator.GenerateJson(report);

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("TestProject", root.GetProperty("project").GetString());
        Assert.Equal("pass", root.GetProperty("verdict").GetString());
        Assert.Equal(0, root.GetProperty("regressions").GetInt32());
        Assert.True(root.GetProperty("build_succeeded").GetBoolean());
        Assert.Equal(10, root.GetProperty("baseline").GetProperty("passed").GetInt32());
        Assert.Equal(10, root.GetProperty("round_trip").GetProperty("passed").GetInt32());
    }

    [Fact]
    public void GenerateJson_BuildFailed_HasCorrectVerdict()
    {
        var report = CreateBuildFailedReport();
        var json = ReportGenerator.GenerateJson(report);

        var doc = JsonDocument.Parse(json);
        Assert.Equal("buildfailed", doc.RootElement.GetProperty("verdict").GetString());
        Assert.False(doc.RootElement.GetProperty("build_succeeded").GetBoolean());
    }

    [Fact]
    public void GenerateJson_FileCounts_AreAccurate()
    {
        var report = CreatePassingReport();
        var json = ReportGenerator.GenerateJson(report);

        var doc = JsonDocument.Parse(json);
        var files = doc.RootElement.GetProperty("files");

        Assert.Equal(3, files.GetProperty("total").GetInt32());
        Assert.Equal(2, files.GetProperty("replaced").GetInt32());
        Assert.Equal(1, files.GetProperty("compile_error").GetInt32());
    }

    private static RoundTripReport CreatePassingReport() => new()
    {
        ProjectName = "TestProject",
        CalorVersion = "0.2.9",
        StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
        FinishedAt = DateTimeOffset.UtcNow,
        Baseline = new TestRunResult
        {
            ExitCode = 0, TotalTests = 10, Passed = 10, Failed = 0, Skipped = 0,
            Results = Enumerable.Range(1, 10).Select(i => new TestResult
            {
                TestName = $"Test{i}", Outcome = "Passed"
            }).ToList(),
        },
        FileResults =
        [
            new() { FilePath = "Lib/Foo.cs", Status = FileStatus.Replaced, ConversionRate = 100 },
            new() { FilePath = "Lib/Bar.cs", Status = FileStatus.Replaced, ConversionRate = 95 },
            new() { FilePath = "Lib/Baz.cs", Status = FileStatus.CompileError, ConversionRate = 80, Errors = ["Parse error"] },
        ],
        BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
        RoundTripTests = new TestRunResult
        {
            ExitCode = 0, TotalTests = 10, Passed = 10, Failed = 0, Skipped = 0,
            Results = Enumerable.Range(1, 10).Select(i => new TestResult
            {
                TestName = $"Test{i}", Outcome = "Passed"
            }).ToList(),
        },
        Comparison = new TestComparison
        {
            Status = ComparisonStatus.Pass,
            BaselineTotal = 10, BaselinePassed = 10,
            RoundTripTotal = 10, RoundTripPassed = 10,
        },
    };

    private static RoundTripReport CreateBuildFailedReport() => new()
    {
        ProjectName = "FailProject",
        CalorVersion = "0.2.9",
        StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
        FinishedAt = DateTimeOffset.UtcNow,
        Baseline = new TestRunResult { ExitCode = 0, TotalTests = 20, Passed = 20 },
        FileResults =
        [
            new() { FilePath = "Lib/X.cs", Status = FileStatus.Replaced, ConversionRate = 100 },
        ],
        BuildResult = new BuildResult
        {
            Succeeded = false, ExitCode = 1,
            Errors = ["X.cs(10,5): error CS0246: Type not found"],
        },
        Comparison = new TestComparison { Status = ComparisonStatus.BuildFailed },
    };

    private static RoundTripReport CreateRegressionReport() => new()
    {
        ProjectName = "RegProject",
        CalorVersion = "0.2.9",
        StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
        FinishedAt = DateTimeOffset.UtcNow,
        Baseline = new TestRunResult
        {
            ExitCode = 0, TotalTests = 5, Passed = 5,
            Results = Enumerable.Range(1, 5).Select(i => new TestResult
            {
                TestName = $"Test{i}", Outcome = "Passed"
            }).ToList(),
        },
        FileResults = [new() { FilePath = "Lib/A.cs", Status = FileStatus.Replaced, ConversionRate = 100 }],
        BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
        RoundTripTests = new TestRunResult
        {
            ExitCode = 1, TotalTests = 5, Passed = 4, Failed = 1,
            Results =
            [
                new() { TestName = "Test1", Outcome = "Passed" },
                new() { TestName = "Test2", Outcome = "Passed" },
                new() { TestName = "Test3", Outcome = "Passed" },
                new() { TestName = "Test4", Outcome = "Passed" },
                new() { TestName = "FailingTest", Outcome = "Failed", ErrorMessage = "Expected 5 but got 6" },
            ],
        },
        Comparison = new TestComparison
        {
            Status = ComparisonStatus.MinorRegressions,
            BaselineTotal = 5, BaselinePassed = 5,
            RoundTripTotal = 5, RoundTripPassed = 4,
            Regressions = [new() { TestName = "FailingTest", Outcome = "Failed", ErrorMessage = "Expected 5 but got 6" }],
        },
    };
}
