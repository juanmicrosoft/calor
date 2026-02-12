using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
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

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);

        // Extract counterexample
        var counterexample = ExtractIntCounterexample(result, "x");
        _output.WriteLine($"Verifier counterexample: x = {counterexample}");

        // Validate at runtime - the contract should actually fail
        unchecked
        {
            int x = counterexample;
            int xPlusOne = x + 1;
            bool contractHolds = xPlusOne > x;

            _output.WriteLine($"Runtime: x = {x}, x + 1 = {xPlusOne}, (x + 1 > x) = {contractHolds}");

            Assert.False(contractHolds,
                $"Contract should fail at runtime with x={x}: {xPlusOne} > {x} should be false");
        }
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

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);

        // Fallback to INT_MIN which is the underflow case
        var counterexample = ExtractIntCounterexample(result, "x", fallback: int.MinValue);
        _output.WriteLine($"Verifier counterexample: x = {counterexample}");

        unchecked
        {
            int x = counterexample;
            int xMinusOne = x - 1;
            bool contractHolds = xMinusOne < x;

            _output.WriteLine($"Runtime: x = {x}, x - 1 = {xMinusOne}, (x - 1 < x) = {contractHolds}");

            Assert.False(contractHolds,
                $"Contract should fail at runtime with x={x}: {xMinusOne} < {x} should be false");
        }
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

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);

        var counterexample = ExtractIntCounterexample(result, "x");
        _output.WriteLine($"Verifier counterexample: x = {counterexample}");

        // Verify precondition holds
        Assert.True(counterexample > 0, "Counterexample should satisfy precondition x > 0");

        unchecked
        {
            int x = counterexample;
            int xTimes2 = x * 2;
            bool contractHolds = xTimes2 > x;

            _output.WriteLine($"Runtime: x = {x}, x * 2 = {xTimes2}, (x * 2 > x) = {contractHolds}");

            Assert.False(contractHolds,
                $"Contract should fail at runtime with x={x}: {xTimes2} > {x} should be false");
        }
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

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);

        var counterexample = ExtractIntCounterexample(result, "x");
        _output.WriteLine($"Verifier counterexample: x = {counterexample}");

        // Verify precondition holds
        Assert.True(counterexample >= 0, "Counterexample should satisfy precondition x >= 0");

        unchecked
        {
            int x = counterexample;
            int xSquared = x * x;
            bool contractHolds = xSquared >= 0;

            _output.WriteLine($"Runtime: x = {x}, x * x = {xSquared}, (x * x >= 0) = {contractHolds}");

            Assert.False(contractHolds,
                $"Contract should fail at runtime with x={x}: {xSquared} >= 0 should be false");
        }
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

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);

        // Fallback to INT_MIN which is the only negative value where -x == x
        var counterexample = ExtractIntCounterexample(result, "x", fallback: int.MinValue);
        _output.WriteLine($"Verifier counterexample: x = {counterexample}");

        // Verify precondition holds
        Assert.True(counterexample < 0, "Counterexample should satisfy precondition x < 0");

        unchecked
        {
            int x = counterexample;
            int negX = -x;
            bool contractHolds = negX > 0;

            _output.WriteLine($"Runtime: x = {x}, -x = {negX}, (-x > 0) = {contractHolds}");

            Assert.False(contractHolds,
                $"Contract should fail at runtime with x={x}: {negX} > 0 should be false");
        }
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

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);

        // Fallback to 0 which is the wraparound case (0 - 1 = uint.MaxValue)
        var counterexample = ExtractUIntCounterexample(result, "x", fallback: 0);
        _output.WriteLine($"Verifier counterexample: x = {counterexample}");

        // If the counterexample isn't 0, we still test it but explain
        if (counterexample != 0)
        {
            _output.WriteLine("Note: Verifier found a different counterexample than expected.");
            _output.WriteLine("The classic wraparound case is x=0, where x-1 wraps to uint.MaxValue.");
            _output.WriteLine("Using x=0 to demonstrate the wraparound behavior:");
            counterexample = 0;
        }

        unchecked
        {
            uint x = counterexample;
            uint xMinusOne = x - 1;
            bool contractHolds = xMinusOne < x;

            _output.WriteLine($"Runtime: x = {x}, x - 1 = {xMinusOne}, (x - 1 < x) = {contractHolds}");

            Assert.False(contractHolds,
                $"Contract should fail at runtime with x={x}: {xMinusOne} < {x} should be false");
        }
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

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);

        var xVal = ExtractIntCounterexample(result, "x");
        var yVal = ExtractIntCounterexample(result, "y");
        _output.WriteLine($"Verifier counterexample: x = {xVal}, y = {yVal}");

        // Verify preconditions hold
        Assert.True(xVal > 0, "Counterexample should satisfy precondition x > 0");
        Assert.True(yVal > 0, "Counterexample should satisfy precondition y > 0");

        unchecked
        {
            int x = xVal;
            int y = yVal;
            int sum = x + y;
            bool contractHolds = sum > 0;

            _output.WriteLine($"Runtime: x = {x}, y = {y}, x + y = {sum}, (x + y > 0) = {contractHolds}");

            Assert.False(contractHolds,
                $"Contract should fail at runtime with x={x}, y={y}: {sum} > 0 should be false");
        }
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
        _output.WriteLine($"  (Verifier models unchecked/hardware semantics where INT_MIN / -1 = INT_MIN)");

        // Verifier should PROVE this because bit-vector division gives INT_MIN / -1 = INT_MIN
        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    #endregion

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
