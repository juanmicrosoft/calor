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

    /// <summary>
    /// Optional named argument labels, parallel to Arguments list.
    /// </summary>
    public IReadOnlyList<string?>? ArgumentNames { get; }

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

    public CallStatementNode(
        TextSpan span,
        string target,
        bool fallible,
        IReadOnlyList<ExpressionNode> arguments,
        AttributeCollection attributes,
        IReadOnlyList<string?>? argumentNames)
        : this(span, target, fallible, arguments, attributes)
    {
        ArgumentNames = argumentNames;
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

/// <summary>
/// Represents a bare expression used as a statement (e.g., (inc x), (post-dec y)).
/// Emitted as: expression;
/// </summary>
public sealed class ExpressionStatementNode : StatementNode
{
    public ExpressionNode Expression { get; }

    public ExpressionStatementNode(TextSpan span, ExpressionNode expression)
        : base(span)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a fallback comment for unsupported C# statements.
/// Emitted as // TODO: Manual conversion needed [feature] with original C# code.
/// </summary>
public sealed class FallbackCommentNode : StatementNode
{
    /// <summary>
    /// The original C# code that could not be converted.
    /// </summary>
    public string OriginalCSharp { get; }

    /// <summary>
    /// The name of the unsupported feature (e.g., "goto", "stackalloc").
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Optional suggestion for how to manually convert this construct.
    /// </summary>
    public string? Suggestion { get; }

    public FallbackCommentNode(TextSpan span, string originalCSharp, string featureName, string? suggestion = null)
        : base(span)
    {
        OriginalCSharp = originalCSharp ?? throw new ArgumentNullException(nameof(originalCSharp));
        FeatureName = featureName ?? throw new ArgumentNullException(nameof(featureName));
        Suggestion = suggestion;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a raw C# passthrough block.
/// §RAW ... §/RAW
/// Content is emitted verbatim as C# code without any transformation.
/// </summary>
public sealed class RawCSharpNode : StatementNode
{
    /// <summary>
    /// The raw C# source code to emit verbatim.
    /// </summary>
    public string CSharpCode { get; }

    public RawCSharpNode(TextSpan span, string csharpCode)
        : base(span)
    {
        CSharpCode = csharpCode ?? throw new ArgumentNullException(nameof(csharpCode));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
