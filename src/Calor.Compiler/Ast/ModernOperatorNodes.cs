using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents an interpolated string expression.
/// §INTERP["Hello, " §EXP §REF[name=name] "!"]
/// generates: $"Hello, {name}!"
/// </summary>
public sealed class InterpolatedStringNode : ExpressionNode
{
    /// <summary>
    /// The parts of the interpolated string (literals or expressions).
    /// </summary>
    public IReadOnlyList<InterpolatedStringPartNode> Parts { get; }

    public InterpolatedStringNode(TextSpan span, IReadOnlyList<InterpolatedStringPartNode> parts)
        : base(span)
    {
        Parts = parts ?? throw new ArgumentNullException(nameof(parts));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Base class for parts of an interpolated string.
/// </summary>
public abstract class InterpolatedStringPartNode : AstNode
{
    protected InterpolatedStringPartNode(TextSpan span) : base(span) { }
}

/// <summary>
/// A literal text part of an interpolated string.
/// </summary>
public sealed class InterpolatedStringTextNode : InterpolatedStringPartNode
{
    public string Text { get; }

    public InterpolatedStringTextNode(TextSpan span, string text)
        : base(span)
    {
        Text = text ?? "";
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// An expression part of an interpolated string.
/// </summary>
public sealed class InterpolatedStringExpressionNode : InterpolatedStringPartNode
{
    public ExpressionNode Expression { get; }

    public InterpolatedStringExpressionNode(TextSpan span, ExpressionNode expression)
        : base(span)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a null-coalescing expression.
/// §?? §REF[name=value] "default"
/// generates: value ?? "default"
/// </summary>
public sealed class NullCoalesceNode : ExpressionNode
{
    /// <summary>
    /// The left operand (value to check for null).
    /// </summary>
    public ExpressionNode Left { get; }

    /// <summary>
    /// The right operand (fallback value).
    /// </summary>
    public ExpressionNode Right { get; }

    public NullCoalesceNode(TextSpan span, ExpressionNode left, ExpressionNode right)
        : base(span)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a null-conditional access expression.
/// §?. §REF[name=person] Name
/// generates: person?.Name
/// </summary>
public sealed class NullConditionalNode : ExpressionNode
{
    /// <summary>
    /// The expression being accessed (may be null).
    /// </summary>
    public ExpressionNode Target { get; }

    /// <summary>
    /// The member or index being accessed.
    /// </summary>
    public string MemberName { get; }

    public NullConditionalNode(TextSpan span, ExpressionNode target, string memberName)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        MemberName = memberName ?? "";
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a range expression.
/// §RANGE 1 5
/// generates: 1..5
/// </summary>
public sealed class RangeExpressionNode : ExpressionNode
{
    /// <summary>
    /// The start of the range (null for open-ended).
    /// </summary>
    public ExpressionNode? Start { get; }

    /// <summary>
    /// The end of the range (null for open-ended).
    /// </summary>
    public ExpressionNode? End { get; }

    public RangeExpressionNode(TextSpan span, ExpressionNode? start, ExpressionNode? end)
        : base(span)
    {
        Start = start;
        End = end;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an index-from-end expression.
/// §^ 1
/// generates: ^1 (index from end)
/// </summary>
public sealed class IndexFromEndNode : ExpressionNode
{
    /// <summary>
    /// The offset from the end.
    /// </summary>
    public ExpressionNode Offset { get; }

    public IndexFromEndNode(TextSpan span, ExpressionNode offset)
        : base(span)
    {
        Offset = offset ?? throw new ArgumentNullException(nameof(offset));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
