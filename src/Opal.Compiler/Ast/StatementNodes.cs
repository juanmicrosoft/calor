using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Base class for statement nodes.
/// </summary>
public abstract class StatementNode : AstNode
{
    protected StatementNode(TextSpan span) : base(span) { }
}

/// <summary>
/// Represents a function call statement.
/// §CALL[target=xxx][fallible=xxx]
/// </summary>
public sealed class CallStatementNode : StatementNode
{
    public string Target { get; }
    public bool Fallible { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }
    public AttributeCollection Attributes { get; }

    public CallStatementNode(
        TextSpan span,
        string target,
        bool fallible,
        IReadOnlyList<ExpressionNode> arguments,
        AttributeCollection attributes)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Fallible = fallible;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a return statement.
/// §RETURN [expression]
/// </summary>
public sealed class ReturnStatementNode : StatementNode
{
    public ExpressionNode? Expression { get; }

    public ReturnStatementNode(TextSpan span, ExpressionNode? expression)
        : base(span)
    {
        Expression = expression;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
