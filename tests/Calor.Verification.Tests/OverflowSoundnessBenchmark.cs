using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Calor.Verification.Tests;

/// <summary>
/// Benchmark tests that validate the soundness of the bit-vector implementation.
/// These tests verify that contracts which can fail due to overflow are correctly
/// DISPROVEN (not falsely proven as they would be with unbounded integers).
///
/// This benchmark measures the "false proof rate" - contracts that would be incorrectly
/// proven with unbounded integer semantics but are correctly disproven with bit-vectors.
///
/// A sound verifier should:
/// - DISPROVE all contracts in the "MustBeDisproven" category (overflow possible)
/// - PROVE all contracts in the "MustBeProven" category (properly bounded)
/// </summary>
public class OverflowSoundnessBenchmark
{
    private readonly ITestOutputHelper _output;

    public OverflowSoundnessBenchmark(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Addition Overflow Tests

    [SkippableFact]
    public void Addition_Unbounded_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // x + 1 > x is FALSE when x = INT_MAX (overflow wraps to INT_MIN)
        // With unbounded integers, this would be (incorrectly) PROVEN
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: Array.Empty<ExpressionNode>(),
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Add, Ref("x"), Int(1)),
                Ref("x")));

        AssertDisproven(result, "x + 1 > x (unbounded)");
    }

    [SkippableFact]
    public void Addition_Bounded_MustBeProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // x + 1 > x is TRUE when x < INT_MAX (no overflow possible)
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.LessThan, Ref("x"), Int(2147483647))
            },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Add, Ref("x"), Int(1)),
                Ref("x")));

        AssertProven(result, "x + 1 > x (bounded by x < INT_MAX)");
    }

    [SkippableFact]
    public void Addition_TwoVariables_Unbounded_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // x > 0 && y > 0 does NOT imply x + y > 0 (overflow possible)
        var result = VerifyContract(
            parameters: new[] { ("x", "i32"), ("y", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.GreaterThan, Ref("x"), Int(0)),
                BinOp(BinaryOperator.GreaterThan, Ref("y"), Int(0))
            },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Add, Ref("x"), Ref("y")),
                Int(0)));

        AssertDisproven(result, "x > 0 && y > 0 => x + y > 0 (unbounded)");
    }

    [SkippableFact]
    public void Addition_TwoVariables_Bounded_MustBeProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // With bounds that prevent overflow, x + y > 0 can be proven
        var result = VerifyContract(
            parameters: new[] { ("x", "i32"), ("y", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.GreaterThan, Ref("x"), Int(0)),
                BinOp(BinaryOperator.LessThan, Ref("x"), Int(1000000000)),
                BinOp(BinaryOperator.GreaterThan, Ref("y"), Int(0)),
                BinOp(BinaryOperator.LessThan, Ref("y"), Int(1000000000))
            },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Add, Ref("x"), Ref("y")),
                Int(0)));

        AssertProven(result, "x > 0 && y > 0 && bounded => x + y > 0");
    }

    #endregion

    #region Subtraction Overflow Tests

    [SkippableFact]
    public void Subtraction_Unbounded_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // x - 1 < x is FALSE when x = INT_MIN (underflow wraps to INT_MAX)
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: Array.Empty<ExpressionNode>(),
            postcondition: BinOp(BinaryOperator.LessThan,
                BinOp(BinaryOperator.Subtract, Ref("x"), Int(1)),
                Ref("x")));

        AssertDisproven(result, "x - 1 < x (unbounded)");
    }

    [SkippableFact]
    public void Subtraction_Bounded_MustBeProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // x - 1 < x is TRUE when x > INT_MIN (no underflow possible)
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.GreaterThan, Ref("x"), Int(-2147483648))
            },
            postcondition: BinOp(BinaryOperator.LessThan,
                BinOp(BinaryOperator.Subtract, Ref("x"), Int(1)),
                Ref("x")));

        AssertProven(result, "x - 1 < x (bounded by x > INT_MIN)");
    }

    #endregion

    #region Multiplication Overflow Tests

    [SkippableFact]
    public void Multiplication_Unbounded_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // x > 0 does NOT imply x * 2 > x (overflow possible)
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.GreaterThan, Ref("x"), Int(0))
            },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Multiply, Ref("x"), Int(2)),
                Ref("x")));

        AssertDisproven(result, "x > 0 => x * 2 > x (unbounded)");
    }

    [SkippableFact]
    public void Multiplication_Bounded_MustBeProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // With bounds that prevent overflow, x * 2 > x can be proven
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.GreaterThan, Ref("x"), Int(0)),
                BinOp(BinaryOperator.LessThan, Ref("x"), Int(1000000000))
            },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Multiply, Ref("x"), Int(2)),
                Ref("x")));

        AssertProven(result, "x > 0 && x < 1B => x * 2 > x");
    }

    [SkippableFact]
    public void Square_Unbounded_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // x >= 0 does NOT imply x * x >= 0 (overflow can make it negative)
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.GreaterOrEqual, Ref("x"), Int(0))
            },
            postcondition: BinOp(BinaryOperator.GreaterOrEqual,
                BinOp(BinaryOperator.Multiply, Ref("x"), Ref("x")),
                Int(0)));

        AssertDisproven(result, "x >= 0 => x * x >= 0 (unbounded)");
    }

    [SkippableFact]
    public void Square_Bounded_MustBeProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // With bounds that prevent overflow (x <= 46340), x * x >= 0 can be proven
        // 46340^2 = 2147395600 < INT_MAX
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.GreaterOrEqual, Ref("x"), Int(0)),
                BinOp(BinaryOperator.LessOrEqual, Ref("x"), Int(46340))
            },
            postcondition: BinOp(BinaryOperator.GreaterOrEqual,
                BinOp(BinaryOperator.Multiply, Ref("x"), Ref("x")),
                Int(0)));

        AssertProven(result, "x >= 0 && x <= 46340 => x * x >= 0");
    }

    #endregion

    #region Division Overflow Tests

    [SkippableFact]
    public void Division_IntMinByNegOne_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // INT_MIN / -1 overflows (result would be INT_MAX + 1)
        // So x / y > x is not always true even when y == -1 and x < 0
        var result = VerifyContract(
            parameters: new[] { ("x", "i32"), ("y", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.NotEqual, Ref("y"), Int(0))
            },
            postcondition: BinOp(BinaryOperator.GreaterOrEqual,
                BinOp(BinaryOperator.Divide, Ref("x"), Ref("y")),
                Ref("x")));

        AssertDisproven(result, "y != 0 => x / y >= x (INT_MIN / -1 case)");
    }

    #endregion

    #region Negation Overflow Tests

    [SkippableFact]
    public void Negation_Unbounded_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // -x > 0 when x < 0 is FALSE when x = INT_MIN (-INT_MIN = INT_MIN)
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.LessThan, Ref("x"), Int(0))
            },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                UnaryOp(UnaryOperator.Negate, Ref("x")),
                Int(0)));

        AssertDisproven(result, "x < 0 => -x > 0 (INT_MIN case)");
    }

    [SkippableFact]
    public void Negation_Bounded_MustBeProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // -x > 0 when x < 0 && x > INT_MIN is TRUE
        var result = VerifyContract(
            parameters: new[] { ("x", "i32") },
            preconditions: new[] {
                BinOp(BinaryOperator.LessThan, Ref("x"), Int(0)),
                BinOp(BinaryOperator.GreaterThan, Ref("x"), Int(-2147483648))
            },
            postcondition: BinOp(BinaryOperator.GreaterThan,
                UnaryOp(UnaryOperator.Negate, Ref("x")),
                Int(0)));

        AssertProven(result, "x < 0 && x > INT_MIN => -x > 0");
    }

    #endregion

    #region Unsigned Type Tests

    [SkippableFact]
    public void Unsigned_AlwaysNonNegative_MustBeProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // For unsigned types, x >= 0 is always true
        var result = VerifyContract(
            parameters: new[] { ("x", "u32") },
            preconditions: Array.Empty<ExpressionNode>(),
            postcondition: BinOp(BinaryOperator.GreaterOrEqual, Ref("x"), Int(0)));

        AssertProven(result, "u32 x >= 0 (unsigned always non-negative)");
    }

    [SkippableFact]
    public void Unsigned_WrapAround_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // For unsigned, x - 1 < x is FALSE when x = 0 (wraps to UINT_MAX)
        var result = VerifyContract(
            parameters: new[] { ("x", "u32") },
            preconditions: Array.Empty<ExpressionNode>(),
            postcondition: BinOp(BinaryOperator.LessThan,
                BinOp(BinaryOperator.Subtract, Ref("x"), Int(1)),
                Ref("x")));

        AssertDisproven(result, "u32: x - 1 < x (wraps at 0)");
    }

    #endregion

    #region 64-bit Overflow Tests

    [SkippableFact]
    public void Addition64_Unbounded_MustBeDisproven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // Same overflow issue exists for 64-bit integers
        var result = VerifyContract(
            parameters: new[] { ("x", "i64") },
            preconditions: Array.Empty<ExpressionNode>(),
            postcondition: BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Add, Ref("x"), Int(1)),
                Ref("x")));

        AssertDisproven(result, "i64: x + 1 > x (unbounded)");
    }

    #endregion

    #region Benchmark Summary

    [SkippableFact]
    public void RunFullBenchmark()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        RunFullBenchmarkCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RunFullBenchmarkCore()
    {
        var sw = Stopwatch.StartNew();

        var mustBeDisproven = new (string Name, Func<ContractVerificationResult> Test)[]
        {
            ("x + 1 > x", () => VerifySimple("i32", null, "(> (+ x 1) x)")),
            ("x - 1 < x", () => VerifySimple("i32", null, "(< (- x 1) x)")),
            ("x > 0 => x * 2 > x", () => VerifySimple("i32", "(> x 0)", "(> (* x 2) x)")),
            ("x >= 0 => x * x >= 0", () => VerifySimple("i32", "(>= x 0)", "(>= (* x x) 0)")),
            ("x < 0 => -x > 0", () => VerifySimple("i32", "(< x 0)", "(> (- 0 x) 0)")),
            ("u32: x - 1 < x", () => VerifySimple("u32", null, "(< (- x 1) x)")),
            ("i64: x + 1 > x", () => VerifySimple("i64", null, "(> (+ x 1) x)")),
        };

        var mustBeProven = new (string Name, Func<ContractVerificationResult> Test)[]
        {
            ("x < MAX => x + 1 > x", () => VerifySimple("i32", "(< x 2147483647)", "(> (+ x 1) x)")),
            ("x > MIN => x - 1 < x", () => VerifySimple("i32", "(> x -2147483648)", "(< (- x 1) x)")),
            ("u32: x >= 0", () => VerifySimple("u32", null, "(>= x 0)")),
        };

        _output.WriteLine("=== Overflow Soundness Benchmark ===\n");

        int disproven = 0, falseProofs = 0;
        _output.WriteLine("Contracts that MUST be DISPROVEN (overflow possible):");
        foreach (var (name, test) in mustBeDisproven)
        {
            var result = test();
            var status = result.Status == ContractVerificationStatus.Disproven ? "PASS" : "FAIL (FALSE PROOF!)";
            if (result.Status == ContractVerificationStatus.Disproven) disproven++;
            else falseProofs++;
            _output.WriteLine($"  [{status}] {name}");
        }

        int proven = 0, missedProofs = 0;
        _output.WriteLine("\nContracts that MUST be PROVEN (properly bounded):");
        foreach (var (name, test) in mustBeProven)
        {
            var result = test();
            var status = result.Status == ContractVerificationStatus.Proven ? "PASS" : "FAIL (MISSED PROOF)";
            if (result.Status == ContractVerificationStatus.Proven) proven++;
            else missedProofs++;
            _output.WriteLine($"  [{status}] {name}");
        }

        sw.Stop();

        _output.WriteLine($"\n=== Summary ===");
        _output.WriteLine($"Correctly disproven: {disproven}/{mustBeDisproven.Length}");
        _output.WriteLine($"Correctly proven: {proven}/{mustBeProven.Length}");
        _output.WriteLine($"False proofs (UNSOUND): {falseProofs}");
        _output.WriteLine($"Missed proofs: {missedProofs}");
        _output.WriteLine($"Total time: {sw.ElapsedMilliseconds}ms");

        // Assert soundness - no false proofs allowed
        Assert.Equal(0, falseProofs);
    }

    #endregion

    #region Helper Methods

    private ContractVerificationResult VerifyContract(
        (string Name, string Type)[] parameters,
        ExpressionNode[] preconditions,
        ExpressionNode postcondition)
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var requires = preconditions.Select(p => new RequiresNode(
            TextSpan.Empty, p, null, new AttributeCollection())).ToArray();

        var ensures = new EnsuresNode(
            TextSpan.Empty, postcondition, null, new AttributeCollection());

        return verifier.VerifyPostcondition(
            parameters.ToList(),
            parameters[0].Type,
            requires,
            ensures);
    }

    private ContractVerificationResult VerifySimple(string type, string? precondition, string postcondition)
    {
        // Simple inline contract verification for the benchmark
        return VerifyContract(
            parameters: new[] { ("x", type) },
            preconditions: precondition != null
                ? new[] { ParseSimpleExpr(precondition) }
                : Array.Empty<ExpressionNode>(),
            postcondition: ParseSimpleExpr(postcondition));
    }

    private ExpressionNode ParseSimpleExpr(string expr)
    {
        // Very simple S-expression parser for benchmark convenience
        expr = expr.Trim();

        if (expr.StartsWith("("))
        {
            var inner = expr.Substring(1, expr.Length - 2);
            var parts = SplitSExpr(inner);
            var op = parts[0];

            return op switch
            {
                ">" => BinOp(BinaryOperator.GreaterThan, ParseSimpleExpr(parts[1]), ParseSimpleExpr(parts[2])),
                ">=" => BinOp(BinaryOperator.GreaterOrEqual, ParseSimpleExpr(parts[1]), ParseSimpleExpr(parts[2])),
                "<" => BinOp(BinaryOperator.LessThan, ParseSimpleExpr(parts[1]), ParseSimpleExpr(parts[2])),
                "<=" => BinOp(BinaryOperator.LessOrEqual, ParseSimpleExpr(parts[1]), ParseSimpleExpr(parts[2])),
                "+" => BinOp(BinaryOperator.Add, ParseSimpleExpr(parts[1]), ParseSimpleExpr(parts[2])),
                "-" => BinOp(BinaryOperator.Subtract, ParseSimpleExpr(parts[1]), ParseSimpleExpr(parts[2])),
                "*" => BinOp(BinaryOperator.Multiply, ParseSimpleExpr(parts[1]), ParseSimpleExpr(parts[2])),
                "/" => BinOp(BinaryOperator.Divide, ParseSimpleExpr(parts[1]), ParseSimpleExpr(parts[2])),
                _ => throw new ArgumentException($"Unknown operator: {op}")
            };
        }

        if (int.TryParse(expr, out var intVal))
            return Int(intVal);

        if (long.TryParse(expr, out var longVal))
            return Int((int)longVal); // Truncate for simplicity

        return Ref(expr);
    }

    private List<string> SplitSExpr(string expr)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;

        foreach (var c in expr)
        {
            if (c == '(' ) depth++;
            else if (c == ')') depth--;
            else if (c == ' ' && depth == 0)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }

    private void AssertDisproven(ContractVerificationResult result, string description)
    {
        _output.WriteLine($"[{result.Status}] {description}");
        if (result.Status == ContractVerificationStatus.Proven)
        {
            _output.WriteLine("  WARNING: This is a FALSE PROOF - would fail at runtime!");
        }
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    private void AssertProven(ContractVerificationResult result, string description)
    {
        _output.WriteLine($"[{result.Status}] {description}");
        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
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
