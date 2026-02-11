using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Verification.Z3.Cache;

/// <summary>
/// Generates deterministic hash keys from contract expressions for caching.
/// </summary>
public sealed class ContractHasher
{
    /// <summary>
    /// Computes a hash key for a precondition.
    /// Format: PRE:{params}::{expression_hash}
    /// </summary>
    public string HashPrecondition(
        IReadOnlyList<(string Name, string TypeName)> parameters,
        RequiresNode precondition)
    {
        var sb = new StringBuilder();
        sb.Append("PRE:");
        AppendParameters(sb, parameters);
        sb.Append("::");
        AppendExpression(sb, precondition.Condition);

        return ComputeSha256Hash(sb.ToString());
    }

    /// <summary>
    /// Computes a hash key for a postcondition.
    /// Format: POST:{params}:{output}:PRECS:{prec_hashes}::POST:{post_hash}
    /// </summary>
    public string HashPostcondition(
        IReadOnlyList<(string Name, string TypeName)> parameters,
        string? outputType,
        IReadOnlyList<RequiresNode> preconditions,
        EnsuresNode postcondition)
    {
        var sb = new StringBuilder();
        sb.Append("POST:");
        AppendParameters(sb, parameters);
        sb.Append(':');
        sb.Append(outputType ?? "void");
        sb.Append(":PRECS:");

        // Include all preconditions in the hash since they affect postcondition verification
        foreach (var pre in preconditions)
        {
            AppendExpression(sb, pre.Condition);
            sb.Append(';');
        }

        sb.Append("::POST:");
        AppendExpression(sb, postcondition.Condition);

        return ComputeSha256Hash(sb.ToString());
    }

    /// <summary>
    /// Gets the canonical string representation of an expression (for testing).
    /// </summary>
    public string GetCanonicalExpression(ExpressionNode expression)
    {
        var sb = new StringBuilder();
        AppendExpression(sb, expression);
        return sb.ToString();
    }

    private void AppendParameters(StringBuilder sb, IReadOnlyList<(string Name, string TypeName)> parameters)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(parameters[i].Name);
            sb.Append(':');
            sb.Append(parameters[i].TypeName);
        }
    }

    private void AppendExpression(StringBuilder sb, ExpressionNode expr)
    {
        switch (expr)
        {
            case IntLiteralNode intLit:
                sb.Append("INT:");
                sb.Append(intLit.Value);
                break;

            case BoolLiteralNode boolLit:
                sb.Append("BOOL:");
                sb.Append(boolLit.Value ? "true" : "false");
                break;

            case FloatLiteralNode floatLit:
                sb.Append("FLOAT:");
                sb.Append(floatLit.Value.ToString("G17"));
                break;

            case StringLiteralNode strLit:
                sb.Append("STR:\"");
                sb.Append(strLit.Value.Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append('"');
                break;

            case ReferenceNode refNode:
                sb.Append("REF:");
                sb.Append(refNode.Name);
                break;

            case BinaryOperationNode binOp:
                sb.Append('(');
                sb.Append(GetOperatorSymbol(binOp.Operator));
                sb.Append(' ');
                AppendExpression(sb, binOp.Left);
                sb.Append(' ');
                AppendExpression(sb, binOp.Right);
                sb.Append(')');
                break;

            case UnaryOperationNode unaryOp:
                sb.Append('(');
                sb.Append(GetUnaryOperatorSymbol(unaryOp.Operator));
                sb.Append(' ');
                AppendExpression(sb, unaryOp.Operand);
                sb.Append(')');
                break;

            case ForallExpressionNode forall:
                sb.Append("(FORALL (");
                foreach (var bv in forall.BoundVariables)
                {
                    sb.Append('(');
                    sb.Append(bv.Name);
                    sb.Append(' ');
                    sb.Append(bv.TypeName);
                    sb.Append(')');
                }
                sb.Append(") ");
                AppendExpression(sb, forall.Body);
                sb.Append(')');
                break;

            case ExistsExpressionNode exists:
                sb.Append("(EXISTS (");
                foreach (var bv in exists.BoundVariables)
                {
                    sb.Append('(');
                    sb.Append(bv.Name);
                    sb.Append(' ');
                    sb.Append(bv.TypeName);
                    sb.Append(')');
                }
                sb.Append(") ");
                AppendExpression(sb, exists.Body);
                sb.Append(')');
                break;

            case ImplicationExpressionNode impl:
                sb.Append("(-> ");
                AppendExpression(sb, impl.Antecedent);
                sb.Append(' ');
                AppendExpression(sb, impl.Consequent);
                sb.Append(')');
                break;

            case ConditionalExpressionNode cond:
                sb.Append("(ITE ");
                AppendExpression(sb, cond.Condition);
                sb.Append(' ');
                AppendExpression(sb, cond.WhenTrue);
                sb.Append(' ');
                AppendExpression(sb, cond.WhenFalse);
                sb.Append(')');
                break;

            case ArrayAccessNode arrAccess:
                sb.Append("(IDX ");
                AppendExpression(sb, arrAccess.Array);
                sb.Append(' ');
                AppendExpression(sb, arrAccess.Index);
                sb.Append(')');
                break;

            case ArrayLengthNode arrLen:
                sb.Append("(LEN ");
                AppendExpression(sb, arrLen.Array);
                sb.Append(')');
                break;

            default:
                // For unsupported expressions, use the type name as a fallback
                sb.Append("UNSUPPORTED:");
                sb.Append(expr.GetType().Name);
                break;
        }
    }

    private static string GetOperatorSymbol(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Power => "**",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterOrEqual => ">=",
            BinaryOperator.And => "&&",
            BinaryOperator.Or => "||",
            BinaryOperator.BitwiseAnd => "&",
            BinaryOperator.BitwiseOr => "|",
            BinaryOperator.BitwiseXor => "^",
            BinaryOperator.LeftShift => "<<",
            BinaryOperator.RightShift => ">>",
            _ => op.ToString()
        };
    }

    private static string GetUnaryOperatorSymbol(UnaryOperator op)
    {
        return op switch
        {
            UnaryOperator.Negate => "-",
            UnaryOperator.Not => "!",
            UnaryOperator.BitwiseNot => "~",
            _ => op.ToString()
        };
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
