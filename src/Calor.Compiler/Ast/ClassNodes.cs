using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Method modifiers for OOP.
/// </summary>
[Flags]
public enum MethodModifiers
{
    None = 0,
    Virtual = 1,
    Override = 2,
    Abstract = 4,
    Sealed = 8,
    Static = 16,
    Const = 32,
    Readonly = 64,
    Required = 128,
    Partial = 256
}

/// <summary>
/// Represents an interface definition.
/// §IFACE{i001:IShape}&lt;T&gt;
///   §METHOD{m001:Area} §O{f64} §/METHOD{m001}
/// §/IFACE{i001}
/// </summary>
public sealed class InterfaceDefinitionNode : TypeDefinitionNode
{
    /// <summary>
    /// Interface methods (signatures only).
    /// </summary>
    public IReadOnlyList<MethodSignatureNode> Methods { get; }

    /// <summary>
    /// Interface properties.
    /// </summary>
    public IReadOnlyList<PropertyNode> Properties { get; }

    /// <summary>
    /// Interfaces this interface extends.
    /// </summary>
    public IReadOnlyList<string> BaseInterfaces { get; }

    /// <summary>
    /// Type parameters if this is a generic interface.
    /// </summary>
    public IReadOnlyList<TypeParameterNode> TypeParameters { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@Obsolete], [@ComVisible]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public InterfaceDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<string> baseInterfaces,
        IReadOnlyList<MethodSignatureNode> methods,
        AttributeCollection attributes)
        : this(span, id, name, baseInterfaces, Array.Empty<TypeParameterNode>(), methods, Array.Empty<PropertyNode>(), attributes, Array.Empty<CalorAttributeNode>())
    {
    }

    public InterfaceDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<string> baseInterfaces,
        IReadOnlyList<MethodSignatureNode> methods,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, id, name, baseInterfaces, Array.Empty<TypeParameterNode>(), methods, Array.Empty<PropertyNode>(), attributes, csharpAttributes)
    {
    }

    public InterfaceDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<string> baseInterfaces,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<MethodSignatureNode> methods,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, id, name, baseInterfaces, typeParameters, methods, Array.Empty<PropertyNode>(), attributes, csharpAttributes)
    {
    }

    public InterfaceDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<string> baseInterfaces,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<MethodSignatureNode> methods,
        IReadOnlyList<PropertyNode> properties,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span, id, name, attributes)
    {
        BaseInterfaces = baseInterfaces ?? throw new ArgumentNullException(nameof(baseInterfaces));
        TypeParameters = typeParameters ?? throw new ArgumentNullException(nameof(typeParameters));
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
        Properties = properties ?? Array.Empty<PropertyNode>();
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a method signature (for interfaces).
/// §METHOD[m001:Area] §O[f64] §E[] §/METHOD[m001]
/// </summary>
public sealed class MethodSignatureNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<TypeParameterNode> TypeParameters { get; }
    public IReadOnlyList<ParameterNode> Parameters { get; }
    public OutputNode? Output { get; }
    public EffectsNode? Effects { get; }
    public IReadOnlyList<RequiresNode> Preconditions { get; }
    public IReadOnlyList<EnsuresNode> Postconditions { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@Obsolete]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    /// <summary>
    /// True if this method signature has any contracts (preconditions or postconditions).
    /// </summary>
    public bool HasContracts => Preconditions.Count > 0 || Postconditions.Count > 0;

    public MethodSignatureNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        AttributeCollection attributes)
        : this(span, id, name, typeParameters, parameters, output, effects,
               Array.Empty<RequiresNode>(), Array.Empty<EnsuresNode>(),
               attributes, Array.Empty<CalorAttributeNode>())
    {
    }

    public MethodSignatureNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, id, name, typeParameters, parameters, output, effects,
               Array.Empty<RequiresNode>(), Array.Empty<EnsuresNode>(),
               attributes, csharpAttributes)
    {
    }

    public MethodSignatureNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeParameters = typeParameters ?? throw new ArgumentNullException(nameof(typeParameters));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Output = output;
        Effects = effects;
        Preconditions = preconditions ?? Array.Empty<RequiresNode>();
        Postconditions = postconditions ?? Array.Empty<EnsuresNode>();
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a class definition.
/// §CLASS[c001:Shape:abs]
///   §IMPL[IShape]
///   §FIELD[string:Name:pri]
///   §METHOD[m001:Area:pub:abs] §O[f64] §E[] §/METHOD[m001]
/// §/CLASS[c001]
/// </summary>
public sealed class ClassDefinitionNode : TypeDefinitionNode
{
    /// <summary>
    /// True if this is an abstract class.
    /// </summary>
    public bool IsAbstract { get; }

    /// <summary>
    /// True if this is a sealed class.
    /// </summary>
    public bool IsSealed { get; }

    /// <summary>
    /// True if this is a partial class.
    /// </summary>
    public bool IsPartial { get; }

    /// <summary>
    /// True if this is a static class.
    /// </summary>
    public bool IsStatic { get; }

    /// <summary>
    /// True if this is a struct (value type).
    /// </summary>
    public bool IsStruct { get; }

    /// <summary>
    /// True if this is a readonly struct.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// The visibility level of this class (public, internal, protected, private).
    /// </summary>
    public Visibility Visibility { get; }

    /// <summary>
    /// The base class (if any).
    /// </summary>
    public string? BaseClass { get; }

    /// <summary>
    /// Interfaces implemented by this class.
    /// </summary>
    public IReadOnlyList<string> ImplementedInterfaces { get; }

    /// <summary>
    /// Type parameters if this is a generic class.
    /// </summary>
    public IReadOnlyList<TypeParameterNode> TypeParameters { get; }

    /// <summary>
    /// Fields defined in this class.
    /// </summary>
    public IReadOnlyList<ClassFieldNode> Fields { get; }

    /// <summary>
    /// Properties defined in this class.
    /// </summary>
    public IReadOnlyList<PropertyNode> Properties { get; }

    /// <summary>
    /// Constructors defined in this class.
    /// </summary>
    public IReadOnlyList<ConstructorNode> Constructors { get; }

    /// <summary>
    /// Methods defined in this class.
    /// </summary>
    public IReadOnlyList<MethodNode> Methods { get; }

    /// <summary>
    /// Events defined in this class.
    /// </summary>
    public IReadOnlyList<EventDefinitionNode> Events { get; }

    /// <summary>
    /// Operator overloads defined in this class.
    /// </summary>
    public IReadOnlyList<OperatorOverloadNode> OperatorOverloads { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@Route("api/[controller]")], [@ApiController]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public ClassDefinitionNode(
        TextSpan span,
        string id,
        string name,
        bool isAbstract,
        bool isSealed,
        string? baseClass,
        IReadOnlyList<string> implementedInterfaces,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ClassFieldNode> fields,
        IReadOnlyList<MethodNode> methods,
        AttributeCollection attributes)
        : this(span, id, name, isAbstract, isSealed, isPartial: false, isStatic: false, baseClass, implementedInterfaces,
               typeParameters, fields, Array.Empty<PropertyNode>(), Array.Empty<ConstructorNode>(), methods,
               Array.Empty<EventDefinitionNode>(), Array.Empty<OperatorOverloadNode>(), attributes, Array.Empty<CalorAttributeNode>())
    {
    }

    public ClassDefinitionNode(
        TextSpan span,
        string id,
        string name,
        bool isAbstract,
        bool isSealed,
        string? baseClass,
        IReadOnlyList<string> implementedInterfaces,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ClassFieldNode> fields,
        IReadOnlyList<PropertyNode> properties,
        IReadOnlyList<ConstructorNode> constructors,
        IReadOnlyList<MethodNode> methods,
        AttributeCollection attributes)
        : this(span, id, name, isAbstract, isSealed, isPartial: false, isStatic: false, baseClass, implementedInterfaces,
               typeParameters, fields, properties, constructors, methods,
               Array.Empty<EventDefinitionNode>(), Array.Empty<OperatorOverloadNode>(), attributes, Array.Empty<CalorAttributeNode>())
    {
    }

    public ClassDefinitionNode(
        TextSpan span,
        string id,
        string name,
        bool isAbstract,
        bool isSealed,
        string? baseClass,
        IReadOnlyList<string> implementedInterfaces,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ClassFieldNode> fields,
        IReadOnlyList<PropertyNode> properties,
        IReadOnlyList<ConstructorNode> constructors,
        IReadOnlyList<MethodNode> methods,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, id, name, isAbstract, isSealed, isPartial: false, isStatic: false, baseClass, implementedInterfaces,
               typeParameters, fields, properties, constructors, methods,
               Array.Empty<EventDefinitionNode>(), Array.Empty<OperatorOverloadNode>(), attributes, csharpAttributes)
    {
    }

    public ClassDefinitionNode(
        TextSpan span,
        string id,
        string name,
        bool isAbstract,
        bool isSealed,
        bool isPartial,
        bool isStatic,
        string? baseClass,
        IReadOnlyList<string> implementedInterfaces,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ClassFieldNode> fields,
        IReadOnlyList<PropertyNode> properties,
        IReadOnlyList<ConstructorNode> constructors,
        IReadOnlyList<MethodNode> methods,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, id, name, isAbstract, isSealed, isPartial, isStatic, baseClass, implementedInterfaces,
               typeParameters, fields, properties, constructors, methods,
               Array.Empty<EventDefinitionNode>(), Array.Empty<OperatorOverloadNode>(), attributes, csharpAttributes)
    {
    }

    public ClassDefinitionNode(
        TextSpan span,
        string id,
        string name,
        bool isAbstract,
        bool isSealed,
        bool isPartial,
        bool isStatic,
        string? baseClass,
        IReadOnlyList<string> implementedInterfaces,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ClassFieldNode> fields,
        IReadOnlyList<PropertyNode> properties,
        IReadOnlyList<ConstructorNode> constructors,
        IReadOnlyList<MethodNode> methods,
        IReadOnlyList<EventDefinitionNode> events,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, id, name, isAbstract, isSealed, isPartial, isStatic, baseClass, implementedInterfaces,
               typeParameters, fields, properties, constructors, methods,
               events, Array.Empty<OperatorOverloadNode>(), attributes, csharpAttributes)
    {
    }

    public ClassDefinitionNode(
        TextSpan span,
        string id,
        string name,
        bool isAbstract,
        bool isSealed,
        bool isPartial,
        bool isStatic,
        string? baseClass,
        IReadOnlyList<string> implementedInterfaces,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ClassFieldNode> fields,
        IReadOnlyList<PropertyNode> properties,
        IReadOnlyList<ConstructorNode> constructors,
        IReadOnlyList<MethodNode> methods,
        IReadOnlyList<EventDefinitionNode> events,
        IReadOnlyList<OperatorOverloadNode> operatorOverloads,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes,
        bool isStruct = false,
        bool isReadOnly = false,
        Visibility visibility = Visibility.Internal)
        : base(span, id, name, attributes)
    {
        IsAbstract = isAbstract;
        IsSealed = isSealed;
        IsPartial = isPartial;
        IsStatic = isStatic;
        IsStruct = isStruct;
        IsReadOnly = isReadOnly;
        Visibility = visibility;
        BaseClass = baseClass;
        ImplementedInterfaces = implementedInterfaces ?? throw new ArgumentNullException(nameof(implementedInterfaces));
        TypeParameters = typeParameters ?? throw new ArgumentNullException(nameof(typeParameters));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Constructors = constructors ?? throw new ArgumentNullException(nameof(constructors));
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
        Events = events ?? Array.Empty<EventDefinitionNode>();
        OperatorOverloads = operatorOverloads ?? Array.Empty<OperatorOverloadNode>();
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a field in a class.
/// §FLD[string:Name:pri]
/// </summary>
public sealed class ClassFieldNode : AstNode
{
    public string Name { get; }
    public string TypeName { get; }
    public Visibility Visibility { get; }
    public MethodModifiers Modifiers { get; }
    public bool IsStatic => Modifiers.HasFlag(MethodModifiers.Static);
    public bool IsRequired => Modifiers.HasFlag(MethodModifiers.Required);
    public ExpressionNode? DefaultValue { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@JsonIgnore], [@NonSerialized]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public ClassFieldNode(
        TextSpan span,
        string name,
        string typeName,
        Visibility visibility,
        ExpressionNode? defaultValue,
        AttributeCollection attributes)
        : this(span, name, typeName, visibility, MethodModifiers.None, defaultValue, attributes, Array.Empty<CalorAttributeNode>())
    {
    }

    public ClassFieldNode(
        TextSpan span,
        string name,
        string typeName,
        Visibility visibility,
        ExpressionNode? defaultValue,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, name, typeName, visibility, MethodModifiers.None, defaultValue, attributes, csharpAttributes)
    {
    }

    public ClassFieldNode(
        TextSpan span,
        string name,
        string typeName,
        Visibility visibility,
        MethodModifiers modifiers,
        ExpressionNode? defaultValue,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Visibility = visibility;
        Modifiers = modifiers;
        DefaultValue = defaultValue;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a method in a class (like FunctionNode but with OOP modifiers).
/// §METHOD[m001:Area:pub:over]
///   §O[f64] §E[]
///   §R §OP[kind=mul] 3.14159 §OP[kind=mul] §REF[name=Radius] §REF[name=Radius]
/// §/METHOD[m001]
/// </summary>
public sealed class MethodNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public Visibility Visibility { get; }
    public MethodModifiers Modifiers { get; }
    public IReadOnlyList<TypeParameterNode> TypeParameters { get; }
    public IReadOnlyList<ParameterNode> Parameters { get; }
    public OutputNode? Output { get; }
    public EffectsNode? Effects { get; }
    public IReadOnlyList<RequiresNode> Preconditions { get; }
    public IReadOnlyList<EnsuresNode> Postconditions { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@HttpPost], [@Authorize], [@Route("api/users")]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    /// <summary>
    /// True if this is an async method.
    /// </summary>
    public bool IsAsync { get; }

    public MethodNode(
        TextSpan span,
        string id,
        string name,
        Visibility visibility,
        MethodModifiers modifiers,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : this(span, id, name, visibility, modifiers, typeParameters, parameters, output, effects,
               preconditions, postconditions, body, attributes, Array.Empty<CalorAttributeNode>(), false)
    {
    }

    public MethodNode(
        TextSpan span,
        string id,
        string name,
        Visibility visibility,
        MethodModifiers modifiers,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes,
        bool isAsync = false)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Visibility = visibility;
        Modifiers = modifiers;
        TypeParameters = typeParameters ?? throw new ArgumentNullException(nameof(typeParameters));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Output = output;
        Effects = effects;
        Preconditions = preconditions ?? throw new ArgumentNullException(nameof(preconditions));
        Postconditions = postconditions ?? throw new ArgumentNullException(nameof(postconditions));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
        IsAsync = isAsync;
    }

    public bool IsVirtual => (Modifiers & MethodModifiers.Virtual) != 0;
    public bool IsOverride => (Modifiers & MethodModifiers.Override) != 0;
    public bool IsAbstract => (Modifiers & MethodModifiers.Abstract) != 0;
    public bool IsSealed => (Modifiers & MethodModifiers.Sealed) != 0;
    public bool IsStatic => (Modifiers & MethodModifiers.Static) != 0;
    public bool IsPartial => (Modifiers & MethodModifiers.Partial) != 0;
    public bool HasContracts => Preconditions.Count > 0 || Postconditions.Count > 0;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// The kind of operator overload.
/// </summary>
public enum OperatorOverloadKind
{
    // Binary operators
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Equality,
    Inequality,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    LeftShift,
    RightShift,
    // Unary operators
    UnaryPlus,
    UnaryNegate,
    LogicalNot,
    BitwiseNot,
    Increment,
    Decrement,
    True,
    False,
    // Conversion operators
    Implicit,
    Explicit
}

/// <summary>
/// Represents an operator overload declaration.
/// §OP{id:operator:visibility}
///   §I{MyType:left}
///   §I{MyType:right}
///   §O{MyType}
///   §Q (>= (. left Value) 0)
///   §S (>= (. result Value) 0)
///   §R §NEW{MyType}((+ (. left Value) (. right Value)))§/NEW
/// §/OP{id}
/// </summary>
public sealed class OperatorOverloadNode : AstNode
{
    public string Id { get; }
    public string OperatorToken { get; }
    public OperatorOverloadKind Kind { get; }
    public Visibility Visibility { get; }
    public IReadOnlyList<ParameterNode> Parameters { get; }
    public OutputNode? Output { get; }
    public IReadOnlyList<RequiresNode> Preconditions { get; }
    public IReadOnlyList<EnsuresNode> Postconditions { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public AttributeCollection Attributes { get; }
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    public bool IsConversion => Kind == OperatorOverloadKind.Implicit || Kind == OperatorOverloadKind.Explicit;
    public bool IsUnary => Kind is OperatorOverloadKind.UnaryPlus or OperatorOverloadKind.UnaryNegate
        or OperatorOverloadKind.LogicalNot or OperatorOverloadKind.BitwiseNot
        or OperatorOverloadKind.Increment or OperatorOverloadKind.Decrement
        or OperatorOverloadKind.True or OperatorOverloadKind.False;
    public bool IsBinary => !IsConversion && !IsUnary;

    public OperatorOverloadNode(
        TextSpan span,
        string id,
        string operatorToken,
        OperatorOverloadKind kind,
        Visibility visibility,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : this(span, id, operatorToken, kind, visibility, parameters, output, preconditions, postconditions, body, attributes, Array.Empty<CalorAttributeNode>())
    {
    }

    public OperatorOverloadNode(
        TextSpan span,
        string id,
        string operatorToken,
        OperatorOverloadKind kind,
        Visibility visibility,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        OperatorToken = operatorToken ?? throw new ArgumentNullException(nameof(operatorToken));
        Kind = kind;
        Visibility = visibility;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Output = output;
        Preconditions = preconditions ?? Array.Empty<RequiresNode>();
        Postconditions = postconditions ?? Array.Empty<EnsuresNode>();
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
    }

    /// <summary>
    /// Resolves the operator kind from a token string and parameter count.
    /// Ambiguous operators like + and - are resolved based on parameter count.
    /// </summary>
    public static OperatorOverloadKind ResolveOperatorKind(string token, int paramCount)
    {
        return token switch
        {
            "+" when paramCount == 1 => OperatorOverloadKind.UnaryPlus,
            "+" => OperatorOverloadKind.Add,
            "-" when paramCount == 1 => OperatorOverloadKind.UnaryNegate,
            "-" => OperatorOverloadKind.Subtract,
            "*" => OperatorOverloadKind.Multiply,
            "/" => OperatorOverloadKind.Divide,
            "%" => OperatorOverloadKind.Modulo,
            "==" => OperatorOverloadKind.Equality,
            "!=" => OperatorOverloadKind.Inequality,
            "<" => OperatorOverloadKind.LessThan,
            ">" => OperatorOverloadKind.GreaterThan,
            "<=" => OperatorOverloadKind.LessThanOrEqual,
            ">=" => OperatorOverloadKind.GreaterThanOrEqual,
            "&" => OperatorOverloadKind.BitwiseAnd,
            "|" => OperatorOverloadKind.BitwiseOr,
            "^" => OperatorOverloadKind.BitwiseXor,
            "<<" => OperatorOverloadKind.LeftShift,
            ">>" => OperatorOverloadKind.RightShift,
            "!" => OperatorOverloadKind.LogicalNot,
            "~" => OperatorOverloadKind.BitwiseNot,
            "++" => OperatorOverloadKind.Increment,
            "--" => OperatorOverloadKind.Decrement,
            "true" => OperatorOverloadKind.True,
            "false" => OperatorOverloadKind.False,
            "implicit" => OperatorOverloadKind.Implicit,
            "explicit" => OperatorOverloadKind.Explicit,
            _ => throw new ArgumentException($"Unknown operator token: {token}", nameof(token))
        };
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a 'new' expression.
/// §NEW[Circle] §A "MyCircle" §A 5.0 §/NEW
/// Or with object initializer: §NEW[Person] { Name: "John", Age: 30 }
/// </summary>
public sealed class NewExpressionNode : ExpressionNode
{
    public string TypeName { get; }
    public IReadOnlyList<string> TypeArguments { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }

    /// <summary>
    /// Object initializer property assignments (e.g., new Person { Name = "John", Age = 30 }).
    /// </summary>
    public IReadOnlyList<ObjectInitializerAssignment> Initializers { get; }

    public NewExpressionNode(
        TextSpan span,
        string typeName,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<ExpressionNode> arguments)
        : this(span, typeName, typeArguments, arguments, Array.Empty<ObjectInitializerAssignment>())
    {
    }

    public NewExpressionNode(
        TextSpan span,
        string typeName,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        IReadOnlyList<ObjectInitializerAssignment> initializers)
        : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        TypeArguments = typeArguments ?? throw new ArgumentNullException(nameof(typeArguments));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        Initializers = initializers ?? Array.Empty<ObjectInitializerAssignment>();
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a property assignment in an object initializer.
/// </summary>
public sealed class ObjectInitializerAssignment
{
    public string PropertyName { get; }
    public ExpressionNode Value { get; }

    public ObjectInitializerAssignment(string propertyName, ExpressionNode value)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// Represents an anonymous object creation expression.
/// §ANON PropertyName = value ... §/ANON
/// Emits: new { PropertyName = value, ... }
/// </summary>
public sealed class AnonymousObjectCreationNode : ExpressionNode
{
    public IReadOnlyList<ObjectInitializerAssignment> Initializers { get; }

    public AnonymousObjectCreationNode(TextSpan span, IReadOnlyList<ObjectInitializerAssignment> initializers)
        : base(span)
    {
        Initializers = initializers ?? throw new ArgumentNullException(nameof(initializers));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a method/function call expression.
/// §C[target] §A arg1 §A arg2 §/C
/// </summary>
public sealed class CallExpressionNode : ExpressionNode
{
    public string Target { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }

    /// <summary>
    /// Optional named argument labels, parallel to Arguments list.
    /// Null entry means positional; non-null means named (e.g., "createIfNotExists").
    /// </summary>
    public IReadOnlyList<string?>? ArgumentNames { get; }

    public CallExpressionNode(TextSpan span, string target, IReadOnlyList<ExpressionNode> arguments)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }

    public CallExpressionNode(TextSpan span, string target, IReadOnlyList<ExpressionNode> arguments, IReadOnlyList<string?>? argumentNames)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        ArgumentNames = argumentNames;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a 'this' expression.
/// §THIS
/// </summary>
public sealed class ThisExpressionNode : ExpressionNode
{
    public ThisExpressionNode(TextSpan span) : base(span) { }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a 'base' expression for calling base class members.
/// §BASE
/// </summary>
public sealed class BaseExpressionNode : ExpressionNode
{
    public BaseExpressionNode(TextSpan span) : base(span) { }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a tuple literal expression.
/// (expr1, expr2, ...) → C# (expr1, expr2, ...)
/// </summary>
public sealed class TupleLiteralNode : ExpressionNode
{
    public IReadOnlyList<ExpressionNode> Elements { get; }

    public TupleLiteralNode(TextSpan span, IReadOnlyList<ExpressionNode> elements)
        : base(span)
    {
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
