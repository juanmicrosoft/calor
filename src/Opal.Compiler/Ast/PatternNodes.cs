using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

// Match expression and statement nodes (Phase 3: Type System)

/// <summary>
/// Represents a match expression that returns a value.
/// §MATCH[id] §REF[name=value]
///   §CASE ... §BODY ... §/CASE
/// §/MATCH[id]
/// </summary>
public sealed class MatchExpressionNode : ExpressionNode
{
    public string Id { get; }
    public ExpressionNode Target { get; }
    public IReadOnlyList<MatchCaseNode> Cases { get; }
    public AttributeCollection Attributes { get; }

    public MatchExpressionNode(TextSpan span, string id, ExpressionNode target, IReadOnlyList<MatchCaseNode> cases, AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? "";
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Cases = cases ?? throw new ArgumentNullException(nameof(cases));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a match statement (no return value).
/// </summary>
public sealed class MatchStatementNode : StatementNode
{
    public string Id { get; }
    public ExpressionNode Target { get; }
    public IReadOnlyList<MatchCaseNode> Cases { get; }
    public AttributeCollection Attributes { get; }

    public MatchStatementNode(TextSpan span, string id, ExpressionNode target, IReadOnlyList<MatchCaseNode> cases, AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? "";
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Cases = cases ?? throw new ArgumentNullException(nameof(cases));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a case in a match expression or statement.
/// </summary>
public sealed class MatchCaseNode : AstNode
{
    public PatternNode Pattern { get; }
    public ExpressionNode? Guard { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public MatchCaseNode(TextSpan span, PatternNode pattern, ExpressionNode? guard, IReadOnlyList<StatementNode> body)
        : base(span)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        Guard = guard;
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

// Phase 10: With expressions

/// <summary>
/// Represents a with-expression for non-destructive mutation.
/// §WITH §REF[name=person]
///   §SET[Name] "New Name"
/// §/WITH
/// generates: person with { Name = "New Name" }
/// </summary>
public sealed class WithExpressionNode : ExpressionNode
{
    /// <summary>
    /// The target expression to copy and modify.
    /// </summary>
    public ExpressionNode Target { get; }

    /// <summary>
    /// The property assignments in the with expression.
    /// </summary>
    public IReadOnlyList<WithPropertyAssignmentNode> Assignments { get; }

    public WithExpressionNode(TextSpan span, ExpressionNode target, IReadOnlyList<WithPropertyAssignmentNode> assignments)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a property assignment within a with-expression.
/// §SET[Name] "New Name"
/// </summary>
public sealed class WithPropertyAssignmentNode : AstNode
{
    public string PropertyName { get; }
    public ExpressionNode Value { get; }

    public WithPropertyAssignmentNode(TextSpan span, string propertyName, ExpressionNode value)
        : base(span)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Base class for pattern nodes used in match expressions.
/// </summary>
public abstract class PatternNode : AstNode
{
    protected PatternNode(TextSpan span) : base(span) { }
}

// Existing pattern nodes (used by match expressions)

/// <summary>
/// Represents a wildcard pattern that matches anything.
/// </summary>
public sealed class WildcardPatternNode : PatternNode
{
    public WildcardPatternNode(TextSpan span) : base(span) { }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a variable pattern that captures a value.
/// </summary>
public sealed class VariablePatternNode : PatternNode
{
    public string Name { get; }

    public VariablePatternNode(TextSpan span, string name) : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a literal pattern that matches a specific value.
/// </summary>
public sealed class LiteralPatternNode : PatternNode
{
    public ExpressionNode Literal { get; }

    public LiteralPatternNode(TextSpan span, ExpressionNode literal) : base(span)
    {
        Literal = literal ?? throw new ArgumentNullException(nameof(literal));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a Some pattern for Option types.
/// </summary>
public sealed class SomePatternNode : PatternNode
{
    public PatternNode InnerPattern { get; }

    public SomePatternNode(TextSpan span, PatternNode innerPattern) : base(span)
    {
        InnerPattern = innerPattern ?? throw new ArgumentNullException(nameof(innerPattern));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a None pattern for Option types.
/// </summary>
public sealed class NonePatternNode : PatternNode
{
    public NonePatternNode(TextSpan span) : base(span) { }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an Ok pattern for Result types.
/// </summary>
public sealed class OkPatternNode : PatternNode
{
    public PatternNode InnerPattern { get; }

    public OkPatternNode(TextSpan span, PatternNode innerPattern) : base(span)
    {
        InnerPattern = innerPattern ?? throw new ArgumentNullException(nameof(innerPattern));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an Err pattern for Result types.
/// </summary>
public sealed class ErrPatternNode : PatternNode
{
    public PatternNode InnerPattern { get; }

    public ErrPatternNode(TextSpan span, PatternNode innerPattern) : base(span)
    {
        InnerPattern = innerPattern ?? throw new ArgumentNullException(nameof(innerPattern));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

// Phase 10: Advanced Patterns

/// <summary>
/// Represents a positional pattern for deconstructing types.
/// §PPOS[Point] §VAR[x] §VAR[y]
/// generates: Point(var x, var y)
/// </summary>
public sealed class PositionalPatternNode : PatternNode
{
    /// <summary>
    /// The type being matched.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// The patterns for each position.
    /// </summary>
    public IReadOnlyList<PatternNode> Patterns { get; }

    public PositionalPatternNode(TextSpan span, string typeName, IReadOnlyList<PatternNode> patterns)
        : base(span)
    {
        TypeName = typeName ?? "";
        Patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a property pattern for matching object properties.
/// §PPROP[Person] §PMATCH[Age] §PREL[gte] 18
/// generates: Person { Age: >= 18 }
/// </summary>
public sealed class PropertyPatternNode : PatternNode
{
    /// <summary>
    /// The type being matched (optional).
    /// </summary>
    public string? TypeName { get; }

    /// <summary>
    /// The property matches.
    /// </summary>
    public IReadOnlyList<PropertyMatchNode> Matches { get; }

    public PropertyPatternNode(TextSpan span, string? typeName, IReadOnlyList<PropertyMatchNode> matches)
        : base(span)
    {
        TypeName = typeName;
        Matches = matches ?? throw new ArgumentNullException(nameof(matches));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a property match within a property pattern.
/// §PMATCH[Age] §PREL[gte] 18
/// </summary>
public sealed class PropertyMatchNode : AstNode
{
    public string PropertyName { get; }
    public PatternNode Pattern { get; }

    public PropertyMatchNode(TextSpan span, string propertyName, PatternNode pattern)
        : base(span)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a relational pattern.
/// §PREL[gte] 18
/// generates: >= 18
/// </summary>
public sealed class RelationalPatternNode : PatternNode
{
    /// <summary>
    /// The operator: lt, lte, gt, gte
    /// </summary>
    public string Operator { get; }

    /// <summary>
    /// The value being compared to.
    /// </summary>
    public ExpressionNode Value { get; }

    public RelationalPatternNode(TextSpan span, string @operator, ExpressionNode value)
        : base(span)
    {
        Operator = @operator ?? throw new ArgumentNullException(nameof(@operator));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a list pattern for matching collections.
/// §PLIST §VAR[first] §REST[rest]
/// generates: [var first, ..var rest]
/// </summary>
public sealed class ListPatternNode : PatternNode
{
    /// <summary>
    /// The patterns for each element.
    /// </summary>
    public IReadOnlyList<PatternNode> Patterns { get; }

    /// <summary>
    /// The slice pattern if present (..rest).
    /// </summary>
    public VarPatternNode? SlicePattern { get; }

    public ListPatternNode(TextSpan span, IReadOnlyList<PatternNode> patterns, VarPatternNode? slicePattern)
        : base(span)
    {
        Patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
        SlicePattern = slicePattern;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a var pattern that captures a value.
/// §VAR[x]
/// generates: var x
/// </summary>
public sealed class VarPatternNode : PatternNode
{
    /// <summary>
    /// The variable name to bind.
    /// </summary>
    public string Name { get; }

    public VarPatternNode(TextSpan span, string name)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a constant pattern that matches a literal value.
/// </summary>
public sealed class ConstantPatternNode : PatternNode
{
    public ExpressionNode Value { get; }

    public ConstantPatternNode(TextSpan span, ExpressionNode value)
        : base(span)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
