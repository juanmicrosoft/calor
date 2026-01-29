using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Visibility levels for functions.
/// </summary>
public enum Visibility
{
    Private,
    Protected,
    Internal,
    Public
}

/// <summary>
/// Represents the output (return) type of a function.
/// </summary>
public sealed class OutputNode : AstNode
{
    public string TypeName { get; }

    public OutputNode(TextSpan span, string typeName) : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents the effects declaration of a function.
/// </summary>
public sealed class EffectsNode : AstNode
{
    public IReadOnlyDictionary<string, string> Effects { get; }

    public EffectsNode(TextSpan span, IReadOnlyDictionary<string, string> effects) : base(span)
    {
        Effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

/// <summary>
/// Represents an OPAL function declaration.
/// §FUNC[id=xxx][name=xxx][visibility=xxx]
/// </summary>
public sealed class FunctionNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public Visibility Visibility { get; }
    public IReadOnlyList<TypeParameterNode> TypeParameters { get; }
    public IReadOnlyList<ParameterNode> Parameters { get; }
    public OutputNode? Output { get; }
    public EffectsNode? Effects { get; }
    public IReadOnlyList<RequiresNode> Preconditions { get; }
    public IReadOnlyList<EnsuresNode> Postconditions { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public AttributeCollection Attributes { get; }

    public FunctionNode(
        TextSpan span,
        string id,
        string name,
        Visibility visibility,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : this(span, id, name, visibility, Array.Empty<TypeParameterNode>(), parameters, output, effects,
               Array.Empty<RequiresNode>(), Array.Empty<EnsuresNode>(), body, attributes)
    {
    }

    public FunctionNode(
        TextSpan span,
        string id,
        string name,
        Visibility visibility,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : this(span, id, name, visibility, Array.Empty<TypeParameterNode>(), parameters, output, effects,
               preconditions, postconditions, body, attributes)
    {
    }

    public FunctionNode(
        TextSpan span,
        string id,
        string name,
        Visibility visibility,
        IReadOnlyList<TypeParameterNode> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Visibility = visibility;
        TypeParameters = typeParameters ?? throw new ArgumentNullException(nameof(typeParameters));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Output = output;
        Effects = effects;
        Preconditions = preconditions ?? throw new ArgumentNullException(nameof(preconditions));
        Postconditions = postconditions ?? throw new ArgumentNullException(nameof(postconditions));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    /// <summary>
    /// Returns true if this function has any contracts (preconditions or postconditions).
    /// </summary>
    public bool HasContracts => Preconditions.Count > 0 || Postconditions.Count > 0;

    /// <summary>
    /// Returns true if this function is generic (has type parameters).
    /// </summary>
    public bool IsGeneric => TypeParameters.Count > 0;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a function parameter.
/// §IN[name=xxx][type=xxx]
/// </summary>
public sealed class ParameterNode : AstNode
{
    public string Name { get; }
    public string TypeName { get; }
    public AttributeCollection Attributes { get; }

    public ParameterNode(
        TextSpan span,
        string name,
        string typeName,
        AttributeCollection attributes)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
