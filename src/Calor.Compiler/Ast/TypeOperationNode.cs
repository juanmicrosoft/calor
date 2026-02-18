using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Type operations supported by Calor: cast, is, as.
/// </summary>
public enum TypeOp
{
    Cast,   // (cast Type expr)  → (Type)expr
    Is,     // (is expr Type)    → expr is Type
    As,     // (as expr Type)    → expr as Type
}

/// <summary>
/// Represents a type operation expression.
/// Examples: (cast i32 x), (is x str), (as x MyClass)
/// </summary>
public sealed class TypeOperationNode : ExpressionNode
{
    public TypeOp Operation { get; }
    public ExpressionNode Operand { get; }
    public string TargetType { get; }

    public TypeOperationNode(
        TextSpan span,
        TypeOp operation,
        ExpressionNode operand,
        string targetType)
        : base(span)
    {
        Operation = operation;
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Helper methods for TypeOp enum.
/// </summary>
public static class TypeOpExtensions
{
    /// <summary>
    /// Parses a type operation name to its enum value.
    /// Returns null if the name is not a recognized type operation.
    /// </summary>
    public static TypeOp? FromString(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            "cast" => TypeOp.Cast,
            "is" => TypeOp.Is,
            "as" => TypeOp.As,
            _ => null
        };
    }

    /// <summary>
    /// Converts a TypeOp enum value back to its Calor syntax name.
    /// </summary>
    public static string ToCalorName(this TypeOp op)
    {
        return op switch
        {
            TypeOp.Cast => "cast",
            TypeOp.Is => "is",
            TypeOp.As => "as",
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown type operation")
        };
    }
}
