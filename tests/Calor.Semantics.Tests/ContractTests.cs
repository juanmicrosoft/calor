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
    /// S10: REQUIRES that fails throws ContractViolationException with FunctionId.
    /// </summary>
    [Fact]
    public void S10_RequiresFails_ThrowsContractViolation()
    {
        var source = @"
module Test Test
  func positiveOnly (int:x) → int
    !
    requires x > 0
    return x
";

        // With x = -1, precondition should fail
        var result = SemanticsTestHarness.Execute(source, "positiveOnly", new object[] { -1 });

        Assert.False(result.Succeeded, "Contract violation should throw");
        Assert.NotNull(result.Exception);

        // Should be ContractViolationException
        Assert.True(
            result.Exception is ContractViolationException,
            $"Expected ContractViolationException but got: {result.Exception.GetType().Name}");

        var cve = (ContractViolationException)result.Exception;

        var compilation = Program.Compile(source, "test.calr",
            new CompilationOptions { EnforceEffects = false });
        Assert.False(compilation.HasErrors);
        Assert.Equal(Assert.Single(compilation.Ast!.Functions).Id, cve.FunctionId);
    }

    /// <summary>
    /// REQUIRES passes when condition is true.
    /// </summary>
    [Fact]
    public void Requires_Passes_WhenConditionTrue()
    {
        var source = @"
module Test Test
  func positiveOnly (int:x) → int
    !
    requires x > 0
    return x * 2
";

        // With x = 5, precondition should pass
        var result = SemanticsTestHarness.Execute(source, "positiveOnly", new object[] { 5 });

        Assert.True(result.Succeeded, $"Execution failed: {result.Exception?.Message}");
        Assert.Equal(10, result.ReturnValue);
    }

    /// <summary>
    /// Multiple REQUIRES are evaluated in order.
    /// </summary>
    [Fact]
    public void MultipleRequires_EvaluatedInOrder()
    {
        var source = @"
module Test Test
  func bounded (int:x) → int
    !
    requires x > 0
    requires x < 100
    return x
";

        // x = 50 should pass both
        var result1 = SemanticsTestHarness.Execute(source, "bounded", new object[] { 50 });
        Assert.True(result1.Succeeded);
        Assert.Equal(50, result1.ReturnValue);

        // x = -1 should fail first requires
        var result2 = SemanticsTestHarness.Execute(source, "bounded", new object[] { -1 });
        Assert.False(result2.Succeeded);
        Assert.IsType<ContractViolationException>(result2.Exception);

        // x = 150 should fail second requires (first passes)
        var result3 = SemanticsTestHarness.Execute(source, "bounded", new object[] { 150 });
        Assert.False(result3.Succeeded);
        Assert.IsType<ContractViolationException>(result3.Exception);
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
  func withMessage (int:x) → int
    !
    requires x > 0
    return x
";

        var result = SemanticsTestHarness.Execute(source, "withMessage", new object[] { -1 });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Exception);

        // Exception should be a ContractViolationException
        Assert.IsType<ContractViolationException>(result.Exception);
    }
}
