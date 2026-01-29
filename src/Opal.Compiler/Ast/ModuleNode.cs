using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Represents an OPAL module declaration.
/// §MODULE[id=xxx][name=xxx]
/// </summary>
public sealed class ModuleNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<UsingDirectiveNode> Usings { get; }
    public IReadOnlyList<InterfaceDefinitionNode> Interfaces { get; }
    public IReadOnlyList<ClassDefinitionNode> Classes { get; }
    public IReadOnlyList<FunctionNode> Functions { get; }
    public AttributeCollection Attributes { get; }

    // Extended Features: Structured Issues
    public IReadOnlyList<IssueNode> Issues { get; }
    // Extended Features: Assumptions
    public IReadOnlyList<AssumeNode> Assumptions { get; }
    // Extended Features: Invariants
    public IReadOnlyList<InvariantNode> Invariants { get; }
    // Extended Features: Decision Records
    public IReadOnlyList<DecisionNode> Decisions { get; }
    // Extended Features: Partial View Markers
    public ContextNode? Context { get; }

    public ModuleNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<UsingDirectiveNode> usings,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes)
        : this(span, id, name, usings, Array.Empty<InterfaceDefinitionNode>(),
               Array.Empty<ClassDefinitionNode>(), functions, attributes,
               Array.Empty<IssueNode>(), Array.Empty<AssumeNode>(),
               Array.Empty<InvariantNode>(), Array.Empty<DecisionNode>(), null)
    {
    }

    public ModuleNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<UsingDirectiveNode> usings,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<ClassDefinitionNode> classes,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes)
        : this(span, id, name, usings, interfaces, classes, functions, attributes,
               Array.Empty<IssueNode>(), Array.Empty<AssumeNode>(),
               Array.Empty<InvariantNode>(), Array.Empty<DecisionNode>(), null)
    {
    }

    public ModuleNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<UsingDirectiveNode> usings,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<ClassDefinitionNode> classes,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes,
        IReadOnlyList<IssueNode> issues,
        IReadOnlyList<AssumeNode> assumptions,
        IReadOnlyList<InvariantNode> invariants,
        IReadOnlyList<DecisionNode> decisions,
        ContextNode? context)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Usings = usings ?? throw new ArgumentNullException(nameof(usings));
        Interfaces = interfaces ?? throw new ArgumentNullException(nameof(interfaces));
        Classes = classes ?? throw new ArgumentNullException(nameof(classes));
        Functions = functions ?? throw new ArgumentNullException(nameof(functions));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
        Assumptions = assumptions ?? throw new ArgumentNullException(nameof(assumptions));
        Invariants = invariants ?? throw new ArgumentNullException(nameof(invariants));
        Decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        Context = context;
    }

    /// <summary>
    /// Returns true if this module has extended metadata (issues, assumptions, etc.).
    /// </summary>
    public bool HasExtendedMetadata => Issues.Count > 0 || Assumptions.Count > 0 ||
        Invariants.Count > 0 || Decisions.Count > 0 || Context != null;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
