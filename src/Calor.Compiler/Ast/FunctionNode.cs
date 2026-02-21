using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Visibility levels for functions.
/// </summary>
public enum Visibility
{
    Private,
    Protected,
    Internal,
    ProtectedInternal,
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
/// Represents an Calor function declaration.
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

    /// <summary>
    /// True if this is an async function.
    /// </summary>
    public bool IsAsync { get; }

    // Extended Features: Inline Examples/Tests
    public IReadOnlyList<ExampleNode> Examples { get; }
    // Extended Features: Structured Issues
    public IReadOnlyList<IssueNode> Issues { get; }
    // Extended Features: Dependencies
    public UsesNode? Uses { get; }
    public UsedByNode? UsedBy { get; }
    // Extended Features: Assumptions
    public IReadOnlyList<AssumeNode> Assumptions { get; }
    // Extended Features: Complexity
    public ComplexityNode? Complexity { get; }
    // Extended Features: Versioning
    public SinceNode? Since { get; }
    public DeprecatedNode? Deprecated { get; }
    public IReadOnlyList<BreakingChangeNode> BreakingChanges { get; }
    // Extended Features: Property-based Testing
    public IReadOnlyList<PropertyTestNode> Properties { get; }
    // Extended Features: Multi-agent Collaboration
    public LockNode? Lock { get; }
    public AuthorNode? Author { get; }
    public TaskRefNode? TaskRef { get; }

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
               Array.Empty<RequiresNode>(), Array.Empty<EnsuresNode>(), body, attributes,
               Array.Empty<ExampleNode>(), Array.Empty<IssueNode>(), null, null,
               Array.Empty<AssumeNode>(), null, null, null, Array.Empty<BreakingChangeNode>(),
               Array.Empty<PropertyTestNode>(), null, null, null)
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
               preconditions, postconditions, body, attributes,
               Array.Empty<ExampleNode>(), Array.Empty<IssueNode>(), null, null,
               Array.Empty<AssumeNode>(), null, null, null, Array.Empty<BreakingChangeNode>(),
               Array.Empty<PropertyTestNode>(), null, null, null)
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
        : this(span, id, name, visibility, typeParameters, parameters, output, effects,
               preconditions, postconditions, body, attributes,
               Array.Empty<ExampleNode>(), Array.Empty<IssueNode>(), null, null,
               Array.Empty<AssumeNode>(), null, null, null, Array.Empty<BreakingChangeNode>(),
               Array.Empty<PropertyTestNode>(), null, null, null)
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
        AttributeCollection attributes,
        IReadOnlyList<ExampleNode> examples,
        IReadOnlyList<IssueNode> issues,
        UsesNode? uses,
        UsedByNode? usedBy,
        IReadOnlyList<AssumeNode> assumptions,
        ComplexityNode? complexity,
        SinceNode? since,
        DeprecatedNode? deprecated,
        IReadOnlyList<BreakingChangeNode> breakingChanges,
        IReadOnlyList<PropertyTestNode> properties,
        LockNode? lockNode,
        AuthorNode? author,
        TaskRefNode? taskRef,
        bool isAsync = false)
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
        Examples = examples ?? throw new ArgumentNullException(nameof(examples));
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
        Uses = uses;
        UsedBy = usedBy;
        Assumptions = assumptions ?? throw new ArgumentNullException(nameof(assumptions));
        Complexity = complexity;
        Since = since;
        Deprecated = deprecated;
        BreakingChanges = breakingChanges ?? throw new ArgumentNullException(nameof(breakingChanges));
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Lock = lockNode;
        Author = author;
        TaskRef = taskRef;
        IsAsync = isAsync;
    }

    /// <summary>
    /// Returns true if this function has any contracts (preconditions or postconditions).
    /// </summary>
    public bool HasContracts => Preconditions.Count > 0 || Postconditions.Count > 0;

    /// <summary>
    /// Returns true if this function is generic (has type parameters).
    /// </summary>
    public bool IsGeneric => TypeParameters.Count > 0;

    /// <summary>
    /// Returns true if this function has extended metadata (examples, issues, dependencies, etc.).
    /// </summary>
    public bool HasExtendedMetadata => Examples.Count > 0 || Issues.Count > 0 || Uses != null ||
        UsedBy != null || Assumptions.Count > 0 || Complexity != null || Since != null ||
        Deprecated != null || BreakingChanges.Count > 0 || Properties.Count > 0 ||
        Lock != null || Author != null || TaskRef != null;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Parameter modifiers for method parameters.
/// </summary>
[Flags]
public enum ParameterModifier
{
    None = 0,
    This = 1,
    Ref = 2,
    Out = 4,
    In = 8,
    Params = 16,
}

/// <summary>
/// Represents a function parameter.
/// §IN[name=xxx][type=xxx]
/// </summary>
public sealed class ParameterNode : AstNode
{
    public string Name { get; }
    public string TypeName { get; }
    public ParameterModifier Modifier { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// C#-style attributes (e.g., [@FromBody], [@Required]).
    /// </summary>
    public IReadOnlyList<CalorAttributeNode> CSharpAttributes { get; }

    /// <summary>
    /// Optional default value for the parameter (from C# = value syntax).
    /// </summary>
    public ExpressionNode? DefaultValue { get; }

    public ParameterNode(
        TextSpan span,
        string name,
        string typeName,
        AttributeCollection attributes)
        : this(span, name, typeName, ParameterModifier.None, attributes, Array.Empty<CalorAttributeNode>(), null)
    {
    }

    public ParameterNode(
        TextSpan span,
        string name,
        string typeName,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, name, typeName, ParameterModifier.None, attributes, csharpAttributes, null)
    {
    }

    public ParameterNode(
        TextSpan span,
        string name,
        string typeName,
        ParameterModifier modifier,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes)
        : this(span, name, typeName, modifier, attributes, csharpAttributes, null)
    {
    }

    public ParameterNode(
        TextSpan span,
        string name,
        string typeName,
        ParameterModifier modifier,
        AttributeCollection attributes,
        IReadOnlyList<CalorAttributeNode> csharpAttributes,
        ExpressionNode? defaultValue)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Modifier = modifier;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
        DefaultValue = defaultValue;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
