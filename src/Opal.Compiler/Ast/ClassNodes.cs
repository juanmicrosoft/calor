using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

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
    Static = 16
}

/// <summary>
/// Represents an interface definition.
/// §IFACE[i001:IShape]
///   §METHOD[m001:Area] §O[f64] §E[] §/METHOD[m001]
/// §/IFACE[i001]
/// </summary>
public sealed class InterfaceDefinitionNode : TypeDefinitionNode
{
    /// <summary>
    /// Interface methods (signatures only).
    /// </summary>
    public IReadOnlyList<MethodSignatureNode> Methods { get; }

    /// <summary>
    /// Interfaces this interface extends.
    /// </summary>
    public IReadOnlyList<string> BaseInterfaces { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@Obsolete], [@ComVisible]).
    /// </summary>
    public IReadOnlyList<OpalAttributeNode> CSharpAttributes { get; }

    public InterfaceDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<string> baseInterfaces,
        IReadOnlyList<MethodSignatureNode> methods,
        AttributeCollection attributes)
        : this(span, id, name, baseInterfaces, methods, attributes, Array.Empty<OpalAttributeNode>())
    {
    }

    public InterfaceDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<string> baseInterfaces,
        IReadOnlyList<MethodSignatureNode> methods,
        AttributeCollection attributes,
        IReadOnlyList<OpalAttributeNode> csharpAttributes)
        : base(span, id, name, attributes)
    {
        BaseInterfaces = baseInterfaces ?? throw new ArgumentNullException(nameof(baseInterfaces));
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
        CSharpAttributes = csharpAttributes ?? Array.Empty<OpalAttributeNode>();
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
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@Obsolete]).
    /// </summary>
    public IReadOnlyList<OpalAttributeNode> CSharpAttributes { get; }

    public MethodSignatureNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        AttributeCollection attributes)
        : this(span, id, name, typeParameters, parameters, output, effects, attributes, Array.Empty<OpalAttributeNode>())
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
        IReadOnlyList<OpalAttributeNode> csharpAttributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeParameters = typeParameters ?? throw new ArgumentNullException(nameof(typeParameters));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Output = output;
        Effects = effects;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<OpalAttributeNode>();
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
    /// C#-style attributes (e.g., [@Route("api/[controller]")], [@ApiController]).
    /// </summary>
    public IReadOnlyList<OpalAttributeNode> CSharpAttributes { get; }

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
        : this(span, id, name, isAbstract, isSealed, baseClass, implementedInterfaces,
               typeParameters, fields, Array.Empty<PropertyNode>(), Array.Empty<ConstructorNode>(), methods, attributes, Array.Empty<OpalAttributeNode>())
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
        : this(span, id, name, isAbstract, isSealed, baseClass, implementedInterfaces,
               typeParameters, fields, properties, constructors, methods, attributes, Array.Empty<OpalAttributeNode>())
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
        IReadOnlyList<OpalAttributeNode> csharpAttributes)
        : base(span, id, name, attributes)
    {
        IsAbstract = isAbstract;
        IsSealed = isSealed;
        BaseClass = baseClass;
        ImplementedInterfaces = implementedInterfaces ?? throw new ArgumentNullException(nameof(implementedInterfaces));
        TypeParameters = typeParameters ?? throw new ArgumentNullException(nameof(typeParameters));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Constructors = constructors ?? throw new ArgumentNullException(nameof(constructors));
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
        CSharpAttributes = csharpAttributes ?? Array.Empty<OpalAttributeNode>();
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
    public ExpressionNode? DefaultValue { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@JsonIgnore], [@NonSerialized]).
    /// </summary>
    public IReadOnlyList<OpalAttributeNode> CSharpAttributes { get; }

    public ClassFieldNode(
        TextSpan span,
        string name,
        string typeName,
        Visibility visibility,
        ExpressionNode? defaultValue,
        AttributeCollection attributes)
        : this(span, name, typeName, visibility, defaultValue, attributes, Array.Empty<OpalAttributeNode>())
    {
    }

    public ClassFieldNode(
        TextSpan span,
        string name,
        string typeName,
        Visibility visibility,
        ExpressionNode? defaultValue,
        AttributeCollection attributes,
        IReadOnlyList<OpalAttributeNode> csharpAttributes)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Visibility = visibility;
        DefaultValue = defaultValue;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<OpalAttributeNode>();
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
    public IReadOnlyList<OpalAttributeNode> CSharpAttributes { get; }

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
               preconditions, postconditions, body, attributes, Array.Empty<OpalAttributeNode>())
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
        IReadOnlyList<OpalAttributeNode> csharpAttributes)
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
        CSharpAttributes = csharpAttributes ?? Array.Empty<OpalAttributeNode>();
    }

    public bool IsVirtual => (Modifiers & MethodModifiers.Virtual) != 0;
    public bool IsOverride => (Modifiers & MethodModifiers.Override) != 0;
    public bool IsAbstract => (Modifiers & MethodModifiers.Abstract) != 0;
    public bool IsSealed => (Modifiers & MethodModifiers.Sealed) != 0;
    public bool IsStatic => (Modifiers & MethodModifiers.Static) != 0;
    public bool HasContracts => Preconditions.Count > 0 || Postconditions.Count > 0;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a 'new' expression.
/// §NEW[Circle] §A "MyCircle" §A 5.0 §/NEW
/// </summary>
public sealed class NewExpressionNode : ExpressionNode
{
    public string TypeName { get; }
    public IReadOnlyList<string> TypeArguments { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }

    public NewExpressionNode(
        TextSpan span,
        string typeName,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<ExpressionNode> arguments)
        : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        TypeArguments = typeArguments ?? throw new ArgumentNullException(nameof(typeArguments));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
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

    public CallExpressionNode(TextSpan span, string target, IReadOnlyList<ExpressionNode> arguments)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
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
