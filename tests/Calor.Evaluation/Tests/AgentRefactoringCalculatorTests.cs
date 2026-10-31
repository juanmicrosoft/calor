using Calor.Evaluation.Metrics;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Unit tests for AgentRefactoringCalculator.
/// Tests the calculator's ability to:
/// - Find the agent-tasks directory
/// - Parse agent task output
/// - Calculate success scores
/// - Handle error conditions
/// </summary>
public class AgentRefactoringCalculatorTests
{
    #region Directory Finding Tests

    [Fact]
    public void Constructor_FindsAgentTasksDir_WhenRunningFromRepoRoot()
    {
        // Arrange & Act
        // This test verifies the path finding logic works when running from the repo
        var calculator = new AgentRefactoringCalculator(quickMode: true);

        // Assert - if we got here without exception, path was found
        Assert.NotNull(calculator);
    }

    [Fact]
    public void Constructor_AcceptsExplicitPath_WhenProvided()
    {
        // Arrange
        var testDir = Path.GetTempPath();

        // Act
        var calculator = new AgentRefactoringCalculator(agentTasksDir: testDir, quickMode: true);

        // Assert
        Assert.NotNull(calculator);
    }

    #endregion

    #region Output Parsing Tests

    [Fact]
    public void ParseAgentResults_ExtractsPassedCount_FromTypicalOutput()
    {
        // Arrange
        var output = """
            ============================================
            Test Summary
            ============================================
            Total: 10 tests
            Passed: 8
            Failed: 2
            """;

        // Act
        var results = InvokeParseAgentResults(output, "calor");

        // Assert
        Assert.Equal(8, results.Passed);
        Assert.Equal(2, results.Failed);
        Assert.Equal("calor", results.Language);
    }

    [Fact]
    public void ParseAgentResults_HandlesZeroPassed_Gracefully()
    {
        // Arrange
        var output = """
            Passed: 0
            Failed: 5
            """;

        // Act
        var results = InvokeParseAgentResults(output, "csharp");

        // Assert
        Assert.Equal(0, results.Passed);
        Assert.Equal(5, results.Failed);
    }

    [Fact]
    public void ParseAgentResults_HandlesNoMatches_Gracefully()
    {
        // Arrange
        var output = "No tests were run";

        // Act
        var results = InvokeParseAgentResults(output, "calor");

        // Assert
        Assert.Equal(0, results.Passed);
        Assert.Equal(0, results.Failed);
    }

    [Fact]
    public void ParseAgentResults_ExtractsTaskResults_FromDetailedOutput()
    {
        // Arrange
        var output = """
            [PASS] refactor-extract-simple-calor: 2/3 passed
            [FAIL] refactor-extract-contracts-calor: 1/3 passed
            Passed: 1
            Failed: 1
            """;

        // Act
        var results = InvokeParseAgentResults(output, "calor");

        // Assert
        Assert.Equal(1, results.Passed);
        Assert.Equal(1, results.Failed);
        Assert.Contains("refactor-extract-simple-calor", results.TaskResults.Keys);
    }

    #endregion

    #region Success Score Calculation Tests

    [Fact]
    public void CalculateSuccessScore_ReturnsCorrectRate_ForMixedResults()
    {
        // Arrange
        var results = new AgentTaskResults
        {
            Language = "calor",
            Passed = 15,
            Failed = 5
        };

        // Act
        var score = CalculateSuccessScore(results);

        // Assert
        Assert.Equal(0.75, score, precision: 2);
    }

    [Fact]
    public void CalculateSuccessScore_ReturnsZero_WhenAllFailed()
    {
        // Arrange
        var results = new AgentTaskResults
        {
            Language = "calor",
            Passed = 0,
            Failed = 10
        };

        // Act
        var score = CalculateSuccessScore(results);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void CalculateSuccessScore_ReturnsOne_WhenAllPassed()
    {
        // Arrange
        var results = new AgentTaskResults
        {
            Language = "calor",
            Passed = 10,
            Failed = 0
        };

        // Act
        var score = CalculateSuccessScore(results);

        // Assert
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void CalculateSuccessScore_ReturnsZero_WhenNoTasks()
    {
        // Arrange
        var results = new AgentTaskResults
        {
            Language = "calor",
            Passed = 0,
            Failed = 0
        };

        // Act
        var score = CalculateSuccessScore(results);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void CalculateSuccessScore_ReturnsZero_WhenError()
    {
        // Arrange
        var results = new AgentTaskResults
        {
            Language = "calor",
            Error = "Script not found"
        };

        // Act
        var score = CalculateSuccessScore(results);

        // Assert
        Assert.Equal(0.0, score);
    }

    #endregion

    #region Historical Source-ID Refactoring Evaluation Tests

    // Preserve the frozen lexical producer's inputs, including nonempty ID-loss controls.

    [Fact]
    public void EvaluateRefactoring_DetectsContractPreservation_WhenContractsMaintained()
    {
        // Arrange
        var calculator = new AgentRefactoringCalculator(quickMode: true);
        var before = "§F{f001:Test} §Q (>= x 0) §S (>= result 0) §R x §/F";
        var after = "§F{f001:Test} §Q (>= x 0) §S (>= result 0) §R x §/F §F{f002:Helper} §Q (>= y 0) §R y §/F";

        // Act
        var result = calculator.EvaluateRefactoring("test-scenario", before, after, "", "");

        // Assert
        Assert.True(result.CalorContractsPreserved);
    }

    [Fact]
    public void EvaluateRefactoring_DetectsContractLoss_WhenContractsRemoved()
    {
        // Arrange
        var calculator = new AgentRefactoringCalculator(quickMode: true);
        var before = "§F{f001:Test} §Q (>= x 0) §S (>= result 0) §R x §/F";
        var after = "§F{f001:Test} §R x §/F"; // Contracts removed

        // Act
        var result = calculator.EvaluateRefactoring("test-scenario", before, after, "", "");

        // Assert
        Assert.False(result.CalorContractsPreserved);
    }

    [Fact]
    public void EvaluateRefactoring_DetectsEffectPreservation_WhenEffectsMaintained()
    {
        // Arrange
        var calculator = new AgentRefactoringCalculator(quickMode: true);
        var before = "§F{f001:Log} §E{cw} §R 0 §/F";
        var after = "§F{f001:Log} §E{cw} §R 0 §/F §F{f002:Helper} §E{cw} §R 1 §/F";

        // Act
        var result = calculator.EvaluateRefactoring("test-scenario", before, after, "", "");

        // Assert
        Assert.True(result.CalorEffectsPreserved);
    }

    [Fact]
    public void EvaluateRefactoring_DetectsIdPreservation_WhenIdsKept()
    {
        // Arrange
        var calculator = new AgentRefactoringCalculator(quickMode: true);
        var before = "§F{f001:Test} §R x §/F §F{f002:Other} §R y §/F";
        var after = "§F{f001:Test} §R x §/F §F{f002:Other} §R y §/F §F{f003:New} §R z §/F";

        // Act
        var result = calculator.EvaluateRefactoring("test-scenario", before, after, "", "");

        // Assert
        Assert.True(result.CalorIdsPreserved);
    }

    [Fact]
    public void EvaluateRefactoring_DetectsIdLoss_WhenIdsRemoved()
    {
        // Arrange
        var calculator = new AgentRefactoringCalculator(quickMode: true);
        var before = "§F{f001:Test} §R x §/F §F{f002:Other} §R y §/F";
        var after = "§F{f001:Test} §R x §/F"; // f002 removed

        // Act
        var result = calculator.EvaluateRefactoring("test-scenario", before, after, "", "");

        // Assert
        Assert.False(result.CalorIdsPreserved);
    }

    [Fact]
    public void EvaluateRefactoring_CalculatesPreservationScore_Correctly()
    {
        // Arrange
        var calculator = new AgentRefactoringCalculator(quickMode: true);
        var before = "§F{f001:Test} §Q (>= x 0) §E{cw} §R x §/F";
        var after = "§F{f001:Test} §Q (>= x 0) §E{cw} §R x §/F";

        // Act
        var result = calculator.EvaluateRefactoring("test-scenario", before, after, "", "");

        // Assert
        Assert.True(result.CalorFullyPreserved);
        Assert.Equal(1.0, result.CalorPreservationScore, precision: 2);
    }

    #endregion

    #region Helper Methods

    // Use reflection to call private ParseAgentResults method
    private static AgentTaskResults InvokeParseAgentResults(string output, string language)
    {
        var method = typeof(AgentRefactoringCalculator)
            .GetMethod("ParseAgentResults", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (method == null)
            throw new InvalidOperationException("ParseAgentResults method not found");

        return (AgentTaskResults)method.Invoke(null, new object[] { output, language })!;
    }

    // Use reflection to call private CalculateSuccessScore method
    private static double CalculateSuccessScore(AgentTaskResults results)
    {
        var method = typeof(AgentRefactoringCalculator)
            .GetMethod("CalculateSuccessScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (method == null)
            throw new InvalidOperationException("CalculateSuccessScore method not found");

        return (double)method.Invoke(null, new object[] { results })!;
    }

    #endregion
}
