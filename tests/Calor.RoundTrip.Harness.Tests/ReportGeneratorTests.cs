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

    [Fact]
    public void GenerateJson_IncludesFidelityDimensions()
    {
        var report = CreatePassingReport();
        report.FileResults.Add(new FileConversionResult
        {
            FilePath = "Lib/Rev.cs",
            Status = FileStatus.Reverted,
            RevertReason = "build-recovery round 1",
        });
        report.Fidelity = ProjectFidelity.Compute(report);
        var json = ReportGenerator.GenerateJson(report);

        var doc = JsonDocument.Parse(json);
        var fidelity = doc.RootElement.GetProperty("fidelity");

        var coverage = fidelity.GetProperty("coverage");
        Assert.Equal(4, coverage.GetProperty("total_convertible_files").GetInt32());
        Assert.Equal(1, coverage.GetProperty("reverted").GetInt32());
        Assert.Equal(0.5, coverage.GetProperty("coverage_fraction").GetDouble());

        var build = fidelity.GetProperty("build");
        Assert.True(build.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, build.GetProperty("recovery_reverted_files").GetInt32());

        var tests = fidelity.GetProperty("tests");
        Assert.Equal(10, tests.GetProperty("baseline_total").GetInt32());
        Assert.Equal(0, tests.GetProperty("inventory_delta").GetInt32());
        Assert.Equal("Pass", tests.GetProperty("comparison_status").GetString());

        // Per-file detail carries revert visibility
        var detail = doc.RootElement.GetProperty("file_detail");
        var reverted = detail.EnumerateArray().Single(e => e.GetProperty("status").GetString() == "Reverted");
        Assert.Equal("build-recovery round 1", reverted.GetProperty("revert_reason").GetString());
    }

    [Fact]
    public void GenerateMarkdown_IncludesFidelitySection_AndRevertedRow()
    {
        var report = CreatePassingReport();
        report.FileResults.Add(new FileConversionResult
        {
            FilePath = "Lib/Rev.cs",
            Status = FileStatus.Reverted,
            RevertReason = "build-recovery round 1",
            Errors = ["Reverted: build error in round-tripped output (recovery round 1)"],
        });
        report.Fidelity = ProjectFidelity.Compute(report);
        var md = ReportGenerator.GenerateMarkdown(report);

        Assert.Contains("## Fidelity (separated verdict dimensions)", md);
        Assert.Contains("### Conversion Coverage", md);
        Assert.Contains("### Build Outcome", md);
        Assert.Contains("### Test Outcome", md);
        Assert.Contains("REVERTED", md);
    }

    [Fact]
    public void InconclusiveRun_EmitsNoCoverageFraction()
    {
        // A run whose recovery build failed unattributably (timeout) must NOT emit a
        // coverage/native fraction — it would be spuriously inflated (M2 guard).
        var report = CreateInconclusiveReport();

        var json = ReportGenerator.GenerateJson(report);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("inconclusive", root.GetProperty("verdict").GetString());
        Assert.True(root.GetProperty("inconclusive").GetBoolean());
        // fidelity is nulled → serialized as absent-or-null (WhenWritingNull), never an
        // object with fractions. No coverage_fraction / native_fraction anywhere.
        Assert.False(root.TryGetProperty("fidelity", out var fid) && fid.ValueKind != JsonValueKind.Null,
            "fidelity must be absent or null for an inconclusive run");
        Assert.DoesNotContain("native_fraction", json);
        Assert.DoesNotContain("coverage_fraction", json);

        var md = ReportGenerator.GenerateMarkdown(report);
        Assert.Contains("INCONCLUSIVE", md);
        Assert.Contains("No coverage fraction is reported", md);
    }

    private static RoundTripReport CreateInconclusiveReport()
    {
        var report = new RoundTripReport
        {
            ProjectName = "TimeoutProject",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            FinishedAt = DateTimeOffset.UtcNow,
            // Files that WOULD produce a high native fraction if trusted.
            FileResults =
            [
                new() { FilePath = "Lib/A.cs", Status = FileStatus.Replaced },
                new() { FilePath = "Lib/B.cs", Status = FileStatus.Replaced },
                new() { FilePath = "Lib/C.cs", Status = FileStatus.Replaced },
            ],
            // Build failed with NO extractable error files (timeout signature).
            BuildResult = new BuildResult { Succeeded = false, ExitCode = -1, Errors = [] },
            Inconclusive = true,
            InconclusiveReason = "recovery build did not complete within the build timeout",
        };
        report.Comparison = new TestComparison { Status = ComparisonStatus.BuildFailed };
        report.Fidelity = ProjectFidelity.Compute(report);
        return report;
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
