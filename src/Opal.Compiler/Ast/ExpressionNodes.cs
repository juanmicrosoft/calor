using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Base class for expression nodes.
/// </summary>
public abstract class ExpressionNode : AstNode
{
    protected ExpressionNode(TextSpan span) : base(span) { }
}

/// <summary>
/// Represents an integer literal.
/// INT:42
/// </summary>
public sealed class IntLiteralNode : ExpressionNode
{
    public int Value { get; }

    public IntLiteralNode(TextSpan span, int value) : base(span)
    {
        Value = value;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a string literal.
/// STR:"hello"
/// </summary>
public sealed class StringLiteralNode : ExpressionNode
{
    public string Value { get; }

    public StringLiteralNode(TextSpan span, string value) : base(span)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a boolean literal.
/// BOOL:true / BOOL:false
/// </summary>
public sealed class BoolLiteralNode : ExpressionNode
{
    public bool Value { get; }

    public BoolLiteralNode(TextSpan span, bool value) : base(span)
    {
        Value = value;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a conditional (ternary) expression.
/// (? condition thenExpr elseExpr)
/// </summary>
public sealed class ConditionalExpressionNode : ExpressionNode
{
    public ExpressionNode Condition { get; }
    public ExpressionNode WhenTrue { get; }
    public ExpressionNode WhenFalse { get; }

    public ConditionalExpressionNode(TextSpan span, ExpressionNode condition, ExpressionNode whenTrue, ExpressionNode whenFalse)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        WhenTrue = whenTrue ?? throw new ArgumentNullException(nameof(whenTrue));
        WhenFalse = whenFalse ?? throw new ArgumentNullException(nameof(whenFalse));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a floating-point literal.
/// FLOAT:3.14
/// </summary>
public sealed class FloatLiteralNode : ExpressionNode
{
    public double Value { get; }

    public FloatLiteralNode(TextSpan span, double value) : base(span)
    {
        Value = value;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a reference to a variable or identifier.
/// </summary>
public sealed class ReferenceNode : ExpressionNode
{
    public string Name { get; }

    public ReferenceNode(TextSpan span, string name) : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a unary operation (prefix operators like !, ~, -).
/// </summary>
public sealed class UnaryOperationNode : ExpressionNode
{
    public UnaryOperator Operator { get; }
    public ExpressionNode Operand { get; }

    public UnaryOperationNode(TextSpan span, UnaryOperator op, ExpressionNode operand)
        : base(span)
    {
        Operator = op;
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Unary operators supported by OPAL.
/// </summary>
public enum UnaryOperator
{
    Negate,         // - (unary minus)
    Not,            // ! (logical not)
    BitwiseNot,     // ~ (bitwise not)
}

/// <summary>
/// Helper methods for UnaryOperator.
/// </summary>
public static class UnaryOperatorExtensions
{
    public static UnaryOperator? FromString(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "NEG" or "NEGATE" or "-" => UnaryOperator.Negate,
            "NOT" or "!" => UnaryOperator.Not,
            "BNOT" or "BITWISENOT" or "~" => UnaryOperator.BitwiseNot,
            _ => null
        };
    }

    public static string ToCSharpOperator(this UnaryOperator op)
    {
        return op switch
        {
            UnaryOperator.Negate => "-",
            UnaryOperator.Not => "!",
            UnaryOperator.BitwiseNot => "~",
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
    }
}
