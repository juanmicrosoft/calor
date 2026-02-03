using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Base class for type definition nodes.
/// </summary>
public abstract class TypeDefinitionNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public AttributeCollection Attributes { get; }

    protected TypeDefinitionNode(TextSpan span, string id, string name, AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }
}

/// <summary>
/// Represents a record type definition.
/// §RECORD[id=xxx][name=Person]
///   §FIELD[name=Name][type=STRING]
///   §FIELD[name=Age][type=INT]
/// §END_RECORD[id=xxx]
/// </summary>
public sealed class RecordDefinitionNode : TypeDefinitionNode
{
    public IReadOnlyList<FieldDefinitionNode> Fields { get; }

    public RecordDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<FieldDefinitionNode> fields,
        AttributeCollection attributes)
        : base(span, id, name, attributes)
    {
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a field in a record definition.
/// §FIELD[name=xxx][type=xxx]
/// </summary>
public sealed class FieldDefinitionNode : AstNode
{
    public string Name { get; }
    public string TypeName { get; }
    public ExpressionNode? DefaultValue { get; }
    public AttributeCollection Attributes { get; }

    public FieldDefinitionNode(
        TextSpan span,
        string name,
        string typeName,
        ExpressionNode? defaultValue,
        AttributeCollection attributes)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        DefaultValue = defaultValue;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a discriminated union type definition.
/// §TYPE[id=xxx][name=Shape]
///   §VARIANT[name=Circle] §FIELD[name=Radius][type=FLOAT]
///   §VARIANT[name=Rectangle] §FIELD[name=Width][type=FLOAT] §FIELD[name=Height][type=FLOAT]
/// §END_TYPE[id=xxx]
/// </summary>
public sealed class UnionTypeDefinitionNode : TypeDefinitionNode
{
    public IReadOnlyList<VariantDefinitionNode> Variants { get; }

    public UnionTypeDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<VariantDefinitionNode> variants,
        AttributeCollection attributes)
        : base(span, id, name, attributes)
    {
        Variants = variants ?? throw new ArgumentNullException(nameof(variants));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a variant in a discriminated union.
/// §VARIANT[name=xxx]
/// </summary>
public sealed class VariantDefinitionNode : AstNode
{
    public string Name { get; }
    public IReadOnlyList<FieldDefinitionNode> Fields { get; }
    public AttributeCollection Attributes { get; }

    public VariantDefinitionNode(
        TextSpan span,
        string name,
        IReadOnlyList<FieldDefinitionNode> fields,
        AttributeCollection attributes)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents a type reference with optional generic arguments.
/// Example: Result[INT, STRING], Option[Person]
/// </summary>
public sealed class TypeReferenceNode : AstNode
{
    public string Name { get; }
    public IReadOnlyList<TypeReferenceNode> TypeArguments { get; }

    public TypeReferenceNode(TextSpan span, string name, IReadOnlyList<TypeReferenceNode>? typeArguments = null)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeArguments = typeArguments ?? Array.Empty<TypeReferenceNode>();
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;

    public override string ToString()
    {
        if (TypeArguments.Count == 0)
            return Name;
        return $"{Name}<{string.Join(", ", TypeArguments)}>";
    }
}

/// <summary>
/// Represents a record instantiation expression.
/// §RECORD[type=Person] §FIELD[name=Name] STR:"Alice" §FIELD[name=Age] INT:30
/// </summary>
public sealed class RecordCreationNode : ExpressionNode
{
    public string TypeName { get; }
    public IReadOnlyList<FieldAssignmentNode> Fields { get; }

    public RecordCreationNode(
        TextSpan span,
        string typeName,
        IReadOnlyList<FieldAssignmentNode> fields)
        : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a field assignment in a record creation.
/// </summary>
public sealed class FieldAssignmentNode : AstNode
{
    public string FieldName { get; }
    public ExpressionNode Value { get; }

    public FieldAssignmentNode(TextSpan span, string fieldName, ExpressionNode value)
        : base(span)
    {
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents field access on an expression.
/// §REF[name=person].Name
/// </summary>
public sealed class FieldAccessNode : ExpressionNode
{
    public ExpressionNode Target { get; }
    public string FieldName { get; }

    public FieldAccessNode(TextSpan span, ExpressionNode target, string fieldName)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an Option.Some expression.
/// §SOME expression
/// </summary>
public sealed class SomeExpressionNode : ExpressionNode
{
    public ExpressionNode Value { get; }

    public SomeExpressionNode(TextSpan span, ExpressionNode value)
        : base(span)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an Option.None expression.
/// §NONE[type=xxx]
/// </summary>
public sealed class NoneExpressionNode : ExpressionNode
{
    public string? TypeName { get; }

    public NoneExpressionNode(TextSpan span, string? typeName)
        : base(span)
    {
        TypeName = typeName;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a Result.Ok expression.
/// §OK expression
/// </summary>
public sealed class OkExpressionNode : ExpressionNode
{
    public ExpressionNode Value { get; }

    public OkExpressionNode(TextSpan span, ExpressionNode value)
        : base(span)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a Result.Err expression.
/// §ERR expression
/// </summary>
public sealed class ErrExpressionNode : ExpressionNode
{
    public ExpressionNode Error { get; }

    public ErrExpressionNode(TextSpan span, ExpressionNode error)
        : base(span)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an enum member in an enum definition.
/// </summary>
public sealed class EnumMemberNode : AstNode
{
    public string Name { get; }
    public string? Value { get; }

    public EnumMemberNode(TextSpan span, string name, string? value)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an enum type definition.
/// §ENUM{id:Name} or §ENUM{id:Name:underlyingType}
///   Red
///   Green = 1
/// §/ENUM{id}
/// </summary>
public sealed class EnumDefinitionNode : TypeDefinitionNode
{
    /// <summary>
    /// The underlying type of the enum (null = default int, or "i8", "u8", "i16", etc.).
    /// </summary>
    public string? UnderlyingType { get; }

    /// <summary>
    /// The enum members.
    /// </summary>
    public IReadOnlyList<EnumMemberNode> Members { get; }

    public EnumDefinitionNode(
        TextSpan span,
        string id,
        string name,
        string? underlyingType,
        IReadOnlyList<EnumMemberNode> members,
        AttributeCollection attributes)
        : base(span, id, name, attributes)
    {
        UnderlyingType = underlyingType;
        Members = members ?? throw new ArgumentNullException(nameof(members));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
