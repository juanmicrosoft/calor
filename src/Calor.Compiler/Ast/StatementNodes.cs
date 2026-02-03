using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

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

/// <summary>
/// Represents a print statement (§P shorthand for Console.WriteLine).
/// §P expression
/// </summary>
public sealed class PrintStatementNode : StatementNode
{
    public ExpressionNode Expression { get; }
    public bool IsWriteLine { get; }  // true for §P (WriteLine), false for §Pf (Write)

    public PrintStatementNode(TextSpan span, ExpressionNode expression, bool isWriteLine = true)
        : base(span)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        IsWriteLine = isWriteLine;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
