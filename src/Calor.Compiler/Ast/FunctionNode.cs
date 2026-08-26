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
    Public,
    PrivateProtected
}

/// <summary>
/// Represents the output (return) type of a function.
/// </summary>
public sealed class OutputNode : AstNode
{
    public string TypeName { get; }
    public TextSpan TypeNameSpan { get; }

    /// <summary>
    /// Optional effect row annotating the return type, written same-line-adjacent to
    /// it: <c>§O{Func&lt;i32&gt;} §E{cw}</c> or <c>-&gt; Func&lt;i32&gt; §E{cw}</c>.
    /// Position 6 of docs/design/effect-rows-in-the-type-system.md §3.3.
    /// A <c>§E</c> on a later line is the declaration's own row, not this.
    /// </summary>
    public EffectsNode? Row { get; }

    public OutputNode(TextSpan span, string typeName, TextSpan? typeNameSpan = null, EffectsNode? row = null) : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        TypeNameSpan = typeNameSpan ?? TextSpan.Empty;
        Row = row;
    }


}

/// <summary>
/// Represents the effects declaration of a function.
/// </summary>
public sealed class EffectsNode : AstNode
{
    public IReadOnlyDictionary<string, string> Effects { get; }

    /// <summary>
    /// Bare-identifier effect variables written inside this row — <c>§E{cw, e}</c>
    /// carries concrete code <c>cw</c> in <see cref="Effects"/> and variable
    /// <c>e</c> here. A name lands here only when an enclosing declaration binds it
    /// with an <c>eff</c> modifier; an unbound name stays an unknown effect code.
    /// See docs/design/effect-rows-in-the-type-system.md §7.2.
    /// </summary>
    public IReadOnlyList<string> EffectVariables { get; }

    public EffectsNode(
        TextSpan span,
        IReadOnlyDictionary<string, string> effects,
        IReadOnlyList<string>? effectVariables = null) : base(span)
    {
        Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        EffectVariables = effectVariables ?? Array.Empty<string>();
    }


}

/// <summary>
/// Represents an Calor function declaration.
/// §FUNC[id=xxx][name=xxx][visibility=xxx]
/// </summary>
public sealed class FunctionNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public TextSpan IdentifierSpan { get; }
    public Visibility Visibility { get; }
    public IReadOnlyList<TypeParameterNode> TypeParameters { get; }

    /// <summary>
    /// Effect variables this declaration binds with an <c>eff</c> modifier in its
    /// type-parameter list. Kept separate from <see cref="TypeParameters"/> so the
    /// binders are erased at codegen by construction — see
    /// <see cref="EffectParameterInfo"/> and design-doc §7.2.
    /// </summary>
    public IReadOnlyList<EffectParameterInfo> EffectParameters { get; init; }
        = Array.Empty<EffectParameterInfo>();
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
        bool isAsync = false,
        TextSpan? identifierSpan = null)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IdentifierSpan = identifierSpan ?? span;
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
    public TextSpan IdentifierSpan { get; }
    public string TypeName { get; }
    public TextSpan TypeNameSpan { get; }
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

    /// <summary>
    /// Optional inline refinement constraint on the parameter type.
    /// Parsed from §I{baseType:name | (predicate using #)}.
    /// </summary>
    public InlineRefinementInfo? InlineRefinement { get; }

    /// <summary>
    /// Optional effect row annotating the parameter type, written same-line-adjacent
    /// to it: <c>§I{Func&lt;i32,i32&gt;:f} §E{cw}</c> (tag form, position 4) or
    /// <c>(Func&lt;i32,i32&gt;:f §E{cw}, i32:v)</c> (inline form, position 5).
    /// See docs/design/effect-rows-in-the-type-system.md §3.3.
    /// </summary>
    public EffectsNode? Row { get; }

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
        ExpressionNode? defaultValue,
        InlineRefinementInfo? inlineRefinement = null,
        TextSpan? identifierSpan = null,
        TextSpan? typeNameSpan = null,
        EffectsNode? row = null)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IdentifierSpan = identifierSpan ?? span;
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        TypeNameSpan = typeNameSpan ?? TextSpan.Empty;
        Modifier = modifier;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        CSharpAttributes = csharpAttributes ?? Array.Empty<CalorAttributeNode>();
        DefaultValue = defaultValue;
        InlineRefinement = inlineRefinement;
        Row = row;
    }


}
