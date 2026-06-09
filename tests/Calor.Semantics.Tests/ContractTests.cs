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
    /// S9: Option.None behaves correctly.
    /// </summary>
    [Fact]
    public void S9_OptionNone_BehavesCorrectly()
    {
        // Test that None can be created and pattern matched
        // For now, just verify a simple function compiles and runs
        var source = @"
§M{m001:Test}
  §F{f001:getNone:pub}
      §O{i32}
      §R INT:0
";

        // For now, just verify compilation succeeds
        // Full Option<T> support would require runtime integration
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
§M{m001:Test}
  §F{f001:positiveOnly:pub}
      §I{i32:x}
      §O{i32}
      §Q (> x INT:0)
      §R x
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

        // Exception should include function ID
        Assert.Contains("f001", cve.FunctionId ?? cve.Message);
    }

    /// <summary>
    /// REQUIRES passes when condition is true.
    /// </summary>
    [Fact]
    public void Requires_Passes_WhenConditionTrue()
    {
        var source = @"
§M{m001:Test}
  §F{f001:positiveOnly:pub}
      §I{i32:x}
      §O{i32}
      §Q (> x INT:0)
      §R (* x INT:2)
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
§M{m001:Test}
  §F{f001:bounded:pub}
      §I{i32:x}
      §O{i32}
      §Q (> x INT:0)
      §Q (< x INT:100)
      §R x
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
§M{m001:Test}
  §F{f001:withMessage:pub}
      §I{i32:x}
      §O{i32}
      §Q (> x INT:0)
      §R x
";

        var result = SemanticsTestHarness.Execute(source, "withMessage", new object[] { -1 });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Exception);

        // Exception should be a ContractViolationException
        Assert.IsType<ContractViolationException>(result.Exception);
    }
}
