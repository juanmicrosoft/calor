using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

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
    public MethodModifiers Modifiers { get; }
    public PropertyAccessorNode? Getter { get; }
    public PropertyAccessorNode? Setter { get; }
    public PropertyAccessorNode? Initer { get; }
    public ExpressionNode? DefaultValue { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@JsonProperty("name")], [@Required]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public bool IsOverride => Modifiers.HasFlag(MethodModifiers.Override);
    public bool IsVirtual => Modifiers.HasFlag(MethodModifiers.Virtual);
    public bool IsAbstract => Modifiers.HasFlag(MethodModifiers.Abstract);
    public bool IsStatic => Modifiers.HasFlag(MethodModifiers.Static);
    public bool IsSealed => Modifiers.HasFlag(MethodModifiers.Sealed);

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
        : this(span, id, name, typeName, visibility, MethodModifiers.None, getter, setter, initer, defaultValue, attributes, Array.Empty<CalorAttributeNode>())
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
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, id, name, typeName, visibility, MethodModifiers.None, getter, setter, initer, defaultValue, attributes, csharpAttributes)
    {
    }

    public PropertyNode(
        TextSpan span,
        string id,
        string name,
        string typeName,
        Visibility visibility,
        MethodModifiers modifiers,
        PropertyAccessorNode? getter,
        PropertyAccessorNode? setter,
        PropertyAccessorNode? initer,
        ExpressionNode? defaultValue,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Visibility = visibility;
        Modifiers = modifiers;
        Getter = getter;
        Setter = setter;
        Initer = initer;
        DefaultValue = defaultValue;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }

    /// <summary>
    /// True if this is an auto-implemented property (all accessors have empty bodies).
    /// </summary>
    public bool IsAutoProperty =>
        (Getter == null || Getter.IsAutoImplemented) &&
        (Setter == null || Setter.IsAutoImplemented) &&
        (Initer == null || Initer.IsAutoImplemented);

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
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public PropertyAccessorNode(
        TextSpan span,
        AccessorKind kind,
        Visibility? visibility,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : this(span, kind, visibility, preconditions, body, attributes, Array.Empty<CalorAttributeNode>())
    {
    }

    public PropertyAccessorNode(
        TextSpan span,
        AccessorKind kind,
        Visibility? visibility,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span)
    {
        Kind = kind;
        Visibility = visibility;
        Preconditions = preconditions ?? throw new ArgumentNullException(nameof(preconditions));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
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
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public ConstructorNode(
        TextSpan span,
        string id,
        Visibility visibility,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<RequiresNode> preconditions,
        ConstructorInitializerNode? initializer,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : this(span, id, visibility, parameters, preconditions, initializer, body, attributes, Array.Empty<CalorAttributeNode>())
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
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Visibility = visibility;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Preconditions = preconditions ?? throw new ArgumentNullException(nameof(preconditions));
        Initializer = initializer;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
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

/// <summary>
/// Compound assignment operator kind (+= -= *= /= etc.)
/// </summary>
public enum CompoundAssignmentOperator
{
    Add,       // +=
    Subtract,  // -=
    Multiply,  // *=
    Divide,    // /=
    Modulo,    // %=
    BitwiseAnd, // &=
    BitwiseOr,  // |=
    BitwiseXor, // ^=
    LeftShift,  // <<=
    RightShift  // >>=
}

/// <summary>
/// Represents a compound assignment statement (+=, -=, *=, /=, etc.)
/// §SET target = (+ target value) for +=
/// </summary>
public sealed class CompoundAssignmentStatementNode : StatementNode
{
    public ExpressionNode Target { get; }
    public CompoundAssignmentOperator Operator { get; }
    public ExpressionNode Value { get; }

    public CompoundAssignmentStatementNode(
        TextSpan span,
        ExpressionNode target,
        CompoundAssignmentOperator op,
        ExpressionNode value)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Operator = op;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a using statement for IDisposable resources.
/// §USING[type:name] = expr
///   ...body...
/// §/USING
/// </summary>
public sealed class UsingStatementNode : StatementNode
{
    public string? Id { get; }
    public string? VariableName { get; }
    public string? VariableType { get; }
    public ExpressionNode Resource { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public UsingStatementNode(
        TextSpan span,
        string? variableName,
        string? variableType,
        ExpressionNode resource,
        IReadOnlyList<StatementNode> body)
        : this(span, null, variableName, variableType, resource, body)
    {
    }

    public UsingStatementNode(
        TextSpan span,
        string? id,
        string? variableName,
        string? variableType,
        ExpressionNode resource,
        IReadOnlyList<StatementNode> body)
        : base(span)
    {
        Id = id;
        VariableName = variableName;
        VariableType = variableType;
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
