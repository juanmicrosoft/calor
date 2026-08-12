using System.Runtime.CompilerServices;
using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Microsoft.CSharp.RuntimeBinder;
using Xunit;

namespace Calor.Verification.Tests;

public sealed class NumericExecutableSemanticsTests
{
    private static readonly TextSpan Span = TextSpan.Empty;
    private static readonly AttributeCollection Attributes = new();
    private static readonly BinaryOperator[] Operators =
    [
        BinaryOperator.Add,
        BinaryOperator.Subtract,
        BinaryOperator.Multiply,
        BinaryOperator.Divide,
        BinaryOperator.Modulo,
        BinaryOperator.Equal,
        BinaryOperator.NotEqual,
        BinaryOperator.LessThan,
        BinaryOperator.LessOrEqual,
        BinaryOperator.GreaterThan,
        BinaryOperator.GreaterOrEqual,
        BinaryOperator.BitwiseAnd,
        BinaryOperator.BitwiseOr,
        BinaryOperator.BitwiseXor,
        BinaryOperator.LeftShift,
        BinaryOperator.RightShift
    ];

    [SkippableFact]
    public void IntegralOperatorsMatchExecutableCSharpSemantics()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        IntegralOperatorsMatchExecutableCSharpSemanticsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void IntegralOperatorsMatchExecutableCSharpSemanticsCore()
    {
        using var context = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(context);

        foreach (var (leftType, leftValues) in IntegralValues())
        {
            foreach (var (rightType, rightValues) in IntegralValues())
            {
                foreach (var left in leftValues)
                {
                    foreach (var right in rightValues)
                    {
                        foreach (var op in Operators)
                            VerifyCase(verifier, leftType, left, rightType, right, op);
                    }
                }

            }
        }
    }

    [SkippableFact]
    public void UnaryNegationMatchesExecutableCSharpSemantics()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UnaryNegationMatchesExecutableCSharpSemanticsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnaryNegationMatchesExecutableCSharpSemanticsCore()
    {
        using var context = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(context);

        foreach (var (type, values) in IntegralValues())
        {
            foreach (var value in values)
            {
                object? runtimeResult = null;
                Exception? runtimeError = null;
                try
                {
                    dynamic operand = value;
                    runtimeResult = -operand;
                }
                catch (Exception error) when (error is RuntimeBinderException or OverflowException)
                {
                    runtimeError = error;
                }

                var negation = new UnaryOperationNode(
                    Span,
                    UnaryOperator.Negate,
                    Reference("value"));
                var result = verifier.VerifyPostcondition(
                    [("value", type)],
                    "bool",
                    [Requires(Equal(Reference("value"), Literal(value)))],
                    Ensures(Equal(negation, Literal(runtimeResult ?? 0))));
                var label = $"-{type}({value})";

                if (runtimeError is RuntimeBinderException)
                {
                    Assert.True(
                        result.Status == ContractVerificationStatus.Unsupported,
                        $"{label}: C# rejects the expression but verification returned {result.Status}");
                }
                else if (runtimeError is not null)
                {
                    Assert.True(
                        result.Status != ContractVerificationStatus.Proven,
                        $"{label}: C# throws {runtimeError.GetType().Name} but verification returned Proven");
                }
                else
                {
                    Assert.True(
                        result.Status == ContractVerificationStatus.Proven,
                        $"{label}: expected Proven, got {result.Status}: {result.CounterexampleDescription}");
                }
            }
        }
    }

    private static void VerifyCase(
        Z3Verifier verifier,
        string leftType,
        object leftValue,
        string rightType,
        object rightValue,
        BinaryOperator op)
    {
        object? runtimeResult = null;
        Exception? runtimeError = null;
        try
        {
            runtimeResult = EvaluateDynamic(leftValue, rightValue, op);
        }
        catch (Exception error) when (error is RuntimeBinderException
                                      or DivideByZeroException
                                      or OverflowException)
        {
            runtimeError = error;
        }

        var operation = new BinaryOperationNode(
            Span,
            op,
            Reference("left"),
            Reference("right"));
        ExpressionNode condition = runtimeResult is bool expectedBoolean
            ? expectedBoolean
                ? operation
                : new UnaryOperationNode(Span, UnaryOperator.Not, operation)
            : Equal(operation, Literal(runtimeResult ?? 0));
        var result = verifier.VerifyPostcondition(
            [("left", leftType), ("right", rightType)],
            "bool",
            [
                Requires(Equal(Reference("left"), Literal(leftValue))),
                Requires(Equal(Reference("right"), Literal(rightValue)))
            ],
            Ensures(condition));

        var label = $"{leftType}({leftValue}) {op} {rightType}({rightValue})";
        if (runtimeError is RuntimeBinderException)
        {
            Assert.True(
                result.Status == ContractVerificationStatus.Unsupported,
                $"{label}: C# rejects the expression but verification returned {result.Status}");
            return;
        }

        if (runtimeError is not null)
        {
            Assert.True(
                result.Status != ContractVerificationStatus.Proven,
                $"{label}: C# throws {runtimeError.GetType().Name} but verification returned Proven");
            return;
        }

        Assert.True(
            result.Status == ContractVerificationStatus.Proven,
            $"{label}: expected Proven, got {result.Status}: {result.CounterexampleDescription}");
    }

    private static object EvaluateDynamic(object left, object right, BinaryOperator op)
    {
        dynamic dynamicLeft = left;
        dynamic dynamicRight = right;
        return op switch
        {
            BinaryOperator.Add => dynamicLeft + dynamicRight,
            BinaryOperator.Subtract => dynamicLeft - dynamicRight,
            BinaryOperator.Multiply => dynamicLeft * dynamicRight,
            BinaryOperator.Divide => dynamicLeft / dynamicRight,
            BinaryOperator.Modulo => dynamicLeft % dynamicRight,
            BinaryOperator.Equal => dynamicLeft == dynamicRight,
            BinaryOperator.NotEqual => dynamicLeft != dynamicRight,
            BinaryOperator.LessThan => dynamicLeft < dynamicRight,
            BinaryOperator.LessOrEqual => dynamicLeft <= dynamicRight,
            BinaryOperator.GreaterThan => dynamicLeft > dynamicRight,
            BinaryOperator.GreaterOrEqual => dynamicLeft >= dynamicRight,
            BinaryOperator.BitwiseAnd => dynamicLeft & dynamicRight,
            BinaryOperator.BitwiseOr => dynamicLeft | dynamicRight,
            BinaryOperator.BitwiseXor => dynamicLeft ^ dynamicRight,
            BinaryOperator.LeftShift => dynamicLeft << dynamicRight,
            BinaryOperator.RightShift => dynamicLeft >> dynamicRight,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
        };
    }

    private static IEnumerable<(string Type, object[] Values)> IntegralValues()
    {
        yield return ("i8", [sbyte.MinValue, (sbyte)0, sbyte.MaxValue]);
        yield return ("u8", [(byte)0, (byte)1, byte.MaxValue]);
        yield return ("i16", [short.MinValue, (short)0, short.MaxValue]);
        yield return ("u16", [(ushort)0, (ushort)1, ushort.MaxValue]);
        yield return ("i32", [int.MinValue, 0, int.MaxValue]);
        yield return ("u32", [0U, 1U, uint.MaxValue]);
        yield return ("i64", [long.MinValue, 0L, long.MaxValue]);
        yield return ("u64", [0UL, 1UL, ulong.MaxValue]);
    }

    private static ReferenceNode Reference(string name) => new(Span, name);

    private static BinaryOperationNode Equal(ExpressionNode left, ExpressionNode right) =>
        new(Span, BinaryOperator.Equal, left, right);

    private static RequiresNode Requires(ExpressionNode condition) =>
        new(Span, condition, null, Attributes);

    private static EnsuresNode Ensures(ExpressionNode condition) =>
        new(Span, condition, null, Attributes);

    private static IntLiteralNode Literal(object value) => value switch
    {
        sbyte number => new IntLiteralNode(Span, number),
        byte number => new IntLiteralNode(Span, number),
        short number => new IntLiteralNode(Span, number),
        ushort number => new IntLiteralNode(Span, number),
        int number => new IntLiteralNode(Span, number),
        uint number => new IntLiteralNode(Span, number, false, true, number),
        long number => new IntLiteralNode(Span, number) { IsLong = true },
        ulong number => new IntLiteralNode(Span, unchecked((long)number), false, true, number)
        {
            IsLong = true
        },
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Not an integral value.")
    };
}
