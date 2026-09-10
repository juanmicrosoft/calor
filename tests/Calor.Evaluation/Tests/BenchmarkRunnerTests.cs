using Calor.Evaluation.Benchmarks;
using Calor.Evaluation.Core;
using Calor.Evaluation.Reports;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Integration tests for the benchmark runner.
/// </summary>
public class BenchmarkRunnerTests
{
    #region BenchmarkRunner Tests

    [Fact]
    public async Task BenchmarkRunner_RunFromSource_ReturnsValidResult()
    {
        // Arrange
        var runner = new BenchmarkRunner();
        var calor = @"§M{m001:Test}
  §F{f001:Hello:pub}
      §O{void}";
        var csharp = @"namespace Test { public class TestModule { public void Hello() { } } }";

        // Act
        var result = await runner.RunFromSourceAsync(calor, csharp, "inline-test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("inline-test", result.CaseId);
        Assert.True(result.Metrics.Count > 0, "Should have metrics");
        Assert.True(result.CalorSuccess, "Calor should compile");
        Assert.True(result.CSharpSuccess, "C# should compile");
    }

    [Fact]
    public async Task BenchmarkRunner_RunAll_ProcessesManifest()
    {
        // Arrange
        var runner = new BenchmarkRunner();
        var manifest = CreateTestManifest();

        // Act
        var result = await runner.RunAllAsync(manifest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(manifest.Benchmarks.Count, result.BenchmarkCount);
        Assert.NotNull(result.Summary);
    }

    [Fact]
    public async Task BenchmarkRunner_RunCategory_FiltersCorrectly()
    {
        // Arrange
        var runner = new BenchmarkRunner(new BenchmarkRunnerOptions
        {
            Categories = new List<string> { "TokenEconomics" }
        });
        var manifest = CreateTestManifest();

        // Act
        var result = await runner.RunAllAsync(manifest);

        // Assert
        Assert.All(result.Metrics, m =>
            Assert.True(m.Category == "TokenEconomics" || m.Category == "Error",
                "Should only have TokenEconomics metrics"));
    }

    [Fact]
    public void BenchmarkRunner_GetCalculators_ReturnsStaticCalculators()
    {
        // Arrange
        var runner = new BenchmarkRunner();

        // Act
        var calculators = runner.GetCalculators();

        // Assert - 8 calculators: 7 static + 1 Correctness
        // LLM-based metrics (TaskCompletion, Safety, EffectDiscipline) require
        // an LLM provider and are run via dedicated commands
        Assert.Equal(8, calculators.Count);
        // Static metrics
        Assert.Contains(calculators, c => c.Category == "TokenEconomics");
        Assert.Contains(calculators, c => c.Category == "GenerationAccuracy");
        Assert.Contains(calculators, c => c.Category == "Comprehension");
        Assert.Contains(calculators, c => c.Category == "EditPrecision");
        Assert.Contains(calculators, c => c.Category == "ErrorDetection");
        Assert.Contains(calculators, c => c.Category == "InformationDensity");
        Assert.Contains(calculators, c => c.Category == "RefactoringStability");
        // Correctness uses code execution, not LLM generation
        Assert.Contains(calculators, c => c.Category == "Correctness");

        // LLM-based metrics are NOT included by default
        Assert.DoesNotContain(calculators, c => c.Category == "TaskCompletion");
        Assert.DoesNotContain(calculators, c => c.Category == "Safety");
        Assert.DoesNotContain(calculators, c => c.Category == "EffectDiscipline");
    }

    #endregion

    #region EvaluationResult Tests

    [Fact]
    public void EvaluationResult_CalculateOverallAdvantage_ComputesGeometricMean()
    {
        // Arrange
        var result = new EvaluationResult
        {
            Summary = new EvaluationSummary
            {
                CategoryAdvantages = new Dictionary<string, double>
                {
                    ["A"] = 2.0,
                    ["B"] = 2.0,
                    ["C"] = 2.0
                }
            }
        };

        // Act
        var overall = result.CalculateOverallAdvantage();

        // Assert
        Assert.Equal(2.0, overall, 2); // Geometric mean of 2,2,2 = 2
    }

    [Fact]
    public void EvaluationResult_CalculateOverallAdvantage_PreservesZero()
    {
        var result = new EvaluationResult
        {
            Summary = new EvaluationSummary
            {
                CategoryAdvantages = new Dictionary<string, double>
                {
                    ["A"] = 0,
                    ["B"] = 4
                }
            }
        };

        Assert.Equal(0, result.CalculateOverallAdvantage());
    }

    [Fact]
    public void CalculateSummary_PreservesZeroAndExcludesCalorOnlyMetrics()
    {
        var result = new EvaluationResult
        {
            Metrics =
            [
                new MetricResult("Comparable", "Zero", 0, 1, 0, new()),
                new MetricResult("Comparable", "Four", 4, 1, 4, new()),
                new MetricResult(
                    "CalorOnly",
                    "Coverage",
                    1,
                    0,
                    1,
                    new Dictionary<string, object> { ["isCalorOnly"] = true })
            ]
        };

        var summary = BenchmarkRunner.CalculateSummary(result);

        Assert.Equal(0, summary.CategoryAdvantages["Comparable"]);
        Assert.DoesNotContain("CalorOnly", summary.CategoryAdvantages.Keys);
        Assert.Equal(0, summary.OverallCalorAdvantage);
    }

    #endregion

    #region BenchmarkManifest Tests

    [Fact]
    public void BenchmarkManifest_GetByCategory_FiltersCorrectly()
    {
        // Arrange
        var manifest = CreateTestManifest();

        // Act
        var tokenBenchmarks = manifest.GetByCategory("TokenEconomics").ToList();

        // Assert
        Assert.All(tokenBenchmarks, b => Assert.Equal("TokenEconomics", b.Category));
    }

    [Fact]
    public void BenchmarkManifest_GetByLevel_FiltersCorrectly()
    {
        // Arrange
        var manifest = CreateTestManifest();

        // Act
        var level1 = manifest.GetByLevel(1).ToList();

        // Assert
        Assert.All(level1, b => Assert.Equal(1, b.Level));
    }

    [Fact]
    public void BenchmarkManifest_GetByFeature_FiltersCorrectly()
    {
        // Arrange
        var manifest = CreateTestManifest();

        // Act
        var withFunction = manifest.GetByFeature("function").ToList();

        // Assert
        Assert.All(withFunction, b => Assert.Contains("function", b.Features));
    }

    [Fact]
    public async Task BenchmarkManifest_SaveAndLoad_RoundTrips()
    {
        // Arrange
        var manifest = CreateTestManifest();
        var tempPath = Path.GetTempFileName();

        try
        {
            // Act
            await manifest.SaveAsync(tempPath);
            var loaded = await BenchmarkManifest.LoadAsync(tempPath);

            // Assert
            Assert.Equal(manifest.Version, loaded.Version);
            Assert.Equal(manifest.Benchmarks.Count, loaded.Benchmarks.Count);
            Assert.Equal(manifest.Benchmarks[0].Id, loaded.Benchmarks[0].Id);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    #endregion

    #region Report Generator Tests

    [Fact]
    public void JsonReportGenerator_Generate_ProducesValidJson()
    {
        // Arrange
        var generator = new JsonReportGenerator();
        var result = CreateTestResult();

        // Act
        var json = generator.Generate(result);

        // Assert
        Assert.NotEmpty(json);
        Assert.Contains("summary", json);
        Assert.Contains("categoryResults", json);
        Assert.Contains("overallCalorAdvantage", json);
    }

    [Fact]
    public void JsonReportGenerator_GenerateSummary_ProducesCompactOutput()
    {
        // Arrange
        var generator = new JsonReportGenerator();
        var result = CreateTestResult();

        // Act
        var json = generator.GenerateSummary(result);

        // Assert
        Assert.NotEmpty(json);
        Assert.Contains("summary", json);
        Assert.DoesNotContain("detailedResults", json);
    }

    [Fact]
    public void MarkdownReportGenerator_Generate_ProducesValidMarkdown()
    {
        // Arrange
        var generator = new MarkdownReportGenerator();
        var result = CreateTestResult();

        // Act
        var markdown = generator.Generate(result);

        // Assert
        Assert.NotEmpty(markdown);
        Assert.Contains("# Calor vs C# Evaluation Report", markdown);
        Assert.Contains("## Executive Summary", markdown);
        Assert.Contains("## Category Breakdown", markdown);
        Assert.Contains("## Interpretation Limits", markdown);
        Assert.Contains("Legacy Composite Direction-Normalized Ratio", markdown);
        Assert.Contains("Direction-normalized ratio", markdown);
        Assert.Contains("Favored language", markdown);
        Assert.Contains("Parse Check Results", markdown);
        Assert.Contains("Source Commit:** `HEAD`", markdown);
        Assert.Contains("Source-Declared Compiler Version", markdown);
        Assert.Contains("Statistical Runs:** 7", markdown);
        Assert.Contains("7 repetitions repeat deterministic observations", markdown);
        Assert.Contains("Calor parse", markdown);
        Assert.Contains("C# parse", markdown);
        Assert.Contains("Average Direction-Normalized Ratio", markdown);
        Assert.Contains("| TokenCount | 50.00 | 100.00 | 2.00x |", markdown);
        Assert.Contains("These checks do not build or execute", markdown);
        Assert.Contains("not all behaviorally equivalent", markdown);
        Assert.Contains(
            "not a measured language, agent-productivity, correctness, or safety advantage",
            markdown);
        Assert.DoesNotContain("Winner", markdown);
        Assert.DoesNotContain("Calor/C# ratio", markdown);
        Assert.DoesNotContain("higher calculator score", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Static Score Ratio", markdown);
        Assert.DoesNotContain("Overall Winner", markdown);
        Assert.DoesNotContain("Advantage Percentage", markdown);
        Assert.DoesNotContain("Avg Advantage", markdown);
        Assert.DoesNotContain("Calor OK", markdown);
        Assert.DoesNotContain("C# OK", markdown);
        Assert.DoesNotContain("Compilation Success", markdown);
        Assert.DoesNotContain("Calor excels", markdown);
        Assert.DoesNotContain("Recommendations", markdown);
    }

    [Fact]
    public async Task JsonReportGenerator_SaveAsync_CreatesFile()
    {
        // Arrange
        var generator = new JsonReportGenerator();
        var result = CreateTestResult();
        var tempPath = Path.GetTempFileName();

        try
        {
            // Act
            await generator.SaveAsync(result, tempPath);

            // Assert
            Assert.True(File.Exists(tempPath));
            var content = await File.ReadAllTextAsync(tempPath);
            Assert.Contains("summary", content);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    #endregion

    #region TestDataAdapter Tests

    [Fact]
    public async Task TestDataAdapter_CreateContextFromFiles_LoadsContent()
    {
        // Arrange
        var calorPath = Path.GetTempFileName();
        var csharpPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(calorPath, "§M{test} §/M");
            await File.WriteAllTextAsync(csharpPath, "class Test { }");

            // Act
            var context = await TestDataAdapter.CreateContextFromFilesAsync(
                calorPath, csharpPath, level: 2, features: new List<string> { "test" });

            // Assert
            Assert.Equal("§M{test} §/M", context.CalorSource);
            Assert.Equal("class Test { }", context.CSharpSource);
            Assert.Equal(2, context.Level);
            Assert.Contains("test", context.Features);
        }
        finally
        {
            File.Delete(calorPath);
            File.Delete(csharpPath);
        }
    }

    #endregion

    #region Helper Methods

    private static BenchmarkManifest CreateTestManifest()
    {
        return new BenchmarkManifest
        {
            Version = "1.0",
            Description = "Test manifest",
            Benchmarks = new List<BenchmarkEntry>
            {
                new()
                {
                    Id = "001",
                    Name = "Test1",
                    Category = "TokenEconomics",
                    CalorFile = "test1.calr",
                    CSharpFile = "test1.cs",
                    Level = 1,
                    Features = new List<string> { "function", "module" }
                },
                new()
                {
                    Id = "002",
                    Name = "Test2",
                    Category = "Comprehension",
                    CalorFile = "test2.calr",
                    CSharpFile = "test2.cs",
                    Level = 2,
                    Features = new List<string> { "function", "contracts" }
                }
            }
        };
    }

    private static EvaluationResult CreateTestResult()
    {
        return new EvaluationResult
        {
            BenchmarkCount = 2,
            CommitHash = "HEAD",
            StatisticalRunCount = 7,
            Metrics = new List<MetricResult>
            {
                new("TokenEconomics", "TokenCount", 50, 100, 2.0, new()),
                new("GenerationAccuracy", "CompileSuccess", 1.0, 1.0, 1.0, new()),
                new("Comprehension", "Clarity", 0.8, 0.6, 1.33, new())
            },
            Summary = new EvaluationSummary
            {
                OverallCalorAdvantage = 1.35,
                CategoryAdvantages = new Dictionary<string, double>
                {
                    ["TokenEconomics"] = 2.0,
                    ["GenerationAccuracy"] = 1.0,
                    ["Comprehension"] = 1.33
                },
                CalorPassCount = 2,
                CSharpPassCount = 2,
                TopCalorCategories = new List<string> { "TokenEconomics", "Comprehension" },
                CSharpAdvantageCategories = new List<string>()
            },
            CaseResults = new List<BenchmarkCaseResult>
            {
                new()
                {
                    CaseId = "001",
                    FileName = "Test1",
                    Level = 1,
                    CalorSuccess = true,
                    CSharpSuccess = true,
                    Metrics = new List<MetricResult>
                    {
                        new("TokenEconomics", "TokenCount", 50, 100, 2.0, new())
                    }
                }
            }
        };
    }

    #endregion
}
