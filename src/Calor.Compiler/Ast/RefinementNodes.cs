using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a refinement type definition.
/// §RTYPE{id:Name:baseType} (predicate using #)
/// </summary>
public sealed class RefinementTypeNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public string BaseTypeName { get; }
    public ExpressionNode Predicate { get; }
    public AttributeCollection Attributes { get; }

    public RefinementTypeNode(
        TextSpan span,
        string id,
        string name,
        string baseTypeName,
        ExpressionNode predicate,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        BaseTypeName = baseTypeName ?? throw new ArgumentNullException(nameof(baseTypeName));
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents the self-reference placeholder (#) inside a refinement predicate.
/// Refers to the value being constrained by the refinement type.
/// </summary>
public sealed class SelfRefNode : ExpressionNode
{
    public SelfRefNode(TextSpan span) : base(span) { }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a proof obligation statement.
/// §PROOF{id:description} (boolean-expression)
/// </summary>
public sealed class ProofObligationNode : StatementNode
{
    public string Id { get; }
    public string? Description { get; }
    public ExpressionNode Condition { get; }
    public AttributeCollection Attributes { get; }

    public ProofObligationNode(
        TextSpan span,
        string id,
        string? description,
        ExpressionNode condition,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Description = description;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an indexed type definition (size-parameterized type).
/// §ITYPE{id:Name:baseType:sizeParam}                     // no constraint
/// §ITYPE{id:Name:baseType:sizeParam} (constraint on #)   // with constraint
/// </summary>
public sealed class IndexedTypeNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public string BaseTypeName { get; }
    public string SizeParam { get; }
    public ExpressionNode? Constraint { get; }
    public AttributeCollection Attributes { get; }

    public IndexedTypeNode(
        TextSpan span,
        string id,
        string name,
        string baseTypeName,
        string sizeParam,
        ExpressionNode? constraint,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        BaseTypeName = baseTypeName ?? throw new ArgumentNullException(nameof(baseTypeName));
        SizeParam = sizeParam ?? throw new ArgumentNullException(nameof(sizeParam));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Constraint = constraint;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Inline refinement information attached to a parameter.
/// Parsed from §I{baseType:name | (predicate using #)}.
/// </summary>
public sealed record InlineRefinementInfo(string BaseTypeName, ExpressionNode Predicate);
