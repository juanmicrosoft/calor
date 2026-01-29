using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Represents a try/catch/finally statement.
/// §TRY[try1]
///   ...
/// §CATCH[IOException:ex]
///   ...
/// §CATCH
///   §RETHROW
/// §FINALLY
///   ...
/// §/TRY[try1]
/// </summary>
public sealed class TryStatementNode : StatementNode
{
    public string Id { get; }
    public IReadOnlyList<StatementNode> TryBody { get; }
    public IReadOnlyList<CatchClauseNode> CatchClauses { get; }
    public IReadOnlyList<StatementNode>? FinallyBody { get; }
    public AttributeCollection Attributes { get; }

    public TryStatementNode(
        TextSpan span,
        string id,
        IReadOnlyList<StatementNode> tryBody,
        IReadOnlyList<CatchClauseNode> catchClauses,
        IReadOnlyList<StatementNode>? finallyBody,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        TryBody = tryBody ?? throw new ArgumentNullException(nameof(tryBody));
        CatchClauses = catchClauses ?? throw new ArgumentNullException(nameof(catchClauses));
        FinallyBody = finallyBody;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a catch clause.
/// §CATCH[IOException:ex]
/// §CATCH
/// </summary>
public sealed class CatchClauseNode : AstNode
{
    /// <summary>
    /// The exception type to catch. Null for catch-all.
    /// </summary>
    public string? ExceptionType { get; }

    /// <summary>
    /// The variable name for the exception. Null if not capturing.
    /// </summary>
    public string? VariableName { get; }

    /// <summary>
    /// Optional filter expression (when clause).
    /// </summary>
    public ExpressionNode? Filter { get; }

    /// <summary>
    /// The catch body statements.
    /// </summary>
    public IReadOnlyList<StatementNode> Body { get; }

    public AttributeCollection Attributes { get; }

    public CatchClauseNode(
        TextSpan span,
        string? exceptionType,
        string? variableName,
        ExpressionNode? filter,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : base(span)
    {
        ExceptionType = exceptionType;
        VariableName = variableName;
        Filter = filter;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a throw statement.
/// §THROW §NEW[ArgumentException] §A "Invalid" §/NEW
/// </summary>
public sealed class ThrowStatementNode : StatementNode
{
    /// <summary>
    /// The exception to throw. Null for rethrow.
    /// </summary>
    public ExpressionNode? Exception { get; }

    public ThrowStatementNode(TextSpan span, ExpressionNode? exception)
        : base(span)
    {
        Exception = exception;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a rethrow statement.
/// §RETHROW
/// </summary>
public sealed class RethrowStatementNode : StatementNode
{
    public RethrowStatementNode(TextSpan span) : base(span) { }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
