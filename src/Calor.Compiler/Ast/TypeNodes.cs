using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Base class for type definition nodes.
/// </summary>
public abstract class TypeDefinitionNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public TextSpan IdentifierSpan { get; }
    public AttributeCollection Attributes { get; }

    protected TypeDefinitionNode(
        TextSpan span,
        string id,
        string name,
        AttributeCollection attributes,
        TextSpan? identifierSpan = null)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IdentifierSpan = identifierSpan ?? span;
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


}

/// <summary>
/// Represents a field in a record definition.
/// §FIELD[name=xxx][type=xxx]
/// </summary>
public sealed class FieldDefinitionNode : AstNode
{
    public string Name { get; }
    public string TypeName { get; }
    public TextSpan TypeNameSpan { get; }
    public ExpressionNode? DefaultValue { get; }
    public AttributeCollection Attributes { get; }

    public FieldDefinitionNode(
        TextSpan span,
        string name,
        string typeName,
        ExpressionNode? defaultValue,
        AttributeCollection attributes,
        TextSpan? typeNameSpan = null)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        TypeNameSpan = typeNameSpan ?? TextSpan.Empty;
        DefaultValue = defaultValue;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


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
    public TextSpan TypeNameSpan { get; }
    public IReadOnlyList<FieldAssignmentNode> Fields { get; }

    public RecordCreationNode(
        TextSpan span,
        string typeName,
        IReadOnlyList<FieldAssignmentNode> fields,
        TextSpan? typeNameSpan = null)
        : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        TypeNameSpan = typeNameSpan ?? TextSpan.Empty;
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }


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


}

/// <summary>
/// Represents field access on an expression.
/// §REF[name=person].Name
/// </summary>
public sealed class FieldAccessNode : ExpressionNode
{
    public ExpressionNode Target { get; }
    public string FieldName { get; }
    public TextSpan FieldNameSpan { get; }

    public FieldAccessNode(
        TextSpan span,
        ExpressionNode target,
        string fieldName,
        TextSpan? fieldNameSpan = null)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        FieldNameSpan = fieldNameSpan ?? span;
    }


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


}

/// <summary>
/// Represents an Option.None expression.
/// §NONE[type=xxx]
/// </summary>
public sealed class NoneExpressionNode : ExpressionNode
{
    public string? TypeName { get; }
    public TextSpan? TypeNameSpan { get; }

    public NoneExpressionNode(
        TextSpan span,
        string? typeName,
        TextSpan? typeNameSpan = null)
        : base(span)
    {
        TypeName = typeName;
        TypeNameSpan = typeName == null ? null : typeNameSpan ?? TextSpan.Empty;
    }


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


}

/// <summary>
/// Represents an enum member in an enum definition.
/// </summary>
public sealed class EnumMemberNode : AstNode
{
    public string Name { get; }
    public string? Value { get; }
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public EnumMemberNode(TextSpan span, string name, string? value)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value;
        CSharpAttributes = Array.Empty<CalorAttributeNode>();
    }

    public EnumMemberNode(TextSpan span, string name, string? value, IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value;
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }


}

/// <summary>
/// Represents an enum type definition.
/// §EN{id:Name:vis} or §EN{id:Name:vis:underlyingType}
///   Red
///   Green = 1
/// §/EN{id}
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

    /// <summary>
    /// The visibility of the enum (public, internal, private, etc.).
    /// </summary>
    public Visibility Visibility { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@Flags], [@Obsolete]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public EnumDefinitionNode(
        TextSpan span,
        string id,
        string name,
        string? underlyingType,
        IReadOnlyList<EnumMemberNode> members,
        AttributeCollection attributes)
        : this(span, id, name, underlyingType, members, attributes, Array.Empty<CalorAttributeNode>())
    {
    }

    public EnumDefinitionNode(
        TextSpan span,
        string id,
        string name,
        string? underlyingType,
        IReadOnlyList<EnumMemberNode> members,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes,
        Visibility visibility = Visibility.Public,
        TextSpan? identifierSpan = null)
        : base(span, id, name, attributes, identifierSpan)
    {
        UnderlyingType = underlyingType;
        Members = members ?? throw new ArgumentNullException(nameof(members));
        Visibility = visibility;
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }


}
