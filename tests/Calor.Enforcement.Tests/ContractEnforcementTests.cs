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
          func Divide public (int:a, int:b | (# != 0)) → int
            !
            return a / b
        """;

    private const string EnsuresCurrentNativeProgram = """
        module Test Test
          func GetPositive public (int:n) → int
            !
            => result > 0
            return n
        """;

    private const string ModeOffCurrentNativeProgram = """
        module Test Test
          func Divide public (int:a, int:b | (# != 0)) → int
            !
            return a / b
        """;

    private const string MultipleReturnsCurrentNativeProgram = """
        module Test Test
          func Abs public (int:n) → int
            !
            => result >= 0
            if n < 0
              return 0 - n
            return n
        """;

    // #1618: a refinement entry check throws ArgumentOutOfRangeException and carries no
    // function id, so the id is observed through a postcondition violation.
    private const string FunctionIdCurrentNativeProgram = """
        module Test Test
          func RequirePositive public (int:n) → int
            !
            => result > 0
            return n
        """;

    private const string NoEffectsCurrentNativeProgram = """
        module Test Test
          func SafeAdd public (int:a | (# >= 0), int:b) → int
            !
            => result >= a
            return a + b
        """;

    [Fact]
    public void C1_RequiresFails_ThrowsContractViolation()
    {
        var source = TestHarness.LoadScenario("Contracts/C1_requires_throws.calr");
        source = RequiresCurrentNativeProgram;

        // Call Divide with b=0. #1618: the precondition is the refinement on b, whose entry
        // check throws ArgumentOutOfRangeException naming the parameter.
        var result = TestHarness.Execute(source, "Divide", new object[] { 10, 0 });

        Assert.NotNull(result.Exception);
        var ex = Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
        Assert.Equal("b", ex.ParamName);
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
    public void C3_ModeOff_RefinementEntryCheckStillRuns()
    {
        var source = TestHarness.LoadScenario("Contracts/C3_mode_off.calr");
        source = ModeOffCurrentNativeProgram;
        var options = new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Off
        };

        // Call Divide with b=0 with contract checks off. #1618: the precondition is the
        // refinement on b, and refinement entry checks are emitted in every contract mode,
        // so the call fails at entry instead of reaching the division.
        var result = TestHarness.Execute(source, "Divide", new object[] { 10, 0 }, options);

        Assert.NotNull(result.Exception);
        Assert.IsNotType<ContractViolationException>(result.Exception);
        Assert.Equal("b", Assert.IsType<ArgumentOutOfRangeException>(result.Exception).ParamName);
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
        // #1618: a precondition is a refinement whose entry check does not vary by mode, so
        // the Debug/Release detail difference is observed on a postcondition.
        var source = TestHarness.LoadScenario("Contracts/C2_ensures_throws.calr");
        source = EnsuresCurrentNativeProgram;
        var options = new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Debug
        };

        var result = TestHarness.Execute(source, "GetPositive", new object[] { -5 }, options);

        Assert.NotNull(result.Exception);
        Assert.IsType<ContractViolationException>(result.Exception);

        var ex = (ContractViolationException)result.Exception;
        // Debug mode should have condition
        Assert.NotNull(ex.Condition);
        Assert.Contains(">", ex.Condition);  // The condition result > 0
    }

    [Fact]
    public void ReleaseMode_HasMinimalInfo()
    {
        // #1618: observed on a postcondition (see DebugMode_IncludesDetailedInfo).
        var source = TestHarness.LoadScenario("Contracts/C2_ensures_throws.calr");
        source = EnsuresCurrentNativeProgram;
        var options = new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Release
        };

        var result = TestHarness.Execute(source, "GetPositive", new object[] { -5 }, options);

        Assert.NotNull(result.Exception);
        Assert.IsType<ContractViolationException>(result.Exception);

        var ex = (ContractViolationException)result.Exception;
        // Release mode has minimal info
        Assert.Equal(Assert.Single(TestHarness.Compile(source, options).Ast!.Functions).Id, ex.FunctionId);
        Assert.Equal(ContractKind.Ensures, ex.Kind);
        // Release mode may not have condition
        Assert.Null(ex.Condition);
    }

    [Fact]
    public void ContractWithNoEffects_CompilesPure()
    {
        // B1 scenario: Function with contracts but no effects is valid
        var source = @"
module Test Test block
  func SafeAdd (int:a | (# >= 0), int:b) → int
    !
    => (result >= a)
    return (a + b)
";
        source = NoEffectsCurrentNativeProgram;
        var result = TestHarness.Compile(source);

        // Should compile - contracts don't require throw effect
        Assert.False(result.HasErrors, $"Should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }
}
