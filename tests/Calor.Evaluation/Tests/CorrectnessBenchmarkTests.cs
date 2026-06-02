using System.Text.Json;
using Calor.Evaluation.LlmTasks;
using Calor.Evaluation.LlmTasks.Execution;
using Calor.Evaluation.Metrics;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Tests for the Correctness benchmark infrastructure.
/// </summary>
public class CorrectnessBenchmarkTests
{
    #region CodeExecutor Tests for Correctness Scenarios

    // Note: Calor-specific execution tests are in SafetyBenchmarkTests.cs which uses proper contract syntax.
    // These tests focus on C# execution and the correctness scoring infrastructure.

    private const string SafeDivideCSharp = @"
public class Test
{
    public static int SafeDivide(int a, int b)
    {
        if (b == 0) return 0;
        return a / b;
    }
}
";

    private const string FindMaxCSharp = @"
public class Test
{
    public static int FindMax(int[] arr)
    {
        if (arr == null || arr.Length == 0) return int.MinValue;
        int max = arr[0];
        for (int i = 1; i < arr.Length; i++)
        {
            if (arr[i] > max) max = arr[i];
        }
        return max;
    }
}
";

    [Fact]
    public void CodeExecutor_CSharpSafeDivide_NormalCase_ReturnsCorrectResult()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000);

        var result = executor.CompileAndExecuteCSharp(SafeDivideCSharp, "SafeDivide", new object[] { 10, 2 });

        Assert.True(result.Success);
        Assert.Equal(5, result.ReturnValue);
    }

    [Fact]
    public void CodeExecutor_CSharpSafeDivide_EdgeCase_DivisionByZero_ReturnsZero()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000);

        var result = executor.CompileAndExecuteCSharp(SafeDivideCSharp, "SafeDivide", new object[] { 10, 0 });

        Assert.True(result.Success);
        Assert.Equal(0, result.ReturnValue);
    }

    [Fact]
    public void CodeExecutor_CSharpFindMax_NormalCase_ReturnsMaxValue()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000);

        var result = executor.CompileAndExecuteCSharp(FindMaxCSharp, "FindMax", new object[] { new[] { 3, 1, 4, 1, 5 } });

        Assert.True(result.Success);
        Assert.Equal(5, result.ReturnValue);
    }

    [Fact]
    public void CodeExecutor_CSharpFindMax_EdgeCase_EmptyArray_ReturnsMinValue()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000);

        var result = executor.CompileAndExecuteCSharp(FindMaxCSharp, "FindMax", new object[] { Array.Empty<int>() });

        Assert.True(result.Success);
        Assert.Equal(int.MinValue, result.ReturnValue);
    }

    #endregion

    #region CorrectnessTestResult Tests

    [Fact]
    public void CorrectnessTestResult_PassedNormalCase_HasCorrectProperties()
    {
        var result = new CorrectnessTestResult
        {
            Index = 0,
            IsEdgeCase = false,
            Passed = true,
            ActualValue = "5",
            ExpectedValue = "5",
            ExecutionTimeMs = 10.5
        };

        Assert.True(result.Passed);
        Assert.False(result.IsEdgeCase);
        Assert.Equal("5", result.ActualValue);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void CorrectnessTestResult_FailedEdgeCase_HasCorrectProperties()
    {
        var result = new CorrectnessTestResult
        {
            Index = 1,
            IsEdgeCase = true,
            Passed = false,
            ActualValue = "5",
            ExpectedValue = "0",
            ErrorMessage = "Expected 0 but got 5"
        };

        Assert.False(result.Passed);
        Assert.True(result.IsEdgeCase);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion

    #region CorrectnessLanguageResult Tests

    [Fact]
    public void CorrectnessLanguageResult_AllTestsPassed_ScoresCorrectly()
    {
        var result = new CorrectnessLanguageResult
        {
            Language = "calor",
            GeneratedCode = "test code",
            CompilationSuccess = true,
            TestResults = new List<CorrectnessTestResult>
            {
                new() { Index = 0, IsEdgeCase = false, Passed = true },
                new() { Index = 1, IsEdgeCase = false, Passed = true },
                new() { Index = 2, IsEdgeCase = true, Passed = true },
                new() { Index = 3, IsEdgeCase = true, Passed = true }
            },
            CorrectnessScore = 1.0,
            EdgeCaseScore = 1.0,
            NormalCaseScore = 1.0,
            TestsPassed = 4,
            TestsTotal = 4
        };

        Assert.Equal(1.0, result.CorrectnessScore);
        Assert.Equal(1.0, result.EdgeCaseScore);
        Assert.Equal(4, result.TestsPassed);
    }

    [Fact]
    public void CorrectnessLanguageResult_PartialEdgeCaseFailure_ScoresCorrectly()
    {
        // 2 normal passed, 1 edge passed, 1 edge failed = 3/4 overall, 1/2 edge
        var result = new CorrectnessLanguageResult
        {
            Language = "csharp",
            GeneratedCode = "test code",
            CompilationSuccess = true,
            TestResults = new List<CorrectnessTestResult>
            {
                new() { Index = 0, IsEdgeCase = false, Passed = true },
                new() { Index = 1, IsEdgeCase = false, Passed = true },
                new() { Index = 2, IsEdgeCase = true, Passed = true },
                new() { Index = 3, IsEdgeCase = true, Passed = false }
            },
            CorrectnessScore = 0.75,
            EdgeCaseScore = 0.5,
            NormalCaseScore = 1.0,
            TestsPassed = 3,
            TestsTotal = 4
        };

        Assert.Equal(0.75, result.CorrectnessScore);
        Assert.Equal(0.5, result.EdgeCaseScore);
        Assert.Equal(1.0, result.NormalCaseScore);
    }

    [Fact]
    public void CorrectnessLanguageResult_CompilationFailure_ScoresZero()
    {
        var result = new CorrectnessLanguageResult
        {
            Language = "calor",
            GeneratedCode = "invalid code",
            CompilationSuccess = false,
            CompilationErrors = new List<string> { "Syntax error at line 1" },
            CorrectnessScore = 0,
            EdgeCaseScore = 0,
            TestsPassed = 0,
            TestsTotal = 0
        };

        Assert.False(result.CompilationSuccess);
        Assert.Equal(0, result.CorrectnessScore);
        Assert.Single(result.CompilationErrors);
    }

    #endregion

    #region CorrectnessTaskResult Tests

    [Fact]
    public void CorrectnessTaskResult_CalorWins_AdvantageRatioGreaterThanOne()
    {
        var task = CreateTestTask("test-001", "Test");

        var result = new CorrectnessTaskResult
        {
            Task = task,
            CalorResult = new CorrectnessLanguageResult
            {
                Language = "calor",
                GeneratedCode = "",
                CompilationSuccess = true,
                CorrectnessScore = 1.0,
                EdgeCaseScore = 1.0
            },
            CSharpResult = new CorrectnessLanguageResult
            {
                Language = "csharp",
                GeneratedCode = "",
                CompilationSuccess = true,
                CorrectnessScore = 0.75,
                EdgeCaseScore = 0.5
            }
        };

        Assert.True(result.AdvantageRatio > 1.0);
        Assert.Equal(1.0 / 0.75, result.AdvantageRatio, precision: 2);
    }

    [Fact]
    public void CorrectnessTaskResult_CSharpWins_AdvantageRatioLessThanOne()
    {
        var task = CreateTestTask("test-002", "Test");

        var result = new CorrectnessTaskResult
        {
            Task = task,
            CalorResult = new CorrectnessLanguageResult
            {
                Language = "calor",
                GeneratedCode = "",
                CompilationSuccess = true,
                CorrectnessScore = 0.5
            },
            CSharpResult = new CorrectnessLanguageResult
            {
                Language = "csharp",
                GeneratedCode = "",
                CompilationSuccess = true,
                CorrectnessScore = 1.0
            }
        };

        Assert.True(result.AdvantageRatio < 1.0);
        Assert.Equal(0.5, result.AdvantageRatio, precision: 2);
    }

    [Fact]
    public void CorrectnessTaskResult_Tie_AdvantageRatioEqualsOne()
    {
        var task = CreateTestTask("test-003", "Test");

        var result = new CorrectnessTaskResult
        {
            Task = task,
            CalorResult = new CorrectnessLanguageResult
            {
                Language = "calor",
                GeneratedCode = "",
                CompilationSuccess = true,
                CorrectnessScore = 0.8
            },
            CSharpResult = new CorrectnessLanguageResult
            {
                Language = "csharp",
                GeneratedCode = "",
                CompilationSuccess = true,
                CorrectnessScore = 0.8
            }
        };

        Assert.Equal(1.0, result.AdvantageRatio, precision: 2);
    }

    #endregion

    #region CorrectnessBenchmarkSummary Tests

    [Fact]
    public void CorrectnessBenchmarkSummary_CalculatesWinsCorrectly()
    {
        var summary = new CorrectnessBenchmarkSummary
        {
            TotalTasks = 10,
            CalorWins = 6,
            CSharpWins = 3,
            Ties = 1,
            AverageCalorScore = 0.85,
            AverageCSharpScore = 0.75,
            AdvantageRatio = 1.13,
            CalorEdgeCaseScore = 0.90,
            CSharpEdgeCaseScore = 0.70,
            TotalTestsPassed = 180,
            TotalTests = 200
        };

        Assert.Equal(10, summary.TotalTasks);
        Assert.Equal(6, summary.CalorWins);
        Assert.Equal(3, summary.CSharpWins);
        Assert.Equal(1, summary.Ties);
        Assert.True(summary.AdvantageRatio > 1.0);
    }

    [Fact]
    public void CorrectnessBenchmarkSummary_CategorySummary_CalculatesCorrectly()
    {
        var summary = new CorrectnessBenchmarkSummary
        {
            ByCategory = new Dictionary<string, CorrectnessCategorySummary>
            {
                ["null-handling"] = new CorrectnessCategorySummary
                {
                    Category = "null-handling",
                    TaskCount = 5,
                    AverageCalorScore = 0.95,
                    AverageCSharpScore = 0.80,
                    CalorEdgeCaseScore = 1.0,
                    CSharpEdgeCaseScore = 0.6,
                    AdvantageRatio = 1.1875
                },
                ["arithmetic-safety"] = new CorrectnessCategorySummary
                {
                    Category = "arithmetic-safety",
                    TaskCount = 5,
                    AverageCalorScore = 0.90,
                    AverageCSharpScore = 0.85,
                    AdvantageRatio = 1.0588
                }
            }
        };

        Assert.Equal(2, summary.ByCategory.Count);
        Assert.True(summary.ByCategory["null-handling"].AdvantageRatio > 1.0);
        Assert.Equal(5, summary.ByCategory["null-handling"].TaskCount);
    }

    #endregion

    #region CorrectnessBenchmarkOptions Tests

    [Fact]
    public void CorrectnessBenchmarkOptions_DefaultValues_AreCorrect()
    {
        var options = new CorrectnessBenchmarkOptions();

        Assert.Equal(5.00m, options.BudgetLimit);
        Assert.True(options.UseCache);
        Assert.False(options.RefreshCache);
        Assert.False(options.DryRun);
        Assert.False(options.Verbose);
        Assert.Null(options.TaskFilter);
        Assert.Null(options.CategoryFilter);
        Assert.Null(options.SampleSize);
    }

    [Fact]
    public void CorrectnessBenchmarkOptions_CustomValues_AreApplied()
    {
        var options = new CorrectnessBenchmarkOptions
        {
            BudgetLimit = 10.00m,
            UseCache = false,
            DryRun = true,
            Verbose = true,
            CategoryFilter = "null-handling",
            SampleSize = 5
        };

        Assert.Equal(10.00m, options.BudgetLimit);
        Assert.False(options.UseCache);
        Assert.True(options.DryRun);
        Assert.Equal("null-handling", options.CategoryFilter);
        Assert.Equal(5, options.SampleSize);
    }

    #endregion

    #region CorrectnessCalculator Tests

    [Fact]
    public void CorrectnessCalculator_Category_IsCorrectness()
    {
        var calculator = new CorrectnessCalculator();

        Assert.Equal("Correctness", calculator.Category);
    }

    [Fact]
    public void CorrectnessCalculator_Description_DescribesCorrectnessMeasurement()
    {
        var calculator = new CorrectnessCalculator();

        // Description should mention correctness and edge cases
        Assert.Contains("correctness", calculator.Description.ToLower());
        Assert.Contains("edge case", calculator.Description.ToLower());
    }

    [Fact]
    public async Task CorrectnessCalculator_EstimationMode_ReturnsValidMetric()
    {
        var calculator = new CorrectnessCalculator();
        var context = CreateTestContext();

        var result = await calculator.CalculateAsync(context);

        Assert.Equal("Correctness", result.Category);
        Assert.Contains("Estimated", result.MetricName);
        Assert.True(result.CalorScore >= 0 && result.CalorScore <= 1);
        Assert.True(result.CSharpScore >= 0 && result.CSharpScore <= 1);
        Assert.True(result.Details.ContainsKey("estimated"));
        Assert.True((bool)result.Details["estimated"]);
    }

    [Fact]
    public async Task CorrectnessCalculator_EstimationMode_CompilingCode_ScoresHigher()
    {
        var calculator = new CorrectnessCalculator();

        // Context with compiling code
        var compilingContext = CreateTestContext(calorCompiles: true, csharpCompiles: true);
        var compilingResult = await calculator.CalculateAsync(compilingContext);

        // Context with non-compiling code
        var nonCompilingContext = CreateTestContext(calorCompiles: false, csharpCompiles: false);
        var nonCompilingResult = await calculator.CalculateAsync(nonCompilingContext);

        Assert.True(compilingResult.CalorScore > nonCompilingResult.CalorScore);
        Assert.True(compilingResult.CSharpScore > nonCompilingResult.CSharpScore);
    }

    [Fact]
    public async Task CorrectnessCalculator_EstimationMode_WithValidationPatterns_ScoresHigher()
    {
        var calculator = new CorrectnessCalculator();

        // Context with validation patterns (null checks, bounds checks)
        // The estimator looks for patterns like "§Q", "§REQ", "null", ">=", "<"
        var withValidation = CreateTestContext(
            calorSource: "§M{m001:Test}\n§F{f001:test:pub}\n  §I{i32:x}\n  §O{i32}\n  §Q (>= x 0)\n  §R x\n\n",
            csharpSource: "public class T { public int test(int x) { if (x == null) throw new ArgumentNullException(); if (x < 0) throw new ArgumentException(); return x; } }"
        );
        var withValidationResult = await calculator.CalculateAsync(withValidation);

        // Context without validation patterns - minimal code
        var withoutValidation = CreateTestContext(
            calorSource: "§M{m001:Test}\n§F{f001:test:pub}\n  §O{void}\n\n",
            csharpSource: "public class T { public void test() { } }"
        );
        var withoutValidationResult = await calculator.CalculateAsync(withoutValidation);

        // The estimation logic gives higher scores to code with validation patterns
        Assert.True(withValidationResult.CSharpScore > withoutValidationResult.CSharpScore,
            $"Expected C# with validation ({withValidationResult.CSharpScore}) > without ({withoutValidationResult.CSharpScore})");
    }

    #endregion

    #region TaskTestCase IsEdgeCase Tests

    [Fact]
    public void TaskTestCase_IsEdgeCase_DefaultsFalse()
    {
        var json = """{"input": [1, 2], "expected": 3}""";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var testCase = JsonSerializer.Deserialize<TaskTestCase>(json, options);

        Assert.NotNull(testCase);
        Assert.False(testCase.IsEdgeCase);
    }

    [Fact]
    public void TaskTestCase_IsEdgeCase_DeserializesTrue()
    {
        var json = """{"input": [10, 0], "expected": 0, "isEdgeCase": true}""";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var testCase = JsonSerializer.Deserialize<TaskTestCase>(json, options);

        Assert.NotNull(testCase);
        Assert.True(testCase.IsEdgeCase);
    }

    [Fact]
    public async Task TaskManifest_CorrectnessManifest_LoadsWithEdgeCases()
    {
        var manifestPath = GetCorrectnessManifestPath();
        if (!File.Exists(manifestPath))
        {
            // Skip if manifest not found (CI environment)
            return;
        }

        var manifest = await LlmTaskManifest.LoadAsync(manifestPath);

        Assert.NotEmpty(manifest.Tasks);

        // Check that some tasks have edge cases
        var tasksWithEdgeCases = manifest.Tasks
            .Where(t => t.TestCases.Any(tc => tc.IsEdgeCase))
            .ToList();

        Assert.NotEmpty(tasksWithEdgeCases);

        // Verify edge case distribution
        var firstTask = manifest.Tasks.First();
        var edgeCases = firstTask.TestCases.Where(tc => tc.IsEdgeCase).ToList();
        var normalCases = firstTask.TestCases.Where(tc => !tc.IsEdgeCase).ToList();

        Assert.NotEmpty(edgeCases);
        Assert.NotEmpty(normalCases);
    }

    #endregion

    #region OutputVerifier Integration Tests

    [Fact]
    public void OutputVerifier_IntegerComparison_MatchesCorrectly()
    {
        var verifier = new OutputVerifier();
        using var executor = new CodeExecutor(timeoutMs: 5000);

        var execResult = executor.CompileAndExecuteCSharp(
            "public class T { public static int Add(int a, int b) => a + b; }",
            "Add",
            new object[] { 2, 3 }
        );

        var expected = JsonDocument.Parse("5").RootElement;
        var verification = verifier.Verify(execResult, expected);

        Assert.True(verification.Passed);
    }

    [Fact]
    public void OutputVerifier_IntegerComparison_DetectsMismatch()
    {
        var verifier = new OutputVerifier();
        using var executor = new CodeExecutor(timeoutMs: 5000);

        var execResult = executor.CompileAndExecuteCSharp(
            "public class T { public static int Add(int a, int b) => a + b; }",
            "Add",
            new object[] { 2, 3 }
        );

        var expected = JsonDocument.Parse("6").RootElement;
        var verification = verifier.Verify(execResult, expected);

        Assert.False(verification.Passed);
        Assert.NotNull(verification.Reason);
    }

    [Fact]
    public void OutputVerifier_BooleanComparison_MatchesCorrectly()
    {
        var verifier = new OutputVerifier();
        using var executor = new CodeExecutor(timeoutMs: 5000);

        var execResult = executor.CompileAndExecuteCSharp(
            "public class T { public static bool IsPositive(int x) => x > 0; }",
            "IsPositive",
            new object[] { 5 }
        );

        var expected = JsonDocument.Parse("true").RootElement;
        var verification = verifier.Verify(execResult, expected);

        Assert.True(verification.Passed);
    }

    [Fact]
    public void OutputVerifier_StringComparison_MatchesCorrectly()
    {
        var verifier = new OutputVerifier();
        using var executor = new CodeExecutor(timeoutMs: 5000);

        var execResult = executor.CompileAndExecuteCSharp(
            """public class T { public static string Greet(string name) => "Hello, " + name; }""",
            "Greet",
            new object[] { "World" }
        );

        var expected = JsonDocument.Parse("\"Hello, World\"").RootElement;
        var verification = verifier.Verify(execResult, expected);

        Assert.True(verification.Passed);
    }

    #endregion

    #region Helper Methods

    private static LlmTaskDefinition CreateTestTask(string id, string name, string category = "test")
    {
        return new LlmTaskDefinition
        {
            Id = id,
            Name = name,
            Category = category,
            Prompt = "Test prompt",
            TestCases = new List<TaskTestCase>
            {
                new()
                {
                    Input = new[] { JsonDocument.Parse("1").RootElement, JsonDocument.Parse("2").RootElement },
                    Expected = JsonDocument.Parse("3").RootElement
                }
            }
        };
    }

    private static Core.EvaluationContext CreateTestContext(
        bool calorCompiles = true,
        bool csharpCompiles = true,
        string? calorSource = null,
        string? csharpSource = null)
    {
        // Use valid or invalid source code to control compilation success
        var defaultValidCalor = "§M{m001:Test}\n§F{f001:test:pub}\n  §O{void}\n\n";
        var defaultInvalidCalor = "§INVALID SYNTAX";
        var defaultValidCSharp = "public class T { public void test() { } }";
        var defaultInvalidCSharp = "public class { invalid }";

        return new Core.EvaluationContext
        {
            CalorSource = calorSource ?? (calorCompiles ? defaultValidCalor : defaultInvalidCalor),
            CSharpSource = csharpSource ?? (csharpCompiles ? defaultValidCSharp : defaultInvalidCSharp),
            FileName = "test",
            Level = 1,
            Features = new List<string>()
        };
    }

    private static string GetCorrectnessManifestPath()
    {
        // Try multiple possible locations
        var paths = new[]
        {
            "Tasks/task-manifest-correctness.json",
            "../Tasks/task-manifest-correctness.json",
            "../../Tasks/task-manifest-correctness.json",
            Path.Combine(AppContext.BaseDirectory, "Tasks/task-manifest-correctness.json"),
            Path.Combine(AppContext.BaseDirectory, "../../../Tasks/task-manifest-correctness.json")
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        // Return the expected path for error message
        return "tests/Calor.Evaluation/Tasks/task-manifest-correctness.json";
    }

    #endregion
}
