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

    public static IEnumerable<object[]> IntegralPromotionCases()
    {
        foreach (var (leftType, leftValues) in IntegralValues())
        {
            foreach (var (rightType, rightValues) in IntegralValues())
            {
                foreach (var left in leftValues)
                {
                    foreach (var right in rightValues)
                        yield return [leftType, left, rightType, right];
                }
            }
        }
    }

    [SkippableTheory]
    [MemberData(nameof(IntegralPromotionCases))]
    public void AdditionMatchesExecutableCSharpPromotion(
        string leftType,
        object leftValue,
        string rightType,
        object rightValue)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        AdditionMatchesExecutableCSharpPromotionCore(
            leftType,
            leftValue,
            rightType,
            rightValue);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AdditionMatchesExecutableCSharpPromotionCore(
        string leftType,
        object leftValue,
        string rightType,
        object rightValue)
    {
        using var context = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(context);

        object? runtimeResult = null;
        RuntimeBinderException? runtimeError = null;
        try
        {
            runtimeResult = AddDynamic(leftValue, rightValue);
        }
        catch (RuntimeBinderException error)
        {
            runtimeError = error;
        }

        var parameters = new List<(string Name, string TypeName)>
        {
            ("left", leftType),
            ("right", rightType)
        };
        var preconditions = new[]
        {
            Requires(Equal(Reference("left"), Literal(leftValue))),
            Requires(Equal(Reference("right"), Literal(rightValue)))
        };
        var sum = new BinaryOperationNode(
            Span,
            BinaryOperator.Add,
            Reference("left"),
            Reference("right"));
        var postcondition = Ensures(Equal(sum, Literal(runtimeResult ?? 0)));
        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            preconditions,
            postcondition);

        if (runtimeError is not null)
        {
            Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
            return;
        }

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    private static object AddDynamic(object left, object right)
    {
        dynamic dynamicLeft = left;
        dynamic dynamicRight = right;
        return dynamicLeft + dynamicRight;
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
        uint number => new IntLiteralNode(Span, number, isHex: false, isUnsigned: true, number),
        long number => new IntLiteralNode(Span, number) { IsLong = true },
        ulong number => new IntLiteralNode(
            Span,
            unchecked((long)number),
            isHex: false,
            isUnsigned: true,
            number)
        {
            IsLong = true
        },
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Not an integral value.")
    };
}
