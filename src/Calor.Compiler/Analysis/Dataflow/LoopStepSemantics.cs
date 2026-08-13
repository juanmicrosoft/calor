using System.Numerics;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;

namespace Calor.Compiler.Analysis.Dataflow;

/// <summary>
/// Shared constant-step semantics for emitted and analyzed numeric for loops.
/// </summary>
public static class LoopStepSemantics
{
    public static bool TryEvaluate(ExpressionNode? expression, out BigInteger value)
    {
        if (expression == null)
        {
            value = BigInteger.One;
            return true;
        }
        if (expression is IntLiteralNode integer)
        {
            value = integer.IsUnsigned
                ? new BigInteger(integer.UnsignedValue)
                : new BigInteger(integer.Value);
            return true;
        }
        if (expression is UnaryOperationNode
            {
                Operator: UnaryOperator.Negate,
            } unary
            && TryEvaluate(unary.Operand, out var operand))
        {
            value = -operand;
            return true;
        }
        if (expression is BinaryOperationNode binary
            && TryEvaluate(binary.Left, out var left)
            && TryEvaluate(binary.Right, out var right))
        {
            return TryApply(binary.Operator, left, right, out value);
        }
        value = default;
        return false;
    }

    public static bool TryEvaluate(BoundExpression? expression, out BigInteger value)
    {
        if (expression == null)
        {
            value = BigInteger.One;
            return true;
        }
        if (expression is BoundIntLiteral integer)
        {
            value = integer.IsUnsigned
                ? new BigInteger(integer.UnsignedValue)
                : new BigInteger(integer.Value);
            return true;
        }
        if (expression is BoundUnaryExpression
            {
                Operator: UnaryOperator.Negate,
            } unary
            && TryEvaluate(unary.Operand, out var operand))
        {
            value = -operand;
            return true;
        }
        if (expression is BoundBinaryExpression binary
            && TryEvaluate(binary.Left, out var left)
            && TryEvaluate(binary.Right, out var right))
        {
            return TryApply(binary.Operator, left, right, out value);
        }
        value = default;
        return false;
    }

    private static bool TryApply(
        BinaryOperator operation,
        BigInteger left,
        BigInteger right,
        out BigInteger value)
    {
        switch (operation)
        {
            case BinaryOperator.Add:
                value = left + right;
                return true;
            case BinaryOperator.Subtract:
                value = left - right;
                return true;
            case BinaryOperator.Multiply:
                value = left * right;
                return true;
            case BinaryOperator.Divide when !right.IsZero:
                value = left / right;
                return true;
            case BinaryOperator.Modulo when !right.IsZero:
                value = left % right;
                return true;
            default:
                value = default;
                return false;
        }
    }
}
