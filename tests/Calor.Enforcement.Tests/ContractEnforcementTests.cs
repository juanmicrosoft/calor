using Calor.Compiler;
using Calor.Runtime;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for contract enforcement at runtime.
/// </summary>
public class ContractEnforcementTests
{
    // Keep the retired scenario loads and inline source intact below.
    // These explicit native programs preserve their contracts, absent effect
    // declarations, checked arithmetic, and Test.TestModule CLR owner.
    private const string RequiresCurrentNativeProgram = """
        module Test Test
          func Divide public (int:a, int:b) → int
            requires b != 0
            return a / b
        """;

    private const string EnsuresCurrentNativeProgram = """
        module Test Test
          func GetPositive public (int:n) → int
            => result > 0
            return n
        """;

    private const string ModeOffCurrentNativeProgram = """
        module Test Test
          func Divide public (int:a, int:b) → int
            requires b != 0
            return a / b
        """;

    private const string MultipleReturnsCurrentNativeProgram = """
        module Test Test
          func Abs public (int:n) → int
            => result >= 0
            if n < 0
              return 0 - n
            return n
        """;

    private const string FunctionIdCurrentNativeProgram = """
        module Test Test
          func RequirePositive public (int:n) → int
            requires n > 0
            return n
        """;

    private const string NoEffectsCurrentNativeProgram = """
        module Test Test
          func SafeAdd public (int:a, int:b) → int
            requires a >= 0
            => result >= a
            return a + b
        """;

    [Fact]
    public void C1_RequiresFails_ThrowsContractViolation()
    {
        var source = TestHarness.LoadScenario("Contracts/C1_requires_throws.calr");
        source = RequiresCurrentNativeProgram;

        // Call Divide with b=0, should throw ContractViolationException
        var result = TestHarness.Execute(source, "Divide", new object[] { 10, 0 });

        Assert.NotNull(result.Exception);
        Assert.IsType<ContractViolationException>(result.Exception);

        var ex = (ContractViolationException)result.Exception;
        Assert.Equal(ContractKind.Requires, ex.Kind);
        Assert.Equal(Assert.Single(TestHarness.Compile(source).Ast!.Functions).Id, ex.FunctionId);
    }

    [Fact]
    public void C1_RequiresPasses_ReturnsNormally()
    {
        var source = TestHarness.LoadScenario("Contracts/C1_requires_throws.calr");
        source = RequiresCurrentNativeProgram;

        // Call Divide with valid arguments
        var result = TestHarness.Execute(source, "Divide", new object[] { 10, 2 });

        Assert.True(result.Succeeded, $"Should succeed. Exception: {result.Exception?.Message}");
        Assert.Equal(5, result.ReturnValue);
    }

    [Fact]
    public void C2_EnsuresFails_ThrowsContractViolation()
    {
        var source = TestHarness.LoadScenario("Contracts/C2_ensures_throws.calr");
        source = EnsuresCurrentNativeProgram;

        // Call GetPositive with n=-5, postcondition fails
        var result = TestHarness.Execute(source, "GetPositive", new object[] { -5 });

        Assert.NotNull(result.Exception);
        Assert.IsType<ContractViolationException>(result.Exception);

        var ex = (ContractViolationException)result.Exception;
        Assert.Equal(ContractKind.Ensures, ex.Kind);
    }

    [Fact]
    public void C2_EnsuresPasses_ReturnsNormally()
    {
        var source = TestHarness.LoadScenario("Contracts/C2_ensures_throws.calr");
        source = EnsuresCurrentNativeProgram;

        // Call GetPositive with n=5, postcondition passes
        var result = TestHarness.Execute(source, "GetPositive", new object[] { 5 });

        Assert.True(result.Succeeded, $"Should succeed. Exception: {result.Exception?.Message}");
        Assert.Equal(5, result.ReturnValue);
    }

    [Fact]
    public void C3_ModeOff_NoExceptionEvenOnViolation()
    {
        var source = TestHarness.LoadScenario("Contracts/C3_mode_off.calr");
        source = ModeOffCurrentNativeProgram;
        var options = new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Off
        };

        // Call Divide with b=0, but contract checks are off
        // This will throw DivideByZeroException, not ContractViolationException
        var result = TestHarness.Execute(source, "Divide", new object[] { 10, 0 }, options);

        // Should not throw ContractViolationException
        if (result.Exception != null)
        {
            Assert.IsNotType<ContractViolationException>(result.Exception);
            // Should be DivideByZeroException instead
            Assert.IsType<DivideByZeroException>(result.Exception);
        }
    }

    [Fact]
    public void C4_MultipleReturns_EnsuresCheckedOnAllPaths()
    {
        var source = TestHarness.LoadScenario("Contracts/C4_multiple_returns.calr");
        source = MultipleReturnsCurrentNativeProgram;

        // Test positive path
        var result1 = TestHarness.Execute(source, "Abs", new object[] { 5 });
        Assert.True(result1.Succeeded);
        Assert.Equal(5, result1.ReturnValue);

        // Test negative path (should return positive)
        var result2 = TestHarness.Execute(source, "Abs", new object[] { -5 });
        Assert.True(result2.Succeeded);
        Assert.Equal(5, result2.ReturnValue);

        // Test zero
        var result3 = TestHarness.Execute(source, "Abs", new object[] { 0 });
        Assert.True(result3.Succeeded);
        Assert.Equal(0, result3.ReturnValue);
    }

    [Fact]
    public void C5_FunctionId_MatchesDefinedId()
    {
        var source = TestHarness.LoadScenario("Contracts/C5_function_id.calr");
        source = FunctionIdCurrentNativeProgram;

        // Call with invalid input to trigger contract violation
        var result = TestHarness.Execute(source, "RequirePositive", new object[] { -1 });

        Assert.NotNull(result.Exception);
        Assert.IsType<ContractViolationException>(result.Exception);

        var ex = (ContractViolationException)result.Exception;
        Assert.Equal(Assert.Single(TestHarness.Compile(source).Ast!.Functions).Id, ex.FunctionId);
    }

    [Fact]
    public void DebugMode_IncludesDetailedInfo()
    {
        var source = TestHarness.LoadScenario("Contracts/C1_requires_throws.calr");
        source = RequiresCurrentNativeProgram;
        var options = new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Debug
        };

        var result = TestHarness.Execute(source, "Divide", new object[] { 10, 0 }, options);

        Assert.NotNull(result.Exception);
        Assert.IsType<ContractViolationException>(result.Exception);

        var ex = (ContractViolationException)result.Exception;
        // Debug mode should have condition
        Assert.NotNull(ex.Condition);
        Assert.Contains("!=", ex.Condition);  // The condition b != 0
    }

    [Fact]
    public void ReleaseMode_HasMinimalInfo()
    {
        var source = TestHarness.LoadScenario("Contracts/C1_requires_throws.calr");
        source = RequiresCurrentNativeProgram;
        var options = new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Release
        };

        var result = TestHarness.Execute(source, "Divide", new object[] { 10, 0 }, options);

        Assert.NotNull(result.Exception);
        Assert.IsType<ContractViolationException>(result.Exception);

        var ex = (ContractViolationException)result.Exception;
        // Release mode has minimal info
        Assert.Equal(Assert.Single(TestHarness.Compile(source, options).Ast!.Functions).Id, ex.FunctionId);
        Assert.Equal(ContractKind.Requires, ex.Kind);
        // Release mode may not have condition
        Assert.Null(ex.Condition);
    }

    [Fact]
    public void ContractWithNoEffects_CompilesPure()
    {
        // B1 scenario: Function with contracts but no effects is valid
        var source = @"
§M{Test}
  §F{SafeAdd:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §Q (>= a INT:0)
      §S (>= result a)
      §R (+ a b)
";
        source = NoEffectsCurrentNativeProgram;
        var result = TestHarness.Compile(source);

        // Should compile - contracts don't require throw effect
        Assert.False(result.HasErrors, $"Should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }
}
