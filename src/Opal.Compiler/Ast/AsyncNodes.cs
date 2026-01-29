using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Represents an await expression.
/// §AWAIT §C[client.GetStringAsync] §A §REF[name=url] §/C
/// §AWAIT[false] §C[reader.ReadAsync] §/C   // ConfigureAwait(false)
/// </summary>
public sealed class AwaitExpressionNode : ExpressionNode
{
    /// <summary>
    /// The expression being awaited.
    /// </summary>
    public ExpressionNode Awaited { get; }

    /// <summary>
    /// Whether to ConfigureAwait(true/false). Null means no explicit configuration.
    /// </summary>
    public bool? ConfigureAwait { get; }

    public AwaitExpressionNode(TextSpan span, ExpressionNode awaited, bool? configureAwait = null)
        : base(span)
    {
        Awaited = awaited ?? throw new ArgumentNullException(nameof(awaited));
        ConfigureAwait = configureAwait;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
