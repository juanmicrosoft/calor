using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Calor.Compiler.Analysis;

public sealed record ResolvedCallSite(SymbolId Callee, string Target, TextSpan Span);
public sealed record UnresolvedCallSite(SymbolId Caller, string Target, TextSpan Span);
public sealed record AstUnresolvedCallSite(string CallerId, string Target, TextSpan Span);

/// <summary>
/// Bound call graph keyed by stable symbol identity. The legacy AST call graph
/// remains available for AST-only consumers.
/// </summary>
public sealed class ResolvedSymbolCallGraph
{
    public IReadOnlyDictionary<SymbolId, IReadOnlyList<ResolvedCallSite>> ForwardGraph { get; }
    public IReadOnlyDictionary<SymbolId, IReadOnlyList<SymbolId>> ReverseGraph { get; }
    public IReadOnlyList<UnresolvedCallSite> UnresolvedCalls { get; }

    internal ResolvedSymbolCallGraph(
        IReadOnlyDictionary<SymbolId, IReadOnlyList<ResolvedCallSite>> forwardGraph,
        IReadOnlyDictionary<SymbolId, IReadOnlyList<SymbolId>> reverseGraph,
        IReadOnlyList<UnresolvedCallSite> unresolvedCalls)
    {
        ForwardGraph = forwardGraph;
        ReverseGraph = reverseGraph;
        UnresolvedCalls = unresolvedCalls;
    }
}

/// <summary>
/// Reusable call graph analysis extracted from EffectEnforcementPass.
/// Builds forward and reverse call graphs, resolves function names to IDs,
/// and computes strongly connected components via Tarjan's algorithm.
/// </summary>
public sealed class CallGraphAnalysis
{
    internal sealed record SourceCallSelection(
        AstNode Declaration,
        string ReturnType,
        IReadOnlyList<ResolvedArgumentMapping> Arguments,
        bool UsesUserDefinedConversion,
        bool UsesAnonymousFunctionConversion,
        bool IncludesExtensionReceiver,
        IMethodSymbol? Method = null,
        bool HasCompleteProofContext = false);

    internal sealed record MetadataMemberTypeSelection(
        ISymbol Member, INamedTypeSymbol ReceiverType, ITypeSymbol ResultType);

    internal sealed record MetadataGetterSelection(
        IPropertySymbol Property, INamedTypeSymbol ReceiverType);

    internal sealed record SourceDispatchSelection(
        IMethodSymbol Method, AstNode? Declaration, bool IsAuthoritative, bool HasCompleteProofContext);

    internal sealed record StableMemberSelection(
        ClassDefinitionNode Owner, PropertyNode Property, bool HasNonNullFlow);

    internal sealed record MetadataMethodGroupCallSelection(
        IMethodSymbol Method, INamedTypeSymbol ReceiverType, IReadOnlyList<FunctionNode> Callbacks);

    internal sealed record InitializationOperation(IOperation Operation, bool Preserved);

    internal sealed record InterpolationFormatting(
        ITypeSymbol? Type, bool IsNull, bool HasBoundedIntegralHexProof = false);

    internal sealed record EventStorageSelection(
        EventDefinitionNode Declaration, INamedTypeSymbol DelegateType, bool IsSubscription);

    internal sealed record SourceInitialization(
        IReadOnlyList<InitializationOperation>? Instance,
        IReadOnlyList<InitializationOperation>? Static,
        IMethodSymbol? BaseConstructor,
        bool IsValueType = false,
        bool HasImplicitStaticConstructor = false);

    internal sealed class OperatorBindings
    {
        internal Dictionary<AstNode, HashSet<string>> SourceCalls { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<AstNode, HashSet<EffectResolverKey>> ExternalCalls { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ExpressionNode, string> ResultTypes { get; } = new(ReferenceEqualityComparer.Instance);
        internal List<string> Failures { get; } = [];
        internal Dictionary<TextSpan, SourceCallSelection> SelectedCalls { get; } = [];
        /// <summary>The Calor declaration of each source method and constructor symbol, in every configuration's compilation.</summary>
        internal Dictionary<ISymbol, AstNode> SourceDeclarations { get; } = new(SymbolEqualityComparer.Default);
        internal Dictionary<TextSpan, SourceDispatchSelection?> SourceDispatches { get; } = [];
        internal HashSet<string> FunctionsRequiringDispatchProof { get; } = [];
        internal HashSet<TextSpan> SourceExtensionCalls { get; } = [];
        internal HashSet<TextSpan> ForeignSourceCallSites { get; } = [];
        internal HashSet<TextSpan> AmbiguousCalls { get; } = [];
        internal Dictionary<ParameterNode, ITypeSymbol> ParameterTypes { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<OutputNode, ITypeSymbol> OutputTypes { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<EventDefinitionNode, INamedTypeSymbol> EventTypes { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<AstNode, EventStorageSelection> EventStorageUses { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<LambdaExpressionNode, INamedTypeSymbol> LambdaTargets { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ExpressionCallNode, Binding.Metadata.MetadataBinderResult> ContextualMetadataCalls { get; } = new(ReferenceEqualityComparer.Instance);
        internal HashSet<BinaryOperationNode> BuiltInNullGuardOperations { get; } = new(ReferenceEqualityComparer.Instance);
        internal HashSet<BinaryOperationNode> TerminatingNullGuardConditions { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ExpressionNode, StableMemberSelection> StableMemberReads { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ExpressionNode, EffectResolverKey> StaticInitializerConstructors { get; } = new(ReferenceEqualityComparer.Instance);
        // Typing evidence only: neither bound-member identity nor getter-effect permission.
        internal Dictionary<FieldAccessNode, MetadataMemberTypeSelection> MetadataMemberTypes { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<FieldAccessNode, MetadataGetterSelection> MetadataGetters { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ExpressionCallNode, MetadataMethodGroupCallSelection> MetadataMethodGroupCalls { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ClassDefinitionNode, SourceInitialization> Initializations { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ISymbol, ClassDefinitionNode> InitializationClasses { get; } = new(SymbolEqualityComparer.Default);
        internal Dictionary<ISymbol, string> InitializationFunctions { get; } = new(SymbolEqualityComparer.Default);
        internal Dictionary<string, List<IOperation>?> InitializationBodies { get; } = [];
        internal Dictionary<ConstructorNode, IMethodSymbol> ConstructorInitializers { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<NewExpressionNode, IMethodSymbol> SourceConstructors { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<InterpolatedStringExpressionNode, InterpolationFormatting> InterpolationFormats { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<AstNode, IReadOnlyList<InterpolationFormatting>> AdditionFormats { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ReferenceNode, ClassDefinitionNode?> StaticFieldInitializations { get; } = new(ReferenceEqualityComparer.Instance);
        internal HashSet<ConstructorNode> RecordCopyConstructors { get; } = new(ReferenceEqualityComparer.Instance);
        internal List<Diagnostics.Diagnostic> SourceConstructionErrors { get; } = [];
        internal List<Diagnostics.Diagnostic> RuntimeQuantifierRejections { get; } = [];
    }

    private OperatorBindings _operatorBindings = new();
    internal IReadOnlyList<string> OperatorBindingFailures => _operatorBindings.Failures;
    internal IReadOnlyList<Diagnostics.Diagnostic> RuntimeQuantifierRejections =>
        _operatorBindings.RuntimeQuantifierRejections;
    internal IReadOnlyCollection<string> OperatorCalls(AstNode expression) =>
        _operatorBindings.SourceCalls.TryGetValue(expression, out var calls) ? calls : Array.Empty<string>();
    internal IReadOnlyCollection<EffectResolverKey> ExternalOperatorCalls(AstNode node) =>
        _operatorBindings.ExternalCalls.TryGetValue(node, out var calls) ? calls : Array.Empty<EffectResolverKey>();
    internal EffectResolverKey? StaticInitializerConstructor(ExpressionNode initializer) =>
        _operatorBindings.StaticInitializerConstructors.GetValueOrDefault(initializer);
    internal MetadataGetterSelection? SelectedMetadataGetter(FieldAccessNode field) =>
        _operatorBindings.MetadataGetters.GetValueOrDefault(field);
    internal MetadataMethodGroupCallSelection? SelectedMetadataMethodGroupCall(ExpressionCallNode call) =>
        _operatorBindings.MetadataMethodGroupCalls.GetValueOrDefault(call);
    internal SourceInitialization? Initialization(ClassDefinitionNode owner) =>
        _operatorBindings.Initializations.GetValueOrDefault(owner);
    internal ClassDefinitionNode? InitializationClass(INamedTypeSymbol type) =>
        _operatorBindings.InitializationClasses.GetValueOrDefault(type.OriginalDefinition);
    internal string? InitializationFunction(IMethodSymbol method) =>
        _operatorBindings.InitializationFunctions.GetValueOrDefault(method.OriginalDefinition);
    internal IReadOnlyList<IOperation>? InitializationBodies(string functionId) =>
        _operatorBindings.InitializationBodies.GetValueOrDefault(functionId);
    internal bool TrySourceDispatch(TextSpan span, out SourceDispatchSelection? selection) =>
        _operatorBindings.SourceDispatches.TryGetValue(span, out selection);
    internal bool RequiresSourceDispatchProof(string functionId) =>
        _operatorBindings.FunctionsRequiringDispatchProof.Contains(functionId);

    internal static bool HasAuthoritativeSourceDispatch(
        IMethodSymbol method, ITypeSymbol? receiver, bool nonVirtualDispatch) =>
        !method.IsAbstract && (method.IsStatic
            || method.ContainingType.TypeKind != TypeKind.Interface
            && (!(method.IsVirtual || method.IsOverride) || nonVirtualDispatch
                || method.IsSealed || method.ContainingType.IsSealed
                || receiver is INamedTypeSymbol { IsSealed: true }));
    internal IMethodSymbol? ConstructorInitializer(ConstructorNode constructor) =>
        _operatorBindings.ConstructorInitializers.GetValueOrDefault(constructor);
    internal IMethodSymbol? SourceConstructor(NewExpressionNode creation) =>
        _operatorBindings.SourceConstructors.GetValueOrDefault(creation);
    internal InterpolationFormatting? SelectedInterpolationFormatting(InterpolatedStringExpressionNode hole) =>
        _operatorBindings.InterpolationFormats.GetValueOrDefault(hole);
    internal IReadOnlyList<InterpolationFormatting>? SelectedAdditionFormatting(AstNode addition) =>
        _operatorBindings.AdditionFormats.GetValueOrDefault(addition);
    internal bool TrySelectedStaticFieldInitialization(ReferenceNode reference, out ClassDefinitionNode? owner) =>
        _operatorBindings.StaticFieldInitializations.TryGetValue(reference, out owner);

    internal static IMethodSymbol? NullableUnwrappingGetter(IConversionOperation conversion)
    {
        // Roslyn leaves OperatorMethod null for this built-in conversion, but
        // lowering still calls the constructed nullable type's Value getter.
        if (conversion.OperatorMethod is not null || !conversion.GetConversion().IsNullable
            || conversion.Operand.Type is not INamedTypeSymbol
                { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            || nullable.Locations.Any(location => location.IsInSource)
            || conversion.Type is not { IsValueType: true } target
            || target.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return null;
        return nullable.GetMembers("Value").OfType<IPropertySymbol>().SingleOrDefault(property =>
            !property.IsStatic && property.Parameters.Length == 0
            && SymbolEqualityComparer.Default.Equals(property.Type, nullable.TypeArguments[0]))?.GetMethod;
    }

    internal static InterpolationFormatting ConcatenationOperand(IOperation value)
    {
        // The built-in string/object operator boxes value operands. Inspect that
        // selected standard boxing conversion, never an explicit cast or user conversion.
        if (value is IConversionOperation
            { IsImplicit: true, OperatorMethod: null, Type.SpecialType: SpecialType.System_Object,
                Operand.Type.IsValueType: true } boxing)
            value = boxing.Operand;
        return new InterpolationFormatting(value.Type, value.ConstantValue is { HasValue: true, Value: null });
    }

    internal static InterpolationFormatting InterpolationOperand(IInterpolationOperation hole) =>
        new(hole.Expression.Type, hole.Expression.ConstantValue is { HasValue: true, Value: null },
            hole.Parent is IInterpolatedStringOperation interpolation && HasBoundedIntegralHexProof(interpolation));

    private static bool HasBoundedIntegralHexProof(IInterpolatedStringOperation interpolation)
    {
        if (interpolation.Type?.SpecialType != SpecialType.System_String
            || interpolation.SemanticModel?.Compilation is not { } compilation
            || HasCompilationErrors(compilation))
            return false;
        for (var parent = interpolation.Parent; parent != null; parent = parent.Parent)
            if (parent is IInterpolatedStringHandlerCreationOperation or IAnonymousFunctionOperation
                or IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String }
                || parent is IConversionOperation conversion
                    && conversion.Type?.SpecialType is not (SpecialType.System_String or SpecialType.System_Object))
                return false;

        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        var handler = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler");
        if (core.Identity.Version.Major != 10
            || compilation.GetMetadataReference(core) is not PortableExecutableReference reference
            || reference.FilePath != typeof(int).Assembly.Location
            || handler is not { IsRefLikeType: true }
            || handler.Locations.Any(location => location.IsInSource)
            || !SymbolEqualityComparer.Default.Equals(handler.ContainingAssembly, core)
            || !handler.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 2
                && constructor.Parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_Int32))
            || !HasAppendFormat(aligned: false) || !HasAppendFormat(aligned: true))
            return false;

        // .NET 10's two-argument default handler disables custom providers and rents
        // at least 256 chars. Bound the whole interpolation so neither hex writing
        // nor alignment can grow it; X/x with 0-2 digits uses the nonthrowing parser.
        var length = 0L;
        foreach (var part in interpolation.Parts)
        {
            if (part is IInterpolatedStringTextOperation { Text.ConstantValue: { HasValue: true, Value: string text } })
                length += text.Length;
            else if (part is IInterpolationOperation hole
                && hole.Expression.Type is { } type
                && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, core)
                && hole.FormatString?.ConstantValue is { HasValue: true, Value: string format }
                && format.Length is >= 1 and <= 3 && format[0] is 'X' or 'x')
            {
                var width = type.SpecialType switch
                {
                    SpecialType.System_SByte or SpecialType.System_Byte => 2,
                    SpecialType.System_Int16 or SpecialType.System_UInt16 => 4,
                    SpecialType.System_Int32 or SpecialType.System_UInt32 => 8,
                    SpecialType.System_Int64 or SpecialType.System_UInt64 => 16,
                    _ => 0
                };
                if (width == 0)
                    return false;
                var precision = 0;
                foreach (var digit in format.AsSpan(1))
                {
                    if (digit is < '0' or > '9')
                        return false;
                    precision = precision * 10 + digit - '0';
                }
                var alignment = 0;
                if (hole.Alignment != null)
                {
                    if (hole.Alignment.ConstantValue is not { HasValue: true, Value: int selectedAlignment }
                        || selectedAlignment is < -256 or > 256)
                        return false;
                    alignment = Math.Abs(selectedAlignment);
                }
                length += Math.Max(Math.Max(width, precision), alignment);
            }
            else
                return false;
            if (length > 256)
                return false;
        }
        return true;

        bool HasAppendFormat(bool aligned) => handler.GetMembers("AppendFormatted").OfType<IMethodSymbol>()
            .Any(method => method is { IsGenericMethod: true, Arity: 1, ReturnsVoid: true }
                && method.Parameters.Length == (aligned ? 3 : 2)
                && method.Parameters[0].Type is ITypeParameterSymbol { Ordinal: 0 }
                && (!aligned || method.Parameters[1].Type.SpecialType == SpecialType.System_Int32)
                && method.Parameters[^1].Type.SpecialType == SpecialType.System_String);
    }

    internal bool IsRecordCopyConstructor(ConstructorNode constructor) =>
        _operatorBindings.RecordCopyConstructors.Contains(constructor);
    internal IReadOnlyList<Diagnostics.Diagnostic> SourceConstructionErrors =>
        _operatorBindings.SourceConstructionErrors;

    // A compilation is immutable, so whether it has an error is computed once. Every
    // GetDiagnostics() call compiles all method bodies again, including every companion
    // module's, and the projection asked up to three times per image plus once per
    // interpolated string.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation,
        CompilationErrorState> CompilationErrorStates = new();

    internal static bool HasCompilationErrors(Compilation compilation) =>
        ErrorStateOf(compilation).HasErrors;

    private static CompilationErrorState ErrorStateOf(Compilation compilation) =>
        CompilationErrorStates.GetValue(compilation, image => new CompilationErrorState(image));

    // For the same reason one semantic model per tree and one diagnostic list per span
    // answer every later request against the same compilation. A semantic model binds
    // each member body once and keeps it, so a second model of the same tree (a shared
    // image's tree bound by another request of the pass) bound every body again; and
    // GetDiagnostics(span) compiles the bodies the span intersects on every call, so
    // the checks that ask about the same span (a type header for every call into that
    // type, a member asked about by more than one request) compiled them again. Both
    // answers are functions of the compilation, the tree and the span alone:
    // SemanticModel.GetDiagnostics(span) is the compilation's diagnostics for that tree
    // and span, independent of the model instance.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation,
        CompilationQueries> CompilationQueryCaches = new();

    private sealed class CompilationQueries(Compilation compilation)
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<SyntaxTree, SemanticModel> _models = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            (SyntaxTree Tree, Microsoft.CodeAnalysis.Text.TextSpan Span),
            System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> _diagnostics = new();

        internal SemanticModel Model(SyntaxTree tree) =>
            _models.GetOrAdd(tree, key => compilation.GetSemanticModel(key));

        internal System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Diagnostics(
            SyntaxTree tree, Microsoft.CodeAnalysis.Text.TextSpan span) =>
            _diagnostics.GetOrAdd((tree, span), key => Model(key.Tree).GetDiagnostics(key.Span));
    }

    private static CompilationQueries QueriesOf(Compilation compilation) =>
        CompilationQueryCaches.GetValue(compilation, image => new CompilationQueries(image));

    /// <summary>The (shared) semantic model of <paramref name="tree"/> in <paramref name="compilation"/>.</summary>
    private static SemanticModel ModelOf(Compilation compilation, SyntaxTree tree) =>
        QueriesOf(compilation).Model(tree);

    /// <summary>
    /// <c>model.GetDiagnostics(span)</c>, computed once per compilation, tree and span.
    /// </summary>
    private static System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> DiagnosticsIn(
        SemanticModel model, Microsoft.CodeAnalysis.Text.TextSpan span) =>
        QueriesOf(model.Compilation).Diagnostics(model.SyntaxTree, span);

    /// <summary>
    /// Where an image's errors are. C# binds each method, accessor and expression body
    /// against declaration symbols only; no body's contents can change how another body
    /// binds. So when every error lies inside some member's body (no parse error, no
    /// declaration, signature, initializer or compilation-level error), an operation in a
    /// member whose own declaration is error-free binds exactly as in an error-free image,
    /// and its proof stands. Any other error voids every proof in the image.
    /// </summary>
    private sealed class CompilationErrorState
    {
        private readonly HashSet<(SyntaxTree Tree, Microsoft.CodeAnalysis.Text.TextSpan Span)>? _erroneousMembers;

        internal bool HasErrors { get; }

        internal CompilationErrorState(Compilation compilation)
        {
            var errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .ToArray();
            HasErrors = errors.Length > 0;
            if (!HasErrors || compilation.SyntaxTrees.Any(tree => tree.GetDiagnostics().Any(diagnostic =>
                    diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)))
                return;
            var members = new HashSet<(SyntaxTree, Microsoft.CodeAnalysis.Text.TextSpan)>();
            foreach (var error in errors)
            {
                if (BodyOwner(error.Location) is not { } member)
                    return;
                members.Add((member.SyntaxTree, member.Span));
            }
            _erroneousMembers = members;
        }

        /// <summary>
        /// True when operations under <paramref name="declaration"/> bind as they would in
        /// an error-free image: no error at all, or only errors confined to other members' bodies.
        /// </summary>
        internal bool AdmitsProofsIn(SyntaxNode declaration)
        {
            if (!HasErrors)
                return true;
            return _erroneousMembers != null
                && declaration.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault() is
                    (BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or BaseFieldDeclarationSyntax)
                    and { } member
                && !_erroneousMembers.Contains((member.SyntaxTree, member.Span));
        }

        // The member whose body (block or expression body, never a signature, constructor
        // initializer, attribute or property/field initializer) contains the location.
        private static MemberDeclarationSyntax? BodyOwner(Location location)
        {
            if (!location.IsInSource || location.SourceTree is not { } tree)
                return null;
            var span = location.SourceSpan;
            for (SyntaxNode? node = tree.GetRoot().FindNode(span, getInnermostNodeForTie: true);
                 node != null; node = node.Parent)
            {
                MemberDeclarationSyntax? owner;
                SyntaxNode? body, expressionBody;
                switch (node)
                {
                    case BaseMethodDeclarationSyntax method:
                        (owner, body, expressionBody) = (method, method.Body, method.ExpressionBody);
                        break;
                    case AccessorDeclarationSyntax accessor:
                        (owner, body, expressionBody) = (accessor.Parent?.Parent as BasePropertyDeclarationSyntax,
                            accessor.Body, accessor.ExpressionBody);
                        break;
                    case PropertyDeclarationSyntax property:
                        (owner, body, expressionBody) = (property, null, property.ExpressionBody);
                        break;
                    case IndexerDeclarationSyntax indexer:
                        (owner, body, expressionBody) = (indexer, null, indexer.ExpressionBody);
                        break;
                    case MemberDeclarationSyntax:
                        return null;
                    default:
                        continue;
                }
                return owner != null
                    && (body?.Span.Contains(span) == true || expressionBody?.Span.Contains(span) == true)
                    ? owner : null;
            }
            return null;
        }
    }

    // A companion projection is emitted once per registry and contract mode, and every
    // module of the project binds against it. Its parse depends only on its text, path
    // and parse options (CSharpParseOptions compares by value, preprocessor symbols
    // included), and a syntax tree may be shared by any number of compilations.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SourceProjection,
        System.Collections.Concurrent.ConcurrentDictionary<CSharpParseOptions, SyntaxTree>> CompanionTrees = new();

    private static SyntaxTree ParseCompanion(SourceProjection companion, CSharpParseOptions options) =>
        CompanionTrees.GetOrCreateValue(companion).GetOrAdd(options,
            selected => CSharpSyntaxTree.ParseText(companion.Source, selected, companion.Path));

    // The preprocessor symbols a parsed companion's #if/#elif conditions name. A tree
    // is immutable, so this is read once per companion tree, not once per module
    // binding against it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxTree, string[]>
        CompanionConditionSymbols = new();

    private static string[] ConditionSymbols(SyntaxTree tree) =>
        CompanionConditionSymbols.GetValue(tree, ReadConditionSymbols);

    private static string[] ReadConditionSymbols(SyntaxTree tree) => tree.GetRoot()
        .DescendantTrivia(descendIntoTrivia: true)
        .Select(trivia => trivia.GetStructure()).OfType<DirectiveTriviaSyntax>()
        .Select(directive => directive switch
        {
            IfDirectiveTriviaSyntax conditional => conditional.Condition,
            ElifDirectiveTriviaSyntax conditional => conditional.Condition,
            _ => null
        })
        .OfType<ExpressionSyntax>()
        .SelectMany(condition => condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        .Select(identifier => identifier.Identifier.ValueText)
        .ToArray();

    /// <summary>
    /// The comment markers an annotated projection writes: expression start/end
    /// around every emitted expression or statement, and an origin marker before
    /// every callable and class declaration. Each set carries a fresh GUID, so
    /// user text can never forge one.
    /// </summary>
    internal sealed record ProjectionMarkers(string Expression, string ExpressionEnd, string Callable, string Type)
    {
        internal static ProjectionMarkers Create()
        {
            var identity = Guid.NewGuid().ToString("N");
            return new($"calor-expression-{identity}:", $"calor-expression-end-{identity}:",
                $"calor-callable-{identity}:", $"calor-type-{identity}:");
        }
    }

    internal sealed record AnnotatedProjection(string Source, List<AstNode> Expressions,
        List<AstNode> Callables, List<ClassDefinitionNode> Types, DiagnosticBag Diagnostics);

    /// <summary>
    /// The C# image of one module that operator and source-call binding reads:
    /// the module's real emission, with every expression and declaration marked
    /// with its Calor origin. Deterministic for a given module, contract mode,
    /// cross-module map and marker set.
    /// </summary>
    internal static AnnotatedProjection EmitAnnotatedProjection(ModuleNode module, ContractMode contractMode,
        IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions,
        ProjectionMarkers markers)
    {
        var expressions = new List<AstNode>();
        var callables = new List<AstNode>();
        var types = new List<ClassDefinitionNode>();
        var diagnostics = new DiagnosticBag();
        var emitter = new CSharpEmitter(contractMode, null, null, diagnostics: diagnostics)
        {
            // Qualify cross-module calls as the real emission does, so a call into a
            // companion module binds instead of failing the whole projection.
            CrossModuleFunctionModules = crossModuleFunctions,
            AnnotateSource = (expression, text) =>
            {
                var index = expressions.Count;
                expressions.Add(expression);
                return $"/*{markers.Expression}{index}*/{text}/*{markers.ExpressionEnd}{index}*/";
            },
            CallableOrigin = declaration =>
            {
                var index = callables.Count;
                callables.Add(declaration);
                return $"/*{markers.Callable}{index}*/";
            },
            TypeOrigin = declaration =>
            {
                var index = types.Count;
                types.Add(declaration);
                return $"/*{markers.Type}{index}*/";
            }
        };
        var source = emitter.Emit(module);
        return new(source, expressions, callables, types, diagnostics);
    }

    // The source methods one companion tree declares, keyed by its callable markers,
    // in declaration order: what binding a module against that companion records.
    private static IEnumerable<(IMethodSymbol Method, AstNode Origin)> DeclaredCompanionMethods(
        Compilation compilation, SyntaxTree tree, string marker, IReadOnlyList<AstNode> callables)
    {
        var model = ModelOf(compilation, tree);
        foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            foreach (var trivia in declaration.GetLeadingTrivia())
            {
                if (TryReadMarker(trivia, marker, out var index)
                    && model.GetDeclaredSymbol(declaration) is IMethodSymbol method)
                    yield return (method, callables[index]);
            }
        }
    }

    /// <summary>
    /// One C# image of every module in a project pass, compiled once per
    /// preprocessor configuration and shared by every module that binds against
    /// its companions (the binder's projection and the call graph's alike).
    ///
    /// <para>Without it each module compiled its own image (itself plus every
    /// companion), and the projection's proof gate asks whether that compilation
    /// has any error, which compiles every companion body. Index builds were
    /// therefore quadratic in project size.</para>
    ///
    /// <para>The module's own image is used only when its projection text is
    /// exactly the text recorded here (same markers, same cross-module map, same
    /// module content), so a changed source, a different map or a different
    /// contract mode can never reuse it. The companions here are the annotated
    /// projections, not the plain ones a per-module compilation uses; a
    /// configuration is shared only after every module's annotated tree is
    /// checked to be the same program as its plain tree: the same tokens, the
    /// same directives and inactive text, the same callable origins at the same
    /// tokens, differing only in comment markers and in the marker text the
    /// contract messages quote inside string literals. Any difference makes the
    /// module bind against its own compilation exactly as before.</para>
    /// </summary>
    internal sealed class SharedProjectionImage
    {
        private readonly (string Path, AnnotatedProjection Projection, SourceProjection Plain)[] _entries;
        // Keyed by the reference list too: an image is shared across one project pass,
        // and the list object stands for the explicit reference set in scope.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            (CSharpParseOptions Options, IReadOnlyList<MetadataReference> References),
            Lazy<SharedCompilation?>> _compilations = new();
        private readonly System.Text.RegularExpressions.Regex _annotation;

        private SharedProjectionImage(ProjectionMarkers markers,
            (string Path, AnnotatedProjection Projection, SourceProjection Plain)[] entries)
        {
            Markers = markers;
            _entries = entries;
            _annotation = new(
                $@"/\*(?:{System.Text.RegularExpressions.Regex.Escape(markers.Expression)}"
                + $"|{System.Text.RegularExpressions.Regex.Escape(markers.ExpressionEnd)}"
                + $@"|{System.Text.RegularExpressions.Regex.Escape(markers.Type)})[0-9]+\*/",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        internal ProjectionMarkers Markers { get; }

        /// <summary>
        /// Emits every module's annotated projection, in the order of the plain
        /// companion projections. Null when any module cannot be projected.
        /// </summary>
        internal static SharedProjectionImage? Create(IReadOnlyList<SourceProjection> plain, ContractMode mode,
            IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions)
        {
            var markers = ProjectionMarkers.Create();
            var entries = new (string, AnnotatedProjection, SourceProjection)[plain.Count];
            for (var index = 0; index < plain.Count; index++)
            {
                AnnotatedProjection projection;
                try
                {
                    projection = EmitAnnotatedProjection(plain[index].Module, mode, crossModuleFunctions, markers);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Sharing is only an optimization: a module that cannot be projected
                    // here still projects (or fails) on its own path, exactly as before.
                    return null;
                }
                if (projection.Diagnostics.HasErrors)
                    return null;
                entries[index] = (plain[index].Path, projection, plain[index]);
            }
            return new(markers, entries);
        }

        internal sealed record Selection(int Index, IReadOnlyList<SharedCompilation> Compilations);

        /// <summary>
        /// The shared compilations a module's projection may bind in, one per
        /// configuration, or null when the module must compile its own image: its
        /// path is not exactly one entry, its text differs from that entry's, or a
        /// configuration failed the equivalence check.
        /// </summary>
        internal Selection? Select(string sourcePath, string source, IReadOnlyList<CSharpParseOptions> configurations)
        {
            var comparer = Incremental.BuildStateCache.GetPathComparer();
            var path = Path.GetFullPath(sourcePath);
            var index = -1;
            for (var candidate = 0; candidate < _entries.Length; candidate++)
            {
                if (!comparer.Equals(_entries[candidate].Path, path))
                    continue;
                if (index >= 0)
                    return null;
                index = candidate;
            }
            if (index < 0 || !string.Equals(_entries[index].Projection.Source, source, StringComparison.Ordinal))
                return null;
            var compilations = new SharedCompilation[configurations.Count];
            for (var configuration = 0; configuration < configurations.Count; configuration++)
            {
                var options = configurations[configuration];
                if (_compilations.GetOrAdd((options, Binding.Metadata.MetadataContext.ProjectionReferences()),
                            selected => new Lazy<SharedCompilation?>(() => Compile(selected.Options, selected.References)))
                        .Value is not { } compilation)
                    return null;
                compilations[configuration] = compilation;
            }
            return new(index, compilations);
        }

        private SharedCompilation? Compile(CSharpParseOptions options, IReadOnlyList<MetadataReference> references)
        {
            var trees = new SyntaxTree[_entries.Length];
            for (var index = 0; index < _entries.Length; index++)
            {
                var (path, projection, plain) = _entries[index];
                trees[index] = CSharpSyntaxTree.ParseText(projection.Source, options, path);
                if (!SameProgram(trees[index], projection.Callables, ParseCompanion(plain, options), plain))
                    return null;
            }
            var compilation = CSharpCompilation.Create("CalorOperatorBindings", trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            return new SharedCompilation(compilation, trees,
                _entries.Select(entry => (IReadOnlyList<AstNode>)entry.Projection.Callables).ToArray(),
                Markers.Callable);
        }

        // The annotated tree and the plain tree are the same program: equal token
        // kinds and texts (a literal's text may differ only by the expression and
        // type markers the annotation quotes into it), equal inactive text, and the
        // same callable origins before the same tokens. Directives, doc comments and
        // skipped text are structured trivia, so their tokens are compared too.
        private bool SameProgram(SyntaxTree annotated, IReadOnlyList<AstNode> annotatedCallables,
            SyntaxTree plain, SourceProjection plainProjection)
        {
            if (annotatedCallables.Count != plainProjection.Callables.Count
                || annotatedCallables.Where((callable, index) =>
                    !ReferenceEquals(callable, plainProjection.Callables[index])).Any())
                return false;
            var left = annotated.GetRoot().DescendantTokens(descendIntoTrivia: true).ToArray();
            var right = plain.GetRoot().DescendantTokens(descendIntoTrivia: true).ToArray();
            if (left.Length != right.Length)
                return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index].RawKind != right[index].RawKind)
                    return false;
                if (string.Equals(left[index].Text, right[index].Text, StringComparison.Ordinal))
                    continue;
                if (!IsLiteralText(left[index])
                    || !string.Equals(Plain(left[index].Text, plainProjection.Marker), right[index].Text,
                        StringComparison.Ordinal))
                    return false;
            }
            return Landmarks(annotated, left, Markers.Callable, plainProjection.Marker)
                .SequenceEqual(Landmarks(plain, right, plainProjection.Marker, plainProjection.Marker),
                    StringComparer.Ordinal);
        }

        private IEnumerable<string> Landmarks(SyntaxTree tree, SyntaxToken[] tokens, string callableMarker,
            string plainMarker)
        {
            foreach (var trivia in tree.GetRoot().DescendantTrivia(descendIntoTrivia: true))
            {
                if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                    yield return "inactive:" + Plain(trivia.ToString(), plainMarker);
                else if (TryReadMarker(trivia, callableMarker, out var index))
                    yield return $"callable:{index}@{FirstTokenFrom(tokens, trivia.Span.End)}";
            }
        }

        // Annotated text with the expression and type markers removed and the
        // callable markers spelled as the plain projection spells them.
        private string Plain(string text, string plainMarker) =>
            _annotation.Replace(text, "").Replace("/*" + Markers.Callable, "/*" + plainMarker, StringComparison.Ordinal);

        private static int FirstTokenFrom(SyntaxToken[] tokens, int position)
        {
            int low = 0, high = tokens.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (tokens[middle].SpanStart < position)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private static bool IsLiteralText(SyntaxToken token) => token.Kind() is SyntaxKind.StringLiteralToken
            or SyntaxKind.Utf8StringLiteralToken or SyntaxKind.SingleLineRawStringLiteralToken
            or SyntaxKind.Utf8SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
            or SyntaxKind.Utf8MultiLineRawStringLiteralToken or SyntaxKind.InterpolatedStringTextToken
            or SyntaxKind.CharacterLiteralToken or SyntaxKind.XmlTextLiteralToken;
    }

    /// <summary>
    /// The operator bindings one project pass has resolved, by request, so that a
    /// second identical request (the same module object, contract mode, effective module
    /// context, source path and reference set) returns the first answer instead of
    /// compiling and binding the same projection again. An answer is returned only when
    /// the request's projection text is exactly the text it was computed from, so a
    /// module whose content (or cross-module qualification) differs is resolved afresh.
    /// Answers are read-only to every consumer. Owned by the pass's
    /// <see cref="ISourceBindingContext"/> and dropped with it.
    /// </summary>
    internal sealed class OperatorBindingsMemo
    {
        internal sealed record Request(ModuleNode Module, ContractMode Mode, bool SingleModuleContext,
            string? SourcePath, object References, object? ReferenceScope)
        {
            // Identity, not structure: the module and the reference set are the objects
            // the answer was computed from.
            public bool Equals(Request? other) => other is not null
                && ReferenceEquals(Module, other.Module) && Mode == other.Mode
                && SingleModuleContext == other.SingleModuleContext
                && string.Equals(SourcePath, other.SourcePath, StringComparison.Ordinal)
                && ReferenceEquals(References, other.References)
                && ReferenceEquals(ReferenceScope, other.ReferenceScope);

            public override int GetHashCode() => HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Module), Mode, SingleModuleContext,
                SourcePath is null ? 0 : StringComparer.Ordinal.GetHashCode(SourcePath),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(References));
        }

        private readonly Dictionary<Request, (string Source, OperatorBindings Bindings)> _answers = [];

        internal OperatorBindings? Find(Request request, string source)
        {
            lock (_answers)
                return _answers.TryGetValue(request, out var answer)
                    && string.Equals(answer.Source, source, StringComparison.Ordinal)
                        ? answer.Bindings : null;
        }

        internal void Record(Request request, string source, OperatorBindings bindings)
        {
            lock (_answers)
                _answers[request] = (source, bindings);
        }
    }

    /// <summary>One configuration of a <see cref="SharedProjectionImage"/>.</summary>
    internal sealed class SharedCompilation
    {
        private readonly Lazy<(IMethodSymbol Method, AstNode Origin)[]>[] _companionMethods;

        internal SharedCompilation(CSharpCompilation compilation, IReadOnlyList<SyntaxTree> trees,
            IReadOnlyList<IReadOnlyList<AstNode>> callables, string marker)
        {
            Compilation = compilation;
            Trees = trees;
            _companionMethods = trees.Select((tree, index) => new Lazy<(IMethodSymbol, AstNode)[]>(() =>
                DeclaredCompanionMethods(compilation, tree, marker, callables[index]).ToArray())).ToArray();
        }

        internal CSharpCompilation Compilation { get; }
        internal IReadOnlyList<SyntaxTree> Trees { get; }

        // What a per-module compilation records for its companions: every other
        // module's declared methods, in module order.
        internal IEnumerable<(IMethodSymbol Method, AstNode Origin)> CompanionMethodsExcept(int index) =>
            _companionMethods.Where((_, candidate) => candidate != index).SelectMany(methods => methods.Value);
    }

    internal static OperatorBindings ResolveOperatorCalls(ModuleNode module, DiagnosticBag? diagnostics = null,
        ContractMode contractMode = ContractMode.Debug, bool singleModuleContext = true,
        ISourceBindingContext? crossModuleTypes = null, string? sourcePath = null,
        IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions = null)
    {
        var bindings = new OperatorBindings();
        var types = bindings.ResultTypes;
        var bindingFailures = bindings.Failures;
        var calls = bindings.SourceCalls;
        var operators = EnumerateClasses(module)
            .SelectMany(owner => EnumerateOperators(owner).Select(op => (Node: op, Id: $"{owner.Name}.{op.Id}")))
            .ToArray();
        var initializerCreations = EnumerateClasses(module)
            .SelectMany(owner => owner.Fields.Where(field => field.IsStatic).Select(field => field.DefaultValue)
                .Concat(owner.Properties.Where(property => property.IsStatic).Select(property => property.DefaultValue)))
            .OfType<NewExpressionNode>()
            .Where(creation => creation.Arguments.Count == 0
                && creation.Initializers.Count == 0 && creation.CollectionInitializers.Count == 0)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var conflictingTypes = new HashSet<ExpressionNode>(ReferenceEqualityComparer.Instance);
        var conflictingParameters = new HashSet<ParameterNode>(ReferenceEqualityComparer.Instance);
        var conflictingOutputs = new HashSet<OutputNode>(ReferenceEqualityComparer.Instance);
        var conflictingEvents = new HashSet<EventDefinitionNode>(ReferenceEqualityComparer.Instance);
        var conflictingEventUses = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var conflictingLambdas = new HashSet<LambdaExpressionNode>(ReferenceEqualityComparer.Instance);
        var conflictingNullGuards = new HashSet<BinaryOperationNode>(ReferenceEqualityComparer.Instance);
        var conflictingGuardTerminations = new HashSet<BinaryOperationNode>(ReferenceEqualityComparer.Instance);
        var conflictingStableMembers = new HashSet<ExpressionNode>(ReferenceEqualityComparer.Instance);
        var conflictingCalls = new HashSet<TextSpan>();
        var conflictingMembers = new HashSet<FieldAccessNode>(ReferenceEqualityComparer.Instance);
        var conflictingGetters = new HashSet<FieldAccessNode>(ReferenceEqualityComparer.Instance);
        var conflictingMethodGroupCalls = new HashSet<ExpressionCallNode>(ReferenceEqualityComparer.Instance);
        var conflictingMetadataCalls = new HashSet<ExpressionCallNode>(ReferenceEqualityComparer.Instance);
        var conflictingConstructorInitializers = new HashSet<ConstructorNode>(ReferenceEqualityComparer.Instance);
        var conflictingSourceConstructors = new HashSet<NewExpressionNode>(ReferenceEqualityComparer.Instance);
        var conflictingInterpolationFormats = new HashSet<InterpolatedStringExpressionNode>(ReferenceEqualityComparer.Instance);
        var conflictingAdditions = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var conflictingStaticFieldReads = new HashSet<ReferenceNode>(ReferenceEqualityComparer.Instance);
        var argumentOwners = new Dictionary<AstNode, (TextSpan Call, int Index)>(ReferenceEqualityComparer.Instance);
        var ambiguousArguments = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var sourceArgumentCounts = new Dictionary<TextSpan, int>();
        var visited = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var nodes = new Stack<AstNode>();
        nodes.Push(module);
        while (nodes.TryPop(out var node))
        {
            if (!visited.Add(node))
                continue;
            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                nodes.Push(child);
            if (node is LocalFunctionStatementNode local)
                nodes.Push(local.Function);
            IReadOnlyList<ExpressionNode>? arguments = node switch
            {
                CallExpressionNode call => call.Arguments,
                ExpressionCallNode call => call.Arguments,
                CallStatementNode call => call.Arguments,
                NewExpressionNode creation => creation.Arguments,
                ConstructorInitializerNode initializer => initializer.Arguments,
                _ => null
            };
            if (arguments is null)
                continue;
            sourceArgumentCounts[node.Span] = arguments.Count;
            // Only a selected extension call expands this receiver into argument zero.
            if (node is ExpressionCallNode { TargetExpression: FieldAccessNode member })
                RegisterArgument(member.Target, (node.Span, -1));
            for (var index = 0; index < arguments.Count; index++)
                RegisterArgument(arguments[index], (node.Span, index));
        }
        var bindExpressions = operators.Length > 0 || visited.OfType<LambdaExpressionNode>().Any();
        var bindNullableUnwrapping = visited.OfType<TypeOperationNode>().Any(operation =>
            operation.Operation == TypeOp.Cast);
        var bindSourceExtensions = visited.OfType<ExpressionCallNode>().Any(call =>
            call.TargetExpression is FieldAccessNode);
        var bindNullGuards = visited.OfType<BinaryOperationNode>().Any(binary =>
            binary.Operator == BinaryOperator.Equal
            && (binary.Left is ReferenceNode { IsNullLiteral: true }
                || binary.Right is ReferenceNode { IsNullLiteral: true }));
        var bindMemberTypes = visited.OfType<FieldAccessNode>().Any();
        var bindInitialization = EnumerateClasses(module).Any();
        var bindEvents = EnumerateClasses(module).Any(owner => EnumerateEvents(owner).Any(evt => !evt.HasAccessors));
        var bindInterpolation = visited.OfType<InterpolatedStringNode>().Any();
        var additions = visited.Where(IsAddition).ToArray();
        if (!bindExpressions && !bindNullableUnwrapping && !bindNullGuards && !bindMemberTypes && !bindInitialization
            && !bindInterpolation && additions.Length == 0
            && initializerCreations.Count == 0 && !visited.OfType<ParameterNode>().Any()
            && !visited.OfType<OutputNode>().Any())
            return bindings;

        void RegisterArgument(AstNode argument, (TextSpan Call, int Index) owner)
        {
            if (ambiguousArguments.Contains(argument))
                return;
            if (argumentOwners.TryGetValue(argument, out var previous) && previous != owner)
            {
                argumentOwners.Remove(argument);
                ambiguousArguments.Add(argument);
            }
            else
                argumentOwners[argument] = owner;
        }
        // Bind the emitted expressions, not operator names or approximate operand
        // types. This also sees implicit receiving conversions and lifted operators.
        // A project pass shares one image of every module (see SharedProjectionImage);
        // this module's projection carries the image's markers so that, when its text
        // is the image's text for this module, the shared compilation stands in for it.
        var image = crossModuleTypes?.SharedImage(sourcePath, contractMode);
        var markers = image?.Markers ?? ProjectionMarkers.Create();
        var expressionPrefix = markers.Expression;
        var expressionEndPrefix = markers.ExpressionEnd;
        var callablePrefix = markers.Callable;
        var typePrefix = markers.Type;
        var operatorIds = operators.ToDictionary(pair => pair.Node, pair => pair.Id);
        var initializationIds = new Dictionary<AstNode, string>(ReferenceEqualityComparer.Instance);
        foreach (var function in module.Functions)
            initializationIds[function] = function.Id;
        foreach (var owner in EnumerateClasses(module))
        {
            foreach (var method in EnumerateMethods(owner))
                initializationIds[method] = $"{owner.Name}.{method.Id}";
            foreach (var constructor in EnumerateConstructors(owner))
                initializationIds[constructor] = $"{owner.Name}.{constructor.Id}";
            foreach (var op in EnumerateOperators(owner))
                initializationIds[op] = $"{owner.Name}.{op.Id}";
            foreach (var property in EnumerateProperties(owner))
                foreach (var accessor in new[] { property.Getter, property.Setter, property.Initer })
                    if (accessor != null)
                        initializationIds[accessor] = GetPropertyAccessorFunctionId(owner.Name, property, accessor);
        }
        var projection = EmitAnnotatedProjection(module, contractMode, crossModuleFunctions, markers);
        var source = projection.Source;
        var expressions = projection.Expressions;
        var emittedCallables = projection.Callables;
        var emittedTypes = projection.Types;
        var emissionDiagnostics = projection.Diagnostics;
        var companions = crossModuleTypes?.CompanionSources(sourcePath, contractMode) ?? [];
        foreach (var diagnostic in emissionDiagnostics)
            diagnostics?.Add(diagnostic);
        if (emissionDiagnostics.HasErrors)
        {
            if (emissionDiagnostics.Errors.All(diagnostic =>
                diagnostic.Code == DiagnosticCode.QuantifierRuntimeLoweringUnsupported))
                bindings.RuntimeQuantifierRejections.AddRange(emissionDiagnostics.Errors);
            bindingFailures.AddRange(emissionDiagnostics.Errors.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));
            return bindings;
        }
        // A project pass asks for the same module's bindings more than once (the symbol
        // index's binder, the effect pass's call-graph binder and the effect pass's call
        // graph itself). The answer is a function of the request and of this projection
        // text, so a request already answered for exactly this text returns that answer.
        // The cross-module map is not part of the request: it reaches the answer only
        // through the projection text, which is compared. The module context is, but only
        // where it is read: for a partial type of this module's own image (below), so
        // without one the two contexts are the same request. The emission diagnostics
        // above were reported to this caller's bag either way.
        var memo = image is null ? null : crossModuleTypes?.OperatorBindingsMemo;
        var memoKey = new OperatorBindingsMemo.Request(module, contractMode,
            singleModuleContext || !emittedTypes.Any(type => type.IsPartial), sourcePath,
            Binding.Metadata.MetadataContext.ProjectionReferences(),
            Binding.Metadata.MetadataContext.CurrentReferenceScope);
        if (memo?.Find(memoKey, source) is { } answered)
            return answered;
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var initial = CSharpSyntaxTree.ParseText(source, options);
        // A companion's condition symbols are read once per parsed companion, not once
        // per module that binds against it; the union is the same set.
        var symbols = companions.Select(companion => ParseCompanion(companion, options))
            .Select(ConditionSymbols).Prepend(ReadConditionSymbols(initial)).SelectMany(names => names)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // Match the converter's exhaustive conditional-analysis bound. An image
        // we cannot exhaust is an explicit failure, never proof of a pure caller.
        const int exhaustiveSymbolLimit = 8;
        if (symbols.Length > exhaustiveSymbolLimit)
        {
            bindingFailures.Add($"Source operator binding requires at most {exhaustiveSymbolLimit} preprocessor condition symbols; found {symbols.Length}.");
            return bindings;
        }
        var configurations = Enumerable.Range(0, 1 << symbols.Length)
            .Select(mask => options.WithPreprocessorSymbols(symbols.Where((_, index) => (mask & 1 << index) != 0)))
            .ToArray();
        // The shared image serves this module only when its text is exactly this
        // projection and every configuration passed the image's equivalence check.
        // Otherwise the module binds against its own compilation, as before.
        if (image?.Select(sourcePath!, source, configurations) is { } shared)
        {
            foreach (var compilation in shared.Compilations)
                BindTree(compilation.Trees[shared.Index], compilation.Compilation,
                    compilation.CompanionMethodsExcept(shared.Index));
            memo?.Record(memoKey, source, bindings);
            return bindings;
        }
        foreach (var configuration in configurations)
        {
            var tree = CSharpSyntaxTree.ParseText(source, configuration);
            var companionTrees = companions.Select(companion => (
                Projection: companion,
                Tree: ParseCompanion(companion, (CSharpParseOptions)tree.Options))).ToArray();
            var compilation = CSharpCompilation.Create("CalorOperatorBindings",
                companionTrees.Select(companion => (SyntaxTree)companion.Tree).Prepend(tree),
                Binding.Metadata.MetadataContext.ProjectionReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            BindTree(tree, compilation, companionTrees.SelectMany(companion => DeclaredCompanionMethods(
                compilation, companion.Tree, companion.Projection.Marker, companion.Projection.Callables)));
        }
        memo?.Record(memoKey, source, bindings);
        return bindings;

        void BindTree(SyntaxTree tree, CSharpCompilation compilation,
            IEnumerable<(IMethodSymbol Method, AstNode Origin)> companionMethods)
        {
            var model = ModelOf(compilation, tree);
            var root = tree.GetRoot();
            var methods = new Dictionary<ISymbol, AstNode>(SymbolEqualityComparer.Default);
            foreach (var (method, origin) in companionMethods)
            {
                methods[method] = origin;
                bindings.SourceDeclarations[method] = origin;
            }
            // A record's synthesized base call, recorded like an explicit initializer: a
            // configuration that selects another constructor makes the selection conflicting.
            void SetRecordInitializer(ConstructorNode constructor, IMethodSymbol target)
            {
                if (conflictingConstructorInitializers.Contains(constructor))
                    return;
                if (bindings.ConstructorInitializers.TryGetValue(constructor, out var previous)
                    && !SameInitializationMethod(previous, target))
                {
                    conflictingConstructorInitializers.Add(constructor);
                    bindings.ConstructorInitializers.Remove(constructor);
                    return;
                }
                bindings.ConstructorInitializers[constructor] = target;
            }
            var events = new Dictionary<ISymbol, EventDefinitionNode>(SymbolEqualityComparer.Default);
            foreach (var declaration in root.DescendantNodes().OfType<EventFieldDeclarationSyntax>())
            {
                foreach (var trivia in declaration.GetLeadingTrivia())
                {
                    if (!TryReadMarker(trivia, callablePrefix, out var index)
                        || emittedCallables[index] is not EventDefinitionNode { HasAccessors: false } origin)
                        continue;
                    var symbol = declaration.Declaration.Variables is [var variable]
                        ? model.GetDeclaredSymbol(variable) as IEventSymbol : null;
                    if (symbol?.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } type
                        || HasErrorType(type) || HasErrorType(invoke.ReturnType)
                        || invoke.Parameters.Any(parameter => HasErrorType(parameter.Type))
                        || HasInvalidEventOwner(symbol.ContainingType)
                        || DiagnosticsIn(model, declaration.Span).Any(diagnostic =>
                            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                        || symbol.ContainingType.GetMembers(symbol.Name).Length != 1
                        || bindings.EventTypes.TryGetValue(origin, out var previous) && !SameEventType(previous, type))
                    {
                        conflictingEvents.Add(origin);
                        bindings.EventTypes.Remove(origin);
                        continue;
                    }
                    events[symbol.OriginalDefinition] = origin;
                    if (!conflictingEvents.Contains(origin))
                        bindings.EventTypes[origin] = type;
                }
            }
            foreach (var declaration in root.DescendantNodes().Where(node =>
                         node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or DelegateDeclarationSyntax))
            {
                foreach (var trivia in declaration.GetLeadingTrivia())
                {
                    if (TryReadMarker(trivia, callablePrefix, out var index)
                        && (model.GetDeclaredSymbol(declaration) switch
                        {
                            IMethodSymbol declared => declared,
                            INamedTypeSymbol { DelegateInvokeMethod: { } invoke } => invoke,
                            _ => null
                        }) is { } method)
                    {
                        var origin = emittedCallables[index];
                        if (origin is not DelegateDefinitionNode)
                        {
                            methods[method] = origin;
                            bindings.SourceDeclarations[method] = origin;
                        }
                        if (initializationIds.TryGetValue(origin, out var functionId))
                        {
                            bindings.InitializationFunctions[method.OriginalDefinition] = functionId;
                            if (!HasAuthoritativeSourceDispatch(method, null, nonVirtualDispatch: false))
                                bindings.FunctionsRequiringDispatchProof.Add(functionId);
                            if (!bindings.InitializationBodies.TryGetValue(functionId, out var bodies))
                                bindings.InitializationBodies[functionId] = bodies = [];
                            if (model.GetOperation(declaration) is not { } body
                                || DiagnosticsIn(model, declaration.Span).Any(diagnostic =>
                                    diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                                bindings.InitializationBodies[functionId] = null;
                            else
                                bodies?.Add(body);
                        }
                        if (origin is ConstructorNode copy && method.ContainingType.IsRecord
                            && method.Parameters is [var original]
                            && SymbolEqualityComparer.Default.Equals(original.Type, method.ContainingType))
                            bindings.RecordCopyConstructors.Add(copy);
                        if (origin is ConstructorNode constructorOrigin
                            && declaration is ConstructorDeclarationSyntax constructorSyntax)
                        {
                            if (model.GetOperation(constructorSyntax) is not IConstructorBodyOperation
                                { Initializer: IExpressionStatementOperation { Operation: IInvocationOperation initializer } }
                                || DiagnosticsIn(model, initializer.Syntax.Span).Any(diagnostic =>
                                    diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                            {
                                conflictingConstructorInitializers.Add(constructorOrigin);
                                bindings.ConstructorInitializers.Remove(constructorOrigin);
                            }
                            else if (bindings.ConstructorInitializers.TryGetValue(constructorOrigin, out var previous)
                                && !SameInitializationMethod(previous, initializer.TargetMethod))
                            {
                                conflictingConstructorInitializers.Add(constructorOrigin);
                                bindings.ConstructorInitializers.Remove(constructorOrigin);
                            }
                            else if (!conflictingConstructorInitializers.Contains(constructorOrigin))
                                bindings.ConstructorInitializers[constructorOrigin] = initializer.TargetMethod;
                        }
                        IReadOnlyList<ParameterNode> parameters = origin switch
                        {
                            FunctionNode function => function.Parameters,
                            MethodNode member => member.Parameters,
                            MethodSignatureNode signature => signature.Parameters,
                            DelegateDefinitionNode definition => definition.Parameters,
                            ConstructorNode constructor => constructor.Parameters,
                            OperatorOverloadNode op => op.Parameters,
                            _ => []
                        };
                        var output = origin switch
                        {
                            FunctionNode function => function.Output,
                            MethodNode member => member.Output,
                            MethodSignatureNode signature => signature.Output,
                            DelegateDefinitionNode definition => definition.Output,
                            OperatorOverloadNode op => op.Output,
                            _ => null
                        };
                        if (output != null)
                        {
                            if (bindings.OutputTypes.TryGetValue(output, out var previous)
                                && !SameType(previous, method.ReturnType))
                            {
                                conflictingOutputs.Add(output);
                                bindings.OutputTypes.Remove(output);
                            }
                            else if (!conflictingOutputs.Contains(output))
                                bindings.OutputTypes[output] = method.ReturnType;
                        }
                        if (parameters.Count != method.Parameters.Length)
                            continue;
                        for (var i = 0; i < parameters.Count; i++)
                        {
                            var parameter = parameters[i];
                            var type = method.Parameters[i].Type;
                            if (bindings.ParameterTypes.TryGetValue(parameter, out var previous)
                                && !SameType(previous, type))
                            {
                                conflictingParameters.Add(parameter);
                                bindings.ParameterTypes.Remove(parameter);
                            }
                            else if (!conflictingParameters.Contains(parameter))
                                bindings.ParameterTypes[parameter] = type;
                        }
                    }
                }
            }
            // Property accessors reached from an initializer (an object-initializer setter, a
            // static property read) resolve to their declarations like methods do. An auto
            // accessor has no body, so it is recorded with no operations rather than none proved.
            // An inline auto-accessor marker (`{ /*m*/get; /*m*/set; }`) is trailing trivia of
            // the token before the accessor.
            foreach (var declaration in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
            {
                foreach (var trivia in declaration.GetFirstToken().GetPreviousToken().TrailingTrivia
                             .Concat(declaration.GetLeadingTrivia()))
                {
                    if (!TryReadMarker(trivia, callablePrefix, out var index)
                        || emittedCallables[index] is not PropertyAccessorNode origin
                        || !initializationIds.TryGetValue(origin, out var functionId)
                        || model.GetDeclaredSymbol(declaration) is not IMethodSymbol accessor)
                        continue;
                    bindings.InitializationFunctions[accessor.OriginalDefinition] = functionId;
                    if (!bindings.InitializationBodies.TryGetValue(functionId, out var bodies))
                        bindings.InitializationBodies[functionId] = bodies = [];
                    if (DiagnosticsIn(model, declaration.Span).Any(diagnostic =>
                            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                        bindings.InitializationBodies[functionId] = null;
                    else if (declaration.Body != null || declaration.ExpressionBody != null)
                    {
                        if (model.GetOperation(declaration) is { } body)
                            bodies?.Add(body);
                        else
                            bindings.InitializationBodies[functionId] = null;
                    }
                }
            }

            var starts = new Dictionary<int, int>();
            var ranges = new List<(int Start, int End, AstNode Node)>();
            foreach (var trivia in root.DescendantTrivia())
            {
                if (TryReadMarker(trivia, expressionPrefix, out var startIndex))
                    starts[startIndex] = trivia.Span.End;
                else if (TryReadMarker(trivia, expressionEndPrefix, out var endIndex))
                    ranges.Add((starts[endIndex], trivia.SpanStart, expressions[endIndex]));
            }
            ranges.Sort((left, right) => (left.End - left.Start).CompareTo(right.End - right.Start));
            var origins = new ExpressionOriginIndex(ranges);

            if (bindInitialization)
                BindInitializations();
            // This narrow initializer proof is independent of the header-only
            // signature projection gate. No other generated operations gain authority.
            if (initializerCreations.Count > 0 && symbols.Length == 0
                && !HasCompilationErrors(compilation))
            {
                var objectConstructor = compilation.GetSpecialType(SpecialType.System_Object)
                    .InstanceConstructors.SingleOrDefault(constructor => constructor.Parameters.Length == 0);
                foreach (var clause in root.DescendantNodes().OfType<EqualsValueClauseSyntax>())
                {
                    var value = model.GetOperation(clause) switch
                    {
                        IFieldInitializerOperation field => field.Value,
                        IPropertyInitializerOperation property => property.Value,
                        _ => null
                    };
                    if (value is not IObjectCreationOperation { Constructor: { } constructor, Initializer: null } creation
                        || creation.Arguments.Length != 0
                        || !SymbolEqualityComparer.Default.Equals(constructor, objectConstructor)
                        || SyntaxOrigin(creation.Syntax) is not NewExpressionNode origin
                        || !initializerCreations.Contains(origin))
                        continue;
                    bindings.StaticInitializerConstructors[origin] = EffectResolverKey.FromBoundReceiver(
                        Binding.Metadata.MetadataBinderResult.ToNominalBoundType(constructor.ContainingType),
                        constructor.MetadataName, [], EffectMemberKind.Constructor);
                }
            }
            if (!bindInitialization && !bindExpressions && !bindNullableUnwrapping
                && !bindNullGuards && !bindMemberTypes && !bindEvents
                && !bindInterpolation && additions.Length == 0)
                return;

            // Proof contexts are scoped to the member whose body is being walked: an
            // error confined to another member's body cannot change this body's binding.
            var bindsProofs = bindMemberTypes || bindNullGuards || bindInterpolation || additions.Length > 0;
            var errorState = ErrorStateOf(compilation);
            bool validProofContext = false, validMemberContext = false, validDispatchContext = false;
            var selectedDispatchesInTree = new HashSet<TextSpan>();
            var selectedInTree = new HashSet<TextSpan>();
            var selectedNullGuardsInTree = new HashSet<BinaryOperationNode>(ReferenceEqualityComparer.Instance);
            var terminatingGuardsInTree = new HashSet<BinaryOperationNode>(ReferenceEqualityComparer.Instance);
            var stableMembersInTree = new HashSet<ExpressionNode>(ReferenceEqualityComparer.Instance);
            var selectedMembersInTree = new HashSet<FieldAccessNode>(ReferenceEqualityComparer.Instance);
            var selectedGettersInTree = new HashSet<FieldAccessNode>(ReferenceEqualityComparer.Instance);
            var selectedMethodGroupCallsInTree = new HashSet<ExpressionCallNode>(ReferenceEqualityComparer.Instance);
            var selectedMetadataCallsInTree = new HashSet<ExpressionCallNode>(ReferenceEqualityComparer.Instance);
            var selectedEventUsesInTree = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
            var selectedInterpolationFormatsInTree = new HashSet<InterpolatedStringExpressionNode>(ReferenceEqualityComparer.Instance);
            var selectedAdditionsInTree = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
            var selectedStaticFieldReadsInTree = new HashSet<ReferenceNode>(ReferenceEqualityComparer.Instance);

            foreach (var declaration in root.DescendantNodes().Where(node =>
                         node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax
                             or EqualsValueClauseSyntax or ArrowExpressionClauseSyntax))
            {
                if (model.GetOperation(declaration) is not { } body)
                    continue;
                validDispatchContext = errorState.AdmitsProofsIn(declaration);
                validProofContext = bindsProofs && validDispatchContext;
                validMemberContext = bindMemberTypes && validProofContext;
                var pending = new Stack<IOperation>();
                pending.Push(body);
                while (pending.TryPop(out var operation))
                {
                    // Opaque C# has no native expression/effect authority. Its callers
                    // still pass through the normal Unknown-call enforcement.
                    if (Origin(operation) is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode)
                        continue;
                    foreach (var child in operation.ChildOperations)
                        pending.Push(child);
                    if (validProofContext && additions.Length > 0)
                    {
                        if (operation is IFieldReferenceOperation { Field.IsStatic: true, Field.IsConst: false } fieldRead
                            && SymbolEqualityComparer.Default.Equals(fieldRead.Field.ContainingAssembly, compilation.Assembly)
                            && Origin(operation) is ReferenceNode reference)
                        {
                            var owner = bindings.InitializationClasses.GetValueOrDefault(
                                fieldRead.Field.ContainingType.OriginalDefinition);
                            selectedStaticFieldReadsInTree.Add(reference);
                            if (conflictingStaticFieldReads.Contains(reference)
                                || bindings.StaticFieldInitializations.TryGetValue(reference, out var previousOwner)
                                    && !ReferenceEquals(previousOwner, owner))
                            {
                                conflictingStaticFieldReads.Add(reference);
                                owner = null;
                            }
                            bindings.StaticFieldInitializations[reference] = owner;
                        }
                        if (Origin(operation) is { } addition && IsAddition(addition)
                            && operation is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add }
                                or ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add })
                        {
                            IReadOnlyList<InterpolationFormatting> formats = operation switch
                            {
                                IBinaryOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_String } binary =>
                                    [ConcatenationOperand(binary.LeftOperand), ConcatenationOperand(binary.RightOperand)],
                                ICompoundAssignmentOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_String } concatCompound =>
                                    [ConcatenationOperand(concatCompound.Target), ConcatenationOperand(concatCompound.Value)],
                                _ => []
                            };
                            if (bindings.AdditionFormats.TryGetValue(addition, out var previous)
                                && (previous.Count != formats.Count
                                    || previous.Zip(formats).Any(pair => !SameFormatting(pair.First, pair.Second))))
                            {
                                conflictingAdditions.Add(addition);
                                bindings.AdditionFormats.Remove(addition);
                            }
                            else if (!conflictingAdditions.Contains(addition))
                            {
                                selectedAdditionsInTree.Add(addition);
                                bindings.AdditionFormats[addition] = formats;
                            }
                            if (operation is IBinaryOperation selected)
                                AddMethod(operation, selected.OperatorMethod);
                            else if (operation is ICompoundAssignmentOperation selectedCompound)
                            {
                                AddMethod(operation, selectedCompound.OperatorMethod);
                                AddMethod(operation, selectedCompound.InConversion.MethodSymbol);
                                AddMethod(operation, selectedCompound.OutConversion.MethodSymbol);
                            }
                        }
                        if (operation is IConversionOperation { OperatorMethod: { } conversion })
                            for (var parent = operation.Parent; parent != null; parent = parent.Parent)
                                if (parent is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add }
                                    or ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add })
                                {
                                    AddMethod(operation, conversion);
                                    break;
                                }
                    }
                    if (validProofContext && operation is IInterpolatedStringOperation
                        { Type.SpecialType: SpecialType.System_String } interpolation
                        && Origin(operation) is InterpolatedStringNode interpolated)
                    {
                        var holes = interpolated.Parts.OfType<InterpolatedStringExpressionNode>()
                            .Where(hole => hole.Intent != InterpolationPartIntent.LiteralPlaceholder).ToArray();
                        var formatted = interpolation.Parts.OfType<IInterpolationOperation>().ToArray();
                        if (holes.Length == formatted.Length)
                            for (var index = 0; index < holes.Length; index++)
                            {
                                var hole = holes[index];
                                var selection = InterpolationOperand(formatted[index]);
                                if (bindings.InterpolationFormats.TryGetValue(hole, out var previous)
                                    && !SameFormatting(previous, selection))
                                {
                                    conflictingInterpolationFormats.Add(hole);
                                    bindings.InterpolationFormats.Remove(hole);
                                }
                                else if (!conflictingInterpolationFormats.Contains(hole))
                                {
                                    selectedInterpolationFormatsInTree.Add(hole);
                                    bindings.InterpolationFormats[hole] = selection;
                                }
                            }
                    }
                    if (operation is IEventReferenceOperation eventReference
                        && Origin(operation) is { } eventUse
                        && events.TryGetValue(eventReference.Event.OriginalDefinition, out var eventOrigin)
                        && bindings.EventTypes.ContainsKey(eventOrigin)
                        && eventReference.Event.Type is INamedTypeSymbol { DelegateInvokeMethod: not null } eventType
                        && !DiagnosticsIn(model, operation.Syntax.Span).Any(diagnostic =>
                            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                    {
                        var selection = new EventStorageSelection(eventOrigin, eventType,
                            operation.Parent is IEventAssignmentOperation);
                        if (bindings.EventStorageUses.TryGetValue(eventUse, out var previous)
                            && (!ReferenceEquals(previous.Declaration, eventOrigin)
                                || previous.IsSubscription != selection.IsSubscription
                                || !SameEventType(previous.DelegateType, eventType)))
                        {
                            conflictingEventUses.Add(eventUse);
                            bindings.EventStorageUses.Remove(eventUse);
                        }
                        else if (!conflictingEventUses.Contains(eventUse))
                        {
                            selectedEventUsesInTree.Add(eventUse);
                            bindings.EventStorageUses[eventUse] = selection;
                        }
                    }
                    if (validProofContext && bindNullGuards)
                        AddStableMemberRead(operation);
                    if (validMemberContext)
                    {
                        AddMetadataMemberType(operation);
                        if (operation is IInvocationOperation metadataInvocation)
                        {
                            AddMetadataMethodGroupCall(metadataInvocation);
                            AddContextualMetadataCall(metadataInvocation);
                        }
                    }
                    if (bindNullGuards && operation is IConditionalOperation
                            { Syntax: IfStatementSyntax conditionalSyntax }
                        && SyntaxOrigin(conditionalSyntax.Condition) is BinaryOperationNode
                            { Operator: BinaryOperator.Equal or BinaryOperator.Or } guardCondition)
                    {
                        var flow = validProofContext
                            ? model.AnalyzeControlFlow(conditionalSyntax.Statement) : null;
                        if (flow is { Succeeded: true, EndPointIsReachable: false }
                            && flow.ExitPoints.All(exit => exit is ReturnStatementSyntax or ThrowStatementSyntax)
                            && !conflictingGuardTerminations.Contains(guardCondition))
                        {
                            terminatingGuardsInTree.Add(guardCondition);
                            bindings.TerminatingNullGuardConditions.Add(guardCondition);
                        }
                        else
                        {
                            conflictingGuardTerminations.Add(guardCondition);
                            bindings.TerminatingNullGuardConditions.Remove(guardCondition);
                        }
                    }
                    if (bindNullGuards && operation is IBinaryOperation binaryGuard
                        && Origin(operation) is BinaryOperationNode
                            { Operator: BinaryOperator.Equal or BinaryOperator.Or } guard)
                    {
                        if (validProofContext && binaryGuard is
                            {
                                OperatorMethod: null,
                                IsLifted: false,
                                Type.SpecialType: SpecialType.System_Boolean
                            }
                            && (binaryGuard.OperatorKind, guard.Operator) is
                                (BinaryOperatorKind.Equals, BinaryOperator.Equal)
                                or (BinaryOperatorKind.ConditionalOr, BinaryOperator.Or)
                            && !HasUserDefinedConversion(binaryGuard.LeftOperand)
                            && !HasUserDefinedConversion(binaryGuard.RightOperand)
                            && !conflictingNullGuards.Contains(guard))
                        {
                            selectedNullGuardsInTree.Add(guard);
                            bindings.BuiltInNullGuardOperations.Add(guard);
                        }
                        else
                        {
                            conflictingNullGuards.Add(guard);
                            bindings.BuiltInNullGuardOperations.Remove(guard);
                        }
                    }
                    if (operation is IInvocationOperation sourceInvocation
                        && sourceInvocation.TargetMethod.MethodKind == MethodKind.Ordinary
                        && sourceInvocation.TargetMethod.Locations.Any(location => location.IsInSource)
                        && Origin(operation) is ExpressionCallNode or CallExpressionNode or CallStatementNode)
                    {
                        var site = Origin(operation)!.Span;
                        var dispatchedMethod = sourceInvocation.TargetMethod;
                        selectedDispatchesInTree.Add(site);
                        var selection = new SourceDispatchSelection(dispatchedMethod,
                            methods.GetValueOrDefault(dispatchedMethod.OriginalDefinition),
                            HasAuthoritativeSourceDispatch(dispatchedMethod, sourceInvocation.Instance?.Type,
                                nonVirtualDispatch: !sourceInvocation.IsVirtual), validDispatchContext);
                        if (bindings.SourceDispatches.TryGetValue(site, out var previous))
                            bindings.SourceDispatches[site] = previous is null
                                || !ReferenceEquals(previous.Declaration, selection.Declaration)
                                    ? null : selection with
                                    {
                                        IsAuthoritative = selection.IsAuthoritative && previous.IsAuthoritative,
                                        HasCompleteProofContext = selection.HasCompleteProofContext
                                            && previous.HasCompleteProofContext
                                    };
                        else
                            bindings.SourceDispatches[site] = selection;
                    }
                    if (operation is IInvocationOperation invocation
                        && (bindExpressions || companions.Count > 0 || invocation.TargetMethod.IsExtensionMethod
                            || invocation.TargetMethod is { IsStatic: true, ContainingType.IsGenericType: true }))
                        AddCall(invocation, invocation.TargetMethod, invocation.Arguments);
                    if (operation is IConversionOperation nullableConversion)
                        AddMethod(operation, NullableUnwrappingGetter(nullableConversion));
                    if (!bindExpressions)
                        continue;
                    if (operation is IObjectCreationOperation { Constructor: { } constructor } creation)
                        AddCall(creation, constructor, creation.Arguments);
                    if (operation is IDelegateCreationOperation
                        {
                            IsImplicit: true, Target: IAnonymousFunctionOperation,
                            Type: INamedTypeSymbol { DelegateInvokeMethod: not null } target
                        } delegateCreation
                        && Origin(delegateCreation) is LambdaExpressionNode lambda
                        && !DiagnosticsIn(model, delegateCreation.Syntax.Span).Any(diagnostic =>
                            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                    {
                        if (bindings.LambdaTargets.TryGetValue(lambda, out var previous)
                            && !SameType(previous, target))
                        {
                            conflictingLambdas.Add(lambda);
                            bindings.LambdaTargets.Remove(lambda);
                        }
                        else if (!conflictingLambdas.Contains(lambda))
                            bindings.LambdaTargets[lambda] = target;
                    }
                    var method = operation switch
                    {
                        IConversionOperation conversion => conversion.OperatorMethod,
                        IBinaryOperation binary => binary.OperatorMethod,
                        IUnaryOperation unary => unary.OperatorMethod,
                        ICompoundAssignmentOperation assignment => assignment.OperatorMethod,
                        IIncrementOrDecrementOperation increment => increment.OperatorMethod,
                        _ => null
                    };
                    AddMethod(operation, method);
                    if (operation is ICompoundAssignmentOperation compound)
                    {
                        AddMethod(operation, compound.InConversion.MethodSymbol);
                        AddMethod(operation, compound.OutConversion.MethodSymbol);
                    }
                    if (operation is IBinaryOperation
                        {
                            OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr,
                            OperatorMethod: { } logical
                        } conditional
                        && logical.Parameters[0].Type is INamedTypeSymbol operand)
                    {
                        var truthName = conditional.OperatorKind == BinaryOperatorKind.ConditionalAnd
                            ? "op_False" : "op_True";
                        foreach (var truth in operand.GetMembers(truthName).OfType<IMethodSymbol>().Where(truth =>
                                     truth.Parameters.Length == 1
                                     && SymbolEqualityComparer.Default.Equals(truth.Parameters[0].Type, operand)))
                            AddMethod(operation, truth);
                    }
                }
            }
            foreach (var guard in ranges.Select(range => range.Node).OfType<BinaryOperationNode>()
                         .Where(guard => guard.Operator is BinaryOperator.Equal or BinaryOperator.Or))
            {
                if (!selectedNullGuardsInTree.Contains(guard))
                {
                    conflictingNullGuards.Add(guard);
                    bindings.BuiltInNullGuardOperations.Remove(guard);
                }
                if (!terminatingGuardsInTree.Contains(guard))
                {
                    conflictingGuardTerminations.Add(guard);
                    bindings.TerminatingNullGuardConditions.Remove(guard);
                }
            }
            foreach (var use in ranges.Select(range => range.Node)
                         .Where(use => !selectedEventUsesInTree.Contains(use)))
            {
                conflictingEventUses.Add(use);
                bindings.EventStorageUses.Remove(use);
            }
            foreach (var hole in ranges.Select(range => range.Node).OfType<InterpolatedStringNode>()
                         .SelectMany(interpolated => interpolated.Parts.OfType<InterpolatedStringExpressionNode>())
                         .Where(hole => hole.Intent != InterpolationPartIntent.LiteralPlaceholder
                             && !selectedInterpolationFormatsInTree.Contains(hole)))
            {
                conflictingInterpolationFormats.Add(hole);
                bindings.InterpolationFormats.Remove(hole);
            }
            foreach (var addition in ranges.Select(range => range.Node).Where(IsAddition)
                         .Where(addition => !selectedAdditionsInTree.Contains(addition)))
            {
                conflictingAdditions.Add(addition);
                bindings.AdditionFormats.Remove(addition);
            }
            foreach (var call in ranges.Select(range => range.Node).Where(node =>
                         node is ExpressionCallNode or CallExpressionNode or CallStatementNode))
                if (!selectedDispatchesInTree.Contains(call.Span) && bindings.SourceDispatches.ContainsKey(call.Span))
                    bindings.SourceDispatches[call.Span] = null;
            foreach (var reference in ranges.Select(range => range.Node).OfType<ReferenceNode>()
                         .Where(reference => !selectedStaticFieldReadsInTree.Contains(reference)))
            {
                conflictingStaticFieldReads.Add(reference);
                if (bindings.StaticFieldInitializations.ContainsKey(reference))
                    bindings.StaticFieldInitializations[reference] = null;
            }
            foreach (var member in ranges.Select(range => range.Node).OfType<FieldAccessNode>()
                         .Where(member => !selectedMembersInTree.Contains(member)))
            {
                conflictingMembers.Add(member);
                bindings.MetadataMemberTypes.Remove(member);
            }
            foreach (var member in ranges.Select(range => range.Node).OfType<ExpressionNode>()
                         .Where(member => member is ReferenceNode or FieldAccessNode)
                         .Where(member => !stableMembersInTree.Contains(member)))
            {
                conflictingStableMembers.Add(member);
                bindings.StableMemberReads.Remove(member);
            }
            foreach (var member in ranges.Select(range => range.Node).OfType<FieldAccessNode>()
                         .Where(member => !selectedGettersInTree.Contains(member)))
            {
                conflictingGetters.Add(member);
                bindings.MetadataGetters.Remove(member);
            }
            foreach (var call in ranges.Select(range => range.Node).OfType<ExpressionCallNode>()
                         .Where(call => !selectedMethodGroupCallsInTree.Contains(call)))
            {
                conflictingMethodGroupCalls.Add(call);
                bindings.MetadataMethodGroupCalls.Remove(call);
            }
            foreach (var call in ranges.Select(range => range.Node).OfType<ExpressionCallNode>()
                         .Where(call => !selectedMetadataCallsInTree.Contains(call)))
            {
                conflictingMetadataCalls.Add(call);
                bindings.ContextualMetadataCalls.Remove(call);
            }
            if (!bindExpressions && !bindSourceExtensions)
                return;

            foreach (var syntax in root.DescendantNodes().Where(node =>
                         node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax))
            {
                var info = model.GetSymbolInfo(syntax);
                var candidates = info.CandidateSymbols.OfType<IMethodSymbol>();
                if (syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }
                    && SyntaxOrigin(syntax) is ExpressionCallNode sourceCall)
                {
                    var owner = model.GetSymbolInfo(access.Expression);
                    if (candidates.Cast<ISymbol>().Concat(info.Symbol is null ? [] : [info.Symbol])
                        .Concat(owner.CandidateSymbols).Concat(owner.Symbol is null ? [] : [owner.Symbol])
                        .Any(symbol => symbol is not INamespaceSymbol && symbol.Locations.Any(location =>
                            location.IsInSource && location.SourceTree != tree)))
                        bindings.ForeignSourceCallSites.Add(sourceCall.Span);
                }
                if (syntax is InvocationExpressionSyntax
                    && SyntaxOrigin(syntax) is ExpressionCallNode { TargetExpression: FieldAccessNode } extensionCall
                    && candidates.Prepend(info.Symbol as IMethodSymbol).Any(method => method is
                        { IsExtensionMethod: true } && method.Locations.Any(location => location.IsInSource)))
                    bindings.SourceExtensionCalls.Add(extensionCall.Span);
                // A reduced extension candidate (`color.Take(value)`) is declared as its static form.
                if (!candidates.Any(candidate => methods.ContainsKey((candidate.ReducedFrom ?? candidate).OriginalDefinition))
                    || !DiagnosticsIn(model, syntax.Span).Any(diagnostic => diagnostic.Id == "CS0121"))
                    continue;
                var arguments = syntax switch
                {
                    InvocationExpressionSyntax invocation => invocation.ArgumentList.Arguments,
                    ObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments ?? default,
                    _ => default
                };
                var sites = arguments.Select(argument => SyntaxOrigin(argument.Expression))
                    .OfType<AstNode>().Where(argumentOwners.ContainsKey)
                    .Select(argument => argumentOwners[argument].Call).Distinct().ToArray();
                if (sites.Length == 1)
                    bindings.AmbiguousCalls.Add(sites[0]);
            }

            foreach (var site in ranges.Select(range => range.Node)
                         .SelectMany(node => node is ExpressionCallNode or CallExpressionNode
                                 or CallStatementNode or NewExpressionNode
                             ? new[] { node.Span }
                             : argumentOwners.TryGetValue(node, out var owner) ? [owner.Call] : [])
                         .Distinct()
                         .Where(site => !selectedInTree.Contains(site)))
            {
                conflictingCalls.Add(site);
                if (bindings.SelectedCalls.TryGetValue(site, out var previous)
                    && (previous.UsesUserDefinedConversion || previous.UsesAnonymousFunctionConversion))
                    bindingFailures.Add("An implicit-conversion source call could not be bound in every active preprocessor configuration.");
                bindings.SelectedCalls.Remove(site);
            }

            AstNode? SyntaxOrigin(SyntaxNode syntax) => origins.Find(syntax.SpanStart, syntax.Span.End);

            AstNode? Origin(IOperation operation) => SyntaxOrigin(operation.Syntax);

            static bool HasUserDefinedConversion(IOperation operand)
            {
                while (operand is IConversionOperation conversion)
                {
                    if (conversion.OperatorMethod is not null)
                        return true;
                    operand = conversion.Operand;
                }
                return false;
            }

            void AddStableMemberRead(IOperation operation)
            {
                if (operation is not IPropertyReferenceOperation
                    {
                        Property:
                        {
                            IsStatic: false, IsIndexer: false, SetMethod: null,
                            ReturnsByRef: false, ReturnsByRefReadonly: false,
                            Type.SpecialType: SpecialType.System_String,
                            GetMethod: { IsVirtual: false, IsOverride: false, IsAbstract: false }
                        } property,
                        Instance: { Type: INamedTypeSymbol { IsReferenceType: true } receiver }
                    } read
                    || Origin(read) is not ExpressionNode origin
                    || conflictingStableMembers.Contains(origin)
                    || !SymbolEqualityComparer.Default.Equals(receiver, property.ContainingType)
                    || !bindings.InitializationClasses.TryGetValue(
                        property.ContainingType.OriginalDefinition, out var owner)
                    || owner.IsPartial || owner.PreprocessorBlocks.Count != 0
                    || bindings.Initializations.GetValueOrDefault(owner)?.Instance is null
                    || property.ContainingType.DeclaringSyntaxReferences.Length != 1
                    || property.DeclaringSyntaxReferences is not [var declaration]
                    || declaration.GetSyntax() is not PropertyDeclarationSyntax
                    {
                        Parent: TypeDeclarationSyntax containing,
                        AccessorList.Accessors: [ { Body: null, ExpressionBody: null } ]
                    } syntax
                    || property.ContainingType.GetMembers().OfType<IFieldSymbol>()
                        .SingleOrDefault(field => SymbolEqualityComparer.Default.Equals(
                            field.AssociatedSymbol, property)) is not
                        { IsImplicitlyDeclared: true, IsReadOnly: true }
                    || read.Instance is not (IInstanceReferenceOperation
                            { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
                        or IParameterReferenceOperation or ILocalReferenceOperation)
                    || read.Syntax.Ancestors().Any(ancestor =>
                        ancestor is ConstructorDeclarationSyntax or AnonymousFunctionExpressionSyntax
                            or LocalFunctionStatementSyntax)
                    || origin is not (ReferenceNode or FieldAccessNode
                        { Target: ReferenceNode or ThisExpressionNode }))
                    return;

                var properties = containing.Members.OfType<PropertyDeclarationSyntax>().ToArray();
                var index = Array.IndexOf(properties, syntax);
                if (properties.Length != owner.Properties.Count || index < 0
                    || owner.Properties[index] is not
                        { IsAutoProperty: true, Getter: not null, Setter: null, Initer: null } native
                    || native.Name != property.Name)
                    return;

                var selection = new StableMemberSelection(owner, native,
                    read.Syntax is ExpressionSyntax expression
                        && model.GetTypeInfo(expression).Nullability.FlowState == NullableFlowState.NotNull);
                if (bindings.StableMemberReads.TryGetValue(origin, out var previous)
                    && previous != selection)
                {
                    conflictingStableMembers.Add(origin);
                    bindings.StableMemberReads.Remove(origin);
                }
                else
                {
                    stableMembersInTree.Add(origin);
                    bindings.StableMemberReads[origin] = selection;
                }
            }

            void AddMetadataMethodGroupCall(IInvocationOperation invocation)
            {
                if (invocation.IsImplicit
                    || invocation.TargetMethod is not { IsStatic: false, MethodKind: MethodKind.Ordinary } method
                    || method.IsExtensionMethod
                    || method.Locations.Any(location => location.IsInSource)
                    || invocation.Instance is not IParameterReferenceOperation
                        { Type: INamedTypeSymbol receiver }
                    || receiver.Locations.Any(location => location.IsInSource)
                    || HasErrorType(receiver) || HasErrorType(method.ReturnType)
                    || Origin(invocation) is not ExpressionCallNode
                        { TargetExpression: FieldAccessNode { Target: ReferenceNode } } origin
                    || origin.Arguments.Count == 0
                    || invocation.Arguments.Length != origin.Arguments.Count
                    || conflictingMethodGroupCalls.Contains(origin))
                    return;

                var callbacks = new Dictionary<ExpressionNode, FunctionNode>(ReferenceEqualityComparer.Instance);
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.ArgumentKind != ArgumentKind.Explicit
                        || argument.Value is not IDelegateCreationOperation
                        {
                            IsImplicit: true,
                            Target: IMethodReferenceOperation { Instance: null, Method.IsStatic: true } reference
                        }
                        || Origin(reference) is not ReferenceNode argumentOrigin
                        || !origin.Arguments.Contains(argumentOrigin)
                        || !methods.TryGetValue(reference.Method.OriginalDefinition, out var declaration)
                        || declaration is not FunctionNode callback
                        || !module.Functions.Contains(callback)
                        || !callbacks.TryAdd(argumentOrigin, callback))
                        return;
                }

                var selection = new MetadataMethodGroupCallSelection(method, receiver,
                    origin.Arguments.Select(argument => callbacks[argument]).ToArray());
                selectedMethodGroupCallsInTree.Add(origin);
                if (bindings.MetadataMethodGroupCalls.TryGetValue(origin, out var previous)
                    && (!SymbolEqualityComparer.Default.Equals(previous.Method, method)
                        || !SameType(previous.ReceiverType, receiver)
                        || !previous.Callbacks.SequenceEqual(selection.Callbacks)))
                {
                    conflictingMethodGroupCalls.Add(origin);
                    bindings.MetadataMethodGroupCalls.Remove(origin);
                }
                else
                    bindings.MetadataMethodGroupCalls[origin] = selection;
            }

            void AddContextualMetadataCall(IInvocationOperation invocation)
            {
                if (invocation.IsImplicit || invocation.Syntax is not InvocationExpressionSyntax
                    { Expression: MemberAccessExpressionSyntax access } syntax
                    || Origin(invocation) is not ExpressionCallNode { TargetExpression: FieldAccessNode member } origin
                    || model.GetSymbolInfo(syntax).Symbol is not IMethodSymbol
                        { IsExtensionMethod: true } method
                    || method.ReducedFrom is null && !method.IsStatic
                    || method.Locations.Any(location => location.IsInSource)
                    || HasErrorType(method.ReturnType) || method.Parameters.Any(parameter => HasErrorType(parameter.Type))
                    || conflictingMetadataCalls.Contains(origin))
                    return;

                var receiver = model.GetTypeInfo(access.Expression).Type;
                if (receiver is null || HasErrorType(receiver)
                    || !method.Parameters.Any(parameter => parameter.Type.TypeKind == TypeKind.Delegate)
                        && ((!IsClosedSourceContainer(receiver)
                                && !IsClosedSourceContainer(method.ReturnType)
                                && !method.Parameters.Any(parameter => IsClosedSourceContainer(parameter.Type)))
                            || HasOpenType(receiver) || HasOpenType(method.ReturnType)
                            || method.TypeArguments.Any(HasOpenType)
                            || method.Parameters.Any(parameter => HasOpenType(parameter.Type))))
                    return;

                var mappings = new List<Binding.Metadata.MetadataCallArgument>();
                foreach (var argument in invocation.Arguments)
                {
                    if (argument is { ArgumentKind: ArgumentKind.DefaultValue, IsImplicit: true })
                        continue;
                    if (argument.ArgumentKind != ArgumentKind.Explicit || argument.Parameter is not { } parameter
                        || HasUserDefinedConversion(argument.Value)
                        || Origin(argument.Value) is not ExpressionNode supplied
                        || !argumentOwners.TryGetValue(supplied, out var owner) || owner.Call != origin.Span)
                        return;
                    if (owner.Index == -1)
                    {
                        if (method.ReducedFrom is null || parameter.Ordinal != 0
                            || !ReferenceEquals(member.Target, supplied))
                            return;
                        continue;
                    }
                    if (owner.Index < 0 || owner.Index >= origin.Arguments.Count
                        || !ReferenceEquals(origin.Arguments[owner.Index], supplied))
                        return;
                    var selectedParameter = method.Parameters[parameter.Ordinal - (method.ReducedFrom is null ? 0 : 1)];
                    mappings.Add(new Binding.Metadata.MetadataCallArgument(
                        owner.Index, selectedParameter, selectedParameter.Type, IsExpandedParams: false));
                }
                if (mappings.Count != origin.Arguments.Count
                    || mappings.Select(mapping => mapping.ArgumentIndex).Distinct().Count() != origin.Arguments.Count)
                    return;
                var selection = Binding.Metadata.MetadataBinderResult.CreateResolved(method,
                    mappings.OrderBy(mapping => mapping.ArgumentIndex).ToArray()) with { ReceiverType = receiver };
                selectedMetadataCallsInTree.Add(origin);
                if (bindings.ContextualMetadataCalls.TryGetValue(origin, out var previous)
                    && (!SymbolEqualityComparer.Default.Equals(
                            previous.Symbol!.ReducedFrom?.OriginalDefinition ?? previous.Symbol.OriginalDefinition,
                            method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition)
                        || (previous.Symbol.ReducedFrom is null) != (method.ReducedFrom is null)
                        || previous.Symbol.TypeArguments.Length != method.TypeArguments.Length
                        || !previous.Symbol.TypeArguments.Zip(method.TypeArguments).All(pair =>
                            SameContextualType(pair.First, pair.Second))
                        || previous.ReceiverType is not { } previousReceiver
                        || !SameContextualType(previousReceiver, receiver)
                        || !SameContextualType(previous.Symbol.ReturnType, method.ReturnType)
                        || previous.Symbol.Parameters.Length != method.Parameters.Length
                        || !previous.Symbol.Parameters.Zip(method.Parameters).All(pair =>
                            pair.First.RefKind == pair.Second.RefKind
                            && SameContextualType(pair.First.Type, pair.Second.Type))
                        || previous.Arguments.Count != selection.Arguments.Count
                        || !previous.Arguments.Zip(selection.Arguments).All(pair =>
                            pair.First.ArgumentIndex == pair.Second.ArgumentIndex
                            && pair.First.Parameter.Ordinal == pair.Second.Parameter.Ordinal
                            && SameContextualType(pair.First.TargetType, pair.Second.TargetType))))
                {
                    conflictingMetadataCalls.Add(origin);
                    bindings.ContextualMetadataCalls.Remove(origin);
                }
                else
                    bindings.ContextualMetadataCalls[origin] = selection;
            }

            static bool IsClosedSourceContainer(ITypeSymbol type) =>
                Binding.Metadata.MetadataBinderResult.ToBoundType(type) is Binding.BoundTypes.GenericInstantiationBoundType
                    { TypeArguments: [Binding.BoundTypes.NominalBoundType element] } container
                && NullabilityChecker.IsReferenceContainerDefinition(container.Definition)
                && NullabilityChecker.IsClosedSourceReference(element)
                && element.RoslynSymbol is { IsGenericType: false };

            static bool HasOpenType(ITypeSymbol type) => type is ITypeParameterSymbol
                || type is INamedTypeSymbol named && (named.IsUnboundGenericType
                    || named.TypeArguments.Any(HasOpenType)
                    || named.ContainingType is { } owner && HasOpenType(owner))
                || type is IArrayTypeSymbol array && HasOpenType(array.ElementType);

            static bool SameContextualType(ITypeSymbol left, ITypeSymbol right) =>
                left.TypeKind == right.TypeKind && left.NullableAnnotation == right.NullableAnnotation
                && (left, right) switch
                {
                    (INamedTypeSymbol named, INamedTypeSymbol other) =>
                        SameContextualDefinition(named.OriginalDefinition, other.OriginalDefinition)
                        && (named.ContainingType is null ? other.ContainingType is null
                            : other.ContainingType is { } owner && SameContextualType(named.ContainingType, owner))
                        && named.TypeArguments.Length == other.TypeArguments.Length
                        && named.TypeArguments.Zip(other.TypeArguments).All(pair =>
                            SameContextualType(pair.First, pair.Second)),
                    (IArrayTypeSymbol array, IArrayTypeSymbol other) =>
                        array.Rank == other.Rank && SameContextualType(array.ElementType, other.ElementType),
                    _ => SymbolEqualityComparer.IncludeNullability.Equals(left, right)
                };

            static bool SameContextualDefinition(INamedTypeSymbol left, INamedTypeSymbol right)
            {
                if (left.DeclaringSyntaxReferences.IsEmpty || right.DeclaringSyntaxReferences.IsEmpty)
                    return SymbolEqualityComparer.Default.Equals(left, right);
                // Each configuration has its own source symbols. Compare their
                // actual image declarations, not just equally spelled type names.
                return left.MetadataName == right.MetadataName
                    && Equals(left.ContainingAssembly.Identity, right.ContainingAssembly.Identity)
                    && left.ContainingNamespace.ToDisplayString() == right.ContainingNamespace.ToDisplayString()
                    && left.DeclaringSyntaxReferences.Length == right.DeclaringSyntaxReferences.Length
                    && left.DeclaringSyntaxReferences.Zip(right.DeclaringSyntaxReferences).All(pair =>
                        pair.First.Span == pair.Second.Span
                        && pair.First.SyntaxTree.FilePath == pair.Second.SyntaxTree.FilePath
                        && pair.First.SyntaxTree.GetText().ContentEquals(pair.Second.SyntaxTree.GetText()));
            }

            void BindInitializations()
            {
                var declarations = new Dictionary<INamedTypeSymbol,
                    List<(ClassDefinitionNode Origin, TypeDeclarationSyntax Syntax)>>(SymbolEqualityComparer.Default);
                foreach (var syntax in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol type)
                        continue;
                    foreach (var trivia in syntax.GetLeadingTrivia())
                    {
                        if (!TryReadMarker(trivia, typePrefix, out var index))
                            continue;
                        if (!declarations.TryGetValue(type.OriginalDefinition, out var parts))
                            declarations[type.OriginalDefinition] = parts = [];
                        parts.Add((emittedTypes[index], syntax));
                    }
                }

                foreach (var (type, parts) in declarations)
                    if (parts is [var part] && type.DeclaringSyntaxReferences.Length == 1)
                    {
                        bindings.InitializationClasses[type] = part.Origin;
                        if (part.Syntax is not RecordDeclarationSyntax record)
                            continue;
                        var primary = record.ParameterList?.Parameters.FirstOrDefault() is { } parameter
                            && model.GetDeclaredSymbol(parameter) is IParameterSymbol selectedParameter
                                ? selectedParameter.ContainingSymbol as IMethodSymbol
                                : null;
                        if (record.ParameterList?.Parameters.Count is null or 0)
                        {
                            var candidates = type.InstanceConstructors.Where(constructor =>
                                    constructor.Parameters.Length == 0
                                    && (constructor.IsImplicitlyDeclared
                                        || constructor.DeclaringSyntaxReferences.Any(reference =>
                                            reference.SyntaxTree == tree && reference.Span == record.Span)))
                                .Take(2).ToArray();
                            primary = candidates is [var selected] ? selected : null;
                        }
                        if (part.Origin.RecordPrimaryConstructor is { } primaryOrigin && primary != null)
                        {
                            methods[primary.OriginalDefinition] = primaryOrigin;
                            bindings.SourceDeclarations[primary.OriginalDefinition] = primaryOrigin;
                            bindings.InitializationFunctions[primary.OriginalDefinition] =
                                $"{part.Origin.Name}.{primaryOrigin.Id}";
                            if (record.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>()
                                    .Select(baseType => model.GetOperation(baseType))
                                    .OfType<IInvocationOperation>().Take(2).ToArray() is [var baseCall]
                                && !DiagnosticsIn(model, baseCall.Syntax.Span).Any(diagnostic =>
                                    diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                                SetRecordInitializer(primaryOrigin, baseCall.TargetMethod);
                        }
                        var copies = type.InstanceConstructors.Where(constructor =>
                            constructor.IsImplicitlyDeclared && constructor.Parameters is [var original]
                            && SymbolEqualityComparer.Default.Equals(original.Type, type)).Take(2).ToArray();
                        var copy = copies is [var selectedCopy] ? selectedCopy : null;
                        if (part.Origin.RecordCopyConstructor is { } copyOrigin && copy != null)
                        {
                            methods[copy.OriginalDefinition] = copyOrigin;
                            bindings.SourceDeclarations[copy.OriginalDefinition] = copyOrigin;
                            bindings.InitializationFunctions[copy.OriginalDefinition] =
                                $"{part.Origin.Name}.{copyOrigin.Id}";
                            bindings.RecordCopyConstructors.Add(copyOrigin);
                            var baseCopies = type.BaseType?.InstanceConstructors.Where(constructor =>
                                constructor.Parameters is [var original]
                                && SymbolEqualityComparer.Default.Equals(original.Type, type.BaseType)
                                && compilation.IsSymbolAccessibleWithin(constructor, type)).Take(2).ToArray();
                            if (baseCopies is [var baseCopy])
                                SetRecordInitializer(copyOrigin, baseCopy);
                        }
                    }
                foreach (var (type, parts) in declarations)
                {
                    if (parts is not [var part] || !bindings.InitializationClasses.ContainsKey(type)
                        || part.Origin.IsPartial && !singleModuleContext
                        || DiagnosticsIn(model, Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                            part.Syntax.SpanStart, part.Syntax.OpenBraceToken.SpanStart)).Any(diagnostic =>
                            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                    {
                        foreach (var invalid in parts)
                            bindings.Initializations[invalid.Origin] = new SourceInitialization(
                                null, null, null, type.IsValueType,
                                type.StaticConstructors.Any(constructor => constructor.IsImplicitlyDeclared)
                                || bindings.Initializations.GetValueOrDefault(invalid.Origin)
                                    is { HasImplicitStaticConstructor: true });
                        continue;
                    }
                    var instance = new List<InitializationOperation>();
                    var statics = new List<InitializationOperation>();
                    var validInstance = true;
                    var validStatic = true;
                    foreach (var member in part.Syntax.Members)
                    {
                        switch (member)
                        {
                            case FieldDeclarationSyntax field:
                                foreach (var variable in field.Declaration.Variables)
                                    AddInitializer(variable.Initializer,
                                        field.Modifiers.Any(SyntaxKind.StaticKeyword));
                                break;
                            case PropertyDeclarationSyntax property:
                                AddInitializer(property.Initializer,
                                    property.Modifiers.Any(SyntaxKind.StaticKeyword));
                                break;
                            case ConstructorDeclarationSyntax constructor:
                                if (model.GetDeclaredSymbol(constructor) is not IMethodSymbol selected
                                    || !methods.ContainsKey(selected.OriginalDefinition))
                                {
                                    if (constructor.Modifiers.Any(SyntaxKind.StaticKeyword))
                                        validStatic = false;
                                    else
                                        validInstance = false;
                                }
                                break;
                            case MethodDeclarationSyntax { AttributeLists.Count: 0 }:
                            case OperatorDeclarationSyntax:
                            case ConversionOperatorDeclarationSyntax:
                            case IndexerDeclarationSyntax:
                            case EventDeclarationSyntax:
                            case EventFieldDeclarationSyntax eventField when
                                eventField.Declaration.Variables.All(variable => variable.Initializer == null):
                            case BaseTypeDeclarationSyntax:
                            case DelegateDeclarationSyntax:
                                break;
                            default:
                                validInstance = validStatic = false;
                                break;
                        }
                    }
                    // This is the language's implicit base() call, not a bare-name lookup.
                    // Optional/params-only and ambiguous base overload sets remain unavailable.
                    var baseConstructors = type.BaseType?.InstanceConstructors
                        .Where(constructor => constructor.Parameters.Length == 0
                            && compilation.IsSymbolAccessibleWithin(constructor, type)).Take(2).ToArray();
                    var baseConstructor = baseConstructors is [var selectedBase] ? selectedBase : null;
                    var hasImplicitStaticConstructor = type.StaticConstructors.Any(constructor =>
                        constructor.IsImplicitlyDeclared);
                    if (bindings.Initializations.TryGetValue(part.Origin, out var previous))
                    {
                        hasImplicitStaticConstructor |= previous.HasImplicitStaticConstructor;
                        validInstance &= previous.Instance != null;
                        validStatic &= previous.Static != null;
                        if (previous.Instance != null) instance.AddRange(previous.Instance);
                        if (previous.Static != null) statics.AddRange(previous.Static);
                        if (previous.BaseConstructor == null || baseConstructor == null
                            || !SameInitializationMethod(previous.BaseConstructor, baseConstructor))
                            baseConstructor = null;
                    }
                    bindings.Initializations[part.Origin] = new SourceInitialization(
                        validInstance ? instance : null, validStatic ? statics : null, baseConstructor,
                        type.IsValueType, hasImplicitStaticConstructor);
                    if (part.Origin is { BaseClass: not null, RecordPrimaryConstructor: { Initializer: null } primary }
                        && baseConstructor != null)
                        SetRecordInitializer(primary, baseConstructor);

                    void AddInitializer(EqualsValueClauseSyntax? initializer, bool isStatic)
                    {
                        if (initializer == null)
                            return;
                        var operation = model.GetOperation(initializer) switch
                        {
                            IFieldInitializerOperation field => field.Value,
                            IPropertyInitializerOperation property => property.Value,
                            _ => null
                        };
                        if (operation == null
                            || DiagnosticsIn(model, initializer.Span).Any(diagnostic =>
                                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                        {
                            if (isStatic) validStatic = false;
                            else validInstance = false;
                            return;
                        }
                        (isStatic ? statics : instance).Add(new InitializationOperation(
                            operation, Origin(operation) is not ExpressionNode));
                    }
                }

                // An invalid getter argument does not select a constructor. Preserve the
                // actual C# failure instead of inventing an unknown constructor effect.
                foreach (var syntax in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    if (SyntaxOrigin(syntax) is not NewExpressionNode creation)
                        continue;
                    if (model.GetTypeInfo(syntax.Type).Type is not INamedTypeSymbol type
                        || !declarations.ContainsKey(type.OriginalDefinition))
                    {
                        conflictingSourceConstructors.Add(creation);
                        bindings.SourceConstructors.Remove(creation);
                        continue;
                    }
                    if (model.GetOperation(syntax) is IObjectCreationOperation { Constructor: { } constructor }
                        && !DiagnosticsIn(model, syntax.Span).Any(diagnostic =>
                            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                    {
                        if (bindings.SourceConstructors.TryGetValue(creation, out var previous)
                            && !SameInitializationMethod(previous, constructor))
                        {
                            conflictingSourceConstructors.Add(creation);
                            bindings.SourceConstructors.Remove(creation);
                        }
                        else if (!conflictingSourceConstructors.Contains(creation))
                            bindings.SourceConstructors[creation] = constructor;
                    }
                    else
                    {
                        conflictingSourceConstructors.Add(creation);
                        bindings.SourceConstructors.Remove(creation);
                    }
                    var invalidReads = syntax.ArgumentList?.Arguments
                        .SelectMany(argument => argument.Expression.DescendantNodesAndSelf()
                            .OfType<MemberAccessExpressionSyntax>())
                        .Where(member =>
                        {
                            var info = model.GetSymbolInfo(member);
                            return (info.Symbol == null ? info.CandidateSymbols : [info.Symbol])
                                .OfType<IPropertySymbol>().Any(property =>
                                    declarations.ContainsKey(property.ContainingType.OriginalDefinition));
                        }) ?? [];
                    foreach (var error in invalidReads.SelectMany(member => DiagnosticsIn(model, member.Span))
                                 .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                    {
                        var message = $"Generated C# failed compilation ({error.Id}): {error.GetMessage()}";
                        if (!bindings.SourceConstructionErrors.Any(previous =>
                                previous.Span == creation.Span && previous.Message == message))
                            bindings.SourceConstructionErrors.Add(new Diagnostics.Diagnostic(
                                DiagnosticCode.CodeGenCompilationError, message, creation.Span,
                                Diagnostics.DiagnosticSeverity.Error));
                    }
                }
            }

            bool SameInitializationMethod(IMethodSymbol left, IMethodSymbol right)
            {
                var leftId = bindings.InitializationFunctions.GetValueOrDefault(left.OriginalDefinition);
                var rightId = bindings.InitializationFunctions.GetValueOrDefault(right.OriginalDefinition);
                return leftId != null || rightId != null ? leftId != null && leftId == rightId
                    : SameType(left.ContainingType, right.ContainingType) && left.MetadataName == right.MetadataName
                        && left.Parameters.Length == right.Parameters.Length
                        && left.Parameters.Zip(right.Parameters).All(pair =>
                            pair.First.RefKind == pair.Second.RefKind && SameType(pair.First.Type, pair.Second.Type));
            }

            void AddMetadataMemberType(IOperation operation)
            {
                var (member, instance, result) = operation switch
                {
                    IPropertyReferenceOperation { Property: { IsStatic: false, IsIndexer: false,
                        ReturnsByRef: false, ReturnsByRefReadonly: false,
                        GetMethod.DeclaredAccessibility: Accessibility.Public } property } read
                        => ((ISymbol)property, read.Instance, property.Type),
                    IFieldReferenceOperation { Field.IsStatic: false } read
                        => ((ISymbol)read.Field, read.Instance, read.Field.Type),
                    _ => (null, null, null)
                };
                if (member is null || result is null
                    || Origin(operation) is not FieldAccessNode origin
                    || origin.FieldName != member.Name
                    || conflictingMembers.Contains(origin)
                    || member.DeclaredAccessibility != Accessibility.Public
                    || member.Locations.Any(location => location.IsInSource)
                    || instance?.Type is not INamedTypeSymbol { IsGenericType: true } receiver
                    || receiver.Locations.Any(location => location.IsInSource)
                    || HasErrorType(receiver)
                    || result.SpecialType is not (SpecialType.System_Boolean or SpecialType.System_Char
                        or SpecialType.System_SByte or SpecialType.System_Byte
                        or SpecialType.System_Int16 or SpecialType.System_UInt16
                        or SpecialType.System_Int32 or SpecialType.System_UInt32
                        or SpecialType.System_Int64 or SpecialType.System_UInt64
                        or SpecialType.System_Single or SpecialType.System_Double
                        or SpecialType.System_Decimal or SpecialType.System_String)
                    || DiagnosticsIn(model, operation.Syntax.Span).Any(diagnostic =>
                        diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                    return;
                var receiverOperations = new Stack<IOperation>();
                receiverOperations.Push(instance);
                var getterReceiverSafe = true;
                while (receiverOperations.TryPop(out var part))
                {
                    if (part.Kind == OperationKind.Invalid || part.Type is { } type && HasErrorType(type)
                        || Origin(part) is CSharpInteropBlockNode or RawCSharpNode or RawCSharpExpressionNode
                        || part is IInvocationOperation call
                            && call.TargetMethod.Locations.Any(location => location.IsInSource)
                            && !methods.ContainsKey(call.TargetMethod.OriginalDefinition))
                        return;
                    if (part is IConversionOperation { OperatorMethod: not null }
                        || part is IPropertyReferenceOperation sourceProperty
                            && sourceProperty.Property.Locations.Any(location => location.IsInSource)
                        || part is IFieldReferenceOperation { Field.IsStatic: true } sourceField
                            && sourceField.Field.Locations.Any(location => location.IsInSource))
                        getterReceiverSafe = false;
                    foreach (var child in part.ChildOperations)
                        receiverOperations.Push(child);
                }
                var selection = new MetadataMemberTypeSelection(member, receiver, result);
                selectedMembersInTree.Add(origin);
                if (bindings.MetadataMemberTypes.TryGetValue(origin, out var previous)
                    && (previous.Member.Kind != member.Kind || previous.Member.MetadataName != member.MetadataName
                        || !SameType(previous.Member.ContainingType, member.ContainingType)
                        || !SameType(previous.ReceiverType, receiver) || !SameType(previous.ResultType, result)))
                {
                    conflictingMembers.Add(origin);
                    bindings.MetadataMemberTypes.Remove(origin);
                    return;
                }
                else
                    bindings.MetadataMemberTypes[origin] = selection;
                // This independent identity proof only selects the normal catalog key.
                // It never supplies an effect row, and cannot borrow an opaque getter's receiver.
                if (getterReceiverSafe && member is IPropertySymbol propertyMember
                    && !conflictingGetters.Contains(origin))
                {
                    selectedGettersInTree.Add(origin);
                    bindings.MetadataGetters[origin] = new MetadataGetterSelection(propertyMember, receiver);
                }
            }

            static bool HasErrorType(ITypeSymbol type) => type.TypeKind == TypeKind.Error
                || type is INamedTypeSymbol named && named.TypeArguments.Any(HasErrorType)
                || type is IArrayTypeSymbol array && HasErrorType(array.ElementType);

            bool HasInvalidEventOwner(INamedTypeSymbol owner)
            {
                for (INamedTypeSymbol? current = owner; current is not null; current = current.ContainingType)
                    foreach (var reference in current.DeclaringSyntaxReferences)
                        if (reference.GetSyntax() is TypeDeclarationSyntax declaration
                            && DiagnosticsIn(ModelOf(compilation, reference.SyntaxTree),
                                Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                                declaration.SpanStart, declaration.OpenBraceToken.Span.End))
                                .Any(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                            return true;
                return false;
            }

            void AddCall(IOperation operation, IMethodSymbol method,
                System.Collections.Immutable.ImmutableArray<IArgumentOperation> arguments)
            {
                var usesReceivingConversion = arguments.Any(argument => HasReceivingConversion(argument.Value));
                if (!methods.TryGetValue(method.OriginalDefinition, out var declaration))
                {
                    if (usesReceivingConversion && method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor
                        && method.Locations.Any(location => location.IsInSource))
                        bindingFailures.Add($"The selected source call '{method}' has no Calor declaration origin for its implicit argument conversion.");
                    return;
                }
                if (DiagnosticsIn(model, operation.Syntax.Span).Any(diagnostic =>
                        diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                    return;
                if (HasErrorType(method.ContainingType) || HasErrorType(method.ReturnType)
                    || method.Parameters.Any(parameter => HasErrorType(parameter.Type))
                    || HasInvalidEventOwner(method.ContainingType))
                    return;
                TextSpan? site = Origin(operation) is ExpressionCallNode or CallExpressionNode
                    or CallStatementNode or NewExpressionNode
                    ? Origin(operation)!.Span : null;
                var mappings = new List<ResolvedArgumentMapping>();
                var usesUserConversion = false;
                var usesAnonymousFunctionConversion = false;
                var includesReceiver = false;
                foreach (var argument in arguments)
                {
                    if (argument.Parameter is null)
                    {
                        if (usesReceivingConversion)
                            bindingFailures.Add($"The selected source call '{method}' has no resolved parameter for its implicit conversion.");
                        return;
                    }
                    var expanded = argument.ArgumentKind == ArgumentKind.ParamArray;
                    var values = expanded && argument.Value is IArrayCreationOperation { IsImplicit: true, Initializer: { } initializer }
                        ? initializer.ElementValues : [argument.Value];
                    foreach (var value in values)
                    {
                        if (Origin(value) is not { } origin || !argumentOwners.TryGetValue(origin, out var owner))
                        {
                            if (argument.ArgumentKind == ArgumentKind.DefaultValue)
                                continue;
                            if (usesReceivingConversion)
                                bindingFailures.Add($"The selected source call '{method}' has no unique Calor argument origin for its implicit conversion.");
                            return;
                        }
                        if (site is { } previous && previous != owner.Call)
                        {
                            if (usesReceivingConversion)
                                bindingFailures.Add($"The selected source call '{method}' has inconsistent Calor argument owners for its implicit conversion.");
                            return;
                        }
                        site = owner.Call;
                        if (owner.Index < 0)
                        {
                            if (!method.IsExtensionMethod || argument.Parameter.Ordinal != 0)
                            {
                                if (usesReceivingConversion)
                                    bindingFailures.Add($"The selected source call '{method}' has an invalid Calor extension receiver origin.");
                                return;
                            }
                            includesReceiver = true;
                        }
                        var parameterType = expanded && argument.Parameter.Type is IArrayTypeSymbol array
                            ? array.ElementType : argument.Parameter.Type;
                        mappings.Add(new ResolvedArgumentMapping(owner.Index, argument.Parameter.Ordinal,
                            Binding.Metadata.MetadataBinderResult.ToBoundType(parameterType).DisplayString, expanded));
                        usesAnonymousFunctionConversion |= HasAnonymousFunctionConversion(value);
                        for (var converted = value as IConversionOperation; converted is not null;
                             converted = converted.Operand as IConversionOperation)
                            usesUserConversion |= converted.IsImplicit && converted.OperatorMethod is not null;
                    }
                }
                if (site is not { } callSite)
                {
                    if (usesReceivingConversion)
                        bindingFailures.Add($"The selected source call '{method}' has no Calor call origin for its implicit conversion.");
                    return;
                }
                if (usesReceivingConversion && (conflictingCalls.Contains(callSite)
                    || mappings.Select(mapping => mapping.ArgumentIndex).Distinct().Count()
                        != sourceArgumentCounts[callSite] + (includesReceiver ? 1 : 0)))
                {
                    bindingFailures.Add($"The selected source call '{method}' has no complete, configuration-consistent implicit-conversion argument map.");
                    return;
                }
                if (conflictingCalls.Contains(callSite))
                    return;
                if (includesReceiver && !validProofContext)
                    return;
                var selection = new SourceCallSelection(declaration,
                    Binding.Metadata.MetadataBinderResult.ToBoundType(method.ReturnType).DisplayString,
                    mappings.OrderBy(mapping => mapping.ArgumentIndex)
                        .Select(mapping => includesReceiver
                            ? mapping with { ArgumentIndex = mapping.ArgumentIndex + 1 } : mapping).ToArray(),
                    usesUserConversion, usesAnonymousFunctionConversion, includesReceiver, method,
                    validProofContext);
                selectedInTree.Add(callSite);
                if (bindings.SelectedCalls.TryGetValue(callSite, out var existing)
                    && (!ReferenceEquals(existing.Declaration, selection.Declaration)
                        || existing.ReturnType != selection.ReturnType
                        || existing.IncludesExtensionReceiver != selection.IncludesExtensionReceiver
                        || existing.Method is { } previousMethod
                            && (!SameType(previousMethod.ContainingType, method.ContainingType)
                                || !previousMethod.TypeArguments.Zip(method.TypeArguments).All(pair =>
                                    SameType(pair.First, pair.Second)))
                        || !existing.Arguments.SequenceEqual(selection.Arguments)))
                {
                    if (usesUserConversion || existing.UsesUserDefinedConversion
                        || usesAnonymousFunctionConversion || existing.UsesAnonymousFunctionConversion)
                        bindingFailures.Add($"The selected source call '{method}' has conflicting implicit-conversion callable identities across preprocessor configurations.");
                    conflictingCalls.Add(callSite);
                    bindings.SelectedCalls.Remove(callSite);
                }
                else
                    bindings.SelectedCalls[callSite] = selection with
                    {
                        UsesUserDefinedConversion = usesUserConversion || existing?.UsesUserDefinedConversion == true,
                        UsesAnonymousFunctionConversion = usesAnonymousFunctionConversion
                            || existing?.UsesAnonymousFunctionConversion == true,
                        HasCompleteProofContext = validProofContext && existing?.HasCompleteProofContext != false
                    };
            }

            static bool HasReceivingConversion(IOperation value) => value switch
            {
                IConversionOperation conversion => conversion.IsImplicit && conversion.OperatorMethod is not null
                    || HasReceivingConversion(conversion.Operand),
                IArrayCreationOperation { IsImplicit: true, Initializer: { } initializer } =>
                    initializer.ElementValues.Any(HasReceivingConversion),
                _ => HasAnonymousFunctionConversion(value)
            };

            static bool HasAnonymousFunctionConversion(IOperation value) => value switch
            {
                IDelegateCreationOperation { IsImplicit: true, Target: IAnonymousFunctionOperation } => true,
                IConversionOperation conversion => HasAnonymousFunctionConversion(conversion.Operand),
                IArrayCreationOperation { IsImplicit: true, Initializer: { } initializer } =>
                    initializer.ElementValues.Any(HasAnonymousFunctionConversion),
                _ => false
            };

            void AddMethod(IOperation operation, IMethodSymbol? method)
            {
                if (method is null)
                    return;
                var syntax = operation.Syntax.Span;
                var origin = origins.Find(syntax.Start, syntax.End);
                if (origin is null)
                {
                    bindingFailures.Add($"A selected source operator '{method}' has no Calor expression origin.");
                    return;
                }
                if (methods.TryGetValue(method.OriginalDefinition, out var declaration)
                    && declaration is OperatorOverloadNode op && operatorIds.TryGetValue(op, out var callee))
                {
                    if (!calls.TryGetValue(origin, out var targets))
                        calls[origin] = targets = new HashSet<string>(StringComparer.Ordinal);
                    targets.Add(callee);
                }
                else
                {
                    if (!bindings.ExternalCalls.TryGetValue(origin, out var targets))
                        bindings.ExternalCalls[origin] = targets = [];
                    targets.Add(EffectResolverKey.FromBoundReceiver(
                        Binding.Metadata.MetadataBinderResult.ToBoundType(method.ContainingType),
                        method.AssociatedSymbol is IPropertySymbol property ? property.MetadataName : method.MetadataName,
                        method.MethodKind == MethodKind.PropertyGet ? null
                            : method.Parameters.Select(parameter => parameter.Type.ToDisplayString()).ToArray(),
                        method.MethodKind == MethodKind.PropertyGet ? EffectMemberKind.Getter : EffectMemberKind.Method));
                }
                if (origin is ExpressionNode expression && operation is IBinaryOperation or IUnaryOperation
                    && operation.Type is { } resultType)
                {
                    var type = resultType.ToDisplayString();
                    if (types.TryGetValue(expression, out var existing) && existing != type)
                    {
                        conflictingTypes.Add(expression);
                        types.Remove(expression);
                    }
                    else if (!conflictingTypes.Contains(expression))
                        types[expression] = type;
                }
            }
        }
    }

    /// <summary>
    /// The smallest annotated expression range that contains a C# span: the first
    /// match in <c>ranges</c>, which is sorted by length. A linear scan per lookup made
    /// the projection quadratic, since every walked operation asks for its origin.
    /// The emitter wraps each expression's text in a start/end marker pair, so the
    /// ranges nest: any two are disjoint or one contains the other. Over such a
    /// family the containing ranges of a span form a chain of strictly growing
    /// length, all of them ancestors of the last range that starts at or before the
    /// span. Walking that range's parents therefore returns the same unique
    /// smallest range in O(log n + depth). Ranges that do not nest (or duplicate a
    /// range) fall back to the original scan, so the answer never changes.
    /// </summary>
    internal sealed class ExpressionOriginIndex
    {
        private readonly IReadOnlyList<(int Start, int End, AstNode Node)> _byLength;
        private readonly (int Start, int End, AstNode Node)[] _byStart = [];
        private readonly int[] _parents = [];
        private readonly bool _nested;

        internal ExpressionOriginIndex(IReadOnlyList<(int Start, int End, AstNode Node)> byLength)
        {
            _byLength = byLength;
            var byStart = byLength.ToArray();
            Array.Sort(byStart, (left, right) => left.Start != right.Start
                ? left.Start.CompareTo(right.Start) : right.End.CompareTo(left.End));
            var parents = new int[byStart.Length];
            var open = new Stack<int>();
            for (var index = 0; index < byStart.Length; index++)
            {
                var range = byStart[index];
                if (range.End < range.Start)
                    return;
                while (open.TryPeek(out var top) && byStart[top].End < range.Start)
                    open.Pop();
                if (open.TryPeek(out var container)
                    && (byStart[container].End < range.End
                        || byStart[container].Start == range.Start && byStart[container].End == range.End))
                    return;
                parents[index] = open.TryPeek(out container) ? container : -1;
                open.Push(index);
            }
            _byStart = byStart;
            _parents = parents;
            _nested = true;
        }

        internal AstNode? Find(int start, int end)
        {
            if (!_nested)
                return _byLength.FirstOrDefault(range => range.Start <= start && range.End >= end).Node;
            int low = 0, high = _byStart.Length - 1, last = -1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                if (_byStart[middle].Start <= start)
                {
                    last = middle;
                    low = middle + 1;
                }
                else
                    high = middle - 1;
            }
            for (var index = last; index >= 0; index = _parents[index])
                if (_byStart[index].End >= end)
                    return _byStart[index].Node;
            return null;
        }
    }

    internal static bool IsAddition(AstNode node) =>
        node is BinaryOperationNode { Operator: BinaryOperator.Add }
            or CompoundAssignmentStatementNode { Operator: CompoundAssignmentOperator.Add };

    private static bool SameFormatting(InterpolationFormatting left, InterpolationFormatting right) =>
        left.IsNull == right.IsNull && left.HasBoundedIntegralHexProof == right.HasBoundedIntegralHexProof
            && (left.Type is null ? right.Type is null
            : right.Type is not null && SameType(left.Type, right.Type));

    private static bool SameEventType(INamedTypeSymbol left, INamedTypeSymbol right)
    {
        var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        return SameType(left, right) && left.ToDisplayString(format) == right.ToDisplayString(format)
            && left.DelegateInvokeMethod is { } leftInvoke && right.DelegateInvokeMethod is { } rightInvoke
            && leftInvoke.ReturnType.ToDisplayString(format) == rightInvoke.ReturnType.ToDisplayString(format)
            && leftInvoke.Parameters.Zip(rightInvoke.Parameters).All(pair =>
                pair.First.Type.ToDisplayString(format) == pair.Second.Type.ToDisplayString(format));
    }

    private static bool SameType(ITypeSymbol left, ITypeSymbol right) =>
        left.TypeKind == right.TypeKind
        && Equals(left.ContainingAssembly?.Identity, right.ContainingAssembly?.Identity)
        && left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        && (left is not INamedTypeSymbol { DelegateInvokeMethod: { } leftInvoke }
            || right is INamedTypeSymbol { DelegateInvokeMethod: { } rightInvoke }
                && leftInvoke.ReturnsByRef == rightInvoke.ReturnsByRef
                && leftInvoke.ReturnsByRefReadonly == rightInvoke.ReturnsByRefReadonly
                && leftInvoke.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    == rightInvoke.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                && leftInvoke.Parameters.Length == rightInvoke.Parameters.Length
                && leftInvoke.Parameters.Zip(rightInvoke.Parameters).All(pair =>
                    pair.First.RefKind == pair.Second.RefKind
                    && pair.First.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        == pair.Second.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

    private static bool TryReadMarker(SyntaxTrivia trivia, string prefix, out int index)
    {
        var text = trivia.ToString();
        var marker = "/*" + prefix;
        index = 0;
        return text.StartsWith(marker, StringComparison.Ordinal) && text.EndsWith("*/", StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(marker.Length, text.Length - marker.Length - 2),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index);
    }

    /// <summary>
    /// 0.30: the explicit static constructors of a type, across every part of a partial type
    /// declared in this module.
    /// </summary>
    internal static IEnumerable<ConstructorNode> StaticConstructorsOf(ModuleNode module, ClassDefinitionNode owner) =>
        (owner.IsPartial
            ? EnumerateClasses(module).Where(part => part.IsPartial
                && part.Name.Equals(owner.Name, StringComparison.Ordinal))
            : [owner])
        .SelectMany(part => part.Constructors).Where(constructor => constructor.IsStatic);

    /// <summary>
    /// 0.30 (soundness gap A): the static members whose execution can trigger a type's
    /// explicit static constructor. C# runs a class's static constructor before the first
    /// instance is created or any static member is referenced (ECMA-334, "Static
    /// constructors"), so calling a static method or operator, or running a static property
    /// accessor, of such a type may run it. Instance creation is charged at <c>new</c>.
    /// A type without an explicit static constructor is <c>beforefieldinit</c>: its
    /// initializers run only before the first static field access ("Static field
    /// initialization"), so its static methods are not charged here.
    /// </summary>
    internal static IEnumerable<(string FunctionId, ClassDefinitionNode Owner)> StaticConstructorTriggers(ModuleNode module)
    {
        foreach (var cls in EnumerateClasses(module))
        {
            if (!StaticConstructorsOf(module, cls).Any())
                continue;
            foreach (var method in EnumerateMethods(cls))
                if (cls.IsModule || method.IsStatic)
                    yield return ($"{cls.Name}.{method.Id}", cls);
            foreach (var op in EnumerateOperators(cls))
                yield return ($"{cls.Name}.{op.Id}", cls);
            foreach (var property in EnumerateProperties(cls))
            {
                if (!(cls.IsModule || property.IsStatic))
                    continue;
                foreach (var accessor in new[] { property.Getter, property.Setter, property.Initer }.OfType<PropertyAccessorNode>())
                    yield return (GetPropertyAccessorFunctionId(cls.Name, property, accessor), cls);
            }
        }
    }

    /// <summary>0.30 (soundness gap B): what a preserved raw C# member declares under a name.</summary>
    internal enum PreservedMemberKind { None, ConstantField, StaticField, InstanceField, Opaque }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CSharpInteropBlockNode,
        ClassDeclarationSyntax?> PreservedMemberHosts = new();

    /// <summary>
    /// 0.30 (soundness gap B): whether a preserved raw C# member declares <paramref name="name"/>,
    /// and as what. A raw block that does not parse as members, or holds preprocessor
    /// directives, answers <see cref="PreservedMemberKind.Opaque"/>: it may declare anything.
    /// </summary>
    internal static PreservedMemberKind FindPreservedMember(CSharpInteropBlockNode block, string name)
    {
        var host = PreservedMemberHosts.GetValue(block, static block =>
        {
            var root = CSharpSyntaxTree.ParseText(
                $"class __CalorPreservedHost\n{{\n{block.CSharpCode}\n}}",
                new CSharpParseOptions(LanguageVersion.Preview)).GetCompilationUnitRoot();
            return root.ContainsDiagnostics || root.ContainsDirectives
                || root.Members is not [ClassDeclarationSyntax owner] ? null : owner;
        });
        if (host == null)
            return PreservedMemberKind.Opaque;
        var result = PreservedMemberKind.None;
        foreach (var member in host.Members)
        {
            var kind = member switch
            {
                BaseFieldDeclarationSyntax field when field.Declaration.Variables.Any(variable =>
                        variable.Identifier.ValueText == name) =>
                    field is EventFieldDeclarationSyntax ? PreservedMemberKind.Opaque
                    : field.Modifiers.Any(SyntaxKind.ConstKeyword) ? PreservedMemberKind.ConstantField
                    : field.Modifiers.Any(SyntaxKind.StaticKeyword) ? PreservedMemberKind.StaticField
                    : PreservedMemberKind.InstanceField,
                PropertyDeclarationSyntax property when property.Identifier.ValueText == name => PreservedMemberKind.Opaque,
                EventDeclarationSyntax evt when evt.Identifier.ValueText == name => PreservedMemberKind.Opaque,
                MethodDeclarationSyntax method when method.Identifier.ValueText == name => PreservedMemberKind.Opaque,
                BaseTypeDeclarationSyntax type when type.Identifier.ValueText == name => PreservedMemberKind.Opaque,
                DelegateDeclarationSyntax type when type.Identifier.ValueText == name => PreservedMemberKind.Opaque,
                _ => PreservedMemberKind.None
            };
            if (kind != PreservedMemberKind.None)
                result = result == PreservedMemberKind.None || result == kind ? kind : PreservedMemberKind.Opaque;
        }
        return result;
    }

    internal static bool PreservedMemberMayInitializeType(CSharpInteropBlockNode block)
    {
        var root = CSharpSyntaxTree.ParseText(
            $"class __CalorInitializationHost\n{{\n{block.CSharpCode}\n}}",
            new CSharpParseOptions(LanguageVersion.Preview)).GetCompilationUnitRoot();
        if (root.ContainsDiagnostics || root.ContainsDirectives
            || root.Members.Count != 1 || root.Members[0] is not ClassDeclarationSyntax owner)
            return true;

        // A method body runs on invocation, not on declaring-type initialization.
        // Attributes may designate a module initializer; other member kinds stay opaque.
        return owner.Members.Any(member => member is not MethodDeclarationSyntax { AttributeLists.Count: 0 });
    }

    private readonly record struct AstCallKey(
        string CallerId,
        string Target,
        int Start,
        int End);

    private readonly IReadOnlyDictionary<AstCallKey, IReadOnlyList<string>> _resolvedCallIds;
    private readonly IReadOnlySet<AstCallKey> _boundCallSites;
    private static readonly IReadOnlyDictionary<string, Binding.BoundTypes.BoundType> NoBoundValues =
        new Dictionary<string, Binding.BoundTypes.BoundType>(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>> _boundValueTypes;

    // v0.15 E3 slice b, design-doc §8.2 — the DECLARED function types of named
    // positions, which is a different question from BoundValueTypes' "what did
    // the binder make of this receiver". Populated once, right after the graph is
    // built, because they come from the same Bind() call that resolves the call
    // sites and re-binding to get them would double the binder's cost.
    private static readonly IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>
        NoFunctionTypes =
            new Dictionary<string, Binding.BoundTypes.FunctionBoundType>(StringComparer.Ordinal);

    private IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>> _declaredFunctionTypes =
            new Dictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>>(
                StringComparer.Ordinal);

    private IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>
        _declaredReturnFunctionTypes = NoFunctionTypes;

    /// <summary>
    /// v0.15 E4 (review round 1, F1/F2) — what the BINDER resolved a value
    /// reference to, keyed by the reference's span, and the declared function
    /// types of LOCALS keyed by their declaration span. Both exist because a
    /// name is not an identity: two sibling branches can each bind a <c>§B{f}</c>,
    /// and the effect pass must charge the row of the one the reference
    /// actually resolves to, not the first of that name in lexical order.
    /// Empty when binding threw, in which case the pass falls back to its
    /// name-keyed AST search and fails CLOSED where that search is ambiguous.
    /// </summary>
    public sealed class BoundValueIndex
    {
        // The bound module the index was read from; null when binding threw.
        internal BoundModule? Module { get; set; }
        // The binder's source-call bindings for this module, when it computed them.
        internal OperatorBindings? SourceOperatorBindings { get; set; }
        internal Dictionary<string, FunctionNode> ExternalSourceFunctions { get; } = [];
        internal Dictionary<(string CallerId, int Start, int End), SourceArgumentSelection>
            SourceArguments
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), BoundCallExpression>
            ComputedMemberCalls
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), IReadOnlyList<string>>
            ConstructorCalls
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), ClassDefinitionNode>
            SourceCreationTypes
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), Microsoft.CodeAnalysis.ITypeSymbol>
            MetadataValueTypes
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), Binding.BoundTypes.NominalBoundType>
            MetadataMemberReceiverTypes
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), Microsoft.CodeAnalysis.IFieldSymbol>
            MetadataEnumConstants
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), Microsoft.CodeAnalysis.IFieldSymbol>
            MetadataFields
        { get; } = new();
        internal HashSet<(string CallerId, int Start, int End)> TypeQualifiedMetadataFields { get; } = new();
        internal HashSet<(string CallerId, int Start, int End)> TypeQualifiedMemberReads { get; } = new();
        internal HashSet<(string CallerId, int Start, int End)> TypeReferences { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), Microsoft.CodeAnalysis.ISymbol>
            UnsupportedMetadataMembers
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), string> SourceMemberOwners { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), VariableSymbol>
            SourceEnumConstants
        { get; } = new();
        internal HashSet<(string CallerId, int Start, int End)> UnresolvedArgumentCalls { get; } = new();
        internal HashSet<(string CallerId, int Start, int End)> TupleElementReads { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), BoundFieldAccessExpression>
            AnonymousPropertyReads
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), BoundFieldAccessExpression>
            SourceFieldAccesses
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), Binding.BoundTypes.ArrayBoundType>
            ArrayTypes
        { get; } = new();
        internal Dictionary<(string CallerId, int Start, int End), BoundArrayAccess>
            ElementAccesses
        { get; } = new();
        /// <summary>(callerId, reference start, reference end) → the resolved
        /// variable's <c>DeclarationSpan</c>.</summary>
        public Dictionary<(string CallerId, int Start, int End), TextSpan> Declarations { get; } = new();

        public Dictionary<(string CallerId, int Start, int End), VariableSymbol> Variables { get; } = new();

        /// <summary>callerId → the locals with a declared <c>FunctionBoundType</c>,
        /// by name and declaration start.</summary>
        public Dictionary<string, List<(string Name, int DeclarationStart, Binding.BoundTypes.FunctionBoundType Type)>>
            Locals
        { get; } = new(StringComparer.Ordinal);

        /// <summary>0.25 F3 (#847): local-function calls, keyed by the enclosing callable.</summary>
        public HashSet<(string CallerId, int Start, int End)> LocalFunctionCalls { get; } = new();

        /// <summary>0.30: calls the binder proved target a metadata member
        /// (<see cref="IsProvablyMetadataCall"/>), keyed by the enclosing callable.
        /// They are no edge to a project declaration, and no unresolved call either.</summary>
        public HashSet<(string CallerId, int Start, int End)> MetadataCalls { get; } = new();

        /// <summary>False when binding threw: no call site is known to be local.</summary>
        public bool Complete { get; set; }
    }

    private BoundValueIndex _boundValues = new();
    internal Verification.ModuleInheritanceResult? ContractInheritance { get; private set; }

    internal sealed record SourceArgumentSelection(
        string CalleeId, IReadOnlyList<ResolvedArgumentMapping> Arguments);

    internal SourceArgumentSelection? SourceArguments(string callerId, TextSpan span) =>
        _boundValues.SourceArguments.GetValueOrDefault((callerId, span.Start, span.End));

    internal BoundCallExpression? ComputedMemberCall(string callerId, TextSpan span) =>
        _boundValues.ComputedMemberCalls.GetValueOrDefault((callerId, span.Start, span.End));

    internal IReadOnlyList<string> ResolvedConstructorCalls(string callerId, TextSpan span) =>
        _boundValues.ConstructorCalls.GetValueOrDefault((callerId, span.Start, span.End)) ?? [];

    internal ClassDefinitionNode? SourceCreationType(string callerId, TextSpan span) =>
        _boundValues.SourceCreationTypes.GetValueOrDefault((callerId, span.Start, span.End));

    internal Microsoft.CodeAnalysis.INamedTypeSymbol? MetadataCreationType(string callerId, TextSpan span) =>
        MetadataValueType(callerId, span) as Microsoft.CodeAnalysis.INamedTypeSymbol;

    internal Microsoft.CodeAnalysis.ITypeSymbol? MetadataValueType(string callerId, TextSpan span) =>
        _boundValues.MetadataValueTypes.GetValueOrDefault((callerId, span.Start, span.End));

    internal Binding.BoundTypes.NominalBoundType? MetadataMemberReceiverType(string callerId, TextSpan span) =>
        _boundValues.MetadataMemberReceiverTypes.GetValueOrDefault((callerId, span.Start, span.End));

    internal Microsoft.CodeAnalysis.IFieldSymbol? MetadataEnumConstant(string callerId, TextSpan span) =>
        _boundValues.MetadataEnumConstants.GetValueOrDefault((callerId, span.Start, span.End));

    internal Microsoft.CodeAnalysis.IFieldSymbol? MetadataField(string callerId, TextSpan span) =>
        _boundValues.MetadataFields.GetValueOrDefault((callerId, span.Start, span.End));

    /// <summary>
    /// Whether the binder selected a static metadata field through a type reference
    /// (<c>System . String . Empty</c>), whose qualifier evaluates nothing.
    /// </summary>
    internal bool IsTypeQualifiedMetadataField(string callerId, TextSpan span) =>
        _boundValues.TypeQualifiedMetadataFields.Contains((callerId, span.Start, span.End));

    /// <summary>
    /// Whether the binder read the member at <paramref name="span"/> on a type
    /// (<c>System.DateTime.Now</c>, <c>Outer.Holder.Instance</c>): its qualifier evaluates nothing.
    /// </summary>
    internal bool IsTypeQualifiedMemberRead(string callerId, TextSpan span) =>
        _boundValues.TypeQualifiedMemberReads.Contains((callerId, span.Start, span.End));

    /// <summary>
    /// The metadata member the binder selected at <paramref name="span"/> whose result type it
    /// cannot carry (a non-builtin struct such as <c>DateTime</c> from <c>DateTime.Now</c>).
    /// </summary>
    internal Microsoft.CodeAnalysis.ISymbol? UnsupportedMetadataMember(string callerId, TextSpan span) =>
        _boundValues.UnsupportedMetadataMembers.GetValueOrDefault((callerId, span.Start, span.End));

    /// <summary>The source type whose member the binder read at <paramref name="span"/>.</summary>
    internal string? SourceMemberOwner(string callerId, TextSpan span) =>
        _boundValues.SourceMemberOwners.GetValueOrDefault((callerId, span.Start, span.End));

    /// <summary>Whether the binder read the name at <paramref name="span"/> as a type (<c>System.Console</c>).</summary>
    internal bool IsTypeReference(string callerId, TextSpan span) =>
        _boundValues.TypeReferences.Contains((callerId, span.Start, span.End));

    internal VariableSymbol? SourceEnumConstant(string callerId, TextSpan span) =>
        _boundValues.SourceEnumConstants.GetValueOrDefault((callerId, span.Start, span.End));

    internal bool IsResolvedEnumConstant(string callerId, TextSpan span) =>
        SourceEnumConstant(callerId, span) is not null || MetadataEnumConstant(callerId, span) is not null;

    internal bool HasUnresolvedCallArguments(string callerId, TextSpan span) =>
        _boundValues.UnresolvedArgumentCalls.Contains((callerId, span.Start, span.End));

    internal bool IsTupleElementRead(string callerId, TextSpan span) =>
        _boundValues.TupleElementReads.Contains((callerId, span.Start, span.End));

    internal BoundFieldAccessExpression? AnonymousPropertyRead(string callerId, TextSpan span) =>
        _boundValues.AnonymousPropertyReads.GetValueOrDefault((callerId, span.Start, span.End));

    internal BoundFieldAccessExpression? SourceFieldAccess(string callerId, TextSpan span) =>
        _boundValues.SourceFieldAccesses.GetValueOrDefault((callerId, span.Start, span.End));

    /// <summary>The declaration span of the variable the binder resolved a bare
    /// value reference (a call target or a <c>ReferenceNode</c>) to, or
    /// <c>null</c> when binding threw or the reference was not a variable.</summary>
    /// <summary>0.25 F3 (#847): <c>null</c> when binding threw (fail closed).</summary>
    public bool? IsLocalFunctionCall(string callerId, TextSpan call) =>
        _boundValues.Complete
            ? _boundValues.LocalFunctionCalls.Contains((callerId, call.Start, call.End))
            : null;

    public TextSpan? BoundValueDeclaration(string callerId, TextSpan reference) =>
        _boundValues.Declarations.TryGetValue((callerId, reference.Start, reference.End), out var span)
            ? span
            : null;

    public VariableSymbol? BoundVariable(string callerId, TextSpan reference) =>
        _boundValues.Variables.GetValueOrDefault((callerId, reference.Start, reference.End));

    /// <summary>The bound element access at <paramref name="span"/> (<c>a[i]</c>), or null.</summary>
    internal BoundArrayAccess? BoundElementAccess(string callerId, TextSpan span) =>
        _boundValues.ElementAccesses.GetValueOrDefault((callerId, span.Start, span.End));

    internal Binding.BoundTypes.ArrayBoundType? BoundArrayReceiverType(string callerId, TextSpan expression) =>
        _boundValues.ArrayTypes.GetValueOrDefault((callerId, expression.Start, expression.End));

    /// <summary>The declared <c>FunctionBoundType</c> of the LOCAL named
    /// <paramref name="name"/> whose declaration lies inside
    /// <paramref name="bindSpan"/>, or <c>null</c>.</summary>
    public Binding.BoundTypes.FunctionBoundType? DeclaredLocalFunctionType(
        string callerId, string name, TextSpan bindSpan)
    {
        if (!_boundValues.Locals.TryGetValue(callerId, out var locals)) return null;
        foreach (var (localName, start, type) in locals)
        {
            if (localName.Equals(name, StringComparison.Ordinal)
                && bindSpan.Start <= start && start <= bindSpan.End)
                return type;
        }
        return null;
    }

    private IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>
        _declaredFieldFunctionTypes = NoFunctionTypes;

    /// <summary>
    /// The declared <c>FunctionBoundType</c> of the named parameters and locals of
    /// one function — <c>VariableSymbol.FunctionType</c>, keyed by name. Empty
    /// when binding threw.
    /// </summary>
    public IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType> DeclaredFunctionTypes(
        string functionId) =>
        _declaredFunctionTypes.TryGetValue(functionId, out var map) ? map : NoFunctionTypes;

    /// <summary>The declared <c>FunctionSymbol.ReturnFunctionType</c> of one function.</summary>
    public Binding.BoundTypes.FunctionBoundType? DeclaredReturnFunctionType(string functionId) =>
        _declaredReturnFunctionTypes.GetValueOrDefault(functionId);

    /// <summary>The declared function type of a class FIELD, keyed <c>Class.field</c>.</summary>
    public Binding.BoundTypes.FunctionBoundType? DeclaredFieldFunctionType(
        string className, string fieldName) =>
        _declaredFieldFunctionTypes.GetValueOrDefault($"{className}.{fieldName}");

    /// <summary>
    /// Builds an overload-precise graph from bound calls. External calls remain
    /// explicitly unresolved instead of being projected onto an internal name.
    /// </summary>
    public static ResolvedSymbolCallGraph BuildResolved(BoundModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var forward = module.Functions.ToDictionary(
            function => function.SymbolId,
            _ => new List<ResolvedCallSite>());
        var reverse = module.Functions.ToDictionary(
            function => function.SymbolId,
            _ => new List<SymbolId>());
        var unresolved = new List<UnresolvedCallSite>();

        foreach (var function in module.Functions)
        {
            foreach (var node in DescendantsAndSelf(function))
            {
                string? target = null;
                TextSpan span = default;
                IReadOnlyList<FunctionSymbol>? callees = null;

                switch (node)
                {
                    case BoundCallStatement statement:
                        target = statement.Target;
                        span = statement.Span;
                        callees = statement.ResolvedSymbols;
                        break;
                    case BoundCallExpression expression:
                        target = expression.Target;
                        span = expression.Span;
                        callees = expression.ResolvedSymbols;
                        break;
                    case BoundNewExpression creation:
                        target = $"{creation.Type.DisplayString}..ctor";
                        span = creation.TypeNameSpan;
                        callees = creation.ResolvedConstructors;
                        break;
                    case BoundExpressionCallExpression expressionCall:
                        target = "<expression-call>";
                        span = expressionCall.Span;
                        break;
                    case BoundExpressionCall expressionCall:
                        target = "<expression-call>";
                        span = expressionCall.Span;
                        break;
                }

                if (target == null)
                    continue;

                var resolvedCallees = callees?
                    .Where(callee => !callee.Id.IsNone)
                    .Select(callee => callee.Id)
                    .Distinct()
                    .ToArray()
                    ?? Array.Empty<SymbolId>();
                if (resolvedCallees.Length > 0)
                {
                    foreach (var resolved in resolvedCallees)
                    {
                        forward[function.SymbolId].Add(new ResolvedCallSite(resolved, target, span));
                        if (!reverse.TryGetValue(resolved, out var callers))
                        {
                            callers = new List<SymbolId>();
                            reverse[resolved] = callers;
                        }
                        callers.Add(function.SymbolId);
                    }
                }
                else if (node is not BoundCallExpression call || !IsProvablyMetadataCall(call))
                {
                    unresolved.Add(new UnresolvedCallSite(function.SymbolId, target, span));
                }
            }
        }

        return new ResolvedSymbolCallGraph(
            forward.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ResolvedCallSite>)pair.Value),
            reverse.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<SymbolId>)pair.Value),
            unresolved);
    }

    /// <summary>
    /// 0.30: whether a bound call that selected no source callee provably targets a
    /// member of referenced metadata (the BCL or another assembly), so that it cannot
    /// be an edge to a project declaration. Such a call is attributed, to metadata, and
    /// does not belong in the unresolved residual: before this, every native member call
    /// such as <c>Console.WriteLine(s)</c> marked its caller's callees, effects and
    /// impact answers partial.
    /// <para>Fails closed. A call stays unresolved when the binder selected nothing,
    /// when a source or opaque (raw C#) candidate took part, when an argument did not
    /// bind, when the member belongs to a delegate (an invocation calls whatever the
    /// value holds), and when the selected member is overridable and the receiver's
    /// static type is not a sealed or value metadata type: a project type may override
    /// or implement that member, so the call may dispatch into project code.</para>
    /// <para>Not modelled, as for source calls: project code that the metadata member
    /// itself calls back into (an argument's override, a delegate argument).</para>
    /// </summary>
    internal static bool IsProvablyMetadataCall(BoundCallExpression call)
    {
        if (call is BoundIncompleteExpression
            || call.ResolvedSymbols.Count > 0
            || call.SelectedSourceMethod is not null
            || call.IsSourceExtensionCall
            || call.HasOpaqueOverloads
            || call.HasUnresolvedArguments
            || call.IsInaccessibleCall
            || call.ResolvedMetadataMethod is not { } method
            || !IsMetadataSymbol(method)
            || !IsMetadataSymbol(method.ContainingType))
            return false;
        if (method.ContainingType.TypeKind == TypeKind.Delegate
            || method.ContainingType.SpecialType is SpecialType.System_Delegate
                or SpecialType.System_MulticastDelegate)
            return false;
        var overridable = method.IsAbstract || method.IsVirtual || method.IsOverride && !method.IsSealed;
        if (!overridable)
            return true;
        return !method.IsStatic
            && call.ResolvedMetadataReceiverType is { } receiverType
            && receiverType.TypeKind is not (TypeKind.TypeParameter or TypeKind.Interface)
            && (receiverType.IsValueType || receiverType.IsSealed)
            && IsMetadataSymbol(receiverType);

        static bool IsMetadataSymbol(ISymbol symbol) =>
            symbol.Locations.Length > 0 && symbol.Locations.All(location => location.IsInMetadata);
    }

    private static IEnumerable<BoundNode> DescendantsAndSelf(BoundNode node)
    {
        yield return node;
        // Other analyses keep these blocks opaque; call graphs need every branch's selected calls.
        IEnumerable<BoundNode> children = node is BoundPreprocessorBlock conditional
            ? conditional.Branches.SelectMany(branch => branch)
            : node.ChildNodes;
        foreach (var child in children)
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    /// <summary>
    /// Forward graph: caller → list of (callee name, call site span).
    /// </summary>
    public Dictionary<string, List<(string Callee, TextSpan Span)>> ForwardGraph { get; }

    /// <summary>
    /// Reverse graph: callee → list of caller IDs.
    /// </summary>
    public Dictionary<string, List<string>> ReverseGraph { get; }

    /// <summary>
    /// All functions indexed by ID.
    /// </summary>
    public Dictionary<string, FunctionNode> Functions { get; }

    /// <summary>
    /// Maps function name to ID for resolving internal calls.
    /// </summary>
    public Dictionary<string, string> FunctionNameToId { get; }

    /// <summary>
    /// Maps bare method name to all qualified function IDs (handles name collisions).
    /// </summary>
    public Dictionary<string, List<string>> MethodNameToIds { get; }

    /// <summary>
    /// Calls that the AST-only compatibility graph cannot resolve exactly.
    /// Ambiguous overloads and external calls remain explicit here.
    /// </summary>
    public IReadOnlyList<AstUnresolvedCallSite> UnresolvedCalls { get; }
    public bool IsBoundResolutionComplete { get; }

    /// <summary>
    /// v0.15 E1 slice 2b — the binder side-channel's RECEIVER types, per caller.
    /// For each legacy function id, the bound
    /// <see cref="Binding.BoundTypes.BoundType"/> of the receiver of every call
    /// site in that function, keyed by the receiver path exactly as the call
    /// target spells it (<c>"sb"</c> for <c>sb.Append</c>, <c>"a.b"</c> for
    /// <c>a.b.M</c>). This is the <c>Receiver</c> BoundExpression PR #1095 put
    /// on the call nodes, handed to a consumer that walks the AST.
    ///
    /// <para>This is what lets <c>EffectEnforcementPass</c>'s resolvers ask the
    /// bound tree BEFORE falling back to AST type strings. A receiver path whose
    /// bound answers disagree between two call sites in one function is dropped
    /// rather than guessed; the AST path then decides as it did before.</para>
    ///
    /// <para><b>The real invariant, stated exactly</b> (review round 1, finding
    /// 6). The map is keyed by NAME, not by position: if a name is used as a
    /// receiver ANYWHERE in the function, this answers for that name EVERYWHERE
    /// in the function, including at occurrences that are not receivers. The
    /// consumer (<c>ResolveLocalValueType</c>) is itself name-keyed and has no
    /// position to pass, so keying by (name, position) would need a second
    /// parameter threaded through eleven call sites; that is deferred.</para>
    ///
    /// <para>The ambiguity rule above is what keeps it sound: two occurrences of
    /// one name that the binder types differently — the case where "answers
    /// everywhere" would be wrong — are dropped, so the AST decides exactly as
    /// before. What survives is a name the binder gives ONE answer for
    /// throughout the function, and a name with one type does not change type by
    /// appearing in a different position.</para>
    ///
    /// <para><b>This spread is not merely benign — it is load-bearing</b>
    /// (review round 2). It is the path by which
    /// <c>EffectEnforcementPass.AskBoundTree</c>'s fail-closed veto is reachable
    /// at all: a name used as a receiver ONCE and as a bare call target
    /// elsewhere carries its <c>Reported</c> <c>UnresolvedBoundType</c> into
    /// <c>InferFromBareNameTarget</c>, where it stops the AST's <c>"?"</c>
    /// sentinel from being mistaken for a type and laundered into a Calor0418
    /// that charges nothing. Pinned by
    /// <c>EffectEnforcementTests.E1Slice2b_ReportedUnresolvedReceiver_VetoesTheAstSentinel</c>.
    /// So keying by position later is not a free tidy-up: it would need that
    /// veto re-established at the bare-target position, or the <c>"?"</c>
    /// sentinel guarded there, or the pin would go silent.</para>
    ///
    /// <para>Deliberately RECEIVERS ONLY, not every bound name. A name in a
    /// non-receiver position — a method group passed as an argument, a bare
    /// call target — must keep resolving through the AST, because the string
    /// this pass gets back is quoted verbatim in Calor0418's message and drives
    /// the method-group charging arm. Widening it is a later slice with its own
    /// evidence.</para>
    ///
    /// <para>Empty when binding threw (<see cref="IsBoundResolutionComplete"/>
    /// false) — the pass then behaves exactly as it did before this slice.</para>
    /// </summary>
    public IReadOnlyDictionary<string, Binding.BoundTypes.BoundType> BoundValueTypes(
        string callerFunctionId) =>
        _boundValueTypes.TryGetValue(callerFunctionId, out var map) ? map : NoBoundValues;

    /// <summary>
    /// Strongly connected components in reverse topological order.
    /// </summary>
    public List<List<string>> StronglyConnectedComponents { get; }

    private CallGraphAnalysis(
        Dictionary<string, List<(string Callee, TextSpan Span)>> forwardGraph,
        Dictionary<string, List<string>> reverseGraph,
        Dictionary<string, FunctionNode> functions,
        Dictionary<string, string> functionNameToId,
        Dictionary<string, List<string>> methodNameToIds,
        IReadOnlyList<AstUnresolvedCallSite> unresolvedCalls,
        IReadOnlyDictionary<AstCallKey, IReadOnlyList<string>> resolvedCallIds,
        IReadOnlySet<AstCallKey> boundCallSites,
        bool isBoundResolutionComplete,
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>> boundValueTypes,
        List<List<string>> sccs)
    {
        ForwardGraph = forwardGraph;
        ReverseGraph = reverseGraph;
        Functions = functions;
        FunctionNameToId = functionNameToId;
        MethodNameToIds = methodNameToIds;
        UnresolvedCalls = unresolvedCalls;
        _resolvedCallIds = resolvedCallIds;
        _boundCallSites = boundCallSites;
        IsBoundResolutionComplete = isBoundResolutionComplete;
        _boundValueTypes = boundValueTypes;
        StronglyConnectedComponents = sccs;
    }

    /// <summary>
    /// Enumerates all classes in a module, including classes wrapped in
    /// module-level §PP type-preprocessor blocks (any branch — a
    /// conditional-compilation branch may be active, so every branch's members
    /// participate in analysis; W2 review C1).
    /// </summary>
    public static IEnumerable<ClassDefinitionNode> EnumerateClasses(ModuleNode module)
    {
        foreach (var cls in module.Classes)
            foreach (var declaration in ClassTree(cls))
                yield return declaration;
        foreach (var block in module.TypePreprocessorBlocks)
        {
            var branch = block;
            while (branch != null)
            {
                foreach (var cls in branch.Classes)
                    foreach (var declaration in ClassTree(cls))
                        yield return declaration;
                branch = branch.ElseBranch;
            }
        }

        static IEnumerable<ClassDefinitionNode> ClassTree(ClassDefinitionNode cls)
        {
            yield return cls;
            foreach (var nested in cls.NestedClasses.Concat(
                         PreprocessorMembers(cls).OfType<ClassDefinitionNode>()))
                foreach (var declaration in ClassTree(nested))
                    yield return declaration;
        }
    }

    /// <summary>
    /// Enumerates all interfaces in a module, including nested and §PP-wrapped ones.
    /// </summary>
    public static IEnumerable<InterfaceDefinitionNode> EnumerateInterfaces(ModuleNode module)
    {
        foreach (var iface in module.Interfaces)
            yield return iface;
        foreach (var block in module.TypePreprocessorBlocks)
        {
            var branch = block;
            while (branch != null)
            {
                foreach (var iface in branch.Interfaces)
                    yield return iface;
                branch = branch.ElseBranch;
            }
        }
        foreach (var cls in EnumerateClasses(module))
            foreach (var iface in cls.NestedInterfaces.Concat(
                         PreprocessorMembers(cls).OfType<InterfaceDefinitionNode>()))
                yield return iface;
    }

    /// <summary>
    /// Enumerates all delegate definitions in a module, including §PP-wrapped ones.
    /// </summary>
    public static IEnumerable<DelegateDefinitionNode> EnumerateDelegates(ModuleNode module)
    {
        foreach (var del in module.Delegates)
            yield return del;
        foreach (var block in module.TypePreprocessorBlocks)
        {
            var branch = block;
            while (branch != null)
            {
                foreach (var del in branch.Delegates)
                    yield return del;
                branch = branch.ElseBranch;
            }
        }
    }

    /// <summary>
    /// Enumerates all methods of a class, including methods wrapped in
    /// class-level §PP member-preprocessor blocks (all branches; W2 review C1 —
    /// a §PP-wrapped method must not escape effect enforcement).
    /// </summary>
    public static IEnumerable<MethodNode> EnumerateMethods(ClassDefinitionNode cls)
    {
        foreach (var method in cls.Methods)
            yield return method;
        foreach (var block in cls.PreprocessorBlocks)
        {
            var branch = block;
            while (branch != null)
            {
                foreach (var method in branch.Methods)
                    yield return method;
                branch = branch.ElseBranch;
            }
        }
    }

    /// <summary>
    /// Enumerates all constructors of a class, including §PP-wrapped ones.
    /// </summary>
    public static IEnumerable<ConstructorNode> EnumerateConstructors(ClassDefinitionNode cls)
    {
        foreach (var ctor in cls.Constructors)
            yield return ctor;
        foreach (var block in cls.PreprocessorBlocks)
        {
            var branch = block;
            while (branch != null)
            {
                foreach (var ctor in branch.Constructors)
                    yield return ctor;
                branch = branch.ElseBranch;
            }
        }
    }

    public static IEnumerable<PropertyNode> EnumerateProperties(ClassDefinitionNode cls)
    {
        foreach (var property in cls.Properties)
            yield return property;
        foreach (var block in cls.PreprocessorBlocks)
        {
            var branch = block;
            while (branch != null)
            {
                foreach (var property in branch.Properties)
                    yield return property;
                branch = branch.ElseBranch;
            }
        }
    }

    public static IEnumerable<EventDefinitionNode> EnumerateEvents(ClassDefinitionNode cls)
    {
        foreach (var evt in cls.Events)
            yield return evt;
        foreach (var block in cls.PreprocessorBlocks)
        {
            var branch = block;
            while (branch != null)
            {
                foreach (var evt in branch.Events)
                    yield return evt;
                branch = branch.ElseBranch;
            }
        }
    }

    internal static IEnumerable<OperatorOverloadNode> EnumerateOperators(ClassDefinitionNode cls)
        => cls.OperatorOverloads.Concat(PreprocessorMembers(cls).OfType<OperatorOverloadNode>());

    internal static IEnumerable<IndexerNode> EnumerateIndexers(ClassDefinitionNode cls)
        => cls.Indexers.Concat(PreprocessorMembers(cls).OfType<IndexerNode>());

    internal static IEnumerable<AstNode> PreprocessorMembers(ClassDefinitionNode cls)
    {
        foreach (var block in cls.PreprocessorBlocks)
            for (var branch = block; branch != null; branch = branch.ElseBranch)
                foreach (var member in RecursiveAstWalker.GetAllChildren(branch))
                    yield return member;
    }

    /// <summary>
    /// The first phase of <see cref="Build(ModuleNode, IReadOnlyDictionary{string, CodeGen.CrossModuleFunctionTarget}?, Verification.ModuleInheritanceResult?, ContractMode, CrossModuleTypeRegistry?, string?)"/>:
    /// every declaration as a function, the contract inheritance, and the module bound
    /// once. Nothing here reads the C# operator bindings, which the second phase adds.
    /// </summary>
    private sealed record DeclarationPhase(
        Dictionary<string, FunctionNode> Functions,
        Dictionary<string, string> FunctionNameToId,
        Dictionary<string, List<string>> MethodNameToIds,
        Dictionary<string, List<string>> CalleeToCallers,
        Dictionary<string, List<(string, TextSpan)>> CallerToCallees,
        Verification.ModuleInheritanceResult ContractInheritance,
        Dictionary<AstCallKey, IReadOnlyList<string>> ResolvedCallIds,
        HashSet<AstCallKey> BoundCallSites,
        bool BoundResolutionComplete,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>> BoundValueTypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>>
            DeclaredFunctionTypes,
        IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType> DeclaredReturnTypes,
        IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType> DeclaredFieldTypes,
        BoundValueIndex BoundValues);

    private static DeclarationPhase BuildDeclarations(ModuleNode ast,
        IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions,
        Verification.ModuleInheritanceResult? contractInheritance,
        ContractMode projectionContractMode,
        CrossModuleTypeRegistry? crossModuleTypes, string? sourcePath)
    {
        if (contractInheritance is null)
        {
            using var checker = new Verification.ContractInheritanceChecker(
                new Diagnostics.DiagnosticBag(), useZ3: false);
            contractInheritance = checker.Check(ast);
        }
        var functions = new Dictionary<string, FunctionNode>(StringComparer.Ordinal);
        var functionNameToId = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguousFunctionNames = new HashSet<string>(StringComparer.Ordinal);
        var methodNameToIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // callee → callers
        var calleeToCallers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // caller → callees with spans
        var callerToCallees = new Dictionary<string, List<(string, TextSpan)>>(StringComparer.Ordinal);

        // Index all top-level functions
        foreach (var function in ast.Functions)
        {
            functions[function.Id] = function;
            AddUniqueName(functionNameToId, ambiguousFunctionNames, function.Name, function.Id);
            calleeToCallers[function.Id] = new List<string>();
            callerToCallees[function.Id] = new List<(string, TextSpan)>();
        }

        foreach (var declaration in EnumerateInterfaces(ast))
        {
            foreach (var method in declaration.Methods)
            {
                var wrapped = new FunctionNode(method.Span, $"{declaration.Name}.{method.Id}",
                    $"{declaration.Name}.{method.Name}", Visibility.Public, method.TypeParameters,
                    method.Parameters, method.Output, method.Effects, method.Preconditions,
                    method.Postconditions, Array.Empty<StatementNode>(), method.Attributes)
                {
                    EffectParameters = method.EffectParameters
                };
                functions[wrapped.Id] = wrapped;
                calleeToCallers[wrapped.Id] = [];
                callerToCallees[wrapped.Id] = [];
            }
        }

        // Index class methods and constructors (including §PP-wrapped members
        // and §PP-wrapped classes — W2 review C1)
        foreach (var cls in EnumerateClasses(ast))
        {
            foreach (var method in EnumerateMethods(cls))
            {
                var wrapped = ToFunctionNode(method, cls.Name, contractInheritance);
                functions[wrapped.Id] = wrapped;
                AddUniqueName(functionNameToId, ambiguousFunctionNames, wrapped.Name, wrapped.Id);
                calleeToCallers[wrapped.Id] = new List<string>();
                callerToCallees[wrapped.Id] = new List<(string, TextSpan)>();

                if (!methodNameToIds.TryGetValue(wrapped.Name, out var ids))
                {
                    ids = new List<string>();
                    methodNameToIds[wrapped.Name] = ids;
                }
                ids.Add(wrapped.Id);
            }
            foreach (var ctor in EnumerateConstructors(cls))
            {
                var wrapped = ToCtorFunctionNode(ctor, cls.Name);
                functions[wrapped.Id] = wrapped;
                AddUniqueName(functionNameToId, ambiguousFunctionNames, wrapped.Name, wrapped.Id);
                calleeToCallers[wrapped.Id] = new List<string>();
                callerToCallees[wrapped.Id] = new List<(string, TextSpan)>();
            }
            foreach (var property in EnumerateProperties(cls))
            {
                foreach (var accessor in new[] { property.Getter, property.Setter, property.Initer }
                    .Where(a => a != null))
                {
                    var wrapped = ToPropertyAccessorFunctionNode(property, accessor!, cls.Name);
                    functions[wrapped.Id] = wrapped;
                    AddUniqueName(functionNameToId, ambiguousFunctionNames, wrapped.Name, wrapped.Id);
                    calleeToCallers[wrapped.Id] = new List<string>();
                    callerToCallees[wrapped.Id] = new List<(string, TextSpan)>();
                }
            }
            foreach (var op in EnumerateOperators(cls))
            {
                var wrapped = new FunctionNode(op.Span, $"{cls.Name}.{op.Id}", $"{cls.Name}.op_{op.Kind}",
                    op.Visibility, op.Parameters, op.Output, op.Effects,
                    op.Preconditions, op.Postconditions, op.Body, op.Attributes);
                functions[wrapped.Id] = wrapped;
                calleeToCallers[wrapped.Id] = [];
                callerToCallees[wrapped.Id] = [];
            }
            foreach (var indexer in EnumerateIndexers(cls))
            {
                foreach (var accessor in new[] { indexer.Getter, indexer.Setter, indexer.Initer }.OfType<PropertyAccessorNode>())
                {
                    var parameters = indexer.Parameters.Concat(accessor.Kind == PropertyAccessorNode.AccessorKind.Get
                        ? [] : new[] { new ParameterNode(accessor.Span, "value", indexer.TypeName, new AttributeCollection()) }).ToArray();
                    var wrapped = new FunctionNode(accessor.Span,
                        $"{cls.Name}.{indexer.Id}.{accessor.Kind.ToString().ToLowerInvariant()}",
                        $"{cls.Name}.this[].{accessor.Kind.ToString().ToLowerInvariant()}",
                        accessor.Visibility ?? indexer.Visibility, parameters, output: null, effects: accessor.Effects,
                        accessor.Preconditions, Array.Empty<EnsuresNode>(), accessor.Body, accessor.Attributes);
                    functions[wrapped.Id] = wrapped;
                    calleeToCallers[wrapped.Id] = [];
                    callerToCallees[wrapped.Id] = [];
                }
            }
            foreach (var evt in EnumerateEvents(cls))
            {
                if (evt.AddBody != null)
                {
                    var wrapped = ToEventAccessorFunctionNode(evt, isAdd: true, cls.Name);
                    functions[wrapped.Id] = wrapped;
                    AddUniqueName(functionNameToId, ambiguousFunctionNames, wrapped.Name, wrapped.Id);
                    calleeToCallers[wrapped.Id] = new List<string>();
                    callerToCallees[wrapped.Id] = new List<(string, TextSpan)>();
                }
                if (evt.RemoveBody != null)
                {
                    var wrapped = ToEventAccessorFunctionNode(evt, isAdd: false, cls.Name);
                    functions[wrapped.Id] = wrapped;
                    AddUniqueName(functionNameToId, ambiguousFunctionNames, wrapped.Name, wrapped.Id);
                    calleeToCallers[wrapped.Id] = new List<string>();
                    callerToCallees[wrapped.Id] = new List<(string, TextSpan)>();
                }
            }
        }

        var (resolvedCallIds, boundCallSites, boundResolutionComplete, boundValueTypes,
             declaredFunctionTypes, declaredReturnTypes, declaredFieldTypes, boundValues) =
            ResolveBoundCallSites(ast, functions, crossModuleFunctions, contractInheritance, projectionContractMode,
                crossModuleTypes, sourcePath);

        foreach (var (id, declaration) in boundValues.ExternalSourceFunctions)
        {
            functions[id] = declaration;
            calleeToCallers[id] = [];
            callerToCallees[id] = [];
        }

        return new(functions, functionNameToId, methodNameToIds, calleeToCallers, callerToCallees,
            contractInheritance, resolvedCallIds, boundCallSites, boundResolutionComplete, boundValueTypes,
            declaredFunctionTypes, declaredReturnTypes, declaredFieldTypes, boundValues);
    }

    /// <summary>
    /// What an effect summary reads of a module's call graph: its declarations as
    /// functions, its contract inheritance and, when binding succeeded, the plain bound
    /// module (the one <see cref="PlainBoundModule"/> returns). These are exactly the
    /// values <see cref="Build(ModuleNode, IReadOnlyDictionary{string, CodeGen.CrossModuleFunctionTarget}?, Verification.ModuleInheritanceResult?, ContractMode, CrossModuleTypeRegistry?, string?)"/>
    /// computes for the same module and map before it resolves operator calls, and
    /// they do not depend on that resolution, which binds a C# image of the module
    /// with Roslyn and is the most expensive part of the call graph (#1738).
    /// </summary>
    internal sealed record DeclarationScan(
        ModuleNode Module,
        IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? CrossModuleFunctions,
        IReadOnlyDictionary<string, FunctionNode> Functions,
        Verification.ModuleInheritanceResult ContractInheritance,
        BoundModule? PlainModule);

    internal static DeclarationScan ScanDeclarations(ModuleNode ast,
        IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions)
    {
        var phase = BuildDeclarations(ast, crossModuleFunctions, null, ContractMode.Debug, null, null);
        return new(ast, crossModuleFunctions, phase.Functions, phase.ContractInheritance, phase.BoundValues.Module);
    }

    /// <summary>
    /// Builds a call graph analysis from a module AST.
    /// </summary>
    public static CallGraphAnalysis Build(ModuleNode ast) => Build(ast, null);

    internal static CallGraphAnalysis Build(ModuleNode ast,
        IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions,
        Verification.ModuleInheritanceResult? contractInheritance = null,
        ContractMode projectionContractMode = ContractMode.Debug,
        CrossModuleTypeRegistry? crossModuleTypes = null, string? sourcePath = null)
    {
        var (functions, functionNameToId, methodNameToIds, calleeToCallers, callerToCallees,
            phaseInheritance, resolvedCallIds, boundCallSites, boundResolutionComplete, boundValueTypes,
            declaredFunctionTypes, declaredReturnTypes, declaredFieldTypes, boundValues) =
            BuildDeclarations(ast, crossModuleFunctions, contractInheritance, projectionContractMode,
                crossModuleTypes, sourcePath);
        contractInheritance = phaseInheritance;

        // Build call edges
        var unresolvedCalls = new List<AstUnresolvedCallSite>();
        foreach (var function in functions.Values)
        {
            var calls = CollectCalls(function);
            callerToCallees[function.Id] = calls;

            foreach (var (callee, span) in calls)
            {
                var key = new AstCallKey(function.Id, callee, span.Start, span.End);
                List<string> calleeIds = resolvedCallIds.TryGetValue(key, out var exactIds)
                    ? exactIds.ToList()
                    : boundCallSites.Contains(key)
                        ? []
                        : ResolveToAllInternalIds(
                            callee,
                            functions,
                            functionNameToId,
                            methodNameToIds);
                if (calleeIds.Count == 0
                    && !boundValues.LocalFunctionCalls.Contains((function.Id, span.Start, span.End))
                    && !boundValues.MetadataCalls.Contains((function.Id, span.Start, span.End)))
                {
                    unresolvedCalls.Add(new AstUnresolvedCallSite(function.Id, callee, span));
                }
                foreach (var calleeId in calleeIds)
                {
                    if (!calleeToCallers.ContainsKey(calleeId))
                        calleeToCallers[calleeId] = new List<string>();
                    calleeToCallers[calleeId].Add(function.Id);
                }
            }
        }

        foreach (var (site, field) in boundValues.SourceFieldAccesses)
        {
            if (field.SourceStaticFieldOwner is not { } fieldOwner)
                continue;
            foreach (var constructor in fieldOwner.Constructors.Where(constructor => constructor.IsStatic))
            {
                var id = $"{fieldOwner.Name}.{constructor.Id}";
                if (!functions.TryGetValue(id, out var initializer))
                    continue;
                callerToCallees[site.CallerId].Add((initializer.Name, field.Span));
                calleeToCallers[id].Add(site.CallerId);
                var key = new AstCallKey(site.CallerId, initializer.Name, field.Span.Start, field.Span.End);
                resolvedCallIds[key] = [id];
                boundCallSites.Add(key);
            }
        }

        // Without cross-module functions this is exactly the binder's own call (same module,
        // contract mode, cross-module types and source path), so its result is reused rather
        // than compiling and binding the projection a second time.
        var operatorBindings = crossModuleFunctions == null && boundValues.SourceOperatorBindings is { } computed
            ? computed
            : ResolveOperatorCalls(ast, contractMode: projectionContractMode,
                singleModuleContext: crossModuleFunctions == null,
                crossModuleTypes: crossModuleTypes, sourcePath: sourcePath,
                crossModuleFunctions: crossModuleFunctions);
        foreach (var (site, call) in boundValues.ComputedMemberCalls)
        {
            if (call.SourceStaticMethodOwner is not { } owner)
                continue;
            foreach (var constructor in owner.Constructors.Where(constructor => constructor.IsStatic))
            {
                var id = $"{owner.Name}.{constructor.Id}";
                if (!functions.TryGetValue(id, out var initializer))
                    continue;
                callerToCallees[site.CallerId].Add((initializer.Name, call.Span));
                calleeToCallers[id].Add(site.CallerId);
                var key = new AstCallKey(site.CallerId, initializer.Name, call.Span.Start, call.Span.End);
                resolvedCallIds[key] = [id];
                boundCallSites.Add(key);
            }
        }
        // 0.30: a static member of a type with an explicit static constructor may run that
        // constructor (C# spec, static constructors: a static member reference triggers it),
        // and the effect pass charges the member for it. The edge keeps the pair in one SCC
        // when the constructor calls the member back.
        foreach (var (memberId, owner) in StaticConstructorTriggers(ast))
        {
            if (!functions.TryGetValue(memberId, out var member))
                continue;
            foreach (var constructor in StaticConstructorsOf(ast, owner))
            {
                var id = $"{owner.Name}.{constructor.Id}";
                if (!functions.TryGetValue(id, out var initializer) || id == memberId)
                    continue;
                callerToCallees[memberId].Add((initializer.Name, member.Span));
                calleeToCallers[id].Add(memberId);
                var key = new AstCallKey(memberId, initializer.Name, member.Span.Start, member.Span.End);
                resolvedCallIds[key] = [id];
                boundCallSites.Add(key);
            }
        }
        var operatorCalls = operatorBindings.SourceCalls;
        foreach (var function in functions.Values)
        {
            var pending = new Stack<AstNode>();
            pending.Push(function);
            while (pending.TryPop(out var node))
            {
                foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                    pending.Push(child);
                if (!operatorCalls.TryGetValue(node, out var targets))
                    continue;
                foreach (var callee in targets)
                {
                    callerToCallees[function.Id].Add((callee, node.Span));
                    calleeToCallers[callee].Add(function.Id);
                    var key = new AstCallKey(function.Id, callee, node.Span.Start, node.Span.End);
                    resolvedCallIds[key] = [callee];
                    boundCallSites.Add(key);
                }
            }
        }

        // Compute SCCs
        var sccs = ComputeSccs(
            functions,
            callerToCallees,
            functionNameToId,
            methodNameToIds,
            resolvedCallIds,
            boundCallSites);

        var analysis = new CallGraphAnalysis(
            callerToCallees,
            calleeToCallers,
            functions,
            functionNameToId,
            methodNameToIds,
            unresolvedCalls,
            resolvedCallIds,
            boundCallSites,
            boundResolutionComplete,
            boundValueTypes,
            sccs);
        analysis._declaredFunctionTypes = declaredFunctionTypes;
        analysis._declaredReturnFunctionTypes = declaredReturnTypes;
        analysis._declaredFieldFunctionTypes = declaredFieldTypes;
        analysis._boundValues = boundValues;
        analysis._operatorBindings = operatorBindings;
        analysis.ContractInheritance = contractInheritance;
        if (crossModuleTypes is null && sourcePath is null && projectionContractMode == ContractMode.Debug
            && boundValues.Module is { } plainModule)
            analysis._plainBinding = (ast, crossModuleFunctions, contractInheritance, plainModule);
        return analysis;
    }

    private (ModuleNode Ast, IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? CrossModuleFunctions,
        Verification.ModuleInheritanceResult? ContractInheritance, BoundModule Module)? _plainBinding;

    /// <summary>
    /// The module this analysis bound, when that binding is exactly the one
    /// <c>new Binder(bag) { CrossModuleFunctionModules = crossModuleFunctions }
    /// .Bind(ast, contractInheritance)</c> performs: the same module, the same
    /// cross-module map and inheritance result, no source path, no project source
    /// registry, and the default projection contract mode. Binding is a function of
    /// those inputs (it does not change the AST), so a caller that would bind again
    /// with them can read this result instead. Null otherwise, or when binding threw.
    /// </summary>
    internal BoundModule? PlainBoundModule(ModuleNode ast,
        IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions,
        Verification.ModuleInheritanceResult? contractInheritance) =>
        _plainBinding is { } binding && ReferenceEquals(binding.Ast, ast)
            && ReferenceEquals(binding.CrossModuleFunctions, crossModuleFunctions)
            && ReferenceEquals(binding.ContractInheritance, contractInheritance)
            ? binding.Module
            : null;

    internal IReadOnlyDictionary<string, FunctionNode> ExternalSourceFunctions =>
        _boundValues.ExternalSourceFunctions;

    private static (
        Dictionary<AstCallKey, IReadOnlyList<string>> Resolved,
        HashSet<AstCallKey> BoundCallSites,
        bool Complete,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>>
            BoundValueTypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>>
            DeclaredFunctionTypes,
        IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType> DeclaredReturnTypes,
        IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType> DeclaredFieldTypes,
        BoundValueIndex BoundValues)
        ResolveBoundCallSites(
            ModuleNode ast,
            IReadOnlyDictionary<string, FunctionNode> functions,
            IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions,
            Verification.ModuleInheritanceResult contractInheritance,
            ContractMode projectionContractMode,
            CrossModuleTypeRegistry? crossModuleTypes,
            string? sourcePath)
    {
        var resolved = new Dictionary<AstCallKey, IReadOnlyList<string>>();
        var boundCallSites = new HashSet<AstCallKey>();
        // callerId -> name -> bound type. A name whose bound answers disagree is
        // recorded as null (ambiguous) and dropped at the end: better to fall
        // back to the AST string than to hand a shadowed name the wrong scope's
        // type. E1 slice 2b.
        var valueTypes =
            new Dictionary<string, Dictionary<string, Binding.BoundTypes.BoundType?>>(
                StringComparer.Ordinal);

        // v0.15 E3 slice b, §8.2 — the DECLARED function types, collected from the
        // same bound module. Unlike `valueTypes` these are not receiver-derived and
        // have no ambiguity rule: a parameter name is unique within its function,
        // and a field name within its class.
        var declaredTypes =
            new Dictionary<string, Dictionary<string, Binding.BoundTypes.FunctionBoundType>>(
                StringComparer.Ordinal);
        var declaredReturns =
            new Dictionary<string, Binding.BoundTypes.FunctionBoundType>(StringComparer.Ordinal);
        var declaredFields =
            new Dictionary<string, Binding.BoundTypes.FunctionBoundType>(StringComparer.Ordinal);
        var boundValues = new BoundValueIndex();

        void RecordValue(string callerId, string name, Binding.BoundTypes.BoundType type)
        {
            if (string.IsNullOrEmpty(name))
                return;
            if (!valueTypes.TryGetValue(callerId, out var map))
            {
                map = new Dictionary<string, Binding.BoundTypes.BoundType?>(StringComparer.Ordinal);
                valueTypes[callerId] = map;
            }
            if (!map.TryGetValue(name, out var existing))
            {
                map[name] = type;
                return;
            }
            if (existing is null || existing.Equals(type))
                return;
            // Two different bound answers for one name in one function: the AST
            // path decides, as it did before this slice.
            map[name] = null;
        }

        // Native member calls carry an evaluated receiver, not a dotted target.
        // Only named receiver paths can feed this name-keyed side channel.
        // This is an identity classifier, not traversal: computed receivers cannot
        // borrow a name from their children, and unsupported forms return no path.
        static string? ReceiverPath(BoundExpression receiver) => receiver switch
        {
            BoundVariableExpression variable => variable.Variable.Name,
            BoundFieldAccessExpression field when ReceiverPath(field.Target) is { } parent
                => $"{parent}.{field.FieldName}",
            _ => null
        };

        void RecordReceiver(
            string callerId,
            string target,
            Binding.BoundExpression? receiverExpression)
        {
            if (receiverExpression is null)
                return;
            if (target.LastIndexOf('.') is > 0 and var lastDot)
                RecordValue(callerId, target[..lastDot], receiverExpression.Type);
            else if (ReceiverPath(receiverExpression) is { } path)
                RecordValue(callerId, path, receiverExpression.Type);
        }

        try
        {
            var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
            var binder = new Binder(diagnostics, sourcePath)
            {
                CrossModuleFunctionModules = crossModuleFunctions,
                CrossModuleTypes = crossModuleTypes,
                ProjectionContractMode = projectionContractMode
            };
            var boundModule = binder.Bind(ast, contractInheritance);
            boundValues.Complete = true;
            boundValues.Module = boundModule;
            boundValues.SourceOperatorBindings = binder.ComputedSourceCallBindings(ast);
            var incompatibleCallSpans = diagnostics
                .Where(diagnostic =>
                    diagnostic.Code is Calor.Compiler.Diagnostics.DiagnosticCode.NoMatchingOverload
                        or Calor.Compiler.Diagnostics.DiagnosticCode.AmbiguousOverload)
                .Select(diagnostic => (diagnostic.Span.Start, diagnostic.Span.End))
                .ToHashSet();
            var legacyIds = boundModule.Functions
                .Select(function => (
                    function.SymbolId,
                    LegacyId: ResolveLegacyFunctionId(function, functions)))
                .Where(item => item.LegacyId != null)
                .ToDictionary(item => item.SymbolId, item => item.LegacyId!);
            foreach (var (id, selected) in binder.ExternalSourceCalls)
            {
                var method = selected.Declaration;
                legacyIds[id] = id.Value;
                boundValues.ExternalSourceFunctions[id.Value] = new FunctionNode(
                    method.Span, id.Value, selected.Symbol.Name, method.Visibility,
                    method.TypeParameters, method.Parameters, method.Output, method.Effects,
                    [], [], [], method.Attributes)
                {
                    EffectParameters = method.EffectParameters
                };
            }
            var typeDeclarations = boundModule.SymbolsById.Values.OfType<TypeSymbol>()
                .ToLookup(symbol => (symbol.DefinitionSpan, symbol.DeclarationSpan));
            var callableDeclarations = boundModule.SymbolsById.Values.OfType<FunctionSymbol>()
                .ToLookup(symbol => (symbol.ContainingTypeName, symbol.DefinitionSpan, symbol.DeclarationSpan));
            var sourceClasses = new Dictionary<SymbolId, ClassDefinitionNode>();
            // Abstract and empty members have declaration symbols but no BoundFunction body.
            foreach (var cls in EnumerateClasses(ast))
            {
                var owners = typeDeclarations[(cls.Span, cls.IdentifierSpan)]
                    .Where(symbol => symbol.Name == cls.Name).Take(2).ToArray();
                if (owners is not [var owner])
                    continue;
                sourceClasses[owner.Id] = cls;
                foreach (var method in EnumerateMethods(cls))
                {
                    var declarations = callableDeclarations[(owner.QualifiedName, method.Span, method.IdentifierSpan)]
                        .Take(2).ToArray();
                    if (declarations is [var declaration])
                        legacyIds.TryAdd(declaration.Id, $"{cls.Name}.{method.Id}");
                }
                foreach (var constructor in EnumerateConstructors(cls))
                {
                    var declarations = callableDeclarations[(owner.QualifiedName, constructor.Span, cls.IdentifierSpan)]
                        .Take(2).ToArray();
                    if (declarations is [var declaration])
                        legacyIds.TryAdd(declaration.Id, $"{cls.Name}.{constructor.Id}");
                }
            }
            foreach (var declaration in EnumerateInterfaces(ast))
            {
                var owners = typeDeclarations[(declaration.Span, declaration.IdentifierSpan)]
                    .Where(symbol => symbol.Name == declaration.Name).Take(2).ToArray();
                if (owners is not [var owner])
                    continue;
                foreach (var method in declaration.Methods)
                {
                    var symbols = callableDeclarations[(owner.QualifiedName, method.Span, method.IdentifierSpan)]
                        .Take(2).ToArray();
                    if (symbols is [var symbol])
                        legacyIds.TryAdd(symbol.Id, $"{declaration.Name}.{method.Id}");
                }
            }
            // 0.25 F3 (#847): a local body is indexed under its enclosing callable's id.
            var callerIds = new Dictionary<SymbolId, string>(legacyIds);
            var localFunctionIds = new HashSet<SymbolId>();
            foreach (var local in boundModule.Functions.Where(function =>
                         function.MemberKind == BoundMemberKind.LocalFunction))
            {
                localFunctionIds.Add(local.SymbolId);
                if (local.EnclosingSymbolId is { } enclosing
                    && legacyIds.TryGetValue(enclosing, out var enclosingId))
                    callerIds[local.SymbolId] = enclosingId;
            }

            foreach (var symbol in boundModule.SymbolsById.Values.Concat(
                         binder.ExternalSourceCalls.Values.Select(selected => selected.Symbol)))
            {
                if (symbol is FunctionSymbol callable && legacyIds.TryGetValue(callable.Id, out var callableId))
                {
                    if (callable.ReturnFunctionType is { } returnFunctionType)
                        declaredReturns[callableId] = returnFunctionType;
                    foreach (var parameter in callable.Parameters)
                    {
                        if (parameter.FunctionType is not { } parameterType)
                            continue;
                        if (!declaredTypes.TryGetValue(callableId, out var byName))
                            declaredTypes[callableId] = byName = new(StringComparer.Ordinal);
                        byName[parameter.Name] = parameterType;
                    }
                }
                if (symbol is not Binding.VariableSymbol
                    {
                        IsField: true,
                        FunctionType: { } fieldType,
                        DeclaringTypeName: { } owner,
                    } field)
                {
                    continue;
                }
                declaredFields[$"{owner}.{field.Name}"] = fieldType;
            }

            foreach (var function in boundModule.Functions)
            {
                if (!callerIds.TryGetValue(function.SymbolId, out var callerId))
                    continue;

                foreach (var node in DescendantsAndSelf(function))
                {
                    if (node is BoundFieldAccessExpression
                        { ResolvedMetadataMember: Microsoft.CodeAnalysis.IFieldSymbol metadataField })
                        boundValues.MetadataFields.TryAdd((callerId, node.Span.Start, node.Span.End), metadataField);
                    if (node is BoundFieldAccessExpression
                        {
                            Target: BoundTypeReferenceExpression,
                            ResolvedMetadataMember: Microsoft.CodeAnalysis.IFieldSymbol { IsStatic: true }
                        })
                        boundValues.TypeQualifiedMetadataFields.Add((callerId, node.Span.Start, node.Span.End));
                    if (node is BoundFieldAccessExpression { Target: BoundTypeReferenceExpression })
                        boundValues.TypeQualifiedMemberReads.Add((callerId, node.Span.Start, node.Span.End));
                    if (node is BoundTypeReferenceExpression)
                        boundValues.TypeReferences.Add((callerId, node.Span.Start, node.Span.End));
                    if (node is BoundFieldAccessExpression
                        {
                            Target.Type: Binding.BoundTypes.NominalBoundType
                                { Declaration: { } memberOwner, RoslynSymbol: null }
                        })
                        boundValues.SourceMemberOwners.TryAdd(
                            (callerId, node.Span.Start, node.Span.End), memberOwner.Name);
                    if (node is BoundFieldAccessExpression { UnsupportedMetadataMember: { } unsupportedMember })
                        boundValues.UnsupportedMetadataMembers.TryAdd(
                            (callerId, node.Span.Start, node.Span.End), unsupportedMember);
                    if (node is BoundFieldAccessExpression
                        {
                            Target: BoundTypeReferenceExpression { ResolvedType.IsEnum: true },
                            ResolvedField: { IsEnumMember: true } sourceConstant
                        })
                        boundValues.SourceEnumConstants.TryAdd((callerId, node.Span.Start, node.Span.End), sourceConstant);
                    if (node is BoundFieldAccessExpression { IsTupleElementRead: true })
                        boundValues.TupleElementReads.Add((callerId, node.Span.Start, node.Span.End));
                    if (node is BoundFieldAccessExpression { IsAnonymousPropertyRead: true } anonymousProperty)
                        boundValues.AnonymousPropertyReads.TryAdd(
                            (callerId, node.Span.Start, node.Span.End), anonymousProperty);
                    if (node is BoundFieldAccessExpression sourceField
                        && (sourceField.SourceStaticFieldOwner is not null
                            || sourceField is
                                { ResolvedField: { IsField: true, IsStatic: false },
                                    Target.Type: Binding.BoundTypes.NominalBoundType { Declaration: not null } }))
                        boundValues.SourceFieldAccesses.TryAdd(
                            (callerId, node.Span.Start, node.Span.End), sourceField);
                    if (node is BoundFieldAccessExpression
                        {
                            Target: BoundTypeReferenceExpression,
                            ResolvedMetadataMember: Microsoft.CodeAnalysis.IFieldSymbol
                            { IsConst: true, Type.TypeKind: Microsoft.CodeAnalysis.TypeKind.Enum } constant
                        })
                        boundValues.MetadataEnumConstants.TryAdd((callerId, node.Span.Start, node.Span.End), constant);
                    if (node is BoundFieldAccessExpression
                        { Target.Type: Binding.BoundTypes.NominalBoundType { RoslynSymbol: not null } memberReceiver })
                        boundValues.MetadataMemberReceiverTypes.TryAdd(
                            (callerId, node.Span.Start, node.Span.End), memberReceiver);
                    if (node is BoundFieldAccessExpression
                        {
                            Target.MethodInputArrayType: not null,
                            ResolvedMetadataMember: Microsoft.CodeAnalysis.IPropertySymbol arrayProperty
                        })
                        boundValues.MetadataMemberReceiverTypes.TryAdd(
                            (callerId, node.Span.Start, node.Span.End),
                            Binding.Metadata.MetadataBinderResult.ToNominalBoundType(arrayProperty.ContainingType));
                    if (node is BoundExpression
                        { Type: Binding.BoundTypes.NominalBoundType { RoslynSymbol: { } metadataValueType } })
                        boundValues.MetadataValueTypes.TryAdd(
                            (callerId, node.Span.Start, node.Span.End), metadataValueType);
                    if (node is BoundArrayAccess elementAccess)
                        boundValues.ElementAccesses.TryAdd((callerId, node.Span.Start, node.Span.End), elementAccess);
                    if (node is BoundExpression arrayExpression)
                    {
                        var arrayType = arrayExpression.MetadataArrayResultType is { } metadataArray
                            ? Binding.Metadata.MetadataBinderResult.ToBoundType(metadataArray) as Binding.BoundTypes.ArrayBoundType
                            : arrayExpression.MethodInputArrayType;
                        if (arrayType is not null)
                            boundValues.ArrayTypes.TryAdd(
                                (callerId, node.Span.Start, node.Span.End), arrayType);
                    }
                    string? target = null;
                    IReadOnlyList<FunctionSymbol>? callees = null;
                    VariableSymbol? receiver = null;
                    var inaccessibleCall = false;
                    var blocksNameFallback = false;
                    var unresolvedArguments = false;
                    var expressionTargetCall = false;
                    switch (node)
                    {
                        // v0.15 E4 — the bound identity of values, for the
                        // invocation charge (F1/F2): every local with a declared
                        // function type, and every bare reference's resolved
                        // declaration.
                        case BoundBindStatement bind when bind.Variable.FunctionType is { } localType:
                            if (!boundValues.Locals.TryGetValue(callerId, out var locals))
                            {
                                locals = new List<(string, int, Binding.BoundTypes.FunctionBoundType)>();
                                boundValues.Locals[callerId] = locals;
                            }
                            locals.Add((bind.Variable.Name, bind.Variable.DeclarationSpan.Start, localType));
                            break;
                        case BoundVariableExpression variable:
                            boundValues.Declarations[(callerId, node.Span.Start, node.Span.End)] =
                                variable.Variable.DeclarationSpan;
                            boundValues.Variables[(callerId, node.Span.Start, node.Span.End)] =
                                variable.Variable.StorageIdentity;
                            break;
                        case BoundFieldAccessExpression { ResolvedField: { EventDeclaration: not null } field }:
                            boundValues.Variables[(callerId, node.Span.Start, node.Span.End)] = field.StorageIdentity;
                            break;
                        case BoundCallStatement statement:
                            target = statement.Target;
                            callees = statement.ResolvedSymbols;
                            receiver = statement.ReceiverSymbol;
                            inaccessibleCall = statement.IsInaccessibleCall;
                            blocksNameFallback = statement.HasOpaqueOverloads || statement.HasUnresolvedArguments;
                            unresolvedArguments = statement.HasUnresolvedArguments;
                            RecordReceiver(callerId, statement.Target, statement.Receiver);
                            if (receiver != null && !target.Contains('.'))
                                boundValues.Declarations[(callerId, node.Span.Start, node.Span.End)] =
                                    receiver.DeclarationSpan;
                            break;
                        case BoundCallExpression expression:
                            target = expression.Target;
                            callees = expression.ResolvedSymbols;
                            receiver = expression.ReceiverSymbol;
                            inaccessibleCall = expression.IsInaccessibleCall;
                            blocksNameFallback = expression.HasOpaqueOverloads || expression.HasUnresolvedArguments;
                            unresolvedArguments = expression.HasUnresolvedArguments;
                            if (expression.EvaluatedReceiver is not null)
                            {
                                expressionTargetCall = true;
                                boundValues.ComputedMemberCalls[(callerId, node.Span.Start, node.Span.End)] = expression;
                            }
                            if (IsProvablyMetadataCall(expression))
                                boundValues.MetadataCalls.Add((callerId, node.Span.Start, node.Span.End));
                            RecordReceiver(callerId, expression.Target, expression.Receiver);
                            if (receiver != null && !target.Contains('.'))
                                boundValues.Declarations[(callerId, node.Span.Start, node.Span.End)] =
                                    receiver.DeclarationSpan;
                            break;
                        case BoundNewExpression creation:
                            target = $"{creation.Type.DisplayString}..ctor";
                            if (creation.ResolvedType is { } sourceType
                                && sourceClasses.TryGetValue(sourceType.Id, out var sourceClass))
                                boundValues.SourceCreationTypes[(callerId, node.Span.Start, node.Span.End)] = sourceClass;
                            callees = creation.ResolvedConstructor == null
                                ? Array.Empty<FunctionSymbol>()
                                : [creation.ResolvedConstructor];
                            foreach (var initializer in creation.CollectionInitializers)
                                boundValues.ComputedMemberCalls[
                                    (callerId, initializer.Span.Start, initializer.Span.End)] = initializer;
                            break;
                        case BoundExpressionCallExpression:
                            target = "<expression-call>";
                            expressionTargetCall = true;
                            break;
                        case BoundExpressionCall:
                            target = "<expression-call>";
                            expressionTargetCall = true;
                            break;
                    }

                    if (target == null)
                        continue;

                    if (unresolvedArguments)
                        boundValues.UnresolvedArgumentCalls.Add((callerId, node.Span.Start, node.Span.End));
                    var key = new AstCallKey(callerId, target, node.Span.Start, node.Span.End);
                    if (callees?.Any(callee => localFunctionIds.Contains(callee.Id)) == true)
                    {
                        boundCallSites.Add(key);
                        boundValues.LocalFunctionCalls.Add((callerId, node.Span.Start, node.Span.End));
                        continue;
                    }
                    var calleeLegacyIds = callees?
                        .Select(callee => callee.Id)
                        .Where(calleeId => !calleeId.IsNone)
                        .Where(legacyIds.ContainsKey)
                        .Select(calleeId => legacyIds[calleeId])
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                        ?? Array.Empty<string>();
                    if (calleeLegacyIds.Length > 0)
                    {
                        var matches = node switch
                        {
                            BoundCallExpression expression => expression.SelectedOverloadMatches,
                            BoundCallStatement statement => statement.SelectedOverloadMatches,
                            _ => []
                        };
                        if (matches is [var match] && legacyIds.TryGetValue(match.Function.Id, out var selectedId))
                            boundValues.SourceArguments[(callerId, node.Span.Start, node.Span.End)] =
                                new SourceArgumentSelection(selectedId, match.Arguments);
                        if (node is BoundNewExpression || target.EndsWith("..ctor", StringComparison.Ordinal))
                            boundValues.ConstructorCalls[(callerId, node.Span.Start, node.Span.End)] = calleeLegacyIds;
                        boundCallSites.Add(key);
                        resolved[key] = calleeLegacyIds;
                    }
                    else if (expressionTargetCall
                             || receiver != null
                             || inaccessibleCall
                             || blocksNameFallback
                             || incompatibleCallSpans.Contains((node.Span.Start, node.Span.End)))
                    {
                        boundCallSites.Add(key);
                    }
                }
            }
        }
        catch
        {
            // AST-only compatibility remains available. Calls that could not be
            // bound fall back only to unambiguous name resolution below. No
            // bound types either: the pass keeps its pre-slice-2b behaviour.
            return (
                resolved,
                boundCallSites,
                false,
                new Dictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>>(
                    StringComparer.Ordinal),
                new Dictionary<
                    string,
                    IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>>(
                    StringComparer.Ordinal),
                NoFunctionTypes,
                NoFunctionTypes,
                new BoundValueIndex());
        }

        return (
            resolved,
            boundCallSites,
            true,
            FreezeValueTypes(valueTypes),
            declaredTypes.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>)entry.Value,
                StringComparer.Ordinal),
            declaredReturns,
            declaredFields,
            boundValues);
    }

    /// <summary>
    /// Drops the ambiguous entries (a name with two different bound answers in
    /// one function) and hands back a read-only view. E1 slice 2b.
    /// </summary>
    private static IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>>
        FreezeValueTypes(
            Dictionary<string, Dictionary<string, Binding.BoundTypes.BoundType?>> valueTypes)
    {
        var frozen =
            new Dictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>>(
                StringComparer.Ordinal);
        foreach (var (callerId, map) in valueTypes)
        {
            var unambiguous = new Dictionary<string, Binding.BoundTypes.BoundType>(
                StringComparer.Ordinal);
            foreach (var (name, type) in map)
            {
                if (type is not null)
                    unambiguous[name] = type;
            }
            frozen[callerId] = unambiguous;
        }
        return frozen;
    }

    private static string? ResolveLegacyFunctionId(
        BoundFunction boundFunction,
        IReadOnlyDictionary<string, FunctionNode> functions)
    {
        var candidates = functions.Values
            .Where(candidate => candidate.Span == boundFunction.Span)
            .ToArray();
        var exactName = candidates.FirstOrDefault(candidate =>
            candidate.Name.Equals(boundFunction.Symbol.Name, StringComparison.Ordinal));
        if (exactName != null)
            return exactName.Id;
        return candidates.Length == 1 ? candidates[0].Id : null;
    }

    private static void AddUniqueName(
        Dictionary<string, string> names,
        HashSet<string> ambiguousNames,
        string name,
        string id)
    {
        if (ambiguousNames.Contains(name))
            return;

        if (names.TryAdd(name, id))
            return;

        names.Remove(name);
        ambiguousNames.Add(name);
    }

    /// <summary>
    /// Resolves a call target to a single internal function ID, or null if ambiguous/external.
    /// </summary>
    public string? ResolveToInternalId(string callee)
    {
        if (FunctionNameToId.TryGetValue(callee, out var id) && Functions.ContainsKey(id))
            return id;
        if (Functions.ContainsKey(callee))
            return callee;

        var lastDot = callee.LastIndexOf('.');
        if (lastDot > 0)
        {
            var bareMethodName = callee[(lastDot + 1)..];
            if (MethodNameToIds.TryGetValue(bareMethodName, out var candidates) && candidates.Count > 1)
                return null;
            if (FunctionNameToId.TryGetValue(bareMethodName, out var bareId) && Functions.ContainsKey(bareId))
                return bareId;
        }

        return null;
    }

    /// <summary>
    /// Gets direct callers of a function by ID.
    /// </summary>
    public List<string> GetCallers(string functionId)
    {
        return ReverseGraph.TryGetValue(functionId, out var callers) ? callers : new List<string>();
    }

    /// <summary>
    /// Gets direct callees of a function by ID (resolved to internal IDs where possible).
    /// </summary>
    public List<(string CalleeId, string CalleeName, TextSpan Span)> GetCallees(string functionId)
    {
        var result = new List<(string, string, TextSpan)>();
        if (!ForwardGraph.TryGetValue(functionId, out var calls))
            return result;

        foreach (var (callee, span) in calls)
        {
            var resolvedIds = ResolveCallSiteIds(functionId, callee, span);
            if (resolvedIds.Count == 0)
                result.Add((callee, callee, span));
            else
                result.AddRange(resolvedIds.Select(resolvedId => (resolvedId, callee, span)));
        }
        return result;
    }

    public string? ResolveCallSite(string callerId, string target, TextSpan span)
    {
        var resolved = ResolveCallSiteIds(callerId, target, span);
        return resolved.Count == 1 ? resolved[0] : null;
    }

    public IReadOnlyList<string> ResolveCallSites(
        string callerId,
        string target,
        TextSpan span) =>
        ResolveCallSiteIds(callerId, target, span);

    public bool IsBinderResolvedCallSite(string callerId, string target, TextSpan span)
        => _resolvedCallIds.ContainsKey(
            new AstCallKey(callerId, target, span.Start, span.End));

    private IReadOnlyList<string> ResolveCallSiteIds(
        string callerId,
        string target,
        TextSpan span)
    {
        var key = new AstCallKey(callerId, target, span.Start, span.End);
        if (_resolvedCallIds.TryGetValue(key, out var resolved))
            return resolved;
        if (_boundCallSites.Contains(key))
            return Array.Empty<string>();
        var fallback = ResolveToInternalId(target);
        return fallback == null ? Array.Empty<string>() : [fallback];
    }

    private static FunctionNode ToFunctionNode(MethodNode method, string className,
        Verification.ModuleInheritanceResult contractInheritance)
    {
        var qualifiedId = $"{className}.{method.Id}";
        var inherited = !method.HasContracts
            ? contractInheritance.RuntimeContracts.GetValueOrDefault(method) : null;
        return new FunctionNode(
            method.Span,
            qualifiedId,
            method.Name,
            method.Visibility,
            method.TypeParameters,
            method.Parameters,
            method.Output,
            method.Effects,
            inherited?.Preconditions ?? method.Preconditions,
            inherited?.Postconditions ?? method.Postconditions,
            method.Body,
            method.Attributes)
        {
            // v0.15 E3 slice b — the `eff` binders travel with the method, or
            // site 6 cannot see that the callee is effect-polymorphic at all.
            EffectParameters = method.EffectParameters,
        };
    }

    private static FunctionNode ToCtorFunctionNode(ConstructorNode ctor, string className)
    {
        var qualifiedId = $"{className}.{ctor.Id}";
        return new FunctionNode(
            ctor.Span,
            qualifiedId,
            $"{className}.{(ctor.IsStatic ? ".cctor" : ".ctor")}",
            ctor.Visibility,
            ctor.Parameters,
            output: null,
            effects: ctor.Effects,
            ctor.Preconditions,
            Array.Empty<EnsuresNode>(),
            ctor.Body,
            ctor.Attributes);
    }

    internal static string GetPropertyAccessorFunctionId(
        string className,
        PropertyNode property,
        PropertyAccessorNode accessor)
        => $"{className}.{property.Id}.{accessor.Kind.ToString().ToLowerInvariant()}";

    internal static string GetEventAccessorFunctionId(
        string className,
        EventDefinitionNode evt,
        bool isAdd)
        => $"{className}.{evt.Id}.{(isAdd ? "add" : "remove")}";

    private static FunctionNode ToPropertyAccessorFunctionNode(
        PropertyNode property,
        PropertyAccessorNode accessor,
        string className)
    {
        var parameters = accessor.Kind == PropertyAccessorNode.AccessorKind.Get
            ? Array.Empty<ParameterNode>()
            : [new ParameterNode(
                accessor.Span,
                "value",
                property.TypeName,
                new AttributeCollection())];
        return new FunctionNode(
            accessor.Span,
            GetPropertyAccessorFunctionId(className, property, accessor),
            $"{className}.{property.Name}.{accessor.Kind.ToString().ToLowerInvariant()}",
            accessor.Visibility ?? property.Visibility,
            parameters,
            output: null,
            effects: accessor.Effects,
            accessor.Preconditions,
            Array.Empty<EnsuresNode>(),
            accessor.Body,
            accessor.Attributes);
    }

    private static FunctionNode ToEventAccessorFunctionNode(
        EventDefinitionNode evt,
        bool isAdd,
        string className)
    {
        var parameter = new ParameterNode(
            evt.Span,
            "value",
            evt.DelegateType,
            new AttributeCollection());
        return new FunctionNode(
            evt.Span,
            GetEventAccessorFunctionId(className, evt, isAdd),
            $"{className}.{evt.Name}.{(isAdd ? "add" : "remove")}",
            evt.Visibility,
            [parameter],
            output: null,
            effects: null,
            isAdd ? evt.AddBody! : evt.RemoveBody!,
            evt.Attributes);
    }

    private static List<(string Callee, TextSpan Span)> CollectCalls(FunctionNode function)
    {
        var calls = new List<(string, TextSpan)>();
        foreach (var contract in RefinementPreconditions.Effective(function.Parameters, function.Preconditions)
                     .Select(precondition => precondition.Condition)
                     .Concat(function.Postconditions.Select(postcondition => postcondition.Condition)))
            CollectCallsFromNode(contract, calls);
        foreach (var statement in function.Body)
            CollectCallsFromNode(statement, calls);
        return calls;
    }

    private static void CollectCallsFromNode(AstNode node, List<(string, TextSpan)> calls)
    {
        switch (node)
        {
            case CallStatementNode call:
                calls.Add((call.Target, call.Span));
                break;
            case CallExpressionNode call:
                calls.Add((call.Target, call.Span));
                break;
            case NewExpressionNode creation:
                calls.Add(($"{creation.TypeName}..ctor", creation.Span));
                break;
            case ExpressionCallNode expressionCall:
                calls.Add(("<expression-call>", expressionCall.Span));
                break;
            case LocalFunctionStatementNode local:
                // 0.25 F3 (#847): a leaf for the walker; its calls are the enclosing callable's.
                calls.AddRange(CollectCalls(local.Function));
                return;
        }

        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            CollectCallsFromNode(child, calls);
    }

    private static List<string> ResolveToAllInternalIds(
        string callee,
        Dictionary<string, FunctionNode> functions,
        Dictionary<string, string> functionNameToId,
        Dictionary<string, List<string>> methodNameToIds)
    {
        if (functionNameToId.TryGetValue(callee, out var id) && functions.ContainsKey(id))
            return new List<string> { id };
        if (functions.ContainsKey(callee))
            return new List<string> { callee };

        var lastDot = callee.LastIndexOf('.');
        if (lastDot > 0)
        {
            var bareMethodName = callee[(lastDot + 1)..];
            if (methodNameToIds.TryGetValue(bareMethodName, out var candidates)
                && candidates.Count == 1)
            {
                return [candidates[0]];
            }
        }

        return new List<string>();
    }

    private static List<List<string>> ComputeSccs(
        Dictionary<string, FunctionNode> functions,
        Dictionary<string, List<(string Callee, TextSpan Span)>> forwardGraph,
        Dictionary<string, string> functionNameToId,
        Dictionary<string, List<string>> methodNameToIds,
        IReadOnlyDictionary<AstCallKey, IReadOnlyList<string>> resolvedCallIds,
        IReadOnlySet<AstCallKey> boundCallSites)
    {
        var sccs = new List<List<string>>();
        var index = 0;
        var indices = new Dictionary<string, int>();
        var lowlinks = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var stack = new Stack<string>();

        foreach (var functionId in functions.Keys)
        {
            if (!indices.ContainsKey(functionId))
            {
                Strongconnect(functionId, ref index, indices, lowlinks, onStack, stack, sccs,
                    functions, forwardGraph, functionNameToId, methodNameToIds,
                    resolvedCallIds, boundCallSites);
            }
        }

        return sccs;
    }

    private static void Strongconnect(
        string v,
        ref int index,
        Dictionary<string, int> indices,
        Dictionary<string, int> lowlinks,
        HashSet<string> onStack,
        Stack<string> stack,
        List<List<string>> sccs,
        Dictionary<string, FunctionNode> functions,
        Dictionary<string, List<(string Callee, TextSpan Span)>> forwardGraph,
        Dictionary<string, string> functionNameToId,
        Dictionary<string, List<string>> methodNameToIds,
        IReadOnlyDictionary<AstCallKey, IReadOnlyList<string>> resolvedCallIds,
        IReadOnlySet<AstCallKey> boundCallSites)
    {
        indices[v] = index;
        lowlinks[v] = index;
        index++;
        stack.Push(v);
        onStack.Add(v);

        if (forwardGraph.TryGetValue(v, out var calls))
        {
            foreach (var (calleeName, span) in calls)
            {
                var key = new AstCallKey(v, calleeName, span.Start, span.End);
                IReadOnlyList<string> calleeIds;
                if (resolvedCallIds.TryGetValue(key, out var exactIds))
                {
                    calleeIds = exactIds;
                }
                else if (boundCallSites.Contains(key))
                {
                    continue;
                }
                else
                {
                    calleeIds = ResolveToAllInternalIds(
                        calleeName,
                        functions,
                        functionNameToId,
                        methodNameToIds);
                }

                foreach (var calleeId in calleeIds)
                {
                    if (!indices.ContainsKey(calleeId))
                    {
                        Strongconnect(calleeId, ref index, indices, lowlinks, onStack, stack, sccs,
                            functions, forwardGraph, functionNameToId, methodNameToIds,
                            resolvedCallIds, boundCallSites);
                        lowlinks[v] = Math.Min(lowlinks[v], lowlinks[calleeId]);
                    }
                    else if (onStack.Contains(calleeId))
                    {
                        lowlinks[v] = Math.Min(lowlinks[v], indices[calleeId]);
                    }
                }
            }
        }

        if (lowlinks[v] == indices[v])
        {
            var scc = new List<string>();
            string w;
            do
            {
                w = stack.Pop();
                onStack.Remove(w);
                scc.Add(w);
            } while (w != v);
            sccs.Add(scc);
        }
    }
}
