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
    public TextSpan IdentifierSpan { get; }
    public string TypeName { get; }
    public TextSpan TypeNameSpan { get; }
    public Visibility Visibility { get; }
    public MethodModifiers Modifiers { get; }
    public PropertyAccessorNode? Getter { get; }
    public PropertyAccessorNode? Setter { get; }
    public PropertyAccessorNode? Initer { get; }
    public ExpressionNode? DefaultValue { get; }

    /// <summary>
    /// v0.17 S1 / #1136 — the effect row on a function-typed property, written in
    /// the header group like a field's: <c>§PROP{p1:Stage:Func&lt;i32&gt;:pub:get,set} §E{cw}</c>.
    /// <para>Before this, <c>§PROP</c> could not carry a row AT ALL — the parser
    /// never consumed an <c>Effects</c> token there, so the declaration was 4x
    /// Calor0100. #1136 recorded the consequence: a property read was
    /// <c>Unknown</c> BY CONSTRUCTION rather than by an inference gap, which is
    /// why that issue's fix set is {a row on §PROP} UNION {fail closed} and says
    /// fixing only fields leaves properties laundering. With fail-closed alone
    /// the diagnostic tells the author to "state a row on the argument's
    /// declaration" — an instruction a §PROP could not follow.</para>
    /// <para>Nothing binds an effect variable here, exactly as for a field, so
    /// the parser rejects a variable in this position.</para>
    /// </summary>
    public EffectsNode? Row { get; }
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
    public bool IsRequired => Modifiers.HasFlag(MethodModifiers.Required);

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
        IReadOnlyList<CalorAttributeNode> csharpAttributes,
        TextSpan? identifierSpan = null,
        TextSpan? typeNameSpan = null,
        EffectsNode? row = null)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IdentifierSpan = identifierSpan ?? span;
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        TypeNameSpan = typeNameSpan ?? TextSpan.Empty;
        Visibility = visibility;
        Modifiers = modifiers;
        Getter = getter;
        Setter = setter;
        Initer = initer;
        DefaultValue = defaultValue;
        Row = row;
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


}

/// <summary>
/// Represents an indexer in a class or interface.
/// §IXER{ix1:int:pub}
///   §I{int:index}
///   §GET
///     §R ...
///   §/GET
///   §SET
///     ...
///   §/SET
/// §/IXER{ix1}
///
/// Compact auto-property form:
/// §IXER{ix1:int:pub:get,set} (int:index)
/// </summary>
public sealed class IndexerNode : AstNode
{
    public string Id { get; }
    public string TypeName { get; }
    public Visibility Visibility { get; }
    public MethodModifiers Modifiers { get; }
    public IReadOnlyList<ParameterNode> Parameters { get; }
    public PropertyAccessorNode? Getter { get; }
    public PropertyAccessorNode? Setter { get; }
    public PropertyAccessorNode? Initer { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@JsonProperty], [@Obsolete]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public bool IsOverride => Modifiers.HasFlag(MethodModifiers.Override);
    public bool IsVirtual => Modifiers.HasFlag(MethodModifiers.Virtual);
    public bool IsAbstract => Modifiers.HasFlag(MethodModifiers.Abstract);
    public bool IsSealed => Modifiers.HasFlag(MethodModifiers.Sealed);

    public IndexerNode(
        TextSpan span,
        string id,
        string typeName,
        Visibility visibility,
        MethodModifiers modifiers,
        IReadOnlyList<ParameterNode> parameters,
        PropertyAccessorNode? getter,
        PropertyAccessorNode? setter,
        PropertyAccessorNode? initer,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Visibility = visibility;
        Modifiers = modifiers;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Getter = getter;
        Setter = setter;
        Initer = initer;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }

    /// <summary>
    /// True if this is an auto-implemented indexer (all accessors have empty bodies).
    /// </summary>
    public bool IsAutoIndexer =>
        (Getter == null || Getter.IsAutoImplemented) &&
        (Setter == null || Setter.IsAutoImplemented) &&
        (Initer == null || Initer.IsAutoImplemented);


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
    public bool IsStatic { get; }
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
        : this(span, id, visibility, parameters, preconditions, initializer, body, attributes, Array.Empty<CalorAttributeNode>(), isStatic: false)
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
        : this(span, id, visibility, parameters, preconditions, initializer, body, attributes, csharpAttributes, isStatic: false)
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
        IReadOnlyList<CalorAttributeNode> csharpAttributes,
        bool isStatic)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Visibility = visibility;
        IsStatic = isStatic;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Preconditions = preconditions ?? throw new ArgumentNullException(nameof(preconditions));
        Initializer = initializer;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }


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
    RightShift, // >>=
    NullCoalesce // ??=
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
    public TextSpan? VariableSpan { get; }
    public string? VariableType { get; }
    public ExpressionNode Resource { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public UsingStatementNode(
        TextSpan span,
        string? variableName,
        string? variableType,
        ExpressionNode resource,
        IReadOnlyList<StatementNode> body,
        TextSpan? variableSpan = null)
        : this(span, null, variableName, variableType, resource, body, variableSpan)
    {
    }

    public UsingStatementNode(
        TextSpan span,
        string? id,
        string? variableName,
        string? variableType,
        ExpressionNode resource,
        IReadOnlyList<StatementNode> body,
        TextSpan? variableSpan = null)
        : base(span)
    {
        Id = id;
        VariableName = variableName;
        VariableSpan = variableName == null ? null : variableSpan ?? span;
        VariableType = variableType;
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }


}
