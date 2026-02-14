using Calor.Compiler.CodeGen;
using Calor.Evaluation.LlmTasks;
using Calor.Evaluation.LlmTasks.Execution;
using Calor.Runtime;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Tests for the safety benchmark infrastructure.
/// </summary>
public class SafetyBenchmarkTests
{
    private const string SafeDivideCalor = @"
§M{m001:Test}
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
";

    [Fact]
    public void CodeExecutor_WithDebugMode_CompilesWithContracts()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000, contractMode: EmitContractMode.Debug);

        var compileResult = executor.CompileCalor(SafeDivideCalor);

        Assert.True(compileResult.Success);
        Assert.Contains("ContractViolationException", compileResult.GeneratedCSharp);
        Assert.Contains("Precondition failed", compileResult.GeneratedCSharp);
    }

    [Fact]
    public void CodeExecutor_WithOffMode_CompilesWithoutContracts()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000, contractMode: EmitContractMode.Off);

        var compileResult = executor.CompileCalor(SafeDivideCalor);

        Assert.True(compileResult.Success);
        Assert.DoesNotContain("ContractViolationException", compileResult.GeneratedCSharp);
    }

    [Fact]
    public void CodeExecutor_DebugMode_NormalCaseSucceeds()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000, contractMode: EmitContractMode.Debug);

        var compileResult = executor.CompileCalor(SafeDivideCalor);
        Assert.True(compileResult.Success);

        var csharpResult = executor.CompileCSharp(compileResult.GeneratedCSharp!);
        Assert.True(csharpResult.Success);

        var execResult = executor.Execute(csharpResult.AssemblyBytes!, "SafeDivide", new object[] { 10, 2 });

        Assert.True(execResult.Success);
        Assert.Equal(5, execResult.ReturnValue);
        Assert.False(execResult.ContractViolation);
    }

    [Fact]
    public void CodeExecutor_DebugMode_ViolationCaseThrowsContractException()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000, contractMode: EmitContractMode.Debug);

        var compileResult = executor.CompileCalor(SafeDivideCalor);
        Assert.True(compileResult.Success);

        var csharpResult = executor.CompileCSharp(compileResult.GeneratedCSharp!);
        Assert.True(csharpResult.Success);

        var execResult = executor.Execute(csharpResult.AssemblyBytes!, "SafeDivide", new object[] { 10, 0 });

        Assert.False(execResult.Success);
        Assert.True(execResult.ContractViolation);
        Assert.Equal("ContractViolationException", execResult.ExceptionType);
        Assert.NotNull(execResult.Exception);
        Assert.IsType<ContractViolationException>(execResult.Exception);
    }

    [Fact]
    public void CodeExecutor_DebugMode_CapturesSafetyAnalysis()
    {
        using var executor = new CodeExecutor(timeoutMs: 5000, contractMode: EmitContractMode.Debug);

        var compileResult = executor.CompileCalor(SafeDivideCalor);
        var csharpResult = executor.CompileCSharp(compileResult.GeneratedCSharp!);
        var execResult = executor.Execute(csharpResult.AssemblyBytes!, "SafeDivide", new object[] { 10, 0 });

        Assert.NotNull(execResult.SafetyAnalysis);
        Assert.True(execResult.SafetyAnalysis.ExceptionDetected);
        Assert.True(execResult.SafetyAnalysis.HasLocation);
        Assert.True(execResult.SafetyAnalysis.HasCondition);
        Assert.Equal("(b != 0)", execResult.SafetyAnalysis.Condition);
        Assert.Equal("f001", execResult.SafetyAnalysis.FunctionId);
        Assert.True(execResult.SafetyAnalysis.Line > 0);
    }

    [Fact]
    public void SafetyScorer_ContractViolationException_ScoresHighly()
    {
        var cve = new ContractViolationException(
            "Precondition failed",
            "f001",
            ContractKind.Requires,
            startOffset: 0,
            length: 10,
            sourceFile: "test.calr",
            line: 5,
            column: 3,
            condition: "(b != 0)"
        );

        var score = SafetyScorer.ScoreErrorQuality(cve, "calor", expectedViolation: true);

        Assert.True(score >= 0.9, $"Expected score >= 0.9, got {score}");
    }

    [Fact]
    public void SafetyScorer_ArgumentException_ScoresLower()
    {
        var argEx = new ArgumentException("b cannot be zero", "b");

        var score = SafetyScorer.ScoreErrorQuality(argEx, "csharp", expectedViolation: true);

        Assert.True(score >= 0.3 && score <= 0.5, $"Expected score between 0.3-0.5, got {score}");
    }

    [Fact]
    public void SafetyScorer_NoException_ScoresZero()
    {
        var score = SafetyScorer.ScoreErrorQuality(null, "csharp", expectedViolation: true);

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void SafetyScorer_AnalyzeException_ContractViolation_ExtractsMetadata()
    {
        var cve = new ContractViolationException(
            "Precondition failed",
            "f001",
            ContractKind.Requires,
            startOffset: 0,
            length: 10,
            sourceFile: "test.calr",
            line: 5,
            column: 3,
            condition: "(b != 0)"
        );

        var analysis = SafetyScorer.AnalyzeException(cve, "calor");

        Assert.True(analysis.ExceptionDetected);
        Assert.True(analysis.HasLocation);
        Assert.True(analysis.HasCondition);
        Assert.Equal("f001", analysis.FunctionId);
        Assert.Equal(5, analysis.Line);
        Assert.Equal(3, analysis.Column);
        Assert.Equal("(b != 0)", analysis.Condition);
        Assert.Equal("Requires", analysis.ContractKind);
        Assert.Equal(ErrorQualityLevel.Excellent, analysis.QualityLevel);
    }

    [Fact]
    public void SafetyScorer_AnalyzeException_ArgumentException_ExtractsParameterName()
    {
        var argEx = new ArgumentException("b cannot be zero", "b");

        var analysis = SafetyScorer.AnalyzeException(argEx, "csharp");

        Assert.True(analysis.ExceptionDetected);
        Assert.True(analysis.HasParameterName);
        Assert.Equal("b", analysis.ParameterName);
        Assert.False(analysis.HasLocation);
        Assert.False(analysis.HasCondition);
    }

    [Fact]
    public void SafetyScorer_CalculateSafetyScore_WeightsCorrectly()
    {
        // Test with all metrics at 1.0
        var perfectScore = SafetyScorer.CalculateSafetyScore(
            violationDetected: true,
            expectedViolation: true,
            errorQualityScore: 1.0,
            normalCorrectness: 1.0
        );

        Assert.Equal(1.0, perfectScore, precision: 2);

        // Test with violation not detected (expected)
        var missedViolation = SafetyScorer.CalculateSafetyScore(
            violationDetected: false,
            expectedViolation: true,
            errorQualityScore: 0.0,
            normalCorrectness: 1.0
        );

        // Should be 0.0 (violation) + 0.0 (quality) + 0.3 (correctness) = 0.3
        Assert.Equal(0.3, missedViolation, precision: 2);
    }
}
