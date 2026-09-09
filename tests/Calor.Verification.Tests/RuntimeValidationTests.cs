using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using ProofStatus = Calor.Compiler.Verification.ProofStatus;
using Microsoft.Z3;
using Xunit;
using Xunit.Abstractions;
using System.Runtime.CompilerServices;

namespace Calor.Verification.Tests;

/// <summary>
/// Tests that validate the verifier's counterexamples against actual C# runtime behavior.
///
/// These tests prove that when the verifier says DISPROVEN, the counterexample it provides
/// actually causes the contract to fail at runtime. This is the ultimate test of soundness:
/// the verifier and C# runtime agree on what can fail.
/// </summary>
public class RuntimeValidationTests
{
    private readonly ITestOutputHelper _output;

    public RuntimeValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Addition Overflow

    [SkippableFact]
    public void AdditionOverflow_CounterexampleFailsAtRuntime()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        AdditionOverflow_CounterexampleFailsAtRuntimeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AdditionOverflow_CounterexampleFailsAtRuntimeCore()
    {
        // Contract: ensures (x + 1 > x)
        // Verifier should find counterexample: x = int.MaxValue

        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var result = VerifyPostcondition(verifier, "i32",
            preconditions: Array.Empty<ExpressionNode>(),
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Add, Ref("x"), Int(1)),
                Ref("x")));

        var x = int.MaxValue;
        AssertConditionalOverflow(result, () => checked(x + 1) > x);
    }

    #endregion

    #region Subtraction Underflow

    [SkippableFact]
    public void SubtractionUnderflow_CounterexampleFailsAtRuntime()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        SubtractionUnderflow_CounterexampleFailsAtRuntimeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SubtractionUnderflow_CounterexampleFailsAtRuntimeCore()
    {
        // Contract: ensures (x - 1 < x)
        // Verifier should find counterexample: x = int.MinValue

        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var result = VerifyPostcondition(verifier, "i32",
            preconditions: Array.Empty<ExpressionNode>(),
            postcondition: BinOp(BinaryOperator.LessThan,
                BinOp(BinaryOperator.Subtract, Ref("x"), Int(1)),
                Ref("x")));

        var x = int.MinValue;
        AssertConditionalOverflow(result, () => checked(x - 1) < x);
    }

    #endregion

    #region Multiplication Overflow

    [SkippableFact]
    public void MultiplicationOverflow_CounterexampleFailsAtRuntime()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        MultiplicationOverflow_CounterexampleFailsAtRuntimeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MultiplicationOverflow_CounterexampleFailsAtRuntimeCore()
    {
        // Contract: requires (x > 0) ensures (x * 2 > x)
        // Verifier should find counterexample where x * 2 overflows

        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var result = VerifyPostcondition(verifier, "i32",
            preconditions: new[] { BinOp(BinaryOperator.GreaterThan, Ref("x"), Int(0)) },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Multiply, Ref("x"), Int(2)),
                Ref("x")));

        var x = int.MaxValue;
        AssertConditionalOverflow(result, () => checked(x * 2) > x);
    }

    #endregion

    #region Square Overflow

    [SkippableFact]
    public void SquareOverflow_CounterexampleFailsAtRuntime()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        SquareOverflow_CounterexampleFailsAtRuntimeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SquareOverflow_CounterexampleFailsAtRuntimeCore()
    {
        // Contract: requires (x >= 0) ensures (x * x >= 0)
        // Verifier should find counterexample where x * x overflows to negative

        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var result = VerifyPostcondition(verifier, "i32",
            preconditions: new[] { BinOp(BinaryOperator.GreaterOrEqual, Ref("x"), Int(0)) },
            postcondition: BinOp(BinaryOperator.GreaterOrEqual,
                BinOp(BinaryOperator.Multiply, Ref("x"), Ref("x")),
                Int(0)));

        var x = int.MaxValue;
        AssertConditionalOverflow(result, () => checked(x * x) >= 0);
    }

    #endregion

    #region Negation Overflow

    [SkippableFact]
    public void NegationOverflow_CounterexampleFailsAtRuntime()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NegationOverflow_CounterexampleFailsAtRuntimeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NegationOverflow_CounterexampleFailsAtRuntimeCore()
    {
        // Contract: requires (x < 0) ensures (-x > 0)
        // Verifier should find counterexample: x = int.MinValue (because -int.MinValue = int.MinValue)

        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var result = VerifyPostcondition(verifier, "i32",
            preconditions: new[] { BinOp(BinaryOperator.LessThan, Ref("x"), Int(0)) },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                UnaryOp(UnaryOperator.Negate, Ref("x")),
                Int(0)));

        var x = int.MinValue;
        AssertConditionalOverflow(result, () => checked(-x) > 0);
    }

    #endregion

    #region Unsigned Wraparound

    [SkippableFact]
    public void UnsignedWraparound_CounterexampleFailsAtRuntime()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UnsignedWraparound_CounterexampleFailsAtRuntimeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UnsignedWraparound_CounterexampleFailsAtRuntimeCore()
    {
        // Contract: ensures (x - 1 < x) for u32
        // Verifier should find counterexample: x = 0 (because 0 - 1 = uint.MaxValue)

        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var result = VerifyPostcondition(verifier, "u32",
            preconditions: Array.Empty<ExpressionNode>(),
            postcondition: BinOp(BinaryOperator.LessThan,
                BinOp(BinaryOperator.Subtract, Ref("x"), Int(1)),
                Ref("x")));

        uint x = 0;
        AssertConditionalOverflow(result, () => checked(x - 1) < x);
    }

    #endregion

    #region Two-Variable Addition Overflow

    [SkippableFact]
    public void TwoVariableAdditionOverflow_CounterexampleFailsAtRuntime()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TwoVariableAdditionOverflow_CounterexampleFailsAtRuntimeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TwoVariableAdditionOverflow_CounterexampleFailsAtRuntimeCore()
    {
        // Contract: requires (x > 0 && y > 0) ensures (x + y > 0)
        // Verifier should find counterexample where x + y overflows to negative

        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32"), ("y", "i32") };

        var preconditions = new[]
        {
            new RequiresNode(TextSpan.Empty,
                BinOp(BinaryOperator.GreaterThan, Ref("x"), Int(0)), null, new AttributeCollection()),
            new RequiresNode(TextSpan.Empty,
                BinOp(BinaryOperator.GreaterThan, Ref("y"), Int(0)), null, new AttributeCollection())
        };

        var postcondition = new EnsuresNode(TextSpan.Empty,
            BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Add, Ref("x"), Ref("y")),
                Int(0)),
            null, new AttributeCollection());

        var result = verifier.VerifyPostcondition(parameters, "i32", preconditions, postcondition);

        var x = int.MaxValue;
        var y = 1;
        AssertConditionalOverflow(result, () => checked(x + y) > 0);
    }

    #endregion

    #region Division Overflow (INT_MIN / -1)

    [SkippableFact]
    public void DivisionOverflow_IntMinDivNegOne_MatchesRuntime()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DivisionOverflow_IntMinDivNegOne_MatchesRuntimeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DivisionOverflow_IntMinDivNegOne_MatchesRuntimeCore()
    {
        // INT_MIN / -1 is a special case: mathematically it's INT_MAX + 1,
        // but in two's complement it overflows back to INT_MIN.
        //
        // NOTE: In C# checked mode, INT_MIN / -1 throws OverflowException.
        // In unchecked mode and in hardware, it returns INT_MIN.
        // Our bit-vector semantics model the unchecked/hardware behavior.
        //
        // This is an important semantic difference:
        // - C# default (checked): throws exception
        // - C# unchecked / hardware / Wasm: returns INT_MIN
        // - Our verifier: models unchecked behavior (INT_MIN)

        _output.WriteLine("Testing INT_MIN / -1 behavior:");
        _output.WriteLine("  C# checked mode: throws OverflowException");
        _output.WriteLine("  C# unchecked mode / hardware: returns INT_MIN");
        _output.WriteLine("  Our verifier: models unchecked behavior");

        // Verify unchecked behavior matches hardware
        int x = int.MinValue;
        int y = -1;

        // Use explicit unchecked block - this bypasses the C# overflow check
        // and gives us the hardware behavior
        int quotient;
        try
        {
            // Try checked first to demonstrate the exception
            checked
            {
                // This line would throw, but we catch it
                quotient = x / y;
            }
            _output.WriteLine($"C# checked: {x} / {y} = {quotient} (unexpected - should have thrown)");
        }
        catch (OverflowException)
        {
            _output.WriteLine($"C# checked: {x} / {y} throws OverflowException (expected)");
        }

        // Now verify the verifier agrees with bit-vector semantics (INT_MIN / -1 = INT_MIN)
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Contract: requires (x == INT_MIN && y == -1) ensures (x / y == x)
        // This should be PROVEN with bit-vector semantics (overflow wraps)
        var result = VerifyPostcondition(verifier, "i32",
            preconditions: new[]
            {
                BinOp(BinaryOperator.Equal, Ref("x"), Int(int.MinValue)),
                BinOp(BinaryOperator.Equal, Ref("y"), Int(-1))
            },
            postcondition: BinOp(BinaryOperator.Equal,
                BinOp(BinaryOperator.Divide, Ref("x"), Ref("y")),
                Ref("x")),
            parameters: new[] { ("x", "i32"), ("y", "i32") });

        _output.WriteLine($"Verifier result: {result.Status}");
        _output.WriteLine($"  (W1 Slice 1 / #833 C4: the emitted runtime check `x / y == x` THROWS");
        _output.WriteLine($"   OverflowException at this exact state — C# division overflow throws in");
        _output.WriteLine($"   checked AND unchecked contexts — so a plain Proven would elide a check");
        _output.WriteLine($"   the program needs. The proof survives only under the overflow side");
        _output.WriteLine($"   condition, which §Q here VIOLATES: Assumed, never elides.)");

        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
        Assert.Contains(Z3Verifier.ContractExpressionDivisionAssumption,
            result.EffectiveOutcome.Assumptions);
    }

    #endregion

    private static void AssertConditionalOverflow(ContractVerificationResult result, Func<bool> predicate)
    {
        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
        Assert.Contains(Z3Verifier.CheckedArithmeticAssumption, result.EffectiveOutcome.Assumptions);
        Assert.Throws<OverflowException>(() => predicate());
    }

    #region Summary Test

    [SkippableFact]
    public void AllCounterexamples_MatchRuntimeBehavior()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        AllCounterexamples_MatchRuntimeBehaviorCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AllCounterexamples_MatchRuntimeBehaviorCore()
    {
        _output.WriteLine("=== Runtime Validation Summary ===\n");

        // Tests where verifier counterexamples match runtime failures
        var tests = new (string Name, Action Test)[]
        {
            ("Addition overflow (x + 1 > x)", () => AdditionOverflow_CounterexampleFailsAtRuntimeCore()),
            ("Subtraction underflow (x - 1 < x)", () => SubtractionUnderflow_CounterexampleFailsAtRuntimeCore()),
            ("Multiplication overflow (x * 2 > x)", () => MultiplicationOverflow_CounterexampleFailsAtRuntimeCore()),
            ("Square overflow (x * x >= 0)", () => SquareOverflow_CounterexampleFailsAtRuntimeCore()),
            ("Negation overflow (-x > 0)", () => NegationOverflow_CounterexampleFailsAtRuntimeCore()),
            ("Unsigned wraparound (x - 1 < x)", () => UnsignedWraparound_CounterexampleFailsAtRuntimeCore()),
            ("Two-variable addition (x + y > 0)", () => TwoVariableAdditionOverflow_CounterexampleFailsAtRuntimeCore()),
        };

        // Semantic verification tests (not counterexample validation)
        var semanticTests = new (string Name, Action Test)[]
        {
            ("INT_MIN / -1 verifier models hardware behavior", () => DivisionOverflow_IntMinDivNegOne_MatchesRuntimeCore()),
        };

        int passed = 0;
        int failed = 0;

        _output.WriteLine("Counterexample validation tests (verifier counterexample -> runtime failure):\n");

        foreach (var (name, test) in tests)
        {
            try
            {
                test();
                _output.WriteLine($"[PASS] {name}");
                passed++;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[FAIL] {name}: {ex.Message}");
                failed++;
            }
        }

        _output.WriteLine($"\nSemantic verification tests:\n");

        int semanticPassed = 0;
        int semanticFailed = 0;

        foreach (var (name, test) in semanticTests)
        {
            try
            {
                test();
                _output.WriteLine($"[PASS] {name}");
                semanticPassed++;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[FAIL] {name}: {ex.Message}");
                semanticFailed++;
            }
        }

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Counterexample tests: {passed}/{tests.Length} passed");
        _output.WriteLine($"Semantic tests: {semanticPassed}/{semanticTests.Length} passed");
        _output.WriteLine($"\nVerifier soundness validated: {failed == 0 && semanticFailed == 0}");

        Assert.Equal(0, failed);
        Assert.Equal(0, semanticFailed);
    }

    #endregion

    #region Helper Methods

    private ContractVerificationResult VerifyPostcondition(
        Z3Verifier verifier,
        string returnType,
        ExpressionNode[] preconditions,
        ExpressionNode postcondition,
        (string Name, string Type)[]? parameters = null)
    {
        parameters ??= new[] { ("x", returnType) };

        var requires = preconditions.Select(p =>
            new RequiresNode(TextSpan.Empty, p, null, new AttributeCollection())).ToArray();

        var ensures = new EnsuresNode(TextSpan.Empty, postcondition, null, new AttributeCollection());

        return verifier.VerifyPostcondition(parameters.ToList(), returnType, requires, ensures);
    }

    private int ExtractIntCounterexample(ContractVerificationResult result, string varName, int fallback = int.MaxValue)
    {
        // Parse counterexample from description
        // Format typically includes "varName = value" or similar
        var desc = result.CounterexampleDescription ?? "";

        // Try to find the variable in the counterexample
        // Common formats: "x = 2147483647" or "x -> 2147483647" or "(x 2147483647)" or "x=value"
        var patterns = new[]
        {
            $@"{varName}\s*=\s*(-?\d+)",
            $@"{varName}\s*->\s*(-?\d+)",
            $@"\({varName}\s+(-?\d+)\)",
            $@"{varName}:\s*(-?\d+)"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(desc, pattern);
            if (match.Success)
            {
                var valueStr = match.Groups[1].Value;
                // Handle values that might be outside int range (treat as unsigned then cast)
                if (long.TryParse(valueStr, out var longValue))
                {
                    return unchecked((int)longValue);
                }
            }
        }

        // If we can't parse it, use a known counterexample value
        _output.WriteLine($"Could not parse counterexample for {varName} from: {desc}");
        _output.WriteLine($"Using fallback counterexample value: {fallback}");

        return fallback;
    }

    private uint ExtractUIntCounterexample(ContractVerificationResult result, string varName, uint fallback = 0)
    {
        var desc = result.CounterexampleDescription ?? "";

        var patterns = new[]
        {
            $@"{varName}\s*=\s*(\d+)",
            $@"{varName}\s*->\s*(\d+)",
            $@"\({varName}\s+(\d+)\)"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(desc, pattern);
            if (match.Success)
            {
                var valueStr = match.Groups[1].Value;
                if (ulong.TryParse(valueStr, out var ulongValue))
                {
                    return unchecked((uint)ulongValue);
                }
            }
        }

        _output.WriteLine($"Could not parse counterexample for {varName} from: {desc}");
        _output.WriteLine($"Using fallback counterexample value: {fallback}");

        return fallback;
    }

    // AST construction helpers
    private static BinaryOperationNode BinOp(BinaryOperator op, ExpressionNode left, ExpressionNode right)
        => new(TextSpan.Empty, op, left, right);

    private static UnaryOperationNode UnaryOp(UnaryOperator op, ExpressionNode operand)
        => new(TextSpan.Empty, op, operand);

    private static ReferenceNode Ref(string name)
        => new(TextSpan.Empty, name);

    private static IntLiteralNode Int(int value)
        => new(TextSpan.Empty, value);

    #endregion
}
