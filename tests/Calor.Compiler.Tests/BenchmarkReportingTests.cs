using Calor.Compiler.Evaluation.Core;
using Calor.Compiler.Evaluation.Reports;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

public class BenchmarkReportingTests
{
    [Fact]
    public void OverallRatio_PreservesZeroValuedCategory()
    {
        var result = new EvaluationResult
        {
            Summary = new EvaluationSummary
            {
                CategoryAdvantages = new Dictionary<string, double>
                {
                    ["Comprehension"] = 0,
                    ["TokenEconomics"] = 4,
                },
            },
        };

        Assert.Equal(0, result.CalculateOverallAdvantage());
    }

    [Fact]
    public void MarkdownReport_LabelsLowerIsBetterRatioAsDirectionNormalized()
    {
        var metric = MetricResult.CreateLowerIsBetter(
            "TokenEconomics",
            "TokenCount",
            calorScore: 50,
            csharpScore: 100);
        var calorOnlyMetric = MetricResult.CreateHigherIsBetter(
            "InteropEffectCoverage",
            "ManifestCoverage",
            calorScore: 1,
            csharpScore: 0,
            new Dictionary<string, object> { ["isCalorOnly"] = true });
        var result = new EvaluationResult
        {
            BenchmarkCount = 1,
            Metrics = [metric, calorOnlyMetric],
            Summary = new EvaluationSummary
            {
                OverallCalorAdvantage = 2,
                CategoryAdvantages = new Dictionary<string, double>
                {
                    ["TokenEconomics"] = 2,
                    ["InteropEffectCoverage"] = 1,
                },
                CalorPassCount = 1,
                CSharpPassCount = 1,
            },
        };

        var report = new MarkdownReportGenerator().Generate(result);
        var json = new JsonReportGenerator().Generate(result);

        Assert.Contains("Legacy Composite Direction-Normalized Ratio", report);
        Assert.Contains("| TokenCount | 50.00 | 100.00 | 2.00x |", report);
        Assert.Contains("lower-is-better metrics whose raw score ratio is inverted", report);
        Assert.Contains("| InteropEffectCoverage | No comparison | Calor-only |", report);
        Assert.Contains("| ManifestCoverage | 1.00 | 0.00 | No comparison |", report);
        Assert.DoesNotContain("| InteropEffectCoverage | 1.00x | Tie |", report);
        Assert.DoesNotContain("Overall Winner", report);
        Assert.DoesNotContain("Overall Advantage", report);
        Assert.DoesNotContain("Recommendations", report);
        Assert.DoesNotContain("Compilation Success", report);
        Assert.Contains("\"overallDirectionNormalizedRatio\": 2", json);
        Assert.Contains("\"overallCalorAdvantage\": 2", json);
        Assert.Contains("\"directionNormalizedRatio\": 2", json);
        Assert.Contains("\"advantageRatio\": 2", json);
    }

    [Fact]
    public void ConsoleReport_LabelsLowerIsBetterRatioAsDirectionNormalized()
    {
        var metric = MetricResult.CreateLowerIsBetter(
            "TokenEconomics",
            "TokenCount",
            calorScore: 50,
            csharpScore: 100);
        var caseResult = new BenchmarkCaseResult
        {
            CaseId = "case-1",
            FileName = "sample",
            Metrics = [metric],
            CalorSuccess = true,
            CSharpSuccess = true,
        };
        var result = new FullBenchmarkResult
        {
            CaseResult = caseResult,
            Summary = new EvaluationSummary
            {
                OverallCalorAdvantage = 2,
                CategoryAdvantages = new Dictionary<string, double>
                {
                    ["TokenEconomics"] = 2,
                },
            },
        };

        var output = BenchmarkIntegration.FormatConsoleOutput(result, "sample.calr", "sample.cs", verbose: true);

        Assert.Contains("Legacy composite direction-normalized ratio: 2.00x", output);
        Assert.Contains("Calor raw=50, C# raw=100, normalized=2.00x", output);
        Assert.Contains("lower-is-better metrics invert the raw score ratio", output);
        Assert.DoesNotContain("Overall Calor Advantage", output);
    }

    [Fact]
    public void CalorOnlyMetrics_AreExcludedFromComparisonSummaryAndJson()
    {
        var metric = MetricResult.CreateHigherIsBetter(
            "InteropEffectCoverage",
            "ManifestCoverage",
            calorScore: 1,
            csharpScore: 0,
            details: new Dictionary<string, object> { ["isCalorOnly"] = true });
        var caseResult = new BenchmarkCaseResult
        {
            CaseId = "interop",
            FileName = "interop",
            Metrics = [metric],
            CalorSuccess = true,
            CSharpSuccess = true,
        };
        var result = new FullBenchmarkResult
        {
            CaseResult = caseResult,
            Summary = new EvaluationSummary(),
        };

        var markdown = BenchmarkIntegration.GenerateMarkdownReport(result);
        var json = BenchmarkIntegration.GenerateJsonReport(result);
        var console = BenchmarkIntegration.FormatConsoleOutput(
            result,
            "interop.calr",
            "interop.cs");

        Assert.Contains("Comparable Direction-Normalized Ratios:** none", markdown);
        Assert.Contains("No comparison", markdown);
        Assert.Contains("\"isCalorOnly\": true", json);
        Assert.DoesNotContain("\"directionNormalizedRatio\"", json);
        Assert.DoesNotContain("\"advantageRatio\"", json);
        Assert.DoesNotContain("\"overallDirectionNormalizedRatio\"", json);
        Assert.DoesNotContain("\"overallCalorAdvantage\"", json);
        Assert.Contains("No comparable metric ratios", console);
        Assert.DoesNotContain("1.00x", console);
    }

    [Fact]
    public async Task ZeroRatioComparableMetric_RemainsAComparison()
    {
        var root = FindRepoRoot();
        var calor = await File.ReadAllTextAsync(
            Path.Combine(root, "tests", "TestData", "Benchmarks", "TokenEconomics", "Abs.calr"));
        var csharp = await File.ReadAllTextAsync(
            Path.Combine(root, "tests", "TestData", "Benchmarks", "TokenEconomics", "Abs.cs"));

        var result = await BenchmarkIntegration.RunFullBenchmarkAsync(
            csharp,
            calor,
            category: "Comprehension");
        var console = BenchmarkIntegration.FormatConsoleOutput(
            result,
            "Abs.calr",
            "Abs.cs");

        Assert.Contains("Comprehension", result.Summary.CategoryAdvantages.Keys);
        Assert.Equal(0, result.Summary.CategoryAdvantages["Comprehension"]);
        Assert.Contains("0.00x", console);
        Assert.DoesNotContain("No comparable metric ratios", console);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
