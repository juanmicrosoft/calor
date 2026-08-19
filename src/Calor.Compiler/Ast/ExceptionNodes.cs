using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

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
    public TextSpan? ExceptionTypeSpan { get; }

    /// <summary>
    /// The variable name for the exception. Null if not capturing.
    /// </summary>
    public string? VariableName { get; }
    public TextSpan? VariableSpan { get; }

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
        AttributeCollection attributes,
        TextSpan? variableSpan = null,
        TextSpan? exceptionTypeSpan = null)
        : base(span)
    {
        ExceptionType = exceptionType;
        ExceptionTypeSpan = exceptionType == null ? null : exceptionTypeSpan ?? TextSpan.Empty;
        VariableName = variableName;
        VariableSpan = variableName == null ? null : variableSpan ?? span;
        Filter = filter;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


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


}

/// <summary>
/// Represents a throw expression (throw in expression position).
/// Used in switch arms, ternary, and ?? contexts.
/// </summary>
public sealed class ThrowExpressionNode : ExpressionNode
{
    /// <summary>
    /// The exception to throw.
    /// </summary>
    public ExpressionNode Exception { get; }

    public ThrowExpressionNode(TextSpan span, ExpressionNode exception)
        : base(span)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }


}

/// <summary>
/// Represents a rethrow statement.
/// §RETHROW
/// </summary>
public sealed class RethrowStatementNode : StatementNode
{
    public RethrowStatementNode(TextSpan span) : base(span) { }


}
