using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Runtime;
using Xunit;

namespace Calor.Semantics.Tests;

/// <summary>
/// Tests for contract semantics (S9-S10).
/// </summary>
public class ContractTests
{
    /// <summary>
    /// Historical S9 compilation placeholder; does not exercise Option.None.
    /// </summary>
    [Fact]
    public void S9_OptionNone_BehavesCorrectly()
    {
        // Retain the original integer-returning fixture without claiming Option coverage.
        var source = @"
module Test Test
  func getNone () → int
    !
    return 0
";

        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, $"Compilation failed: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    /// <summary>
    /// S10: a failing precondition throws at entry. #1618: the precondition is a parameter
    /// refinement, whose entry check throws ArgumentOutOfRangeException naming the parameter.
    /// </summary>
    [Fact]
    public void S10_RequiresFails_ThrowsContractViolation()
    {
        var source = @"
module Test Test
  func positiveOnly (int:x | (# > 0)) → int
    !
    return x
";

        // With x = -1, precondition should fail
        var result = SemanticsTestHarness.Execute(source, "positiveOnly", new object[] { -1 });

        Assert.False(result.Succeeded, "Contract violation should throw");
        Assert.NotNull(result.Exception);

        var violation = Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
        Assert.Equal("x", violation.ParamName);
    }

    /// <summary>
    /// REQUIRES passes when condition is true.
    /// </summary>
    [Fact]
    public void Requires_Passes_WhenConditionTrue()
    {
        var source = @"
module Test Test
  func positiveOnly (int:x | (# > 0)) → int
    !
    return x * 2
";

        // With x = 5, precondition should pass
        var result = SemanticsTestHarness.Execute(source, "positiveOnly", new object[] { 5 });

        Assert.True(result.Succeeded, $"Execution failed: {result.Exception?.Message}");
        Assert.Equal(10, result.ReturnValue);
    }

    /// <summary>
    /// Multiple preconditions are evaluated in order. #1618: one refinement per parameter,
    /// checked in parameter order.
    /// </summary>
    [Fact]
    public void MultipleRequires_EvaluatedInOrder()
    {
        var source = @"
module Test Test
  func bounded (int:x | (# > 0), int:y | (# < 100)) → int
    !
    return x
";

        // x = 50, y = 50 should pass both
        var result1 = SemanticsTestHarness.Execute(source, "bounded", new object[] { 50, 50 });
        Assert.True(result1.Succeeded);
        Assert.Equal(50, result1.ReturnValue);

        // Both violated: the first parameter's check fails first
        var result2 = SemanticsTestHarness.Execute(source, "bounded", new object[] { -1, 150 });
        Assert.False(result2.Succeeded);
        Assert.Equal("x", Assert.IsType<ArgumentOutOfRangeException>(result2.Exception).ParamName);

        // y = 150 should fail the second check (first passes)
        var result3 = SemanticsTestHarness.Execute(source, "bounded", new object[] { 50, 150 });
        Assert.False(result3.Succeeded);
        Assert.Equal("y", Assert.IsType<ArgumentOutOfRangeException>(result3.Exception).ParamName);
    }

    /// <summary>
    /// Contract messages are included in exceptions.
    /// </summary>
    [Fact]
    public void ContractMessage_IncludedInException()
    {
        // This test uses simple REQUIRES without custom message
        // since message syntax may not be supported
        var source = @"
module Test Test
  func withMessage (int:x | (# > 0)) → int
    !
    return x
";

        var result = SemanticsTestHarness.Execute(source, "withMessage", new object[] { -1 });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Exception);

        // #1618: the refinement entry check names the violated parameter in its message
        var violation = Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
        Assert.Contains("parameter 'x'", violation.Message);
    }
}
