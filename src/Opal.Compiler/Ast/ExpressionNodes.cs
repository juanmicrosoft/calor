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
