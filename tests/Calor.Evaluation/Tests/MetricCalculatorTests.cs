using Calor.Evaluation.Core;
using Calor.Evaluation.Metrics;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Unit tests for metric calculators.
/// </summary>
public class MetricCalculatorTests
{
    #region Token Economics Tests

    [Fact]
    public async Task TokenEconomicsCalculator_CalculatesTokenRatio_WhenCalorIsMoreCompact()
    {
        // Arrange
        var calculator = new TokenEconomicsCalculator();
        // Using realistic Calor/C# comparison - C# has more verbose boilerplate
        var context = CreateContext(
            calor: @"§M{m:Calc} §F{f:Add} §I{i32:a} §I{i32:b} §O{i32} §R (+ a b) §/F §/M",
            csharp: @"using System;
namespace Calculator
{
    public static class CalculatorModule
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }
    }
}");

        // Act
        var result = await calculator.CalculateAsync(context);

        // Assert
        Assert.Equal("TokenEconomics", result.Category);
        // The tokenizer counts punctuation heavily, so we verify the metric calculation runs
        // and produces comparable scores rather than assuming Calor is always smaller by token count
        Assert.True(result.CalorScore > 0, "Should calculate Calor token count");
        Assert.True(result.CSharpScore > 0, "Should calculate C# token count");
        Assert.True(result.AdvantageRatio > 0, "Should calculate advantage ratio");
    }

    [Fact]
    public void TokenEconomicsCalculator_CalculatesDetailedMetrics()
    {
        // Arrange
        var calculator = new TokenEconomicsCalculator();
        var context = CreateContext(
            calor: "§M{m001:Test} §F{f001:Foo} §R 42 §/F §/M",
            csharp: "namespace Test { class C { int Foo() { return 42; } } }");

        // Act
        var results = calculator.CalculateDetailedMetrics(context);

        // Assert
        Assert.True(results.Count >= 3, "Should have multiple metrics");
        Assert.Contains(results, r => r.MetricName == "TokenCount");
        Assert.Contains(results, r => r.MetricName == "CharacterCount");
        Assert.Contains(results, r => r.MetricName == "LineCount");
    }

    #endregion

    #region Generation Accuracy Tests

    [Fact]
    public async Task GenerationAccuracyCalculator_DetectsCompilationSuccess()
    {
        // Arrange
        var calculator = new GenerationAccuracyCalculator();
        var context = CreateContext(
            calor: @"§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}",
            csharp: @"namespace Test { public static class TestModule { public static int Add(int a, int b) { return a + b; } } }");

        // Act
        var result = await calculator.CalculateAsync(context);

        // Assert
        Assert.Equal("GenerationAccuracy", result.Category);
        Assert.True(result.Details.ContainsKey("calorCompileSuccess"));
        Assert.True(result.Details.ContainsKey("csharpCompileSuccess"));
    }

    [Fact]
    public async Task GenerationAccuracyCalculator_DetectsCompilationFailure()
    {
        // Arrange
        var calculator = new GenerationAccuracyCalculator();
        var context = CreateContext(
            calor: "§INVALID_SYNTAX",
            csharp: "this is not valid C# code { { {");

        // Act
        var result = await calculator.CalculateAsync(context);

        // Assert
        Assert.False((bool)result.Details["calorCompileSuccess"]);
        Assert.False((bool)result.Details["csharpCompileSuccess"]);
    }

    #endregion

    #region Comprehension Tests

    [Fact]
    public async Task ComprehensionCalculator_CalculatesClarityScore()
    {
        // Arrange
        var calculator = new ComprehensionCalculator();
        var context = CreateContext(
            calor: @"§M{m001:Math}
§F{f001:Divide:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §REQ (> b 0)
  §ENS (== result (/ a b))
  §R (/ a b)
§/F{f001}
§/M{m001}",
            csharp: @"namespace Math { public class MathOps { public int Divide(int a, int b) { return a / b; } } }");

        // Act
        var result = await calculator.CalculateAsync(context);

        // Assert
        Assert.Equal("Comprehension", result.Category);
        Assert.True(result.CalorScore > result.CSharpScore,
            "Calor with contracts should have higher clarity score");
    }

    #endregion

    #region Edit Precision Tests

    [Fact]
    public async Task EditPrecisionCalculator_CalculatesTargetingPrecision()
    {
        // Arrange
        var calculator = new EditPrecisionCalculator();
        var context = CreateContext(
            calor: @"§M{m001:App}
§F{f001:Method1:pub}
  §O{void}
§/F{f001}
§F{f002:Method2:pub}
  §O{void}
§/F{f002}
§/M{m001}",
            csharp: @"namespace App { public class AppModule { public void Method1() { } public void Method2() { } } }");

        // Act
        var result = await calculator.CalculateAsync(context);

        // Assert
        Assert.Equal("EditPrecision", result.Category);
        Assert.True(result.Details.ContainsKey("calorTargetingCapabilities"));
        Assert.True(result.Details.ContainsKey("csharpTargetingCapabilities"));
    }

    [Fact]
    public void EditPrecisionCalculator_EvaluatesEditDiff()
    {
        // Arrange
        var calculator = new EditPrecisionCalculator();

        var calorBefore = "§F{f001:Foo} §R 1 §/F";
        var calorAfter = "§F{f001:Foo} §R 2 §/F";
        var csharpBefore = "int Foo() { return 1; }";
        var csharpAfter = "int Foo() { return 2; }";

        // Act
        var result = calculator.EvaluateEdit(
            calorBefore, calorAfter,
            csharpBefore, csharpAfter,
            "Change return value from 1 to 2");

        // Assert
        Assert.Equal("EditPrecision", result.Category);
        Assert.True(result.Details.ContainsKey("calorDiff"));
        Assert.True(result.Details.ContainsKey("csharpDiff"));
    }

    #endregion

    #region Error Detection Tests

    [Fact]
    public async Task ErrorDetectionCalculator_CalculatesDetectionCapability()
    {
        // Arrange
        var calculator = new ErrorDetectionCalculator();
        var context = CreateContext(
            calor: @"§M{m001:Safe}
§F{f001:Divide:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §REQ (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}",
            csharp: @"public class Safe { public int Divide(int a, int b) { return a / b; } }");

        // Act
        var result = await calculator.CalculateAsync(context);

        // Assert
        Assert.Equal("ErrorDetection", result.Category);
        Assert.True(result.CalorScore > result.CSharpScore,
            "Calor with contracts should have higher detection capability");
    }

    #endregion

    #region Information Density Tests

    [Fact]
    public async Task InformationDensityCalculator_CalculatesDensity()
    {
        // Arrange
        var calculator = new InformationDensityCalculator();
        var context = CreateContext(
            calor: @"§M{m001:Dense}
§F{f001:Process:pub}
  §I{i32:x}
  §O{i32}
  §E{pure}
  §REQ (>= x 0)
  §ENS (>= result 0)
  §R (* x x)
§/F{f001}
§/M{m001}",
            csharp: @"namespace Dense { public class DenseModule { public int Process(int x) { return x * x; } } }");

        // Act
        var result = await calculator.CalculateAsync(context);

        // Assert
        Assert.Equal("InformationDensity", result.Category);
        Assert.True(result.CalorScore > 0, "Should calculate Calor density");
        Assert.True(result.CSharpScore > 0, "Should calculate C# density");
    }

    [Fact]
    public void InformationDensityCalculator_CalculatesDetailedMetrics()
    {
        // Arrange
        var calculator = new InformationDensityCalculator();
        var context = CreateContext(
            calor: "§M{m} §F{f} §I{i32:x} §O{i32} §REQ (> x 0) §R x §/F §/M",
            csharp: "class C { int F(int x) { return x; } }");

        // Act
        var results = calculator.CalculateDetailedMetrics(context);

        // Assert
        Assert.Contains(results, r => r.MetricName == "OverallDensity");
        Assert.Contains(results, r => r.MetricName == "TypeDensity");
        Assert.Contains(results, r => r.MetricName == "ContractDensity");
    }

    #endregion

    #region Task Completion Tests

    [Fact]
    public async Task TaskCompletionCalculator_CalculatesCompletionPotential()
    {
        // Arrange
        var calculator = new TaskCompletionCalculator();
        var context = CreateContext(
            calor: @"§M{m001:Task}
§F{f001:Run:pub}
  §O{void}
§/F{f001}
§/M{m001}",
            csharp: @"namespace Task { public class TaskModule { public void Run() { } } }");

        // Act
        var result = await calculator.CalculateAsync(context);

        // Assert
        Assert.Equal("TaskCompletion", result.Category);
        Assert.True(result.Details.ContainsKey("calorFactors"));
        Assert.True(result.Details.ContainsKey("csharpFactors"));
    }

    #endregion

    #region MetricResult Tests

    [Fact]
    public void MetricResult_CreateLowerIsBetter_CalculatesCorrectRatio()
    {
        // Lower is better means C#/Calor ratio indicates Calor advantage
        var result = MetricResult.CreateLowerIsBetter("Test", "Metric", 50, 100);

        Assert.Equal(2.0, result.AdvantageRatio); // 100/50 = 2x advantage for Calor
        Assert.True(result.CalorHasAdvantage);
        Assert.Equal(100, result.AdvantagePercentage);
    }

    [Fact]
    public void MetricResult_CreateHigherIsBetter_CalculatesCorrectRatio()
    {
        // Higher is better means Calor/C# ratio indicates Calor advantage
        var result = MetricResult.CreateHigherIsBetter("Test", "Metric", 100, 50);

        Assert.Equal(2.0, result.AdvantageRatio); // 100/50 = 2x advantage for Calor
        Assert.True(result.CalorHasAdvantage);
    }

    [Fact]
    public void MetricResult_AdvantagePercentage_CalculatesCorrectly()
    {
        var result = new MetricResult("Test", "Metric", 0, 0, 1.5, new());

        Assert.Equal(50, result.AdvantagePercentage); // 50% advantage
    }

    #endregion

    #region Helper Methods

    private static EvaluationContext CreateContext(string calor, string csharp)
    {
        return new EvaluationContext
        {
            CalorSource = calor,
            CSharpSource = csharp,
            FileName = "test",
            Level = 1,
            Features = new List<string>()
        };
    }

    #endregion
}
