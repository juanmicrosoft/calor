using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Represents a property in a class.
/// §PROP[p001:Name:string:pub]
///   §GET
///   §SET[pri]
///     §Q §OP[kind=gte] §REF[name=value] 0   // setter precondition
/// §/PROP[p001]
/// </summary>
public sealed class PropertyNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public string TypeName { get; }
    public Visibility Visibility { get; }
    public PropertyAccessorNode? Getter { get; }
    public PropertyAccessorNode? Setter { get; }
    public PropertyAccessorNode? Initer { get; }
    public ExpressionNode? DefaultValue { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@JsonProperty("name")], [@Required]).
    /// </summary>
    public IReadOnlyList<OpalAttributeNode> CSharpAttributes { get; }

    public PropertyNode(
        TextSpan span,
        string id,
        string name,
        string typeName,
        Visibility visibility,
        PropertyAccessorNode? getter,
        PropertyAccessorNode? setter,
        PropertyAccessorNode? initer,
        ExpressionNode? defaultValue,
        AttributeCollection attributes)
        : this(span, id, name, typeName, visibility, getter, setter, initer, defaultValue, attributes, Array.Empty<OpalAttributeNode>())
    {
    }

    public PropertyNode(
        TextSpan span,
        string id,
        string name,
        string typeName,
        Visibility visibility,
        PropertyAccessorNode? getter,
        PropertyAccessorNode? setter,
        PropertyAccessorNode? initer,
        ExpressionNode? defaultValue,
        AttributeCollection attributes,
        IReadOnlyList<OpalAttributeNode> csharpAttributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Visibility = visibility;
        Getter = getter;
        Setter = setter;
        Initer = initer;
        DefaultValue = defaultValue;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<OpalAttributeNode>();
    }

    public bool IsAutoProperty => Getter == null && Setter == null && Initer == null;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a property accessor (get, set, or init).
/// §GET
/// §SET[pri]
/// </summary>
public sealed class PropertyAccessorNode : AstNode
{
    public enum AccessorKind { Get, Set, Init }

    public AccessorKind Kind { get; }
    public Visibility? Visibility { get; }
    public IReadOnlyList<RequiresNode> Preconditions { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@MethodImpl(MethodImplOptions.AggressiveInlining)]).
    /// </summary>
    public IReadOnlyList<OpalAttributeNode> CSharpAttributes { get; }

    public PropertyAccessorNode(
        TextSpan span,
        AccessorKind kind,
        Visibility? visibility,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : this(span, kind, visibility, preconditions, body, attributes, Array.Empty<OpalAttributeNode>())
    {
    }

    public PropertyAccessorNode(
        TextSpan span,
        AccessorKind kind,
        Visibility? visibility,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes,
        IReadOnlyList<OpalAttributeNode> csharpAttributes)
        : base(span)
    {
        Kind = kind;
        Visibility = visibility;
        Preconditions = preconditions ?? throw new ArgumentNullException(nameof(preconditions));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<OpalAttributeNode>();
    }

    public bool IsAutoImplemented => Body.Count == 0;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a constructor.
/// §CTOR[ctor1:pub]
///   §I[string:name] §I[f64:radius]
///   §Q §OP[kind=gt] §REF[name=radius] 0
///   §BASE §A §REF[name=name] §/BASE
///   §ASSIGN §REF[name=Radius] §REF[name=radius]
/// §/CTOR[ctor1]
/// </summary>
public sealed class ConstructorNode : AstNode
{
    public string Id { get; }
    public Visibility Visibility { get; }
    public IReadOnlyList<ParameterNode> Parameters { get; }
    public IReadOnlyList<RequiresNode> Preconditions { get; }
    public ConstructorInitializerNode? Initializer { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@Obsolete], [@JsonConstructor]).
    /// </summary>
    public IReadOnlyList<OpalAttributeNode> CSharpAttributes { get; }

    public ConstructorNode(
        TextSpan span,
        string id,
        Visibility visibility,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<RequiresNode> preconditions,
        ConstructorInitializerNode? initializer,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : this(span, id, visibility, parameters, preconditions, initializer, body, attributes, Array.Empty<OpalAttributeNode>())
    {
    }

    public ConstructorNode(
        TextSpan span,
        string id,
        Visibility visibility,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<RequiresNode> preconditions,
        ConstructorInitializerNode? initializer,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes,
        IReadOnlyList<OpalAttributeNode> csharpAttributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Visibility = visibility;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Preconditions = preconditions ?? throw new ArgumentNullException(nameof(preconditions));
        Initializer = initializer;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<OpalAttributeNode>();
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a constructor initializer (: base(...) or : this(...)).
/// §BASE §A §REF[name=name] §/BASE
/// </summary>
public sealed class ConstructorInitializerNode : AstNode
{
    public bool IsBaseCall { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }

    public ConstructorInitializerNode(
        TextSpan span,
        bool isBaseCall,
        IReadOnlyList<ExpressionNode> arguments)
        : base(span)
    {
        IsBaseCall = isBaseCall;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an assignment statement.
/// §ASSIGN §REF[name=Radius] §REF[name=radius]
/// </summary>
public sealed class AssignmentStatementNode : StatementNode
{
    public ExpressionNode Target { get; }
    public ExpressionNode Value { get; }

    public AssignmentStatementNode(TextSpan span, ExpressionNode target, ExpressionNode value)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
