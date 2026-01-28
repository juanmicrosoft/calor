using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Represents a pattern match expression.
/// §MATCH[id=xxx] expression
///   §CASE pattern body
///   §CASE pattern body
/// §END_MATCH[id=xxx]
/// </summary>
public sealed class MatchExpressionNode : ExpressionNode
{
    public string Id { get; }
    public ExpressionNode Target { get; }
    public IReadOnlyList<MatchCaseNode> Cases { get; }
    public AttributeCollection Attributes { get; }

    public MatchExpressionNode(
        TextSpan span,
        string id,
        ExpressionNode target,
        IReadOnlyList<MatchCaseNode> cases,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Cases = cases ?? throw new ArgumentNullException(nameof(cases));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a pattern match statement (for side effects).
/// </summary>
public sealed class MatchStatementNode : StatementNode
{
    public string Id { get; }
    public ExpressionNode Target { get; }
    public IReadOnlyList<MatchCaseNode> Cases { get; }
    public AttributeCollection Attributes { get; }

    public MatchStatementNode(
        TextSpan span,
        string id,
        ExpressionNode target,
        IReadOnlyList<MatchCaseNode> cases,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Cases = cases ?? throw new ArgumentNullException(nameof(cases));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a case in a match expression/statement.
/// §CASE pattern body
/// </summary>
public sealed class MatchCaseNode : AstNode
{
    public PatternNode Pattern { get; }
    public ExpressionNode? Guard { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public MatchCaseNode(
        TextSpan span,
        PatternNode pattern,
        ExpressionNode? guard,
        IReadOnlyList<StatementNode> body)
        : base(span)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        Guard = guard;
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Base class for pattern nodes.
/// </summary>
public abstract class PatternNode : AstNode
{
    protected PatternNode(TextSpan span) : base(span) { }
}

/// <summary>
/// Represents a wildcard pattern that matches anything.
/// _
/// </summary>
public sealed class WildcardPatternNode : PatternNode
{
    public WildcardPatternNode(TextSpan span) : base(span) { }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a variable binding pattern.
/// x (binds the matched value to x)
/// </summary>
public sealed class VariablePatternNode : PatternNode
{
    public string Name { get; }

    public VariablePatternNode(TextSpan span, string name) : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a literal pattern.
/// INT:42, STR:"hello", BOOL:true
/// </summary>
public sealed class LiteralPatternNode : PatternNode
{
    public ExpressionNode Literal { get; }

    public LiteralPatternNode(TextSpan span, ExpressionNode literal) : base(span)
    {
        Literal = literal ?? throw new ArgumentNullException(nameof(literal));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a Some(x) pattern for Option types.
/// §SOME pattern
/// </summary>
public sealed class SomePatternNode : PatternNode
{
    public PatternNode InnerPattern { get; }

    public SomePatternNode(TextSpan span, PatternNode innerPattern) : base(span)
    {
        InnerPattern = innerPattern ?? throw new ArgumentNullException(nameof(innerPattern));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a None pattern for Option types.
/// §NONE
/// </summary>
public sealed class NonePatternNode : PatternNode
{
    public NonePatternNode(TextSpan span) : base(span) { }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents an Ok(x) pattern for Result types.
/// §OK pattern
/// </summary>
public sealed class OkPatternNode : PatternNode
{
    public PatternNode InnerPattern { get; }

    public OkPatternNode(TextSpan span, PatternNode innerPattern) : base(span)
    {
        InnerPattern = innerPattern ?? throw new ArgumentNullException(nameof(innerPattern));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents an Err(x) pattern for Result types.
/// §ERR pattern
/// </summary>
public sealed class ErrPatternNode : PatternNode
{
    public PatternNode InnerPattern { get; }

    public ErrPatternNode(TextSpan span, PatternNode innerPattern) : base(span)
    {
        InnerPattern = innerPattern ?? throw new ArgumentNullException(nameof(innerPattern));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a constructor/variant pattern for discriminated unions.
/// §VARIANT[name=Circle] pattern...
/// </summary>
public sealed class ConstructorPatternNode : PatternNode
{
    public string TypeName { get; }
    public IReadOnlyList<FieldPatternNode> Fields { get; }

    public ConstructorPatternNode(TextSpan span, string typeName, IReadOnlyList<FieldPatternNode> fields)
        : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a field pattern within a constructor pattern.
/// </summary>
public sealed class FieldPatternNode : AstNode
{
    public string FieldName { get; }
    public PatternNode Pattern { get; }

    public FieldPatternNode(TextSpan span, string fieldName, PatternNode pattern)
        : base(span)
    {
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a record pattern for destructuring records.
/// §RECORD[type=Person] §FIELD[name=Name] namePattern §FIELD[name=Age] agePattern
/// </summary>
public sealed class RecordPatternNode : PatternNode
{
    public string TypeName { get; }
    public IReadOnlyList<FieldPatternNode> Fields { get; }

    public RecordPatternNode(TextSpan span, string typeName, IReadOnlyList<FieldPatternNode> fields)
        : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}
