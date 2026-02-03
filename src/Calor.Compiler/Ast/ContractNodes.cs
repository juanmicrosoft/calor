using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a precondition contract.
/// §REQUIRES[message="..."] expression
/// </summary>
public sealed class RequiresNode : AstNode
{
    public ExpressionNode Condition { get; }
    public string? Message { get; }
    public AttributeCollection Attributes { get; }

    public RequiresNode(
        TextSpan span,
        ExpressionNode condition,
        string? message,
        AttributeCollection attributes)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Message = message;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a postcondition contract.
/// §ENSURES[message="..."] expression
/// The special identifier 'result' can be used to refer to the return value.
/// </summary>
public sealed class EnsuresNode : AstNode
{
    public ExpressionNode Condition { get; }
    public string? Message { get; }
    public AttributeCollection Attributes { get; }

    public EnsuresNode(
        TextSpan span,
        ExpressionNode condition,
        string? message,
        AttributeCollection attributes)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Message = message;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a class/type invariant contract.
/// §INVARIANT[message="..."] expression
/// </summary>
public sealed class InvariantNode : AstNode
{
    public ExpressionNode Condition { get; }
    public string? Message { get; }
    public AttributeCollection Attributes { get; }

    public InvariantNode(
        TextSpan span,
        ExpressionNode condition,
        string? message,
        AttributeCollection attributes)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Message = message;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an effect declaration on a function.
/// Contains information about what side effects the function may have.
/// </summary>
public sealed class EffectDeclaration
{
    /// <summary>
    /// The type of effect (e.g., "io", "mutation", "allocation", "exception").
    /// </summary>
    public string EffectType { get; }

    /// <summary>
    /// The specific effect value (e.g., "console_write", "file_read").
    /// </summary>
    public string EffectValue { get; }

    public EffectDeclaration(string effectType, string effectValue)
    {
        EffectType = effectType ?? throw new ArgumentNullException(nameof(effectType));
        EffectValue = effectValue ?? throw new ArgumentNullException(nameof(effectValue));
    }

    public override string ToString() => $"{EffectType}={EffectValue}";
}

/// <summary>
/// Known effect types in Calor.
/// </summary>
public static class EffectTypes
{
    /// <summary>
    /// I/O effects (console, file, network).
    /// </summary>
    public const string IO = "io";

    /// <summary>
    /// Memory mutation effects.
    /// </summary>
    public const string Mutation = "mutation";

    /// <summary>
    /// Heap allocation effects.
    /// </summary>
    public const string Allocation = "allocation";

    /// <summary>
    /// Exception throwing effects.
    /// </summary>
    public const string Exception = "exception";

    /// <summary>
    /// Non-deterministic effects (random, time).
    /// </summary>
    public const string Nondeterminism = "nondeterminism";
}

/// <summary>
/// Known I/O effect values.
/// </summary>
public static class IOEffects
{
    public const string ConsoleRead = "console_read";
    public const string ConsoleWrite = "console_write";
    public const string FileRead = "file_read";
    public const string FileWrite = "file_write";
    public const string NetworkRead = "network_read";
    public const string NetworkWrite = "network_write";
}
