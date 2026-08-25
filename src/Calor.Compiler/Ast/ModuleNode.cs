using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

public enum NamespaceScopeKind
{
    Named,
    Global
}

/// <summary>
/// A lexical C# namespace declaration preserved during C# → Calor migration.
/// Multiple declarations may have the same <see cref="FullName"/> while keeping
/// distinct using scopes.
/// </summary>
public sealed record NamespaceScopeInfo(
    string Id,
    string Name,
    string FullName,
    string? ParentScopeId,
    bool IsFileScoped,
    TextSpan Span,
    NamespaceScopeKind Kind = NamespaceScopeKind.Named)
{
    public bool IsGlobal => Kind == NamespaceScopeKind.Global;
}

/// <summary>
/// Represents an Calor module declaration.
/// §MODULE[id=xxx][name=xxx]
/// </summary>
public sealed class ModuleNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public TextSpan IdentifierSpan { get; }
    public IReadOnlyList<UsingDirectiveNode> Usings { get; }
    public IReadOnlyList<InterfaceDefinitionNode> Interfaces { get; }
    public IReadOnlyList<ClassDefinitionNode> Classes { get; }
    public IReadOnlyList<EnumDefinitionNode> Enums { get; }
    public IReadOnlyList<EnumExtensionNode> EnumExtensions { get; }
    public IReadOnlyList<DelegateDefinitionNode> Delegates { get; }
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
    // C# Interop Blocks (member-level raw C# preserved during partial conversion)
    public IReadOnlyList<CSharpInteropBlockNode> InteropBlocks { get; }
    // Preprocessor conditional blocks wrapping entire type declarations
    public IReadOnlyList<TypePreprocessorBlockNode> TypePreprocessorBlocks { get; }
    // Source-ordered module items used when directives must retain lexical placement.
    public IReadOnlyList<AstNode> Items { get; }
    // Dependent Types: Refinement type definitions at module level
    public IReadOnlyList<RefinementTypeNode> RefinementTypes { get; }
    // Dependent Types: Indexed type definitions at module level
    public IReadOnlyList<IndexedTypeNode> IndexedTypes { get; }
    // Lexical C# namespace declarations preserved during migration.
    public IReadOnlyList<NamespaceScopeInfo> NamespaceScopes { get; }
    // §SEMVER{MAJOR.MINOR.PATCH} directive text as written, or null when the
    // module declares nothing (and therefore takes the compiler's own version).
    // Compatibility is checked at parse time (Calor0700/0701/0702); the value
    // is kept on the node so Calor→Calor emission round-trips it.
    public string? DeclaredSemanticsVersion { get; }

    public ModuleNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<UsingDirectiveNode> usings,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes)
        : this(span, id, name, usings, Array.Empty<InterfaceDefinitionNode>(),
               Array.Empty<ClassDefinitionNode>(), Array.Empty<EnumDefinitionNode>(),
               Array.Empty<EnumExtensionNode>(), Array.Empty<DelegateDefinitionNode>(),
               functions, attributes,
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
        : this(span, id, name, usings, interfaces, classes, Array.Empty<EnumDefinitionNode>(),
               Array.Empty<EnumExtensionNode>(), Array.Empty<DelegateDefinitionNode>(),
               functions, attributes,
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
        IReadOnlyList<EnumDefinitionNode> enums,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes)
        : this(span, id, name, usings, interfaces, classes, enums,
               Array.Empty<EnumExtensionNode>(), Array.Empty<DelegateDefinitionNode>(),
               functions, attributes,
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
        : this(span, id, name, usings, interfaces, classes, Array.Empty<EnumDefinitionNode>(),
               Array.Empty<EnumExtensionNode>(), Array.Empty<DelegateDefinitionNode>(),
               functions, attributes, issues, assumptions, invariants, decisions, context)
    {
    }

    public ModuleNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<UsingDirectiveNode> usings,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<ClassDefinitionNode> classes,
        IReadOnlyList<EnumDefinitionNode> enums,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes,
        IReadOnlyList<IssueNode> issues,
        IReadOnlyList<AssumeNode> assumptions,
        IReadOnlyList<InvariantNode> invariants,
        IReadOnlyList<DecisionNode> decisions,
        ContextNode? context)
        : this(span, id, name, usings, interfaces, classes, enums,
               Array.Empty<EnumExtensionNode>(), Array.Empty<DelegateDefinitionNode>(),
               functions, attributes,
               issues, assumptions, invariants, decisions, context)
    {
    }

    public ModuleNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<UsingDirectiveNode> usings,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<ClassDefinitionNode> classes,
        IReadOnlyList<EnumDefinitionNode> enums,
        IReadOnlyList<DelegateDefinitionNode> delegates,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes,
        IReadOnlyList<IssueNode> issues,
        IReadOnlyList<AssumeNode> assumptions,
        IReadOnlyList<InvariantNode> invariants,
        IReadOnlyList<DecisionNode> decisions,
        ContextNode? context)
        : this(span, id, name, usings, interfaces, classes, enums,
               Array.Empty<EnumExtensionNode>(), delegates,
               functions, attributes,
               issues, assumptions, invariants, decisions, context)
    {
    }

    public ModuleNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<UsingDirectiveNode> usings,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<ClassDefinitionNode> classes,
        IReadOnlyList<EnumDefinitionNode> enums,
        IReadOnlyList<EnumExtensionNode> enumExtensions,
        IReadOnlyList<DelegateDefinitionNode> delegates,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes,
        IReadOnlyList<IssueNode> issues,
        IReadOnlyList<AssumeNode> assumptions,
        IReadOnlyList<InvariantNode> invariants,
        IReadOnlyList<DecisionNode> decisions,
        ContextNode? context,
        IReadOnlyList<CSharpInteropBlockNode>? interopBlocks = null,
        IReadOnlyList<RefinementTypeNode>? refinementTypes = null,
        IReadOnlyList<IndexedTypeNode>? indexedTypes = null,
        IReadOnlyList<TypePreprocessorBlockNode>? typePreprocessorBlocks = null,
        TextSpan? identifierSpan = null,
        IReadOnlyList<AstNode>? items = null,
        IReadOnlyList<NamespaceScopeInfo>? namespaceScopes = null,
        string? declaredSemanticsVersion = null)
        : base(span)
    {
        DeclaredSemanticsVersion = declaredSemanticsVersion;
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IdentifierSpan = identifierSpan ?? span;
        Usings = usings ?? throw new ArgumentNullException(nameof(usings));
        Interfaces = interfaces ?? throw new ArgumentNullException(nameof(interfaces));
        Classes = classes ?? throw new ArgumentNullException(nameof(classes));
        Enums = enums ?? throw new ArgumentNullException(nameof(enums));
        EnumExtensions = enumExtensions ?? throw new ArgumentNullException(nameof(enumExtensions));
        Delegates = delegates ?? throw new ArgumentNullException(nameof(delegates));
        Functions = functions ?? throw new ArgumentNullException(nameof(functions));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
        Assumptions = assumptions ?? throw new ArgumentNullException(nameof(assumptions));
        Invariants = invariants ?? throw new ArgumentNullException(nameof(invariants));
        Decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        Context = context;
        InteropBlocks = interopBlocks ?? Array.Empty<CSharpInteropBlockNode>();
        RefinementTypes = refinementTypes ?? Array.Empty<RefinementTypeNode>();
        IndexedTypes = indexedTypes ?? Array.Empty<IndexedTypeNode>();
        TypePreprocessorBlocks = typePreprocessorBlocks ?? Array.Empty<TypePreprocessorBlockNode>();
        Items = items ?? Array.Empty<AstNode>();
        NamespaceScopes = namespaceScopes ?? Array.Empty<NamespaceScopeInfo>();
    }

    /// <summary>
    /// Creates a metadata-preserving copy after applying explicit updates.
    /// The update object mirrors every aggregate field, so architecture tests
    /// fail when a new field is added without participating in copies.
    /// </summary>
    public ModuleNode With(Action<ModuleUpdate> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var update = new ModuleUpdate(this);
        configure(update);
        return CopyMetadataTo(update.Build());
    }

    public sealed class ModuleUpdate
    {
        internal ModuleUpdate(ModuleNode source)
        {
            Span = source.Span;
            Id = source.Id;
            Name = source.Name;
            IdentifierSpan = source.IdentifierSpan;
            Usings = source.Usings;
            Interfaces = source.Interfaces;
            Classes = source.Classes;
            Enums = source.Enums;
            EnumExtensions = source.EnumExtensions;
            Delegates = source.Delegates;
            Functions = source.Functions;
            Attributes = source.Attributes;
            Issues = source.Issues;
            Assumptions = source.Assumptions;
            Invariants = source.Invariants;
            Decisions = source.Decisions;
            Context = source.Context;
            InteropBlocks = source.InteropBlocks;
            TypePreprocessorBlocks = source.TypePreprocessorBlocks;
            Items = source.Items;
            RefinementTypes = source.RefinementTypes;
            IndexedTypes = source.IndexedTypes;
            NamespaceScopes = source.NamespaceScopes;
            DeclaredSemanticsVersion = source.DeclaredSemanticsVersion;
        }

        public TextSpan Span { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public TextSpan IdentifierSpan { get; set; }
        public IReadOnlyList<UsingDirectiveNode> Usings { get; set; }
        public IReadOnlyList<InterfaceDefinitionNode> Interfaces { get; set; }
        public IReadOnlyList<ClassDefinitionNode> Classes { get; set; }
        public IReadOnlyList<EnumDefinitionNode> Enums { get; set; }
        public IReadOnlyList<EnumExtensionNode> EnumExtensions { get; set; }
        public IReadOnlyList<DelegateDefinitionNode> Delegates { get; set; }
        public IReadOnlyList<FunctionNode> Functions { get; set; }
        public AttributeCollection Attributes { get; set; }
        public IReadOnlyList<IssueNode> Issues { get; set; }
        public IReadOnlyList<AssumeNode> Assumptions { get; set; }
        public IReadOnlyList<InvariantNode> Invariants { get; set; }
        public IReadOnlyList<DecisionNode> Decisions { get; set; }
        public ContextNode? Context { get; set; }
        public IReadOnlyList<CSharpInteropBlockNode> InteropBlocks { get; set; }
        public IReadOnlyList<TypePreprocessorBlockNode> TypePreprocessorBlocks { get; set; }
        public IReadOnlyList<AstNode> Items { get; set; }
        public IReadOnlyList<RefinementTypeNode> RefinementTypes { get; set; }
        public IReadOnlyList<IndexedTypeNode> IndexedTypes { get; set; }
        public IReadOnlyList<NamespaceScopeInfo> NamespaceScopes { get; set; }
        public string? DeclaredSemanticsVersion { get; set; }

        internal ModuleNode Build() =>
            new(
                Span,
                Id,
                Name,
                Usings,
                Interfaces,
                Classes,
                Enums,
                EnumExtensions,
                Delegates,
                Functions,
                Attributes,
                Issues,
                Assumptions,
                Invariants,
                Decisions,
                Context,
                InteropBlocks,
                RefinementTypes,
                IndexedTypes,
                TypePreprocessorBlocks,
                IdentifierSpan,
                Items,
                NamespaceScopes,
                DeclaredSemanticsVersion);
    }

    /// <summary>
    /// Returns true if this module has extended metadata (issues, assumptions, etc.).
    /// </summary>
    public bool HasExtendedMetadata => Issues.Count > 0 || Assumptions.Count > 0 ||
        Invariants.Count > 0 || Decisions.Count > 0 || Context != null;


}
