using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Binding;

internal sealed record SourceProjection(ModuleNode Module, string Path, string Source,
    string Marker, IReadOnlyList<AstNode> Callables);

internal sealed record SourceCallable(MethodNode Declaration, ClassDefinitionNode Owner,
    FunctionSymbol Symbol, TypeSymbol OwnerSymbol);

// Binding consumes project source evidence without depending on effect enforcement.
internal interface ISourceBindingContext
{
    IReadOnlyList<SourceProjection> CompanionSources(string? sourcePath, ContractMode mode);
    // One annotated image of every module of the pass, shared by every projection that
    // binds a module of it (null when there is none to share).
    CallGraphAnalysis.SharedProjectionImage? SharedImage(string? sourcePath, ContractMode mode) => null;
    SourceCallable? FindCallable(AstNode declaration);
}

/// <summary>
/// Performs semantic analysis and builds the bound tree.
/// </summary>
public sealed class Binder
{
    internal IReadOnlyDictionary<string, CrossModuleFunctionTarget>? CrossModuleFunctionModules { get; init; }
    internal ISourceBindingContext? CrossModuleTypes { get; init; }
    internal Dictionary<SymbolId, SourceCallable> ExternalSourceCalls { get; } = [];
    internal IReadOnlyDictionary<AstNode, FunctionSymbol> FunctionSymbols => _functionSymbols;
    internal IReadOnlyDictionary<ClassDefinitionNode, TypeSymbol> ClassSymbols => _classSymbols;
    private readonly string? _sourcePath;
    internal Dictionary<TextSpan, string> CrossModuleCallTargets { get; } = new();
    internal Dictionary<TextSpan, FunctionSymbol> SourceExtensionCallTargets { get; } = new();
    internal delegate BoundExpression ExpressionBinder(Binder binder, ExpressionNode expression);

    private static readonly IReadOnlyDictionary<Type, ExpressionBinder> ExpressionBinders =
        new Dictionary<Type, ExpressionBinder>
        {
            [typeof(AddressOfNode)] = static (binder, expression) => binder.BindAddressOf((AddressOfNode)expression),
            [typeof(AnonymousObjectCreationNode)] = static (binder, expression) => binder.BindAnonymousObjectCreation((AnonymousObjectCreationNode)expression),
            [typeof(ArrayAccessNode)] = static (binder, expression) => binder.BindArrayAccess((ArrayAccessNode)expression),
            [typeof(ArrayCreationNode)] = static (binder, expression) => binder.BindArrayCreation((ArrayCreationNode)expression),
            [typeof(ArrayLengthNode)] = static (binder, expression) => binder.BindArrayLength((ArrayLengthNode)expression),
            [typeof(AwaitExpressionNode)] = static (binder, expression) => binder.BindAwaitExpression((AwaitExpressionNode)expression),
            [typeof(BaseExpressionNode)] = static (binder, expression) => binder.BindBaseExpression((BaseExpressionNode)expression),
            [typeof(BinaryOperationNode)] = static (binder, expression) => binder.BindBinaryOperation((BinaryOperationNode)expression),
            [typeof(BoolLiteralNode)] = static (_, expression) => new BoundBoolLiteral(expression.Span, ((BoolLiteralNode)expression).Value),
            [typeof(CallExpressionNode)] = static (binder, expression) => binder.BindCallExpression((CallExpressionNode)expression),
            [typeof(CharOperationNode)] = static (binder, expression) => binder.BindCharOperation((CharOperationNode)expression),
            [typeof(CollectionContainsNode)] = static (binder, expression) => binder.BindCollectionContains((CollectionContainsNode)expression),
            [typeof(CollectionCountNode)] = static (binder, expression) => binder.BindCollectionCount((CollectionCountNode)expression),
            [typeof(ConditionalExpressionNode)] = static (binder, expression) => binder.BindConditionalExpression((ConditionalExpressionNode)expression),
            [typeof(DecimalLiteralNode)] = static (_, expression) => new BoundDecimalLiteral(expression.Span, ((DecimalLiteralNode)expression).Value),
            [typeof(DictionaryCreationNode)] = static (binder, expression) => binder.BindDictionaryCreation((DictionaryCreationNode)expression),
            [typeof(ExistsExpressionNode)] = static (binder, expression) => binder.BindExistsExpression((ExistsExpressionNode)expression),
            [typeof(ExpressionCallNode)] = static (binder, expression) => binder.BindExpressionCall((ExpressionCallNode)expression),
            [typeof(FallbackExpressionNode)] = static (binder, expression) => binder.BindFallbackInterop((FallbackExpressionNode)expression),
            [typeof(FieldAccessNode)] = static (binder, expression) => binder.BindFieldAccess((FieldAccessNode)expression),
            [typeof(FloatLiteralNode)] = static (binder, expression) => binder.BindFloatLiteral((FloatLiteralNode)expression),
            [typeof(ForallExpressionNode)] = static (binder, expression) => binder.BindForallExpression((ForallExpressionNode)expression),
            [typeof(GenericTypeNode)] = static (binder, expression) => binder.BindGenericType((GenericTypeNode)expression),
            [typeof(ImplicationExpressionNode)] = static (binder, expression) => binder.BindImplicationExpression((ImplicationExpressionNode)expression),
            [typeof(IndexFromEndNode)] = static (binder, expression) => binder.BindIndexFromEnd((IndexFromEndNode)expression),
            [typeof(IntLiteralNode)] = static (binder, expression) => binder.BindIntLiteral((IntLiteralNode)expression),
            [typeof(InterpolatedStringNode)] = static (binder, expression) => binder.BindInterpolatedString((InterpolatedStringNode)expression),
            [typeof(IsPatternNode)] = static (binder, expression) => binder.BindIsPattern((IsPatternNode)expression),
            [typeof(LambdaExpressionNode)] = static (binder, expression) => binder.BindLambdaExpression((LambdaExpressionNode)expression),
            [typeof(ListCreationNode)] = static (binder, expression) => binder.BindListCreation((ListCreationNode)expression),
            [typeof(MatchExpressionNode)] = static (binder, expression) => binder.BindMatchExpression((MatchExpressionNode)expression),
            [typeof(MultiDimArrayAccessNode)] = static (binder, expression) => binder.BindMultiDimArrayAccess((MultiDimArrayAccessNode)expression),
            [typeof(MultiDimArrayCreationNode)] = static (binder, expression) => binder.BindMultiDimArrayCreation((MultiDimArrayCreationNode)expression),
            [typeof(NameOfExpressionNode)] = static (binder, expression) => binder.BindStringLiteral(
                new StringLiteralNode(expression.Span, ((NameOfExpressionNode)expression).Name)),
            [typeof(NewExpressionNode)] = static (binder, expression) => binder.BindNewExpression((NewExpressionNode)expression),
            [typeof(NullCoalesceNode)] = static (binder, expression) => binder.BindNullCoalesce((NullCoalesceNode)expression),
            [typeof(NullConditionalNode)] = static (binder, expression) => binder.BindNullConditional((NullConditionalNode)expression),
            [typeof(OverflowContextExpressionNode)] = static (binder, expression) => binder.BindOverflowContext((OverflowContextExpressionNode)expression),
            [typeof(PointerDereferenceNode)] = static (binder, expression) => binder.BindPointerDereference((PointerDereferenceNode)expression),
            [typeof(RangeExpressionNode)] = static (binder, expression) => binder.BindRangeExpression((RangeExpressionNode)expression),
            [typeof(RawCSharpExpressionNode)] = static (binder, expression) => binder.BindRawCSharpInterop((RawCSharpExpressionNode)expression),
            [typeof(RecordCreationNode)] = static (binder, expression) => binder.BindRecordCreation((RecordCreationNode)expression),
            [typeof(ReferenceNode)] = static (binder, expression) => binder.BindReferenceExpression((ReferenceNode)expression),
            [typeof(SelfRefNode)] = static (binder, expression) => binder.BindSelfReference((SelfRefNode)expression),
            [typeof(SetCreationNode)] = static (binder, expression) => binder.BindSetCreation((SetCreationNode)expression),
            [typeof(SizeOfNode)] = static (binder, expression) => binder.BindSizeOf((SizeOfNode)expression),
            [typeof(StackAllocNode)] = static (binder, expression) => binder.BindStackAlloc((StackAllocNode)expression),
            [typeof(StringBuilderOperationNode)] = static (binder, expression) => binder.BindStringBuilderOperation((StringBuilderOperationNode)expression),
            [typeof(StringLiteralNode)] = static (binder, expression) => binder.BindStringLiteral((StringLiteralNode)expression),
            [typeof(StringOperationNode)] = static (binder, expression) => binder.BindStringOperation((StringOperationNode)expression),
            [typeof(ThisExpressionNode)] = static (binder, expression) => binder.BindThisExpression((ThisExpressionNode)expression),
            [typeof(ThrowExpressionNode)] = static (binder, expression) => binder.BindThrowExpression((ThrowExpressionNode)expression),
            [typeof(TupleLiteralNode)] = static (binder, expression) => binder.BindTupleLiteral((TupleLiteralNode)expression),
            [typeof(TypeOfExpressionNode)] = static (binder, expression) => binder.BindTypeOf((TypeOfExpressionNode)expression),
            [typeof(TypeOperationNode)] = static (binder, expression) => binder.BindTypeOperation((TypeOperationNode)expression),
            [typeof(UnaryOperationNode)] = static (binder, expression) => binder.BindUnaryOperation((UnaryOperationNode)expression),
            [typeof(WithExpressionNode)] = static (binder, expression) => binder.BindWithExpression((WithExpressionNode)expression),
        };

    internal static IReadOnlyCollection<Type> RegisteredExpressionNodeTypes =>
        ExpressionBinders.Keys.ToArray();

    internal static IReadOnlyDictionary<Type, ExpressionBinder> ExpressionDispatch =>
        ExpressionBinders;

    public int ExpressionsBound { get; private set; }

    private readonly DiagnosticBag _diagnostics;
    private readonly string _sourceIdentity;
    private Scope _scope;
    private readonly Dictionary<AstNode, FunctionSymbol> _functionSymbols = new();
    private readonly Dictionary<AwaitExpressionNode, BoundStructuralExpression> _boundAwaits =
        new(ReferenceEqualityComparer.Instance);
    internal IReadOnlyDictionary<AwaitExpressionNode, BoundStructuralExpression> BoundAwaits => _boundAwaits;
    private readonly Dictionary<ExpressionNode, BoundFieldAccessExpression> _boundSourceEnumConstants =
        new(ReferenceEqualityComparer.Instance);
    internal IReadOnlyDictionary<ExpressionNode, BoundFieldAccessExpression> BoundSourceEnumConstants =>
        _boundSourceEnumConstants;
    private readonly Dictionary<ExpressionNode, BoundFieldAccessExpression> _boundSourceStaticFieldReads =
        new(ReferenceEqualityComparer.Instance);
    internal IReadOnlyDictionary<ExpressionNode, BoundFieldAccessExpression> BoundSourceStaticFieldReads =>
        _boundSourceStaticFieldReads;
    private readonly Dictionary<ExpressionNode, BoundFieldAccessExpression> _boundMetadataConstants =
        new(ReferenceEqualityComparer.Instance);
    internal IReadOnlyDictionary<ExpressionNode, BoundFieldAccessExpression> BoundMetadataConstants =>
        _boundMetadataConstants;
    private readonly Dictionary<ExpressionNode, BoundFieldAccessExpression> _boundMetadataStaticFieldReads =
        new(ReferenceEqualityComparer.Instance);
    internal IReadOnlyDictionary<ExpressionNode, BoundFieldAccessExpression> BoundMetadataStaticFieldReads =>
        _boundMetadataStaticFieldReads;
    private readonly Dictionary<ExpressionNode, BoundExpression> _boundContractExpressions = new();
    internal IReadOnlyDictionary<ExpressionNode, BoundExpression> BoundContractExpressions =>
        _boundContractExpressions;
    private readonly Dictionary<ExpressionNode, BoundCallExpression> _boundVerificationCalls =
        new(ReferenceEqualityComparer.Instance);
    internal IReadOnlyDictionary<ExpressionNode, BoundCallExpression> BoundVerificationCalls =>
        _boundVerificationCalls;
    private bool _bindingContract;
    private Verification.ModuleInheritanceResult? _contractInheritance;
    internal Verification.ModuleInheritanceResult? ContractInheritance => _contractInheritance;
    internal bool IsStaticSourceMethod(FunctionSymbol symbol) => _staticMethodSymbols.Contains(symbol);
    private readonly HashSet<FunctionSymbol> _staticMethodSymbols = new();
    private readonly Dictionary<SymbolId, string?> _functionNamespaces = new();
    private readonly Dictionary<ClassDefinitionNode, Scope> _classScopes = new();
    private readonly Dictionary<EventDefinitionNode, VariableSymbol> _eventSymbols = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ClassDefinitionNode, ClassDefinitionNode> _containingClasses = new();
    private readonly Dictionary<ClassDefinitionNode, string> _qualifiedClassNames = new();
    private readonly Dictionary<ClassDefinitionNode, SymbolId> _classSymbolIds = new();
    private readonly Dictionary<ClassDefinitionNode, TypeSymbol> _classSymbols = new();
    private readonly Dictionary<string, ClassDefinitionNode> _classesByQualifiedName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ClassDefinitionNode>> _classesBySimpleName = new(StringComparer.Ordinal);
    private readonly Dictionary<SymbolId, (TypeSymbol Symbol, InterfaceDefinitionNode Declaration)> _interfacesBySymbolId = new();
    private readonly Dictionary<SymbolId, IReadOnlyList<VariableSymbol>> _enumMembers = new();
    private readonly Dictionary<string, DelegateDefinitionNode> _delegateDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<VariableSymbol, VariableSymbol> _flowNarrowingSources = new();
    // 0.30: storage in a `nullable oblivious` module that may hold a null the module itself
    // introduced (a null or ?T initializer, any reassignment, a ref/out write, a lost
    // non-null proof). Such storage is no longer merely oblivious, so it keeps the
    // conservative Calor0274 checks. Flow-insensitive within a module by design.
    private readonly HashSet<VariableSymbol> _obliviousNullTaintedStorage = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(VariableSymbol? Receiver, VariableSymbol Member), VariableSymbol>
        _memberFlowStorages = new();
    private readonly Dictionary<VariableSymbol, CallableMutationSummary> _callableState =
        new(ReferenceEqualityComparer.Instance);
    private List<CallableMutationWrite>? _deferredCallableWrites;
    private bool _isDiscoveringLoopCallables;
    private readonly Dictionary<string, HashSet<string>> _topLevelFunctionLookupNames = new(StringComparer.Ordinal);
    private readonly Dictionary<CSharpInteropBlockNode, IReadOnlySet<(string Name, int Arity)>> _interopMethodArities = new();
    private readonly Dictionary<SymbolId, Symbol> _symbolsById = new();
    // Insertion order of _symbolsById. Loop discovery removes the symbols it
    // added; rebuilding the module's map from this list keeps the published
    // enumeration order equal to declaration order without copying the whole
    // map at every loop.
    private readonly List<SymbolId> _symbolOrder = new();
    // The TypeSymbol values of _symbolsById, in its enumeration order. Type lookups
    // scan this on every variable reference; rebuilding it from the whole map (which
    // also holds every local) each time made binding quadratic in module size.
    // Reset whenever a TypeSymbol enters or leaves the map. Other entries can come
    // and go without moving the TypeSymbols' relative enumeration order.
    private TypeSymbol[]? _sourceTypeSymbols;
    private readonly Dictionary<SymbolId, int> _declarationIdOccurrences = new();
    private readonly Dictionary<(SymbolId Context, string Kind), SymbolId> _snapshotLocalContexts = new();
    private readonly System.Buffers.ArrayBufferWriter<byte> _localIdentityBytes = new(256);
    private System.Text.Json.Utf8JsonWriter? _localIdentityWriter;
    // While a loop body is bound for discovery, each declaration-id counter
    // change is journaled (previous value, 0 = absent) so it can be undone.
    private List<(SymbolId Key, int Previous)>? _declarationOccurrenceJournal;
    private IReadOnlySet<string> _declaredReferenceTypes = new HashSet<string>(StringComparer.Ordinal);
    private SymbolId _moduleSymbolId;
    private HashSet<string> _toolingIds = new(StringComparer.Ordinal);
    private SymbolId _declarationContext;
    private string _transientBindingId = "";
    private string? _currentClassName;
    private ClassDefinitionNode? _currentClass;

    /// <summary>v0.17 R2 — type parameters of the member currently being bound
    /// (a method or a module-level function), searched alongside the enclosing
    /// class's when resolving a generic argument to its constraints.</summary>
    private IReadOnlyList<TypeParameterNode>? _currentMemberTypeParameters;
    private Scope? _currentClassScope;
    private SymbolId _currentClassIdentity;
    private string? _currentNamespaceIdentity;
    private string? _currentNamespaceScopeId;
    private bool _isStaticContext;

    /// <summary>#943 (D1 RO-NULL-2): the current callable's ref/out/in parameters, which may alias.</summary>
    private IReadOnlyList<VariableSymbol> _byReferenceParameters = [];

    /// <summary>#943 (D1 RO-LV-1): the constructor being bound, where its class's readonly fields are writable.</summary>
    private ConstructorNode? _currentConstructor;

    /// <summary>#943: aliased-storage changes seen so far; a loop's discovery pass compares it.</summary>
    private int _aliasInvalidations;

    /// <summary>
    /// v0.14 §S3b (nullability workstream / issue #875) — lazily-constructed
    /// MetadataBinder for enriching BCL-shaped call resolutions with Roslyn
    /// NullableAnnotation. Lazy because MetadataContext.Create loads TPA
    /// references (non-trivial cost); tests and small binds shouldn't pay
    /// that cost unless they need it. Null until first BCL-shaped resolve.
    /// </summary>
    private Metadata.MetadataBinder? _metadataBinder;
    private readonly Dictionary<SymbolId, IReadOnlyDictionary<string, Microsoft.CodeAnalysis.ITypeParameterSymbol>>
        _metadataTypeParameterScopes = new();
    private IReadOnlyList<UsingDirectiveNode> _metadataUsings = [];
    private IReadOnlyDictionary<string, NamespaceScopeInfo> _metadataNamespaceScopes =
        new Dictionary<string, NamespaceScopeInfo>();
    private readonly Dictionary<(string? Scope, string? Namespace, string Name, int Arity),
        (bool Handled, Microsoft.CodeAnalysis.INamedTypeSymbol? Type)> _scopedMetadataTypes = new();
    private ModuleNode? _asyncTypeModule;
    private Analysis.CallGraphAnalysis.OperatorBindings? _sourceCallConversions;
    // The annotated C# binding image disables this to prevent recursive rebinding.
    internal bool ResolveSourceCallConversions { get; init; } = true;
    internal ContractMode ProjectionContractMode { get; init; } = ContractMode.Debug;

    /// <summary>
    /// v0.14 §S4 (nullability workstream / issue #875) — declared return
    /// type of the currently-binding function/method/accessor, or null when
    /// we're not inside a function body. Read by <see cref="BindReturnStatement"/>
    /// to feed the same <see cref="NullabilityChecker.IsPossiblyNullAssignedTo"/>
    /// predicate S3b uses at <c>§B</c> sites. Set via
    /// <see cref="PushReturnTypeContext"/> at every function-binding entry
    /// point (BindFunction / BindMethod / BindConstructor / BindPropertyAccessor
    /// / BindOperator / BindIndexerAccessor). Lambdas clear this context because
    /// their return type is inferred, not the enclosing function's declaration.
    /// </summary>
    private string? _currentFunctionReturnType;
    private BoundTypes.NullableAnnotation _declaredReferenceAnnotation = BoundTypes.NullableAnnotation.NotAnnotated;

    public Binder(DiagnosticBag diagnostics, string? sourceIdentity = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _sourceIdentity = SymbolSourceIdentity.Canonicalize(sourceIdentity);
        _sourcePath = sourceIdentity;
        _scope = new Scope();
    }

    /// <summary>
    /// Returns a shared per-Binder MetadataBinder, constructed on first use.
    /// Returns null if MetadataContext.Create throws (e.g. TPA references
    /// unavailable in the test/tools host); callers fall back to today's
    /// Oblivious-default path in that case.
    /// </summary>
    private Metadata.MetadataBinder? GetOrCreateMetadataBinder()
    {
        if (_metadataBinder is not null) return _metadataBinder;
        try
        {
            var ctx = Metadata.MetadataContext.Create();
            _metadataBinder = new Metadata.MetadataBinder(ctx);
        }
        catch
        {
            // Deliberately swallow — MetadataBinder is an enrichment, not a
            // correctness requirement. Fallback: Oblivious-default Type.
        }
        return _metadataBinder;
    }

    /// <summary>
    /// Best-effort BCL-shaped call resolution via MetadataBinder. Returns
    /// the selected Roslyn symbol and its actual supplied-argument mappings,
    /// including normal versus expanded params, or null when the receiver
    /// is not BCL-shaped, MetadataBinder is unavailable, or resolution
    /// fails. Additive: never subtracts from Binder's own string-based
    /// resolve.
    ///
    /// <para>v0.14 §S4: parameter annotations feed the Calor0274 call-site
    /// check in <see cref="BindCallExpression"/>. §S3b previously returned
    /// only the return type; the parameter list is the incremental extension
    /// this slice needs.</para>
    /// </summary>
    private Metadata.MetadataBinderResult? TryResolveBclCall(
        string callTarget,
        IReadOnlyList<BoundExpression> args,
        IReadOnlyList<string?>? argumentNames,
        IReadOnlyList<string?>? argumentModifiers,
        BoundExpression? computedReceiver = null,
        IReadOnlyList<string>? typeArguments = null,
        ExpressionCallNode? contextualCall = null)
    {
        if (args.Where((argument, index) => argument.HasUnknownInferredType
                || argument is BoundVariableExpression { Variable: { Id.IsNone: true, Name: var name } }
                    && (name != "_" || argumentModifiers?.ElementAtOrDefault(index) != "out")).Any()
            || computedReceiver?.HasUnknownInferredType == true
            || computedReceiver is BoundVariableExpression { Variable.Id.IsNone: true })
            return null;
        // BCL-shape heuristic: receiver contains at least one dot AND starts
        // with System./Microsoft. or a capitalized identifier followed by a dot.
        var lastDot = callTarget.LastIndexOf('.');
        if (lastDot <= 0) return null;
        var receiverName = callTarget[..lastDot];
        var methodName = callTarget[(lastDot + 1)..];
        if (string.IsNullOrEmpty(receiverName) || string.IsNullOrEmpty(methodName)) return null;
        if (computedReceiver is null && !(receiverName.StartsWith("System.", StringComparison.Ordinal)
              || receiverName.StartsWith("Microsoft.", StringComparison.Ordinal)))
        {
            return null;
        }

        var binder = GetOrCreateMetadataBinder();
        if (binder is null) return null;
        if (computedReceiver is not null
            && NullabilityChecker.GetMethodInputArrayType(computedReceiver) is null
            && (computedReceiver.Type is BoundTypes.NominalBoundType { Declaration: not null }
                || ResolveClass(computedReceiver.Type.DisplayString) is not null))
            return null;
        if (computedReceiver?.Type is BoundTypes.UnresolvedBoundType
            || computedReceiver is BoundCallExpression { ResolvedSymbol: null, ResolvedMetadataMethod: null,
                Type.DisplayString: "OBJECT" })
            return null;
        if (computedReceiver is not null && args.Any(argument =>
            argument.Type is BoundTypes.UnresolvedBoundType
            || argument is BoundCallExpression { ResolvedSymbol: null, ResolvedMetadataMethod: null,
                Type.DisplayString: "OBJECT" }
            || argument is BoundVariableExpression
                { Variable: { IsTypeInferred: true, TypeName: "OBJECT", InferredReferenceType: null } }))
            return null;

        // Member reads and their receiving calls must use the same actual reference set.
        var ctx = binder.Context;
        var receiverType = computedReceiver is null ? ctx.TryResolveType(receiverName)
            : ResolveMetadataArrayReceiver(computedReceiver);
        if (receiverType is null && computedReceiver is not null
            && NullabilityChecker.GetMethodInputArrayType(computedReceiver) is null)
            receiverType = ResolveComputedMetadataType(ctx, computedReceiver.Type.DisplayString);
        if (receiverType is null) return ResolveContextualCall();

        // Argument types: use each BoundExpression's Type.DisplayString to
        // find a Roslyn type. The Calor short names (STRING/INT/BOOL/…) are
        // mapped to their BCL equivalents (System.String/System.Int32/…) via
        // TypeIdentity.MapShortTypeNameToFullName — otherwise
        // GetTypeByMetadataName ("STRING") returns null and overload
        // resolution silently degrades to System.Object placeholders. A
        // trailing '?' (Roslyn's Annotated display, e.g. "string?" from
        // GetEnvironmentVariable's return type) is trimmed before mapping —
        // the metadata name is unannotated. Falling back to System.Object
        // when unknown so MetadataBinder can at least attempt overload
        // resolution.
        var metaArgs = new Metadata.MetadataArgument[args.Count];
        for (var i = 0; i < args.Count; i++)
        {
            var arrayType = NullabilityChecker.GetMethodInputArrayType(args[i]);
            var display = args[i].Type.DisplayString;
            // Strip only recognized nullable annotations for metadata lookup.
            var trimmed = display;
            while (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(trimmed, out var referent))
            {
                trimmed = referent;
            }
            var t = arrayType is not null
                ? GetMetadataArrayInputType(ctx, arrayType)
                : args[i].Type is BoundTypes.NominalBoundType
                    { RoslynSymbol: { IsValueType: true, IsGenericType: false,
                        SpecialType: not Microsoft.CodeAnalysis.SpecialType.None } primitive }
                    ? primitive
                : (computedReceiver is null ? ResolveBclArgumentType(ctx, trimmed) : ResolveComputedMetadataType(ctx, display))
                    ?? (computedReceiver is null ? ctx.TryResolveType("System.Object") : null);
            if (t is null) return ResolveContextualCall();
            var modifier = argumentModifiers != null && i < argumentModifiers.Count
                ? argumentModifiers[i]
                : null;
            var refKind = modifier switch
            {
                "out" => Microsoft.CodeAnalysis.RefKind.Out,
                "ref" => Microsoft.CodeAnalysis.RefKind.Ref,
                "in" => Microsoft.CodeAnalysis.RefKind.In,
                _ => Microsoft.CodeAnalysis.RefKind.None,
            };
            var name = argumentNames != null && i < argumentNames.Count ? argumentNames[i] : null;
            metaArgs[i] = new Metadata.MetadataArgument(t, refKind, name);
        }

        Microsoft.CodeAnalysis.ITypeSymbol[] explicitTypes = [];
        if (typeArguments is { Count: > 0 })
        {
            var resolvedTypes = typeArguments.Select(type => ResolveComputedMetadataType(ctx, type)).ToArray();
            if (resolvedTypes.Any(type => type is null))
                return null;
            explicitTypes = resolvedTypes.OfType<Microsoft.CodeAnalysis.ITypeSymbol>().ToArray();
        }
        var result = computedReceiver is null ? binder.ResolveCall(receiverType, methodName, metaArgs)
            : binder.ResolveExpressionCall(receiverType, methodName, metaArgs, explicitTypes,
                computedReceiver is BoundTypeReferenceExpression);
        if (!result.IsResolved) return ResolveContextualCall();

        if (result.Symbol!.IsGenericMethod && metaArgs.Any(argument => argument.Type is Microsoft.CodeAnalysis.IArrayTypeSymbol))
        {
            var preserved = computedReceiver is null
                ? binder.ResolveCall(receiverType, methodName, metaArgs, preserveArrayAnnotations: true)
                : binder.ResolveExpressionCall(receiverType, methodName, metaArgs, explicitTypes,
                    computedReceiver is BoundTypeReferenceExpression, preserveArrayAnnotations: true);
            if (!Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(result.Symbol, preserved.Symbol))
                return null;
            // Accurate method-input facts must not implicitly expand initialization/return policy.
            result = preserved with { LegacyReturnType = result.GetReturnBoundTypeEx() };
        }
        return result;

        Metadata.MetadataBinderResult? ResolveContextualCall()
        {
            if (contextualCall is null || !ResolveSourceCallConversions || _asyncTypeModule is null)
                return null;
            // Only the complete image can name source payloads inside metadata
            // containers. Its selected signature is typing evidence, not an effect grant.
            return SourceCallBindings().ContextualMetadataCalls.TryGetValue(contextualCall, out var selected)
                ? selected : null;
        }
    }

    private Microsoft.CodeAnalysis.ITypeSymbol? ResolveComputedMetadataType(
        Metadata.MetadataContext context, string typeName)
        => ResolveComputedMetadataType(context, typeName, _currentNamespaceScopeId,
            _currentNamespaceIdentity, _currentClassName, null);

    private Microsoft.CodeAnalysis.ITypeSymbol? ResolveComputedMetadataType(
        Metadata.MetadataContext context, string typeName, string? namespaceScopeId,
        string? namespaceIdentity, string? containingTypeName, IReadOnlySet<string>? declaredTypeParameters)
    {
        if (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(typeName, out var referent))
        {
            var inner = ResolveComputedMetadataType(context, referent, namespaceScopeId,
                namespaceIdentity, containingTypeName, declaredTypeParameters);
            return inner?.IsValueType == true
                ? context.TryResolveType("System.Nullable`1")?.Construct(inner)
                : inner?.WithNullableAnnotation(Microsoft.CodeAnalysis.NullableAnnotation.Annotated);
        }
        if (declaredTypeParameters?.Contains(typeName.TrimStart('@')) == true)
            return null;
        if (declaredTypeParameters is not null
            && TrySplitArrayTypeSpelling(typeName, out var element, out var rank, out var arrayAnnotation,
                _declaredReferenceAnnotation))
            return ResolveComputedMetadataType(context, element, namespaceScopeId,
                    namespaceIdentity, containingTypeName, declaredTypeParameters) is { } arrayElement
                ? context.HostCompilationForBinder.CreateArrayTypeSymbol(arrayElement, rank, arrayElement.NullableAnnotation)
                    .WithNullableAnnotation(arrayAnnotation == BoundTypes.NullableAnnotation.Annotated
                        ? Microsoft.CodeAnalysis.NullableAnnotation.Annotated
                        : arrayAnnotation == BoundTypes.NullableAnnotation.Oblivious
                            ? Microsoft.CodeAnalysis.NullableAnnotation.None : Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated)
                : null;
        if (declaredTypeParameters is null && GetTypeParameterConstraints(typeName) is not null)
            return ResolveMetadataTypeParameter(context, typeName);
        if (declaredTypeParameters is not null
            && ResolveSourceType(TypeIdentity.ToLookupName(typeName), containingTypeName,
                namespaceIdentity, namespaceScopeId).Handled)
            return null;
        if (ResolveClass(typeName) is not null
            || SourceTypeSymbols().Any(type =>
                type.Name == typeName || type.QualifiedName == TypeIdentity.ToLookupName(typeName)))
            return null;
        if (typeName is "OBJECT" or "object")
            return PreserveDeclaredAnnotation(context.TryResolveType("System.Object"));
        if (TypeIdentity.TrySplitGeneric(typeName, out var name, out var arguments))
        {
            if (declaredTypeParameters?.Contains(name.TrimStart('@')) == true
                || ResolveTypeSymbol(name, arguments.Count) is not null)
                return null;
            var metadataName = TypeIdentity.MapShortTypeNameToFullName(typeName);
            if (!metadataName.Contains('`'))
                metadataName = (name is "Func" or "Action" ? $"System.{name}" : name) + $"`{arguments.Count}";
            var scoped = ResolveScopedMetadataType(context, name, arguments.Count,
                namespaceScopeId, namespaceIdentity);
            var definition = scoped.Handled ? scoped.Type : context.TryResolveType(metadataName);
            var types = arguments.Select(argument => ResolveComputedMetadataType(context, argument,
                namespaceScopeId, namespaceIdentity, containingTypeName, declaredTypeParameters)).ToArray();
            return definition is not null && definition.Arity == types.Length && types.All(type => type is not null)
                ? PreserveDeclaredAnnotation(definition.Construct(types.OfType<Microsoft.CodeAnalysis.ITypeSymbol>().ToArray()))
                : null;
        }
        var resolved = ResolveScopedMetadataType(context, typeName, 0,
            namespaceScopeId, namespaceIdentity);
        return PreserveDeclaredAnnotation(resolved.Handled ? resolved.Type : ResolveBclArgumentType(context, typeName));

        Microsoft.CodeAnalysis.ITypeSymbol? PreserveDeclaredAnnotation(Microsoft.CodeAnalysis.ITypeSymbol? type) =>
            declaredTypeParameters is not null && type?.IsReferenceType == true
                ? type.WithNullableAnnotation(_declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious
                    ? Microsoft.CodeAnalysis.NullableAnnotation.None : Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated)
                : type;
    }

    private (bool Handled, Microsoft.CodeAnalysis.INamedTypeSymbol? Type) ResolveScopedMetadataType(
        Metadata.MetadataContext context, string name, int arity, string? scopeId, string? namespaceIdentity)
    {
        var key = (scopeId, namespaceIdentity, name, arity);
        if (_scopedMetadataTypes.TryGetValue(key, out var cached))
            return cached;

        var scopes = new List<NamespaceScopeInfo>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var id = scopeId; id is not null;)
        {
            if (!visited.Add(id) || !_metadataNamespaceScopes.TryGetValue(id, out var scope))
            {
                _diagnostics.ReportInfo(TextSpan.Empty, DiagnosticCode.SignatureUnresolved,
                    "Metadata lookup requires a valid lexical namespace scope ancestry.");
                return _scopedMetadataTypes[key] = (true, null);
            }
            scopes.Add(scope);
            id = scope.ParentScopeId;
        }
        scopes.Reverse();
        var visibleScopes = scopes.Select(scope => scope.Id).ToHashSet(StringComparer.Ordinal);
        var imports = _metadataUsings.Where(import =>
            import.NamespaceScopeId is null || visibleScopes.Contains(import.NamespaceScopeId)).ToArray();
        // Predefined keywords do not depend on imports; retain their existing binding/nullability path.
        if (imports.Length == 0 && string.IsNullOrEmpty(namespaceIdentity)
            && SyntaxFactory.ParseTypeName(name) is PredefinedTypeSyntax)
            return _scopedMetadataTypes[key] = (false, null);

        var emitter = new CodeGen.CSharpEmitter();
        var source = new System.Text.StringBuilder();
        // The production emitter always imports System, including in otherwise import-free modules.
        source.AppendLine("using System;");
        foreach (var import in imports.Where(import => import.NamespaceScopeId is null))
            source.AppendLine(import.Accept(emitter));
        var namespaceCount = 0;
        string NamespaceSpelling(string value) =>
            string.Join(".", value.Split('.').Select(segment => "@" + segment.TrimStart('@')));
        foreach (var scope in scopes)
        {
            if (!scope.IsGlobal && scope.Name.Length > 0)
            {
                source.Append("namespace ").Append(NamespaceSpelling(scope.Name)).AppendLine(" {");
                namespaceCount++;
            }
            foreach (var import in imports.Where(import => import.NamespaceScopeId == scope.Id))
                source.AppendLine(import.Accept(emitter));
        }
        if (scopes.Count == 0 && !string.IsNullOrEmpty(namespaceIdentity))
        {
            source.Append("namespace ").Append(NamespaceSpelling(namespaceIdentity)).AppendLine(" {");
            namespaceCount++;
        }
        var spelling = arity == 0 ? name : name + "<" + new string(',', arity - 1) + ">";
        source.Append("class __CalorMetadataTypeLookup_").Append(Guid.NewGuid().ToString("N"))
            .Append(" { static global::System.Type Resolve() => typeof(").Append(spelling).AppendLine("); }");
        source.Append('}', namespaceCount);
        var tree = CSharpSyntaxTree.ParseText(source.ToString());
        var compilation = context.HostCompilationForBinder.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<TypeOfExpressionSyntax>().Single().Type;
        var diagnostics = model.GetDiagnostics();
        var symbol = model.GetTypeInfo(syntax).Type;
        var ambiguous = diagnostics.Any(diagnostic => diagnostic.Id is "CS0104" or "CS0576" or "CS1537");
        // Missing peer imports do not invalidate an independently resolved BCL type.
        // Errors at the lookup itself and conflicting imports still veto its identity.
        if (!ambiguous && !diagnostics.Any(diagnostic =>
                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                && (!diagnostic.Location.IsInSource
                    || diagnostic.Location.SourceSpan.IntersectsWith(syntax.Span)))
            && symbol is Microsoft.CodeAnalysis.INamedTypeSymbol { TypeKind: not Microsoft.CodeAnalysis.TypeKind.Error } named)
            return _scopedMetadataTypes[key] = (true, named.IsUnboundGenericType ? named.OriginalDefinition : named);

        var head = name.Split('.')[0].TrimStart('@');
        var alias = !name.StartsWith("global::", StringComparison.Ordinal)
            && imports.Any(import => import.Alias == head);
        return _scopedMetadataTypes[key] = (alias || ambiguous, null);
    }

    private Microsoft.CodeAnalysis.ITypeParameterSymbol? ResolveMetadataTypeParameter(
        Metadata.MetadataContext context, string name)
    {
        if (!_metadataTypeParameterScopes.TryGetValue(_declarationContext, out var symbols))
        {
            var parameters = (_currentMemberTypeParameters ?? [])
                .Concat(_currentClass?.TypeParameters ?? [])
                .ToArray();
            if (parameters.DistinctBy(parameter => parameter.Name).Count() != parameters.Length)
            {
                _diagnostics.ReportInfo(parameters[0].Span, DiagnosticCode.SignatureUnresolved,
                    "Metadata binding cannot flatten shadowed class and member type-parameter identities.");
                _metadataTypeParameterScopes.Add(_declarationContext,
                    new Dictionary<string, Microsoft.CodeAnalysis.ITypeParameterSymbol>());
                return null;
            }
            if (parameters.SelectMany(parameter => parameter.Constraints)
                .Where(constraint => constraint.TypeName is not null)
                .Any(constraint => TypeIdentity.TrySplitGeneric(constraint.TypeName!, out var root, out var arguments)
                    ? ResolveTypeSymbol(root, arguments.Count) is not null
                    : ResolveTypeSymbol(constraint.TypeName!) is not null))
            {
                _diagnostics.ReportInfo(parameters[0].Span, DiagnosticCode.SignatureUnresolved,
                    "Metadata binding requires the declared identity of a source-defined generic constraint.");
                _metadataTypeParameterScopes.Add(_declarationContext,
                    new Dictionary<string, Microsoft.CodeAnalysis.ITypeParameterSymbol>());
                return null;
            }
            var emitter = new CodeGen.CSharpEmitter();
            var names = string.Join(", ", parameters.Select(parameter => "@" + parameter.Name));
            var constraints = string.Join(" ", parameters.Where(parameter => parameter.Constraints.Count > 0)
                .Select(parameter => $"where @{parameter.Name} : "
                    + string.Join(", ", parameter.Constraints.Select(constraint => constraint.Accept(emitter)))));
            var tree = CSharpSyntaxTree.ParseText(
                "using System; using System.Collections.Generic; using System.Threading.Tasks;\n"
                + $"class __CalorTypeParameters<{names}> {constraints} {{ }}");
            var compilation = context.HostCompilationForBinder.AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);
            symbols = model.GetDiagnostics().Any(diagnostic =>
                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    ? new Dictionary<string, Microsoft.CodeAnalysis.ITypeParameterSymbol>()
                    : tree.GetRoot().DescendantNodes().OfType<TypeParameterSyntax>()
                        .Select(parameter => model.GetDeclaredSymbol(parameter))
                        .OfType<Microsoft.CodeAnalysis.ITypeParameterSymbol>()
                        .ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
            _metadataTypeParameterScopes.Add(_declarationContext, symbols);
        }
        return symbols.GetValueOrDefault(name.TrimStart('@'));
    }

    private Metadata.MetadataBinderResult? TryResolveInstanceArrayCall(
        CallExpressionNode call, BoundExpression? receiver)
    {
        if (call.Arguments.Count != 0 || call.TypeArguments is { Count: > 0 }
            || receiver is null or BoundTypeReferenceExpression
            || ResolveMetadataArrayReceiver(receiver) is not { } receiverType)
            return null;

        var metadata = GetOrCreateMetadataBinder();
        if (metadata is null)
            return null;
        var result = metadata.ResolveCall(receiverType, call.Target[(call.Target.LastIndexOf('.') + 1)..], []);
        return result.Symbol is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0,
            ReturnType: Microsoft.CodeAnalysis.IArrayTypeSymbol }
            ? result
            : null;
    }

    private Microsoft.CodeAnalysis.ITypeSymbol? ResolveMetadataArrayReceiver(BoundExpression receiver)
    {
        if (receiver.HasUnknownInferredType)
            return null;
        if (NullabilityChecker.GetMethodInputArrayType(receiver) is { } array)
        {
            var context = GetOrCreateMetadataBinder()?.Context;
            if (context is null)
                return null;
            if (GetMetadataArrayInputType(context, array) is { } metadataArray)
                return metadataArray;
            // Source elements have no metadata symbol. The established array still
            // inherits System.Array; do not invent a substitute element type.
            var element = array.ElementType;
            while (element is BoundTypes.ArrayBoundType nested)
                element = nested.ElementType;
            return array.NullableAnnotation != BoundTypes.NullableAnnotation.Annotated
                && element is BoundTypes.NominalBoundType { Declaration: { Id.IsNone: false } declaration }
                && !declaration.QualifiedName.Contains('`')
                && !TypeIdentity.IsBuiltinCanonicalName(TypeIdentity.Canonicalize(declaration.Name))
                && _symbolsById.TryGetValue(declaration.Id, out var actual)
                && ReferenceEquals(actual, declaration)
                && IsSourceTypeAccessible(declaration)
                    ? context.HostCompilationForBinder.GetSpecialType(Microsoft.CodeAnalysis.SpecialType.System_Array)
                    : null;
        }
        if (receiver.Type is BoundTypes.NominalBoundType { RoslynSymbol: { } symbol })
            return symbol;
        if (receiver.Type is BoundTypes.GenericInstantiationBoundType { RoslynSymbol: { } constructed })
            return constructed;
        if (receiver is not BoundVariableExpression { Variable: { IsTypeInferred: false } variable }
            || receiver.Type is not BoundTypes.NominalBoundType { Declaration: null }
            || _currentMemberTypeParameters?.Any(parameter => parameter.Name == variable.TypeName) == true
            || _currentClass?.TypeParameters.Any(parameter => parameter.Name == variable.TypeName) == true
            || SourceTypeSymbols().Any(type => type.Name == variable.TypeName))
            return null;

        var metadata = GetOrCreateMetadataBinder();
        if (metadata is null)
            return null;
        return ResolveComputedMetadataType(metadata.Context, variable.TypeName)
            is Microsoft.CodeAnalysis.INamedTypeSymbol { IsReferenceType: true } resolved ? resolved : null;
    }

    private Microsoft.CodeAnalysis.IPropertySymbol? ResolveMetadataArrayProperty(
        BoundExpression receiver, string memberName)
    {
        return ResolveMetadataArrayReceiver(receiver) is Microsoft.CodeAnalysis.INamedTypeSymbol receiverType
            && GetOrCreateMetadataBinder()?.ResolveMemberRead(
                receiverType, memberName, receiver is BoundTypeReferenceExpression, out _)
                is Microsoft.CodeAnalysis.IPropertySymbol { Type: Microsoft.CodeAnalysis.IArrayTypeSymbol } property
                ? property : null;
    }

    private Microsoft.CodeAnalysis.IPropertySymbol? ResolveMetadataScalarArrayProperty(
        BoundExpression receiver, string memberName)
    {
        var receiverType = ResolveMetadataArrayReceiver(receiver);
        var arrayBase = receiverType is Microsoft.CodeAnalysis.IArrayTypeSymbol array
            ? array.BaseType
            : NullabilityChecker.GetMethodInputArrayType(receiver) is not null
                && receiverType is Microsoft.CodeAnalysis.INamedTypeSymbol
                    { SpecialType: Microsoft.CodeAnalysis.SpecialType.System_Array } sourceArrayBase
                    ? sourceArrayBase : null;
        return receiver is not BoundTypeReferenceExpression
            && arrayBase is not null
            && GetOrCreateMetadataBinder()?.ResolveMemberRead(arrayBase, memberName, false, out _)
                is Microsoft.CodeAnalysis.IPropertySymbol
                {
                    IsStatic: false, Parameters.Length: 0,
                    Type.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_Int32
                        or Microsoft.CodeAnalysis.SpecialType.System_Int64
                        or Microsoft.CodeAnalysis.SpecialType.System_Boolean
                } property ? property : null;
    }

    private static bool IsArrayReceiver(BoundExpression receiver)
    {
        var typeName = receiver.Type.DisplayString;
        return receiver.Type is BoundTypes.ArrayBoundType
            || receiver.MethodInputArrayType is not null
            || typeName.EndsWith("[]", StringComparison.Ordinal)
            || typeName.StartsWith("ARRAY[element=", StringComparison.Ordinal) && typeName.EndsWith(']');
    }

    // Exact analysis identities use CLR names, not C# keywords or reference-nullability syntax.
    private static readonly Microsoft.CodeAnalysis.SymbolDisplayFormat BclParameterIdentityFormat = new(
        globalNamespaceStyle: Microsoft.CodeAnalysis.SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: Microsoft.CodeAnalysis.SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: Microsoft.CodeAnalysis.SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: Microsoft.CodeAnalysis.SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | Microsoft.CodeAnalysis.SymbolDisplayMiscellaneousOptions.ExpandNullable);

    private static IReadOnlyList<int>? GetBclArgumentParameterIndices(
        Metadata.MetadataBinderResult? resolution, int argumentCount)
    {
        if (resolution is not { } bcl) return null;
        var indices = Enumerable.Repeat(-1, argumentCount).ToArray();
        foreach (var mapping in bcl.Arguments)
        {
            var receiverOffset = bcl.Symbol?.ReducedFrom != null
                && mapping.Parameter.ContainingSymbol is Microsoft.CodeAnalysis.IMethodSymbol { ReducedFrom: null }
                ? 1 : 0;
            indices[mapping.ArgumentIndex] = mapping.Parameter.Ordinal - receiverOffset;
        }
        return indices;
    }

    private static Microsoft.CodeAnalysis.ITypeSymbol? GetMetadataArrayInputType(
        Metadata.MetadataContext context, BoundTypes.BoundType type)
    {
        Microsoft.CodeAnalysis.ITypeSymbol? symbol = type switch
        {
            BoundTypes.ArrayBoundType { RoslynSymbol: { } arraySymbol } => arraySymbol,
            BoundTypes.ArrayBoundType array => GetMetadataArrayInputType(context, array.ElementType) is { } element
                ? context.HostCompilationForBinder.CreateArrayTypeSymbol(element, array.Rank, element.NullableAnnotation)
                : null,
            BoundTypes.NominalBoundType { RoslynSymbol: { } nominalSymbol } => nominalSymbol,
            BoundTypes.GenericInstantiationBoundType { RoslynSymbol: { } genericSymbol } => genericSymbol,
            BoundTypes.NominalBoundType { Declaration: null, QualifiedName: "STRING" or "string" or "str" } =>
                context.TryResolveType("System.String"),
            BoundTypes.PrimitiveBoundType primitive when IsBuiltInValueTypeName(primitive.Name) =>
                context.TryResolveType(TypeIdentity.MapShortTypeNameToFullName(primitive.Name)),
            _ => null
        };
        var annotation = NullabilityChecker.GetAnnotation(type) switch
        {
            BoundTypes.NullableAnnotation.Annotated => Microsoft.CodeAnalysis.NullableAnnotation.Annotated,
            BoundTypes.NullableAnnotation.NotAnnotated => Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated,
            _ => Microsoft.CodeAnalysis.NullableAnnotation.None
        };
        return symbol?.WithNullableAnnotation(annotation);
    }

    private static Microsoft.CodeAnalysis.ITypeSymbol? ResolveBclArgumentType(
        Metadata.MetadataContext context, string typeName)
    {
        string? elementName = null;
        if (typeName.EndsWith("[]", StringComparison.Ordinal))
            elementName = typeName[..^2];
        else if (typeName.StartsWith("ARRAY[element=", StringComparison.Ordinal) && typeName.EndsWith(']'))
            elementName = typeName["ARRAY[element=".Length..^1];
        else if (typeName.StartsWith('[') && typeName.EndsWith(']') && !typeName.Contains(','))
            elementName = typeName[1..^1];

        if (elementName != null)
        {
            var element = ResolveBclArgumentType(context, elementName.Trim('?'));
            return element == null ? null : context.HostCompilationForBinder.CreateArrayTypeSymbol(element);
        }

        return context.TryResolveType(TypeIdentity.MapShortTypeNameToFullName(typeName))
            ?? context.TryResolveType(typeName);
    }

    private void ValidateCallArguments(
        IReadOnlyList<BoundExpression> arguments,
        OverloadResolutionResult resolution,
        Metadata.MetadataBinderResult? bclResolution)
    {
        var reported = new HashSet<(Parsing.TextSpan Span, string ParameterName, string TargetShape)>();
        if (bclResolution is { } bcl)
        {
            foreach (var mapping in bcl.Arguments)
            {
                if (mapping.Parameter.RefKind == Microsoft.CodeAnalysis.RefKind.Out)
                    continue;
                var target = TryBuildBclArgumentTarget(mapping.TargetType);
                if (target != null)
                    ValidateCallArgument(arguments[mapping.ArgumentIndex], mapping.Parameter.Name, target,
                        mapping.IsExpandedParams, reported);
            }
            return;
        }

        foreach (var match in resolution.Matches)
        {
            foreach (var mapping in match.Arguments)
            {
                var parameter = match.Function.Parameters[mapping.ParameterIndex];
                if (parameter.Modifier.HasFlag(ParameterModifier.Out))
                    continue;
                if (!TryBuildMethodArgumentTarget(
                        mapping.ParameterType, out var target, match.Function))
                    continue;
                ValidateCallArgument(arguments[mapping.ArgumentIndex], parameter.Name, target!,
                    mapping.IsExpandedParams, reported, resolution.ReplacesNativeOverloadError);
            }
        }

    }

    private void ValidateDirectFunctionArguments(
        string target,
        VariableSymbol? receiverSymbol,
        IReadOnlyList<BoundExpression> arguments,
        OverloadResolutionResult resolution,
        Metadata.MetadataBinderResult? bclResolution)
    {
        if (resolution.Kind != OverloadResolutionKind.NotFound || bclResolution is not null)
            return;

        var functionType = _scope.Lookup(target) is VariableSymbol { FunctionType: { } direct }
            ? direct
            : target.EndsWith(".Invoke", StringComparison.Ordinal)
                ? receiverSymbol?.FunctionType
                : null;
        if (functionType is null || functionType.ParameterTypes.Length != arguments.Count)
            return;

        var reported = new HashSet<(Parsing.TextSpan Span, string ParameterName, string TargetShape)>();
        for (var index = 0; index < arguments.Count; index++)
        {
            if (functionType.ParameterTypes[index] is not BoundTypes.NominalBoundType parameter
                || !IsScalarStringTypeName(parameter.QualifiedName)
                || parameter.NullableAnnotation != BoundTypes.NullableAnnotation.NotAnnotated)
            {
                continue;
            }

            ValidateCallArgument(
                arguments[index],
                $"argument {index + 1}",
                new BoundTypes.NominalBoundType(
                    "STRING",
                    BoundTypes.NullableAnnotation.NotAnnotated),
                isExpandedParams: false,
                reported);
        }
    }

    private void InvalidateMutatingCallArguments(
        IReadOnlyList<BoundExpression> arguments,
        IReadOnlyList<string?>? argumentModifiers,
        OverloadResolutionResult resolution,
        Metadata.MetadataBinderResult? bclResolution)
    {
        var mutatingArgumentIndices = new HashSet<int>();
        if (bclResolution is { } bcl)
        {
            foreach (var mapping in bcl.Arguments)
            {
                if (mapping.Parameter.RefKind is Microsoft.CodeAnalysis.RefKind.Ref
                    or Microsoft.CodeAnalysis.RefKind.Out)
                {
                    mutatingArgumentIndices.Add(mapping.ArgumentIndex);
                }
            }
        }
        else
        {
            foreach (var match in resolution.Matches)
            {
                foreach (var mapping in match.Arguments)
                {
                    var modifier = match.Function.Parameters[mapping.ParameterIndex].Modifier;
                    if (modifier.HasFlag(ParameterModifier.Ref)
                        || modifier.HasFlag(ParameterModifier.Out))
                    {
                        mutatingArgumentIndices.Add(mapping.ArgumentIndex);
                    }
                }
            }

            if (argumentModifiers is not null)
            {
                for (var index = 0; index < argumentModifiers.Count; index++)
                {
                    if (argumentModifiers[index] is "ref" or "out")
                        mutatingArgumentIndices.Add(index);
                }
            }
        }

        foreach (var index in mutatingArgumentIndices)
        {
            if (index < arguments.Count
                && arguments[index] is BoundVariableExpression variable)
            {
                var visible = _scope.Lookup(variable.Variable.Name) as VariableSymbol
                    ?? variable.Variable;
                InvalidateMutableFlowNarrowing(visible, requireRebindable: false);
            }
        }
    }

    private void ValidateCallArgument(
        BoundExpression argument,
        string parameterName,
        BoundTypes.BoundType target,
        bool isExpandedParams,
        HashSet<(Parsing.TextSpan Span, string ParameterName, string TargetShape)> reported,
        bool replacesNativeOverloadError = false,
        bool isInstanceReceiver = false)
    {
        if (IsObliviousFlowInObliviousModule(argument))
            return;
        var arrayMismatch = NullabilityChecker.ArrayMismatch.None;
        if (isInstanceReceiver
            ? !argument.HasUnknownInferredType
                && NullabilityChecker.GetAnnotation(argument.Type) == BoundTypes.NullableAnnotation.NotAnnotated
            : !NullabilityChecker.IsPossiblyNullAssignedTo(
                argument, target, BindingReceivingBoundary.MethodArgument, out arrayMismatch))
            return;

        var (targetShapeLabel, fixHintTargetLabel) = DescribeStringTargetShape(target);
        if (target is BoundTypes.ArrayBoundType)
            targetShapeLabel = $"'{AttributeHelper.ToSurfaceSpelling(target.DisplayString)}'";
        if (!reported.Add((argument.Span, parameterName, targetShapeLabel)))
            return;
        var receivingPosition = isExpandedParams ? "Expanded argument to element of params parameter" : "Argument to parameter";
        var parameterPart = isExpandedParams ? "parameter element" : "parameter";
        var message = $"{receivingPosition} '{parameterName}' declares non-nullable {targetShapeLabel} " +
            $"but the value may be null (source annotation: '{DescribeAnnotation(argument)}'). " +
            $"Change the {parameterPart} type to {fixHintTargetLabel} or add an explicit non-null check at the interop boundary.";
        if (isInstanceReceiver)
            message = $"Instance receiver for '{parameterName}' must be proven non-null, " +
                $"but the value may be null (source annotation: '{DescribeAnnotation(argument)}'). " +
                "Add an explicit non-null guard, a non-null fallback, or use conditional access.";
        if (arrayMismatch != NullabilityChecker.ArrayMismatch.None)
        {
            var components = arrayMismatch switch
            {
                NullabilityChecker.ArrayMismatch.Container => "array container",
                NullabilityChecker.ArrayMismatch.Elements => "string array elements",
                _ => "array container and string elements"
            };
            var supplied = NullabilityChecker.GetMethodInputArrayType(argument)!;
            message = $"{receivingPosition} '{parameterName}' requires non-null {components} of {targetShapeLabel}, " +
                $"but the supplied {components} may be null (container annotation: '{supplied.NullableAnnotation}', " +
                $"element annotation: '{NullabilityChecker.GetAnnotation(supplied.ElementType)?.ToString() ?? "unsupported"}'). " +
                "Change the affected receiving annotation or explicitly validate the relevant container/elements.";
        }
        if (DescribeClosedGenericReceiving(argument, target) is { } genericMessage)
            message = $"{receivingPosition} '{parameterName}': {genericMessage}";
        var context = BindingDiagnosticContext.For(BindingReceivingBoundary.MethodArgument, target);
        if (replacesNativeOverloadError && context.Shape == BindingReceivingShape.ScalarString)
            context = context with { ReplacesNativeOverloadError = true };
        _diagnostics.Report(
            argument.Span,
            DiagnosticCode.NullableArgumentToNonNullableParameter,
            message,
            SemanticsVersion.NullabilitySeverityFor(),
            context);
    }

    private void ValidateStringInstanceReceiver(
        BoundExpression receiver, Microsoft.CodeAnalysis.IMethodSymbol? method)
    {
        if (method is not { IsStatic: false,
                MethodKind: Microsoft.CodeAnalysis.MethodKind.Ordinary or Microsoft.CodeAnalysis.MethodKind.PropertyGet,
                ContainingType.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String }
            || method.Locations.Any(location => location.IsInSource))
            return;

        // The callvirt null check precedes the contracted body and can dispatch
        // through exception resource lookup. A selected member is not a receiver proof.
        ValidateCallArgument(receiver, method.ToDisplayString(),
            Metadata.MetadataBinderResult.ToNominalBoundType(method.ContainingType),
            false, [], isInstanceReceiver: true);
    }

    private static BoundTypes.BoundType? TryBuildBclArgumentTarget(Microsoft.CodeAnalysis.ITypeSymbol type)
    {
        if (type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_String)
            return new BoundTypes.NominalBoundType("STRING", Metadata.MetadataBinderResult.MapAnnotation(type.NullableAnnotation));

        if (type is Microsoft.CodeAnalysis.IArrayTypeSymbol)
            return Metadata.MetadataBinderResult.ToBoundType(type);

        if (Metadata.MetadataBinderResult.ToBoundType(type) is BoundTypes.GenericInstantiationBoundType
            { TypeArguments: [BoundTypes.NominalBoundType element] } referenceGeneric
            && NullabilityChecker.IsReferenceContainerDefinition(referenceGeneric.Definition)
            && NullabilityChecker.IsClosedSourceReference(element))
            return referenceGeneric;

        var nominal = Metadata.MetadataBinderResult.ToNominalBoundType(type);
        if (nominal.IsKnownReferenceType)
            return nominal;

        if (type is Microsoft.CodeAnalysis.INamedTypeSymbol { IsGenericType: true } generic
            && generic.TypeArguments.Length == 1
            && generic.TypeArguments[0].SpecialType == Microsoft.CodeAnalysis.SpecialType.System_String
            && GenericStringContainerWhitelist.Contains(
                $"{generic.ContainingNamespace.ToDisplayString()}.{generic.Name}"))
        {
            return new BoundTypes.GenericInstantiationBoundType(
                new BoundTypes.NominalBoundType(generic.Name, BoundTypes.NullableAnnotation.Oblivious),
                [new BoundTypes.NominalBoundType("STRING",
                    Metadata.MetadataBinderResult.MapAnnotation(generic.TypeArguments[0].NullableAnnotation))],
                Metadata.MetadataBinderResult.MapAnnotation(generic.NullableAnnotation));
        }
        return null;
    }

    private IDisposable PushScope(Scope newScope)
    {
        var previous = _scope;
        _scope = newScope;
        return new ScopeRestorer(this, previous);
    }

    private sealed class ScopeRestorer : IDisposable
    {
        private readonly Binder _binder;
        private readonly Scope _previous;
        public ScopeRestorer(Binder binder, Scope previous) { _binder = binder; _previous = previous; }
        public void Dispose() => _binder._scope = _previous;
    }

    private IDisposable PushStaticContext(bool isStatic)
    {
        var previous = _isStaticContext;
        _isStaticContext = isStatic;
        return new StaticContextRestorer(this, previous);
    }

    /// <summary>
    /// v0.17 R2 — the type parameters of the member being bound, so a generic
    /// parameter used as an argument can be resolved to its constraints. The
    /// commonest Calor0208 in the conversion corpus is
    /// <c>Publish&lt;TNotification&gt;</c> calling
    /// <c>PublishNotification(INotification, …)</c>, and <c>TNotification</c> is
    /// declared on the METHOD, not the class — so the class's list alone misses
    /// it.
    /// </summary>
    private IDisposable PushMemberTypeParameters(IReadOnlyList<TypeParameterNode> typeParameters)
    {
        var previous = _currentMemberTypeParameters;
        _currentMemberTypeParameters = typeParameters;
        return new MemberTypeParametersRestorer(this, previous);
    }

    private sealed class MemberTypeParametersRestorer(
        Binder binder,
        IReadOnlyList<TypeParameterNode>? previous) : IDisposable
    {
        public void Dispose() => binder._currentMemberTypeParameters = previous;
    }

    private sealed class StaticContextRestorer : IDisposable
    {
        private readonly Binder _binder;
        private readonly bool _previous;
        public StaticContextRestorer(Binder binder, bool previous) { _binder = binder; _previous = previous; }
        public void Dispose() => _binder._isStaticContext = _previous;
    }

    private IDisposable PushDeclarationContext(SymbolId context)
    {
        var previousContext = _declarationContext;
        _declarationContext = context;
        var restorer = new DeclarationContextRestorer(this, previousContext, _byReferenceParameters, _currentConstructor);
        _byReferenceParameters = [];
        _currentConstructor = null;
        return restorer;
    }

    private sealed class DeclarationContextRestorer : IDisposable
    {
        private readonly Binder _binder;
        private readonly SymbolId _previousContext;
        private readonly IReadOnlyList<VariableSymbol> _previousByReferenceParameters;
        private readonly ConstructorNode? _previousConstructor;

        public DeclarationContextRestorer(
            Binder binder,
            SymbolId previousContext,
            IReadOnlyList<VariableSymbol> previousByReferenceParameters,
            ConstructorNode? previousConstructor)
        {
            _binder = binder;
            _previousContext = previousContext;
            _previousByReferenceParameters = previousByReferenceParameters;
            _previousConstructor = previousConstructor;
        }

        public void Dispose()
        {
            _binder._declarationContext = _previousContext;
            _binder._byReferenceParameters = _previousByReferenceParameters;
            _binder._currentConstructor = _previousConstructor;
        }
    }

    /// <summary>
    /// v0.14 §S4 — set <see cref="_currentFunctionReturnType"/> for the
    /// scope of a function/method/accessor body, restoring the previous
    /// value on dispose. Nested lambdas clear the enclosing context;
    /// yield-return statements share the same context by design (they still
    /// contribute to the enclosing function's return-type contract).
    /// </summary>
    private IDisposable PushReturnTypeContext(string? returnTypeName)
    {
        var previous = _currentFunctionReturnType;
        _currentFunctionReturnType = returnTypeName;
        return new ReturnTypeContextRestorer(this, previous);
    }

    private sealed class ReturnTypeContextRestorer : IDisposable
    {
        private readonly Binder _binder;
        private readonly string? _previous;
        public ReturnTypeContextRestorer(Binder binder, string? previous) { _binder = binder; _previous = previous; }
        public void Dispose() => _binder._currentFunctionReturnType = _previous;
    }

    public BoundModule Bind(ModuleNode module) => Bind(module, null);

    internal void ValidateCallableEffectRows(DiagnosticBag diagnostics)
    {
        foreach (var position in UnannotatedCallablePositions())
            diagnostics.ReportError(position.Span, DiagnosticCode.MissingCallableEffectRow,
                "A callback parameter or return type requires an explicit effect row. "
                + "Use '! unknown' when no invocation contract is known; this does not authorize calling it.");
    }

    internal void MaterializeUnknownCallableRows()
    {
        foreach (var position in UnannotatedCallablePositions())
        {
            var row = new EffectsNode(position.Span, new Dictionary<string, string> { ["unknown"] = "*" });
            if (position is ParameterNode parameter)
                parameter.Row = row;
            else if (position is OutputNode output)
                output.Row = row;
        }
    }

    private IEnumerable<AstNode> UnannotatedCallablePositions()
    {
        foreach (var (declaration, symbol) in _functionSymbols)
        {
            var (parameters, output) = declaration switch
            {
                FunctionNode function => (function.Parameters, function.Output),
                MethodNode method => (method.Parameters, method.Output),
                MethodSignatureNode method => (method.Parameters, method.Output),
                OperatorOverloadNode op => (op.Parameters, op.Output),
                ConstructorNode constructor => (constructor.Parameters, (OutputNode?)null),
                _ => (Array.Empty<ParameterNode>(), (OutputNode?)null)
            };
            for (var index = 0; index < parameters.Count; index++)
                if (parameters[index].Row == null && symbol.Parameters[index].FunctionType != null)
                    yield return parameters[index];
            if (output is { Row: null } && symbol.ReturnFunctionType != null)
                yield return output;
        }
        foreach (var (name, declaration) in _delegateDefinitions)
        {
            foreach (var parameter in declaration.Parameters)
                if (parameter.Row == null && TryBuildParameterFunctionType(parameter, [], name) != null)
                    yield return parameter;
            if (declaration.Output is { Row: null } output
                && TryBuildReturnFunctionType(output, [], name) != null)
                yield return output;
        }
    }

    internal BoundModule Bind(ModuleNode module, Verification.ModuleInheritanceResult? contractInheritance,
        bool declarationsOnly = false)
    {
        _contractInheritance = contractInheritance;
        _declaredReferenceAnnotation = module.HasObliviousNullability()
            ? BoundTypes.NullableAnnotation.Oblivious : BoundTypes.NullableAnnotation.NotAnnotated;
        _obliviousNullTaintedStorage.Clear();
        using var diagnosticContext = _diagnostics.EnterBindingContext();
        _declaredReferenceTypes = Parsing.AttributeHelper.GetDeclaredReferenceTypeNames(module);
        _metadataUsings = module.Usings;
        _metadataNamespaceScopes = module.NamespaceScopes.ToDictionary(scope => scope.Id, StringComparer.Ordinal);
        _scopedMetadataTypes.Clear();
        _asyncTypeModule = module;
        _sourceCallConversions = null;
        _scope = new Scope();
        _functionSymbols.Clear();
        _boundAwaits.Clear();
        _boundSourceEnumConstants.Clear();
        _boundSourceStaticFieldReads.Clear();
        _boundMetadataConstants.Clear();
        _boundMetadataStaticFieldReads.Clear();
        _boundContractExpressions.Clear();
        _boundVerificationCalls.Clear();
        CrossModuleCallTargets.Clear();
        SourceExtensionCallTargets.Clear();
        ExternalSourceCalls.Clear();
        _staticMethodSymbols.Clear();
        _functionNamespaces.Clear();
        _classScopes.Clear();
        _eventSymbols.Clear();
        _containingClasses.Clear();
        _qualifiedClassNames.Clear();
        _classSymbolIds.Clear();
        _classSymbols.Clear();
        _classesByQualifiedName.Clear();
        _interfacesBySymbolId.Clear();
        _enumMembers.Clear();
        _classesBySimpleName.Clear();
        _delegateDefinitions.Clear();
        _flowNarrowingSources.Clear();
        _memberFlowStorages.Clear();
        _topLevelFunctionLookupNames.Clear();
        _symbolsById.Clear();
        _sourceTypeSymbols = null;
        _metadataTypeParameterScopes.Clear();
        _symbolOrder.Clear();
        _declarationIdOccurrences.Clear();
        _snapshotLocalContexts.Clear();
        _localFunctionSymbols.Clear();
        _boundLocalFunctions.Clear();
        _delegateTypeNames.Clear();
        _misplacedRowsReported.Clear();
        _currentNamespaceIdentity = null;
        _currentNamespaceScopeId = null;
        _transientBindingId = CreateSnapshotIdentityKey(module, out var hasToolingIdentity);
        _toolingIds = hasToolingIdentity
            ? Ids.DeclarationIdentityStore.ToolingIds(module).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        _moduleSymbolId = module.ToolingId != null
            ? SymbolId.Create("declaration", module.ToolingId)
            : SymbolId.Create("source", _sourceIdentity, "module", module.Name);
        _declarationContext = _moduleSymbolId;

        var functions = new List<BoundFunction>();

        // v0.15 E2 slice b — §DEL names first, before ANY registration: a row on
        // a delegate-typed parameter must not be reported as misplaced merely
        // because top-level functions register before module.Delegates do.
        CollectDelegateTypeNames(module);
        CollectDelegateDefinitions(module);

        RegisterTopLevelFunctions(module);
        CheckGeneratedModuleNameCollisions(module);
        RegisterAdditionalTypes(module);
        foreach (var cls in module.Classes)
            RegisterClassTree(cls, _moduleSymbolId, null);

        if (declarationsOnly)
            return new BoundModule(module.Span, module.Name, [],
                _symbolOrder.ToDictionary(id => id, id => _symbolsById[id]));

        foreach (var variable in _symbolsById.Values.OfType<VariableSymbol>().Where(variable =>
                     (variable.IsField || variable.IsProperty) && variable.MethodInputArrayType is not null))
        {
            if (TryBuildMethodInputArrayType(variable.TypeName, out var array,
                    declaringTypeName: variable.DeclaringTypeName))
                variable.MethodInputArrayType = array;
        }

        // v0.15 E2 slice b, pin P6.
        CheckEffectRowPlacement(module);

        foreach (var func in module.Functions)
            functions.Add(BindFunction(func));

        foreach (var cls in module.Classes)
            BindClassMembers(cls, functions);

        foreach (var (owner, declaration) in _interfacesBySymbolId.Values)
        {
            foreach (var method in declaration.Methods.Where(method =>
                         method.Preconditions.Count > 0 || method.Postconditions.Count > 0))
            {
                functions.Add(BindInterfaceContracts(owner, declaration, method));
            }
        }

        functions.AddRange(_boundLocalFunctions);

        return new BoundModule(
            module.Span,
            module.Name,
            functions,
            _symbolOrder.ToDictionary(id => id, id => _symbolsById[id]));
    }

    private BoundFunction BindInterfaceContracts(
        TypeSymbol owner, InterfaceDefinitionNode declaration, MethodSignatureNode method)
    {
        var declarationScope = _scope.CreateChild();
        foreach (var member in declaration.Methods)
            declarationScope.TryDeclareOverload(GetCallableLookupName(member.Name), _functionSymbols[member], out _);
        var functionScope = declarationScope.CreateChild();
        using var scope = PushScope(functionScope);
        using var typeParameters = PushMemberTypeParameters(
            declaration.TypeParameters.Concat(method.TypeParameters).ToArray());
        var symbol = _functionSymbols[method];
        using var identity = PushDeclarationContext(symbol.Id);
        var previousClassName = _currentClassName;
        var previousNamespaceIdentity = _currentNamespaceIdentity;
        var previousNamespaceScopeId = _currentNamespaceScopeId;
        _currentClassName = owner.QualifiedName;
        _currentNamespaceIdentity = declaration.NamespaceIdentity;
        _currentNamespaceScopeId = declaration.NamespaceScopeId;
        try
        {
            DeclareParameters(symbol.Parameters, method.Parameters);
            return new BoundFunction(method.Span, symbol, [], functionScope,
                ExtractMethodEffects(method.Effects), BoundMemberKind.Method, owner.QualifiedName)
            {
                ContractConditions = BindContracts(method.Preconditions, method.Postconditions, symbol.BodyReturnType)
            };
        }
        finally
        {
            _currentClassName = previousClassName;
            _currentNamespaceIdentity = previousNamespaceIdentity;
            _currentNamespaceScopeId = previousNamespaceScopeId;
        }
    }

    private void RegisterTopLevelFunctions(ModuleNode module)
    {
        var ambiguousKeys = module.Functions.GroupBy(function =>
            CallableIdentityKey(function.Name, function.Parameters, function.TypeParameters, function.NamespaceIdentity))
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            var fallbackKey = CallableIdentityKey(function.Name, function.Parameters, function.TypeParameters, function.NamespaceIdentity);
            if (ambiguousKeys.Contains(fallbackKey))
                fallbackKey += ":ambiguous-snapshot:" + _transientBindingId;
            var symbol = CreateFunctionSymbol(
                CreateDeclarationId(_moduleSymbolId, "function", function.Id, fallbackKey),
                function.Name,
                function.Output?.TypeName ?? "VOID",
                GetCallableTypeParameters(
                    function.Name,
                    function.TypeParameters.Select(parameter => parameter.Name)),
                function.Parameters,
                function.IdentifierSpan,
                function.Visibility,
                containingTypeName: null,
                definitionSpan: function.Span,
                output: function.Output,
                effectParameters: function.EffectParameters,
                lexicalTypeContext: function.NamespaceIdentity,
                isAsync: function.IsAsync);
            _functionSymbols.Add(function, symbol);
            _functionNamespaces.Add(symbol.Id, function.NamespaceIdentity);

            var simpleLookupName = GetCallableLookupName(function.Name);
            var lookupName = QualifyTopLevelName(
                function,
                simpleLookupName);
            if (!_topLevelFunctionLookupNames.TryGetValue(
                    simpleLookupName,
                    out var qualifiedLookupNames))
            {
                qualifiedLookupNames = new HashSet<string>(StringComparer.Ordinal);
                _topLevelFunctionLookupNames.Add(
                    simpleLookupName,
                    qualifiedLookupNames);
            }
            qualifiedLookupNames.Add(lookupName);

            if (!_scope.TryDeclareOverload(lookupName, symbol, out var duplicate))
                ReportDuplicateSignature(function.Span, lookupName, symbol, duplicate);
        }
    }

    private void RegisterAdditionalTypes(ModuleNode module)
    {
        foreach (var @interface in module.Interfaces)
        {
            RegisterInterface(@interface, _moduleSymbolId, GetQualifiedTypeName(@interface));
        }

        foreach (var @enum in module.Enums)
        {
            RegisterEnum(@enum, _moduleSymbolId, GetQualifiedTypeName(@enum));
        }

        foreach (var @delegate in module.Delegates)
        {
            RegisterTypeSymbol(
                _moduleSymbolId,
                "delegate",
                @delegate.Id,
                @delegate.Name,
                GetQualifiedTypeName(@delegate),
                Visibility.Public,
                @delegate.IdentifierSpan,
                @delegate.Span);
        }
    }

    private void CheckGeneratedModuleNameCollisions(ModuleNode module)
    {
        var fallbackNamespace = module.Name == "_global" ? "" : module.Name;
        var generatedNames = module.Functions
            .Select(function => function.NamespaceIdentity ?? fallbackNamespace)
            .Distinct(StringComparer.Ordinal)
            .Select(ns => (Namespace: ns, Name: string.IsNullOrEmpty(ns)
                ? "GlobalModule" : ns.Split('.').Last() + "Module"))
            .ToHashSet();
        foreach (var cls in module.Classes)
        {
            if (cls.TypeParameters.Count == 0
                && generatedNames.Contains((cls.NamespaceIdentity ?? fallbackNamespace, cls.Name)))
            {
                _diagnostics.ReportError(cls.IdentifierSpan, DiagnosticCode.DuplicateDefinition,
                    $"Type '{cls.Name}' collides with the generated module in the same namespace.");
            }
        }
    }

    private TypeSymbol RegisterTypeSymbol(
        SymbolId parentIdentity,
        string kind,
        string stableAstId,
        string name,
        string qualifiedName,
        Visibility visibility,
        Parsing.TextSpan declarationSpan,
        Parsing.TextSpan definitionSpan)
    {
        var symbol = new TypeSymbol(
            CreateDeclarationId(parentIdentity, kind, stableAstId, qualifiedName),
            name,
            qualifiedName,
            visibility,
            declarationSpan,
            definitionSpan,
            // E1 slice 2b: §DEL declarations are function types. Marking the
            // symbol is what lets a consumer ask the type instead of the string.
            isDelegate: kind == "delegate")
        {
            IsReferenceType = kind is "interface" or "delegate",
            IsEnum = kind == "enum"
        };
        TrackSymbol(symbol);
        return symbol;
    }

    private void RegisterEnum(EnumDefinitionNode declaration, SymbolId parentIdentity, string qualifiedName)
    {
        var owner = RegisterTypeSymbol(parentIdentity, "enum", declaration.Id, declaration.Name,
            qualifiedName, declaration.Visibility, declaration.IdentifierSpan, declaration.Span);
        _enumMembers.Add(owner.Id, declaration.Members.Select(member =>
        {
            var symbol = new VariableSymbol(
                CreateDeclarationId(owner.Id, "enum-member", stableAstId: null, member.Name),
                member.Name, qualifiedName, isMutable: false, declarationSpan: member.IdentifierSpan,
                declaringTypeName: qualifiedName, isField: true, isStatic: true)
            {
                IsEnumMember = true
            };
            TrackSymbol(symbol);
            return symbol;
        }).ToArray());
    }

    private void RegisterInterface(InterfaceDefinitionNode declaration, SymbolId parentIdentity, string qualifiedName)
    {
        var owner = RegisterTypeSymbol(parentIdentity, "interface", declaration.Id, declaration.Name,
            qualifiedName, declaration.Visibility, declaration.IdentifierSpan, declaration.Span);
        _interfacesBySymbolId.Add(owner.Id, (owner, declaration));
        foreach (var method in declaration.Methods)
        {
            var lookupName = $"{qualifiedName}.{GetCallableLookupName(method.Name)}";
            var symbol = CreateFunctionSymbol(
                CreateDeclarationId(owner.Id, "interface-method", method.Id,
                    CallableIdentityKey(method.Name, method.Parameters, method.TypeParameters)),
                $"{qualifiedName}.{method.Name}", method.Output?.TypeName ?? "VOID",
                GetCallableTypeParameters(method.Name, method.TypeParameters.Select(parameter => parameter.Name)),
                method.Parameters, method.IdentifierSpan, Visibility.Public, qualifiedName, method.Span,
                output: method.Output, effectParameters: method.EffectParameters);
            _functionSymbols.Add(method, symbol);
            if (!_scope.TryDeclareOverload(lookupName, symbol, out var duplicate))
                ReportDuplicateSignature(method.Span, lookupName, symbol, duplicate);
        }
    }

    private void RegisterClassTree(
        ClassDefinitionNode cls,
        SymbolId parentIdentity,
        string? containingTypeName)
    {
        var classIdentity = CreateDeclarationId(parentIdentity, cls.IsModule ? "module" : cls.IsRecord ? "record" : "class", cls.Id, GetQualifiedTypeName(cls));
        var qualifiedClassName = containingTypeName == null
            ? GetQualifiedTypeName(cls)
            : $"{containingTypeName}.{GetTypeNameWithArity(cls.Name, cls.TypeParameters.Count)}";
        var classScope = _scope.CreateChild();
        var classSymbol = new TypeSymbol(
            classIdentity,
            cls.Name,
            qualifiedClassName,
            cls.Visibility,
            cls.IdentifierSpan,
            cls.Span)
        {
            IsReferenceType = !cls.IsStruct,
            IsRecord = cls.IsRecord
        };
        TrackSymbol(classSymbol);

        _classScopes.Add(cls, classScope);
        if (containingTypeName is not null)
            _containingClasses.Add(cls, _classesByQualifiedName[containingTypeName]);
        _qualifiedClassNames.Add(cls, qualifiedClassName);
        _classSymbolIds.Add(cls, classIdentity);
        _classSymbols.Add(cls, classSymbol);
        if (_classesByQualifiedName.TryGetValue(qualifiedClassName, out var previous)
            && (cls.IsModule || previous.IsModule)
            && !(cls.IsModule && previous.IsModule && cls.IsPartial && previous.IsPartial))
        {
            _diagnostics.ReportError(cls.IdentifierSpan, DiagnosticCode.DuplicateDefinition,
                $"Module/type name '{qualifiedClassName}' is already defined in this namespace or containing type.");
        }
        _classesByQualifiedName[qualifiedClassName] = cls;
        if (cls.IsModule
            && (cls.IsAbstract || cls.IsSealed || cls.IsStruct || cls.IsReadOnly
                || cls.BaseClass != null || cls.ImplementedInterfaces.Count > 0
                || cls.Indexers.Count > 0 || cls.OperatorOverloads.Count > 0
                || cls.Constructors.Any(constructor => !constructor.IsStatic)))
        {
            _diagnostics.ReportError(cls.IdentifierSpan, DiagnosticCode.InvalidReference,
                $"Module '{qualifiedClassName}' cannot declare instance-type modifiers, inheritance, indexers, operators, or instance constructors.");
        }
        var simpleLookupName = GetTypeNameWithArity(
            cls.Name,
            cls.TypeParameters.Count);
        if (!_classesBySimpleName.TryGetValue(simpleLookupName, out var simpleMatches))
        {
            simpleMatches = new List<ClassDefinitionNode>();
            _classesBySimpleName.Add(simpleLookupName, simpleMatches);
        }
        simpleMatches.Add(cls);

        var fields = EnumerateFieldRegistrations(cls).ToArray();
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            var (field, conditionalAlternative) = fields[fieldIndex];
            var isMutable = !field.Modifiers.HasFlag(MethodModifiers.Readonly);
            var symbol = CreateVariable(
                CreateDeclarationId(classIdentity, "field", field.ToolingId, field.Name),
                field.Name,
                field.TypeName,
                isMutable,
                isParameter: false,
                ParameterModifier.None,
                field.IdentifierSpan,
                visibility: field.Visibility,
                declaringTypeName: qualifiedClassName,
                isField: true,
                isStatic: field.IsStatic || field.Modifiers.HasFlag(MethodModifiers.Const),
                conditionalAlternative: conditionalAlternative,
                // v0.14 nullability workstream (task #3) — inherit declared
                // STRING nullability on fields so subsequent references
                // through BoundVariableExpression carry the correct
                // annotation. Scoped to STRING per §D6; non-STRING keeps
                // the safe Oblivious default.
                nullableAnnotation: TryReadDeclaredStringAnnotation(field.TypeName),
                // v0.15 E2 slice b — position 8's row (§3.3). A §FLD has no
                // type-parameter list, so no `eff` binders are in scope.
                functionType: TryBuildFunctionType(
                    field.TypeName, field.Row, binders: null, qualifiedClassName));
            if (!classScope.TryDeclare(symbol))
            {
                _diagnostics.ReportError(
                    field.Span,
                    DiagnosticCode.DuplicateDefinition,
                    $"Field '{field.Name}' is already defined in '{qualifiedClassName}'");
            }
        }

        var properties = EnumeratePropertyRegistrations(cls).ToArray();
        for (var propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
        {
            var (property, conditionalAlternative) = properties[propertyIndex];
            var symbol = CreateVariable(
                CreateDeclarationId(classIdentity, "property", property.Id, property.Name),
                property.Name,
                property.TypeName,
                property.Setter != null || property.Initer != null,
                isParameter: false,
                ParameterModifier.None,
                property.IdentifierSpan,
                visibility: property.Visibility,
                declaringTypeName: qualifiedClassName,
                isProperty: true,
                isStatic: property.IsStatic,
                conditionalAlternative: conditionalAlternative,
                // v0.14 nullability workstream (task #3) — inherit declared
                // STRING nullability on properties so subsequent references
                // through BoundVariableExpression carry the correct
                // annotation. Scoped to STRING per §D6.
                nullableAnnotation: TryReadDeclaredStringAnnotation(property.TypeName));
            if (!classScope.TryDeclare(symbol))
            {
                _diagnostics.ReportError(
                    property.Span,
                    DiagnosticCode.DuplicateDefinition,
                    $"Property '{property.Name}' is already defined in '{qualifiedClassName}'");
            }
        }

        foreach (var (evt, alternative) in EnumerateEventRegistrations(cls))
        {
            var symbol = CreateVariable(
                CreateDeclarationId(classIdentity, "event", evt.ToolingId, evt.Name),
                evt.Name, evt.DelegateType, isMutable: true, isParameter: false,
                ParameterModifier.None, evt.Span,
                visibility: evt.Visibility, declaringTypeName: qualifiedClassName,
                isField: !evt.HasAccessors, isStatic: evt.IsStatic,
                conditionalAlternative: alternative,
                functionType: TryBuildEventFunctionType(evt),
                eventDeclaration: evt);
            _eventSymbols.Add(evt, symbol);
            if (!classScope.TryDeclare(symbol))
                _diagnostics.ReportError(evt.Span, DiagnosticCode.DuplicateDefinition,
                    $"Event '{evt.Name}' is already defined in '{qualifiedClassName}'");
        }

        var methods = EnumerateMethodRegistrations(cls).ToArray();
        var ambiguousMethodKeys = methods.GroupBy(item =>
            CallableIdentityKey(item.Method.Name, item.Method.Parameters, item.Method.TypeParameters))
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        for (var methodIndex = 0; methodIndex < methods.Length; methodIndex++)
        {
            var (method, conditionalAlternative) = methods[methodIndex];
            var fallbackKey = CallableIdentityKey(method.Name, method.Parameters, method.TypeParameters);
            if (ambiguousMethodKeys.Contains(fallbackKey))
                fallbackKey += ":ambiguous-snapshot:" + _transientBindingId;
            var extensionParameters = method.Parameters
                .Select((parameter, index) => (parameter, index))
                .Where(item => item.parameter.Modifier.HasFlag(ParameterModifier.This))
                .ToArray();
            if (extensionParameters.Length > 0
                && (!cls.IsStatic || !method.IsStatic || containingTypeName is not null
                    || cls.TypeParameters.Count > 0
                    || extensionParameters.Length != 1 || extensionParameters[0].index != 0))
            {
                _diagnostics.ReportError(method.IdentifierSpan, DiagnosticCode.InvalidReference,
                    $"Extension function '{method.Name}' must be static in a non-nested, non-generic module, with 'this' only on its first parameter.");
            }
            var lookupName = GetCallableLookupName(method.Name);
            var qualifiedLookupName = $"{qualifiedClassName}.{lookupName}";
            var symbol = CreateFunctionSymbol(
                CreateDeclarationId(classIdentity, "method", method.Id, fallbackKey),
                $"{qualifiedClassName}.{method.Name}",
                method.Output?.TypeName ?? "VOID",
                GetCallableTypeParameters(
                    method.Name,
                    method.TypeParameters.Select(parameter => parameter.Name)),
                method.Parameters,
                method.IdentifierSpan,
                method.Visibility,
                qualifiedClassName,
                method.Span,
                conditionalAlternative,
                output: method.Output,
                effectParameters: method.EffectParameters,
                isAsync: method.IsAsync);
            _functionSymbols.Add(method, symbol);
            if (method.IsStatic)
                _staticMethodSymbols.Add(symbol);

            if (!classScope.TryDeclareOverload(lookupName, symbol, out var duplicate))
            {
                ReportDuplicateSignature(method.Span, qualifiedLookupName, symbol, duplicate);
                continue;
            }

            if (!_scope.TryDeclareOverload(qualifiedLookupName, symbol, out duplicate))
                ReportDuplicateSignature(method.Span, qualifiedLookupName, symbol, duplicate);
        }

        var constructors = EnumerateConstructorRegistrations(cls).ToArray();
        var ambiguousConstructorKeys = constructors.GroupBy(item =>
                CallableIdentityKey(item.Constructor.IsStatic ? ".cctor" : ".ctor", item.Constructor.Parameters, []))
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        for (var constructorIndex = 0; constructorIndex < constructors.Length; constructorIndex++)
        {
            var (constructor, conditionalAlternative) = constructors[constructorIndex];
            var constructorName = constructor.IsStatic ? ".cctor" : ".ctor";
            var qualifiedLookupName = $"{qualifiedClassName}.{constructorName}";
            var fallbackKey = CallableIdentityKey(constructorName, constructor.Parameters, []);
            if (ambiguousConstructorKeys.Contains(fallbackKey))
                fallbackKey += ":ambiguous-snapshot:" + _transientBindingId;
            var symbol = CreateFunctionSymbol(
                CreateDeclarationId(classIdentity, "constructor", constructor.Id,
                    fallbackKey),
                qualifiedLookupName,
                "VOID",
                Array.Empty<string>(),
                constructor.Parameters,
                cls.IdentifierSpan,
                constructor.Visibility,
                qualifiedClassName,
                constructor.Span,
                conditionalAlternative,
                // A §CTOR has no §O and no type-parameter list, so no return row
                // and no `eff` binders — only its parameters can carry a row.
                output: null,
                effectParameters: null);
            _functionSymbols.Add(constructor, symbol);

            if (!classScope.TryDeclareOverload(qualifiedLookupName, symbol, out var duplicate))
            {
                ReportDuplicateSignature(constructor.Span, qualifiedLookupName, symbol, duplicate);
                continue;
            }

            if (!constructor.IsStatic
                && !classScope.TryDeclareOverload(cls.Name, symbol, out duplicate))
            {
                ReportDuplicateSignature(constructor.Span, cls.Name, symbol, duplicate);
            }
            if (!_scope.TryDeclareOverload(qualifiedLookupName, symbol, out duplicate))
                ReportDuplicateSignature(constructor.Span, qualifiedLookupName, symbol, duplicate);
        }

        foreach (var nested in cls.NestedClasses)
        {
            RegisterClassTree(
                nested,
                classIdentity,
                qualifiedClassName);
        }
        foreach (var nested in cls.NestedInterfaces)
        {
            RegisterInterface(nested, classIdentity, $"{qualifiedClassName}.{nested.Name}");
        }
        foreach (var nested in cls.NestedEnums)
        {
            RegisterEnum(nested, classIdentity, $"{qualifiedClassName}.{nested.Name}");
        }
        foreach (var nested in cls.NestedDelegates)
        {
            RegisterTypeSymbol(
                classIdentity,
                "delegate",
                nested.Id,
                nested.Name,
                $"{qualifiedClassName}.{nested.Name}",
                Visibility.Public,
                nested.IdentifierSpan,
                nested.Span);
        }
    }

    private static IEnumerable<MemberPreprocessorBlockNode> EnumeratePreprocessorBranches(
        ClassDefinitionNode cls)
    {
        foreach (var block in cls.PreprocessorBlocks)
        {
            for (var branch = block; branch != null; branch = branch.ElseBranch)
                yield return branch;
        }
    }

    private static IEnumerable<ClassFieldNode> EnumerateFields(ClassDefinitionNode cls) =>
        cls.Fields.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Fields));

    private static IEnumerable<(ClassFieldNode Field, ConditionalAlternative? Alternative)>
        EnumerateFieldRegistrations(ClassDefinitionNode cls)
    {
        foreach (var field in cls.Fields)
            yield return (field, null);
        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
        {
            foreach (var field in branch.Fields)
                yield return (field, alternative);
        }
    }

    private static IEnumerable<PropertyNode> EnumerateProperties(ClassDefinitionNode cls) =>
        cls.Properties.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Properties));

    private static IEnumerable<(PropertyNode Property, ConditionalAlternative? Alternative)>
        EnumeratePropertyRegistrations(ClassDefinitionNode cls)
    {
        foreach (var property in cls.Properties)
            yield return (property, null);
        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
        {
            foreach (var property in branch.Properties)
                yield return (property, alternative);
        }
    }

    private static IEnumerable<IndexerNode> EnumerateIndexers(ClassDefinitionNode cls) =>
        cls.Indexers.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Indexers));

    private static IEnumerable<ConstructorNode> EnumerateConstructors(ClassDefinitionNode cls) =>
        cls.Constructors.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Constructors));

    private static IEnumerable<(ConstructorNode Constructor, ConditionalAlternative? Alternative)>
        EnumerateConstructorRegistrations(ClassDefinitionNode cls)
    {
        foreach (var constructor in cls.Constructors)
            yield return (constructor, null);
        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
        {
            foreach (var constructor in branch.Constructors)
                yield return (constructor, alternative);
        }
    }

    private static IEnumerable<MethodNode> EnumerateMethods(ClassDefinitionNode cls) =>
        cls.Methods.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Methods));

    private static IEnumerable<(MethodNode Method, ConditionalAlternative? Alternative)>
        EnumerateMethodRegistrations(ClassDefinitionNode cls)
    {
        foreach (var method in cls.Methods)
            yield return (method, null);

        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
        {
            foreach (var method in branch.Methods)
                yield return (method, alternative);
        }
    }

    private static IEnumerable<(MemberPreprocessorBlockNode Branch, ConditionalAlternative Alternative)>
        EnumerateConditionalMemberBranches(ClassDefinitionNode cls)
    {
        for (var blockIndex = 0; blockIndex < cls.PreprocessorBlocks.Count; blockIndex++)
        {
            var groupId = $"{cls.Id}:member-pp:{blockIndex}";
            var branchIndex = 0;
            for (var branch = cls.PreprocessorBlocks[blockIndex];
                 branch != null;
                 branch = branch.ElseBranch)
            {
                yield return (branch, new ConditionalAlternative(groupId, branchIndex++));
            }
        }
    }

    private static IEnumerable<EventDefinitionNode> EnumerateEvents(ClassDefinitionNode cls) =>
        cls.Events.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Events));

    private static IEnumerable<(EventDefinitionNode Event, ConditionalAlternative? Alternative)>
        EnumerateEventRegistrations(ClassDefinitionNode cls)
    {
        foreach (var evt in cls.Events)
            yield return (evt, null);
        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
            foreach (var evt in branch.Events)
                yield return (evt, alternative);
    }

    private static IEnumerable<OperatorOverloadNode> EnumerateOperators(ClassDefinitionNode cls) =>
        cls.OperatorOverloads.Concat(
            EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.OperatorOverloads));

    private FunctionSymbol CreateFunctionSymbol(
        SymbolId id,
        string name,
        string returnType,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        Parsing.TextSpan declarationSpan,
        Visibility visibility,
        string? containingTypeName,
        Parsing.TextSpan definitionSpan,
        ConditionalAlternative? conditionalAlternative = null,
        // v0.15 E2 slice b — the declaration's own §E-row context. `output`
        // carries position 6's row; `effectParameters` are the `eff` binders a
        // parameter row may mention (§7.2).
        OutputNode? output = null,
        IReadOnlyList<EffectParameterInfo>? effectParameters = null,
        string? lexicalTypeContext = null,
        bool isAsync = false)
    {
        var classTypeParameters = containingTypeName is not null
            && _classesByQualifiedName.TryGetValue(containingTypeName, out var declaringClass)
                ? ReturnShape.GetCallableTypeParameters(declaringClass, _asyncTypeModule)
                    .Select(parameter => parameter.Name)
                : [];
        var returnShape = ReturnShape.GetAsyncReturnTypes(returnType, isAsync, _asyncTypeModule,
            typeParameters.Concat(classTypeParameters));
        var parameterSymbols = parameters
            .Select((parameter, index) => CreateVariable(
                CreateDeclarationId(id, "parameter", stableAstId: null, parameter.Name),
                parameter.Name,
                parameter.TypeName,
                isMutable: false,
                isParameter: true,
                parameter.Modifier,
                parameter.IdentifierSpan,
                parameter.DefaultValue,
                // v0.14 nullability workstream (task #3) — inherit the
                // declared STRING nullability on function parameters so
                // that BoundVariableExpression reads of the parameter
                // flow the correct annotation into downstream checks
                // (unblocks the pin test
                // Calor0272_StillFires_For_ParameterReference_KnownLimitation).
                // parameter.TypeName is already ExpandType'd by the
                // parser (?string → NULLABLE[inner=STRING]); the helper
                // handles both forms. Scoped to STRING per §D6.
                nullableAnnotation: TryReadDeclaredStringAnnotation(parameter.TypeName),
                // v0.15 E2 slice b — position 4/5's row (§3.3). Null unless the
                // parameter both carries a §E and spells a function type.
                functionType: TryBuildParameterFunctionType(
                    parameter, effectParameters,
                    containingTypeName ?? lexicalTypeContext)))
            .ToArray();
        var symbol = new FunctionSymbol(
            id,
            name,
            returnShape.CallableType,
            parameterSymbols,
            typeParameters,
            declarationSpan,
            visibility,
            containingTypeName,
            definitionSpan,
            conditionalAlternative)
        {
            // v0.15 E2 slice b — position 6's row (§3.3).
            BodyReturnType = returnShape.BodyType,
            ReturnFunctionType = isAsync ? null : TryBuildReturnFunctionType(
                output, effectParameters,
                containingTypeName ?? lexicalTypeContext),
        };
        TrackSymbol(symbol);
        return symbol;
    }

    private VariableSymbol CreateVariable(
        SymbolId id,
        string name,
        string typeName,
        bool isMutable,
        bool isParameter,
        ParameterModifier modifier,
        Parsing.TextSpan declarationSpan,
        ExpressionNode? defaultValue = null,
        Visibility visibility = Visibility.Public,
        string? declaringTypeName = null,
        bool isField = false,
        bool isProperty = false,
        bool isStatic = false,
        ConditionalAlternative? conditionalAlternative = null,
        BoundTypes.NullableAnnotation nullableAnnotation = BoundTypes.NullableAnnotation.Oblivious,
        BoundTypes.FunctionBoundType? functionType = null,
        BoundTypes.NominalBoundType? inferredReferenceType = null,
        bool isTypeInferred = false,
        BoundTypes.ArrayBoundType? methodInputArrayType = null,
        BoundTypes.GenericInstantiationBoundType? genericNullabilityType = null,
        bool hasUnknownInferredType = false,
        EventDefinitionNode? eventDeclaration = null)
    {
        var symbol = new VariableSymbol(
            id,
            name,
            typeName,
            isMutable,
            isParameter,
            modifier,
            declarationSpan,
            defaultValue,
            visibility,
            declaringTypeName,
            isField,
            isProperty,
            isStatic,
            conditionalAlternative,
            nullableAnnotation,
            functionType)
        {
            EventDeclaration = eventDeclaration,
            InferredReferenceType = inferredReferenceType,
            IsTypeInferred = isTypeInferred,
            HasUnknownInferredType = hasUnknownInferredType,
            MethodInputArrayType = methodInputArrayType
                ?? (TryBuildMethodInputArrayType(typeName, out var arrayType) ? arrayType : null),
            GenericNullabilityType = genericNullabilityType
                ?? (TryBuildDeclaredNullabilityShape(typeName, out var nullabilityShape)
                ? nullabilityShape as BoundTypes.GenericInstantiationBoundType
                : null)
        };
        TrackSymbol(symbol);
        return symbol;
    }

    private VariableSymbol CreateLocalVariable(
        string name,
        string typeName,
        bool isMutable,
        bool isParameter,
        ParameterModifier modifier,
        Parsing.TextSpan declarationSpan,
        string kind,
        ExpressionNode? defaultValue = null,
        BoundTypes.NullableAnnotation nullableAnnotation = BoundTypes.NullableAnnotation.Oblivious,
        BoundTypes.FunctionBoundType? functionType = null,
        BoundTypes.NominalBoundType? inferredReferenceType = null,
        bool isTypeInferred = false,
        BoundTypes.ArrayBoundType? methodInputArrayType = null,
        BoundTypes.GenericInstantiationBoundType? genericNullabilityType = null,
        bool hasUnknownInferredType = false)
    {
        var context = _declarationContext.IsNone ? _moduleSymbolId : _declarationContext;
        if (!_snapshotLocalContexts.TryGetValue((context, kind), out var snapshotContext))
        {
            snapshotContext = context.Append("snapshot", _transientBindingId, kind);
            _snapshotLocalContexts.Add((context, kind), snapshotContext);
        }
        return CreateVariable(
            RegisterDeclarationOccurrence(snapshotContext.AppendEscapedSuffix(
                LocalIdentitySuffix(name, typeName, declarationSpan.Start))),
            name,
            typeName,
            isMutable,
            isParameter,
            modifier,
            declarationSpan,
            defaultValue,
            nullableAnnotation: nullableAnnotation,
            functionType: functionType,
            inferredReferenceType: inferredReferenceType,
            isTypeInferred: isTypeInferred,
            methodInputArrayType: methodInputArrayType,
            genericNullabilityType: genericNullabilityType,
            hasUnknownInferredType: hasUnknownInferredType);
    }

    private string LocalIdentitySuffix(string name, string typeName, int start)
    {
        _localIdentityBytes.Clear();
        _localIdentityWriter ??= new System.Text.Json.Utf8JsonWriter(_localIdentityBytes);
        var writer = _localIdentityWriter;
        writer.Reset(_localIdentityBytes);
        writer.WriteStartObject();
        writer.WriteString("name"u8, name);
        writer.WriteString("typeName"u8, typeName);
        writer.WriteNumber("Start"u8, start);
        writer.WriteEndObject();
        writer.Flush();
        static bool Unescaped(byte value) => value is >= (byte)'a' and <= (byte)'z'
            or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9'
            or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~';
        var length = 8; // /name%3A
        foreach (var value in _localIdentityBytes.WrittenSpan) length += Unescaped(value) ? 1 : 3;
        return string.Create(length, _localIdentityBytes.WrittenMemory, static (result, bytes) =>
        {
            "/name%3A".AsSpan().CopyTo(result);
            var index = 8;
            const string hex = "0123456789ABCDEF";
            foreach (var value in bytes.Span)
            {
                if (Unescaped(value)) result[index++] = (char)value;
                else
                {
                    result[index++] = '%';
                    result[index++] = hex[value >> 4];
                    result[index++] = hex[value & 15];
                }
            }
        });
    }

    private static string SnapshotIdentityKey(ModuleNode module) =>
        CreateSnapshotIdentityKey(module, out _);

    private static string CreateSnapshotIdentityKey(ModuleNode module, out bool hasToolingIdentity)
    {
        // Snapshot-local handles must change after edits, not between identical compilations.
        // Tooling and parser identities are metadata, not part of the syntax fingerprint.
        using var text = new SnapshotHashWriter();
        var pending = new Stack<AstNode>();
        var visited = System.Threading.Interlocked.Exchange(ref SnapshotVisited, null)
            ?? new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var children = new List<AstNode>();
        hasToolingIdentity = false;
        try
        {
            pending.Push(module);
            while (pending.TryPop(out var node))
            {
                if (!visited.Add(node)) continue;
                // Separate callable bodies and type-kind validation retain the declaration inventory.
                hasToolingIdentity |= node.ToolingId != null
                    || node is LocalFunctionStatementNode or TypeDefinitionNode;
                text.Append(node.GetType().FullName!);
                text.Append(":");
                text.Append(node.Span.Start);
                text.Append(":");
                text.Append(node.Span.Length);
                foreach (var plan in SnapshotProperties.GetOrAdd(node.GetType(), static type =>
                {
                    var children = RecursiveAstWalker.GetAllChildProperties(type);
                    return type.GetProperties().Where(property => property.CanRead
                        && property.GetIndexParameters().Length == 0
                        && property.Name is not ("Id" or "LocalId" or "ToolingId"))
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                        .Select(property =>
                        {
                            var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                            return new SnapshotProperty(property,
                                valueType.IsPrimitive || valueType.IsEnum || valueType == typeof(string) || valueType == typeof(decimal),
                                children.Contains(property));
                        }).ToArray();
                }))
                {
                    var property = plan.Property;
                    if (property.Name == nameof(AstNode.Span) && property.DeclaringType == typeof(AstNode))
                    {
                        text.ResetJson();
                        WriteSnapshotSpan(text.Json, node.Span);
                        text.Append("|Span=");
                        text.AppendJson();
                        continue;
                    }
                    var value = property.GetValue(node);
                    text.ResetJson();
                    if (value == null) text.Json.WriteNullValue();
                    else if (plan.Scalar)
                        text.Json.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                    else if (!WriteSnapshotMetadata(text.Json, value, text.MapEntries))
                    {
                        if (value is AstNode or IEnumerable<AstNode>
                            || plan.Child) continue;
                        var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                        throw new InvalidOperationException($"Unsupported identity snapshot metadata: {node.GetType().Name}.{property.Name} ({valueType.Name}).");
                    }
                    text.Append("|");
                    text.Append(property.Name);
                    text.Append("=");
                    text.AppendJson();
                }
                text.Append(Environment.NewLine);
                children.Clear();
                foreach (var property in RecursiveAstWalker.GetAllChildProperties(node.GetType()))
                    CollectSnapshotChildren(property.GetValue(node), children);
                // The global visited set preserves first-visit order without per-node deduplication.
                for (var index = children.Count - 1; index >= 0; index--) pending.Push(children[index]);
            }
            return text.Finish();
        }
        finally
        {
            visited.Clear();
            // Retain one small scratch set, never ASTs or large-module working storage.
            if (visited.EnsureCapacity(0) <= 4096)
                System.Threading.Interlocked.CompareExchange(ref SnapshotVisited, visited, null);
        }
    }

    private static void CollectSnapshotChildren(object? value, List<AstNode> children)
    {
        switch (value)
        {
            case AstNode node: children.Add(node); break;
            case ObjectInitializerAssignment initializer: children.Add(initializer.Value); break;
            case InlineRefinementInfo refinement: children.Add(refinement.Predicate); break;
            case IReadOnlyList<AstNode> nodes:
                for (var index = 0; index < nodes.Count; index++)
                    if (nodes[index] is { } child) children.Add(child);
                break;
            case System.Collections.IEnumerable sequence when value is not string:
                foreach (var item in sequence) CollectSnapshotChildren(item, children);
                break;
        }
    }

    private sealed record SnapshotProperty(System.Reflection.PropertyInfo Property, bool Scalar, bool Child);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, SnapshotProperty[]> SnapshotProperties = new();
    private static HashSet<AstNode>? SnapshotVisited;

    private sealed class SnapshotHashWriter : IDisposable
    {
        private readonly System.Security.Cryptography.IncrementalHash _hash =
            System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        private readonly System.Text.Encoder _encoder = System.Text.Encoding.UTF8.GetEncoder();
        private readonly byte[] _bytes = new byte[4096];
        private readonly System.Buffers.ArrayBufferWriter<byte> _jsonBytes = new(512);
        public System.Text.Json.Utf8JsonWriter Json { get; }
        public List<KeyValuePair<string, string>> MapEntries { get; } = new();
        public SnapshotHashWriter() => Json = new(_jsonBytes);

        public void Append(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                _encoder.Convert(text, _bytes, false, out var characters, out var bytes, out _);
                _hash.AppendData(_bytes.AsSpan(0, bytes));
                text = text[characters..];
            }
        }

        public void Append(int value)
        {
            Span<char> text = stackalloc char[32];
            if (!value.TryFormat(text, out var length))
            {
                Append(value.ToString());
                return;
            }
            Append(text[..length]);
        }

        public void ResetJson()
        {
            _jsonBytes.Clear();
            Json.Reset(_jsonBytes);
        }

        public void AppendJson()
        {
            Json.Flush();
            _hash.AppendData(_jsonBytes.WrittenSpan);
        }

        public string Finish()
        {
            _encoder.Convert(ReadOnlySpan<char>.Empty, _bytes, true, out _, out var bytes, out _);
            _hash.AppendData(_bytes.AsSpan(0, bytes));
            return Convert.ToHexString(_hash.GetHashAndReset());
        }

        public void Dispose()
        {
            Json.Dispose();
            _hash.Dispose();
        }
    }

    private static bool WriteSnapshotMetadata(System.Text.Json.Utf8JsonWriter writer, object value,
        List<KeyValuePair<string, string>> mapEntries)
    {
        switch (value)
        {
            case AttributeCollection attributes:
                WriteSnapshotMap(writer, attributes.All(), mapEntries); break;
            case IReadOnlyDictionary<string, string> map:
                WriteSnapshotMap(writer, map, mapEntries); break;
            case IReadOnlyList<string> strings:
                System.Text.Json.JsonSerializer.Serialize(writer, strings); break;
            case IReadOnlyList<int> ordinals:
                System.Text.Json.JsonSerializer.Serialize(writer, ordinals); break;
            case IReadOnlyList<EffectParameterInfo> parameters:
                System.Text.Json.JsonSerializer.Serialize(writer, parameters); break;
            case IReadOnlyList<NamespaceScopeInfo> scopes:
                System.Text.Json.JsonSerializer.Serialize(writer, scopes.Select(scope => new { scope.Name, scope.FullName, scope.IsFileScoped, scope.Span, scope.Kind })); break;
            case IReadOnlyList<TextSpan> spans:
                System.Text.Json.JsonSerializer.Serialize(writer, spans); break;
            case IReadOnlyList<ObjectInitializerAssignment> initializers:
                System.Text.Json.JsonSerializer.Serialize(writer, initializers.Select(initializer => initializer.PropertyName)); break;
            case IReadOnlyList<CalorAttributeArgument> arguments:
                System.Text.Json.JsonSerializer.Serialize(writer, arguments.Select(argument => new { argument.Name, Value = argument.GetCalorFormattedValue() })); break;
            case InlineRefinementInfo refinement:
                System.Text.Json.JsonSerializer.Serialize(writer, refinement.BaseTypeName); break;
            case TextSpan span:
                WriteSnapshotSpan(writer, span); break;
            case DateOnly date:
                System.Text.Json.JsonSerializer.Serialize(writer, date); break;
            case DateTime date:
                System.Text.Json.JsonSerializer.Serialize(writer, date); break;
            default: return false;
        }
        return true;
    }

    private static void WriteSnapshotSpan(System.Text.Json.Utf8JsonWriter writer, TextSpan span)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Start"u8, span.Start);
        writer.WriteNumber("Length"u8, span.Length);
        writer.WriteNumber("Line"u8, span.Line);
        writer.WriteNumber("Column"u8, span.Column);
        writer.WriteNumber("End"u8, span.End);
        writer.WriteEndObject();
    }

    private static void WriteSnapshotMap(System.Text.Json.Utf8JsonWriter writer,
        IEnumerable<KeyValuePair<string, string>> map, List<KeyValuePair<string, string>> entries)
    {
        entries.Clear();
        foreach (var pair in map) entries.Add(pair);
        entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        writer.WriteStartArray();
        foreach (var pair in entries)
        {
            writer.WriteStartObject();
            writer.WriteString("Key"u8, pair.Key);
            writer.WriteString("Value"u8, pair.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static string CallableIdentityKey(
        string name,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<TypeParameterNode> typeParameters,
        string? namespaceIdentity = null)
    {
        var names = typeParameters.Select(parameter => parameter.Name).ToArray();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            namespaceIdentity,
            name,
            arity = names.Length,
            parameters = parameters.Select(parameter => new
            {
                type = TypeIdentity.CanonicalizeSignature(parameter.TypeName, names),
                modifier = parameter.Modifier.ToString()
            })
        });
    }

    private SymbolId CreateDeclarationId(
        SymbolId parent,
        string kind,
        string? stableAstId,
        string fallbackName)
    {
        var baseId = stableAstId != null && _toolingIds.Contains(stableAstId)
            ? SymbolId.Create("declaration", stableAstId)
            : parent.Append(kind, $"name:{fallbackName}");
        return RegisterDeclarationOccurrence(baseId);
    }

    private SymbolId RegisterDeclarationOccurrence(SymbolId baseId)
    {
        var key = baseId;
        if (!_declarationIdOccurrences.TryGetValue(key, out var occurrence))
        {
            _declarationOccurrenceJournal?.Add((key, 0));
            _declarationIdOccurrences.Add(key, 1);
            return baseId;
        }

        _declarationOccurrenceJournal?.Add((key, occurrence));
        _declarationIdOccurrences[key] = occurrence + 1;
        return baseId.Append($"duplicate:{occurrence}");
    }

    private VariableSymbol CreateUnresolvedVariable(ReferenceNode reference)
    {
        return new VariableSymbol(
            SymbolId.None,
            reference.Name,
            "INT",
            isMutable: false,
            isParameter: false,
            ParameterModifier.None,
            reference.Span);
    }

    private TypeSymbol[] SourceTypeSymbols() =>
        _sourceTypeSymbols ??= _symbolsById.Values.OfType<TypeSymbol>().ToArray();

    private void TrackSymbol(Symbol symbol)
    {
        if (!symbol.Id.IsNone)
        {
            _symbolsById.Add(symbol.Id, symbol);
            _symbolOrder.Add(symbol.Id);
            if (symbol is TypeSymbol)
                _sourceTypeSymbols = null;
        }
    }

    private void ReportDuplicateSignature(
        Parsing.TextSpan span,
        string lookupName,
        FunctionSymbol symbol,
        FunctionSymbol? duplicate)
    {
        var conflict = duplicate == null ? string.Empty : $" Conflicts with '{duplicate.Id}'.";
        _diagnostics.ReportError(
            span,
            DiagnosticCode.DuplicateFunctionSignature,
            $"Duplicate signature '{lookupName}{FormatSignatureSuffix(symbol)}'.{conflict}");
    }

    private static string FormatSignatureSuffix(FunctionSymbol symbol)
    {
        var generic = symbol.GenericArity == 0 ? string.Empty : $"<{symbol.GenericArity}>";
        var parameters = string.Join(
            ", ",
            symbol.Parameters.Select(parameter =>
                $"{FormatParameterModifier(parameter.Modifier)}{TypeIdentity.CanonicalizeSignature(parameter.TypeName, symbol.TypeParameters)}"));
        return $"{generic}({parameters})";
    }

    private static string FormatParameterModifier(ParameterModifier modifier)
    {
        var callModifier = modifier & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In);
        return callModifier switch
        {
            ParameterModifier.Ref => "ref ",
            ParameterModifier.Out => "out ",
            ParameterModifier.In => "in ",
            _ when modifier.HasFlag(ParameterModifier.Params) => "params ",
            _ when modifier.HasFlag(ParameterModifier.This) => "this ",
            _ => string.Empty,
        };
    }

    private static string GetCallableLookupName(string declaredName)
    {
        var open = declaredName.LastIndexOf('<');
        return open > 0 && declaredName.EndsWith('>')
            ? declaredName[..open]
            : declaredName;
    }

    private static string QualifyTopLevelName(AstNode declaration, string name)
        => declaration.NamespaceIdentity == null
            || string.IsNullOrEmpty(declaration.NamespaceIdentity)
            ? name
            : $"{declaration.NamespaceIdentity}.{name}";

    private static string GetQualifiedTypeName(TypeDefinitionNode type)
    {
        var name = QualifyTopLevelName(type, type.Name);
        var arity = type switch
        {
            ClassDefinitionNode cls => cls.TypeParameters.Count,
            InterfaceDefinitionNode iface => iface.TypeParameters.Count,
            _ => 0
        };
        return TypeIdentity.ToLookupName(name, arity);
    }

    private static string GetTypeNameWithArity(string name, int arity)
        => TypeIdentity.ToLookupName(name, arity);

    private static IReadOnlyList<string> GetCallableTypeParameters(
        string declaredName,
        IEnumerable<string> representedTypeParameters)
    {
        var represented = representedTypeParameters.ToArray();
        if (represented.Length > 0)
            return represented;

        var open = declaredName.LastIndexOf('<');
        if (open <= 0 || !declaredName.EndsWith('>'))
            return Array.Empty<string>();

        return declaredName[(open + 1)..^1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private readonly HashSet<FunctionSymbol> _localFunctionSymbols = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundFunction> _boundLocalFunctions = new();

    /// <summary>0.25 F3 (#847): declares a body's local functions in a scope between the
    /// host and the body scope; returns the body scope's parent.</summary>
    private Scope DeclareLocalFunctions(
        IReadOnlyList<StatementNode> body,
        SymbolId enclosing,
        out List<(FunctionNode Node, FunctionSymbol Symbol)> locals)
    {
        locals = [];
        var scope = _scope;
        foreach (var local in body.OfType<LocalFunctionStatementNode>())
        {
            if (locals.Count == 0)
                scope = _scope.CreateChild();
            var function = local.Function;
            var symbol = CreateFunctionSymbol(
                CreateDeclarationId(enclosing, "localfunction", function.Id,
                    CallableIdentityKey(function.Name, function.Parameters, function.TypeParameters)),
                function.Name,
                function.Output?.TypeName ?? "VOID",
                Array.Empty<string>(),
                function.Parameters,
                function.IdentifierSpan,
                Visibility.Private,
                containingTypeName: null,
                definitionSpan: function.Span,
                output: function.Output,
                isAsync: function.IsAsync);
            _localFunctionSymbols.Add(symbol);
            _functionSymbols[function] = symbol;
            if (!scope.TryDeclareOverload(function.Name, symbol, out var duplicate))
                ReportDuplicateSignature(function.Span, function.Name, symbol, duplicate);
            locals.Add((function, symbol));
        }
        return scope;
    }

    /// <summary>Binds each local body as a static callable blind to the enclosing locals.</summary>
    private void BindLocalFunctions(
        List<(FunctionNode Node, FunctionSymbol Symbol)> locals,
        Scope localFunctionScope,
        SymbolId enclosing)
    {
        foreach (var (function, symbol) in locals)
        {
            var outerFlowNarrowings = _flowNarrowingSources.ToArray();
            var outerCallableState = CaptureCallableState();
            var outerCallableWrites = _deferredCallableWrites;
            _deferredCallableWrites = [];
            _flowNarrowingSources.Clear();
            try
            {
                var bodyScope = localFunctionScope.CreateChild();
                using var _s = PushScope(bodyScope);
                using var _c = PushStaticContext(true);
                using var _identity = PushDeclarationContext(symbol.Id);
                using var _returnCtx = PushReturnTypeContext(symbol.BodyReturnType);
                DeclareParameters(symbol.Parameters, function.Parameters);
                var contracts = BindContracts(function.Preconditions, function.Postconditions, symbol.BodyReturnType);
                var body = BindStatements(function.Body);
                _boundLocalFunctions.Add(new BoundFunction(function.Span, symbol, body, bodyScope,
                    Array.Empty<string>(), BoundMemberKind.LocalFunction, containingTypeName: null)
                {
                    EnclosingSymbolId = enclosing,
                    ContractConditions = contracts
                });
            }
            finally
            {
                _flowNarrowingSources.Clear();
                foreach (var (narrowed, source) in outerFlowNarrowings)
                    _flowNarrowingSources[narrowed] = source;
                RestoreCallableState(outerCallableState);
                _deferredCallableWrites = outerCallableWrites;
            }
        }
    }

    /// <summary>0.25 F3 (#847): C# lookup stops at a visible local function even when
    /// its overload check fails here, so the call still names it.</summary>
    private IReadOnlyList<FunctionSymbol> WithLocalFunctionCandidates(
        string target,
        IReadOnlyList<FunctionSymbol> resolved)
    {
        if (resolved.Count > 0 || target.Contains('.'))
            return resolved;
        var locals = _scope.GetOverloads(target).Where(_localFunctionSymbols.Contains).ToArray();
        return locals.Length > 0 ? locals : resolved;
    }

    private BoundFunction BindFunction(FunctionNode func)
    {
        var localFunctionScope = DeclareLocalFunctions(
            func.Body, _functionSymbols[func].Id, out var localFunctions);
        var functionScope = localFunctionScope.CreateChild();
        using var _ = PushScope(functionScope);
        // v0.17 R2, round-4 finding — a module-level generic function's type
        // parameters are declared on the FUNCTION; without this push
        // GetTypeParameterConstraints fell back to _currentClass, which is null
        // at module scope, so `§F{…}<T> (T:n)` calling an overload that takes
        // T's constraint interface still reported Calor0208.
        using var _tp = PushMemberTypeParameters(func.TypeParameters);
        var functionSymbol = _functionSymbols[func];
        using var _identity = PushDeclarationContext(functionSymbol.Id);
        // v0.14 §S4 — expose the declared return type to BindReturnStatement.
        using var _returnCtx = PushReturnTypeContext(functionSymbol.BodyReturnType);
        var previousNamespaceIdentity = _currentNamespaceIdentity;
        var previousNamespaceScopeId = _currentNamespaceScopeId;
        _currentNamespaceIdentity = func.NamespaceIdentity;
        _currentNamespaceScopeId = func.NamespaceScopeId;

        try
        {
            DeclareParameters(functionSymbol.Parameters, func.Parameters);
            var contracts = BindContracts(func.Preconditions, func.Postconditions, functionSymbol.BodyReturnType);

            // Bind body
            var boundBody = BindStatements(func.Body);
            BindLocalFunctions(localFunctions, localFunctionScope, functionSymbol.Id);

            // Extract declared effects for taint analysis
            var declaredEffects = ExtractEffects(func);

            return new BoundFunction(func.Span, functionSymbol, boundBody, functionScope, declaredEffects)
            {
                ContractConditions = contracts
            };
        }
        finally
        {
            _currentNamespaceIdentity = previousNamespaceIdentity;
            _currentNamespaceScopeId = previousNamespaceScopeId;
        }
    }

    private void DeclareParameters(
        IReadOnlyList<VariableSymbol> symbols,
        IReadOnlyList<ParameterNode> parameters)
    {
        _byReferenceParameters = symbols
            .Where(symbol => (symbol.Modifier & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In)) != 0)
            .ToArray();
        for (var index = 0; index < symbols.Count; index++)
        {
            var symbol = symbols[index];
            // Parameter registration precedes source type registration.
            if (TryBuildMethodInputArrayType(symbol.TypeName, out var array))
                symbol.MethodInputArrayType = array;
            symbol.GenericNullabilityType = TryBuildClosedReferenceGeneric(symbol.TypeName)
                ?? symbol.GenericNullabilityType;
            if (!_scope.TryDeclare(symbol))
            {
                var suggestedName = GenerateUniqueName(symbol.Name);
                _diagnostics.ReportDuplicateDefinitionWithFix(
                    parameters[index].IdentifierSpan,
                    symbol.Name,
                    suggestedName);
            }
        }
    }

    private IReadOnlyList<BoundExpression> BindContracts(
        IReadOnlyList<RequiresNode> preconditions,
        IReadOnlyList<EnsuresNode> postconditions,
        string returnType)
    {
        var previous = _bindingContract;
        _bindingContract = true;
        try
        {
            var conditions = preconditions.Select(precondition => BindExpression(precondition.Condition)).ToList();
            using var scope = PushScope(_scope.CreateChild());
            if (!ReturnShape.IsVoidType(returnType))
                _scope.TryDeclare(CreateLocalVariable("result", returnType, isMutable: false,
                    isParameter: true, ParameterModifier.None, Parsing.TextSpan.Empty, "contract-result",
                    nullableAnnotation: TryReadDeclaredStringAnnotation(returnType)));
            conditions.AddRange(postconditions.Select(postcondition => BindExpression(postcondition.Condition)));
            return conditions;
        }
        finally
        {
            _bindingContract = previous;
        }
    }

    private List<VariableSymbol> BindParameters(IReadOnlyList<ParameterNode> parameters)
    {
        var result = new List<VariableSymbol>(parameters.Count);
        foreach (var parameter in parameters)
        {
            var symbol = CreateLocalVariable(
                parameter.Name,
                parameter.TypeName,
                isMutable: false,
                isParameter: true,
                parameter.Modifier,
                parameter.IdentifierSpan,
                "parameter",
                parameter.DefaultValue,
                // v0.14 nullability workstream (task #3) — mirror the
                // top-level function-parameter site so constructor /
                // operator / indexer / etc. parameters also inherit
                // their declared STRING nullability. Scoped per §D6.
                nullableAnnotation: TryReadDeclaredStringAnnotation(parameter.TypeName),
                // v0.15 E2 slice b — position 4/5's row for the callables that
                // bind their parameters here rather than at registration
                // (operators, indexers). No `eff` binders: neither production
                // has a type-parameter list.
                functionType: TryBuildFunctionType(
                    parameter.TypeName, parameter.Row, binders: null));
            if (!_scope.TryDeclare(symbol))
            {
                var suggestedName = GenerateUniqueName(parameter.Name);
                _diagnostics.ReportDuplicateDefinitionWithFix(
                    parameter.IdentifierSpan,
                    parameter.Name,
                    suggestedName);
            }
            result.Add(symbol);
        }
        return result;
    }

    private IReadOnlyList<BoundStatement> BindStatements(IReadOnlyList<StatementNode> statements)
    {
        var result = new List<BoundStatement>();
        foreach (var stmt in statements)
        {
            if (stmt is ForeachStatementNode or UsingStatementNode)
                InvalidateByReferenceParameterNarrowing();
            var bound = BindStatement(stmt);
            if (bound != null)
            {
                result.Add(bound);
            }
            if (bound is BoundCallStatement || WritesAliasedStorage(stmt, bound))
                InvalidateByReferenceParameterNarrowing();
            if (stmt is IfStatementNode
                {
                    ElseIfClauses.Count: 0,
                    ElseBody: null
                } ifStatement
                && bound is BoundIfStatement boundIf
                && DefinitelyTerminates(ifStatement.ThenBody))
            {
                DeclareFailedConditionPatterns(ifStatement.Condition, boundIf.Condition);
                if (ifStatement.Condition is BinaryOperationNode guard
                    && IsSelectedBuiltInNullGuard(guard)
                    && _sourceCallConversions?.TerminatingNullGuardConditions.Contains(guard) == true)
                    DeclareFailedNullEqualityConditionPatterns(guard, boundIf.Condition);
            }
        }
        return result;
    }

    private static bool DefinitelyTerminates(IReadOnlyList<StatementNode> statements) =>
        statements.LastOrDefault() switch
        {
            ReturnStatementNode or ThrowStatementNode or RethrowStatementNode => true,
            IfStatementNode nested when nested.ElseBody is not null =>
                DefinitelyTerminates(nested.ThenBody)
                && nested.ElseIfClauses.All(clause => DefinitelyTerminates(clause.Body))
                && DefinitelyTerminates(nested.ElseBody),
            _ => false
        };

    private BoundStatement? BindStatement(StatementNode stmt)
    {
        return stmt switch
        {
            CallStatementNode call => BindCallStatement(call),
            ReturnStatementNode ret => BindReturnStatement(ret),
            ForStatementNode forStmt => BindForStatement(forStmt),
            WhileStatementNode whileStmt => BindWhileStatement(whileStmt),
            IfStatementNode ifStmt => BindIfStatement(ifStmt),
            BindStatementNode bind => BindBindStatement(bind),
            BreakStatementNode breakStmt => new BoundBreakStatement(breakStmt.Span),
            ContinueStatementNode continueStmt => new BoundContinueStatement(continueStmt.Span),
            GotoStatementNode gotoStmt => new BoundGotoStatement(gotoStmt.Span, gotoStmt.Label),
            LabelStatementNode labelStmt => new BoundLabelStatement(labelStmt.Span, labelStmt.Label),
            TryStatementNode tryStmt => BindTryStatement(tryStmt),
            MatchStatementNode matchStmt => BindMatchStatement(matchStmt),
            ProofObligationNode proof => BindProofObligation(proof),
            // Class member body statement types
            AssignmentStatementNode assign => BindAssignmentStatement(assign),
            CompoundAssignmentStatementNode compound => BindCompoundAssignment(compound),
            ForeachStatementNode forEach => BindForeachStatement(forEach),
            DictionaryForeachNode deconstruction => BindDeconstructionForeach(deconstruction),
            UsingStatementNode usingStmt => BindUsingStatement(usingStmt),
            ThrowStatementNode throwStmt => new BoundThrowStatement(throwStmt.Span,
                throwStmt.Exception != null ? BindExpression(throwStmt.Exception) : null),
            RethrowStatementNode rethrow => new BoundThrowStatement(rethrow.Span, null),
            DoWhileStatementNode doWhile => BindDoWhileStatement(doWhile),
            ExpressionStatementNode exprStmt => new BoundExpressionStatement(exprStmt.Span, BindExpression(exprStmt.Expression)),
            YieldReturnStatementNode yieldRet => new BoundReturnStatement(yieldRet.Span,
                yieldRet.Expression != null ? BindExpression(yieldRet.Expression) : null),
            YieldBreakStatementNode => new BoundBreakStatement(stmt.Span),
            SyncBlockNode sync => BindSyncBlock(sync),
            PrintStatementNode print => new BoundExpressionStatement(print.Span,
                BindExpression(print.Expression)),
            // Comments have no executable semantics; preserved C# remains opaque.
            FallbackCommentNode => null,
            RawCSharpNode => BindUnsupportedStatement(stmt),
            // 0.25 F3 (#847): bound as its own callable (BindLocalFunctions).
            LocalFunctionStatementNode => null,
            PreprocessorDirectiveNode preprocessor => BindPreprocessorDirective(preprocessor),
            EventSubscribeNode subscribe => BindEventSubscription(subscribe, subscribe.Event, subscribe.Handler, adding: true),
            EventUnsubscribeNode unsubscribe => BindEventSubscription(unsubscribe, unsubscribe.Event, unsubscribe.Handler, adding: false),
            // Unknown — explicit unsupported node, NOT null
            _ => BindUnsupportedStatement(stmt)
        };
    }

    private BoundProofObligation BindProofObligation(ProofObligationNode proof)
    {
        var condition = BindExpression(proof.Condition);
        return new BoundProofObligation(proof.Span, proof.Id, proof.Description, condition);
    }

    private BoundCallStatement BindCallStatement(CallStatementNode call)
    {
        var args = BindCallArguments(call.Arguments, call.ArgumentModifiers);
        var isEventCall = TryGetEventStorage(call, out var eventStorage, out var eventType);
        var receiverSymbol = isEventCall ? eventStorage : ResolveCallReceiver(
            call.Target,
            call.ReceiverSpan ?? call.CalleeSpan);
        var receiverTypeSymbol = receiverSymbol == null
            ? ResolveCallReceiverType(call.Target)
            : null;
        var resolution = isEventCall ? OverloadResolutionResult.NotFound() : ResolveCall(
            call.Span,
            call.Target,
            args,
            call.ArgumentNames,
            call.ArgumentModifiers,
            call.TypeArguments,
            allowNullableStringCompatibility: true);
        var bclResolution = !isEventCall && resolution.Kind == OverloadResolutionKind.NotFound && !resolution.HasOpaqueOverloads
            ? TryResolveBclCall(call.Target, args, call.ArgumentNames, call.ArgumentModifiers)
            : null;
        ValidateCallArguments(args, resolution, bclResolution);
        ValidateDirectFunctionArguments(call.Target, receiverSymbol, args, resolution, bclResolution);
        InvalidateMutatingCallArguments(args, call.ArgumentModifiers, resolution, bclResolution);
        var callable = isEventCall ? eventStorage.StorageIdentity : ResolveCallableStorage(call.Target, receiverSymbol);
        var callableEffects = InvalidateDeferredCallTarget(callable);
        var (resolvedTypeName, resolvedMethodName) = GetResolvedCallIdentity(
            call.Target,
            receiverSymbol,
            receiverTypeSymbol);
        var receiver = isEventCall
            ? new BoundVariableExpression(call.CalleeSpan, eventStorage, typeOverride: eventType)
            : BuildCallReceiver(
            call.Target,
            call.ReceiverSpan ?? call.CalleeSpan,
            receiverSymbol,
            receiverTypeSymbol,
            resolvedTypeName);

        if (resolution.Kind == OverloadResolutionKind.NotFound && !resolution.HasOpaqueOverloads
            && receiver is not null)
            ValidateStringInstanceReceiver(receiver, TryResolveBclCall(
                $"{receiver.Type.FullyQualifiedName}.{resolvedMethodName}", args,
                call.ArgumentNames, call.ArgumentModifiers, receiver, call.TypeArguments)?.Symbol);

        var boundCall = new BoundCallStatement(
            call.Span,
            call.Target,
            args,
            resolution.Function,
            call.ArgumentNames,
            call.ArgumentModifiers,
            receiverSymbol,
            resolution.Kind == OverloadResolutionKind.UnresolvedArguments ? []
                : WithLocalFunctionCandidates(call.Target, resolution.Functions),
            call.CalleeSpan,
            call.ReceiverSpan,
            resolution.Kind == OverloadResolutionKind.Inaccessible,
            call.TypeArguments,
            receiverTypeSymbol,
            resolvedTypeName,
            resolvedMethodName,
            (isEventCall ? eventType.ParameterTypes.Select(type => type.DisplayString)
                : bclResolution?.Symbol?.Parameters.Select(parameter => parameter.Type.ToDisplayString(BclParameterIdentityFormat))
                ?? resolution.Function?.Parameters
                .Select(parameter => parameter.TypeName)
                ?? args.Select(argument => argument.Type.DisplayString))
                .ToArray(),
            receiver)
        {
            CallableStorageIdentity = callable,
            ObservedCallableEffects = callableEffects,
            ArgumentParameterIndices = GetBclArgumentParameterIndices(bclResolution, args.Count),
            SelectedOverloadMatches = resolution.Matches,
            HasOpaqueOverloads = resolution.HasOpaqueOverloads,
            HasUnresolvedArguments = resolution.Kind == OverloadResolutionKind.UnresolvedArguments
        };
        return boundCall;
    }

    private BoundReturnStatement BindReturnStatement(ReturnStatementNode ret)
    {
        var expr = ret.Expression is LambdaExpressionNode { IsAsync: true } asyncLambda
            ? BindExpression(asyncLambda, TryBuildFunctionType(_currentFunctionReturnType, null, null))
            : ret.Expression != null ? BindExpression(ret.Expression) : null;
        if (expr is not null && _currentFunctionReturnType is not null)
            CheckMetadataArrayToCollection(expr, _currentFunctionReturnType, ret.Span, "Return");

        // v0.14 §S4 nullability check (issue #875, D2 predicate). Return-site
        // sibling of the §S3b BindBindStatement check. Fires when the current
        // function's declared return type is a non-nullable :string / :str
        // and the returned expression's BoundType.NullableAnnotation is
        // Annotated or Oblivious (per D3, Oblivious is treated conservatively
        // as possibly-null). Same TryBuildStringTarget filter — only scalar
        // STRING return types are in-scope for S3 (per D6); non-string
        // returns silently pass. Current semantics produces binder Errors;
        // BindingDiagnosticPolicy keeps these analysis-only. Unsupported
        // major versions are rejected before binding, not compiled in Info mode.
        if (expr != null
            && _currentFunctionReturnType != null
            && TryBuildStringTarget(_currentFunctionReturnType, out var stringTarget)
            && !IsObliviousFlowInObliviousModule(expr)
            && NullabilityChecker.IsPossiblyNullAssignedTo(expr, stringTarget!))
        {
            var (targetShapeLabel, fixHintTargetLabel) = DescribeStringTargetShape(stringTarget!);
            _diagnostics.Report(
                expr.Span,
                DiagnosticCode.NullableReturnFromNonNullable,
                DescribeClosedGenericReceiving(expr, stringTarget!) ??
                $"Return declares non-nullable {targetShapeLabel} but the returned value may be null " +
                $"(source annotation: '{DescribeAnnotation(expr)}'). " +
                $"Change the return type to {fixHintTargetLabel} or add an explicit non-null check at the interop boundary.",
                SemanticsVersion.NullabilitySeverityFor(),
                BindingDiagnosticContext.For(BindingReceivingBoundary.NativeReturn, stringTarget!));
        }

        return new BoundReturnStatement(ret.Span, expr);
    }

    private BoundForStatement BindForStatement(ForStatementNode forStmt)
    {
        using var _ = PushScope(_scope.CreateChild());
        var from = BindExpression(forStmt.From);
        var snapshotTo = !forStmt.ReevaluatesCondition ? BindExpression(forStmt.To) : null;
        var step = !forStmt.ReevaluatesCondition && forStmt.Step != null ? BindExpression(forStmt.Step) : null;

        // Declare loop variable
        var loopVar = CreateLocalVariable(
            forStmt.VariableName,
            forStmt.VariableType,
            isMutable: true,
            isParameter: false,
            ParameterModifier.None,
            forStmt.VariableSpan,
            "for");
        loopVar.IsLoopVariable = !forStmt.ReevaluatesCondition;
        if (!_scope.TryDeclare(loopVar))
        {
            _diagnostics.ReportError(forStmt.Span, DiagnosticCode.DuplicateDefinition,
                $"Variable '{forStmt.VariableName}' is already defined");
        }

        var condition = forStmt.Condition != null ? BindExpression(forStmt.Condition) : null;
        var to = snapshotTo ?? BindExpression(forStmt.To);
        if (forStmt.Condition != null && condition != null)
            DeclareSuccessfulConditionPatterns(forStmt.Condition, condition);
        var update = forStmt.Update != null ? BindStatement(forStmt.Update) : null;
        PrepareLoopCallableState(forStmt.Body);
        var headerCallables = CaptureCallableState();
        var body = BindStatements(forStmt.Body);
        JoinCallableState(headerCallables);

        return new BoundForStatement(forStmt.Span, loopVar, from, to, step, body, condition, update);
    }

    private BoundWhileStatement BindWhileStatement(WhileStatementNode whileStmt)
    {
        using var _ = PushScope(_scope.CreateChild());

        PrepareLoopCallableState(whileStmt.Body, whileStmt.Condition);
        var headerCallables = CaptureCallableState();
        var condition = BindExpression(whileStmt.Condition);
        DeclareSuccessfulConditionPatterns(whileStmt.Condition, condition);
        var body = BindStatements(whileStmt.Body);
        JoinCallableState(headerCallables);

        return new BoundWhileStatement(whileStmt.Span, condition, body);
    }

    private void PrepareLoopCallableState(
        IReadOnlyList<StatementNode> statements, ExpressionNode? condition = null,
        bool conditionAfterBody = false)
    {
        if (_isDiscoveringLoopCallables)
            return;

        var bindings = _scope.CaptureVisibleVariableBindings();
        var flow = CaptureFlowNarrowingState();
        var diagnostics = _diagnostics.CreateCheckpoint();
        // Discovery only adds symbols and bumps declaration counters, so it is
        // undone from the symbols appended after this mark and from a journal.
        // Copying both module-wide maps here made binding quadratic in module
        // size (one full copy per loop).
        var symbolCount = _symbolOrder.Count;
        var declarationJournal = new List<(SymbolId Key, int Previous)>();
        _declarationOccurrenceJournal = declarationJournal;
        var expressionsBound = ExpressionsBound;
        var misplacedRows = _misplacedRowsReported.ToArray();
        var unsupportedNodeTypes = _unsupportedNodeTypes.ToArray();
        var enclosingWrites = _deferredCallableWrites;
        _deferredCallableWrites = [];
        _isDiscoveringLoopCallables = true;
        CallableMutationSummary summary;
        HashSet<string> byReferenceWrites = new(StringComparer.Ordinal);
        var memberReceiverWrites = new List<BoundExpression>();
        var aliasInvalidationsBefore = _aliasInvalidations;
        var loopMayWriteAliases = false;
        try
        {
            // Discover callable writes without committing diagnostics, symbols,
            // or ordinary narrowing from this preliminary iteration.
            using var _ = PushScope(_scope.CreateChild());
            var boundCondition = condition is null || conditionAfterBody ? null : BindExpression(condition);
            if (condition is not null && boundCondition is not null)
                DeclareSuccessfulConditionPatterns(condition, boundCondition);
            var body = BindStatements(statements);
            if (conditionAfterBody && condition is not null)
                boundCondition = BindExpression(condition);
            // #943 (D1 RO-NULL-2): a ref/out write or an aliased-storage change later in the body
            // reaches the next iteration's reads, so it must end narrowings before the body binds.
            foreach (var node in body.SelectMany(DescendantsAndSelf).Concat(boundCondition is null ? [] : DescendantsAndSelf(boundCondition)))
            {
                if (node is BoundCallExpression { ArgumentModifiers: { } expressionModifiers, Arguments: var expressionArguments })
                    byReferenceWrites.UnionWith(ByReferenceWrittenNames(expressionModifiers, expressionArguments));
                else if (node is BoundCallStatement { ArgumentModifiers: { } statementModifiers, Arguments: var statementArguments })
                    byReferenceWrites.UnionWith(ByReferenceWrittenNames(statementModifiers, statementArguments));
                if (node.DirectWriteTarget is { } writtenTarget)
                    memberReceiverWrites.Add(writtenTarget);
            }
            // Every aliased-storage change the body binds (calls, stores, using, foreach, events) was
            // seen by the statement and expression hooks during this discovery pass.
            loopMayWriteAliases = _aliasInvalidations != aliasInvalidationsBefore;
            summary = new CallableMutationSummary
            {
                Mutations = GetDeferredMutations(boundCondition, body)
                    .Select(variable => variable.StorageIdentity)
                    .Where(variable => variable is
                        { IsField: false, IsProperty: false, MethodInputArrayType: not null })
                    .Distinct<VariableSymbol>(ReferenceEqualityComparer.Instance)
                    .ToArray(),
                Alternatives = GetDeferredCallEffects(boundCondition, body).ToArray(),
                Writes = _deferredCallableWrites.ToArray()
            };
        }
        finally
        {
            Scope.RestoreVisibleVariableBindings(bindings);
            RestoreFlowNarrowingState(flow);
            _diagnostics.RestoreCheckpoint(diagnostics);
            for (var index = _symbolOrder.Count - 1; index >= symbolCount; index--)
                if (_symbolsById.Remove(_symbolOrder[index], out var removed) && removed is TypeSymbol)
                    _sourceTypeSymbols = null;
            _symbolOrder.RemoveRange(symbolCount, _symbolOrder.Count - symbolCount);
            _declarationOccurrenceJournal = null;
            for (var index = declarationJournal.Count - 1; index >= 0; index--)
            {
                var (key, previous) = declarationJournal[index];
                if (previous == 0)
                    _declarationIdOccurrences.Remove(key);
                else
                    _declarationIdOccurrences[key] = previous;
            }
            ExpressionsBound = expressionsBound;
            _misplacedRowsReported.Clear();
            _misplacedRowsReported.UnionWith(misplacedRows);
            _unsupportedNodeTypes.Clear();
            _unsupportedNodeTypes.UnionWith(unsupportedNodeTypes);
            _deferredCallableWrites = enclosingWrites;
            _isDiscoveringLoopCallables = false;
        }

        // The weak-write fixed point includes zero executions and targets
        // installed on a preceding iteration. Bind receiving sites only after
        // this state is available; fresh guards in the body still narrow normally.
        ApplyCallableMutationSummary(summary);
        foreach (var target in memberReceiverWrites)
            InvalidateMemberFlowNarrowing(target);
        foreach (var name in byReferenceWrites)
        {
            if (_scope.Lookup(name) is VariableSymbol visible)
                InvalidateMutableFlowNarrowing(visible, requireRebindable: false);
        }
        if (loopMayWriteAliases)
            InvalidateByReferenceParameterNarrowing();
    }

    private void JoinCallableState(IReadOnlyDictionary<VariableSymbol, CallableMutationSummary> other)
    {
        foreach (var (storage, effects) in other)
            StoreCallableState(storage, UnionCallableEffects(GetCallableState(storage), effects));
    }

    private BoundIfStatement BindIfStatement(IfStatementNode ifStmt)
    {
        var condition = BindExpression(ifStmt.Condition);
        var baseline = CaptureFlowNarrowingState();
        var branches = new List<FlowNarrowingState>();
        var fallthrough = baseline;

        var thenBody = BindIsolatedFlowBranch(
            baseline,
            branches,
            () => DeclareSuccessfulConditionPatterns(ifStmt.Condition, condition),
            () => BindStatements(ifStmt.ThenBody));

        var elseIfClauses = new List<BoundElseIfClause>();
        foreach (var elseIf in ifStmt.ElseIfClauses)
        {
            RestoreFlowNarrowingState(fallthrough);
            var elseIfCondition = BindExpression(elseIf.Condition);
            // Evaluating a false condition still executes its calls. Only the
            // body is conditional on its result, not the condition's effects.
            fallthrough = CaptureFlowNarrowingState();
            var elseIfBody = BindIsolatedFlowBranch(
                fallthrough,
                branches,
                () => DeclareSuccessfulConditionPatterns(elseIf.Condition, elseIfCondition),
                () => BindStatements(elseIf.Body));
            elseIfClauses.Add(new BoundElseIfClause(
                elseIf.Span,
                elseIfCondition,
                elseIfBody));
        }

        IReadOnlyList<BoundStatement>? elseBody = null;
        if (ifStmt.ElseBody != null)
        {
            elseBody = BindIsolatedFlowBranch(
                fallthrough,
                branches,
                () => DeclareFailedConditionPatterns(
                    ifStmt.ElseIfClauses.LastOrDefault()?.Condition ?? ifStmt.Condition,
                    elseIfClauses.LastOrDefault()?.Condition ?? condition),
                () => BindStatements(ifStmt.ElseBody));
        }

        if (ifStmt.ElseBody is null)
            branches.Add(fallthrough);
        JoinFlowBranchStates(baseline, branches);

        return new BoundIfStatement(ifStmt.Span, condition, thenBody, elseIfClauses, elseBody);
    }

    private FlowNarrowingState CaptureFlowNarrowingState()
    {
        var sources = new Dictionary<VariableSymbol, VariableSymbol>(_flowNarrowingSources);
        var visible = sources.Keys
            .Where(variable => ReferenceEquals(_scope.Lookup(variable.Name), variable))
            .ToArray();
        return new FlowNarrowingState(
            sources, visible, CaptureCallableState(), _scope.CaptureVisibleVariableBindings());
    }

    private IReadOnlyList<BoundStatement> BindIsolatedFlowBranch(
        FlowNarrowingState baseline,
        List<FlowNarrowingState> branches,
        Action? setup,
        Func<IReadOnlyList<BoundStatement>> bind)
    {
        RestoreFlowNarrowingState(baseline);
        try
        {
            using var _ = PushScope(_scope.CreateChild());
            foreach (var narrowed in baseline.VisibleNarrowings)
                _scope.TryDeclare(narrowed);
            setup?.Invoke();
            return bind();
        }
        finally
        {
            branches.Add(CaptureFlowNarrowingState());
            RestoreFlowNarrowingState(baseline);
        }
    }

    private void JoinFlowBranchStates(
        FlowNarrowingState baseline, IReadOnlyList<FlowNarrowingState> branches)
    {
        RestoreFlowNarrowingState(baseline);
        if (branches.Count == 0)
            return;
        _callableState.Clear();
        foreach (var branch in branches)
            JoinCallableState(branch.Callables);
        // Restored ancestor bindings can be hidden by a local name while a
        // previously declared callable still mutates their captured storage.
        foreach (var narrowed in baseline.Bindings.Select(binding => binding.Symbol)
                     .Where(baseline.Sources.ContainsKey)
                     .Distinct<VariableSymbol>(ReferenceEqualityComparer.Instance))
        {
            if (branches.Any(branch => !branch.Sources.ContainsKey(narrowed)))
                InvalidateMutableFlowNarrowing(narrowed, requireRebindable: false);
        }
    }

    private void RestoreFlowNarrowingState(FlowNarrowingState state)
    {
        Scope.RestoreVisibleVariableBindings(state.Bindings);
        RestoreCallableState(state.Callables);
        _flowNarrowingSources.Clear();
        foreach (var (narrowed, source) in state.Sources)
            _flowNarrowingSources.Add(narrowed, source);
    }

    private sealed record FlowNarrowingState(
        IReadOnlyDictionary<VariableSymbol, VariableSymbol> Sources,
        IReadOnlyList<VariableSymbol> VisibleNarrowings,
        IReadOnlyDictionary<VariableSymbol, CallableMutationSummary> Callables,
        IReadOnlyList<(Scope Scope, string Name, VariableSymbol Symbol)> Bindings);

    private BoundStatement BindBindStatement(BindStatementNode bind)
    {
        BoundExpression? initializer = null;
        string typeName;

        if (bind.Initializer != null)
        {
            initializer = bind.Initializer is LambdaExpressionNode { IsAsync: true } asyncLambda
                ? BindExpression(asyncLambda, TryBuildFunctionType(bind.TypeName, null, null))
                : BindExpression(bind.Initializer);
            // Infer type from initializer if not specified
            typeName = bind.TypeName ?? initializer.Type.DisplayString;
            if (bind.TypeName is not null)
                CheckMetadataArrayToCollection(initializer, bind.TypeName, bind.Span, $"Binding '{bind.Name}'");
        }
        else if (bind.TypeName != null)
        {
            typeName = bind.TypeName;
        }
        else
        {
            // A binding with neither a type nor initializer previously
            // silently defaulted to INT (latent bug). Per RFC v0.6 bind
            // inference formalization §3.2, this is now Calor0250.
            // Fall back to "INT" so subsequent binding still produces a
            // usable symbol and we don't cascade NREs through the bound tree.
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindRequiresTypeOrInitializer,
                $"Binding '{bind.Name}' has no type annotation and no initializer. " +
                "Add either a type before the name (e.g. 'int:name') " +
                "or an initializer expression so the binder can infer the type.");
            typeName = "INT";
        }

        var rebindTarget = bind.IsMutable ? FindRebindTarget(bind.Name) : null;
        if (rebindTarget != null)
        {
            rebindTarget = InvalidateMutableFlowNarrowing(rebindTarget,
                mayWriteNull: initializer == null || MayIntroduceNullInObliviousModule(initializer));
            if (rebindTarget.StorageIdentity.IsLoopVariable)
                ReportWriteToReadOnlyLocal(rebindTarget, bind.IdentifierSpan, "rebind {0}");
            if (!CanRebind(rebindTarget))
            {
                _diagnostics.ReportError(
                    bind.IdentifierSpan,
                    DiagnosticCode.BindReassignsImmutable,
                    $"Binding '{bind.Name}' is immutable and cannot be rebound. " +
                    "Declare it mutable at its original declaration or use a new name.");

                var recoveryVariable = CreateLocalVariable(
                    bind.Name,
                    typeName,
                    isMutable: true,
                    isParameter: false,
                    ParameterModifier.None,
                    bind.IdentifierSpan,
                    "invalid-rebind");
                return new BoundBindStatement(bind.Span, recoveryVariable, initializer);
            }

            var annotatedType = bind.TypeName == null
                ? null
                : TypeIdentity.Canonicalize(bind.TypeName);
            var valueType = initializer?.Type.DisplayString;
            if ((annotatedType != null
                    && !AreAssignmentCompatible(rebindTarget.TypeName, annotatedType))
                || (valueType != null
                    && !AreAssignmentCompatible(rebindTarget.TypeName, valueType)))
            {
                _diagnostics.ReportError(
                    bind.IdentifierSpan,
                    DiagnosticCode.BindRebindTypeMismatch,
                    $"Mutable binding '{bind.Name}' has type '{AttributeHelper.ToSurfaceSpelling(rebindTarget.TypeName)}' and cannot be " +
                    $"rebound with '{AttributeHelper.ToSurfaceSpelling(bind.TypeName ?? valueType!)}'.");
            }

            if (initializer != null)
            {
                SetCallableState(rebindTarget, initializer);
                return new BoundAssignmentStatement(
                    bind.Span,
                    new BoundVariableExpression(bind.IdentifierSpan, rebindTarget),
                    initializer);
            }

            _diagnostics.ReportError(
                bind.IdentifierSpan,
                DiagnosticCode.BindRequiresTypeOrInitializer,
                $"Mutable rebind '{bind.Name}' requires an initializer value.");
            return new BoundBindStatement(bind.Span, rebindTarget, initializer);
        }

        // Explicit declarations win. Inferred references carry the producer's
        // resolved identity as well as its annotation; unknown expressions do not
        // acquire a non-null guarantee merely from an inferred type spelling.
        var inferredReferenceType = bind.TypeName is null
            ? GetInferredReferenceType(initializer)
            : null;
        var declaredAnnotation = TryReadDeclaredStringAnnotation(bind.TypeName)
            is var explicitAnnotation && explicitAnnotation != BoundTypes.NullableAnnotation.Oblivious
                ? explicitAnnotation
                : inferredReferenceType?.NullableAnnotation
                    ?? InferAnnotationForStringBinding(bind.TypeName, typeName, initializer);

        // v0.15 E2 slice b — position 7's row (§3.3), and §3.5's asymmetry:
        //   * a row that is WRITTEN is the binding's row (Calor0405 when the
        //     binding is not function-typed — this is the one row position that
        //     CheckEffectRowPlacement cannot reach, because a §B nests
        //     arbitrarily deep inside a body and the binder already visits every
        //     one of them here);
        //   * an OMITTED row on a binding WITH an initializer is INFERRED from
        //     the initializer's row, not Unknown. That is what keeps the common
        //     `§B{f} §LAM …` shape silent, which §5 needs;
        //   * an omitted row with NO initializer stays Unknown (no
        //     FunctionBoundType, nothing to infer from, and defaulting to pure
        //     would re-open the hole PR #968 closed).
        var bindFunctionType = BindBindingRow(bind, typeName, initializer);

        var variable = CreateLocalVariable(
            bind.Name,
            typeName,
            bind.IsMutable,
            isParameter: false,
            ParameterModifier.None,
            bind.IdentifierSpan,
            "local",
            nullableAnnotation: declaredAnnotation,
            functionType: bindFunctionType,
            inferredReferenceType: inferredReferenceType,
            isTypeInferred: bind.TypeName is null,
            methodInputArrayType: GetLocalMethodInputArrayType(bind.TypeName, initializer),
            genericNullabilityType: bind.TypeName is null ? initializer?.GenericNullabilityType : null,
            hasUnknownInferredType: bind.TypeName is null && initializer?.HasUnknownInferredType == true);
        variable.IsImmutableBinding = !bind.IsMutable && !bind.IsCollectionStatementForm; // #1550
        variable.MetadataArrayResultType = initializer?.MetadataArrayResultType;
        variable.AnonymousObjectMembers = bind.TypeName is null ? initializer?.AnonymousObjectMembers : null;

        if (!_scope.TryDeclare(variable))
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.DuplicateDefinition,
                $"Variable '{bind.Name}' is already defined");
        }
        if (initializer is not null)
            SetCallableState(variable, initializer);

        if (variable.MethodInputArrayType is
                { NullableAnnotation: BoundTypes.NullableAnnotation.Annotated or BoundTypes.NullableAnnotation.Oblivious } declaredArray
            && initializer is { HasUnknownInferredType: false }
            && NullabilityChecker.GetMethodInputArrayType(initializer) is
                { NullableAnnotation: BoundTypes.NullableAnnotation.NotAnnotated } initializedArray
            && initializedArray.Rank == declaredArray.Rank
            && (initializer is not BoundCallExpression call
                || !call.HasUnresolvedArguments && !call.HasOpaqueOverloads && !call.IsInaccessibleCall
                    && (call.ResolvedMetadataMethod is not null || call.SelectedOverloadMatches.Count == 1)))
        {
            // The declaration keeps its annotations; only the current container value is non-null.
            DeclareNarrowedNonNullVariable(new ReferenceNode(bind.IdentifierSpan, bind.Name), variable);
        }

        // v0.14 §S3 nullability check (issue #875, D2 predicate). Fires when
        // an explicit :string / :str target is initialized with a value whose
        // BoundType.NullableAnnotation is Annotated or Oblivious (per D3,
        // Oblivious is treated conservatively as possibly-null).
        //
        // S3b wired MetadataBinder into BindCallExpression so BCL-shaped calls
        // now carry real annotations on their BoundCallExpression.Type.
        // Current semantics produces binder Errors, kept analysis-only by
        // BindingDiagnosticPolicy. Unsupported majors do not compile in Info mode.
        if (initializer != null
            && bind.TypeName != null
            && TryBuildStringTarget(bind.TypeName, out var stringTarget)
            && !IsObliviousFlowInObliviousModule(initializer)
            && NullabilityChecker.IsPossiblyNullAssignedTo(initializer, stringTarget!))
        {
            var (targetShapeLabel, fixHintTargetLabel) = DescribeStringTargetShape(stringTarget!);
            _diagnostics.Report(
                initializer.Span,
                DiagnosticCode.NullableToNonNullableBinding,
                DescribeClosedGenericReceiving(initializer, stringTarget!) ??
                $"Binding '{bind.Name}' declares non-nullable {targetShapeLabel} but its initializer " +
                $"may be null (source annotation: '{DescribeAnnotation(initializer)}'). " +
                $"Change the target to {fixHintTargetLabel} or add an explicit non-null check at the interop boundary.",
                SemanticsVersion.NullabilitySeverityFor(),
                BindingDiagnosticContext.For(BindingReceivingBoundary.Initializer, stringTarget!));
        }

        if (initializer != null && MayIntroduceNullInObliviousModule(initializer))
            _obliviousNullTaintedStorage.Add(variable.StorageIdentity);
        return new BoundBindStatement(bind.Span, variable, initializer);
    }

    /// <summary>
    /// §3.5 — the function type a <c>§B</c> denotes, with its row. Written row
    /// wins; otherwise the initializer's row is INHERITED when the initializer
    /// is itself function-typed (the <c>§B{f} §LAM …</c> shape); otherwise
    /// <c>null</c>, which is Unknown.
    /// </summary>
    private BoundTypes.FunctionBoundType? BindBindingRow(
        BindStatementNode bind,
        string typeName,
        BoundExpression? initializer)
    {
        if (bind.Row != null)
        {
            // The declared type when there is one; otherwise the inferred type
            // name, so `§B{f} §E{cw} §LAM …` is judged on what it actually is.
            var subjectType = bind.TypeName ?? typeName;
            if (!IsFunctionTypedSpelling(subjectType)
                && initializer?.Type is not BoundTypes.FunctionBoundType)
            {
                // F2: same fail-open rule as CheckRowPosition. A §B of an unknown
                // nominal type keeps its row rather than drawing a hard error.
                if (!IsProvablyNonFunctionBindingType(subjectType))
                    return TryBuildFunctionType(subjectType, bind.Row, binders: null);

                ReportRowOnNonFunctionType(bind.Row, subjectType, bind.Name, "binding");
                return null;
            }

            var declared = BindRow(bind.Row, binders: null);
            return initializer?.Type is BoundTypes.FunctionBoundType initializerType
                ? new BoundTypes.FunctionBoundType(
                    initializerType.ParameterTypes,
                    initializerType.ReturnType,
                    displayOverride: initializerType.DisplayString,
                    row: declared,
                    parameterRows: initializerType.ParameterRows)
                : BuildFunctionType(subjectType, declared);
        }

        // Omitted row, function-typed initializer: inherit the initializer's
        // row verbatim — including its Unknown, when that is what it is.
        if (initializer?.Type is BoundTypes.FunctionBoundType inherited)
            return inherited;

        // v0.15 E3 slice a, §8.2 — a function-typed §B with neither a row of its
        // own nor a function-typed initializer still HAS a function type; its row
        // is simply Unknown. Slice b left this null.
        return TryBuildFunctionType(bind.TypeName ?? typeName, row: null, binders: null);
    }

    private BoundTypes.ArrayBoundType? GetLocalMethodInputArrayType(
        string? declaredType, BoundExpression? initializer)
    {
        if (declaredType is null)
            return initializer is not null ? NullabilityChecker.GetMethodInputArrayType(initializer) : null;
        if (!TryBuildMethodInputArrayType(declaredType, out var declared))
            return null;
        if (initializer is BoundArrayCreation or BoundMultiDimArrayCreation
            && NullabilityChecker.GetMethodInputArrayType(initializer) is { } created
            && created.Rank == declared!.Rank
            && declared.ElementType is BoundTypes.NominalBoundType receiving
            && created.ElementType is BoundTypes.NominalBoundType supplied
            && (receiving.HasSameUnderlyingReferenceType(supplied)
                || TypeIdentity.Canonicalize(receiving.QualifiedName) == "STRING"
                    && TypeIdentity.Canonicalize(supplied.QualifiedName) == "STRING"))
        {
            var annotation = receiving.NullableAnnotation == BoundTypes.NullableAnnotation.Annotated
                || supplied.NullableAnnotation == BoundTypes.NullableAnnotation.NotAnnotated
                ? receiving.NullableAnnotation : supplied.NullableAnnotation;
            var element = new BoundTypes.NominalBoundType(
                receiving.QualifiedName, annotation, receiving.Declaration, receiving.RoslynSymbol);
            return new BoundTypes.ArrayBoundType(element, declared.Rank, declared.NullableAnnotation);
        }
        return declared;
    }

    private bool TryBuildMethodArgumentTarget(
        string declaredType,
        out BoundTypes.BoundType? target,
        FunctionSymbol declaringFunction)
    {
        if (TryBuildMethodInputArrayType(declaredType, out var array, declaringFunction))
        {
            target = array;
            return true;
        }
        return TryBuildStringTarget(declaredType, out target, declaringFunction);
    }

    private bool TryBuildMethodInputArrayType(
        string declaredType,
        out BoundTypes.ArrayBoundType? array,
        FunctionSymbol? declaringFunction = null,
        string? declaringTypeName = null)
    {
        array = null;
        if (!TrySplitArrayTypeSpelling(declaredType, out var elementName, out var rank, out var annotation,
                _declaredReferenceAnnotation))
            return false;

        BoundTypes.BoundType element;
        if (TryBuildMethodInputArrayType(elementName, out var nested, declaringFunction, declaringTypeName))
            element = nested!;
        else if (IsBuiltInValueTypeName(TypeIdentity.Canonicalize(elementName))
            || IsBuiltInValueTypeName(elementName))
            element = new BoundTypes.PrimitiveBoundType(
                IsBuiltInValueTypeName(TypeIdentity.Canonicalize(elementName))
                    ? TypeIdentity.Canonicalize(elementName) : elementName,
                bits: null, signed: null, displayOverride: elementName);
        else if (TryBuildDeclaredMetadataArrayElement(elementName, declaringFunction, declaringTypeName) is { } metadataElement)
            element = metadataElement;
        else if (_declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious
            && TypeIdentity.TrySplitGeneric(elementName.TrimEnd('?'), out _, out _))
            element = BuildDeclaredBoundType(elementName);
        else if ((declaringFunction is not null
                ? TryBuildCallableReference(elementName, declaringFunction)
                : TryBuildReferenceType(elementName, declaringTypeName ?? _currentClassName,
                    declaringTypeName is not null && _classesByQualifiedName.TryGetValue(declaringTypeName, out var owner)
                        ? owner.NamespaceIdentity : _currentNamespaceIdentity,
                    _currentMemberTypeParameters?.Select(parameter => parameter.Name)))
            is { } reference)
            element = reference;
        else
            element = new BoundTypes.UnresolvedBoundType($"Unsupported declared array element '{elementName}'");

        array = new BoundTypes.ArrayBoundType(element, rank, annotation);
        return true;
    }

    private BoundTypes.BoundType? TryBuildDeclaredMetadataArrayElement(
        string elementName, FunctionSymbol? function, string? declaringTypeName)
    {
        if (!TypeIdentity.TrySplitGeneric(elementName.TrimEnd('?'), out _, out _)
            || GetOrCreateMetadataBinder() is not { } metadata)
            return null;
        var containingType = function is not null ? function.ContainingTypeName : declaringTypeName ?? _currentClassName;
        var scopeId = _currentNamespaceScopeId;
        var namespaceIdentity = _currentNamespaceIdentity;
        var parameters = function is null
            ? (_currentMemberTypeParameters ?? []).Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal)
            : function.TypeParameters.ToHashSet(StringComparer.Ordinal);
        if (function is not null)
        {
            _functionNamespaces.TryGetValue(function.Id, out namespaceIdentity);
            scopeId = _functionSymbols.FirstOrDefault(pair => pair.Value == function).Key?.NamespaceScopeId;
        }
        if (containingType is not null && _classesByQualifiedName.TryGetValue(containingType, out var owner))
        {
            scopeId = owner.NamespaceScopeId;
            namespaceIdentity = owner.NamespaceIdentity;
            parameters.UnionWith(ReturnShape.GetCallableTypeParameters(owner, _asyncTypeModule).Select(parameter => parameter.Name));
        }
        return ResolveComputedMetadataType(metadata.Context, elementName, scopeId,
                namespaceIdentity, containingType, parameters)
            is Microsoft.CodeAnalysis.INamedTypeSymbol { IsReferenceType: true, IsUnboundGenericType: false } selected
            ? Metadata.MetadataBinderResult.ToBoundType(selected.WithNullableAnnotation(
                elementName.EndsWith('?') ? Microsoft.CodeAnalysis.NullableAnnotation.Annotated
                : _declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious
                    ? Microsoft.CodeAnalysis.NullableAnnotation.None : Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated))
            : null;
    }

    // Lexical decomposition only. Bound shapes are built from declarations, never
    // from a bound display; the legacy resolver also uses the pieces for built-ins.
    private static bool TrySplitArrayTypeSpelling(
        string spelling,
        out string element,
        out int rank,
        out BoundTypes.NullableAnnotation annotation,
        BoundTypes.NullableAnnotation defaultAnnotation = BoundTypes.NullableAnnotation.NotAnnotated)
    {
        var type = spelling.Trim();
        annotation = defaultAnnotation;
        var wholeAnnotation = type.StartsWith('?') || type.EndsWith('?')
            || type.StartsWith("NULLABLE[inner=", StringComparison.OrdinalIgnoreCase)
                && IsWholeBracketedType(type, type.IndexOf('['));
        if (wholeAnnotation && Parsing.AttributeHelper.TryUnwrapNullableAnnotation(type, out var referent))
        {
            type = referent.Trim();
            annotation = BoundTypes.NullableAnnotation.Annotated;
        }
        rank = 1;
        element = string.Empty;
        const string expanded = "ARRAY[element=";
        if (TypeIdentity.TrySplitNativeArray(type, out element, out rank))
            return true;
        rank = 1;
        if (type.EndsWith(']') && type.LastIndexOf('[') is var open && open > 0
            && type[(open + 1)..^1].All(character => character == ','))
        {
            rank = type.Length - open - 1;
            element = type[..open].Trim();
        }
        else if (type.StartsWith(expanded, StringComparison.Ordinal)
            && IsWholeBracketedType(type, type.IndexOf('[')))
            element = type[expanded.Length..^1].Trim();
        else if (type.StartsWith('[') && IsWholeBracketedType(type, 0))
            element = type[1..^1].Trim();
        return element.Length > 0;
    }

    private static bool IsWholeBracketedType(string type, int open)
    {
        var depth = 0;
        for (var index = open; index < type.Length; index++)
        {
            if (type[index] == '[') depth++;
            else if (type[index] == ']' && --depth == 0) return index == type.Length - 1;
        }
        return false;
    }

    private bool HaveSameMethodInputArrayReferent(string parameterType, string argumentType)
    {
        if (!TryBuildMethodInputArrayType(parameterType, out var parameter)
            || !TryBuildMethodInputArrayType(argumentType, out var argument))
            return false;
        return HaveSameArrayReferent(parameter!, argument!)
            || HaveSameBuiltinArrayReferent(parameterType, argumentType);
    }

    private static bool HaveSameBuiltinArrayReferent(string parameterType, string argumentType)
    {
        if (!TrySplitArrayTypeSpelling(parameterType, out var parameterElement, out var parameterRank, out _)
            || !TrySplitArrayTypeSpelling(argumentType, out var argumentElement, out var argumentRank, out _)
            || parameterRank != argumentRank)
            return false;

        static string CanonicalElement(string element)
        {
            if (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(element, out var inner)
                && TypeIdentity.Canonicalize(inner) is "STRING" or "System.String")
                return "STRING";
            var canonical = TypeIdentity.Canonicalize(element);
            return canonical == "System.String" ? "STRING" : canonical;
        }

        var receiving = CanonicalElement(parameterElement);
        var supplied = CanonicalElement(argumentElement);
        return (receiving == "STRING" || IsBuiltInValueTypeName(receiving))
            && string.Equals(receiving, supplied, StringComparison.Ordinal);
    }

    private static bool HaveSameArrayReferent(
        BoundTypes.ArrayBoundType parameter,
        BoundTypes.ArrayBoundType argument)
    {
        if (parameter.Rank != argument.Rank)
            return false;
        return (parameter.ElementType, argument.ElementType) switch
        {
            (BoundTypes.ArrayBoundType parameterArray, BoundTypes.ArrayBoundType argumentArray)
                => HaveSameArrayReferent(parameterArray, argumentArray),
            (BoundTypes.PrimitiveBoundType parameterPrimitive,
                BoundTypes.PrimitiveBoundType argumentPrimitive)
                => parameterPrimitive.Equals(argumentPrimitive),
            (BoundTypes.NominalBoundType parameterNominal,
                BoundTypes.NominalBoundType argumentNominal)
                => TypeIdentity.Canonicalize(parameterNominal.QualifiedName) == "STRING"
                    && TypeIdentity.Canonicalize(argumentNominal.QualifiedName) == "STRING"
                    || parameterNominal.HasSameUnderlyingReferenceType(argumentNominal),
            _ => false
        };
    }

    private bool TryBuildStringTarget(
        string bindTypeName,
        out BoundTypes.BoundType? target,
        FunctionSymbol? declaringFunction = null)
    {
        if (TryBuildClosedReferenceGeneric(bindTypeName, declaringFunction, allowCoreReference: true)
            is { } referenceGeneric)
        {
            target = referenceGeneric;
            return true;
        }
        if (TryBuildMethodInputArrayType(bindTypeName, out var arrayTarget, declaringFunction))
        {
            target = arrayTarget;
            return true;
        }
        if (!TryBuildDeclaredNullabilityShape(bindTypeName, out target))
            return false;
        if (target is BoundTypes.NominalBoundType nominal
            && TypeIdentity.Canonicalize(nominal.QualifiedName) != "STRING")
        {
            target = declaringFunction is not null
                ? TryBuildCallableReference(bindTypeName, declaringFunction)
                : TryBuildReferenceType(
                    bindTypeName, _currentClassName, _currentNamespaceIdentity,
                    _currentMemberTypeParameters?.Select(parameter => parameter.Name));
            return target is not null;
        }
        return true;
    }

    private BoundTypes.NominalBoundType? TryBuildReferenceType(
        string typeName,
        string? containingTypeName,
        string? namespaceIdentity,
        IEnumerable<string>? typeParameters = null,
        bool allowObject = true)
        => TryBuildReferenceType(typeName, containingTypeName, namespaceIdentity,
            typeParameters, allowObject, _currentNamespaceScopeId);

    private BoundTypes.NominalBoundType? TryBuildReferenceType(
        string typeName,
        string? containingTypeName,
        string? namespaceIdentity,
        IEnumerable<string>? typeParameters,
        bool allowObject,
        string? namespaceScopeId)
    {
        var referent = typeName.Trim();
        var annotated = Parsing.AttributeHelper.TryUnwrapNullableAnnotation(referent, out var inner);
        if (annotated)
            referent = inner.Trim();
        if (referent == "object")
            return allowObject && GetOrCreateMetadataBinder()?.Context.TryResolveType("System.Object") is { } objectType
                ? new BoundTypes.NominalBoundType(referent,
                    annotated ? BoundTypes.NullableAnnotation.Annotated : _declaredReferenceAnnotation,
                    roslynSymbol: objectType)
                : null;
        var lookupReferent = string.Join(".", referent.Split('.').Select(part => part.TrimStart('@')));
        if (referent.Length == 0 || referent.IndexOfAny(['[', ']', '<', '>', '`', '*', '&', '(', ')']) >= 0
            || typeParameters?.Contains(lookupReferent, StringComparer.Ordinal) == true)
            return null;
        var canonical = TypeIdentity.Canonicalize(referent);
        if (TypeIdentity.IsBuiltinCanonicalName(canonical) && canonical is not ("OBJECT" or "STRING")
            || TypeIdentity.IsKnownValueMetadataName(referent))
            return null;
        if (canonical == "OBJECT" && !allowObject)
            return null;
        if (containingTypeName is not null
            && _classesByQualifiedName.TryGetValue(containingTypeName, out var containingClass)
            && ReturnShape.GetCallableTypeParameters(containingClass, _asyncTypeModule)
                .Any(parameter => parameter.Name == lookupReferent))
            return null;

        var annotation = annotated
            ? BoundTypes.NullableAnnotation.Annotated
            : _declaredReferenceAnnotation;
        // Predefined string spellings name the CLR special type, not a lexical type alias.
        if (referent is "STRING" or "string" or "str")
            return GetOrCreateMetadataBinder()?.Context.TryResolveType("System.String") is { } stringType
                ? new BoundTypes.NominalBoundType(referent, annotation, roslynSymbol: stringType)
                : null;
        var lexicalScope = containingTypeName is not null
            && _classesByQualifiedName.TryGetValue(containingTypeName, out var declaringClass)
                ? declaringClass.NamespaceScopeId : namespaceScopeId;
        var source = ResolveSourceType(referent, containingTypeName, namespaceIdentity, lexicalScope);
        if (source.Handled)
            return source.Type is { IsReferenceType: true } declaration
                ? new BoundTypes.NominalBoundType(referent, annotation, declaration)
                : null;

        var lookupNames = ReferenceLookupNames(lookupReferent, containingTypeName ?? namespaceIdentity).ToArray();
        var metadataName = TypeIdentity.MapShortTypeNameToFullName(lookupNames[^1]);
        var metadata = GetOrCreateMetadataBinder();
        var scoped = metadata is not null
            ? ResolveScopedMetadataType(metadata.Context, referent, 0, lexicalScope, namespaceIdentity)
            : default;
        var symbol = scoped.Handled ? scoped.Type
            : metadataName.Contains('.', StringComparison.Ordinal) ? metadata?.Context.TryResolveType(metadataName) : null;
        return symbol is { IsReferenceType: true }
            && (!symbol.IsGenericType || !symbol.IsDefinition)
            ? new BoundTypes.NominalBoundType(referent, annotation, roslynSymbol: symbol)
            : null;
    }

    private static IEnumerable<string> ReferenceLookupNames(string name, string? context)
    {
        if (name.StartsWith("global::", StringComparison.Ordinal))
        {
            yield return name["global::".Length..];
            yield break;
        }
        while (!string.IsNullOrEmpty(context))
        {
            yield return $"{context}.{name}";
            var separator = context.LastIndexOf('.');
            context = separator < 0 ? null : context[..separator];
        }
        yield return name;
    }

    private BoundTypes.NominalBoundType? TryBuildVariableReference(VariableSymbol variable) =>
        variable.IsTypeInferred ? variable.InferredReferenceType : TryBuildReferenceType(
            variable.TypeName,
            variable.DeclaringTypeName ?? _currentClassName,
            _currentNamespaceIdentity,
            variable.DeclaringTypeName is null
                ? _currentMemberTypeParameters?.Select(parameter => parameter.Name)
                : null,
            allowObject: variable.NullableAnnotation != BoundTypes.NullableAnnotation.Oblivious);

    private BoundTypes.NominalBoundType? GetInferredReferenceType(BoundExpression? expression)
    {
        if (expression?.Type is not BoundTypes.NominalBoundType nominal)
            return null;
        if (nominal.IsKnownReferenceType)
            return nominal;
        if (expression is not BoundNewExpression constructed)
            return null;

        // Keep the constructor's existing annotation; only recover its actual
        // type identity for later local reads.
        var identity = constructed.ResolvedType is { } declaration
            ? new BoundTypes.NominalBoundType(nominal.QualifiedName, nominal.NullableAnnotation, declaration)
            : TryBuildReferenceType(nominal.QualifiedName, _currentClassName,
                _currentNamespaceIdentity, _currentMemberTypeParameters?.Select(parameter => parameter.Name));
        return identity is { IsKnownReferenceType: true }
            ? new BoundTypes.NominalBoundType(nominal.QualifiedName, nominal.NullableAnnotation,
                identity.Declaration, identity.RoslynSymbol)
            : null;
    }

    private BoundTypes.NominalBoundType? TryBuildCallableReference(string typeName, FunctionSymbol function)
    {
        _functionNamespaces.TryGetValue(function.Id, out var namespaceIdentity);
        var namespaceScopeId = _functionSymbols.FirstOrDefault(pair => pair.Value == function).Key?.NamespaceScopeId;
        if (function.ContainingTypeName is not null
            && _classesByQualifiedName.TryGetValue(function.ContainingTypeName, out var cls))
            namespaceIdentity = cls.NamespaceIdentity;
        return TryBuildReferenceType(
            typeName, function.ContainingTypeName, namespaceIdentity, function.TypeParameters,
            allowObject: true, namespaceScopeId);
    }

    private bool TryBuildDeclaredNullabilityShape(string bindTypeName, out BoundTypes.BoundType? target)
    {
        target = null;
        var trimmed = bindTypeName.Trim();
        if (TryBuildClosedReferenceGeneric(trimmed) is { } referenceGeneric)
        {
            target = referenceGeneric;
            return true;
        }
        if (_declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious
            && TypeIdentity.TrySplitGeneric(trimmed.TrimEnd('?'), out _, out _))
        {
            target = BuildDeclaredBoundType(trimmed);
            return true;
        }

        // v0.14 §S6 (task #7 Phase-C) — array-shape targets. Surface forms
        // accepted here mirror what ExpandType and IsLikelyType emit:
        //   :[str] / :[string]                 -> "ARRAY[element=STRING]"    (non-null elements)
        //   :[?str] / :[?string]               -> "ARRAY[element=NULLABLE[inner=STRING]]" (nullable elements)
        //   :str[] / :string[]                 -> "STRING[]"                 (postfix; non-null elements)
        //   :?str[] / :?string[]               -> "?STRING[]"                (nullable elements)
        // Recognize both post-expansion and raw surface forms because
        // some code paths construct BindStatementNode without going
        // through ExpandType. Prefix "?" on the ELEMENT indicates nullable
        // elements; the array container itself is orthogonal (S6 diagnoses
        // element mismatch only, per the D6 follow-on scope).
        if (TryParseArrayStringTarget(trimmed, out var arrayTarget, _declaredReferenceAnnotation))
        {
            target = arrayTarget;
            return true;
        }

        // v0.14 §S7 (task #7 Phase-C) — whitelisted generic instantiations
        // whose relevant type argument is STRING. Recognizes:
        //   List<string> / ?string-in-List    (T=payload)
        //   List<T>, IList<T>, IEnumerable<T>, IReadOnlyList<T>,
        //   ICollection<T>, IReadOnlyCollection<T>  (T=element)
        // For each, position 0 of TypeArguments is the meaningful slot; the
        // container's own annotation is orthogonal (per D6 same as arrays).
        // Anything outside this whitelist (Dictionary, Task, custom generic)
        // stays out-of-scope and falls through to the scalar path below.
        if (TryParseGenericStringTarget(trimmed, out var genericTarget, _declaredReferenceAnnotation))
        {
            target = genericTarget;
            return true;
        }

        var annotatedNullable = Parsing.AttributeHelper.TryUnwrapNullableAnnotation(trimmed, out var referent);
        if (annotatedNullable)
            trimmed = referent.Trim();

        if (trimmed is ("STRING" or "string" or "str" or "System.String"))
        {
            target = new BoundTypes.NominalBoundType(
                "STRING",
                annotatedNullable
                    ? BoundTypes.NullableAnnotation.Annotated
                    : _declaredReferenceAnnotation);
            return true;
        }

        // This reads a declared spelling, not its resolved kind. The receiving
        // gate separately requires a real reference declaration or metadata symbol.
        if (trimmed.Length > 0
            && trimmed.IndexOfAny(['[', ']', '<', '>', '*', '&', '(', ')']) < 0
            && !IsBuiltInValueTypeName(trimmed))
        {
            target = new BoundTypes.NominalBoundType(
                trimmed,
                annotatedNullable
                    ? BoundTypes.NullableAnnotation.Annotated
                    : _declaredReferenceAnnotation);
            return true;
        }
        return false;
    }

    private BoundTypes.GenericInstantiationBoundType? TryBuildClosedReferenceGeneric(
        string spelling, FunctionSymbol? function = null, bool allowCoreReference = false)
    {
        if (!ResolveSourceCallConversions)
            return null;
        var annotation = _declaredReferenceAnnotation;
        if (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(spelling, out var referent))
        {
            spelling = referent;
            annotation = BoundTypes.NullableAnnotation.Annotated;
        }
        if (!TypeIdentity.TrySplitGeneric(spelling, out var name, out var arguments)
            || arguments.Count != 1 || GetOrCreateMetadataBinder() is not { } metadata)
            return null;
        var scope = _currentNamespaceScopeId;
        var ns = _currentNamespaceIdentity;
        var owner = _currentClassName;
        if (function is not null)
        {
            _functionNamespaces.TryGetValue(function.Id, out ns);
            scope = _functionSymbols.FirstOrDefault(pair => pair.Value == function).Key?.NamespaceScopeId;
            owner = function.ContainingTypeName;
        }
        if (owner is not null && _classesByQualifiedName.TryGetValue(owner, out var declaringClass))
        {
            ns = declaringClass.NamespaceIdentity;
            scope = declaringClass.NamespaceScopeId;
        }
        if (ResolveSourceType(TypeIdentity.ToLookupName(name, 1), owner, ns, scope).Handled)
            return null;
        var resolved = ResolveScopedMetadataType(metadata.Context, name, 1, scope, ns);
        var definition = resolved.Handled ? resolved.Type
            : GenericStringContainerWhitelist.Contains(name)
                ? metadata.Context.TryResolveType($"System.Collections.Generic.{ShortGenericName(name)}`1")
                : null;
        if (definition is null)
            return null;
        var boundDefinition = Metadata.MetadataBinderResult.ToNominalBoundType(definition);
        if (!NullabilityChecker.IsReferenceContainerDefinition(boundDefinition))
            return null;
        var element = function is null
            ? TryBuildReferenceType(arguments[0], owner, ns,
                _currentMemberTypeParameters?.Select(parameter => parameter.Name),
                allowObject: true, scope)
            : TryBuildCallableReference(arguments[0], function);
        if (element is null || !NullabilityChecker.IsClosedSourceReference(element)
            && !(allowCoreReference && element.RoslynSymbol?.SpecialType
                is Microsoft.CodeAnalysis.SpecialType.System_Object
                    or Microsoft.CodeAnalysis.SpecialType.System_String))
            return null;
        return new BoundTypes.GenericInstantiationBoundType(
            new BoundTypes.NominalBoundType(
                definition.ContainingNamespace + "." + definition.Name, roslynSymbol: definition),
            [element], annotation);
    }

    /// <summary>
    /// v0.14 §S8 helper — Calor built-in value type names that must NOT
    /// participate in the reference nullability gate. Absence from this
    /// list is not evidence that a type is a reference.
    /// </summary>
    internal static bool IsBuiltInValueTypeName(string name) => name switch
    {
        "INT" or "int" or "i32" => true,
        "LONG" or "long" or "i64" => true,
        "SHORT" or "short" or "i16" => true,
        "BYTE" or "byte" or "i8" => true,
        "UINT" or "uint" or "u32" => true,
        "ULONG" or "ulong" or "u64" => true,
        "USHORT" or "ushort" or "u16" => true,
        "UBYTE" or "ubyte" or "u8" => true,
        "FLOAT" or "float" or "f32" => true,
        "DOUBLE" or "double" or "f64" => true,
        "DECIMAL" or "decimal" => true,
        "BOOL" or "bool" => true,
        "CHAR" or "char" => true,
        "VOID" or "void" => true,
        _ => false,
    };

    /// <summary>
    /// v0.14 §S6 helper — recognize array-of-STRING targets (both the
    /// bracket-prefix Calor form <c>[str]</c>/<c>[?str]</c> and the
    /// postfix <c>str[]</c>/<c>?str[]</c>, plus their post-ExpandType
    /// normalizations). Builds an <see cref="BoundTypes.ArrayBoundType"/>
    /// whose element is a STRING <see cref="BoundTypes.NominalBoundType"/>
    /// carrying the declared element annotation. Returns false for
    /// non-array or non-STRING-element shapes, keeping the S6 scope
    /// narrow (D6 follow-on).
    /// </summary>
    private static bool TryParseArrayStringTarget(string trimmed, out BoundTypes.ArrayBoundType? target,
        BoundTypes.NullableAnnotation defaultAnnotation = BoundTypes.NullableAnnotation.NotAnnotated)
    {
        target = null;
        string? innerType = null;

        // Post-expansion form: ARRAY[element=<inner>]
        if (trimmed.StartsWith("ARRAY[element=", StringComparison.Ordinal)
            && trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            innerType = trimmed["ARRAY[element=".Length..^1];
        }
        // Postfix bracket form: <inner>[] (post-ExpandType) or str[]/string[] (raw)
        else if (trimmed.EndsWith("[]", StringComparison.Ordinal) && trimmed.Length > 2)
        {
            innerType = trimmed[..^2];
        }
        // Raw Calor bracket-prefix form: [<inner>]
        else if (trimmed.StartsWith("[", StringComparison.Ordinal)
                 && trimmed.EndsWith("]", StringComparison.Ordinal)
                 && trimmed.Length > 2
                 && !trimmed.Contains(',')) // Multi-dim arrays not in S6 scope
        {
            innerType = trimmed[1..^1];
        }

        if (innerType is null) return false;

        var elementAnnotation = defaultAnnotation;
        var elementTrimmed = innerType.Trim();

        if (elementTrimmed.StartsWith("NULLABLE[inner=", StringComparison.Ordinal)
            && elementTrimmed.EndsWith("]", StringComparison.Ordinal))
        {
            elementAnnotation = BoundTypes.NullableAnnotation.Annotated;
            elementTrimmed = elementTrimmed["NULLABLE[inner=".Length..^1];
        }
        else if (elementTrimmed.StartsWith("?", StringComparison.Ordinal))
        {
            elementAnnotation = BoundTypes.NullableAnnotation.Annotated;
            elementTrimmed = elementTrimmed[1..];
        }
        else if (elementTrimmed.EndsWith("?", StringComparison.Ordinal) && elementTrimmed.Length > 1)
        {
            elementAnnotation = BoundTypes.NullableAnnotation.Annotated;
            elementTrimmed = elementTrimmed[..^1];
        }

        if (elementTrimmed is not ("STRING" or "string" or "str" or "System.String"))
        {
            return false;
        }

        var elementBound = new BoundTypes.NominalBoundType("STRING", elementAnnotation);
        target = new BoundTypes.ArrayBoundType(elementBound, nullableAnnotation: defaultAnnotation);
        return true;
    }

    // v0.14 §S7 whitelist — the generic containers that participate in
    // the string-scope nullability gate. Keys cover both the Calor surface
    // spelling (e.g. "List") and Roslyn's fully-qualified
    // display-name (e.g. "System.Collections.Generic.List") so the same
    // whitelist works for both bind-site (surface strings) and metadata-
    // resolved (BCL) call/return-site parameter types. Everything not in
    // this set is deliberately out-of-scope; extending the set is a
    // scoping decision (D6 discipline).
    private static readonly HashSet<string> GenericStringContainerWhitelist =
        new(StringComparer.Ordinal)
        {
            "List",             "System.Collections.Generic.List",
            "IList",            "System.Collections.Generic.IList",
            "IEnumerable",      "System.Collections.Generic.IEnumerable",
            "IReadOnlyList",    "System.Collections.Generic.IReadOnlyList",
            "ICollection",      "System.Collections.Generic.ICollection",
            "IReadOnlyCollection", "System.Collections.Generic.IReadOnlyCollection",
        };

    /// <summary>
    /// v0.14 §S7 helper — recognizes a whitelisted generic instantiation
    /// whose position-0 type argument is a scalar STRING. Accepts both
    /// the Calor surface form (<c>List&lt;string&gt;</c>,
    /// <c>List&lt;?string&gt;</c>) and the post-ExpandType form where the
    /// argument is normalized (<c>List&lt;STRING&gt;</c>,
    /// <c>List&lt;NULLABLE[inner=STRING]&gt;</c>). Returns a
    /// <see cref="BoundTypes.GenericInstantiationBoundType"/> whose
    /// single argument is a STRING <see cref="BoundTypes.NominalBoundType"/>
    /// carrying the declared inner annotation, so
    /// <see cref="NullabilityChecker.IsPossiblyNullAssignedTo"/> can compare
    /// symmetrically against a source generic. Returns false for
    /// non-whitelisted definitions or non-STRING type arguments.
    /// </summary>
    private static bool TryParseGenericStringTarget(string trimmed, out BoundTypes.GenericInstantiationBoundType? target,
        BoundTypes.NullableAnnotation defaultAnnotation = BoundTypes.NullableAnnotation.NotAnnotated)
    {
        target = null;
        var outerAnnotation = defaultAnnotation;
        if (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(trimmed, out var referent))
        {
            outerAnnotation = BoundTypes.NullableAnnotation.Annotated;
            trimmed = referent.Trim();
        }

        var open = trimmed.IndexOf('<');
        if (open <= 0) return false;
        if (!trimmed.EndsWith(">", StringComparison.Ordinal)) return false;

        var baseName = trimmed[..open];
        var argsSection = trimmed[(open + 1)..^1];

        // S7 scope narrow: whitelist gate first — anything else is
        // out-of-scope per D6 and must not construct a target.
        if (!GenericStringContainerWhitelist.Contains(baseName)) return false;

        // Position-0 argument only: none of the whitelisted containers
        // use multi-arg like Dictionary yet. If the argument list contains
        // a top-level comma, we conservatively reject rather than guess.
        if (ContainsTopLevelComma(argsSection)) return false;

        var innerRaw = argsSection.Trim();
        var innerAnnotation = defaultAnnotation;

        if (innerRaw.StartsWith("NULLABLE[inner=", StringComparison.Ordinal)
            && innerRaw.EndsWith("]", StringComparison.Ordinal))
        {
            innerAnnotation = BoundTypes.NullableAnnotation.Annotated;
            innerRaw = innerRaw["NULLABLE[inner=".Length..^1];
        }
        else if (innerRaw.StartsWith("?", StringComparison.Ordinal) && innerRaw.Length > 1)
        {
            innerAnnotation = BoundTypes.NullableAnnotation.Annotated;
            innerRaw = innerRaw[1..];
        }
        else if (innerRaw.EndsWith("?", StringComparison.Ordinal) && innerRaw.Length > 1)
        {
            innerAnnotation = BoundTypes.NullableAnnotation.Annotated;
            innerRaw = innerRaw[..^1];
        }

        if (innerRaw is not ("STRING" or "string" or "str" or "System.String"))
        {
            return false;
        }

        var innerBound = new BoundTypes.NominalBoundType("STRING", innerAnnotation);
        var definition = new BoundTypes.NominalBoundType(baseName, outerAnnotation);
        target = new BoundTypes.GenericInstantiationBoundType(
            definition,
            System.Collections.Immutable.ImmutableArray.Create<BoundTypes.BoundType>(innerBound),
            outerAnnotation);
        return true;
    }

    /// <summary>
    /// Helper for <see cref="TryParseGenericStringTarget"/> — returns true
    /// when the generic argument list contains a comma at the top level
    /// (i.e. a multi-argument generic). Nested angle brackets and square
    /// brackets are respected so <c>Dictionary&lt;string, List&lt;int&gt;&gt;</c>
    /// is detected as multi-arg while <c>List&lt;NULLABLE[inner=STRING]&gt;</c>
    /// is not.
    /// </summary>
    private static bool ContainsTopLevelComma(string argsSection)
    {
        var depth = 0;
        foreach (var c in argsSection)
        {
            switch (c)
            {
                case '<':
                case '[':
                    depth++;
                    break;
                case '>':
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// v0.14 §S4 helper — reshapes a resolved callee parameter's
    /// NominalBoundType into the target the <see cref="NullabilityChecker"/>
    /// consumes for scalar STRING checks. Returns null when the parameter
    /// is NOT a scalar STRING (out of scope per D6: arrays, generics,
    /// user reference types) OR when the parameter is already declared
    /// nullable (:?string — accepting null is by design). Callers use
    /// null as "skip this argument, do not fire Calor0274".
    /// </summary>
    private static BoundTypes.BoundType? TryBuildScalarStringTarget(BoundTypes.NominalBoundType parameterType)
    {
        if (parameterType is null) return null;

        // Roslyn STRING types round-trip through NominalBoundType as either
        // "System.String" (fully-qualified) or "string" (short display).
        // Match both — NullabilityChecker.IsScalarString also accepts either,
        // but we build the target with the canonical "STRING" spelling to
        // mirror the S3 bind-side helper.
        var isScalarString = parameterType.QualifiedName switch
        {
            "STRING" => true,
            "string" => true,
            "str" => true,
            "System.String" => true,
            _ => false,
        };
        if (isScalarString)
        {
            // Parameter already declared nullable — accepting null is intended.
            if (parameterType.NullableAnnotation != BoundTypes.NullableAnnotation.NotAnnotated) return null;

            return new BoundTypes.NominalBoundType(
                "STRING",
                BoundTypes.NullableAnnotation.NotAnnotated);
        }

        if (parameterType.IsKnownReferenceType)
            return parameterType;

        // v0.14 §S6 — array-of-STRING parameter shape. MetadataBinder
        // currently flattens Roslyn IArrayTypeSymbol into a NominalBoundType
        // whose QualifiedName is "string[]" / "System.String[]". Parse
        // that surface here so Calor0274 fires when a possibly-null-
        // element array is passed into a non-null-element parameter.
        var arrayShape = TryBuildArrayStringParameterTarget(parameterType);
        if (arrayShape is not null) return arrayShape;

        // v0.14 §S7 — whitelisted generic instantiation parameter shape.
        // MetadataBinder currently flattens INamedTypeSymbol into a
        // NominalBoundType whose QualifiedName reads as e.g.
        // "System.Collections.Generic.List<string>". Parse that surface
        // here so Calor0274 fires when a possibly-null-elements generic
        // is passed into a non-nullable-elements parameter.
        return TryBuildGenericStringParameterTarget(parameterType);
    }

    /// <summary>
    /// v0.14 §S6 helper — recognizes an array-of-STRING parameter shape
    /// encoded by MetadataBinder as a flat <see cref="BoundTypes.NominalBoundType"/>
    /// with QualifiedName like <c>string[]</c> / <c>System.String[]</c>.
    /// Returns an <see cref="BoundTypes.ArrayBoundType"/> target with a
    /// STRING element carrying NotAnnotated so the checker will fire on
    /// possibly-null-element sources. Returns null when the parameter is
    /// not a string-element array. Element-level nullability from the
    /// Roslyn side is not yet observable through the flat encoding, so
    /// this helper conservatively treats the parameter as expecting
    /// non-null elements — the same conservative default as scalar STRING.
    /// </summary>
    private static BoundTypes.BoundType? TryBuildArrayStringParameterTarget(BoundTypes.NominalBoundType parameterType)
    {
        var qn = parameterType.QualifiedName;
        if (!qn.EndsWith("[]", StringComparison.Ordinal)) return null;
        var element = qn[..^2];
        var isStringElement = element switch
        {
            "STRING" => true,
            "string" => true,
            "str" => true,
            "System.String" => true,
            _ => false,
        };
        if (!isStringElement) return null;

        return new BoundTypes.ArrayBoundType(
            new BoundTypes.NominalBoundType("STRING", BoundTypes.NullableAnnotation.NotAnnotated));
    }

    /// <summary>
    /// v0.14 §S7 helper — recognizes a whitelisted generic instantiation
    /// parameter shape encoded by MetadataBinder as a flat
    /// <see cref="BoundTypes.NominalBoundType"/> with QualifiedName like
    /// <c>System.Collections.Generic.List&lt;string&gt;</c>. Reuses
    /// <see cref="TryParseGenericStringTarget"/> to preserve the same
    /// whitelist + inner-annotation parsing. Returns null when the
    /// parameter is not a whitelisted generic-of-STRING. Roslyn's per-
    /// argument nullability is not yet observable through the flat
    /// QualifiedName encoding, so the parameter is conservatively treated
    /// as expecting a NotAnnotated inner — matching how S6 treats array
    /// parameters.
    /// </summary>
    private static BoundTypes.BoundType? TryBuildGenericStringParameterTarget(BoundTypes.NominalBoundType parameterType)
    {
        // Delegate all parsing to the surface-form helper so the whitelist
        // and inner-annotation parsing stay in one place. The flat
        // QualifiedName format Roslyn emits ("List<string>",
        // "System.Collections.Generic.List<string>") is a subset of what
        // TryParseGenericStringTarget already accepts.
        return TryParseGenericStringTarget(parameterType.QualifiedName, out var target)
            ? target
            : null;
    }

    /// <summary>
    /// 0.30: a module declared <c>nullable oblivious</c> mirrors C#'s disabled nullable
    /// context, where an oblivious value may flow into a non-null parameter, return,
    /// binding or receiver without a diagnostic. Only a wholly oblivious source is
    /// exempt: an explicit <c>?T</c> anywhere in its type, a null literal, a
    /// null-containing conditional, or storage the module may have set to null (see
    /// <see cref="_obliviousNullTaintedStorage"/>) is still reported. Modules without the policy keep
    /// the conservative Oblivious-is-possibly-null default (D3).
    /// </summary>
    private bool IsObliviousFlowInObliviousModule(BoundExpression source)
        => _declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious
           && !MayIntroduceNullInObliviousModule(source)
           && !(source is BoundVariableExpression variable
               && (_obliviousNullTaintedStorage.Contains(variable.Variable.StorageIdentity)
                   || variable.ResolvedSymbols.Any(symbol =>
                       _obliviousNullTaintedStorage.Contains(symbol.StorageIdentity))));

    private bool MayIntroduceNullInObliviousModule(BoundExpression source)
        => _declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious
           && (source is BoundNullLiteral
               || NullabilityChecker.IsNullContainingScalarStringSource(source)
               || HasAnnotatedComponent(source.Type)
               || NullabilityChecker.GetMethodInputArrayType(source) is { } inputArray
                  && HasAnnotatedComponent(inputArray));

    private static bool HasAnnotatedComponent(BoundTypes.BoundType type) => type switch
    {
        BoundTypes.NominalBoundType nominal
            => nominal.NullableAnnotation == BoundTypes.NullableAnnotation.Annotated,
        BoundTypes.GenericInstantiationBoundType generic
            => generic.NullableAnnotation == BoundTypes.NullableAnnotation.Annotated
               || generic.TypeArguments.Any(HasAnnotatedComponent),
        BoundTypes.ArrayBoundType array
            => array.NullableAnnotation == BoundTypes.NullableAnnotation.Annotated
               || HasAnnotatedComponent(array.ElementType),
        _ => false
    };

    private static string DescribeAnnotation(BoundExpression source) => source.Type switch
    {
        BoundTypes.NominalBoundType n => n.NullableAnnotation.ToString(),
        BoundTypes.GenericInstantiationBoundType g => g.NullableAnnotation.ToString(),
        BoundTypes.ArrayBoundType a => a.NullableAnnotation.ToString(),
        _ => "unknown",
    };

    /// <summary>
    /// v0.14 §S7 — shape-labels for the three Calor027X diagnostics.
    /// Returns a pair (declared-shape label, fix-hint label) that mirrors
    /// the target's actual shape so messages read naturally at each of
    /// the three emit sites (S3 bind, S4 return, S4 call). Scalar STRING
    /// yields <c>'string'</c> / <c>'string?'</c>, array yields
    /// <c>'string[]'</c> / <c>'string?[]'</c>, generic yields
    /// <c>'List&lt;string&gt;'</c> / <c>'List&lt;string?&gt;'</c>
    /// (spelled with the definition's short name so it matches what the
    /// user wrote). Fix hints use the native suffix spelling (0.30); the
    /// retired prefix form '?T' no longer parses.
    /// </summary>
    private static (string TargetShapeLabel, string FixHintLabel) DescribeStringTargetShape(BoundTypes.BoundType stringTarget)
    {
        return stringTarget switch
        {
            BoundTypes.ArrayBoundType => ("'string[]'", "'string?[]'"),
            BoundTypes.GenericInstantiationBoundType g =>
                (
                    $"'{ShortGenericName(g.Definition.QualifiedName)}<string>'",
                    $"'{ShortGenericName(g.Definition.QualifiedName)}<string?>'"
                ),
            // v0.14 §S8 — user-declared reference types echo their own name
            // in the message so users see 'Foo' / 'Foo?' rather than the
            // scalar-STRING boilerplate. Distinguished by QualifiedName not
            // matching the STRING aliases.
            BoundTypes.NominalBoundType n when n.QualifiedName is not ("STRING" or "string" or "str" or "System.String") =>
                ($"'{n.QualifiedName}'", $"'{n.QualifiedName}?'"),
            _ => ("'string'", "'string?'"),
        };
    }

    private static string? DescribeClosedGenericReceiving(
        BoundExpression source, BoundTypes.BoundType target)
    {
        if (target is not BoundTypes.GenericInstantiationBoundType
            { TypeArguments: [BoundTypes.NominalBoundType receiving] } generic)
            return null;
        var supplied = source.GenericNullabilityType ?? source.Type as BoundTypes.GenericInstantiationBoundType;
        var value = supplied?.TypeArguments.SingleOrDefault() as BoundTypes.NominalBoundType;
        if (!NullabilityChecker.IsClosedSourceReference(receiving)
            && (value is null || !NullabilityChecker.IsClosedSourceReference(value)))
            return null;
        return $"Reference-generic receiving contract '{generic.Definition.QualifiedName}<{receiving.QualifiedName}>' " +
            $"requires container annotation '{generic.NullableAnnotation}' and element annotation '{receiving.NullableAnnotation}', " +
            $"but the supplied annotations are '{supplied?.NullableAnnotation.ToString() ?? "null"}' " +
            $"and '{value?.NullableAnnotation.ToString() ?? "unsupported"}'. " +
            "Prove the affected container/elements non-null or use compatible annotations; " +
            "invariant containers must also preserve the original element contract against nullable writes.";
    }

    /// <summary>Trims a possibly fully-qualified container name to its
    /// last dotted segment so diagnostic labels read as the user wrote
    /// them (e.g. <c>System.Collections.Generic.List</c> → <c>List</c>).</summary>
    private static string ShortGenericName(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        return lastDot < 0 ? qualifiedName : qualifiedName[(lastDot + 1)..];
    }

    // v0.14 nullability workstream — helper for BindBindStatement to keep
    // the source-count of TypeName string-equality sites flat (F-3 ratchet
    // in BoundTypeArchitectureTests). Delegates to TryBuildStringTarget so
    // surface-form parsing (`:string`, `:?string`, `:str`,
    // `NULLABLE[inner=STRING]`) stays in one place.
    // v0.14 nullability workstream — fallback for §B{x} STR:"hi" (inferred
    // type). When bind.TypeName is null but the inferred typeName resolves
    // to a scalar STRING, copy the initializer's annotation onto the
    // symbol. This lets a subsequent §B{y:string} x see NotAnnotated for
    // literal-initialized locals (which are provably non-null) rather
    // than silently degrading to Oblivious and misfiring Calor0272.
    // Non-STRING inferred types keep the safe Oblivious default per §D6.
    private BoundTypes.NullableAnnotation InferAnnotationForStringBinding(
        string? bindTypeName,
        string typeName,
        BoundExpression? initializer)
    {
        // `is not null` (rather than `!= null`) sidesteps the F-3 grep-
        // based ratchet on typename-string-equality sites.
        if (bindTypeName is not null) return BoundTypes.NullableAnnotation.Oblivious;
        if (initializer is null) return BoundTypes.NullableAnnotation.Oblivious;
        if (!TryBuildDeclaredNullabilityShape(typeName, out var target)
            || target is not BoundTypes.NominalBoundType { QualifiedName: "STRING" })
            return BoundTypes.NullableAnnotation.Oblivious;
        return initializer.Type is BoundTypes.NominalBoundType n
            ? n.NullableAnnotation
            : BoundTypes.NullableAnnotation.Oblivious;
    }

    private BoundTypes.NullableAnnotation TryReadDeclaredStringAnnotation(string? bindTypeName)
    {
        if (bindTypeName is null) return BoundTypes.NullableAnnotation.Oblivious;
        if (!TryBuildDeclaredNullabilityShape(bindTypeName, out var target)) return BoundTypes.NullableAnnotation.Oblivious;
        // This helper feeds the scalar VariableSymbol.NullableAnnotation
        // for downstream BoundVariableExpression reads. Only scalar STRING
        // targets contribute — array-shape (§S6) VariableSymbols do not
        // yet carry a per-element annotation, so array targets fall
        // through as Oblivious. When S6 threads element annotations onto
        // array VariableSymbols this helper widens.
        return target switch
        {
            BoundTypes.NominalBoundType n => n.NullableAnnotation,
            _ => BoundTypes.NullableAnnotation.Oblivious,
        };
    }

    private VariableSymbol? FindRebindTarget(string name)
    {
        if (_scope.Lookup(name) is not VariableSymbol variable)
            return null;

        var classMember = _currentClassScope?.LookupLocal(name);
        return classMember is VariableSymbol member && member.Id == variable.Id
            ? null
            : variable;
    }

    private static bool CanRebind(VariableSymbol variable) =>
        variable.IsMutable
        || (variable.IsParameter
            && !variable.Modifier.HasFlag(ParameterModifier.In));

    private static bool AreAssignmentCompatible(string targetType, string valueType)
    {
        var target = TypeIdentity.Canonicalize(targetType);
        var value = TypeIdentity.Canonicalize(valueType);
        if (target == value || target is "OBJECT" || value is "OBJECT" or "<unresolved>")
            return true;

        return value switch
        {
            "INT[bits=8][signed=true]" => target is
                "INT[bits=16][signed=true]" or "INT" or "LONG"
                or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "INT[bits=8][signed=false]" => target is
                "INT[bits=16][signed=true]" or "INT[bits=16][signed=false]"
                or "INT" or "UINT" or "LONG" or "ULONG"
                or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "INT[bits=16][signed=true]" => target is
                "INT" or "LONG" or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "INT[bits=16][signed=false]" => target is
                "INT" or "UINT" or "LONG" or "ULONG"
                or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "INT" => target is "LONG" or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "UINT" => target is "LONG" or "ULONG" or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "LONG" or "ULONG" => target is "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "FLOAT[bits=32]" => target is "FLOAT",
            _ => false,
        };
    }

    private BoundExpression BindExpression(ExpressionNode expr) => BindExpression(expr, targetType: null);

    private BoundExpression BindExpression(
        ExpressionNode expr, BoundTypes.FunctionBoundType? targetType)
    {
        ArgumentNullException.ThrowIfNull(expr);
        ExpressionsBound++;
        var bound = expr is LambdaExpressionNode lambda && targetType != null
            ? BindLambdaExpression(lambda, targetType)
            : ExpressionBinders.TryGetValue(expr.GetType(), out var binder)
            ? binder(this, expr)
            : BindUnsupportedExpression(expr);
        if (bound is IBoundStorageExpression storageExpression
            && TryGetStableMemberStorage(expr, bound, out var memberStorage, out _, requireNonNullFlow: true)
            && _scope.Lookup(memberStorage.Name) is VariableSymbol narrowedMember
            && _flowNarrowingSources.ContainsKey(narrowedMember)
            && bound.Type is BoundTypes.NominalBoundType memberType)
        {
            var nonNull = new BoundTypes.NominalBoundType(
                memberType.QualifiedName, BoundTypes.NullableAnnotation.NotAnnotated,
                memberType.Declaration, memberType.RoslynSymbol);
            bound = storageExpression.WithFlowType(nonNull);
        }
        if (_bindingContract)
            _boundContractExpressions[expr] = bound;
        if (expr is CallExpressionNode or ExpressionCallNode && bound is BoundCallExpression call)
            _boundVerificationCalls[expr] = call;
        if (MayChangeHeap(bound))
            InvalidateByReferenceParameterNarrowing();
        if (bound is BoundFieldAccessExpression
            {
                ResolvedField.IsEnumMember: true,
                Target: BoundTypeReferenceExpression { ResolvedType: { IsEnum: true } owner },
                Type: BoundTypes.NominalBoundType { Declaration: { } resultOwner }
            } constant && ReferenceEquals(owner, resultOwner))
            _boundSourceEnumConstants[expr] = constant;
        if (bound is BoundFieldAccessExpression
            {
                ResolvedField: { IsField: true, IsStatic: true, ConditionalAlternative: null } selected,
                ResolvedFields: [var field],
                SourceStaticFieldOwner: not null,
                Target: BoundTypeReferenceExpression
                {
                    ResolvedType: { } receiver,
                    Type: BoundTypes.NominalBoundType { Declaration: { } receiverDeclaration }
                }
            } staticField && ReferenceEquals(selected, field)
                && ReferenceEquals(receiver, receiverDeclaration))
            _boundSourceStaticFieldReads[expr] = staticField;
        if (bound is BoundFieldAccessExpression
            {
                Target: BoundTypeReferenceExpression,
                ResolvedMetadataMember: Microsoft.CodeAnalysis.IFieldSymbol
                    { IsStatic: true, IsConst: true, Type.SpecialType: not Microsoft.CodeAnalysis.SpecialType.None }
            } metadataConstant)
            _boundMetadataConstants[expr] = metadataConstant;
        if (bound is BoundFieldAccessExpression
            {
                Target: BoundTypeReferenceExpression,
                ResolvedMetadataMember: Microsoft.CodeAnalysis.IFieldSymbol
                    { IsStatic: true, IsConst: false, Type.TypeKind: not Microsoft.CodeAnalysis.TypeKind.Error },
                Type: BoundTypes.NominalBoundType { RoslynSymbol: not null }
            } metadataStaticField)
            _boundMetadataStaticFieldReads[expr] = metadataStaticField;
        return bound;
    }

    internal BoundExpression BindVerificationReceiver(
        ExpressionNode receiver, IEnumerable<(string Name, string Type)> variables)
    {
        using var scope = PushVerificationScope(variables, receiver.Span);
        if (TryBindPredefinedTypeReceiver(receiver) is { } predefined)
            return predefined;
        if (receiver is ReferenceNode reference && GetOrCreateMetadataBinder() is { } metadata)
        {
            if (_scope.Lookup(reference.Name) is VariableSymbol variable
                && ResolveVerificationType(metadata.Context, variable.TypeName) is { } variableType)
                return new BoundVariableExpression(receiver.Span, variable,
                    typeOverride: Metadata.MetadataBinderResult.ToBoundType(variableType));
            if (_scope.Lookup(reference.Name) is null
                && !(reference.IsVerbatim && SyntaxFactory.ParseTypeName(reference.Name) is PredefinedTypeSyntax)
                && ResolveVerificationType(metadata.Context, reference.Name) is { } staticType)
                return new BoundTypeReferenceExpression(receiver.Span, reference.Name,
                    Metadata.MetadataBinderResult.ToNominalBoundType(staticType));
        }
        return TryBindMetadataTypeReceiver(receiver) ?? BindExpression(receiver);
    }

    internal BoundExpression BindVerificationExpression(
        ExpressionNode expression, IEnumerable<(string Name, string Type)> variables)
    {
        using var scope = PushVerificationScope(variables, expression.Span);
        return BindExpression(expression);
    }

    private static Microsoft.CodeAnalysis.ITypeSymbol? ResolveVerificationType(
        Metadata.MetadataContext context, string typeName)
    {
        var open = typeName.IndexOf('<');
        if (open > 0 && typeName.EndsWith('>')
            && typeName[..open] is "List" or "System.Collections.Generic.List"
            && ResolveBclArgumentType(context, typeName[(open + 1)..^1]) is { } element
            && context.TryResolveType("System.Collections.Generic.List`1") is { } list)
            return list.Construct(element);
        return ResolveBclArgumentType(context, typeName);
    }

    private IDisposable PushVerificationScope(
        IEnumerable<(string Name, string Type)> variables, Parsing.TextSpan span)
    {
        var scope = PushScope(_scope.CreateChild());
        foreach (var (name, type) in variables)
            _scope.TryDeclare(new VariableSymbol(
                SymbolId.Create("verification", name), name, type,
                isMutable: false, isParameter: true, ParameterModifier.None, span)
            {
                MethodInputArrayType = TryBuildMethodInputArrayType(type, out var array) ? array : null,
                GenericNullabilityType = TryParseGenericStringTarget(type, out var generic, _declaredReferenceAnnotation) ? generic : null
            });
        return scope;
    }

    internal Microsoft.CodeAnalysis.IMethodSymbol? ResolveVerificationStringCall(
        CallExpressionNode call, bool isStatic, bool ordinal,
        IEnumerable<(string Name, string Type)> variables)
    {
        using var scope = PushVerificationScope(variables, call.Span);
        var metadata = GetOrCreateMetadataBinder();
        if (metadata?.Context.TryResolveType("System.String") is not { } receiver)
            return null;
        var args = new List<Metadata.MetadataArgument>();
        for (var index = 0; index < call.Arguments.Count; index++)
        {
            var type = ordinal && index == call.Arguments.Count - 1
                ? metadata.Context.TryResolveType("System.StringComparison")
                : ResolveBclArgumentType(metadata.Context, BindExpression(call.Arguments[index]).Type.DisplayString);
            if (type is null)
                return null;
            args.Add(new Metadata.MetadataArgument(type));
        }
        var resolution = metadata.ResolveCall(receiver, call.Target[(call.Target.LastIndexOf('.') + 1)..], args);
        if (!resolution.IsResolved || resolution.Symbol is not { } method
            || method.IsStatic != isStatic || method.Parameters.Length != args.Count
            || method.ContainingType.SpecialType != Microsoft.CodeAnalysis.SpecialType.System_String
            || method.Parameters.Where((parameter, index) =>
                !Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(parameter.Type, args[index].Type)).Any())
            return null;
        return method;
    }

    private BoundExpression BindIntLiteral(IntLiteralNode literal)
    {
        var typeName = literal.IsUnsigned
            ? literal.IsLong || literal.UnsignedValue > uint.MaxValue ? "ULONG" : "UINT"
            : literal.IsLong || literal.Value is > int.MaxValue or < int.MinValue ? "LONG" : "INT";
        return new BoundIntLiteral(
            literal.Span,
            literal.Value,
            literal.UnsignedValue,
            literal.IsUnsigned,
            typeName);
    }

    private BoundExpression BindFloatLiteral(FloatLiteralNode literal)
    {
        if (literal.IsDecimal)
            return new BoundDecimalLiteral(literal.Span, (decimal)literal.Value);

        var typeName = literal.IsSingle ? "FLOAT[bits=32]" : "FLOAT";
        var value = literal.IsSingle ? (double)(float)literal.Value : literal.Value;
        return new BoundFloatLiteral(literal.Span, value, typeName);
    }

    private BoundExpression BindStringLiteral(StringLiteralNode literal) =>
        new BoundStringLiteral(
            literal.Span,
            literal.Value,
            literal.IsMultiline,
            literal.IsUtf8,
            literal.IsUtf8 ? null : GetOrCreateMetadataBinder()?.Context.TryResolveType("System.String"));

    private BoundExpression BindThisExpression(ThisExpressionNode expression)
    {
        if (!_isStaticContext && _currentClassName != null)
            return new BoundThisExpression(expression.Span, _currentClassName);

        return BindUnsupportedExpression(
            expression,
            _currentClassName ?? "UNKNOWN",
            reason: _isStaticContext
                ? "'this' is unavailable in a static context"
                : "'this' is unavailable outside an instance member");
    }

    private BoundExpression BindBaseExpression(BaseExpressionNode expression)
    {
        if (!_isStaticContext && _currentClassName != null)
        {
            var baseClass = ResolveBaseClass(_currentClass);
            return new BoundBaseExpression(
                expression.Span,
                baseClass == null ? "OBJECT" : _qualifiedClassNames[baseClass]);
        }

        return BindUnsupportedExpression(
            expression,
            reason: _isStaticContext
                ? "'base' is unavailable in a static context"
                : "'base' is unavailable outside an instance member");
    }

    private BoundExpression BindFieldAccess(FieldAccessNode fieldAccess, BoundExpression? boundTarget = null,
        bool isPatternMember = false)
    {
        var target = boundTarget
            ?? TryBindSourceEnumTypeReceiver(fieldAccess.Target)
            ?? TryBindSourceStaticFieldReceiver(fieldAccess)
            ?? TryBindMetadataTypeReceiver(fieldAccess.Target)
            ?? BindExpression(fieldAccess.Target);
        if (target is BoundConditionalAccessExpression conditionalTarget)
            return ContinueConditionalAccess(fieldAccess.Span, conditionalTarget,
                BindFieldAccess(fieldAccess, conditionalTarget.WhenNotNull, isPatternMember));
        if (TryGetEventStorage(fieldAccess, out var eventStorage, out var eventType))
            return new BoundFieldAccessExpression(fieldAccess.Span, target, fieldAccess.FieldName,
                eventStorage.TypeName, eventStorage, fieldAccess.FieldNameSpan, resolvedType: eventType);
        if (target is BoundTypeReferenceExpression { ResolvedType: { IsEnum: true } enumType })
        {
            var members = _enumMembers[enumType.Id].Where(member => member.Name == fieldAccess.FieldName).ToArray();
            if (members is [var member] && IsSourceTypeAccessible(enumType))
                return new BoundFieldAccessExpression(fieldAccess.Span, target, fieldAccess.FieldName,
                    enumType.QualifiedName, member, fieldAccess.FieldNameSpan,
                    resolvedType: new BoundTypes.NominalBoundType(enumType.QualifiedName, declaration: enumType));
            _diagnostics.ReportError(fieldAccess.FieldNameSpan, DiagnosticCode.UndefinedReference,
                $"Enum member '{enumType.QualifiedName}.{fieldAccess.FieldName}' is missing, ambiguous or inaccessible.");
            return new BoundFieldAccessExpression(fieldAccess.Span, target, fieldAccess.FieldName, typeName: null,
                fieldNameSpan: fieldAccess.FieldNameSpan,
                resolvedType: new BoundTypes.UnresolvedBoundType("Unresolved source enum member", reported: true))
            {
                HasUnknownInferredType = true
            };
        }
        if (fieldAccess.FieldName == "Length" && IsArrayReceiver(target))
            return new BoundArrayLength(fieldAccess.Span, target);
        if (ResolveMetadataScalarArrayProperty(target, fieldAccess.FieldName) is { } scalarArrayProperty)
            return new BoundFieldAccessExpression(
                fieldAccess.Span, target, fieldAccess.FieldName, typeName: null,
                fieldNameSpan: fieldAccess.FieldNameSpan,
                resolvedType: Metadata.MetadataBinderResult.ToNominalBoundType(scalarArrayProperty.Type))
            {
                ResolvedMetadataMember = scalarArrayProperty
            };
        if (target.AnonymousObjectMembers is { } anonymousMembers
            && anonymousMembers.Where(member => member.Name == fieldAccess.FieldName).ToArray() is [var anonymousMember])
        {
            var value = anonymousMember.Value;
            return new BoundFieldAccessExpression(fieldAccess.Span, target, fieldAccess.FieldName,
                typeName: null, fieldNameSpan: fieldAccess.FieldNameSpan, resolvedType: value.Type)
            {
                IsAnonymousPropertyRead = true,
                AnonymousObjectMembers = value.AnonymousObjectMembers,
                MethodInputArrayType = value.MethodInputArrayType,
                MetadataArrayResultType = value.MetadataArrayResultType,
                GenericNullabilityType = value.GenericNullabilityType,
                HasUnknownInferredType = value.HasUnknownInferredType
            };
        }
        if (target is BoundVariableExpression { ResolvedSymbols.Count: 1 } tupleVariable
            && tupleVariable.Variable.TypeName.StartsWith('(')
            && SyntaxFactory.ParseTypeName(tupleVariable.Variable.TypeName) is TupleTypeSyntax tupleType
            && !tupleType.ContainsDiagnostics
            && fieldAccess.FieldName.StartsWith("Item", StringComparison.Ordinal)
            && int.TryParse(fieldAccess.FieldName.AsSpan(4), out var tupleIndex)
            && tupleIndex >= 1 && tupleIndex <= tupleType.Elements.Count
            && fieldAccess.FieldName == $"Item{tupleIndex}")
            return new BoundFieldAccessExpression(fieldAccess.Span, target, fieldAccess.FieldName,
                tupleType.Elements[tupleIndex - 1].Type.ToString(), fieldNameSpan: fieldAccess.FieldNameSpan)
            {
                IsTupleElementRead = true
            };
        var resolvedFields = fieldAccess.Target switch
        {
            _ when target is BoundTypeReferenceExpression { ResolvedType: { } sourceType }
                && _classesByQualifiedName.TryGetValue(sourceType.QualifiedName, out var sourceOwner) =>
                ResolveAccessibleMembers(sourceOwner, fieldAccess.FieldName)
                    .OfType<VariableSymbol>().Where(member => member.IsField && member.IsStatic).Cast<Symbol>().ToArray(),
            ThisExpressionNode => ResolveAccessibleMembers(_currentClass, fieldAccess.FieldName),
            BaseExpressionNode => ResolveAccessibleMembers(
                ResolveBaseClass(_currentClass),
                fieldAccess.FieldName),
            // #943 (D1 RO-COPY-1): a field of an in-module struct value (`(§IDX{pts} 0).X`) is
            // one storage path; resolving it keeps the operand typed instead of OBJECT.
            _ when isPatternMember => ResolveAccessibleMembers(
                ResolveClass(target.Type.DisplayString), fieldAccess.FieldName),
            _ when !target.HasUnknownInferredType
                && target is BoundArrayAccess or BoundMultiDimArrayAccess or IBoundStorageExpression
                && target.Type is BoundTypes.NominalBoundType
                    { Declaration: { } declaration, NullableAnnotation: not BoundTypes.NullableAnnotation.Annotated }
                && IsSourceTypeAccessible(declaration)
                && !declaration.QualifiedName.Contains('`')
                && !TypeIdentity.IsBuiltinCanonicalName(TypeIdentity.Canonicalize(declaration.Name))
                && _classesByQualifiedName.TryGetValue(declaration.QualifiedName, out var declaredOwner)
                && declaredOwner.TypeParameters.Count == 0 =>
                ResolveReadableInstanceMembers(declaredOwner, fieldAccess.FieldName),
            _ when ResolveClass(target.Type.DisplayString) is { IsStruct: false } instanceOwner =>
                ResolveReadableInstanceProperties(instanceOwner, fieldAccess.FieldName),
            _ => ResolveStructFieldPath(target.Type.DisplayString, fieldAccess.FieldName),
        };
        var fields = resolvedFields.OfType<VariableSymbol>().ToArray();
        if (fields.Any(field => field.EventDeclaration is not null))
        {
            _diagnostics.ReportError(fieldAccess.FieldNameSpan, DiagnosticCode.UndefinedReference,
                $"Event '{fieldAccess.FieldName}' is not readable as delegate storage in this context");
            return new BoundFieldAccessExpression(fieldAccess.Span, target, fieldAccess.FieldName, typeName: null,
                fieldNameSpan: fieldAccess.FieldNameSpan,
                resolvedType: new BoundTypes.UnresolvedBoundType("Unavailable event backing storage", reported: true))
            {
                HasUnknownInferredType = true
            };
        }
        var fieldTypes = fields
            .Select(field => TypeIdentity.Canonicalize(field.TypeName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        BoundTypes.BoundType? resolvedType = null;
        if (fields is [var field])
        {
            if (ResolveVariableEnumType(field) is { } fieldEnum)
                resolvedType = new BoundTypes.NominalBoundType(fieldEnum.QualifiedName, declaration: fieldEnum);
            var identity = TryBuildVariableReference(field);
            var isString = TryBuildDeclaredNullabilityShape(field.TypeName, out var declared)
                && declared is BoundTypes.NominalBoundType { QualifiedName: "STRING" };
            if (resolvedType is null && (identity is not null || isString))
                resolvedType = new BoundTypes.NominalBoundType(
                    field.TypeName, field.NullableAnnotation, identity?.Declaration, identity?.RoslynSymbol);
        }

        if (_asyncTypeModule != null
            && fields is [var nativeMember]
            && (isPatternMember || nativeMember.IsProperty)
            && TypeIdentity.TryResolveNativeInstanceMemberType(
                _asyncTypeModule, target.Type.DisplayString, fieldAccess.FieldName, out var nativeMemberType))
            return new BoundFieldAccessExpression(
                fieldAccess.Span, target, fieldAccess.FieldName, nativeMemberType, nativeMember,
                fieldAccess.FieldNameSpan, fields,
                nativeMember.IsProperty && resolvedType is BoundTypes.NominalBoundType nominal
                    ? new BoundTypes.NominalBoundType(nativeMemberType, nominal.NullableAnnotation,
                        nominal.Declaration, nominal.RoslynSymbol)
                    : null);

        if (fields.Length == 0
            && ResolveMetadataArrayProperty(target, fieldAccess.FieldName) is { } arrayProperty)
        {
            return new BoundFieldAccessExpression(
                fieldAccess.Span, target, fieldAccess.FieldName, typeName: null,
                fieldNameSpan: fieldAccess.FieldNameSpan)
            {
                ResolvedMetadataMember = arrayProperty,
                MetadataArrayResultType = arrayProperty.Type as Microsoft.CodeAnalysis.IArrayTypeSymbol
            };
        }

        // Native this/base resolution remains authoritative; N2 owns its annotation transfer.
        if (fields.Length == 0
            && target.Type is BoundTypes.NominalBoundType
            {
                RoslynSymbol: { IsGenericType: false } receiverType
            }
            && (receiverType.IsReferenceType
                || target is BoundTypeReferenceExpression
                    && (receiverType.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum
                        || receiverType.SpecialType != Microsoft.CodeAnalysis.SpecialType.None)))
        {
            var metadataBinder = GetOrCreateMetadataBinder();
            string? reason = "The metadata context is unavailable.";
            var member = metadataBinder?.ResolveMemberRead(
                receiverType, fieldAccess.FieldName, target is BoundTypeReferenceExpression, out reason);
            var memberType = member switch
            {
                Microsoft.CodeAnalysis.IPropertySymbol property => property.Type,
                Microsoft.CodeAnalysis.IFieldSymbol metadataField => metadataField.Type,
                _ => null
            };
            if (memberType is Microsoft.CodeAnalysis.INamedTypeSymbol namedMemberType
                && (namedMemberType is { IsReferenceType: true, IsGenericType: false }
                    || TypeIdentity.IsBuiltinCanonicalName(TypeIdentity.Canonicalize(namedMemberType.ToDisplayString()))
                    || member is Microsoft.CodeAnalysis.IFieldSymbol { IsConst: true, Type.TypeKind: Microsoft.CodeAnalysis.TypeKind.Enum }
                        && Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(namedMemberType, receiverType)))
            {
                if (member is Microsoft.CodeAnalysis.IPropertySymbol property)
                    ValidateStringInstanceReceiver(target, property.GetMethod);
                return new BoundFieldAccessExpression(
                    fieldAccess.Span, target, fieldAccess.FieldName, typeName: null,
                    fieldNameSpan: fieldAccess.FieldNameSpan,
                    resolvedType: namedMemberType.IsValueType
                        ? new BoundTypes.NominalBoundType(
                            NormalizeTypeName(AttributeHelper.ExpandType(namedMemberType.ToDisplayString())),
                            Metadata.MetadataBinderResult.MapAnnotation(memberType.NullableAnnotation),
                            roslynSymbol: namedMemberType)
                        : Metadata.MetadataBinderResult.ToNominalBoundType(memberType))
                {
                    ResolvedMetadataMember = member
                };
            }

            reason ??= "The member result is outside supported scalar reads.";
            _diagnostics.ReportInfo(fieldAccess.Span, DiagnosticCode.SignatureUnresolved,
                $"Metadata member resolution incomplete: {reason}");
            return new BoundFieldAccessExpression(
                fieldAccess.Span, target, fieldAccess.FieldName, typeName: null,
                fieldNameSpan: fieldAccess.FieldNameSpan,
                resolvedType: new BoundTypes.UnresolvedBoundType(reason))
            {
                // #943: the by-reference operand check still needs to know it is a property.
                UnsupportedMetadataMember = member,
                HasUnknownInferredType = true
            };
        }

        return new BoundFieldAccessExpression(
            fieldAccess.Span,
            target,
            fieldAccess.FieldName,
            fieldTypes.Length == 1 ? fields[0].TypeName : "OBJECT",
            fields.FirstOrDefault(),
            fieldAccess.FieldNameSpan,
            fields,
            resolvedType)
        {
            SourceStaticFieldOwner = target is BoundTypeReferenceExpression
                && fields is [{ IsField: true, IsStatic: true, DeclaringTypeName: { } declaringType }]
                ? _classesByQualifiedName.GetValueOrDefault(declaringType) : null,
            MethodInputArrayType = fields is [var arrayField] ? arrayField.MethodInputArrayType : null,
            HasUnknownInferredType = fieldTypes.Length != 1
                || fields.Any(field => field.HasUnknownInferredType)
        };
    }

    /// <summary>#943: the instance field a (possibly dotted) path names through in-module struct values.</summary>
    private Symbol[] ResolveStructFieldPath(string typeName, string path)
    {
        VariableSymbol? field = null;
        foreach (var segment in path.Split('.'))
        {
            if (ResolveClass(field?.TypeName ?? typeName) is not { IsStruct: true } owner)
                return [];
            field = ResolveAccessibleMembers(owner, segment).OfType<VariableSymbol>()
                .FirstOrDefault(member => member is { IsField: true, IsStatic: false });
            if (field is null)
                return [];
        }
        return field is null ? [] : [field];
    }

    private BoundTypeReferenceExpression? TryBindMetadataTypeReceiver(ExpressionNode expression)
    {
        if (TryBindPredefinedTypeReceiver(expression) is { } predefined)
            return predefined;
        var name = DottedReferenceName(expression);
        if (name is null)
            return null;
        var head = name.Split('.')[0];
        if (IsTypeReceiverHeadShadowed(head, allowDeclaredTypes: false))
            return null;
        if (ResolveSourceType(name, _currentClassName, _currentNamespaceIdentity, _currentNamespaceScopeId).Handled)
            return null;
        if (expression is ReferenceNode { IsVerbatim: true }
            && SyntaxFactory.ParseTypeName(name) is PredefinedTypeSyntax)
            return null;
        var type = TryBuildReferenceType(
            name, _currentClassName, _currentNamespaceIdentity,
            _currentMemberTypeParameters?.Select(parameter => parameter.Name));
        if (type?.RoslynSymbol is not null)
            return new BoundTypeReferenceExpression(expression.Span, name, type);
        return GetOrCreateMetadataBinder() is { } metadata
            && ResolveComputedMetadataType(metadata.Context, name) is Microsoft.CodeAnalysis.INamedTypeSymbol symbol
                ? new BoundTypeReferenceExpression(expression.Span, name,
                    Metadata.MetadataBinderResult.ToNominalBoundType(symbol))
                : null;
    }

    private BoundTypeReferenceExpression? TryBindSourceEnumTypeReceiver(ExpressionNode expression)
    {
        var name = DottedReferenceName(expression);
        if (name is null
            || !name.StartsWith("global::", StringComparison.Ordinal)
                && IsTypeReceiverHeadShadowed(name.Split('.')[0], allowDeclaredTypes: true))
            return null;
        var resolved = ResolveSourceType(name, _currentClassName, _currentNamespaceIdentity, _currentNamespaceScopeId);
        return resolved.Type is { IsEnum: true } owner
            ? new BoundTypeReferenceExpression(expression.Span, name,
                new BoundTypes.NominalBoundType(owner.QualifiedName, declaration: owner), owner)
            : null;
    }

    private BoundTypeReferenceExpression? TryBindSourceStaticFieldReceiver(FieldAccessNode expression)
    {
        var name = DottedReferenceName(expression.Target);
        if (name is null
            || !name.StartsWith("global::", StringComparison.Ordinal)
                && IsTypeReceiverHeadShadowed(name.Split('.')[0], allowDeclaredTypes: true))
            return null;
        var type = ResolveSourceType(name, _currentClassName, _currentNamespaceIdentity, _currentNamespaceScopeId).Type;
        if (type is null || !IsSourceTypeAccessible(type)
            || !_classesByQualifiedName.TryGetValue(type.QualifiedName, out var owner)
            || ResolveAccessibleMembers(owner, expression.FieldName).OfType<VariableSymbol>().ToArray()
                is not [{ IsField: true, IsStatic: true }])
            return null;
        return new BoundTypeReferenceExpression(expression.Target.Span, name,
            new BoundTypes.NominalBoundType(type.QualifiedName, declaration: type), type);
    }

    private (bool Handled, TypeSymbol? Type) ResolveSourceType(
        string name, string? containingTypeName, string? namespaceIdentity, string? scopeId)
    {
        static string LookupName(string value) =>
            string.Join(".", value.Split('.').Select(part => part.TrimStart('@')));
        var global = name.StartsWith("global::", StringComparison.Ordinal);
        var spelling = global ? name["global::".Length..] : name;
        spelling = LookupName(spelling);
        var head = spelling.Split('.')[0];
        var visibleScopes = new HashSet<string>(StringComparer.Ordinal);
        for (var id = scopeId; id is not null;)
        {
            if (!visibleScopes.Add(id) || !_metadataNamespaceScopes.TryGetValue(id, out var scope))
                return (true, null);
            id = scope.ParentScopeId;
        }
        var imports = _metadataUsings.Where(import =>
            import.NamespaceScopeId is null || visibleScopes.Contains(import.NamespaceScopeId)).ToArray();
        var aliases = global ? [] : imports.Where(import => import.Alias == head).ToArray();
        if (aliases.Length > 1)
            return (true, null);
        var types = SourceTypeSymbols();
        foreach (var lookup in ReferenceLookupNames(
            global ? "global::" + spelling : spelling, containingTypeName ?? namespaceIdentity))
        {
            var matches = types.Where(type => LookupName(type.QualifiedName) == LookupName(lookup)).Take(2).ToArray();
            if (matches.Length > 0)
                return aliases.Length == 0 && matches is [var match] ? (true, match) : (true, null);
            var separator = lookup.Length - spelling.Length - 1;
            if (separator > 0 && _classesByQualifiedName.TryGetValue(lookup[..separator], out var owner)
                && EnumerateCurrentClassInteropBlocks(owner).Any(block =>
                    CSharpSyntaxTree.ParseText("class __Host { " + block.CSharpCode + " }").GetCompilationUnitRoot()
                        .Members.OfType<ClassDeclarationSyntax>().SelectMany(host => host.Members).Any(member => member switch
                        {
                            BaseTypeDeclarationSyntax type => type.Identifier.ValueText == head,
                            DelegateDeclarationSyntax type => type.Identifier.ValueText == head,
                            _ => false
                        })))
                return (true, null);
        }
        if (aliases is [var alias])
        {
            var target = string.Join(".", alias.Namespace.Replace("global::", "", StringComparison.Ordinal)
                    .Split('.').Select(part => part.TrimStart('@')))
                + spelling[head.Length..];
            var matches = types.Where(type => LookupName(type.QualifiedName) == target).Take(2).ToArray();
            return matches.Length == 0 ? default : (true, matches is [var match] ? match : null);
        }
        if (global)
            return default;
        var imported = imports.Where(import => import.Alias is null && !import.IsStatic)
            .SelectMany(import => types.Where(type => LookupName(type.QualifiedName) == LookupName(import.Namespace) + "." + spelling))
            .Distinct().Take(2).ToArray();
        if (imported.Length == 0)
            return default;
        // A metadata import can compete with a source import; neither spelling wins by itself.
        if (GetOrCreateMetadataBinder() is { } metadata
            && ResolveScopedMetadataType(metadata.Context, spelling, 0, scopeId, namespaceIdentity).Type is not null)
            return (true, null);
        return (true, imported is [var importedType] ? importedType : null);
    }

    private bool IsSourceTypeAccessible(TypeSymbol type)
    {
        while (true)
        {
            var separator = type.QualifiedName.LastIndexOf('.');
            if (separator < 0 || !_classesByQualifiedName.TryGetValue(type.QualifiedName[..separator], out var owner))
                return type.Visibility is Visibility.Public or Visibility.Internal;
            var accessible = type.Visibility switch
            {
                Visibility.Private => IsWithinDeclaringType(type.QualifiedName[..separator]),
                Visibility.Protected or Visibility.PrivateProtected =>
                    _currentClass != null && IsSameOrDerivedClass(_currentClass, type.QualifiedName[..separator]),
                _ => true
            };
            if (!accessible)
                return false;
            type = _classSymbols[owner];
        }
    }

    private TypeSymbol? ResolveCallableEnumType(FunctionSymbol function, string typeName)
    {
        if (_enumMembers.Count == 0 || function.TypeParameters.Contains(typeName, StringComparer.Ordinal))
            return null;
        _functionNamespaces.TryGetValue(function.Id, out var ns);
        var scopeId = _functionSymbols.FirstOrDefault(pair => pair.Value == function).Key?.NamespaceScopeId;
        if (function.ContainingTypeName is not null
            && _classesByQualifiedName.TryGetValue(function.ContainingTypeName, out var owner))
        {
            if (ReturnShape.GetCallableTypeParameters(owner, _asyncTypeModule).Any(parameter => parameter.Name == typeName))
                return null;
            ns = owner.NamespaceIdentity;
            scopeId = owner.NamespaceScopeId;
        }
        return ResolveSourceType(typeName, function.ContainingTypeName, ns, scopeId).Type
            is { IsEnum: true } type ? type : null;
    }

    private string ResolveCallableParameterType(FunctionSymbol function, string typeName)
    {
        if (ResolveCallableEnumType(function, typeName) is { } type)
            return "global::" + type.QualifiedName;
        if (!TypeIdentity.IsBuiltinCanonicalName(TypeIdentity.Canonicalize(typeName).TrimEnd('?'))
            && _functionSymbols.Values.Contains(function)
            && GetResolvedStringOverloadType(TryBuildCallableReference(typeName, function)) is { } stringType)
            return stringType;
        return typeName;
    }

    private static string? GetResolvedStringOverloadType(BoundTypes.BoundType? type) =>
        type is BoundTypes.NominalBoundType
            { RoslynSymbol.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String } reference
                ? reference.NullableAnnotation == BoundTypes.NullableAnnotation.Annotated ? "STRING?" : "STRING"
                : null;

    private TypeSymbol? ResolveVariableEnumType(VariableSymbol variable)
    {
        var owner = variable.DeclaringTypeName is { } ownerName
            ? _classesByQualifiedName.GetValueOrDefault(ownerName) : null;
        if (owner is null ? GetTypeParameterConstraints(variable.TypeName) is not null
            : ReturnShape.GetCallableTypeParameters(owner, _asyncTypeModule).Any(parameter => parameter.Name == variable.TypeName))
            return null;
        return ResolveSourceType(variable.TypeName, variable.DeclaringTypeName ?? _currentClassName,
            owner?.NamespaceIdentity ?? _currentNamespaceIdentity,
            owner?.NamespaceScopeId ?? _currentNamespaceScopeId).Type is { IsEnum: true } type ? type : null;
    }

    private BoundTypeReferenceExpression? TryBindPredefinedTypeReceiver(ExpressionNode expression)
    {
        if (expression is not ReferenceNode { IsVerbatim: false } reference
            || SyntaxFactory.ParseTypeName(reference.Name) is not PredefinedTypeSyntax predefined
            || GetOrCreateMetadataBinder() is not { } metadata)
            return null;
        var specialType = predefined.Keyword.Kind() switch
        {
            SyntaxKind.BoolKeyword => Microsoft.CodeAnalysis.SpecialType.System_Boolean,
            SyntaxKind.ByteKeyword => Microsoft.CodeAnalysis.SpecialType.System_Byte,
            SyntaxKind.SByteKeyword => Microsoft.CodeAnalysis.SpecialType.System_SByte,
            SyntaxKind.ShortKeyword => Microsoft.CodeAnalysis.SpecialType.System_Int16,
            SyntaxKind.UShortKeyword => Microsoft.CodeAnalysis.SpecialType.System_UInt16,
            SyntaxKind.IntKeyword => Microsoft.CodeAnalysis.SpecialType.System_Int32,
            SyntaxKind.UIntKeyword => Microsoft.CodeAnalysis.SpecialType.System_UInt32,
            SyntaxKind.LongKeyword => Microsoft.CodeAnalysis.SpecialType.System_Int64,
            SyntaxKind.ULongKeyword => Microsoft.CodeAnalysis.SpecialType.System_UInt64,
            SyntaxKind.CharKeyword => Microsoft.CodeAnalysis.SpecialType.System_Char,
            SyntaxKind.FloatKeyword => Microsoft.CodeAnalysis.SpecialType.System_Single,
            SyntaxKind.DoubleKeyword => Microsoft.CodeAnalysis.SpecialType.System_Double,
            SyntaxKind.DecimalKeyword => Microsoft.CodeAnalysis.SpecialType.System_Decimal,
            SyntaxKind.StringKeyword => Microsoft.CodeAnalysis.SpecialType.System_String,
            SyntaxKind.ObjectKeyword => Microsoft.CodeAnalysis.SpecialType.System_Object,
            SyntaxKind.VoidKeyword => Microsoft.CodeAnalysis.SpecialType.System_Void,
            _ => Microsoft.CodeAnalysis.SpecialType.None
        };
        if (specialType == Microsoft.CodeAnalysis.SpecialType.None)
            return null;
        var symbol = metadata.Context.HostCompilationForBinder.GetSpecialType(specialType);
        return symbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Error
            ? new BoundTypeReferenceExpression(expression.Span, reference.Name,
                Metadata.MetadataBinderResult.ToNominalBoundType(symbol))
            : null;
    }

    private bool IsTypeReceiverHeadShadowed(string head, bool allowDeclaredTypes) =>
        _scope.LookupAll(head).Concat(ResolveLexicalMembers(head))
            .Any(symbol => !allowDeclaredTypes || symbol is not TypeSymbol)
        || GetTypeParameterConstraints(head) is not null;

    private bool HasVisibleUsingAlias(string head)
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        for (var id = _currentNamespaceScopeId; id is not null
            && scopes.Add(id) && _metadataNamespaceScopes.TryGetValue(id, out var scope); id = scope.ParentScopeId)
        {
        }
        return _metadataUsings.Any(import => import.Alias == head.TrimStart('@')
            && (import.IsGlobal || import.NamespaceScopeId is null || scopes.Contains(import.NamespaceScopeId)));
    }

    internal static string? DottedReferenceName(ExpressionNode expression) => expression switch
    {
        ReferenceNode reference => reference.Name,
        FieldAccessNode field when DottedReferenceName(field.Target) is { } prefix =>
            $"{prefix}.{field.FieldName}",
        _ => null
    };

    private BoundExpression BindOverflowContext(OverflowContextExpressionNode expression)
    {
        var operand = BindExpression(expression.Operand);
        return new BoundStructuralExpression(
            expression.Span, nameof(OverflowContextExpressionNode), operand.Type.DisplayString,
            [operand], new Dictionary<string, object?> { ["IsChecked"] = expression.IsChecked },
            deferredChildren: null, typeAnnotation: BoundTypes.NullableAnnotation.Oblivious,
            resultType: operand.Type)
        {
            HasUnknownInferredType = operand.HasUnknownInferredType,
            MethodInputArrayType = operand.MethodInputArrayType,
            GenericNullabilityType = operand.GenericNullabilityType
        };
    }

    private BoundExpression BindTypeOperation(TypeOperationNode typeOp)
    {
        BoundTypes.FunctionBoundType? callable = null;
        if (typeOp is { Operation: TypeOp.Cast, Operand: LambdaExpressionNode }
            && GetOrCreateMetadataBinder() is { } metadata
            && ResolveComputedMetadataType(metadata.Context, typeOp.TargetType)
                is Microsoft.CodeAnalysis.INamedTypeSymbol
                    { TypeKind: Microsoft.CodeAnalysis.TypeKind.Delegate, DelegateInvokeMethod: { } invoke })
            callable = new BoundTypes.FunctionBoundType(
                System.Collections.Immutable.ImmutableArray.CreateRange(
                    invoke.Parameters.Select(parameter => Metadata.MetadataBinderResult.ToBoundType(parameter.Type))),
                Metadata.MetadataBinderResult.ToBoundType(invoke.ReturnType),
                displayOverride: typeOp.TargetType, row: BoundTypes.EffectRow.Unknown);
        var operand = BindExpression(typeOp.Operand, callable);
        // Keep the delegate's return identity and nullable contract; only a bound
        // non-null body can refine an otherwise oblivious object result.
        if (callable?.ReturnType is BoundTypes.NominalBoundType
            {
                RoslynSymbol.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_Object,
                NullableAnnotation: BoundTypes.NullableAnnotation.Oblivious
            } objectResult
            && operand is BoundLambdaExpression { IsAsync: false, ExpressionBody: { } body }
            && (IsDefinitelyNonNullScalar(body)
                || NullabilityChecker.GetMethodInputArrayType(body)?.NullableAnnotation
                    == BoundTypes.NullableAnnotation.NotAnnotated
                || NullabilityChecker.GetAnnotation(body.Type) == BoundTypes.NullableAnnotation.NotAnnotated
                    && (body.Type switch
                    {
                        BoundTypes.NominalBoundType nominal =>
                            nominal.RoslynSymbol?.IsReferenceType == true || nominal.IsKnownReferenceType,
                        BoundTypes.ArrayBoundType => true,
                        BoundTypes.GenericInstantiationBoundType generic =>
                            generic.RoslynSymbol?.IsReferenceType == true
                            || generic.Definition.Declaration?.IsReferenceType == true,
                        _ => false
                    })))
            callable = new BoundTypes.FunctionBoundType(callable.ParameterTypes,
                new BoundTypes.NominalBoundType(objectResult.QualifiedName,
                    BoundTypes.NullableAnnotation.NotAnnotated, objectResult.Declaration, objectResult.RoslynSymbol),
                callable.DisplayString, callable.Row, callable.ParameterRows);
        var annotation = typeOp.Operation switch
        {
            TypeOp.Cast when operand is BoundNullLiteral =>
                BoundTypes.NullableAnnotation.Annotated,
            TypeOp.As when IsScalarStringTypeName(typeOp.TargetType) =>
                BoundTypes.NullableAnnotation.Annotated,
            TypeOp.Cast when IsScalarStringTypeName(typeOp.TargetType)
                             && (IsBuiltInValueTypeName(TypeIdentity.Canonicalize(operand.Type.DisplayString))
                                 || operand.Type is BoundTypes.NominalBoundType source
                                 && IsScalarStringTypeName(source.QualifiedName)
                                 && source.NullableAnnotation == BoundTypes.NullableAnnotation.NotAnnotated) =>
                BoundTypes.NullableAnnotation.NotAnnotated,
            _ => BoundTypes.NullableAnnotation.Oblivious
        };
        BoundTypes.BoundType? resolvedType = callable;
        if (typeOp.Operation is TypeOp.Cast or TypeOp.As
            && TryBuildReferenceType(typeOp.TargetType, _currentClassName, _currentNamespaceIdentity,
                _currentMemberTypeParameters?.Select(parameter => parameter.Name))
                is { RoslynSymbol.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String } stringType)
        {
            if (typeOp.Operation == TypeOp.Cast && !operand.HasUnknownInferredType
                && operand.Type is BoundTypes.NominalBoundType
                    { RoslynSymbol.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String } stringSource)
                annotation = stringSource.NullableAnnotation;
            resolvedType = new BoundTypes.NominalBoundType(
                typeOp.TargetType, annotation, roslynSymbol: stringType.RoslynSymbol);
        }
        if (typeOp.Operation == TypeOp.Cast && GetTypeParameterConstraints(typeOp.TargetType) is null
            && ResolveSourceType(typeOp.TargetType, _currentClassName, _currentNamespaceIdentity,
                _currentNamespaceScopeId).Type is { IsEnum: true } enumType)
            resolvedType = new BoundTypes.NominalBoundType(enumType.QualifiedName, declaration: enumType);
        return new BoundTypeOperationExpression(
            typeOp.Span,
            typeOp.Operation,
            operand,
            typeOp.TargetType,
            annotation,
            resolvedType: resolvedType);
    }

    private BoundExpression BindIsPattern(IsPatternNode isPattern)
    {
        var operand = BindExpression(isPattern.Operand);
        return new BoundIsPatternExpression(
            isPattern.Span,
            operand,
            isPattern.TargetType,
            isPattern.VariableName)
        {
            Binding = isPattern.VariableName is null ? null : CreateLocalVariable(
                isPattern.VariableName,
                isPattern.TargetType == "var" ? operand.Type.DisplayString : isPattern.TargetType,
                isMutable: false, isParameter: false, ParameterModifier.None, isPattern.Span, "pattern",
                nullableAnnotation: isPattern.TargetType == "var"
                    ? NullabilityChecker.GetAnnotation(operand.Type) ?? BoundTypes.NullableAnnotation.Oblivious
                    : BoundTypes.NullableAnnotation.NotAnnotated)
        };
    }

    private void DeclareSuccessfulConditionPatterns(ExpressionNode condition, BoundExpression boundCondition)
        => DeclareSuccessfulConditionPatterns(condition, boundCondition, null);

    private void DeclareSuccessfulConditionPatterns(
        ExpressionNode condition,
        BoundExpression boundCondition,
        IReadOnlySet<string>? excludedNarrowings)
    {
        if (condition is BinaryOperationNode { Operator: BinaryOperator.And } conjunction
            && boundCondition is BoundBinaryExpression binary)
        {
            var leftExclusions = excludedNarrowings is null
                ? binary.InvalidatedLeftNarrowingNames
                : excludedNarrowings
                    .Concat(binary.InvalidatedLeftNarrowingNames)
                    .ToHashSet(StringComparer.Ordinal);
            DeclareSuccessfulConditionPatterns(
                conjunction.Left,
                binary.Left,
                leftExclusions);
            DeclareSuccessfulConditionPatterns(conjunction.Right, binary.Right, excludedNarrowings);
        }
        if (condition is IsPatternNode { VariableName: not null } pattern
            && boundCondition is BoundIsPatternExpression { Binding: not null } boundPattern)
        {
            if (excludedNarrowings?.Contains(pattern.VariableName) == true)
                return;
            DeclarePatternVariable(pattern.Span, pattern.VariableName, boundPattern.Binding.TypeName,
                boundPattern.Binding.NullableAnnotation, boundPattern.Binding);
        }
        if (condition is IsPatternNode
            {
                VariableName: null,
                Operand: ReferenceNode testedReference,
                TargetType: var testedType
            }
            && boundCondition is BoundIsPatternExpression
            {
                Operand: BoundVariableExpression testedVariable
            })
        {
            if (excludedNarrowings?.Contains(testedReference.Name) == true)
                return;
            DeclareNarrowedNonNullVariable(
                testedReference,
                testedVariable.Variable,
                testedType);
        }
        if (condition is TypeOperationNode
            {
                Operation: TypeOp.Is,
                Operand: ReferenceNode reference,
                TargetType: var targetType
            }
            && boundCondition is BoundTypeOperationExpression
            {
                Operand: BoundVariableExpression typedVariable
            })
        {
            if (excludedNarrowings?.Contains(reference.Name) == true)
                return;
            DeclareNarrowedNonNullVariable(
                reference,
                typedVariable.Variable,
                targetType);
        }
        if (condition is BinaryOperationNode { Operator: BinaryOperator.NotEqual } comparison
            && boundCondition is BoundBinaryExpression boundComparison)
        {
            if (comparison.Left is ReferenceNode { IsNullLiteral: true }
                && comparison.Right is ReferenceNode right
                && boundComparison.Right is BoundVariableExpression rightVariable)
            {
                if (excludedNarrowings?.Contains(right.Name) == true)
                    return;
                DeclareNarrowedNonNullVariable(right, rightVariable.Variable);
            }
            else if (comparison.Right is ReferenceNode { IsNullLiteral: true }
                     && comparison.Left is ReferenceNode left
                     && boundComparison.Left is BoundVariableExpression leftVariable)
            {
                if (excludedNarrowings?.Contains(left.Name) == true)
                    return;
                DeclareNarrowedNonNullVariable(left, leftVariable.Variable);
            }
        }
    }

    private void DeclareFailedConditionPatterns(ExpressionNode condition, BoundExpression boundCondition)
    {
        var arguments = condition switch
        {
            CallExpressionNode call => call.Arguments,
            ExpressionCallNode call => call.Arguments,
            _ => null
        };
        if (arguments is [ReferenceNode referenceArgument]
            && boundCondition is BoundCallExpression
            {
                Arguments: [BoundVariableExpression variableArgument],
                ResolvedMetadataMethod:
                {
                    IsStatic: true,
                    Name: nameof(string.IsNullOrEmpty) or nameof(string.IsNullOrWhiteSpace),
                    ContainingType.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String,
                    ReturnType.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_Boolean,
                    Parameters: [{ Type.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String }]
                }
            })
        {
            DeclareNarrowedNonNullVariable(referenceArgument, variableArgument.Variable);
        }
        if (condition is StringOperationNode
            {
                Operation: StringOp.IsNullOrEmpty or StringOp.IsNullOrWhiteSpace,
                Arguments: [ReferenceNode reference]
            }
            && boundCondition is BoundStructuralExpression
            {
                Children: [BoundVariableExpression variable]
            })
        {
            DeclareNarrowedNonNullVariable(reference, variable.Variable);
        }
    }

    private bool IsSelectedBuiltInNullGuard(BinaryOperationNode condition)
    {
        if (condition.Operator is not (BinaryOperator.Equal or BinaryOperator.Or)
            || !ResolveSourceCallConversions || _asyncTypeModule is null)
            return false;
        SourceCallBindings();
        return _sourceCallConversions.BuiltInNullGuardOperations.Contains(condition);
    }

    private void DeclareFailedNullEqualityConditionPatterns(
        ExpressionNode condition,
        BoundExpression boundCondition,
        IReadOnlySet<string>? excludedNarrowings = null)
    {
        if (condition is not BinaryOperationNode comparison
            || boundCondition is not BoundBinaryExpression binary
            || !IsSelectedBuiltInNullGuard(comparison))
            return;

        if (comparison.Operator == BinaryOperator.Or)
        {
            var leftExclusions = excludedNarrowings is null
                ? binary.InvalidatedLeftNarrowingNames
                : excludedNarrowings.Concat(binary.InvalidatedLeftNarrowingNames)
                    .ToHashSet(StringComparer.Ordinal);
            DeclareFailedNullEqualityConditionPatterns(comparison.Left, binary.Left, leftExclusions);
            DeclareFailedNullEqualityConditionPatterns(comparison.Right, binary.Right, excludedNarrowings);
            return;
        }

        if (comparison.Operator != BinaryOperator.Equal)
            return;
        var (reference, bound) = comparison.Left is ReferenceNode { IsNullLiteral: true }
            ? (comparison.Right, binary.Right)
            : comparison.Right is ReferenceNode { IsNullLiteral: true }
                ? (comparison.Left, binary.Left)
                : (null, null);
        if (reference is ReferenceNode local
            && bound is BoundVariableExpression { HasUnknownInferredType: false } variable
            && variable.Variable.StorageIdentity is { IsField: false, IsProperty: false }
            && excludedNarrowings?.Contains(local.Name) != true)
            DeclareNarrowedNonNullVariable(local, variable.Variable);
        else if (reference is not null && bound is not null
            && TryGetStableMemberStorage(reference, bound, out var storage, out var receiver)
            && excludedNarrowings?.Contains(storage.Name) != true
            && (receiver is null || excludedNarrowings?.Contains(receiver.Name) != true))
            DeclareNarrowedNonNullVariable(new ReferenceNode(reference.Span, storage.Name), storage);
    }

    private bool TryGetStableMemberStorage(
        ExpressionNode expression, BoundExpression bound,
        out VariableSymbol storage, out VariableSymbol? receiver, bool requireNonNullFlow = false)
    {
        storage = null!;
        receiver = null;
        if (_sourceCallConversions?.StableMemberReads.GetValueOrDefault(expression) is not { } selected
            || requireNonNullFlow && !selected.HasNonNullFlow)
            return false;
        if (bound is not IBoundStorageExpression { ResolvedStorageCandidates: [var candidate] })
            return false;
        var member = candidate.StorageIdentity;
        if (!member.IsProperty || !ReferenceEquals(
                _classScopes[selected.Owner].LookupLocal(selected.Property.Name), member))
            return false;
        if (bound is BoundFieldAccessExpression access)
        {
            if (access.Target is BoundVariableExpression { HasUnknownInferredType: false } variable
                && variable.Variable.StorageIdentity is { IsField: false, IsProperty: false }
                && variable.Type is BoundTypes.NominalBoundType { Declaration: { } owner }
                && ReferenceEquals(owner, _classSymbols[selected.Owner]))
                receiver = variable.Variable.StorageIdentity;
            else if (access.Target is not BoundThisExpression
                || !ReferenceEquals(_currentClass, selected.Owner))
                return false;
        }
        else if (!ReferenceEquals(_currentClass, selected.Owner))
            return false;

        var key = (receiver, member);
        if (!_memberFlowStorages.TryGetValue(key, out storage!))
        {
            // These scoped flow slots retain the real member/receiver pair; they are
            // not source declarations or dotted-name aliases for user storage.
            storage = new VariableSymbol(
                member.Id.Append("member-flow", receiver?.Id.Value ?? "this"),
                $"\0member-flow:{_memberFlowStorages.Count}", member.TypeName,
                isMutable: true, nullableAnnotation: member.NullableAnnotation);
            _memberFlowStorages.Add(key, storage);
        }
        return true;
    }

    private void InvalidateMemberFlowNarrowing(VariableSymbol variable)
    {
        foreach (var (key, storage) in _memberFlowStorages)
        {
            if ((ReferenceEquals(key.Receiver, variable.StorageIdentity)
                    || ReferenceEquals(key.Member, variable.StorageIdentity))
                && _scope.Lookup(storage.Name) is VariableSymbol narrowed
                && _flowNarrowingSources.ContainsKey(narrowed))
                InvalidateMutableFlowNarrowing(narrowed, requireRebindable: false);
        }
    }

    private void InvalidateMemberFlowNarrowing(BoundExpression target)
    {
        if (target is BoundVariableExpression variable)
            InvalidateMemberFlowNarrowing(variable.Variable);
        else if (target is BoundFieldAccessExpression field)
            foreach (var member in field.ResolvedFields)
                InvalidateMemberFlowNarrowing(member);
        else if (target is BoundTupleLiteral tuple)
            foreach (var element in tuple.Elements)
                InvalidateMemberFlowNarrowing(element);
    }

    private void DeclareNarrowedNonNullVariable(
        ReferenceNode reference,
        VariableSymbol variable,
        string? narrowedTypeName = null)
    {
        if (narrowedTypeName is null
            && NullabilityChecker.GetAnnotation(new BoundVariableExpression(reference.Span, variable).Type)
                is not (BoundTypes.NullableAnnotation.Annotated
                    or BoundTypes.NullableAnnotation.Oblivious))
        {
            return;
        }

        var narrowed = CreateLocalVariable(
            reference.Name,
            narrowedTypeName ?? variable.TypeName,
            variable.IsMutable,
            variable.IsParameter,
            variable.Modifier,
            reference.Span,
            "narrowed",
            variable.DefaultValue,
            BoundTypes.NullableAnnotation.NotAnnotated,
            variable.FunctionType,
            variable.InferredReferenceType,
            variable.IsTypeInferred,
            variable.MethodInputArrayType is { } array
                ? new BoundTypes.ArrayBoundType(
                    array.ElementType,
                    array.Rank,
                    BoundTypes.NullableAnnotation.NotAnnotated)
                {
                    RoslynSymbol = array.RoslynSymbol
                }
                : null,
            variable.GenericNullabilityType is { } generic
                ? new BoundTypes.GenericInstantiationBoundType(
                    new BoundTypes.NominalBoundType(
                        generic.Definition.QualifiedName,
                        BoundTypes.NullableAnnotation.NotAnnotated,
                        generic.Definition.Declaration,
                        generic.Definition.RoslynSymbol),
                    generic.TypeArguments,
                    BoundTypes.NullableAnnotation.NotAnnotated)
                : null);
        narrowed.StorageIdentity = variable.StorageIdentity;
        if (_scope.TryDeclare(narrowed) || _scope.TryReplaceLocal(variable, narrowed))
            _flowNarrowingSources[narrowed] = variable;
    }

    private VariableSymbol InvalidateMutableFlowNarrowing(
        VariableSymbol variable,
        bool requireRebindable = true,
        bool mayWriteNull = true)
    {
        if (mayWriteNull && _declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious)
            _obliviousNullTaintedStorage.Add(variable.StorageIdentity);
        InvalidateMemberFlowNarrowing(variable);
        variable.StorageIdentity.MetadataArrayResultType = null;
        variable.MetadataArrayResultType = null;
        var source = variable;
        var traversed = new List<VariableSymbol>();
        while (_flowNarrowingSources.TryGetValue(source, out var previous))
        {
            traversed.Add(source);
            source = previous;
        }

        if (traversed.Count == 0)
        {
            var descendants = _flowNarrowingSources.Keys
                .Where(narrowed =>
                {
                    var root = narrowed;
                    while (_flowNarrowingSources.TryGetValue(root, out var previous))
                        root = previous;
                    return ReferenceEquals(root, variable);
                })
                .ToArray();
            foreach (var narrowed in descendants)
                _flowNarrowingSources.Remove(narrowed);
            return variable;
        }

        if (requireRebindable && !CanRebind(source))
        {
            return variable;
        }
        if (requireRebindable)
        {
            if (!_scope.TryReplaceVisible(variable, source))
                return variable;
        }
        else
        {
            _scope.ReplaceVisibleSymbols(
                variable.Name,
                new HashSet<VariableSymbol>(traversed, ReferenceEqualityComparer.Instance),
                source);
        }

        foreach (var narrowed in traversed)
            _flowNarrowingSources.Remove(narrowed);
        return source;
    }

    private BoundExpression BindNewExpression(NewExpressionNode newExpr)
    {
        var boundArgs = BindExpressions(newExpr.Arguments);
        var boundTypeReference = BindTypeReference(newExpr.TypeReference);
        var initializers = newExpr.Initializers
            .Select(initializer => new BoundObjectInitializer(
                initializer.Value.Span,
                initializer.PropertyName,
                BindExpression(initializer.Value)))
            .ToArray();
        if (string.IsNullOrWhiteSpace(newExpr.TypeName)
            || string.IsNullOrWhiteSpace(newExpr.TypeReference.Name))
        {
            if (!_diagnostics.Any(diagnostic =>
                    diagnostic.Code == DiagnosticCode.ExpectedTypeName
                    && diagnostic.Span.Start == newExpr.TypeNameSpan.Start
                    && diagnostic.Span.Length == newExpr.TypeNameSpan.Length))
            {
                _diagnostics.ReportError(
                    newExpr.TypeNameSpan,
                    DiagnosticCode.ExpectedTypeName,
                    "new requires a non-empty type name.");
            }

            return new BoundNewExpression(
                newExpr.Span,
                newExpr.TypeName,
                newExpr.TypeArguments,
                boundArgs,
                initializers,
                resolvedConstructor: null,
                resolvedType: null,
                typeNameSpan: newExpr.TypeNameSpan,
                resolvedConstructors: Array.Empty<FunctionSymbol>(),
                typeReference: boundTypeReference,
                argumentNames: newExpr.ArgumentNames);
        }

        var constructorTypeName = TypeIdentity.ToLookupName(
            newExpr.TypeReference.Name,
            newExpr.TypeReference.TypeArguments.Count);
        var resolution = ResolveCall(
            newExpr.Span,
            $"{constructorTypeName}..ctor",
            boundArgs,
            argumentNames: newExpr.ArgumentNames,
            argumentModifiers: null,
            typeArguments: null);

        var metadata = GetOrCreateMetadataBinder();
        var fullTypeName = newExpr.TypeArguments.Count == 0 ? newExpr.TypeName
            : $"{newExpr.TypeName}<{string.Join(",", newExpr.TypeArguments)}>";
        var metadataType = boundTypeReference.ResolvedType is null && metadata is not null
            ? ResolveComputedMetadataType(metadata.Context, fullTypeName) as Microsoft.CodeAnalysis.INamedTypeSymbol
            : null;
        var collectionDefinition = metadataType?.OriginalDefinition;
        if (newExpr.CollectionInitializers.Count > 0 && collectionDefinition is null
            && boundTypeReference.ResolvedType is null && metadata is not null
            && TypeIdentity.TrySplitGeneric(fullTypeName, out var root, out var typeArguments)
            && ResolveTypeSymbol(root, typeArguments.Count) is null)
        {
            var scoped = ResolveScopedMetadataType(metadata.Context, root, typeArguments.Count,
                _currentNamespaceScopeId, _currentNamespaceIdentity);
            collectionDefinition = scoped.Handled ? scoped.Type
                : metadata.Context.TryResolveType(TypeIdentity.MapShortTypeNameToFullName(fullTypeName));
        }
        if (newExpr.CollectionInitializers.Count > 0
            && boundTypeReference.ResolvedType is not null
            && (ResolveClass(fullTypeName) is not { } sourceClass
                || !IsEnumerableCollectionClass(sourceClass, new HashSet<ClassDefinitionNode>())))
            _diagnostics.ReportError(newExpr.TypeNameSpan, DiagnosticCode.TypeMismatch,
                $"Collection initializer type '{fullTypeName}' requires a resolved System.Collections.IEnumerable implementation.");
        var collectionCalls = new List<BoundCallExpression>();
        foreach (var entry in newExpr.CollectionInitializers)
        {
            var arguments = BindExpressions(entry.Arguments);
            var source = ResolveCall(entry.Span, entry.Target, arguments, null, null, null);
            // This side-band receiver describes the fresh instance, not the enclosing class.
            var receiver = new BoundThisExpression(newExpr.TypeNameSpan, fullTypeName,
                metadataType is null ? null : Metadata.MetadataBinderResult.ToNominalBoundType(metadataType));
            var selected = source.Functions.Count == 0
                ? TryResolveBclCall(entry.Target, arguments, null, null, computedReceiver: receiver)
                : null;
            var sourceListEntry = false;
            var sourceAddDefinition = collectionDefinition is not null && metadata is not null
                && Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
                    collectionDefinition, metadata.Context.TryResolveType("System.Collections.Generic.List`1"))
                ? collectionDefinition.GetMembers("Add")
                .OfType<Microsoft.CodeAnalysis.IMethodSymbol>().SingleOrDefault(method =>
                    !method.IsStatic && !method.IsGenericMethod && method.ReturnsVoid
                    && method.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public
                    && method.Parameters is [{ RefKind: Microsoft.CodeAnalysis.RefKind.None,
                        Type: Microsoft.CodeAnalysis.ITypeParameterSymbol { Ordinal: 0 } parameter }]
                    && Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
                        parameter.ContainingSymbol, collectionDefinition)) : null;
            if (selected is null && source.Functions.Count == 0
                && sourceAddDefinition is not null
                && TypeIdentity.TrySplitGeneric(fullTypeName, out _, out var listArguments)
                && listArguments.Count == 1
                && GetTypeParameterConstraints(listArguments[0]) is null
                && ResolveTypeSymbol(listArguments[0], 0) is { IsReferenceType: true } elementType)
            {
                sourceListEntry = arguments is [var argument]
                    && GetTypeParameterConstraints(argument.Type.DisplayString) is null
                    && (argument is BoundNewExpression constructed ? constructed.ResolvedType
                        : (argument.Type as BoundTypes.NominalBoundType)?.Declaration)?.Id == elementType.Id
                    && argument.Type is BoundTypes.NominalBoundType
                        { NullableAnnotation: BoundTypes.NullableAnnotation.NotAnnotated };
            }
            if (source.Functions.Count == 0 && selected is null && !sourceListEntry)
                _diagnostics.ReportError(entry.Span, DiagnosticCode.NoMatchingOverload,
                    $"Collection initializer cannot resolve an instance Add overload for '{fullTypeName}'.");
            if (metadataType is not null && !metadataType.AllInterfaces.Any(type =>
                    type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Collections_IEnumerable))
                _diagnostics.ReportError(newExpr.TypeNameSpan, DiagnosticCode.TypeMismatch,
                    $"Collection initializer type '{fullTypeName}' must implement System.Collections.IEnumerable.");
            var call = new BoundCallExpression(entry.Span, entry.Target, arguments,
                selected?.Symbol?.ReturnType.ToDisplayString() ?? source.Function?.ReturnType ?? "VOID",
                resolvedTypeName: selected?.Symbol?.ContainingType.ToDisplayString(),
                resolvedMethodName: selected?.Symbol?.Name,
                resolvedParameterTypes: selected?.Symbol?.Parameters.Select(parameter => parameter.Type.ToDisplayString()).ToArray(),
                resolvedSymbol: source.Function,
                receiver: receiver,
                resolvedSymbols: source.Functions,
                calleeSpan: TextSpan.Empty)
            {
                ResolvedMetadataMethod = selected?.Symbol,
                CollectionMethodDefinition = sourceListEntry ? sourceAddDefinition : null,
                MutatesOnlyFreshCollectionReceiver = source.Functions.Count == 0
                    && resolution.Functions.Count == 0
                    && newExpr.Arguments.Count == 0
                    && IsFreshBclCollectionAdd(metadata, metadataType ?? collectionDefinition,
                        selected?.Symbol ?? (sourceListEntry ? sourceAddDefinition : null))
            };
            collectionCalls.Add(call);
            InvalidateByReferenceParameterNarrowing();
        }
        return new BoundNewExpression(
            newExpr.Span,
            newExpr.TypeName,
            newExpr.TypeArguments,
            boundArgs,
            initializers,
            resolution.Function,
            boundTypeReference.ResolvedType,
            newExpr.TypeNameSpan,
            resolution.Functions,
            boundTypeReference,
            newExpr.ArgumentNames,
            metadataType,
            collectionCalls);
    }

    private static bool IsFreshBclCollectionAdd(
        Metadata.MetadataBinder? metadata,
        Microsoft.CodeAnalysis.INamedTypeSymbol? receiver,
        Microsoft.CodeAnalysis.IMethodSymbol? method)
    {
        if (metadata is null || receiver is null || method is null
            || method.IsStatic || method.IsGenericMethod
            || !receiver.InstanceConstructors.Any(constructor =>
                constructor.Parameters.IsEmpty
                && constructor.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public))
            return false;
        var symbols = Microsoft.CodeAnalysis.SymbolEqualityComparer.Default;
        var definition = receiver.OriginalDefinition;
        var list = metadata.Context.TryResolveType("System.Collections.Generic.List`1");
        var set = metadata.Context.TryResolveType("System.Collections.Generic.HashSet`1");
        var dictionary = metadata.Context.TryResolveType("System.Collections.Generic.Dictionary`2");
        var isList = symbols.Equals(definition, list);
        if (!isList && !symbols.Equals(definition, set) && !symbols.Equals(definition, dictionary))
            return false;
        if (!definition.GetMembers("Add").OfType<Microsoft.CodeAnalysis.IMethodSymbol>()
            .Any(candidate => symbols.Equals(candidate, method.OriginalDefinition)))
            return false;
        // Default equality for these sealed CLR primitives cannot dispatch to user callbacks.
        return isList || receiver.TypeArguments[0].SpecialType is
            Microsoft.CodeAnalysis.SpecialType.System_Boolean or Microsoft.CodeAnalysis.SpecialType.System_Char
            or Microsoft.CodeAnalysis.SpecialType.System_SByte or Microsoft.CodeAnalysis.SpecialType.System_Byte
            or Microsoft.CodeAnalysis.SpecialType.System_Int16 or Microsoft.CodeAnalysis.SpecialType.System_UInt16
            or Microsoft.CodeAnalysis.SpecialType.System_Int32 or Microsoft.CodeAnalysis.SpecialType.System_UInt32
            or Microsoft.CodeAnalysis.SpecialType.System_Int64 or Microsoft.CodeAnalysis.SpecialType.System_UInt64
            or Microsoft.CodeAnalysis.SpecialType.System_Single or Microsoft.CodeAnalysis.SpecialType.System_Double
            or Microsoft.CodeAnalysis.SpecialType.System_Decimal or Microsoft.CodeAnalysis.SpecialType.System_String;
    }

    private bool IsEnumerableCollectionClass(ClassDefinitionNode cls, HashSet<ClassDefinitionNode> visited)
    {
        if (!visited.Add(cls) || cls.TypeParameters.Count > 0 || GetOrCreateMetadataBinder() is not { } metadata)
            return false;
        var oldNamespace = _currentNamespaceIdentity;
        var oldScope = _currentNamespaceScopeId;
        _currentNamespaceIdentity = cls.NamespaceIdentity;
        _currentNamespaceScopeId = cls.NamespaceScopeId;
        try
        {
            foreach (var name in cls.ImplementedInterfaces.Concat(
                         cls.BaseClass is { } baseClass ? [baseClass] : Array.Empty<string>()))
            {
                if (ResolveComputedMetadataType(metadata.Context, name) is Microsoft.CodeAnalysis.INamedTypeSymbol type
                    && (type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Collections_IEnumerable
                        || type.AllInterfaces.Any(parent =>
                            parent.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Collections_IEnumerable)))
                    return true;
                if (ResolveClass(name) is { } parent && IsEnumerableCollectionClass(parent, visited))
                    return true;
            }
            return false;
        }
        finally
        {
            _currentNamespaceIdentity = oldNamespace;
            _currentNamespaceScopeId = oldScope;
        }
    }

    private BoundTypeReference BindTypeReference(TypeReferenceNode typeReference)
    {
        var typeArguments = typeReference.TypeArguments
            .Select(BindTypeReference)
            .ToArray();
        return new BoundTypeReference(
            typeReference.Name,
            typeReference.Span,
            string.IsNullOrEmpty(typeReference.Name)
                ? null
                : ResolveTypeSymbol(
                    typeReference.Name,
                    typeReference.TypeArguments.Count),
            typeArguments);
    }

    private BoundExpression BindArrayAccess(ArrayAccessNode arrayAccess)
    {
        var array = BindExpression(arrayAccess.Array);
        var index = BindExpression(arrayAccess.Index);
        if (index is BoundRangeExpression && GetOrCreateMetadataBinder() is { } sliceMetadata
            && ResolveComputedMetadataType(sliceMetadata.Context, array.Type.DisplayString) is { } slicedType
            && (slicedType is Microsoft.CodeAnalysis.IArrayTypeSymbol { Rank: 1 }
                || slicedType.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_String))
        {
            var inputArray = NullabilityChecker.GetMethodInputArrayType(array) is { } originalArray
                ? new BoundTypes.ArrayBoundType(originalArray.ElementType, originalArray.Rank,
                    BoundTypes.NullableAnnotation.NotAnnotated) : null;
            return new BoundArrayAccess(arrayAccess.Span, array, index, resolvedType:
                inputArray ?? Metadata.MetadataBinderResult.ToBoundType(slicedType.WithNullableAnnotation(
                    Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated)))
            {
                MethodInputArrayType = inputArray,
                MetadataArrayResultType = slicedType as Microsoft.CodeAnalysis.IArrayTypeSymbol
            };
        }
        var metadataArray = array is BoundVariableExpression { Variable.IsTypeInferred: false }
            && GetOrCreateMetadataBinder() is { } metadata
            && ResolveBclArgumentType(metadata.Context, array.Type.DisplayString)
                is Microsoft.CodeAnalysis.IArrayTypeSymbol { ElementType: Microsoft.CodeAnalysis.IArrayTypeSymbol element }
                ? element : null;
        var elementType = GetArrayAccessElementType(array);
        if (elementType is null && GetOrCreateMetadataBinder() is { } indexerMetadata
            && ResolveComputedMetadataType(indexerMetadata.Context, array.Type.DisplayString)
                is { SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String } receiverType
            && ResolveComputedMetadataType(indexerMetadata.Context, index.Type.DisplayString) is { } indexType
            && indexerMetadata.ResolveIndexer(receiverType, [new Metadata.MetadataArgument(indexType)])
                is { GetMethod: not null } property)
        {
            elementType = Metadata.MetadataBinderResult.ToBoundType(property.Type);
            metadataArray = property.Type as Microsoft.CodeAnalysis.IArrayTypeSymbol;
        }
        return new BoundArrayAccess(arrayAccess.Span, array, index, resolvedType: elementType)
        {
            MetadataArrayResultType = metadataArray
        };
    }

    private BoundTypes.BoundType? GetArrayAccessElementType(BoundExpression array)
    {
        if (array.HasUnknownInferredType)
            return null;
        if (NullabilityChecker.GetMethodInputArrayType(array) is { } establishedArray)
            return establishedArray.Rank == 1 ? establishedArray.ElementType : null;
        var typeName = array.Type.DisplayString;
        if (GetOrCreateMetadataBinder() is { } metadata
            && ResolveComputedMetadataType(metadata.Context, typeName)
                is Microsoft.CodeAnalysis.INamedTypeSymbol { SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String } text)
        {
            var stringIndexer = text.GetMembers().OfType<Microsoft.CodeAnalysis.IPropertySymbol>()
                .FirstOrDefault(property => property.IsIndexer && property.GetMethod is not null
                    && property.Parameters.Length == 1
                    && property.Parameters[0].Type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Int32
                    && property.Type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Char);
            if (stringIndexer?.Type is Microsoft.CodeAnalysis.INamedTypeSymbol character)
                // Preserve CLR identity instead of the C# display alias for effect resolution.
                return new BoundTypes.NominalBoundType("System.Char", roslynSymbol: character);
        }
        if (!TypeIdentity.TrySplitGeneric(typeName, out var name, out var arguments))
            return null;
        if (array.Type is BoundTypes.NominalBoundType { Declaration: not null }
            || ResolveTypeSymbol(name, arguments.Count) is not null)
            return null;

        var metadataName = name switch
        {
            "List" or "System.Collections.Generic.List" => "System.Collections.Generic.List`1",
            "IList" or "System.Collections.Generic.IList" => "System.Collections.Generic.IList`1",
            "IReadOnlyList" or "System.Collections.Generic.IReadOnlyList" => "System.Collections.Generic.IReadOnlyList`1",
            "Dictionary" or "Dict" or "System.Collections.Generic.Dictionary" => "System.Collections.Generic.Dictionary`2",
            "IDictionary" or "System.Collections.Generic.IDictionary" => "System.Collections.Generic.IDictionary`2",
            "IReadOnlyDictionary" or "System.Collections.Generic.IReadOnlyDictionary" => "System.Collections.Generic.IReadOnlyDictionary`2",
            _ => null
        };
        if (metadataName is null
            || GetOrCreateMetadataBinder()?.Context.TryResolveType(metadataName) is not { } definition
            || definition.Arity != arguments.Count
            || array.Type is BoundTypes.NominalBoundType { RoslynSymbol: { } actual }
                && !Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(actual.OriginalDefinition, definition))
            return null;

        var indexer = definition.GetMembers().OfType<Microsoft.CodeAnalysis.IPropertySymbol>()
            .FirstOrDefault(property => property.IsIndexer && property.GetMethod is not null);
        if (indexer?.Type is not Microsoft.CodeAnalysis.ITypeParameterSymbol parameter
            || parameter.Ordinal >= arguments.Count)
            return null;

        var generic = array.GenericNullabilityType ?? array.Type as BoundTypes.GenericInstantiationBoundType
            ?? (array as BoundFieldAccessExpression)?.ResolvedField?.GenericNullabilityType;
        if (generic?.TypeArguments.Length == arguments.Count)
            return generic.TypeArguments[parameter.Ordinal];
        if (array.Type is BoundTypes.NominalBoundType { RoslynSymbol: { } constructed })
            return Metadata.MetadataBinderResult.ToBoundType(constructed.TypeArguments[parameter.Ordinal]);

        var argument = arguments[parameter.Ordinal];
        return TryBuildReferenceType(argument, _currentClassName, _currentNamespaceIdentity,
                _currentMemberTypeParameters?.Select(typeParameter => typeParameter.Name))
            ?? BuildDeclaredBoundType(argument);
    }

    private BoundExpression BindMultiDimArrayAccess(MultiDimArrayAccessNode arrayAccess)
    {
        var array = BindExpression(arrayAccess.Array);
        var indices = BindExpressions(arrayAccess.Indices);
        return new BoundMultiDimArrayAccess(arrayAccess.Span, array, indices);
    }

    private BoundExpression BindArrayCreation(ArrayCreationNode array)
    {
        var size = array.Size == null ? null : BindExpression(array.Size);
        var initializer = BindExpressions(array.Initializer);
        return new BoundArrayCreation(
            array.Span,
            array.Id,
            array.Name,
            array.ElementType,
            size,
            initializer,
            array.Attributes,
            BuildCreatedArrayInputType(array.ElementType, 1, initializer));
    }

    private BoundExpression BindMultiDimArrayCreation(MultiDimArrayCreationNode array)
    {
        IReadOnlyList<BoundExpression> dimensions = BindExpressions(array.DimensionSizes);
        if (dimensions.Count == 0 && array.InitializerNesting is { } nesting)
        {
            var lengths = Enumerable.Repeat(-1, array.Rank).ToArray();
            var position = 0;
            if (ReadLevel(0) && position == nesting.Count)
                dimensions = lengths.Select(length => (BoundExpression)new BoundIntLiteral(
                    array.Span, Math.Max(length, 0))).ToArray();

            bool ReadLevel(int level)
            {
                if (position >= nesting.Count)
                    return false;
                var count = nesting[position++];
                if (count < 0 || lengths[level] >= 0 && lengths[level] != count)
                    return false;
                lengths[level] = count;
                if (level < array.Rank - 1)
                    for (var index = 0; index < count; index++)
                        if (!ReadLevel(level + 1))
                            return false;
                return true;
            }
        }
        var rows = array.Initializer
            .Select(row => (IReadOnlyList<BoundExpression>)BindExpressions(row))
            .ToArray();
        return new BoundMultiDimArrayCreation(
            array.Span,
            array.Id,
            array.Name,
            array.ElementType,
            array.Rank,
            dimensions,
            rows,
            BuildCreatedArrayInputType(array.ElementType, array.Rank, rows.SelectMany(row => row).ToArray()));
    }

    private BoundTypes.ArrayBoundType? BuildCreatedArrayInputType(
        string elementType,
        int rank,
        IReadOnlyList<BoundExpression> initializer)
    {
        if (!TryBuildMethodInputArrayType($"ARRAY[element={elementType}]", out var declared))
            return null;
        var element = declared!.ElementType;
        if (element is BoundTypes.NominalBoundType reference
            && (reference.IsKnownReferenceType || TypeIdentity.Canonicalize(reference.QualifiedName) == "STRING")
            && reference.NullableAnnotation != BoundTypes.NullableAnnotation.Annotated)
        {
            var annotations = initializer.Select(value => NullabilityChecker.GetAnnotation(value.Type)).ToArray();
            // Allocation guarantees the container, not initialization of every
            // slot. Retain declared element annotations for sized allocations;
            // explicit initializer values still contribute their annotations.
            var annotation = annotations.Contains(BoundTypes.NullableAnnotation.Annotated)
                ? BoundTypes.NullableAnnotation.Annotated
                : annotations.Any(value => value != BoundTypes.NullableAnnotation.NotAnnotated)
                    ? BoundTypes.NullableAnnotation.Oblivious
                    : reference.NullableAnnotation;
            element = new BoundTypes.NominalBoundType(
                reference.QualifiedName, annotation, reference.Declaration, reference.RoslynSymbol);
        }
        return new BoundTypes.ArrayBoundType(element, rank, BoundTypes.NullableAnnotation.NotAnnotated);
    }

    private BoundExpression BindArrayLength(ArrayLengthNode arrayLength)
    {
        var array = BindExpression(arrayLength.Array);
        return new BoundArrayLength(arrayLength.Span, array);
    }

    private BoundExpression BindListCreation(ListCreationNode list)
    {
        return new BoundListCreation(
            list.Span,
            list.Id,
            list.Name,
            list.ElementType,
            BindExpressions(list.Elements),
            list.Attributes);
    }

    private BoundExpression BindDictionaryCreation(DictionaryCreationNode dictionary)
    {
        return new BoundDictionaryCreation(
            dictionary.Span,
            dictionary.Id,
            dictionary.Name,
            dictionary.KeyType,
            dictionary.ValueType,
            dictionary.Entries
                .Select(entry => new BoundPair(
                    BindExpression(entry.Key),
                    BindExpression(entry.Value),
                    entry.Span))
                .ToArray(),
            dictionary.Attributes);
    }

    private BoundExpression BindSetCreation(SetCreationNode set)
    {
        return new BoundSetCreation(
            set.Span,
            set.Id,
            set.Name,
            set.ElementType,
            BindExpressions(set.Elements),
            set.Attributes);
    }

    private BoundExpression BindCollectionContains(CollectionContainsNode contains)
    {
        var collection = BindReferenceExpression(new ReferenceNode(contains.Span, contains.CollectionName, isVerbatim: true));
        var value = BindExpression(contains.KeyOrValue);
        return new BoundCollectionContains(
            contains.Span,
            contains.CollectionName,
            value,
            contains.Mode,
            collection);
    }

    private BoundExpression BindCollectionCount(CollectionCountNode count)
    {
        var collection = BindExpression(count.Collection);
        return new BoundCollectionCount(count.Span, collection);
    }

    private BoundExpression BindRecordCreation(RecordCreationNode record)
    {
        return new BoundRecordCreation(
            record.Span,
            record.TypeName,
            record.Fields
                .Select(field => new BoundNamedValue(
                    field.FieldName,
                    BindExpression(field.Value),
                    field.Span))
                .ToArray());
    }

    private BoundExpression BindAnonymousObjectCreation(AnonymousObjectCreationNode anonymous)
    {
        return new BoundAnonymousObjectCreation(
            anonymous.Span,
            anonymous.Initializers
                .Select(initializer => new BoundNamedValue(
                    initializer.PropertyName,
                    BindExpression(initializer.Value),
                    initializer.Value.Span))
                .ToArray());
    }

    private BoundExpression BindWithExpression(WithExpressionNode withExpression)
    {
        var target = BindExpression(withExpression.Target);
        return new BoundWithExpression(
            withExpression.Span,
            target,
            withExpression.Assignments
                .Select(assignment => new BoundNamedValue(
                    assignment.PropertyName,
                    BindExpression(assignment.Value),
                    assignment.Span))
                .ToArray());
    }

    private BoundExpression BindAwaitExpression(AwaitExpressionNode awaitExpression)
    {
        var awaited = BindExpression(awaitExpression.Awaited);
        var metadataType = awaited.Type switch
        {
            BoundTypes.NominalBoundType nominal => nominal.RoslynSymbol,
            BoundTypes.GenericInstantiationBoundType generic => generic.RoslynSymbol,
            _ => null
        };
        var awaiter = metadataType is not null ? GetOrCreateMetadataBinder()?.ResolveAwait(metadataType) : null;
        var resultType = awaiter?.GetResultMethod is { } getResult
            ? Metadata.MetadataBinderResult.ToBoundType(getResult.ReturnType)
            : metadataType is null ? GetAwaitedResultType(awaited.Type) : null;
        var bound = new BoundStructuralExpression(
            awaitExpression.Span,
            nameof(AwaitExpressionNode),
            resultType?.DisplayString ?? "OBJECT",
            [awaited],
            new Dictionary<string, object?>
            {
                ["ConfigureAwait"] = awaitExpression.ConfigureAwait,
                ["GetAwaiter"] = awaiter?.GetAwaiterMethod,
                ["IsCompleted"] = awaiter?.IsCompletedProperty,
                ["GetResult"] = awaiter?.GetResultMethod,
            },
            deferredChildren: null,
            typeAnnotation: BoundTypes.NullableAnnotation.Oblivious,
            resultType: resultType)
        {
            HasUnknownInferredType = resultType is null || awaited.HasUnknownInferredType
        };
        _boundAwaits[awaitExpression] = bound;
        return bound;
    }

    private BoundTypes.BoundType? GetAwaitedResultType(BoundTypes.BoundType awaitedType)
    {
        if (awaitedType is BoundTypes.GenericInstantiationBoundType
            {
                Definition.QualifiedName: var definition,
                TypeArguments.Length: 1
            } generic
            && Parsing.AttributeHelper.ResolveAsyncCarrierName(definition, _asyncTypeModule)
                is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
        {
            return generic.TypeArguments[0];
        }

        return Parsing.AttributeHelper.TryGetAsyncResultType(awaitedType.DisplayString, _asyncTypeModule, out var result)
            ? BuildDeclaredBoundType(result)
            : null;
    }

    private BoundExpression BindNullCoalesce(NullCoalesceNode coalesce)
    {
        var left = BindExpression(coalesce.Left);
        var right = BindExpression(coalesce.Right);
        var leftValueType = Parsing.AttributeHelper.TryUnwrapNullableAnnotation(left.Type.DisplayString, out var referent)
            ? NormalizeTypeName(referent) : left.Type.DisplayString;
        var rightValueType = Parsing.AttributeHelper.TryUnwrapNullableAnnotation(right.Type.DisplayString, out var rightReferent)
            ? NormalizeTypeName(rightReferent) : right.Type.DisplayString;
        var resultName = ExpressionResultTypes.IsNever(left.Type) ? "NEVER"
            : coalesce.Left is ReferenceNode { IsNullLiteral: true } ? right.Type.DisplayString
            : coalesce.Right is ReferenceNode { IsNullLiteral: true } ? leftValueType
            : GetCommonType(leftValueType, rightValueType);
        var shape = NullabilityResultTypes.Coalesce(left, right);
        return new BoundStructuralExpression(
            coalesce.Span,
            nameof(NullCoalesceNode),
            resultName,
            [left, right],
            metadata: null,
            deferredChildren: [right],
            typeAnnotation: BoundTypes.NullableAnnotation.Oblivious,
            resultType: shape ?? ExpressionResultTypes.Coalesce(resultName, left, right))
        {
            MethodInputArrayType = shape as BoundTypes.ArrayBoundType,
            GenericNullabilityType = shape as BoundTypes.GenericInstantiationBoundType,
            HasUnknownInferredType = !ExpressionResultTypes.IsNever(left.Type)
                && (left.HasUnknownInferredType || right.HasUnknownInferredType)
        };
    }

    private BoundExpression BindNullConditional(NullConditionalNode conditional)
    {
        var target = BindExpression(conditional.Target);
        return BindConditionalAccess(conditional.Span, target, receiver =>
            BindFieldAccess(new FieldAccessNode(conditional.Span,
                conditional.Target, conditional.MemberName), receiver));
    }

    private BoundExpression BindConditionalAccess(
        TextSpan span, BoundExpression target, Func<BoundExpression, BoundExpression> bindContinuation)
    {
        if (target is BoundConditionalAccessExpression outer)
            return ContinueConditionalAccess(span, outer,
                BindConditionalAccess(span, outer.WhenNotNull, bindContinuation));

        var type = GetConditionalReceiverType(ConditionalValueType(target));
        if (type is BoundTypes.GenericInstantiationBoundType
            { RoslynSymbol.OriginalDefinition.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_Nullable_T,
                TypeArguments: [var underlying] })
            type = underlying;
        else
            type = ConditionalReferenceType(type, BoundTypes.NullableAnnotation.NotAnnotated);
        // A placeholder is not another evaluation of the receiver. Only the outer node owns it.
        var receiver = new BoundStructuralExpression(span, "ConditionalReceiver", type.DisplayString,
            children: null, metadata: null, deferredChildren: null,
            BoundTypes.NullableAnnotation.Oblivious, type)
        {
            HasUnknownInferredType = target.HasUnknownInferredType,
            MethodInputArrayType = type as BoundTypes.ArrayBoundType,
            GenericNullabilityType = type as BoundTypes.GenericInstantiationBoundType
        };
        var continuation = bindContinuation(receiver);
        return new BoundConditionalAccessExpression(span, target, continuation,
            LiftConditionalResult(ConditionalValueType(continuation)));
    }

    private BoundConditionalAccessExpression ContinueConditionalAccess(
        TextSpan span, BoundConditionalAccessExpression conditional, BoundExpression continuation) =>
        new(span, conditional.Receiver, continuation, LiftConditionalResult(ConditionalValueType(continuation)));

    private static BoundTypes.BoundType ConditionalValueType(BoundExpression expression) =>
        (BoundTypes.BoundType?)NullabilityChecker.GetMethodInputArrayType(expression)
        ?? expression.GenericNullabilityType ?? expression.Type;

    private BoundTypes.BoundType LiftConditionalResult(BoundTypes.BoundType type)
    {
        type = GetConditionalReceiverType(type);
        var symbol = type switch
        {
            BoundTypes.NominalBoundType nominal => nominal.RoslynSymbol,
            BoundTypes.GenericInstantiationBoundType generic => generic.RoslynSymbol,
            _ => null
        };
        if (symbol is { IsValueType: true }
            && symbol.SpecialType != Microsoft.CodeAnalysis.SpecialType.System_Void
            && symbol.OriginalDefinition.SpecialType != Microsoft.CodeAnalysis.SpecialType.System_Nullable_T
            && GetOrCreateMetadataBinder()?.Context.TryResolveType("System.Nullable`1") is { } nullable)
            return Metadata.MetadataBinderResult.ToBoundType(nullable.Construct(symbol));
        return ConditionalReferenceType(type, BoundTypes.NullableAnnotation.Annotated);
    }

    private BoundTypes.BoundType GetConditionalReceiverType(BoundTypes.BoundType type)
    {
        if (type is BoundTypes.NominalBoundType { RoslynSymbol: null, Declaration: null } nominal
            && GetOrCreateMetadataBinder() is { } metadata
            && ResolveComputedMetadataType(metadata.Context, nominal.QualifiedName)
                is Microsoft.CodeAnalysis.INamedTypeSymbol symbol)
            return symbol.IsValueType || symbol.IsGenericType
                ? Metadata.MetadataBinderResult.ToBoundType(symbol)
                : new BoundTypes.NominalBoundType(nominal.QualifiedName, nominal.NullableAnnotation,
                    roslynSymbol: symbol);
        return type;
    }

    private static BoundTypes.BoundType ConditionalReferenceType(
        BoundTypes.BoundType type, BoundTypes.NullableAnnotation annotation) => type switch
    {
        BoundTypes.NominalBoundType nominal when nominal.IsKnownReferenceType
            || nominal.RoslynSymbol?.IsReferenceType == true =>
            new BoundTypes.NominalBoundType(nominal.QualifiedName, annotation,
                nominal.Declaration, nominal.RoslynSymbol),
        BoundTypes.ArrayBoundType array =>
            new BoundTypes.ArrayBoundType(array.ElementType, array.Rank, annotation)
            {
                RoslynSymbol = array.RoslynSymbol
            },
        BoundTypes.GenericInstantiationBoundType generic when generic.RoslynSymbol?.IsValueType != true =>
            new BoundTypes.GenericInstantiationBoundType(generic.Definition, generic.TypeArguments, annotation,
                generic.RoslynSymbol),
        _ => type
    };

    private BoundExpression BindRangeExpression(RangeExpressionNode range)
    {
        var children = new List<BoundExpression>(2);
        if (range.Start != null)
            children.Add(BindExpression(range.Start));
        if (range.End != null)
            children.Add(BindExpression(range.End));

        return new BoundRangeExpression(
            range.Span,
            range.Start == null ? null : children[0],
            range.End == null ? null : children[^1]);
    }

    private BoundExpression BindIndexFromEnd(IndexFromEndNode index)
    {
        var offset = BindExpression(index.Offset);
        return new BoundIndexFromEnd(index.Span, offset);
    }

    private BoundExpression BindTupleLiteral(TupleLiteralNode tuple)
    {
        var elements = BindExpressions(tuple.Elements);
        return new BoundTupleLiteral(tuple.Span, elements);
    }

    private BoundExpression BindTypeOf(TypeOfExpressionNode typeOf)
    {
        var symbol = GetOrCreateMetadataBinder()?.Context.TryResolveType("System.Type");
        BoundTypes.BoundType resultType;
        if (symbol is not null)
            resultType = new BoundTypes.NominalBoundType("System.Type",
                BoundTypes.NullableAnnotation.NotAnnotated, roslynSymbol: symbol);
        else
        {
            ReportAnalysisIncomplete(typeOf, "The runtime System.Type metadata could not be resolved.");
            resultType = new BoundTypes.UnresolvedBoundType("System.Type metadata unavailable", reported: true);
        }
        return new BoundStructuralExpression(
            typeOf.Span,
            nameof(TypeOfExpressionNode),
            "System.Type",
            children: null,
            metadata: new Dictionary<string, object?>
            {
                ["OperandType"] = typeOf.TypeName,
            },
            deferredChildren: null,
            typeAnnotation: BoundTypes.NullableAnnotation.NotAnnotated,
            resultType: resultType)
        {
            HasUnknownInferredType = symbol is null
        };
    }

    private BoundExpression BindGenericType(GenericTypeNode genericType)
    {
        return new BoundGenericTypeExpression(
            genericType.Span,
            genericType.TypeName,
            genericType.TypeArguments);
    }

    private BoundExpression BindSelfReference(SelfRefNode selfReference) =>
        Structural(selfReference, "OBJECT");

    private BoundExpression BindThrowExpression(ThrowExpressionNode throwExpression)
    {
        var exception = BindExpression(throwExpression.Exception);
        return new BoundThrowExpression(throwExpression.Span, exception);
    }

    private BoundExpression BindInterpolatedString(InterpolatedStringNode interpolated)
    {
        var parts = new List<BoundInterpolatedStringPart>(interpolated.Parts.Count);
        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case InterpolatedStringTextNode text:
                    parts.Add(new BoundInterpolatedStringPart(text.Span, text.Text, null));
                    break;
                case InterpolatedStringExpressionNode expression:
                    parts.Add(new BoundInterpolatedStringPart(
                        expression.Span,
                        null,
                        BindExpression(expression.Expression),
                        expression.FormatSpecifier,
                        expression.AlignmentClause));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown interpolated string part '{part.GetType().Name}'");
            }
        }

        return new BoundInterpolatedStringExpression(interpolated.Span, parts);
    }

    private BoundExpression BindLambdaExpression(
        LambdaExpressionNode lambda, BoundTypes.FunctionBoundType? targetType = null)
    {
        if (targetType is null && ResolveSourceCallConversions && _asyncTypeModule is not null)
        {
            SourceCallBindings();
            if (_sourceCallConversions.LambdaTargets.TryGetValue(lambda, out var target)
                && target.DelegateInvokeMethod is { } invoke)
                targetType = new BoundTypes.FunctionBoundType(
                    System.Collections.Immutable.ImmutableArray.CreateRange(
                        invoke.Parameters.Select(parameter => Metadata.MetadataBinderResult.ToBoundType(parameter.Type))),
                    Metadata.MetadataBinderResult.ToBoundType(invoke.ReturnType),
                    row: BoundTypes.EffectRow.Unknown);
        }
        var outerBindings = _scope.CaptureVisibleVariableBindings();
        var mutableArrayResults = outerBindings.Select(binding => binding.Symbol.StorageIdentity)
            .Distinct()
            .Where(variable => variable.IsMutable && variable.MetadataArrayResultType is not null)
            .Select(variable => (Variable: variable, Type: variable.MetadataArrayResultType))
            .ToArray();
        foreach (var (variable, _) in mutableArrayResults)
            variable.MetadataArrayResultType = null;
        var outerFlowNarrowings = _flowNarrowingSources.ToArray();
        // A rebindable captured array may change before the deferred body runs.
        foreach (var (narrowed, _) in outerFlowNarrowings)
        {
            if (narrowed.MethodInputArrayType is not null
                && narrowed.StorageIdentity is { IsField: false, IsProperty: false } storage
                && CanRebind(storage)
                && ReferenceEquals(_scope.Lookup(narrowed.Name), narrowed))
            {
                InvalidateMutableFlowNarrowing(narrowed, requireRebindable: false);
            }
        }
        var outerCallableState = CaptureCallableState();
        var outerCallableWrites = _deferredCallableWrites;
        _deferredCallableWrites = [];
        var parentContext = _declarationContext.IsNone ? _moduleSymbolId : _declarationContext;
        var lambdaIdentity = CreateDeclarationId(
            parentContext,
            "lambda",
            lambda.Id,
            $"snapshot:{_transientBindingId}:lambda:{lambda.Span.Start}");
        using var _scopeGuard = PushScope(_scope.CreateChild());
        using var _staticGuard = PushStaticContext(lambda.IsStatic);
        using var _identityGuard = PushDeclarationContext(lambdaIdentity);
        using var _returnGuard = PushReturnTypeContext(null);

        var parameters = new List<VariableSymbol>(lambda.Parameters.Count);
        foreach (var (parameter, index) in lambda.Parameters.Select((parameter, index) => (parameter, index)))
        {
            var contextual = parameter.TypeName is null && targetType is not null
                && index < targetType.ParameterTypes.Length ? targetType.ParameterTypes[index] : null;
            var symbol = CreateLocalVariable(
                parameter.Name,
                parameter.TypeName ?? contextual?.DisplayString ?? "OBJECT",
                isMutable: false,
                isParameter: true,
                ParameterModifier.None,
                parameter.IdentifierSpan,
                "parameter",
                // v0.14 nullability workstream (task #3) — inherit
                // declared STRING nullability on lambda parameters.
                // Scoped to STRING per §D6; non-STRING keeps Oblivious.
                // An untyped parameter takes the selected delegate's STRING
                // annotation (0.30: String instance receivers must be proven).
                nullableAnnotation: contextual is BoundTypes.NominalBoundType
                    {
                        QualifiedName: "STRING" or "string" or "str" or "System.String"
                    } contextualString
                    ? contextualString.NullableAnnotation
                    : TryReadDeclaredStringAnnotation(parameter.TypeName));
            if (!_scope.TryDeclare(symbol))
            {
                _diagnostics.ReportError(
                    parameter.Span,
                    DiagnosticCode.DuplicateDefinition,
                    $"Lambda parameter '{parameter.Name}' is already defined");
            }
            parameters.Add(symbol);
        }

        var expressionBody = lambda.ExpressionBody != null
            ? BindExpression(lambda.ExpressionBody)
            : null;
        var statementBody = lambda.StatementBody != null
            ? BindStatements(lambda.StatementBody)
            : null;
        var returnType = expressionBody?.Type.DisplayString
            ?? GetCommonType(CollectReturnTypes(statementBody))
            ?? "VOID";
        if (lambda.IsAsync)
            returnType = targetType?.ReturnType.DisplayString
                ?? (ReturnShape.IsVoidType(returnType)
                    ? "global::System.Threading.Tasks.Task"
                    : $"global::System.Threading.Tasks.Task<{returnType}>");
        var effects = lambda.Effects?.Effects
            .Select(effect => $"{effect.Key}:{effect.Value}")
            .ToArray()
            ?? Array.Empty<string>();

        // v0.15 E2 slice b, §5 — the §LAM's §E becomes the lambda's DECLARED
        // row (ρ_decl), which is what its type carries. Omitted leaves the type
        // Unknown here: §3.5 says an omitted lambda row is inferred from the
        // BODY, and the body's row (ρ_body) is computed by the effect pass, not
        // the binder — E3 owns both the inference and the ρ_body ⊑ ρ_decl check
        // (Calor0410). Unknown is the sound placeholder: it fits nothing and is
        // fitted by nothing, so nothing can pass a check on the strength of a
        // row slice b has not computed.
        //
        // The declaration boundary converts deliberately (§5): what leaves a
        // lambda that declares a row is Concrete(declared), never an Assumed row
        // carrying the body's reasons.
        var declaredRow = lambda.Effects == null
            ? BoundTypes.EffectRow.Unknown
            : BoundTypes.EffectRow.AtDeclarationBoundary(
                BindRow(lambda.Effects, binders: null));
        var outerSymbols = new HashSet<VariableSymbol>(
            outerBindings.Select(binding => binding.Symbol.StorageIdentity),
            ReferenceEqualityComparer.Instance);
        var deferredMutations = new HashSet<VariableSymbol>(
            GetDeferredMutations(expressionBody, statementBody)
                .Select(variable => variable.StorageIdentity)
                .Where(outerSymbols.Contains),
            ReferenceEqualityComparer.Instance);
        var callableEffects = new CallableMutationSummary
        {
            Mutations = deferredMutations.ToArray(),
            Alternatives = GetDeferredCallEffects(expressionBody, statementBody).ToArray(),
            Writes = _deferredCallableWrites.ToArray()
        };

        Scope.RestoreVisibleVariableBindings(outerBindings);
        _flowNarrowingSources.Clear();
        foreach (var (narrowed, source) in outerFlowNarrowings)
            _flowNarrowingSources[narrowed] = source;
        RestoreCallableState(outerCallableState);
        foreach (var (variable, type) in mutableArrayResults)
            variable.MetadataArrayResultType = type;
        _deferredCallableWrites = outerCallableWrites;

        return new BoundLambdaExpression(
            lambda.Span,
            lambda.Id,
            parameters,
            lambda.Effects,
            effects,
            lambda.Attributes,
            lambda.IsAsync,
            lambda.IsStatic,
            expressionBody,
            statementBody,
            returnType,
            // E1 slice 2b: hand the expression body's real BoundType to the
            // lambda's FunctionBoundType. A statement body has no single bound
            // expression, so the node falls back to the string it just computed.
            lambda.IsAsync ? BuildDeclaredBoundType(returnType) : expressionBody?.Type,
            declaredRow,
            deferredMutations)
        {
            CallableEffects = callableEffects
        };
    }

    private BoundExpression BindMatchExpression(MatchExpressionNode match)
    {
        var target = BindExpression(match.Target);
        var cases = BindMatchCases(match.Cases, target);
        var resultType = GetCommonType(
                cases.Where(matchCase => matchCase.Result != null)
                    .Select(matchCase => matchCase.Result!.Type.DisplayString))
            ?? "OBJECT";
        return new BoundMatchExpression(
            match.Span,
            match.Id,
            target,
            cases,
            match.Attributes,
            resultType);
    }

    private IReadOnlyList<BoundMatchCase> BindMatchCases(
        IReadOnlyList<MatchCaseNode> cases, BoundExpression target)
    {
        var baseline = CaptureFlowNarrowingState();
        var fallthrough = baseline;
        var branches = new List<FlowNarrowingState>();
        var canFallThrough = true;
        var boundCases = new List<BoundMatchCase>(cases.Count);
        foreach (var matchCase in cases)
        {
            RestoreFlowNarrowingState(fallthrough);
            FlowNarrowingState afterGuard;
            using (PushScope(_scope.CreateChild()))
            {
                var pattern = BindPattern(matchCase.Pattern, matchedValue: target);
                var guard = matchCase.Guard != null ? BindExpression(matchCase.Guard) : null;
                afterGuard = CaptureFlowNarrowingState();
                if (matchCase.Guard != null && guard != null)
                    DeclareSuccessfulConditionPatterns(matchCase.Guard, guard);
                var body = BindStatements(matchCase.Body);
                var result = body.LastOrDefault() is BoundReturnStatement { Expression: not null } returnStatement
                    ? returnStatement.Expression
                    : null;
                boundCases.Add(new BoundMatchCase(
                    matchCase.Span,
                    pattern,
                    matchCase.Pattern is WildcardPatternNode,
                    guard,
                    body,
                    result));
            }
            if (!canFallThrough)
                continue;

            branches.Add(CaptureFlowNarrowingState());
            RestoreFlowNarrowingState(afterGuard);
            var guardedFallthrough = CaptureFlowNarrowingState();
            // A pattern miss skips its guard; a false guard keeps its effects.
            // Neither path executes this arm's body, nor keeps pattern bindings.
            JoinFlowBranchStates(fallthrough, [fallthrough, guardedFallthrough]);
            fallthrough = CaptureFlowNarrowingState();
            canFallThrough = matchCase.Guard is not null || matchCase.Pattern switch
            {
                WildcardPatternNode or VarPatternNode => false,
                VariablePatternNode variable => variable.Name.Contains('.', StringComparison.Ordinal),
                _ => true
            };
        }
        if (canFallThrough)
            branches.Add(fallthrough);
        JoinFlowBranchStates(baseline, branches);
        return boundCases;
    }

    private BoundPattern BindPattern(
        PatternNode pattern, bool allowBindings = true, BoundExpression? matchedValue = null)
    {
        switch (pattern)
        {
            case WildcardPatternNode:
                return Pattern(pattern);
            case VariablePatternNode variable:
                if (allowBindings && !variable.Name.Contains('.', StringComparison.Ordinal))
                    DeclarePatternVariable(variable.IdentifierSpan, variable.Name,
                        matchedValue?.Type is BoundTypes.UnresolvedBoundType
                            ? "OBJECT" : matchedValue?.Type.DisplayString ?? "OBJECT",
                        isTypeInferred: true, inferredValue: matchedValue);
                return Pattern(
                    variable,
                    metadata: new Dictionary<string, object?>
                    {
                        ["Name"] = variable.Name,
                        ["IsConstant"] = variable.Name.Contains('.', StringComparison.Ordinal),
                    });
            case VarPatternNode variable:
                if (allowBindings && variable.Name != "_")
                    DeclarePatternVariable(variable.IdentifierSpan, variable.Name,
                        matchedValue?.Type is BoundTypes.UnresolvedBoundType
                            ? "OBJECT" : matchedValue?.Type.DisplayString ?? "OBJECT",
                        isTypeInferred: true, inferredValue: matchedValue);
                return Pattern(
                    variable,
                    metadata: new Dictionary<string, object?> { ["Name"] = variable.Name });
            case TypePatternNode typePattern:
                if (allowBindings && typePattern.BindingName != null)
                    DeclarePatternVariable(
                        typePattern.BindingSpan ?? typePattern.Span,
                        typePattern.BindingName,
                        typePattern.TypeName,
                        BoundTypes.NullableAnnotation.NotAnnotated);
                return Pattern(
                    typePattern,
                    metadata: new Dictionary<string, object?>
                    {
                        ["TypeName"] = typePattern.TypeName,
                        ["BindingName"] = typePattern.BindingName,
                    });
            case LiteralPatternNode literal:
                return Pattern(literal, expressions: [BindExpression(literal.Literal)]);
            case ConstantPatternNode constant:
                return Pattern(constant, expressions: [BindExpression(constant.Value)]);
            case RelationalPatternNode relational:
                return Pattern(
                    relational,
                    metadata: new Dictionary<string, object?> { ["Operator"] = relational.Operator },
                    expressions: [BindExpression(relational.Value)]);
            case PositionalPatternNode positional:
                return Pattern(
                    positional,
                    metadata: new Dictionary<string, object?> { ["TypeName"] = positional.TypeName },
                    patterns: positional.Patterns.Select(child => BindPattern(child, allowBindings)).ToArray());
            case PropertyPatternNode property:
                var propertyTarget = property.TypeName is { } typeName
                    ? new BoundStructuralExpression(property.Span, nameof(PropertyPatternNode), typeName,
                        children: matchedValue == null ? [] : [matchedValue])
                    : matchedValue;
                var memberValues = property.Matches.Select(match => propertyTarget == null ? null : BindFieldAccess(
                    new FieldAccessNode(match.Span, new ReferenceNode(match.Span, ""),
                        match.PropertyName), propertyTarget, isPatternMember: true)).ToArray();
                return Pattern(
                    property,
                    metadata: new Dictionary<string, object?>
                    {
                        ["TypeName"] = property.TypeName,
                        ["PropertyNames"] = property.Matches.Select(match => match.PropertyName).ToArray(),
                    },
                    expressions: memberValues.OfType<BoundExpression>().ToArray(),
                    patterns: property.Matches.Select((match, index) =>
                        BindPattern(match.Pattern, allowBindings, memberValues[index])).ToArray());
            case ListPatternNode list:
                var matchedArray = matchedValue?.MethodInputArrayType
                    ?? matchedValue?.Type as BoundTypes.ArrayBoundType;
                var elementValue = matchedArray is { Rank: 1 }
                    ? PatternValue(list.Span, matchedArray.ElementType)
                    : null;
                var listPatterns = list.Patterns.Select(child => BindPattern(child, allowBindings, elementValue)).ToList();
                if (list.SlicePattern != null)
                {
                    var sliceValue = matchedArray is { Rank: 1 }
                        ? PatternValue(list.SlicePattern.Span,
                            new BoundTypes.ArrayBoundType(matchedArray.ElementType,
                                nullableAnnotation: BoundTypes.NullableAnnotation.NotAnnotated)
                            { RoslynSymbol = matchedArray.RoslynSymbol })
                        : null;
                    listPatterns.Insert(Math.Min(list.SliceIndex, listPatterns.Count),
                        BindPattern(list.SlicePattern, allowBindings, sliceValue));
                }
                return Pattern(
                    list,
                    metadata: new Dictionary<string, object?>
                    {
                        ["SliceIndex"] = list.SliceIndex,
                        ["HasSlice"] = list.SlicePattern != null,
                    },
                    patterns: listPatterns);
            case NegatedPatternNode negated:
                return Pattern(negated, patterns: [BindPattern(negated.Inner, allowBindings: false)]);
            case OrPatternNode orPattern:
                return Pattern(orPattern, patterns: [BindPattern(orPattern.Left, allowBindings: false), BindPattern(orPattern.Right, allowBindings: false)]);
            case AndPatternNode andPattern:
                return Pattern(andPattern, patterns: [BindPattern(andPattern.Left, allowBindings, matchedValue),
                    BindPattern(andPattern.Right, allowBindings, andPattern.Left is TypePatternNode typed
                        ? new BoundStructuralExpression(typed.Span, nameof(TypePatternNode), typed.TypeName,
                            children: matchedValue == null ? [] : [matchedValue]) : matchedValue)]);
            default:
                return Pattern(
                    pattern,
                    metadata: new Dictionary<string, object?>
                    {
                        ["AnalysisIncomplete"] = true,
                    });
        }

        static BoundExpression PatternValue(Parsing.TextSpan span, BoundTypes.BoundType type) =>
            new BoundStructuralExpression(span, nameof(ListPatternNode), type.DisplayString,
                children: null, metadata: null, deferredChildren: null,
                typeAnnotation: BoundTypes.NullableAnnotation.Oblivious, resultType: type);
    }

    private void DeclarePatternVariable(Parsing.TextSpan span, string name, string typeName,
        BoundTypes.NullableAnnotation nullableAnnotation = BoundTypes.NullableAnnotation.Oblivious,
        VariableSymbol? binding = null,
        bool isTypeInferred = false,
        BoundExpression? inferredValue = null)
    {
        if (_scope.LookupLocal(name) is VariableSymbol existing
            && string.Equals(existing.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
            return;

        var inferredReferenceType = isTypeInferred ? GetInferredReferenceType(inferredValue) : null;
        var symbol = binding ?? CreateLocalVariable(
            name,
            typeName,
            isMutable: false,
            isParameter: false,
            ParameterModifier.None,
            span,
            "pattern",
            nullableAnnotation: isTypeInferred
                ? inferredReferenceType?.NullableAnnotation
                    ?? InferAnnotationForStringBinding(null, typeName, inferredValue)
                : nullableAnnotation,
            isTypeInferred: isTypeInferred,
            inferredReferenceType: inferredReferenceType,
            functionType: isTypeInferred ? inferredValue?.Type as BoundTypes.FunctionBoundType : null,
            methodInputArrayType: isTypeInferred ? inferredValue?.Type as BoundTypes.ArrayBoundType : null,
            genericNullabilityType: isTypeInferred ? inferredValue?.Type as BoundTypes.GenericInstantiationBoundType : null,
            hasUnknownInferredType: isTypeInferred && (inferredValue is null
                || inferredValue.HasUnknownInferredType
                || inferredValue.Type is BoundTypes.UnresolvedBoundType
                || inferredValue is BoundVariableExpression { Variable.Id.IsNone: true }));
        if (!_scope.TryDeclare(symbol))
        {
            _diagnostics.ReportError(
                span,
                DiagnosticCode.DuplicateDefinition,
                $"Pattern variable '{name}' is already defined");
        }
    }

    private static BoundPattern Pattern(
        PatternNode pattern,
        IReadOnlyDictionary<string, object?>? metadata = null,
        IReadOnlyList<BoundPattern>? patterns = null,
        IReadOnlyList<BoundExpression>? expressions = null) =>
        new(pattern.Span, pattern.GetType().Name, metadata, patterns, expressions);

    private BoundExpression BindForallExpression(ForallExpressionNode forall) =>
        BindQuantifier(forall, forall.BoundVariables, forall.Body);

    private BoundExpression BindExistsExpression(ExistsExpressionNode exists) =>
        BindQuantifier(exists, exists.BoundVariables, exists.Body);

    private BoundExpression BindQuantifier(
        ExpressionNode quantifier,
        IReadOnlyList<QuantifierVariableNode> variables,
        ExpressionNode body)
    {
        using var _ = PushScope(_scope.CreateChild());
        var symbols = new List<VariableSymbol>(variables.Count);
        foreach (var variable in variables)
        {
            var symbol = CreateLocalVariable(
                variable.Name,
                variable.TypeName,
                isMutable: false,
                isParameter: true,
                ParameterModifier.None,
                variable.IdentifierSpan,
                "quantifier");
            if (!_scope.TryDeclare(symbol))
            {
                _diagnostics.ReportError(
                    variable.Span,
                    DiagnosticCode.DuplicateDefinition,
                    $"Quantifier variable '{variable.Name}' is already defined");
            }
            symbols.Add(symbol);
        }

        return new BoundQuantifierExpression(
            quantifier.Span,
            quantifier.GetType().Name,
            symbols,
            BindExpression(body));
    }

    private BoundExpression BindImplicationExpression(ImplicationExpressionNode implication)
    {
        var antecedent = BindExpression(implication.Antecedent);
        var consequent = BindExpression(implication.Consequent);
        return Structural(
            implication,
            "BOOL",
            [antecedent, consequent],
            deferredChildren: [consequent]);
    }

    private BoundExpression BindStringOperation(StringOperationNode operation)
    {
        var arguments = BindExpressions(operation.Arguments);
        var resultType = operation.Operation switch
        {
            StringOp.Length or StringOp.IndexOf => "INT",
            StringOp.Contains or StringOp.StartsWith or StringOp.EndsWith
                or StringOp.IsNullOrEmpty or StringOp.IsNullOrWhiteSpace
                or StringOp.Equals or StringOp.RegexTest => "BOOL",
            StringOp.Split or StringOp.RegexSplit => "str[]",
            StringOp.RegexMatch => "OBJECT",
            _ => "STRING",
        };
        // v0.14 nullability (#1056, #1061): the STRING-returning string
        // ops split into two soundness buckets:
        //   * Non-null per BCL contract — Substring, SubstringFrom, Trim,
        //     TrimStart, TrimEnd, ToUpper, ToLower, Replace, PadLeft,
        //     PadRight, Format, Join, Concat, RegexReplace — stamped
        //     NotAnnotated.
        //   * StringOp.ToString maps to object.ToString(), which the
        //     modern BCL annotates as string? because overrides may
        //     return null. Stamp Annotated so downstream flow requires
        //     an explicit null-check before non-null uses.
        var stringAnnotation = operation.Operation == StringOp.ToString
            && (arguments.Count == 0 || !IsDefinitelyNonNullScalar(arguments[0]))
                ? BoundTypes.NullableAnnotation.Annotated
                : BoundTypes.NullableAnnotation.NotAnnotated;
        return Structural(
            operation,
            resultType,
            arguments,
            new Dictionary<string, object?>
            {
                ["Operation"] = operation.Operation,
                ["ComparisonMode"] = operation.ComparisonMode,
            },
            typeAnnotation: resultType == "STRING"
                ? stringAnnotation
                : BoundTypes.NullableAnnotation.Oblivious);
    }

    private static bool IsDefinitelyNonNullScalar(BoundExpression argument)
    {
        if (IsBuiltInValueTypeName(TypeIdentity.Canonicalize(argument.Type.DisplayString)))
            return true;

        return argument.Type is BoundTypes.NominalBoundType nominal
            && IsScalarStringTypeName(nominal.QualifiedName)
            && nominal.NullableAnnotation == BoundTypes.NullableAnnotation.NotAnnotated;
    }

    private BoundExpression BindCharOperation(CharOperationNode operation)
    {
        var resultType = operation.Operation switch
        {
            CharOp.CharCode => "INT",
            CharOp.IsLetter or CharOp.IsDigit or CharOp.IsWhiteSpace
                or CharOp.IsUpper or CharOp.IsLower => "BOOL",
            _ => "CHAR",
        };
        return Structural(
            operation,
            resultType,
            BindExpressions(operation.Arguments),
            new Dictionary<string, object?> { ["Operation"] = operation.Operation });
    }

    private BoundExpression BindStringBuilderOperation(StringBuilderOperationNode operation)
    {
        var resultType = operation.Operation switch
        {
            StringBuilderOp.ToString => "STRING",
            StringBuilderOp.Length => "INT",
            _ => "StringBuilder",
        };
        return Structural(
            operation,
            resultType,
            BindExpressions(operation.Arguments),
            new Dictionary<string, object?> { ["Operation"] = operation.Operation },
            // v0.14 nullability (#1056): StringBuilder.ToString() returns
            // non-null per BCL contract.
            typeAnnotation: resultType == "STRING"
                ? BoundTypes.NullableAnnotation.NotAnnotated
                : BoundTypes.NullableAnnotation.Oblivious);
    }

    private BoundExpression BindStackAlloc(StackAllocNode stackAlloc)
    {
        var children = new List<BoundExpression>();
        if (stackAlloc.Size != null)
            children.Add(BindExpression(stackAlloc.Size));
        children.AddRange(BindExpressions(stackAlloc.Initializer));
        return Structural(
            stackAlloc,
            $"Span<{stackAlloc.ElementType}>",
            children,
            new Dictionary<string, object?>
            {
                ["ElementType"] = stackAlloc.ElementType,
                ["HasSize"] = stackAlloc.Size != null,
                ["InitializerCount"] = stackAlloc.Initializer.Count,
            });
    }

    private BoundExpression BindAddressOf(AddressOfNode addressOf)
    {
        var operand = BindExpression(addressOf.Operand);
        return Structural(addressOf, $"{operand.Type.DisplayString}*", [operand]);
    }

    private BoundExpression BindPointerDereference(PointerDereferenceNode dereference)
    {
        var operand = BindExpression(dereference.Operand);
        var operandTypeName = operand.Type.DisplayString;
        var resultType = operandTypeName.EndsWith('*')
            ? operandTypeName[..^1]
            : "OBJECT";
        return Structural(dereference, resultType, [operand]);
    }

    private BoundExpression BindSizeOf(SizeOfNode sizeOf)
    {
        return Structural(
            sizeOf,
            "INT",
            metadata: new Dictionary<string, object?> { ["OperandType"] = sizeOf.TypeName });
    }

    private BoundExpression BindExpressionCall(ExpressionCallNode call)
    {
        if (call.TargetExpression is NullConditionalNode conditional)
            return BindConditionalAccess(call.Span, BindExpression(conditional.Target), receiver =>
                BindCallExpression(new CallExpressionNode(call.Span,
                    "<expression-call>", call.Arguments, call.ArgumentNames, call.ArgumentModifiers,
                    call.TypeArguments, conditional.Span, conditional.Target.Span),
                    receiver, conditional.MemberName));
        if (call.TargetExpression is FieldAccessNode member)
        {
            if (TryBindConstructedSourceCall(call, member) is { } constructedCall)
                return constructedCall;
            if (TryBindExternalSourceCall(call, member) is { } sourceCall)
                return sourceCall;
            var sourceReceiver = DottedReferenceName(member.Target);
            var sourceTarget = sourceReceiver is null ? null : $"{sourceReceiver}.{member.FieldName}";
            BoundExpression? receiver = TryBindPredefinedTypeReceiver(member.Target);
            if (receiver is null && DottedReferenceName(member.Target) is { } receiverName
                && !IsTypeReceiverHeadShadowed(receiverName.Split('.')[0], allowDeclaredTypes: true)
                && ResolveSourceType(receiverName, _currentClassName,
                    _currentNamespaceIdentity, _currentNamespaceScopeId).Type is { } typeSymbol)
                receiver = new BoundTypeReferenceExpression(member.Target.Span,
                    receiverName,
                    new BoundTypes.NominalBoundType(typeSymbol.QualifiedName, declaration: typeSymbol), typeSymbol);
            receiver ??= TryBindMetadataTypeReceiver(member.Target);
            if (receiver is null && sourceReceiver is not null && sourceTarget is not null
                && !IsTypeReceiverHeadShadowed(sourceReceiver.Split('.')[0], allowDeclaredTypes: false)
                && !HasVisibleUsingAlias(sourceReceiver.Split('.')[0])
                && CrossModuleFunctionModules?.TryGetValue(sourceTarget, out var moduleTarget) == true)
            {
                receiver = new BoundTypeReferenceExpression(member.Target.Span, sourceReceiver,
                    new BoundTypes.NominalBoundType(moduleTarget.QualifiedClassName));
                CrossModuleCallTargets[call.Span] = sourceTarget;
                return BindCallExpression(new CallExpressionNode(call.Span,
                    sourceTarget, call.Arguments, call.ArgumentNames, call.ArgumentModifiers,
                    call.TypeArguments, member.FieldNameSpan, member.Target.Span), receiver, member.FieldName);
            }

            receiver ??= BindExpression(member.Target);
            if (TryGetEventStorage(member, out _, out _))
                return BindFunctionValueCall(call, BindFieldAccess(member, receiver));
            if (receiver is BoundVariableExpression { Variable.Id.IsNone: true } unresolved)
                receiver = new BoundVariableExpression(unresolved.Span, unresolved.Variable,
                    typeOverride: new BoundTypes.UnresolvedBoundType(
                        $"Undefined receiver '{sourceReceiver}'", reported: true));
            else if (receiver is BoundVariableExpression { HasUnknownInferredType: true } inferred
                && sourceTarget is not null)
                receiver = BuildCallReceiver(sourceTarget, member.Target.Span, inferred.Variable, null, null);
            if (receiver is BoundConditionalAccessExpression conditionalReceiver)
                return ContinueConditionalAccess(call.Span, conditionalReceiver,
                    BindCallExpression(new CallExpressionNode(call.Span,
                        "<expression-call>", call.Arguments, call.ArgumentNames, call.ArgumentModifiers,
                        call.TypeArguments, member.FieldNameSpan, member.Target.Span),
                        conditionalReceiver.WhenNotNull, member.FieldName));
            return BindCallExpression(new CallExpressionNode(call.Span,
                "<expression-call>", call.Arguments, call.ArgumentNames, call.ArgumentModifiers,
                call.TypeArguments, member.FieldNameSpan, member.Target.Span), receiver, member.FieldName, call);
        }
        return BindFunctionValueCall(call, BindExpression(call.TargetExpression));
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_sourceCallConversions))]
    private CallGraphAnalysis.OperatorBindings SourceCallBindings() =>
        _sourceCallConversions ??= CallGraphAnalysis.ResolveOperatorCalls(
            _asyncTypeModule!, _diagnostics, ProjectionContractMode,
            crossModuleTypes: CrossModuleTypes, sourcePath: _sourcePath);

    private BoundCallExpression? TryBindConstructedSourceCall(ExpressionCallNode call, FieldAccessNode member)
    {
        if (!ResolveSourceCallConversions || _asyncTypeModule is null
            || DottedReferenceName(member.Target) is not { } receiverName
            || !receiverName.Contains('<') && !HasVisibleUsingAlias(receiverName.Split('.')[0]))
            return null;
        var bindings = SourceCallBindings();
        if (!bindings.SelectedCalls.TryGetValue(call.Span, out var selected)
            || !selected.HasCompleteProofContext
            || selected.Method is not
                { IsStatic: true, MethodKind: Microsoft.CodeAnalysis.MethodKind.Ordinary,
                    ContainingType.IsGenericType: true } method
            || selected.IncludesExtensionReceiver
            || selected.Arguments.Count != call.Arguments.Count
            || !selected.Arguments.Select(mapping => mapping.ArgumentIndex).SequenceEqual(
                Enumerable.Range(0, call.Arguments.Count))
            || !_functionSymbols.TryGetValue(selected.Declaration, out var function)
            || !_staticMethodSymbols.Contains(function) || !IsFunctionAccessible(function)
            || function.ContainingTypeName is not { } ownerName
            || !_classesByQualifiedName.TryGetValue(ownerName, out var owner))
            return null;

        // The image selects the declaration and its construction together; a
        // stripped receiver spelling cannot establish either identity.
        var receiver = new BoundTypeReferenceExpression(member.Target.Span,
            receiverName,
            new BoundTypes.NominalBoundType(method.ContainingType.ToDisplayString(),
                Metadata.MetadataBinderResult.MapAnnotation(method.ContainingType.NullableAnnotation),
                _classSymbols[owner], method.ContainingType), _classSymbols[owner]);
        return BindCallExpression(new CallExpressionNode(call.Span, "<expression-call>",
            call.Arguments, call.ArgumentNames, call.ArgumentModifiers, call.TypeArguments,
            member.FieldNameSpan, member.Target.Span), receiver, member.FieldName, call,
            (selected, function));
    }

    private BoundCallExpression? TryBindExternalSourceCall(ExpressionCallNode call, FieldAccessNode member)
    {
        if (CrossModuleTypes is null || !ResolveSourceCallConversions || _asyncTypeModule is null)
            return null;
        var bindings = SourceCallBindings();
        bindings.SelectedCalls.TryGetValue(call.Span, out var selected);
        if (selected is not null && _functionSymbols.ContainsKey(selected.Declaration))
            return null;
        if (selected is null
            || selected.Method is not { IsStatic: true, MethodKind: Microsoft.CodeAnalysis.MethodKind.Ordinary } method
            || method.ContainingType.StaticConstructors.Length != 0
            || selected.IncludesExtensionReceiver
            || CrossModuleTypes.FindCallable(selected.Declaration) is not { } callable)
        {
            if (!bindings.ForeignSourceCallSites.Contains(call.Span))
                return null;
            var evaluatedReceiver = BindExpression(member.Target);
            var arguments = BindCallArguments(call.Arguments, call.ArgumentModifiers);
            InvalidateMutatingCallArguments(arguments, call.ArgumentModifiers, OverloadResolutionResult.NotFound(), null);
            return new BoundCallExpression(call.Span, "<expression-call>", arguments, "OBJECT",
                argumentNames: call.ArgumentNames, argumentModifiers: call.ArgumentModifiers,
                typeArguments: call.TypeArguments, calleeSpan: member.FieldNameSpan,
                receiverSpan: member.Target.Span, receiver: evaluatedReceiver)
            {
                EvaluatedReceiver = evaluatedReceiver,
                HasOpaqueOverloads = true,
                HasUnknownInferredType = true
            };
        }
        var receiver = new BoundTypeReferenceExpression(member.Target.Span,
            DottedReferenceName(member.Target) ?? method.ContainingType.ToDisplayString(),
            Metadata.MetadataBinderResult.ToNominalBoundType(method.ContainingType), callable.OwnerSymbol);
        var bound = BindCallExpression(new CallExpressionNode(call.Span, "<expression-call>",
            call.Arguments, call.ArgumentNames, call.ArgumentModifiers, call.TypeArguments,
            member.FieldNameSpan, member.Target.Span), receiver, member.FieldName, call,
            (selected, callable.Symbol));
        if (bound.ResolvedSymbol == callable.Symbol)
            ExternalSourceCalls[callable.Symbol.Id] = callable;
        return bound;
    }

    private BoundExpression BindFunctionValueCall(ExpressionCallNode call, BoundExpression target)
    {
        var arguments = BindCallArguments(call.Arguments, call.ArgumentModifiers);
        var resultType = target.Type is BoundTypes.FunctionBoundType function
            ? function.ReturnType
            : target is BoundCallExpression { ResolvedSymbol.ReturnFunctionType: { } returnedFunction }
                ? returnedFunction.ReturnType
                : (target as BoundVariableExpression)?.Variable.FunctionType?.ReturnType;
        var observed = InvalidateDeferredCallTarget(target);
        InvalidateMutatingCallArguments(arguments, call.ArgumentModifiers, OverloadResolutionResult.NotFound(), null);
        return new BoundExpressionCall(call.Span, target, arguments, resultType,
            call.ArgumentNames, call.ArgumentModifiers, call.TypeArguments)
        {
            ObservedCallableEffects = observed,
            HasUnknownInferredType = resultType is null
        };
    }

    private BoundExpression BindFallbackInterop(FallbackExpressionNode fallback)
    {
        ReportAnalysisIncomplete(fallback, $"Interop fallback feature '{fallback.FeatureName}'");
        return new BoundInteropExpression(
            fallback.Span,
            nameof(FallbackExpressionNode),
            fallback.OriginalCSharp,
            "OBJECT",
            metadata: new Dictionary<string, object?>
            {
                ["FeatureName"] = fallback.FeatureName,
                ["Suggestion"] = fallback.Suggestion,
            })
        {
            HasUnknownInferredType = true
        };
    }

    private BoundExpression BindRawCSharpInterop(RawCSharpExpressionNode raw)
    {
        ReportAnalysisIncomplete(raw, "Raw C# expression is opaque to Calor analysis");
        return new BoundInteropExpression(
            raw.Span,
            nameof(RawCSharpExpressionNode),
            raw.CSharpCode,
            "OBJECT",
            metadata: new Dictionary<string, object?>
            {
                ["CSharpCode"] = raw.CSharpCode,
            })
        {
            HasUnknownInferredType = true
        };
    }

    private BoundExpression BindReferenceExpression(ReferenceNode refNode)
    {
        if (refNode.IsNullLiteral)
            return new BoundNullLiteral(refNode.Span);

        if (TryGetEventStorage(refNode, out var eventStorage, out var eventType))
            return new BoundVariableExpression(refNode.Span, eventStorage, typeOverride: eventType);

        if (TryBindSourceStaticFieldReference(refNode) is { } sourceField)
            return sourceField;

        var symbols = _scope.LookupAll(refNode.Name);
        if (symbols.Count == 0 || symbols.All(symbol => symbol is TypeSymbol))
        {
            var lexicalMembers = ResolveLexicalMembers(refNode.Name);
            if (lexicalMembers.Count != 0)
                symbols = lexicalMembers;
        }
        // Module functions in a namespaced module are declared under their qualified
        // name. A bare reference to one is still a function used as a value: report it
        // as such (with the call fix), not as an undefined variable.
        if (symbols.Count == 0 && !refNode.Name.Contains('.')
            && !string.IsNullOrEmpty(_currentNamespaceIdentity)
            && _scope.LookupAll($"{_currentNamespaceIdentity}.{refNode.Name}") is { Count: > 0 } namespaced
            && namespaced.All(symbol => symbol is FunctionSymbol))
            symbols = namespaced;

        if (symbols.Count == 0)
        {
            BoundExpression? unresolvedMember = null;
            var receiverReportedError = false;
            // The lexer also accepts a dotted reference as one token. Route the
            // resolved external form through the same member binder as explicit dots.
            var lastDot = refNode.Name.LastIndexOf('.');
            if (lastDot > 0)
            {
                var receiverName = refNode.Name[..lastDot];
                var receiverSpan = new Parsing.TextSpan(
                    refNode.Span.Start, lastDot, refNode.Span.Line, refNode.Span.Column);
                var receiverNode = new ReferenceNode(receiverSpan, receiverName, refNode.IsVerbatim);
                BoundExpression? receiver = TryBindSourceEnumTypeReceiver(receiverNode)
                    ?? TryBindMetadataTypeReceiver(receiverNode)
                    ?? TryBindSourceStaticFieldReference(receiverNode);
                // As in C#, a variable in scope wins simple-name lookup over a namespace or
                // type, so `x.F` and `x.F.G` read members of x exactly like `x . F . G`.
                var variableRoot = false;
                if (receiver is null && (_scope.Lookup(receiverName) is VariableSymbol
                    || _scope.Lookup(receiverName.Split('.')[0]) is VariableSymbol))
                {
                    var errorsBefore = _diagnostics.Errors.Count;
                    receiver = BindReferenceExpression(receiverNode);
                    variableRoot = true;
                    // A dotted receiver already reported its own undefined prefix.
                    receiverReportedError = receiverName.Contains('.') && _diagnostics.Errors.Count != errorsBefore;
                }
                // The member read binds exactly as the spaced form does. On a user type the
                // member must exist (a declared field or property), or the undefined-name
                // report below still applies; past an unknown-typed member read in the
                // chain, the spaced form's binding is all there is to know.
                var memberName = refNode.Name[(lastDot + 1)..];
                ClassDefinitionNode? receiverClass = null;
                if (variableRoot && !receiverReportedError
                    && (receiver is BoundVariableExpression or BoundFieldAccessExpression
                            && receiver.Type is BoundTypes.NominalBoundType { Declaration: { } receiverType, RoslynSymbol: null }
                            && _classesByQualifiedName.TryGetValue(receiverType.QualifiedName, out receiverClass)
                        || receiver is BoundFieldAccessExpression
                            {
                                HasUnknownInferredType: true, ResolvedField: null, IsAnonymousPropertyRead: false
                            } and not { Type: BoundTypes.UnresolvedBoundType }))
                {
                    var memberSpan = new Parsing.TextSpan(
                        refNode.Span.Start + lastDot + 1, refNode.Name.Length - lastDot - 1,
                        refNode.Span.Line, refNode.Span.Column + lastDot + 1);
                    var member = BindFieldAccess(new FieldAccessNode(
                        refNode.Span, receiverNode, memberName, memberSpan), receiver);
                    if (receiverClass is null
                            ? member.Type is not BoundTypes.UnresolvedBoundType
                            : ResolveAccessibleMembers(receiverClass, memberName).Any(symbol => symbol is VariableSymbol))
                        return member;
                    unresolvedMember = member;
                }
                else if (!receiverReportedError && (receiver is BoundTypeReferenceExpression { ResolvedType.IsEnum: true }
                    || receiver is BoundVariableExpression tupleVariable
                        && tupleVariable.Variable.TypeName.StartsWith('(')
                        && SyntaxFactory.ParseTypeName(tupleVariable.Variable.TypeName)
                            is TupleTypeSyntax { ContainsDiagnostics: false }
                    || receiver?.Type is BoundTypes.NominalBoundType { RoslynSymbol: not null }
                    || receiver?.AnonymousObjectMembers is not null
                    || receiver is BoundFieldAccessExpression { IsAnonymousPropertyRead: true }
                    || receiver is not null && ResolveMetadataArrayProperty(receiver, refNode.Name[(lastDot + 1)..]) is not null
                    || receiver is not null && ResolveMetadataScalarArrayProperty(receiver, refNode.Name[(lastDot + 1)..]) is not null
                    || refNode.Name[(lastDot + 1)..] == "Length"
                        && receiver is not null && (IsArrayReceiver(receiver)
                            || receiver is BoundVariableExpression { Variable.MetadataArrayResultType: not null })))
                {
                    var memberSpan = new Parsing.TextSpan(
                        refNode.Span.Start + lastDot + 1, refNode.Name.Length - lastDot - 1,
                        refNode.Span.Line, refNode.Span.Column + lastDot + 1);
                    var member = BindFieldAccess(new FieldAccessNode(
                        refNode.Span, receiverNode, refNode.Name[(lastDot + 1)..], memberSpan), receiver);
                    if (member.Type is not BoundTypes.UnresolvedBoundType)
                        return member;
                    unresolvedMember = member;
                }
            }

            var similarName = _scope.FindSimilarName(refNode.Name);
            if (receiverReportedError)
            {
                // The receiver prefix already carries the undefined-name report.
            }
            else if (similarName != null)
            {
                // Create a fix to replace the undefined reference with the similar name
                var fix = new SuggestedFix(
                    $"Change to '{similarName}'",
                    TextEdit.Replace(
                        "", // File path will be set from DiagnosticBag._currentFilePath
                        refNode.Span.Line,
                        refNode.Span.Column,
                        refNode.Span.Line,
                        refNode.Span.Column + refNode.Name.Length,
                        similarName));

                _diagnostics.ReportErrorWithFix(refNode.Span, DiagnosticCode.UndefinedReference,
                    $"Undefined variable '{refNode.Name}'. Did you mean '{similarName}'?", fix);
            }
            else
            {
                _diagnostics.ReportError(refNode.Span, DiagnosticCode.UndefinedReference,
                    $"Undefined variable '{refNode.Name}'");
            }
            if (unresolvedMember is not null)
                return unresolvedMember;
            // Return a dummy variable to continue analysis
            return new BoundVariableExpression(
                refNode.Span,
                CreateUnresolvedVariable(refNode));
        }

        if (symbols.All(symbol => symbol is VariableSymbol))
        {
            var variables = symbols.Cast<VariableSymbol>().ToArray();
            if (variables.Any(variable => variable.StorageIdentity.EventDeclaration is not null))
            {
                _diagnostics.ReportError(refNode.Span, DiagnosticCode.UndefinedReference,
                    $"Event '{refNode.Name}' is not readable as delegate storage in this context");
                return new BoundVariableExpression(refNode.Span, CreateUnresolvedVariable(refNode));
            }
            if (variables.Any(variable =>
                    variable.DeclaringTypeName != null
                    && !IsMemberAccessible(variable)))
            {
                _diagnostics.ReportError(
                    refNode.Span,
                    DiagnosticCode.UndefinedReference,
                    $"Member '{refNode.Name}' is not accessible in this context");
                return new BoundVariableExpression(
                    refNode.Span,
                    CreateUnresolvedVariable(refNode));
            }

            if (_isStaticContext
                && variables.Any(variable =>
                    variable.DeclaringTypeName != null
                    && !variable.IsStatic))
            {
                _diagnostics.ReportError(
                    refNode.Span,
                    DiagnosticCode.InstanceMemberInStaticContext,
                    $"Instance member '{refNode.Name}' cannot be accessed by a bare reference in a static context");
                return new BoundVariableExpression(
                    refNode.Span,
                    CreateUnresolvedVariable(refNode));
            }

            var identity = variables.Length == 1
                ? TryBuildVariableReference(variables[0])
                : null;
            if (variables is [var variable] && ResolveVariableEnumType(variable) is { } enumType)
                return new BoundVariableExpression(refNode.Span, variable, variables,
                    typeOverride: new BoundTypes.NominalBoundType(enumType.QualifiedName, declaration: enumType));
            return new BoundVariableExpression(refNode.Span, variables[0], variables,
                typeOverride: null, referenceIdentity: identity);
        }

        // Symbol exists but is not a variable - provide helpful fix
        _diagnostics.ReportNotAVariableWithFix(
            refNode.Span,
            refNode.Name,
            symbols.All(symbol => symbol is FunctionSymbol));
        return new BoundVariableExpression(
            refNode.Span,
            CreateUnresolvedVariable(refNode));
    }

    private BoundExpression? TryBindSourceStaticFieldReference(ReferenceNode refNode)
    {
        var sourceFieldDot = refNode.Name.LastIndexOf('.');
        if (sourceFieldDot > 0)
        {
            var receiver = new ReferenceNode(new Parsing.TextSpan(
                refNode.Span.Start, sourceFieldDot, refNode.Span.Line, refNode.Span.Column),
                refNode.Name[..sourceFieldDot], refNode.IsVerbatim);
            var member = new FieldAccessNode(refNode.Span, receiver, refNode.Name[(sourceFieldDot + 1)..],
                new Parsing.TextSpan(refNode.Span.Start + sourceFieldDot + 1,
                    refNode.Name.Length - sourceFieldDot - 1, refNode.Span.Line, refNode.Span.Column + sourceFieldDot + 1));
            if (TryBindSourceStaticFieldReceiver(member) is { } sourceReceiver)
                return BindFieldAccess(member, sourceReceiver);
        }

        return null;
    }

    private BoundBinaryExpression BindBinaryOperation(BinaryOperationNode binOp)
    {
        BoundExpression left;
        BoundExpression right;
        if (binOp.Operator == BinaryOperator.And
            || binOp.Operator == BinaryOperator.Or && IsSelectedBuiltInNullGuard(binOp))
        {
            using var _ = PushScope(_scope.CreateChild());
            var existingNarrowings = _flowNarrowingSources.Keys.ToHashSet();
            left = BindExpression(binOp.Left);
            if (binOp.Operator == BinaryOperator.And)
                DeclareSuccessfulConditionPatterns(binOp.Left, left);
            else
                DeclareFailedNullEqualityConditionPatterns(binOp.Left, left);
            var introducedNarrowings = _flowNarrowingSources.Keys
                .Where(variable => !existingNarrowings.Contains(variable))
                .ToArray();
            right = BindExpression(binOp.Right);
            var invalidatedNames = introducedNarrowings
                .Where(variable => !_flowNarrowingSources.ContainsKey(variable))
                .Select(variable => variable.Name)
                .ToHashSet(StringComparer.Ordinal);
            invalidatedNames.UnionWith(GetExplicitlyMutatedVariableNames(right));

            var shortCircuitResultType = GetBinaryOperationResultType(
                binOp.Operator,
                left.Type.DisplayString,
                right.Type.DisplayString);
            return new BoundBinaryExpression(binOp.Span, binOp.Operator, left, right, shortCircuitResultType)
            {
                InvalidatedLeftNarrowingNames = invalidatedNames
            };
        }
        else
        {
            left = BindExpression(binOp.Left);
            right = BindExpression(binOp.Right);
        }

        // Determine result type based on operator (S7 batch-3: .Type.DisplayString shim).
        var resultType = GetBinaryOperationResultType(binOp.Operator, left.Type.DisplayString, right.Type.DisplayString);

        return new BoundBinaryExpression(binOp.Span, binOp.Operator, left, right, resultType);
    }

    private static IEnumerable<string> GetExplicitlyMutatedVariableNames(BoundExpression expression)
    {
        if (expression is BoundLambdaExpression)
            yield break;

        if (expression is BoundCallExpression
            {
                ArgumentModifiers: { } modifiers,
                Arguments: var arguments
            })
        {
            for (var index = 0; index < modifiers.Count && index < arguments.Count; index++)
            {
                if (modifiers[index] is "ref" or "out"
                    && arguments[index] is BoundVariableExpression variable)
                {
                    yield return variable.Variable.Name;
                }
            }

        }

        foreach (var child in expression.Children)
        {
            foreach (var name in GetExplicitlyMutatedVariableNames(child))
                yield return name;
        }
    }

    private IEnumerable<VariableSymbol> GetDeferredMutations(
        BoundExpression? expressionBody,
        IReadOnlyList<BoundStatement>? statementBody)
    {
        if (expressionBody is not null)
        {
            foreach (var variable in GetExplicitlyMutatedVariables(expressionBody))
                yield return variable;
        }
        if (statementBody is null)
            yield break;
        foreach (var statement in statementBody)
        {
            foreach (var variable in GetExplicitlyMutatedVariables(statement))
                yield return variable;
        }
    }

    private IEnumerable<VariableSymbol> GetExplicitlyMutatedVariables(BoundNode node)
    {
        if (node is BoundLambdaExpression)
            yield break;
        if (node is BoundPreprocessorBlock preprocessor) // #1678: either branch may run
        {
            foreach (var statement in preprocessor.Branches.SelectMany(branch => branch))
            {
                foreach (var variable in GetExplicitlyMutatedVariables(statement))
                    yield return variable;
            }
            yield break;
        }
        if (node is BoundAssignmentStatement
            {
                Target: BoundVariableExpression assigned
            })
            yield return assigned.Variable;
        if (node is BoundCompoundAssignment
            {
                Target: BoundVariableExpression compounded
            })
            yield return compounded.Variable;
        if (node is BoundCallExpression expression)
        {
            if (expression.ArgumentModifiers is { } modifiers)
            {
                for (var index = 0; index < modifiers.Count && index < expression.Arguments.Count; index++)
                {
                    if (modifiers[index] is "ref" or "out"
                        && expression.Arguments[index] is BoundVariableExpression variable)
                        yield return variable.Variable;
                }
            }
            foreach (var captured in expression.ObservedCallableEffects.Mutations)
                yield return captured;
        }
        else if (node is BoundCallStatement call)
        {
            if (call.ArgumentModifiers is { } modifiers)
            {
                for (var index = 0; index < modifiers.Count && index < call.Arguments.Count; index++)
                {
                    if (modifiers[index] is "ref" or "out"
                        && call.Arguments[index] is BoundVariableExpression variable)
                        yield return variable.Variable;
                }
            }
            foreach (var captured in call.ObservedCallableEffects.Mutations)
                yield return captured;
        }
        else if (node is BoundExpressionCall expressionCall)
        {
            for (var index = 0; index < expressionCall.Arguments.Count; index++)
            {
                if (expressionCall.ArgumentModifiers?.ElementAtOrDefault(index) is "ref" or "out"
                    && expressionCall.Arguments[index] is BoundVariableExpression variable)
                    yield return variable.Variable;
            }
            foreach (var captured in expressionCall.ObservedCallableEffects.Mutations)
                yield return captured;
        }
        foreach (var child in node.ChildNodes)
        {
            foreach (var variable in GetExplicitlyMutatedVariables(child))
                yield return variable;
        }
    }

    private VariableSymbol? ResolveCallableStorage(string target, VariableSymbol? receiverSymbol) =>
        (target.EndsWith(".Invoke", StringComparison.Ordinal)
            ? receiverSymbol
            : _scope.Lookup(target) is VariableSymbol { StorageIdentity.EventDeclaration: null } variable
                ? variable : null)?.StorageIdentity;

    private CallableMutationSummary InvalidateDeferredCallTarget(BoundExpression target)
    {
        var observed = GetCallableEffects(target);
        return new CallableMutationSummary
        {
            Mutations = ApplyCallableMutationSummary(observed),
            Dependencies = GetCallableValueSources(target).Distinct<VariableSymbol>(ReferenceEqualityComparer.Instance).ToArray(),
            Alternatives = [observed]
        };
    }

    private CallableMutationSummary InvalidateDeferredCallTarget(VariableSymbol? callable)
    {
        if (callable is null)
            return CallableMutationSummary.Empty;

        var observed = GetCallableState(callable);
        return new CallableMutationSummary
        {
            Mutations = ApplyCallableMutationSummary(observed),
            Dependencies = [callable],
            Alternatives = [observed]
        };
    }

    private IReadOnlyList<VariableSymbol> ApplyCallableMutationSummary(CallableMutationSummary observed)
    {
        var mutations = new HashSet<VariableSymbol>(ReferenceEqualityComparer.Instance);
        var dependencies = new HashSet<VariableSymbol>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<CallableMutationSummary>(ReferenceEqualityComparer.Instance);
        var writes = new List<CallableMutationWrite>();
        var pending = new Queue<CallableMutationSummary>();
        pending.Enqueue(observed);
        bool changed;
        do
        {
            while (pending.TryDequeue(out var effects))
            {
                if (!visited.Add(effects))
                    continue;
                mutations.UnionWith(effects.Mutations);
                foreach (var alternative in effects.Alternatives)
                    pending.Enqueue(alternative);
                foreach (var dependency in effects.Dependencies)
                {
                    dependencies.Add(dependency);
                    pending.Enqueue(GetCallableState(dependency));
                }
                writes.AddRange(effects.Writes);
            }
            changed = false;
            foreach (var (storage, observedValue, valueSources) in writes)
            {
                // A summary joins all paths through the deferred body. A write
                // may not execute, so preserve the pre-call target as well.
                var value = observedValue;
                foreach (var source in valueSources)
                    value = UnionCallableEffects(value, GetCallableState(source));
                var previous = GetCallableState(storage);
                var updated = UnionCallableEffects(previous, value);
                if (ReferenceEquals(previous, updated))
                    continue;
                StoreCallableState(storage, updated);
                changed = true;
                if (dependencies.Contains(storage))
                    pending.Enqueue(updated);
            }
        }
        while (changed || pending.Count > 0);
        foreach (var captured in mutations)
        {
            foreach (var binding in _scope.CaptureVisibleVariableBindings())
            {
                if (ReferenceEquals(binding.Symbol.StorageIdentity, captured.StorageIdentity))
                    InvalidateMutableFlowNarrowing(binding.Symbol, requireRebindable: false);
            }
        }
        return mutations.ToArray();
    }

    private CallableMutationSummary GetCallableState(VariableSymbol storage) =>
        _callableState.GetValueOrDefault(storage.StorageIdentity, CallableMutationSummary.Empty);

    // An absent entry and CallableMutationSummary.Empty are the same state:
    // every read goes through GetCallableState, which defaults to Empty, and
    // UnionCallableEffects treats Empty as its identity. Not storing Empty keeps
    // the map proportional to callable-valued storage rather than to every
    // initialized local bound so far in the module, which CaptureCallableState
    // copies at each branch, loop, and lambda.
    private void StoreCallableState(VariableSymbol storage, CallableMutationSummary effects)
    {
        if (ReferenceEquals(effects, CallableMutationSummary.Empty))
            _callableState.Remove(storage);
        else
            _callableState[storage] = effects;
    }

    private Dictionary<VariableSymbol, CallableMutationSummary> CaptureCallableState() =>
        new(_callableState, ReferenceEqualityComparer.Instance);

    private void RestoreCallableState(IReadOnlyDictionary<VariableSymbol, CallableMutationSummary> state)
    {
        _callableState.Clear();
        foreach (var (storage, effects) in state)
            _callableState.Add(storage, effects);
    }

    private void SetCallableState(VariableSymbol variable, BoundExpression value)
    {
        var effects = GetCallableEffects(value);
        if (variable.StorageIdentity.EventDeclaration is not null)
            effects = UnionCallableEffects(GetCallableState(variable), effects);
        StoreCallableState(variable.StorageIdentity, effects);
        _deferredCallableWrites?.Add(new(
            variable.StorageIdentity, effects, GetCallableValueSources(value).ToArray()));
    }

    private BoundStatement? BindEventSubscription(
        StatementNode statement, ExpressionNode eventExpression, ExpressionNode handler, bool adding)
    {
        if (!TryGetEventStorage(eventExpression, out var storage, out var type, subscription: true)
            || storage.DeclaringTypeName is not { } owner || !IsWithinDeclaringType(owner))
            return ScanUnboundWrites(statement);

        BoundExpression target = eventExpression is FieldAccessNode field
            ? new BoundFieldAccessExpression(field.Span, BindExpression(field.Target), field.FieldName,
                storage.TypeName, storage, field.FieldNameSpan, resolvedType: type)
            : new BoundVariableExpression(eventExpression.Span, storage, typeOverride: type);
        var value = BindExpression(handler);
        InvalidateMutableFlowNarrowing(storage, requireRebindable: false);
        // Removal need not remove the last occurrence, and unknown subscribers
        // can still be present. Retain the conservative union for both writes.
        var effects = UnionCallableEffects(GetCallableState(storage), GetCallableEffects(value));
        StoreCallableState(storage, effects);
        _deferredCallableWrites?.Add(new(storage, effects, GetCallableValueSources(value).ToArray()));
        return new BoundCompoundAssignment(statement.Span, target,
            adding ? CompoundAssignmentOperator.Add : CompoundAssignmentOperator.Subtract, value);
    }

    private BoundTypes.FunctionBoundType? TryBuildEventFunctionType(EventDefinitionNode declaration)
    {
        if (declaration.HasAccessors || !ResolveSourceCallConversions || _asyncTypeModule is null)
            return null;
        SourceCallBindings();
        return _sourceCallConversions.EventTypes.TryGetValue(declaration, out var type)
            ? BuildEventFunctionType(type, declaration.DelegateType) : null;
    }

    private static BoundTypes.FunctionBoundType BuildEventFunctionType(
        Microsoft.CodeAnalysis.INamedTypeSymbol type, string display)
    {
        var invoke = type.DelegateInvokeMethod!;
        return new BoundTypes.FunctionBoundType(
            System.Collections.Immutable.ImmutableArray.CreateRange(
                invoke.Parameters.Select(parameter => Metadata.MetadataBinderResult.ToBoundType(parameter.Type))),
            Metadata.MetadataBinderResult.ToBoundType(invoke.ReturnType),
            displayOverride: display, row: BoundTypes.EffectRow.Unknown);
    }

    private bool TryGetEventStorage(AstNode expression, out VariableSymbol storage,
        out BoundTypes.FunctionBoundType type, bool subscription = false)
    {
        storage = null!;
        type = null!;
        if (_eventSymbols.Count == 0 || !ResolveSourceCallConversions || _asyncTypeModule is null)
            return false;
        SourceCallBindings();
        if (!_sourceCallConversions.EventStorageUses.TryGetValue(expression, out var selected)
            || selected.IsSubscription != subscription
            || !_sourceCallConversions.EventTypes.ContainsKey(selected.Declaration)
            || !_eventSymbols.TryGetValue(selected.Declaration, out storage!))
            return false;
        type = BuildEventFunctionType(selected.DelegateType, storage.TypeName);
        return true;
    }

    private static IEnumerable<VariableSymbol> GetCallableValueSources(BoundExpression expression)
    {
        if (expression is BoundLambdaExpression)
            yield break;
        if (expression is BoundVariableExpression variable)
            yield return variable.Variable.StorageIdentity;
        if (expression is BoundFieldAccessExpression { ResolvedFields: [var field] }
            && field.EventDeclaration is not null)
            yield return field.StorageIdentity;
        foreach (var child in expression.Children)
        {
            foreach (var source in GetCallableValueSources(child))
                yield return source;
        }
    }

    private CallableMutationSummary GetCallableEffects(BoundExpression expression)
    {
        if (expression is BoundLambdaExpression lambda)
            return lambda.CallableEffects;
        if (expression is BoundVariableExpression variable)
            return GetCallableState(variable.Variable);
        if (expression is BoundFieldAccessExpression { ResolvedFields: [var field] }
            && field.EventDeclaration is not null)
            return GetCallableState(field);
        var effects = CallableMutationSummary.Empty;
        foreach (var child in expression.Children)
            effects = UnionCallableEffects(effects, GetCallableEffects(child));
        return effects;
    }

    private static CallableMutationSummary UnionCallableEffects(
        CallableMutationSummary left, CallableMutationSummary right)
    {
        if (ReferenceEquals(left, right) || ReferenceEquals(right, CallableMutationSummary.Empty))
            return left;
        if (ReferenceEquals(left, CallableMutationSummary.Empty))
            return right;
        var alternatives = new HashSet<CallableMutationSummary>(
            GetCallableAlternatives(left), ReferenceEqualityComparer.Instance);
        var previousCount = alternatives.Count;
        alternatives.UnionWith(GetCallableAlternatives(right));
        if (alternatives.Count == previousCount)
            return left;
        return new CallableMutationSummary { Alternatives = alternatives.ToArray() };
    }

    private static IEnumerable<CallableMutationSummary> GetCallableAlternatives(CallableMutationSummary effects)
    {
        if (ReferenceEquals(effects, CallableMutationSummary.Empty))
            yield break;
        if (effects.Mutations.Count > 0 || effects.Dependencies.Count > 0 || effects.Writes.Count > 0
            || effects.Alternatives.Count == 0)
        {
            yield return effects;
            yield break;
        }
        foreach (var alternative in effects.Alternatives)
        {
            foreach (var nested in GetCallableAlternatives(alternative))
                yield return nested;
        }
    }

    private static IEnumerable<CallableMutationSummary> GetDeferredCallEffects(
        BoundExpression? expression, IReadOnlyList<BoundStatement>? statements)
    {
        if (expression is not null)
        {
            foreach (var effects in GetObservedCallEffects(expression))
                yield return effects;
        }
        if (statements is not null)
        {
            foreach (var statement in statements)
            {
                foreach (var effects in GetObservedCallEffects(statement))
                    yield return effects;
            }
        }
    }

    private static IEnumerable<CallableMutationSummary> GetObservedCallEffects(BoundNode node)
    {
        if (node is BoundLambdaExpression)
            yield break;
        if (node is BoundPreprocessorBlock preprocessor) // #1678: either branch may run
        {
            foreach (var statement in preprocessor.Branches.SelectMany(branch => branch))
            {
                foreach (var effects in GetObservedCallEffects(statement))
                    yield return effects;
            }
            yield break;
        }
        if (node is BoundCallExpression expression)
            yield return expression.ObservedCallableEffects;
        else if (node is BoundCallStatement statement)
            yield return statement.ObservedCallableEffects;
        else if (node is BoundExpressionCall expressionCall)
            yield return expressionCall.ObservedCallableEffects;
        foreach (var child in node.ChildNodes)
        {
            foreach (var effects in GetObservedCallEffects(child))
                yield return effects;
        }
    }

    private static string GetBinaryOperationResultType(BinaryOperator op, string leftType, string rightType)
    {
        if (op is BinaryOperator.Equal or BinaryOperator.NotEqual
            or BinaryOperator.LessThan or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual
            or BinaryOperator.And or BinaryOperator.Or)
            return "BOOL";

        if (op == BinaryOperator.Add
            && (IsScalarStringTypeName(leftType) || IsScalarStringTypeName(rightType)))
            return "STRING";

        if (op is BinaryOperator.LeftShift or BinaryOperator.RightShift)
            return NormalizeTypeName(leftType);

        return GetCommonType(leftType, rightType);
    }

    private BoundUnaryExpression BindUnaryOperation(UnaryOperationNode unaryOp)
    {
        var operand = BindExpression(unaryOp.Operand);
        if (unaryOp.Operator is UnaryOperator.PreIncrement or UnaryOperator.PostIncrement)
            ReportWriteToReadOnlyLocal(operand, unaryOp.Operand.Span, "increment {0}");
        else if (unaryOp.Operator is UnaryOperator.PreDecrement or UnaryOperator.PostDecrement)
            ReportWriteToReadOnlyLocal(operand, unaryOp.Operand.Span, "decrement {0}");
        if (unaryOp.Operator is UnaryOperator.PreIncrement or UnaryOperator.PostIncrement
            or UnaryOperator.PreDecrement or UnaryOperator.PostDecrement)
            InvalidateMemberFlowNarrowing(operand);
        var resultType = unaryOp.Operator switch
        {
            UnaryOperator.Not => "BOOL",
            UnaryOperator.Negate => operand.Type.DisplayString,
            UnaryOperator.BitwiseNot => operand.Type.DisplayString,
            _ => operand.Type.DisplayString
        };
        return new BoundUnaryExpression(unaryOp.Span, unaryOp.Operator, operand, resultType);
    }

    private BoundCallExpression BindCallExpression(CallExpressionNode callExpr,
        BoundExpression? computedReceiver = null, string? computedMethodName = null,
        ExpressionCallNode? contextualCall = null,
        (CallGraphAnalysis.SourceCallSelection Selection, FunctionSymbol Symbol)? sourceSelection = null)
    {
        var args = BindCallArguments(callExpr.Arguments, callExpr.ArgumentModifiers);
        var argumentNames = callExpr.ArgumentNames;
        var argumentModifiers = callExpr.ArgumentModifiers;
        var isEventCall = TryGetEventStorage(callExpr, out var eventStorage, out var eventType)
            && computedReceiver is null;
        var receiverSymbol = isEventCall ? eventStorage : computedReceiver is null ? ResolveCallReceiver(
            callExpr.Target,
            callExpr.ReceiverSpan ?? callExpr.CalleeSpan) : (computedReceiver as BoundVariableExpression)?.Variable;
        var receiverTypeSymbol = computedReceiver is BoundTypeReferenceExpression computedTypeReceiver
            ? computedTypeReceiver.ResolvedType
            : computedReceiver is null && receiverSymbol == null
                ? ResolveCallReceiverType(callExpr.Target)
                : null;
        var resolution = sourceSelection is { } source
            ? args.Any(argument => argument.HasUnknownInferredType)
                ? OverloadResolutionResult.UnresolvedArguments()
                : OverloadResolutionResult.Resolved(source.Symbol, source.Selection.ReturnType, source.Selection.Arguments)
            : isEventCall ? OverloadResolutionResult.NotFound() : ResolveCall(
            callExpr.Span,
            callExpr.Target,
            args,
            callExpr.ArgumentNames,
            callExpr.ArgumentModifiers,
            callExpr.TypeArguments,
            allowNullableStringCompatibility: true,
            computedReceiver: computedReceiver,
            computedMethodName: computedMethodName);
        var isSourceExtensionCall = false;
        if (resolution.Kind == OverloadResolutionKind.NotFound && !resolution.HasOpaqueOverloads
            && computedReceiver is not null and not BoundTypeReferenceExpression)
        {
            IReadOnlyList<BoundExpression> extensionArguments = [computedReceiver, .. args];
            IReadOnlyList<string?>? extensionNames = argumentNames is null ? null : [null, .. argumentNames];
            IReadOnlyList<string?>? extensionModifiers = argumentModifiers is null ? null : [null, .. argumentModifiers];
            var extensionResolution = computedReceiver.Type is BoundTypes.NominalBoundType { Declaration.IsEnum: true }
                ? ResolveSourceEnumExtension(
                    callExpr, computedMethodName!, extensionArguments, extensionNames, extensionModifiers)
                : ResolveSelectedSourceExtension(callExpr, extensionArguments);
            if (extensionResolution.Kind != OverloadResolutionKind.NotFound || extensionResolution.HasOpaqueOverloads)
            {
                resolution = extensionResolution;
                args = extensionArguments;
                argumentNames = extensionNames;
                argumentModifiers = extensionModifiers;
                isSourceExtensionCall = true;
                // Image-selected calls must retain receiver syntax: a static
                // spelling can select a competing non-extension overload.
                if (resolution.Function is { } selected
                    && computedReceiver.Type is BoundTypes.NominalBoundType { Declaration.IsEnum: true })
                    SourceExtensionCallTargets[callExpr.Span] = selected;
            }
        }
        var returnType = resolution.ResolvedReturnType ?? "OBJECT";

        var (resolvedTypeName, resolvedMethodName) = GetResolvedCallIdentity(
            callExpr.Target,
            receiverSymbol,
            receiverTypeSymbol);
        if (computedReceiver is not null)
        {
            resolvedTypeName = computedReceiver.Type.FullyQualifiedName;
            resolvedMethodName = computedMethodName;
        }
        var receiver = isEventCall
            ? new BoundVariableExpression(callExpr.CalleeSpan, eventStorage, typeOverride: eventType)
            : computedReceiver ?? BuildCallReceiver(
            callExpr.Target,
            callExpr.ReceiverSpan ?? callExpr.CalleeSpan,
            receiverSymbol,
            receiverTypeSymbol,
            resolvedTypeName);

        // v0.14 §S3b: enrich BCL-shaped calls with MetadataBinder-resolved
        // Roslyn NullableAnnotation on the return type. Additive only —
        // Binder's own string-based ResolveCall above still runs. §S4
        // additionally consumes the parameter-side annotations from the same
        // resolution to fire Calor0274 for possibly-null strings passed into
        // non-nullable :string parameters.
        var metadataReceiver = computedReceiver
            ?? (receiverSymbol is { FunctionType: null } && receiver is BoundVariableExpression ? receiver : null);
        if (metadataReceiver is null && receiver is BoundTypeReferenceExpression { ResolvedType: null } typeReceiver
            && !callExpr.Target.StartsWith("System.", StringComparison.Ordinal)
            && !callExpr.Target.StartsWith("Microsoft.", StringComparison.Ordinal))
            metadataReceiver = TryBindMetadataTypeReceiver(new ReferenceNode(typeReceiver.Span, typeReceiver.Name));
        var bclResolution = !isEventCall && resolution.Kind == OverloadResolutionKind.NotFound && !resolution.HasOpaqueOverloads
            ? metadataReceiver is not null
                ? TryResolveBclCall($"{metadataReceiver.Type.FullyQualifiedName}.{resolvedMethodName}", args,
                    callExpr.ArgumentNames, callExpr.ArgumentModifiers, metadataReceiver, callExpr.TypeArguments, contextualCall)
                : TryResolveBclCall(callExpr.Target, args, callExpr.ArgumentNames, callExpr.ArgumentModifiers,
                    contextualCall: contextualCall)
            : null;
        var metadataScalarReturn = bclResolution?.Symbol?.ReturnType.SpecialType
            is Microsoft.CodeAnalysis.SpecialType.System_String
                or Microsoft.CodeAnalysis.SpecialType.System_Boolean or Microsoft.CodeAnalysis.SpecialType.System_Char
                or Microsoft.CodeAnalysis.SpecialType.System_SByte or Microsoft.CodeAnalysis.SpecialType.System_Byte
                or Microsoft.CodeAnalysis.SpecialType.System_Int16 or Microsoft.CodeAnalysis.SpecialType.System_UInt16
                or Microsoft.CodeAnalysis.SpecialType.System_Int32 or Microsoft.CodeAnalysis.SpecialType.System_UInt32
                or Microsoft.CodeAnalysis.SpecialType.System_Int64 or Microsoft.CodeAnalysis.SpecialType.System_UInt64
                or Microsoft.CodeAnalysis.SpecialType.System_Single or Microsoft.CodeAnalysis.SpecialType.System_Double
                or Microsoft.CodeAnalysis.SpecialType.System_Decimal or Microsoft.CodeAnalysis.SpecialType.System_IntPtr
                or Microsoft.CodeAnalysis.SpecialType.System_UIntPtr;
        // Array enrichment shares the scalar-result boundary of named calls.
        // In particular it must not admit previously unresolved mutating void calls.
        if (metadataReceiver is not null && !metadataScalarReturn
            && (computedReceiver is null || NullabilityChecker.GetMethodInputArrayType(metadataReceiver) is not null))
            bclResolution = null;
        var instanceArrayResolution = computedReceiver is null && resolution.Kind == OverloadResolutionKind.NotFound
            && !resolution.HasOpaqueOverloads
            ? TryResolveInstanceArrayCall(callExpr, receiver)
            : null;
        var selectedSourceMethod = sourceSelection is { } selectedCall && resolution.Function == selectedCall.Symbol
            ? selectedCall.Selection.Method : null;
        var annotatedReturn = selectedSourceMethod is { } selectedSource
            ? Metadata.MetadataBinderResult.ToBoundType(selectedSource.ReturnType)
            : isEventCall ? eventType.ReturnType : bclResolution?.GetReturnBoundTypeEx();
        var methodInputArrayReturn = annotatedReturn as BoundTypes.ArrayBoundType;
        var genericNullabilityReturn = annotatedReturn as BoundTypes.GenericInstantiationBoundType;
        if (bclResolution?.LegacyReturnType is { } legacyReturn)
            annotatedReturn = legacyReturn;

        // v0.14 §F-3B (nullability): thread the declared return-type
        // annotation from a resolved pure-Calor callee onto BoundCallExpression.Type.
        // Prior to this slice a call to a pure-Calor function returned an
        // Oblivious NominalBoundType regardless of whether the declaration
        // said `-> string` (NotAnnotated) or `-> ?string` / `-> ?Foo`
        // (Annotated). Reuses TryReadDeclaredStringAnnotation (which
        // recognizes scalar STRING, whitelisted generic, and user-ref
        // shapes — the same set §S3/S7/S8 target-side gates handle) to
        // extract the annotation, then wraps the ALREADY-COMPUTED
        // `returnType` string so BoundCallExpression.Type.DisplayString
        // stays byte-identical to today (e.g. "str", "NULLABLE[inner=STRING]",
        // "Foo"). We deliberately do NOT swap in TryBuildStringTarget's
        // canonicalized target ("STRING") — that would break every
        // downstream consumer (BinderOverloadSetTests, etc.) that reads
        // DisplayString for pure-Calor calls. BCL-resolved returns still
        // take priority (bclResolution?.Return already carries Roslyn's
        // annotation on its own DisplayString shape).
        if (annotatedReturn is null
            && resolution.Function is { } calorReturnee)
        {
            if (ResolveCallableEnumType(calorReturnee, returnType) is { } returnEnum)
                annotatedReturn = new BoundTypes.NominalBoundType(returnEnum.QualifiedName, declaration: returnEnum);
            var declaredAnnotation = TryReadDeclaredStringAnnotation(calorReturnee.ReturnType);
            if (annotatedReturn is null && declaredAnnotation != BoundTypes.NullableAnnotation.Oblivious)
            {
                var identity = TryBuildCallableReference(calorReturnee.ReturnType, calorReturnee);
                annotatedReturn = new BoundTypes.NominalBoundType(
                    returnType, declaredAnnotation, identity?.Declaration, identity?.RoslynSymbol);
            }
        }
        BoundTypes.BoundType? callableReturn = null;
        if (annotatedReturn is null
            && _scope.Lookup(callExpr.Target) is VariableSymbol { FunctionType: { } functionType })
        {
            annotatedReturn = callableReturn = functionType.ReturnType;
        }
        if (annotatedReturn is null
            && resolvedMethodName == "Invoke"
            && receiverSymbol?.FunctionType is { } receiverFunctionType)
        {
            annotatedReturn = callableReturn = receiverFunctionType.ReturnType;
        }
        if (callableReturn is not null)
        {
            var callableShape = GetCallableReturnNullabilityShape(callableReturn);
            methodInputArrayReturn ??= callableShape as BoundTypes.ArrayBoundType;
            genericNullabilityReturn ??= callableShape as BoundTypes.GenericInstantiationBoundType;
        }
        if (sourceSelection?.Selection.Method is { } sourceArgumentMethod
            && resolution.Function == sourceSelection.Value.Symbol)
        {
            var reported = new HashSet<(TextSpan Span, string ParameterName, string TargetShape)>();
            foreach (var mapping in sourceSelection.Value.Selection.Arguments)
            {
                var parameter = sourceArgumentMethod.Parameters[mapping.ParameterIndex];
                var parameterType = mapping.IsExpandedParams && parameter.Type is Microsoft.CodeAnalysis.IArrayTypeSymbol array
                    ? array.ElementType : parameter.Type;
                if (parameter.RefKind != Microsoft.CodeAnalysis.RefKind.Out
                    && TryBuildBclArgumentTarget(parameterType) is { } target)
                    ValidateCallArgument(args[mapping.ArgumentIndex], parameter.Name, target,
                        mapping.IsExpandedParams, reported);
            }
        }
        else
            ValidateCallArguments(args, resolution, bclResolution);
        if (receiver is not null)
            ValidateStringInstanceReceiver(receiver, bclResolution?.Symbol);
        if (bclResolution?.Symbol is { ReducedFrom: not null, ReceiverType: { } reducedReceiver } reduced
            && (computedReceiver ?? receiver) is { } extensionReceiver
            && TryBuildBclArgumentTarget(reducedReceiver) is BoundTypes.GenericInstantiationBoundType
                { TypeArguments: [BoundTypes.NominalBoundType receiverElement] } receiverTarget
            && NullabilityChecker.IsClosedSourceReference(receiverElement))
            ValidateCallArgument(extensionReceiver, reduced.ReducedFrom.Parameters[0].Name,
                receiverTarget, false, []);
        ValidateDirectFunctionArguments(callExpr.Target, receiverSymbol, args, resolution, bclResolution);
        InvalidateMutatingCallArguments(args, argumentModifiers, resolution, bclResolution);
        var callable = isEventCall ? eventStorage.StorageIdentity : computedMethodName == "Invoke"
            ? receiverSymbol?.StorageIdentity
            : ResolveCallableStorage(callExpr.Target, receiverSymbol);
        var callableEffects = computedMethodName == "Invoke" && computedReceiver is not null
            ? InvalidateDeferredCallTarget(computedReceiver)
            : InvalidateDeferredCallTarget(callable);

        var boundCall = new BoundCallExpression(
            callExpr.Span,
            callExpr.Target,
            args,
            returnType,
            resolvedTypeName,
            resolvedMethodName,
            resolvedParameterTypes: (selectedSourceMethod is { } sourceMethod
                ? sourceMethod.Parameters.Select(parameter => parameter.Type.ToDisplayString(BclParameterIdentityFormat))
                : isEventCall ? eventType.ParameterTypes.Select(type => type.DisplayString)
                : bclResolution?.Symbol?.Parameters.Select(parameter => parameter.Type.ToDisplayString(BclParameterIdentityFormat))
                ?? resolution.Function?.Parameters
                .Select(parameter => parameter.TypeName)
                ?? args.Select(argument => argument.Type.DisplayString))
                .ToArray(),
            argumentNames: argumentNames,
            argumentModifiers: argumentModifiers,
            typeArguments: callExpr.TypeArguments,
            resolvedSymbol: resolution.Function,
            receiverSymbol: receiverSymbol,
            resolvedSymbols: resolution.Kind == OverloadResolutionKind.UnresolvedArguments ? []
                : WithLocalFunctionCandidates(callExpr.Target, resolution.Functions),
            calleeSpan: callExpr.CalleeSpan,
            receiverSpan: callExpr.ReceiverSpan,
            isInaccessibleCall: resolution.Kind == OverloadResolutionKind.Inaccessible,
            receiverTypeSymbol: receiverTypeSymbol,
            annotatedReturnType: annotatedReturn,
            receiver: receiver)
        {
            EvaluatedReceiver = isEventCall ? receiver : computedReceiver,
            SelectedSourceMethod = selectedSourceMethod,
            SourceStaticMethodOwner = selectedSourceMethod is { IsStatic: true, ContainingType.IsGenericType: true }
                && receiverTypeSymbol is { } sourceOwnerSymbol
                    ? _classesByQualifiedName.GetValueOrDefault(sourceOwnerSymbol.QualifiedName) : null,
            IsSourceExtensionCall = isSourceExtensionCall,
            ResolvedMetadataMethod = instanceArrayResolution?.Symbol
                ?? (metadataReceiver is not null || metadataScalarReturn ? bclResolution?.Symbol : null),
            ResolvedMetadataReceiverType = bclResolution?.ReceiverType,
            MetadataArrayResultType = (instanceArrayResolution?.Symbol
                ?? (metadataReceiver is not null ? bclResolution?.Symbol : null))?.ReturnType as Microsoft.CodeAnalysis.IArrayTypeSymbol,
            CallableStorageIdentity = callable,
            ObservedCallableEffects = callableEffects,
            ArgumentParameterIndices = GetBclArgumentParameterIndices(bclResolution, args.Count),
            SelectedOverloadMatches = resolution.Matches,
            HasOpaqueOverloads = resolution.HasOpaqueOverloads,
            HasUnresolvedArguments = resolution.Kind == OverloadResolutionKind.UnresolvedArguments,
            HasUnknownInferredType = resolution.ResolvedReturnType is null
                && annotatedReturn is null && instanceArrayResolution is null,
            MethodInputArrayType = methodInputArrayReturn ?? (resolution.Matches.Count == 1
                && TryBuildMethodInputArrayType(resolution.Matches[0].ReturnType, out var nativeArray,
                    resolution.Matches[0].Function) ? nativeArray : null),
            GenericNullabilityType = genericNullabilityReturn ?? (resolution.Matches.Count == 1
                ? TryBuildClosedReferenceGeneric(
                    resolution.Matches[0].ReturnType, resolution.Matches[0].Function)
                    ?? (TryBuildDeclaredNullabilityShape(resolution.Matches[0].ReturnType, out var genericReturn)
                        ? genericReturn as BoundTypes.GenericInstantiationBoundType : null)
                : null)
        };
        return boundCall;
    }

    private OverloadResolutionResult ResolveSelectedSourceExtension(
        CallExpressionNode call, IReadOnlyList<BoundExpression> arguments)
    {
        if (!ResolveSourceCallConversions || _asyncTypeModule is null)
            return OverloadResolutionResult.NotFound();
        SourceCallBindings();
        if (_sourceCallConversions.SelectedCalls.TryGetValue(call.Span, out var selected)
            && selected.IncludesExtensionReceiver
            && selected.Arguments.Count == arguments.Count
            && selected.Arguments.Select(mapping => mapping.ArgumentIndex).SequenceEqual(
                Enumerable.Range(0, arguments.Count))
            && selected.Declaration is MethodNode { IsStatic: true, Parameters.Count: > 0 } method
            && method.Parameters[0].Modifier.HasFlag(ParameterModifier.This)
            && _functionSymbols.TryGetValue(method, out var function)
            && _staticMethodSymbols.Contains(function) && IsFunctionAccessible(function))
            return OverloadResolutionResult.Resolved(function, selected.ReturnType, selected.Arguments);
        // A selected or competing source extension cannot fall back to a
        // same-named metadata contract when its native identity is unavailable.
        return _sourceCallConversions.SourceExtensionCalls.Contains(call.Span)
            ? OverloadResolutionResult.Opaque() : OverloadResolutionResult.NotFound();
    }

    private OverloadResolutionResult ResolveSourceEnumExtension(
        CallExpressionNode call, string methodName, IReadOnlyList<BoundExpression> arguments,
        IReadOnlyList<string?>? argumentNames, IReadOnlyList<string?>? argumentModifiers)
    {
        var metadata = GetOrCreateMetadataBinder();
        if (metadata?.Context.TryResolveType("System.Enum") is not { } enumBase)
            return OverloadResolutionResult.Opaque();
        // Instance members precede extension lookup. Until their applicability
        // is established for source enums, do not replace them with a pure row.
        if (Metadata.MetadataMemberLookup.MethodGroup(
                metadata.Context.HostCompilationForBinder, enumBase, methodName)
            .Any(method => !method.IsStatic))
            return OverloadResolutionResult.Opaque();
        var visibleScopes = new HashSet<string>(StringComparer.Ordinal);
        for (var id = _currentNamespaceScopeId; id is not null
            && visibleScopes.Add(id) && _metadataNamespaceScopes.TryGetValue(id, out var scope);)
            id = scope.ParentScopeId;
        var namespaces = _metadataUsings.Where(import => import.Alias is null && !import.IsStatic
                && (import.NamespaceScopeId is null || visibleScopes.Contains(import.NamespaceScopeId)))
            .Select(import => import.Namespace).ToHashSet(StringComparer.Ordinal);
        for (var ns = _currentNamespaceIdentity ?? ""; ; )
        {
            namespaces.Add(ns);
            var dot = ns.LastIndexOf('.');
            if (ns.Length == 0) break;
            ns = dot < 0 ? "" : ns[..dot];
        }
        var providers = _classesByQualifiedName.Where(pair => pair.Value.IsStatic
                && pair.Value.TypeParameters.Count == 0
                && pair.Key == GetQualifiedTypeName(pair.Value)
                && namespaces.Contains(pair.Value.NamespaceIdentity ?? ""))
            .ToArray();
        // Preserved declarations may compete with a native winner. Their bodies
        // and signatures cannot authorize a source effect row.
        if (providers.Any(provider => HasOpaqueMethodDeclaration(
                provider.Value, methodName, call.TypeArguments?.Count)))
            return OverloadResolutionResult.Opaque();
        var owners = providers.Select(provider => provider.Key).ToHashSet(StringComparer.Ordinal);
        var candidates = _functionSymbols.Where(pair => pair.Key is MethodNode method
                && method.Name == methodName && method.IsStatic
                && method.Parameters.Count > 0
                && method.Parameters[0].Modifier.HasFlag(ParameterModifier.This)
                && method.Parameters.Skip(1).All(parameter => !parameter.Modifier.HasFlag(ParameterModifier.This))
                && ResolveCallableEnumType(pair.Value, pair.Value.Parameters[0].TypeName) is not null
                && pair.Value.ContainingTypeName is { } owner && owners.Contains(owner))
            .Select(pair => pair.Value).ToArray();
        var argumentTypes = arguments.Select((argument, index) =>
            GetOverloadArgumentType(argument, argumentModifiers?.ElementAtOrDefault(index))).ToArray();
        var resolution = ResolveAccessibleOverload(methodName, argumentTypes,
            argumentNames, argumentModifiers, GetOverloadTypeArguments(call.TypeArguments),
            allowNullableStringCompatibility: true, requireStatic: true, candidateOverrides: candidates,
            arguments: arguments, callSpan: call.Span, includesExtensionReceiver: true);
        if (resolution.Kind == OverloadResolutionKind.NoMatch)
            _diagnostics.ReportError(call.Span, DiagnosticCode.NoMatchingOverload,
                $"No source extension overload '{methodName}' matches {FormatCallSignature(argumentTypes, argumentModifiers, call.TypeArguments)}.");
        else if (resolution.Kind == OverloadResolutionKind.Ambiguous)
            _diagnostics.ReportError(call.Span, DiagnosticCode.AmbiguousOverload,
                $"Source extension call '{methodName}' is ambiguous between: {FormatCandidates(resolution.Candidates)}");
        return resolution;
    }

    private BoundTypes.BoundType? GetCallableReturnNullabilityShape(BoundTypes.BoundType returnType)
    {
        if (returnType is BoundTypes.ArrayBoundType or BoundTypes.GenericInstantiationBoundType)
            return returnType;
        if (returnType is not BoundTypes.NominalBoundType nominal)
            return null;

        var spelling = nominal.NullableAnnotation == BoundTypes.NullableAnnotation.Annotated
            ? $"?{nominal.QualifiedName}"
            : nominal.QualifiedName;
        if (TryBuildMethodInputArrayType(spelling, out var array))
            return array;
        return TryParseGenericStringTarget(spelling, out var generic, nominal.NullableAnnotation) ? generic : null;
    }

    /// <summary>
    /// v0.15 E1 slice 2a (roadmap §4.2; scoping doc §D6) — build the receiver of a
    /// dotted call as a real <see cref="BoundExpression"/> with a real
    /// <see cref="BoundType"/>, so consumers read a type the binder vouches for
    /// instead of re-deriving one from symbol strings.
    ///
    /// <para>Where the binder cannot name the receiver's type it says so with
    /// <see cref="BoundTypes.UnresolvedBoundType"/> and reports <c>Calor0270</c>
    /// (SignatureUnresolved, Info — §F-4's promotion path owns the severity)
    /// exactly once at the receiver span. Two such cases exist today: the
    /// <c>OBJECT</c> inference fallback on an inferred local, and a member chain
    /// (<c>a.b.M</c>) whose intermediate members the binder does not type.
    /// A declared <c>object</c>/<c>OBJECT</c> parameter, field or property is a
    /// genuine type and is left alone.</para>
    ///
    /// <para>Returns null when there is nothing to say: a bare call, or a dotted
    /// head that is neither a bound variable nor written as a type reference.
    /// Null is never "no receiver" — it is "not vouched for".</para>
    /// </summary>
    private BoundExpression? BuildCallReceiver(
        string target,
        Parsing.TextSpan receiverSpan,
        VariableSymbol? receiverSymbol,
        TypeSymbol? receiverTypeSymbol,
        string? resolvedTypeName)
    {
        var lastDot = target.LastIndexOf('.');
        if (lastDot <= 0)
            return null;

        var receiverPath = target[..lastDot];

        if (receiverSymbol != null)
        {
            // Constructing the reference node canonicalizes the symbol's type
            // string, and TypeIdentity.Canonicalize (Binding/Scope.cs:316) throws
            // on a string it cannot parse — an empty one, or a bare "?".
            //
            // A bare "?" is not hypothetical: the C# → Calor converter emits it
            // for a lambda parameter whose type it could not infer.
            // TryInferLambdaParameterType (Migration/RoslynSyntaxVisitor.cs:11774-11794,
            // called at :11877 and :11885) hands back Roslyn's display spelling of
            // the parameter's type, and for an error type that spelling is "?" —
            // which the emitter then writes as `§LAM{l1:ctx:?}`. That is a
            // converter defect (root cause: issue #1097, 72 pre-existing ICEs over the
            // converted corpus, unchanged by this slice); the binder's job here is not
            // to amplify it.
            //
            // A receiver whose type string the binder cannot even canonicalize is
            // precisely an unresolved receiver, so it is caught and marked as one
            // here rather than escaping as an internal-error diagnostic that would
            // abandon the rest of the member. Constructions below that pass
            // typeOverride do not canonicalize and cannot throw.
            BoundVariableExpression? typedHead;
            try
            {
                typedHead = new BoundVariableExpression(receiverSpan, receiverSymbol);
            }
            catch (ArgumentException)
            {
                typedHead = null;
            }

            var firstDot = receiverPath.IndexOf('.');
            if (firstDot > 0)
            {
                // a.b.M — the binder resolves `a` only. `a.b` has no bound type,
                // so the chain is built over the head and reported unresolved
                // rather than attributed to `a`'s type. One Calor0270 for the
                // whole chain, and the head shares its reason when it is itself
                // untyped.
                var unresolved = UnresolvedReceiver(
                    receiverSpan,
                    receiverPath,
                    "is a member chain the binder does not type",
                    report: ShouldReportUnresolvedReceiver(receiverPath));
                BoundExpression chain = typedHead
                    ?? new BoundVariableExpression(
                        receiverSpan, receiverSymbol, typeOverride: unresolved);
                foreach (var segment in receiverPath[(firstDot + 1)..].Split('.'))
                {
                    chain = new BoundFieldAccessExpression(
                        receiverSpan, chain, segment, typeName: null, resolvedType: unresolved);
                }
                return chain;
            }

            if (typedHead is null)
            {
                return new BoundVariableExpression(
                    receiverSpan,
                    receiverSymbol,
                    typeOverride: UnresolvedReceiver(
                        receiverSpan,
                        receiverPath,
                        "carries a type string the binder cannot resolve to a type",
                        report: ShouldReportUnresolvedReceiver(receiverPath)));
            }

            if (typedHead.HasUnknownInferredType)
            {
                return new BoundVariableExpression(
                    receiverSpan,
                    receiverSymbol,
                    typeOverride: UnresolvedReceiver(
                        receiverSpan,
                        receiverPath,
                        "is an inferred local whose type the binder could not determine",
                        report: ShouldReportUnresolvedReceiver(receiverPath)));
            }

            return typedHead;
        }

        if (receiverTypeSymbol != null)
        {
            return new BoundTypeReferenceExpression(
                receiverSpan,
                receiverPath,
                new BoundTypes.NominalBoundType(receiverTypeSymbol.QualifiedName, declaration: receiverTypeSymbol),
                receiverTypeSymbol);
        }

        // E1 slice 2b (PR #1095 review round 1, finding 10): the predicate and the
        // short-name table both live in Binding/TypeIdentity now, so Binding/ no
        // longer reaches into Effects/. ArchitectureTests pins that direction.
        //
        // Not a bound variable and not a Calor-declared type. GetResolvedCallIdentity's
        // name for this shape is the source text with short BCL names expanded, so it
        // only counts when the receiver is WRITTEN as a type reference — Console,
        // System.IO.File, OrderRepo. A lowercase head (foo.Bar, this.sb.Append,
        // _items.Add) is not vouched for and yields null.
        return TypeIdentity.IsTypeQualifiedReference(receiverPath)
               && resolvedTypeName is { } sourceQualifiedType
            ? new BoundTypeReferenceExpression(
                receiverSpan, receiverPath, new BoundTypes.NominalBoundType(sourceQualifiedType))
            : null;
    }

    /// <summary>
    /// The <see cref="BoundTypes.UnresolvedBoundType"/> for a receiver the binder
    /// cannot type, and — only when <paramref name="report"/> — one Calor0270 at
    /// the receiver span.
    ///
    /// <para><b>Marking and reporting are separate decisions.</b> Every receiver
    /// the binder cannot type is marked, always, because downstream analyses must
    /// never read an unresolved receiver as resolved. Only the shapes an author
    /// can actually act on are reported, because a diagnostic nobody can act on
    /// is noise in the editor. <see cref="ShouldReportUnresolvedReceiver"/> owns
    /// that split and states the evidence behind it.</para>
    /// </summary>
    private BoundTypes.UnresolvedBoundType UnresolvedReceiver(
        Parsing.TextSpan span,
        string receiverPath,
        string reason,
        bool report)
    {
        if (report)
        {
            _diagnostics.ReportInfo(
                span,
                DiagnosticCode.SignatureUnresolved,
                $"Receiver '{receiverPath}' {reason}, so the call's receiver type is unresolved. " +
                "Analyses that need the type report the call as unresolved rather than guess one; " +
                "give the binding an explicit type, or call through the declaring type.");
        }

        // E1 slice 2b: carry the report decision onto the type. A consumer that
        // fails closed on "the binder looked and could not name this" needs to
        // tell an authoritative answer from a binder limitation, and `report` is
        // exactly that split.
        return new BoundTypes.UnresolvedBoundType(
            $"receiver '{receiverPath}' {reason}",
            reported: report);
    }

    /// <summary>
    /// Whether an unresolved receiver is worth telling the author about.
    ///
    /// <para>Measured on the converted corpus (see the ledger registered at
    /// <c>bench/phase0-agent-native/calor0270-corpus-ledger.json</c>): reporting
    /// every unresolved receiver produced 875 Info diagnostics across 76 of 305
    /// modules, overwhelmingly of two shapes an author cannot act on —</para>
    /// <list type="number">
    /// <item><b>member chains</b> (<c>result.IsValid.ToString</c>): the binder
    /// does not type members of a receiver yet. That is a binder limitation, not
    /// anything wrong with the code, and "give the binding an explicit type" is
    /// not advice the author can follow.</item>
    /// <item><b>converter-synthesized temporaries</b> (<c>_chainIsValid042</c>):
    /// the author never wrote the binding, so they cannot annotate it.</item>
    /// </list>
    /// <para>Both are still <i>marked</i> <see cref="BoundTypes.UnresolvedBoundType"/> —
    /// silence here is about the editor, never about what the type system claims.
    /// The two shapes that remain reportable are the ones an explicit type
    /// annotation fixes: an inferred local the binder could not type, and a
    /// declared type string it cannot resolve to a type.</para>
    /// </summary>
    private static bool ShouldReportUnresolvedReceiver(string receiverPath) =>
        !receiverPath.Contains('.', StringComparison.Ordinal)
        && !IsConverterSynthesizedName(receiverPath);

    /// <summary>
    /// A binding the C# → Calor converter generated rather than the author.
    /// <c>ConversionContext.GenerateId</c> (<c>Migration/ConversionContext.cs:424-429</c>)
    /// spells them <c>_{prefix}{Hint}{NNN}</c> — a leading underscore and a
    /// three-digit counter — across ~23 prefixes (<c>_chain</c>, <c>_cast</c>,
    /// <c>_tern</c>, …). Matching the shape rather than enumerating the prefixes
    /// keeps this from silently going stale when a prefix is added; the cost is
    /// that a hand-written <c>_foo001</c> is also treated as synthesized, which
    /// only ever means one fewer Info diagnostic.
    /// </summary>
    private static bool IsConverterSynthesizedName(string name)
    {
        if (name.Length < 5 || name[0] != '_')
            return false;
        for (var i = name.Length - 3; i < name.Length; i++)
        {
            if (!char.IsAsciiDigit(name[i]))
                return false;
        }
        return char.IsAsciiLetter(name[name.Length - 4]);
    }

    private (string? TypeName, string? MethodName) GetResolvedCallIdentity(
        string target,
        VariableSymbol? receiverSymbol,
        TypeSymbol? receiverTypeSymbol)
    {
        var lastDot = target.LastIndexOf('.');
        if (lastDot <= 0)
            return (null, null);

        var typePart = target[..lastDot];
        return (
            receiverSymbol != null
                ? ResolveTypeSymbol(receiverSymbol.TypeName)?.QualifiedName
                    ?? TypeIdentity.MapShortTypeNameToFullName(
                        receiverSymbol.TypeName)
                : receiverTypeSymbol != null
                    ? receiverTypeSymbol.QualifiedName
                    : !typePart.Contains('.')
                        ? TypeIdentity.MapShortTypeNameToFullName(typePart)
                        : typePart,
            target[(lastDot + 1)..]);
    }

    private VariableSymbol? ResolveCallReceiver(
        string target,
        Parsing.TextSpan referenceSpan)
    {
        var firstDot = target.IndexOf('.');
        var receiverName = firstDot <= 0 ? target : target[..firstDot];
        var variables = _scope.LookupAll(receiverName)
            .OfType<VariableSymbol>()
            .ToArray();
        if (variables.Length == 0)
            return null;

        if (variables.Any(variable => variable.StorageIdentity.EventDeclaration is not null))
        {
            _diagnostics.ReportError(referenceSpan, DiagnosticCode.UndefinedReference,
                $"Event '{receiverName}' is not readable as delegate storage in this context");
            return null;
        }

        if (_isStaticContext
            && variables.Any(variable =>
                variable.DeclaringTypeName != null
                && !variable.IsStatic))
        {
            _diagnostics.ReportError(
                referenceSpan,
                DiagnosticCode.InstanceMemberInStaticContext,
                $"Instance member '{receiverName}' cannot be accessed by a bare reference in a static context");
            return null;
        }

        return variables[0];
    }

    private TypeSymbol? ResolveCallReceiverType(string target)
    {
        var receiverName = GetTypeQualifiedCallReceiver(target);
        if (receiverName == null)
            return null;

        return ResolveTypeSymbol(receiverName);
    }

    private static string? GetTypeQualifiedCallReceiver(string target)
    {
        const string globalPrefix = "global::";
        if (target.StartsWith(globalPrefix, StringComparison.Ordinal))
            target = target[globalPrefix.Length..];

        const string constructorSuffix = "..ctor";
        if (target.EndsWith(constructorSuffix, StringComparison.Ordinal))
            return target[..^constructorSuffix.Length];

        var lastDot = target.LastIndexOf('.');
        return lastDot <= 0 ? null : target[..lastDot];
    }

    private OverloadResolutionResult ResolveCall(
        Parsing.TextSpan span,
        string target,
        IReadOnlyList<BoundExpression> arguments,
        IReadOnlyList<string?>? argumentNames,
        IReadOnlyList<string?>? argumentModifiers,
        IReadOnlyList<string>? typeArguments,
        bool allowNullableStringCompatibility = false,
        BoundExpression? computedReceiver = null,
        string? computedMethodName = null)
    {
        if (arguments.Any(argument => argument.HasUnknownInferredType)
            || computedReceiver?.HasUnknownInferredType == true)
            return OverloadResolutionResult.UnresolvedArguments();
        // Synthetic constructor targets can also occur in ordinary call nodes.
        allowNullableStringCompatibility &= !target.EndsWith("..ctor", StringComparison.Ordinal);
        var argumentTypes = arguments
            .Select((argument, index) =>
                argument is BoundVariableExpression { Variable.Id.IsNone: true }
                    // #943: a by-reference element or member path the binder cannot type.
                    || argument is BoundFieldAccessExpression { ResolvedField: null }
                        or BoundArrayAccess or BoundArrayAccessExpression or BoundMultiDimArrayAccess
                        && (argument.Type is BoundTypes.UnresolvedBoundType || argument.Type.DisplayString == "OBJECT")
                        && !string.IsNullOrEmpty(argumentModifiers?.ElementAtOrDefault(index))
                    ? "<unresolved>"
                    : argument is BoundNullLiteral
                        ? "NULL"
                    : allowNullableStringCompatibility
                        && NullabilityChecker.IsNullContainingScalarStringSource(argument)
                            ? "?STRING"
                    : GetOverloadArgumentType(
                        argument,
                        argumentModifiers != null && index < argumentModifiers.Count
                            ? argumentModifiers[index]
                            : null))
            .ToArray();
        var interfaceCandidates = GetInterfaceCallCandidates(computedReceiver, computedMethodName, out var opaqueInterface);
        if (opaqueInterface)
            return OverloadResolutionResult.Opaque();
        if (interfaceCandidates is { Count: 0 })
        {
            _diagnostics.ReportError(span, DiagnosticCode.NoMatchingOverload,
                $"Interface receiver has no method '{computedMethodName}'.");
            return OverloadResolutionResult.NoMatch(interfaceCandidates);
        }
        var lookupNames = interfaceCandidates is not null ? ["<interface-call>"]
            : computedReceiver is null ? GetCallLookupNames(target)
            : EnumerateHierarchyLookupNames(ResolveClass(computedReceiver.Type.DisplayString), computedMethodName!);
        var overloadTypeArguments = GetOverloadTypeArguments(typeArguments);
        var opaqueOwner = computedReceiver is null ? _currentClass : ResolveClass(computedReceiver.Type.FullyQualifiedName);
        var opaqueTarget = computedReceiver is null ? target : computedMethodName;
        foreach (var lookupName in lookupNames.Distinct(StringComparer.Ordinal))
        {
            var requireStatic = computedReceiver is BoundTypeReferenceExpression
                || computedReceiver is null && IsEnclosingLexicalCall(target, lookupName);
            var resolution = ResolveAccessibleOverload(
                lookupName,
                argumentTypes,
                argumentNames,
                argumentModifiers,
                overloadTypeArguments,
                allowNullableStringCompatibility,
                requireStatic,
                requireInstance: computedReceiver is not null and not BoundTypeReferenceExpression,
                candidateOverrides: interfaceCandidates,
                arguments: arguments,
                callSpan: span);
            if (resolution.Kind == OverloadResolutionKind.NotFound)
                continue;

            if ((resolution.Kind is OverloadResolutionKind.NoMatch or OverloadResolutionKind.Ambiguous
                    || resolution.Kind == OverloadResolutionKind.Resolved
                        && !resolution.Functions.Any(_localFunctionSymbols.Contains))
                && opaqueTarget != null
                && HasOpaqueMethodDeclaration(opaqueOwner, opaqueTarget, typeArguments?.Count))
            {
                // A native winner is not authoritative when a preserved overload
                // can compete; leave the call unresolved for downstream analysis.
                return OverloadResolutionResult.Opaque();
            }

            // v0.17 R2 — an argument whose type this module CANNOT SEE is in the
            // same epistemic position as `<unresolved>`: the binder cannot prove
            // the call is wrong, so reporting Calor0208 is a false positive, not
            // a finding. Conversion is PER FILE, so this is routine on real code
            // — `LogContext.Push(PropertyEnricher)` against
            // `Push(ILogEventEnricher)` is unresolvable only because
            // PropertyEnricher is declared in another file and the two never
            // meet in one module. Extending the existing `<unresolved>`
            // suppression is the honest reading; the alternative is emitting an
            // error whose premise the compiler has not established.
            var hasUnresolvedArguments = argumentTypes.Where((type, index) =>
                string.Equals(type, "<unresolved>", StringComparison.Ordinal)
                || !string.Equals(type, "NULL", StringComparison.Ordinal)
                    && arguments[index] is not BoundLambdaExpression
                    && IsNominalTypeInvisibleToThisModule(type)).Any();
            if (allowNullableStringCompatibility && !hasUnresolvedArguments
                && resolution.Kind == OverloadResolutionKind.Resolved
                && resolution.Matches.Any(match => match.Arguments.Any(mapping =>
                    Scope.HasNullableStringAnnotationDifference(
                        mapping.ParameterType, argumentTypes[mapping.ArgumentIndex]))))
            {
                // Preserve actual prior rejection, not a guess based on the new
                // winner: an OBJECT overload may already have accepted this call.
                var previous = ResolveAccessibleOverload(
                    lookupName, argumentTypes, argumentNames, argumentModifiers, overloadTypeArguments,
                    requireStatic: requireStatic,
                    requireInstance: computedReceiver is not null and not BoundTypeReferenceExpression,
                    candidateOverrides: interfaceCandidates,
                    arguments: arguments);
                if (previous.Kind is OverloadResolutionKind.NoMatch or OverloadResolutionKind.Ambiguous)
                    resolution = resolution.WithReplacedNativeOverloadError();
            }
            if (!hasUnresolvedArguments
                && resolution.Kind == OverloadResolutionKind.NoMatch)
            {
                _diagnostics.ReportError(
                    span,
                    DiagnosticCode.NoMatchingOverload,
                    $"No overload of internal call '{target}' matches " +
                    $"{FormatCallSignature(argumentTypes, argumentModifiers, typeArguments)}. " +
                    $"Candidates: {FormatCandidates(resolution.Candidates)}");
            }
            else if (!hasUnresolvedArguments
                     && resolution.Kind == OverloadResolutionKind.Ambiguous)
            {
                _diagnostics.ReportError(
                    span,
                    DiagnosticCode.AmbiguousOverload,
                    $"Call '{target}{FormatCallSignature(argumentTypes, argumentModifiers, typeArguments)}' " +
                    $"is ambiguous between: {FormatCandidates(resolution.Candidates)}");
            }

            return resolution;
        }

        return OverloadResolutionResult.NotFound();
    }

    private string[]? GetOverloadTypeArguments(IReadOnlyList<string>? typeArguments) =>
        typeArguments?.Select(typeArgument =>
            GetTypeParameterConstraints(typeArgument) is null
                && ResolveSourceType(typeArgument, _currentClassName, _currentNamespaceIdentity,
                    _currentNamespaceScopeId).Type is { IsEnum: true } enumType
                    ? "global::" + enumType.QualifiedName : typeArgument).ToArray();

    private bool HasOpaqueMethodDeclaration(ClassDefinitionNode? owner, string target, int? typeArgumentCount)
    {
        if (owner == null)
            return false;

        var methodName = target.StartsWith("this.", StringComparison.Ordinal)
            ? target["this.".Length..]
            : target;
        if (methodName.Contains('.', StringComparison.Ordinal))
            return false;

        var visited = new HashSet<ClassDefinitionNode>();
        for (var current = owner; current != null && visited.Add(current); current = ResolveBaseClass(current))
        {
            if (EnumerateCurrentClassInteropBlocks(current)
                .Any(block => GetInteropMethodArities(block).Any(method =>
                    string.Equals(method.Name, methodName, StringComparison.Ordinal)
                    && (typeArgumentCount == null || method.Arity == typeArgumentCount))))
                return true;
        }
        return false;
    }

    private static IEnumerable<CSharpInteropBlockNode> EnumerateCurrentClassInteropBlocks(
        ClassDefinitionNode cls)
    {
        foreach (var block in cls.InteropBlocks)
            yield return block;
        foreach (var branch in EnumeratePreprocessorBranches(cls))
        {
            foreach (var block in branch.InteropBlocks)
                yield return block;
        }
    }

    private IReadOnlySet<(string Name, int Arity)> GetInteropMethodArities(CSharpInteropBlockNode block)
    {
        if (_interopMethodArities.TryGetValue(block, out var methods))
            return methods;

        var wrapper = $"class __CalorInteropHost\n{{\n{block.CSharpCode}\n}}";
        var root = CSharpSyntaxTree.ParseText(wrapper).GetCompilationUnitRoot();
        methods = root.Members
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(declaration => declaration.Members.OfType<MethodDeclarationSyntax>())
            .Where(method => method.ExplicitInterfaceSpecifier == null)
            .Select(method => (
                Name: method.Identifier.ValueText,
                Arity: method.TypeParameterList?.Parameters.Count ?? 0))
            .Where(method => !string.IsNullOrEmpty(method.Name))
            .ToHashSet();
        _interopMethodArities.Add(block, methods);
        return methods;
    }

    private string GetOverloadArgumentType(BoundExpression argument, string? argumentModifier)
    {
        if (argument.Type is BoundTypes.NominalBoundType { Declaration.IsEnum: true } enumType)
            return "global::" + enumType.Declaration.QualifiedName;
        if (argument is not BoundVariableExpression variable
            || argumentModifier is not ("ref" or "out"))
        {
            return GetResolvedStringOverloadType(argument.Type) ?? argument.Type.DisplayString;
        }

        var storage = variable.Variable;
        while (_flowNarrowingSources.TryGetValue(storage, out var declared))
            storage = declared;
        return GetResolvedStringOverloadType(TryBuildVariableReference(storage)) ?? storage.TypeName;
    }

    private OverloadResolutionResult ResolveAccessibleOverload(
        string lookupName,
        IReadOnlyList<string> argumentTypes,
        IReadOnlyList<string?>? argumentNames,
        IReadOnlyList<string?>? argumentModifiers,
        IReadOnlyList<string>? typeArguments,
        bool allowNullableStringCompatibility = false,
        bool requireStatic = false,
        bool requireInstance = false,
        IReadOnlyList<FunctionSymbol>? candidateOverrides = null,
        IReadOnlyList<BoundExpression>? arguments = null,
        TextSpan? callSpan = null,
        bool includesExtensionReceiver = false)
    {
        var candidates = (candidateOverrides ?? _scope.GetOverloads(lookupName))
            .ToArray();
        if (candidates.Length == 0)
            return OverloadResolutionResult.NotFound();

        var accessibleCandidates = candidates
            .Where(candidate => IsFunctionAccessible(candidate)
                && (!requireStatic || _staticMethodSymbols.Contains(candidate))
                && (!requireInstance || !_staticMethodSymbols.Contains(candidate)))
            .ToArray();
        if (accessibleCandidates.Length == 0)
            return OverloadResolutionResult.Inaccessible(candidates);

        if (ResolveSourceCallConversions && _asyncTypeModule is not null && callSpan is { } site)
        {
            SourceCallBindings();
            if (_sourceCallConversions.AmbiguousCalls.Contains(site))
                return OverloadResolutionResult.Ambiguous(accessibleCandidates);
            if (_sourceCallConversions.SelectedCalls.TryGetValue(site, out var selected)
                && (selected.UsesUserDefinedConversion || selected.UsesAnonymousFunctionConversion)
                && selected.IncludesExtensionReceiver == includesExtensionReceiver
                && selected.Arguments.Count == argumentTypes.Count
                && _functionSymbols.TryGetValue(selected.Declaration, out var selectedFunction)
                && accessibleCandidates.Contains(selectedFunction))
                return OverloadResolutionResult.Resolved(
                    selectedFunction, selected.ReturnType, selected.Arguments);
        }

        var accessibleScope = new Scope();
        var duplicates = new Dictionary<FunctionSymbol, List<FunctionSymbol>>();
        foreach (var candidate in accessibleCandidates)
        {
            if (!accessibleScope.TryDeclareOverload(lookupName, candidate, out var duplicate)
                && candidateOverrides is not null && duplicate is not null)
            {
                if (!duplicates.TryGetValue(duplicate, out var alternatives))
                    duplicates[duplicate] = alternatives = [duplicate];
                alternatives.Add(candidate);
            }
        }

        var resolved = accessibleScope.ResolveOverload(
            lookupName,
            argumentTypes,
            argumentNames,
            argumentModifiers,
            typeArguments,
            GetImplicitConversionCost,
            allowNullableStringCompatibility,
            IsBetterNullConversionTarget,
            (function, type) => arguments is not null
                && TryBuildClosedReferenceGeneric(type, function) is { } generic
                && generic.TypeArguments is [BoundTypes.NominalBoundType { Declaration: { } element } payload]
                    ? $"global::{generic.Definition.QualifiedName}<global::{element.QualifiedName}" +
                        $"{(payload.NullableAnnotation == BoundTypes.NullableAnnotation.Annotated ? "?" : "")}>" +
                        $"{(generic.NullableAnnotation == BoundTypes.NullableAnnotation.Annotated ? "?" : "")}"
                    : ResolveCallableParameterType(function, type),
            arguments is null ? null : (index, parameterType, argumentType) =>
                GetImplicitArgumentConversionCost(parameterType, argumentType, arguments[index]),
            arguments is null ? null : (index, candidateType, otherType) =>
                IsBetterIntegerLiteralConversionTarget(
                    arguments[index], argumentTypes[index], candidateType, otherType));
        return resolved.Function is { } winner && duplicates.TryGetValue(winner, out var ambiguous)
            ? OverloadResolutionResult.Ambiguous(ambiguous) : resolved;
    }

    private IReadOnlyList<FunctionSymbol>? GetInterfaceCallCandidates(
        BoundExpression? receiver, string? methodName, out bool opaque)
    {
        opaque = false;
        if (receiver?.Type is not BoundTypes.NominalBoundType { Declaration: { } identity }
            || !_interfacesBySymbolId.TryGetValue(identity.Id, out var root))
            return null;
        var candidates = new List<FunctionSymbol>();
        var pending = new Queue<(TypeSymbol Symbol, InterfaceDefinitionNode Declaration)>();
        var visited = new HashSet<SymbolId>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current.Symbol.Id))
                continue;
            if (current.Declaration.InteropBlocks.Count > 0 || current.Declaration.PreprocessorBlocks.Count > 0)
            {
                opaque = true;
                return [];
            }
            var declared = current.Declaration.Methods.Where(method =>
                GetCallableLookupName(method.Name) == methodName).ToArray();
            if (declared.Length > 0)
            {
                candidates.AddRange(declared.Select(method => _functionSymbols[method]));
                continue;
            }
            foreach (var parent in current.Declaration.BaseInterfaces)
            {
                var type = TryBuildReferenceType(parent, current.Symbol.QualifiedName,
                    current.Declaration.NamespaceIdentity);
                if (type?.Declaration is not { } parentIdentity
                    || !_interfacesBySymbolId.TryGetValue(parentIdentity.Id, out var inherited))
                {
                    opaque = true;
                    return [];
                }
                pending.Enqueue(inherited);
            }
        }
        // Every interface-typed value is an object, so C# also binds System.Object's
        // public instance members on it. They are not interface members, and their
        // implementation is whatever the runtime type overrides: opaque, never NoMatch.
        if (candidates.Count == 0
            && methodName is nameof(ToString) or nameof(Equals) or nameof(GetHashCode) or nameof(GetType))
        {
            opaque = true;
            return [];
        }
        return candidates.DistinctBy(candidate => candidate.Id).ToArray();
    }

    private bool IsBetterNullConversionTarget(string candidateType, string otherType)
    {
        static string StripNullableAnnotation(string typeName)
        {
            var type = typeName.Trim();
            while (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(type, out var referent))
                type = referent.Trim();
            return type;
        }

        var candidate = StripNullableAnnotation(candidateType);
        var other = StripNullableAnnotation(otherType);
        if (string.Equals(
                TypeIdentity.Canonicalize(candidate),
                TypeIdentity.Canonicalize(other),
                StringComparison.Ordinal)
            || !IsNullCompatibleParameterType(candidate)
            || !IsNullCompatibleParameterType(other))
        {
            return false;
        }

        return GetImplicitConversionCost(other, candidate) is not null
            && GetImplicitConversionCost(candidate, other) is null;
    }

    private int? GetImplicitArgumentConversionCost(
        string parameterType, string argumentType, BoundExpression argument)
    {
        var ordinaryCost = GetImplicitConversionCost(parameterType, argumentType);
        if (ordinaryCost is not null)
            return ordinaryCost;
        if (TryBuildClosedReferenceGeneric(parameterType) is { } receiving
            && (argument.GenericNullabilityType ?? argument.Type as BoundTypes.GenericInstantiationBoundType)
                is { } supplied
            && NullabilityChecker.IsReferenceContainerDefinition(supplied.Definition)
            && NullabilityChecker.SameGenericDefinition(
                receiving.Definition.QualifiedName, supplied.Definition.QualifiedName)
            && receiving.TypeArguments is [BoundTypes.NominalBoundType targetElement]
            && supplied.TypeArguments is [BoundTypes.NominalBoundType sourceElement]
            && targetElement.HasSameUnderlyingReferenceType(sourceElement))
            return 0;
        if (!TryGetSignedIntegerLiteral(argument, out var value))
            return null;

        return (TypeIdentity.Canonicalize(argumentType), TypeIdentity.Canonicalize(parameterType)) switch
        {
            ("INT", "INT[bits=8][signed=true]") when value is >= sbyte.MinValue and <= sbyte.MaxValue => 10,
            ("INT", "INT[bits=8][signed=false]") when value is >= byte.MinValue and <= byte.MaxValue => 10,
            ("INT", "INT[bits=16][signed=true]") when value is >= short.MinValue and <= short.MaxValue => 10,
            ("INT", "INT[bits=16][signed=false]") when value is >= ushort.MinValue and <= ushort.MaxValue => 10,
            ("INT", "UINT") when value is >= uint.MinValue and <= uint.MaxValue => 10,
            ("INT", "ULONG") when value >= 0 => 10,
            ("LONG", "ULONG") when value >= 0 => 10,
            _ => null
        };
    }

    private static bool TryGetSignedIntegerLiteral(BoundExpression expression, out long value)
    {
        switch (expression)
        {
            case BoundIntLiteral { IsUnsigned: false } literal:
                value = literal.Value;
                return true;
            case BoundUnaryExpression { Operator: UnaryOperator.Negate } unary
                when TryGetSignedIntegerLiteral(unary.Operand, out var operand) && operand != long.MinValue:
                value = -operand;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool IsBetterIntegerLiteralConversionTarget(
        BoundExpression argument, string argumentType, string candidateType, string otherType)
    {
        if (!TryGetSignedIntegerLiteral(argument, out _))
            return false;
        var candidate = TypeIdentity.Canonicalize(candidateType);
        var other = TypeIdentity.Canonicalize(otherType);
        var source = TypeIdentity.Canonicalize(argumentType);
        if (candidate == source)
            return true;
        if (other == source)
            return false;
        if (IsImplicitNumericConversion(candidate, other)
            && !IsImplicitNumericConversion(other, candidate))
            return true;
        // C# also prefers these signed targets when neither target converts to the other.
        return (candidate, other) switch
        {
            ("INT[bits=8][signed=true]", "INT[bits=8][signed=false]"
                or "INT[bits=16][signed=false]" or "UINT" or "ULONG") => true,
            ("INT[bits=16][signed=true]", "INT[bits=16][signed=false]" or "UINT" or "ULONG") => true,
            ("INT", "UINT" or "ULONG") => true,
            ("LONG", "ULONG") => true,
            _ => false
        };
    }

    /// <summary>
    /// Entry point kept two-argument so it converts to the
    /// <c>Func&lt;string, string, int?&gt;</c> overload-resolution expects.
    /// </summary>
    private int? GetImplicitConversionCost(string parameterType, string argumentType) =>
        GetImplicitConversionCost(parameterType, argumentType, visitedTypeParameters: null);

    /// <param name="visitedTypeParameters">
    /// v0.17 R2, round-4 finding — the type parameters already expanded on this
    /// path. `§WHERE T : U` together with `§WHERE U : T` is accepted by
    /// <c>ParseWhereClause</c>, and without this set the constraint recursion
    /// below chases the two forever: a StackOverflowException, which the
    /// Calor0932 handler cannot catch, so the process dies.
    /// </param>
    private int? GetImplicitConversionCost(
        string parameterType,
        string argumentType,
        HashSet<string>? visitedTypeParameters)
    {
        var parameter = TypeIdentity.Canonicalize(parameterType);
        var argument = TypeIdentity.Canonicalize(argumentType);
        if (string.Equals(parameter, argument, StringComparison.Ordinal))
            return 0;
        if (string.Equals(argument, "<unresolved>", StringComparison.Ordinal))
            return null;
        if (string.Equals(argument, "NULL", StringComparison.Ordinal))
            return IsNullCompatibleParameterType(parameterType) ? 40 : null;

        if (TypeIdentity.TrySplitGeneric(parameter, out var parameterDefinition, out var parameterArguments)
            && TypeIdentity.TrySplitGeneric(argument, out var argumentDefinition, out var argumentArguments)
            && NullabilityChecker.SameGenericDefinition(parameterDefinition, argumentDefinition)
            && parameterArguments.SequenceEqual(argumentArguments))
            return 0;

        var parameterIsArray = TrySplitArrayTypeSpelling(parameterType, out _, out _, out _);
        var argumentIsArray = TrySplitArrayTypeSpelling(argumentType, out _, out _, out _);
        if (parameterIsArray || argumentIsArray)
        {
            if (parameterIsArray && argumentIsArray)
                return HaveSameMethodInputArrayReferent(parameterType, argumentType) ? 0 : null;
            if (parameterIsArray || parameter != "OBJECT")
                return null;
        }

        if (parameter == "OBJECT" && argument != "VOID")
            return 50;

        if (IsImplicitNumericConversion(argument, parameter))
            return 10;

        // v0.17 R2. A generic type PARAMETER is compatible with anything its
        // constraints are. `Publish<TNotification>(TNotification n)` calling
        // `PublishNotification(INotification, ...)` is the single commonest
        // Calor0208 in the conversion corpus, and it failed here because the
        // walk below only knew about base classes: `TNotification` resolves to
        // no class at all, so the whole method returned null and overload
        // resolution reported "no matching overload" for a call C# accepts.
        if (GetTypeParameterConstraints(argumentType) is { Count: > 0 } constraints)
        {
            var expanded = visitedTypeParameters ?? new HashSet<string>(StringComparer.Ordinal);
            if (!expanded.Add(argumentType))
                return null;

            var best = constraints
                .Select(constraint => GetImplicitConversionCost(parameterType, constraint, expanded))
                .Where(cost => cost.HasValue)
                .Select(cost => cost!.Value)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            // +5 so a direct conversion always outranks one reached through a
            // constraint, and two candidates cannot tie on it.
            if (best != int.MaxValue)
                return best + 5;
        }

        var argumentClass = ResolveClass(argumentType);

        // v0.17 R2 — an implicit reference conversion to an INTERFACE the
        // argument implements is as ordinary as one to a base class, and the
        // walk below knew only about base classes: `Push(PropertyEnricher)`
        // against `Push(ILogEventEnricher)` was reported as no matching
        // overload. Checked by NAME and BEFORE the class-resolution guard,
        // because interfaces are `InterfaceDefinitionNode` and never enter
        // `_classesByQualifiedName` — resolving the parameter as a class returns
        // null for every interface, which is what made a first attempt at this
        // fix silently do nothing.
        if (argumentClass != null && ImplementsInterface(argumentClass, parameterType))
            return 30;

        var parameterClass = ResolveClass(parameterType);
        if (parameterClass == null || argumentClass == null)
            return null;

        var distance = 0;
        var current = argumentClass;
        var visited = new HashSet<ClassDefinitionNode>();
        while (visited.Add(current))
        {
            if (ReferenceEquals(current, parameterClass))
                return 20 + distance;

            var baseClass = ResolveBaseClass(current);
            if (baseClass == null)
                break;
            current = baseClass;
            distance++;
        }

        return null;
    }

    private bool IsNullCompatibleParameterType(string parameterType)
    {
        var type = parameterType.Trim();
        while (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(type, out var referent))
            type = referent.Trim();

        if (TrySplitArrayTypeSpelling(type, out _, out _, out _))
            return true;

        var canonical = TypeIdentity.Canonicalize(type);
        if (canonical is "STRING" or "OBJECT")
            return true;

        if (TryParseGenericStringTarget(type, out _))
            return true;

        if (TypeIdentity.TrySplitGeneric(type, out _, out _))
        {
            if (ResolveClass(type) is { } declaredClass)
                return !declaredClass.IsStruct;

            var lookupName = TypeIdentity.ToLookupName(type);
            var declaredTypes = SourceTypeSymbols()
                .Where(symbol => symbol.QualifiedName == lookupName)
                .Take(2)
                .ToArray();
            if (declaredTypes.Length != 0)
                return declaredTypes is [var declaration] && declaration.IsReferenceType;

            var metadataName = TypeIdentity.MapShortTypeNameToFullName(type);
            return GetOrCreateMetadataBinder()?.Context.TryResolveType(metadataName)
                is { IsReferenceType: true };
        }

        return TryBuildReferenceType(
            type,
            _currentClassName,
            _currentNamespaceIdentity,
            _currentMemberTypeParameters?.Select(parameter => parameter.Name)) is not null;
    }

    /// <summary>
    /// v0.17 R2 — does <paramref name="cls"/>, or any of its base classes,
    /// declare <paramref name="interfaceName"/> among its implemented
    /// interfaces? Compared on canonicalized NAMES: interfaces are
    /// <see cref="InterfaceDefinitionNode"/> and are not in the class registry,
    /// so there is no node to compare by reference.
    /// </summary>
    private bool ImplementsInterface(ClassDefinitionNode cls, string interfaceName)
    {
        var target = TypeIdentity.Canonicalize(interfaceName);
        var current = cls;
        var visitedClasses = new HashSet<ClassDefinitionNode>();
        while (current != null && visitedClasses.Add(current))
        {
            foreach (var declared in current.ImplementedInterfaces)
            {
                if (string.Equals(
                        TypeIdentity.Canonicalize(declared), target, StringComparison.Ordinal))
                    return true;
            }

            current = ResolveBaseClass(current);
        }

        return false;
    }

    /// <summary>
    /// v0.17 R2 — the type-constraint names on a generic type parameter of the
    /// enclosing class or of the function being bound, or <c>null</c> when
    /// <paramref name="name"/> is not a type parameter in scope. Only
    /// the three constraint kinds that NAME a type carry a conversion —
    /// <see cref="TypeConstraintKind.Interface"/>,
    /// <see cref="TypeConstraintKind.BaseClass"/> and
    /// <see cref="TypeConstraintKind.TypeName"/>. `new()`, `class`, `struct`,
    /// `notnull` and `unmanaged` name nothing to convert to.
    /// </summary>
    private IReadOnlyList<string>? GetTypeParameterConstraints(string name)
    {
        if (_currentClass == null && _currentMemberTypeParameters == null)
            return null;

        foreach (var parameter in Enumerable.Concat(
                     _currentMemberTypeParameters ?? Array.Empty<TypeParameterNode>(),
                     _currentClass?.TypeParameters ?? Array.Empty<TypeParameterNode>()))
        {
            if (!string.Equals(parameter.Name, name.TrimStart('@'), StringComparison.Ordinal))
                continue;
            return parameter.Constraints
                .Where(constraint => constraint.Kind is TypeConstraintKind.Interface
                                         or TypeConstraintKind.BaseClass
                                         or TypeConstraintKind.TypeName
                                     && !string.IsNullOrEmpty(constraint.TypeName))
                .Select(constraint => constraint.TypeName!)
                .ToArray();
        }

        return null;
    }

    /// <summary>
    /// v0.17 R2 — true when <paramref name="type"/> names a nominal type this
    /// module has no declaration for, so no claim about its convertibility can
    /// be justified. Builtins canonicalize to upper-case forms
    /// (<c>INT</c>, <c>STRING</c>, <c>OBJECT</c>, …) and are always visible; a
    /// type parameter in scope is visible through its constraints; anything else
    /// must resolve to a declared class.
    /// <para>Generic and array types are judged on their HEAD:
    /// <c>Seq&lt;Assembly&gt;</c> is visible if <c>Seq</c> is, because a wrong
    /// element type is a real mismatch the binder can still see.</para>
    /// </summary>
    private bool IsNominalTypeInvisibleToThisModule(string type)
    {
        var canonical = TypeIdentity.Canonicalize(type);
        // Nullable spellings must not suppress a failed overload.
        // Keep that rejection until #1382/#1383/#1385 hand it off.
        // This compatibility condition is not a claim that the referent was resolved.
        if (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(canonical, out _))
            return false;

        // Head of a generic/array form: `Seq<Assembly>` -> `Seq`.
        var head = canonical;
        var generic = head.IndexOf('<');
        if (generic > 0)
            head = head[..generic];
        head = StripArrayAndNullableDecorators(head);
        if (head.Length == 0)
            return false;

        // v0.17 R2, round-4 finding — this used to ask "is every letter upper
        // case?", which is false for every DECORATED canonical builtin:
        // `FLOAT[bits=32]` and `INT[bits=8][signed=true]` carry lower-case
        // `bits`/`signed`/`true`. Every call with an i8/u8/i16/u16/f32 argument
        // was therefore declared invisible to the module, which SUPPRESSES
        // Calor0208 — overload checking silently switched off for those types.
        // The canonical builtin set is the authority, not letter case.
        if (TypeIdentity.IsBuiltinCanonicalName(head))
            return false;

        if (GetTypeParameterConstraints(head) != null)
            return false;

        if (TryBuildReferenceType(head, _currentClassName, _currentNamespaceIdentity,
                _currentMemberTypeParameters?.Select(parameter => parameter.Name))
            is { RoslynSymbol.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_Object })
            return false;

        return ResolveClass(head) == null
            && ResolveSourceType(head, _currentClassName, _currentNamespaceIdentity, _currentNamespaceScopeId).Type
                is not { IsEnum: true };
    }

    /// <summary>
    /// Strips the postfix decorators <see cref="TypeIdentity.Canonicalize"/>
    /// preserves — <c>[]</c>, <c>[,]</c>, <c>?</c>, <c>*</c> — while leaving the
    /// KEYED brackets a canonical builtin carries (<c>INT[bits=8]</c>) alone. A
    /// blanket <c>TrimEnd('[', ']', '?', '*')</c> cannot tell the two apart.
    /// </summary>
    private static string StripArrayAndNullableDecorators(string canonical)
    {
        var head = canonical;
        while (head.Length > 0)
        {
            if (head[^1] is '?' or '*')
            {
                head = head[..^1];
                continue;
            }

            if (head[^1] == ']')
            {
                var open = head.LastIndexOf('[');
                if (open < 0)
                    break;

                var inner = head[(open + 1)..^1];
                if (inner.Length == 0 || inner.All(c => c == ','))
                {
                    head = head[..open];
                    continue;
                }
            }

            break;
        }

        return head;
    }

    private static bool IsImplicitNumericConversion(string from, string to) =>
        (from, to) switch
        {
            ("INT[bits=8][signed=true]", "INT[bits=16][signed=true]"
                or "INT"
                or "LONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("INT[bits=8][signed=false]", "INT[bits=16][signed=true]"
                or "INT[bits=16][signed=false]"
                or "INT"
                or "UINT"
                or "LONG"
                or "ULONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("INT[bits=16][signed=true]", "INT"
                or "LONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("INT[bits=16][signed=false]", "INT"
                or "UINT"
                or "LONG"
                or "ULONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("INT", "LONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("UINT", "LONG"
                or "ULONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("LONG", "FLOAT[bits=32]" or "FLOAT" or "DECIMAL") => true,
            ("ULONG", "FLOAT[bits=32]" or "FLOAT" or "DECIMAL") => true,
            ("FLOAT[bits=32]", "FLOAT") => true,
            _ => false,
        };

    private bool IsFunctionAccessible(FunctionSymbol function)
    {
        // Top-level functions belong to the ModuleNode, not to the generated
        // namespace-specific static class used by C# emission. Every top-level
        // declaration in this binding unit is therefore module-local and
        // accessible from every namespace scope in the same module.
        if (function.ContainingTypeName == null)
            return true;

        return function.Visibility switch
        {
            Visibility.Private =>
                IsWithinDeclaringType(function.ContainingTypeName),
            Visibility.Protected or Visibility.PrivateProtected =>
                _currentClass != null
                && IsSameOrDerivedClass(_currentClass, function.ContainingTypeName),
            _ => true,
        };
    }

    private VariableSymbol? ResolveAccessibleMember(
        ClassDefinitionNode? start,
        string memberName) =>
        ResolveAccessibleMembers(start, memberName).FirstOrDefault() as VariableSymbol;

    private IReadOnlyList<Symbol> ResolveAccessibleMembers(
        ClassDefinitionNode? start,
        string memberName)
    {
        var current = start;
        var visited = new HashSet<ClassDefinitionNode>();
        while (current != null && visited.Add(current))
        {
            var declared = _classScopes[current].LookupAllLocal(memberName);
            if (declared.Count > 0)
            {
                return declared.All(symbol =>
                        symbol is VariableSymbol variable
                        && IsMemberAccessible(variable))
                    ? declared
                    : Array.Empty<Symbol>();
            }

            current = ResolveBaseClass(current);
        }

        return Array.Empty<Symbol>();
    }

    private IReadOnlyList<Symbol> ResolveLexicalMembers(string memberName)
    {
        var declaredHere = ResolveAccessibleMembers(_currentClass, memberName);
        if (declaredHere.Count > 0 || memberName.Contains('.', StringComparison.Ordinal))
            return declaredHere;

        var enclosing = _currentClass;
        while (enclosing != null && _containingClasses.TryGetValue(enclosing, out enclosing))
        {
            var declared = _classScopes[enclosing].LookupAllLocal(memberName);
            if (declared.Count > 0)
            {
                return declared.All(symbol => symbol is VariableSymbol { IsStatic: true } variable
                        && IsMemberAccessible(variable))
                    ? declared
                    : Array.Empty<Symbol>();
            }
        }
        return Array.Empty<Symbol>();
    }

    private IReadOnlyList<Symbol> ResolveReadableInstanceProperties(ClassDefinitionNode receiverClass, string memberName)
    {
        var members = ResolveAccessibleMembers(receiverClass, memberName);
        return members.All(member => member is VariableSymbol { IsProperty: true, IsStatic: false } property
                && HasAccessiblePropertyGetter(property, receiverClass))
            ? members : Array.Empty<Symbol>();
    }

    private IReadOnlyList<Symbol> ResolveReadableInstanceMembers(ClassDefinitionNode receiverClass, string memberName)
    {
        var members = ResolveAccessibleMembers(receiverClass, memberName);
        if (members is [VariableSymbol { IsField: true, IsStatic: false } field]
            && field.DeclaringTypeName is { } declaringType
            && _classesByQualifiedName.TryGetValue(declaringType, out var owner)
            && !owner.IsPartial && owner.PreprocessorBlocks.Count == 0 && owner.InteropBlocks.Count == 0
            && EnumerateFields(owner).Count(declaration => declaration.Name == memberName) == 1
            && (field.Visibility is not (Visibility.Protected or Visibility.PrivateProtected)
                || _currentClass != null
                    && IsSameOrDerivedClass(receiverClass, _qualifiedClassNames[_currentClass])))
            return members;
        return ResolveReadableInstanceProperties(receiverClass, memberName);
    }

    private bool HasAccessiblePropertyGetter(VariableSymbol member, ClassDefinitionNode receiverClass)
    {
        if (member.DeclaringTypeName == null
            || !_classesByQualifiedName.TryGetValue(member.DeclaringTypeName, out var declaringClass))
            return false;

        var declarations = EnumerateProperties(declaringClass)
            .Where(property => property.Name == member.Name && property.IdentifierSpan == member.DeclarationSpan)
            .ToArray();
        if (declarations is not [{ Getter: { } getter } property])
            return false;

        var visibility = getter.Visibility ?? property.Visibility;
        return IsMemberAccessible(visibility, member.DeclaringTypeName)
            && (visibility is not (Visibility.Protected or Visibility.PrivateProtected)
                || _currentClass != null
                    && IsSameOrDerivedClass(receiverClass, _qualifiedClassNames[_currentClass]));
    }

    private bool IsMemberAccessible(VariableSymbol member) =>
        member.DeclaringTypeName == null || IsMemberAccessible(member.Visibility, member.DeclaringTypeName);

    private bool IsMemberAccessible(Visibility visibility, string declaringTypeName)
    {
        return visibility switch
        {
            Visibility.Private =>
                IsWithinDeclaringType(declaringTypeName),
            Visibility.Protected or Visibility.PrivateProtected =>
                _currentClass != null
                && IsSameOrDerivedClass(_currentClass, declaringTypeName),
            Visibility.Internal or Visibility.ProtectedInternal => true,
            Visibility.Public => true,
            _ => false,
        };
    }

    private bool IsWithinDeclaringType(string declaringTypeName)
    {
        var current = _currentClass;
        while (current != null)
        {
            if (string.Equals(_qualifiedClassNames[current], declaringTypeName, StringComparison.Ordinal))
                return true;
            if (!_containingClasses.TryGetValue(current, out current))
                break;
        }
        return false;
    }

    private bool IsSameOrDerivedClass(
        ClassDefinitionNode cls,
        string expectedBaseName)
    {
        var current = cls;
        var visited = new HashSet<ClassDefinitionNode>();
        while (visited.Add(current))
        {
            if (string.Equals(
                    _qualifiedClassNames[current],
                    expectedBaseName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            var baseClass = ResolveBaseClass(current);
            if (baseClass == null)
                return false;
            current = baseClass;
        }

        return false;
    }

    private bool IsEnclosingLexicalCall(string target, string lookupName)
    {
        if (target.Contains('.', StringComparison.Ordinal) || _currentClass is null
            || EnumerateHierarchyLookupNames(_currentClass, target).Contains(lookupName, StringComparer.Ordinal))
            return false;

        var enclosing = _currentClass;
        while (_containingClasses.TryGetValue(enclosing, out enclosing))
        {
            if (EnumerateHierarchyLookupNames(enclosing, target).Contains(lookupName, StringComparer.Ordinal))
                return true;
        }
        return false;
    }

    private IEnumerable<string> GetCallLookupNames(string target)
    {
        const string globalPrefix = "global::";
        if (target.StartsWith(globalPrefix, StringComparison.Ordinal))
        {
            yield return target[globalPrefix.Length..];
            yield break;
        }

        if (target.StartsWith("this.", StringComparison.Ordinal) && _currentClassName != null)
        {
            foreach (var lookupName in EnumerateHierarchyLookupNames(
                         _currentClass,
                         target["this.".Length..]))
            {
                yield return lookupName;
            }
            yield break;
        }

        if (target.StartsWith("base.", StringComparison.Ordinal))
        {
            var baseClass = ResolveBaseClass(_currentClass);
            foreach (var lookupName in EnumerateHierarchyLookupNames(
                         baseClass,
                         target["base.".Length..]))
            {
                yield return lookupName;
            }
            yield break;
        }

        var firstDot = target.IndexOf('.');
        if (firstDot <= 0)
        {
            // 0.25 F3 (#847): C# simple-name lookup: a local/parameter is invoked as a
            // value; a visible local function wins over members and module functions.
            if (_scope.Lookup(target) is VariableSymbol { DeclaringTypeName: null }
                || _scope.Lookup(target) is VariableSymbol { StorageIdentity.EventDeclaration: not null }
                || ResolveLexicalMembers(target).OfType<VariableSymbol>()
                    .Any(variable => variable.EventDeclaration is not null))
                yield break;
            if (_scope.GetOverloads(target).Any(_localFunctionSymbols.Contains))
            {
                yield return target;
                yield break;
            }

            if (_currentClass != null)
            {
                foreach (var lookupName in EnumerateHierarchyLookupNames(
                             _currentClass,
                             target))
                {
                    yield return lookupName;
                }
                var enclosing = _currentClass;
                while (_containingClasses.TryGetValue(enclosing, out enclosing))
                {
                    foreach (var lookupName in EnumerateHierarchyLookupNames(enclosing, target))
                        yield return lookupName;
                }
            }

            if (!string.IsNullOrEmpty(_currentNamespaceIdentity))
                yield return $"{_currentNamespaceIdentity}.{target}";

            if (_topLevelFunctionLookupNames.TryGetValue(
                    GetCallableLookupName(target),
                    out var qualifiedLookupNames)
                && qualifiedLookupNames.Count == 1)
            {
                yield return qualifiedLookupNames.Single();
            }

            yield return target;
            yield break;
        }

        var receiverName = target[..firstDot];
        var receiver = _scope.LookupAll(receiverName)
            .OfType<VariableSymbol>()
            .FirstOrDefault();
        if (receiver != null)
        {
            var receiverClass = ResolveClass(receiver.TypeName);
            if (receiverClass != null)
            {
                foreach (var lookupName in EnumerateHierarchyLookupNames(
                             receiverClass,
                             target[(firstDot + 1)..]))
                {
                    yield return lookupName;
                }
            }
            else
            {
                yield return target;
            }
            yield break;
        }

        var typeReceiverName = GetTypeQualifiedCallReceiver(target);
        if (typeReceiverName != null)
        {
            var receiverClass = ResolveClass(typeReceiverName);
            if (receiverClass != null)
            {
                yield return
                    $"{_qualifiedClassNames[receiverClass]}{target[typeReceiverName.Length..]}";
                yield break;
            }
        }

        yield return target;
    }

    private IEnumerable<string> EnumerateHierarchyLookupNames(
        ClassDefinitionNode? start,
        string memberName)
    {
        var current = start;
        var visited = new HashSet<ClassDefinitionNode>();
        while (current != null && visited.Add(current))
        {
            yield return $"{_qualifiedClassNames[current]}.{memberName}";
            current = ResolveBaseClass(current);
        }
    }

    private TypeSymbol? ResolveTypeSymbol(
        string typeName,
        int? explicitArity = null)
    {
        var cls = ResolveClass(typeName, explicitArity);
        return cls == null ? null : _classSymbols[cls];
    }

    private ClassDefinitionNode? ResolveBaseClass(ClassDefinitionNode? cls) =>
        cls?.BaseClass is { Length: > 0 } baseClass ? ResolveClass(baseClass) : null;

    private ClassDefinitionNode? ResolveClass(
        string typeName,
        int? explicitArity = null)
    {
        var lookupName = TypeIdentity.ToLookupName(
            typeName,
            explicitArity);
        if (_classesByQualifiedName.TryGetValue(lookupName, out var qualified))
            return qualified;

        if (!string.IsNullOrEmpty(_currentNamespaceIdentity))
        {
            var namespaceCandidate =
                $"{_currentNamespaceIdentity}.{lookupName}";
            if (_classesByQualifiedName.TryGetValue(
                    namespaceCandidate,
                    out var namespaced))
            {
                return namespaced;
            }
        }

        if (_currentClassName != null)
        {
            var containingSeparator = _currentClassName.LastIndexOf('.');
            if (containingSeparator > 0)
            {
                var nestedCandidate =
                    $"{_currentClassName[..containingSeparator]}.{lookupName}";
                if (_classesByQualifiedName.TryGetValue(nestedCandidate, out var nested))
                    return nested;
            }
        }

        return _classesBySimpleName.TryGetValue(lookupName, out var matches) && matches.Count == 1
            ? matches[0]
            : null;
    }

    private static string FormatCallSignature(
        IReadOnlyList<string> argumentTypes,
        IReadOnlyList<string?>? argumentModifiers,
        IReadOnlyList<string>? typeArguments)
    {
        var generic = typeArguments == null
            ? string.Empty
            : $"<{string.Join(",", typeArguments.Select(TypeIdentity.Canonicalize))}>";
        var arguments = argumentTypes.Select((type, index) =>
        {
            var modifier = argumentModifiers != null && index < argumentModifiers.Count
                ? argumentModifiers[index]
                : null;
            return $"{(string.IsNullOrWhiteSpace(modifier) ? string.Empty : modifier + " ")}" +
                   TypeIdentity.Canonicalize(type);
        });
        return $"{generic}({string.Join(", ", arguments)})";
    }

    private static string FormatCandidates(IReadOnlyList<FunctionSymbol> candidates) =>
        string.Join(
            ", ",
            candidates.Select(candidate =>
                $"{candidate.DisplaySignature} [{candidate.Id}]"));

    private BoundExpression BindConditionalExpression(ConditionalExpressionNode condExpr)
    {
        var condition = BindExpression(condExpr.Condition);
        BoundExpression whenTrue;
        using (PushScope(_scope.CreateChild()))
        {
            DeclareSuccessfulConditionPatterns(condExpr.Condition, condition);
            whenTrue = BindExpression(condExpr.WhenTrue);
        }
        var whenFalse = BindExpression(condExpr.WhenFalse);

        return new BoundConditionalExpression(
            condExpr.Span,
            condition,
            whenTrue,
            whenFalse,
            GetExpressionCommonType(whenTrue.Type.DisplayString, whenFalse.Type.DisplayString));
    }

    private static string GetExpressionCommonType(string leftType, string rightType)
    {
        var left = Parsing.AttributeHelper.TryUnwrapNullableAnnotation(leftType, out var leftReferent)
            ? leftReferent
            : leftType;
        var right = Parsing.AttributeHelper.TryUnwrapNullableAnnotation(rightType, out var rightReferent)
            ? rightReferent
            : rightType;
        return GetCommonType(left, right);
    }

    private static BoundStructuralExpression Structural(
        ExpressionNode expression,
        string typeName,
        IReadOnlyList<BoundExpression>? children = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        IReadOnlyList<BoundExpression>? deferredChildren = null,
        BoundTypes.NullableAnnotation typeAnnotation = BoundTypes.NullableAnnotation.Oblivious)
    {
        return new BoundStructuralExpression(
            expression.Span,
            expression.GetType().Name,
            typeName,
            children,
            metadata,
            deferredChildren,
            typeAnnotation);
    }

    private BoundUnsupportedExpression BindUnsupportedExpression(
        ExpressionNode expression,
        string typeName = "OBJECT",
        IReadOnlyList<BoundExpression>? children = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        string reason = "Analysis support is incomplete")
    {
        children ??= BindReflectedExpressionChildren(expression);
        ReportAnalysisIncomplete(expression, reason);
        return new BoundUnsupportedExpression(
            expression.Span,
            expression.GetType().Name,
            typeName,
            children,
            metadata,
            reason);
    }

    private IReadOnlyList<BoundExpression> BindReflectedExpressionChildren(ExpressionNode expression)
    {
        var children = new List<BoundExpression>();
        foreach (var property in expression.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length != 0 || property.Name == nameof(AstNode.Span))
                continue;

            var value = property.GetValue(expression);
            if (value is ExpressionNode child)
            {
                children.Add(BindExpression(child));
            }
            else if (value is IEnumerable<ExpressionNode> expressionChildren)
            {
                children.AddRange(BindExpressions(expressionChildren));
            }
        }
        return children;
    }

    private void ReportAnalysisIncomplete(ExpressionNode expression, string detail)
    {
        var typeName = expression.GetType().Name;
        if (_unsupportedNodeTypes.Add(typeName))
        {
            _diagnostics.ReportInfo(
                expression.Span,
                DiagnosticCode.AnalysisUnsupportedNode,
                $"Expression type '{typeName}' has incomplete analysis support; {detail}");
        }
    }

    private IReadOnlyList<BoundExpression> BindExpressions(IEnumerable<ExpressionNode> expressions) =>
        expressions.Select(BindExpression).ToArray();

    /// <summary>
    /// #943 (D1 RO-LV-1/2, RO-DECL-2): binds call arguments and refuses, with
    /// <see cref="DiagnosticCode.InvalidByReferenceOperand"/>, every ref/out/in operand C# cannot
    /// pass by reference. <c>§A{out} _</c> with no symbol <c>_</c> in scope is a discard.
    /// </summary>
    private IReadOnlyList<BoundExpression> BindCallArguments(
        IReadOnlyList<ExpressionNode> arguments, IReadOnlyList<string?>? modifiers)
    {
        var bound = new BoundExpression[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            var modifier = modifiers != null && index < modifiers.Count ? modifiers[index] : null;
            if (string.IsNullOrEmpty(modifier))
            {
                bound[index] = BindExpression(arguments[index]);
                continue;
            }
            string? reason;
            if (arguments[index] is ReferenceNode { Name: "_", IsVerbatim: false } discard)
            {
                var declared = _scope.LookupAll("_").Count > 0 || ResolveLexicalMembers("_").Count > 0;
                reason = modifier != "out" ? $"'{modifier} _' is not a discard; only 'out _' is"
                    : declared ? "'out _' would bind to the declared symbol '_', not discard" : null;
                bound[index] = declared ? BindExpression(discard) : new BoundVariableExpression(discard.Span, CreateUnresolvedVariable(discard));
            }
            else
            {
                bound[index] = BindExpression(arguments[index]);
                reason = GetInvalidByReferenceReason(bound[index], modifier is "ref" or "out");
                // #1550: ref/out keep parity with §ASSIGN (maintainer decision, 2026-10-11).
                if (reason == null && modifier is "ref" or "out")
                    ReportWriteToReadOnlyLocal(bound[index], arguments[index].Span, $"pass {{0}} as a '{modifier}' argument");
            }
            if (reason != null)
            {
                _diagnostics.ReportError(arguments[index].Span, DiagnosticCode.InvalidByReferenceOperand,
                    $"Argument {index + 1} cannot be passed '{modifier}': {reason}. " +
                    "Pass a local, parameter, field or array element, or bind the value to a mutable local first.");
            }
        }
        return bound;
    }

    private string? GetInvalidByReferenceReason(BoundExpression operand, bool writes)
    {
        switch (operand)
        {
            // A dotted name the binder left unresolved (`s.F.N`, `other.n`, `this.n`, `K.slot`).
            case BoundVariableExpression { Variable: { Id.IsNone: true, Name: var dotted } } when dotted.IndexOf('.') > 0:
                var head = dotted[..dotted.IndexOf('.')];
                var rest = dotted[(head.Length + 1)..].Split('.');
                if (head == "this" && _currentClassName != null)
                    return MemberPathReason(new BoundThisExpression(operand.Span, _currentClassName), _currentClassName, rest, writes);
                if (_scope.Lookup(head) is VariableSymbol root)
                {
                    if (IsScalarStringTypeName(root.TypeName) || IsBuiltInValueTypeName(TypeIdentity.Canonicalize(root.TypeName)))
                        return $"'{dotted}' is a member of a built-in type, not a variable";
                    return MemberPathReason(new BoundVariableExpression(operand.Span, root), root.TypeName, rest, writes);
                }
                if (rest.Length != 1)
                    return null;
                // Static members of a built-in type (`Int32.MaxValue`, `String.Empty`) are never variables.
                if (TypeIdentity.IsBuiltinCanonicalName(TypeIdentity.Canonicalize(head)) && TypeIdentity.Canonicalize(head) != "OBJECT")
                    return $"'{dotted}' is a member of a built-in type, not a variable";
                if (ResolveClass(head) is { } staticOwner)
                    return StaticMemberReason(ResolveAccessibleMembers(staticOwner, rest[0]).OfType<VariableSymbol>().FirstOrDefault(), rest[0], writes);
                return MetadataMemberReason(
                    (TryBindMetadataTypeReceiver(new ReferenceNode(operand.Span, head))?.Type as BoundTypes.NominalBoundType)?.RoslynSymbol?.GetMembers(rest[0]).FirstOrDefault(),
                    rest[0], writes);
            case BoundVariableExpression { Variable: var variable }:
                if (variable.IsProperty)
                    return $"'{variable.Name}' is a property";
                if (writes && variable.IsField && !variable.IsMutable && !IsWritableReadonlyField(variable))
                    return $"'{variable.Name}' is a readonly field outside its class's constructor";
                if (writes && variable.IsParameter && variable.Modifier.HasFlag(ParameterModifier.In))
                    return $"'{variable.Name}' is an 'in' parameter, which is read-only";
                return null;
            case BoundFieldAccessExpression { Target: BoundTypeReferenceExpression typeTarget } field:
                // A static member: in-module (by symbol) or metadata (const, readonly, property).
                if (ResolveClass(typeTarget.Name) is { } inModule)
                    return StaticMemberReason(ResolveAccessibleMembers(inModule, field.FieldName).OfType<VariableSymbol>().FirstOrDefault(), field.FieldName, writes);
                return MetadataMemberReason(field.ResolvedMetadataMember
                    ?? (typeTarget.Type as BoundTypes.NominalBoundType)?.RoslynSymbol?.GetMembers(field.FieldName).FirstOrDefault(), field.FieldName, writes);
            case BoundFieldAccessExpression field:
                if (MemberPath(field.Target.Type.DisplayString, field.FieldName.Split('.')) is not null)
                    return MemberPathReason(field.Target, field.Target.Type.DisplayString, field.FieldName.Split('.'), writes);
                var metadataMember = MetadataMemberReason(field.ResolvedMetadataMember ?? field.UnsupportedMetadataMember, field.FieldName, writes);
                // A metadata field of a value-type receiver is storage only when the receiver is.
                return metadataMember ?? ((field.ResolvedMetadataMember ?? field.UnsupportedMetadataMember) is Microsoft.CodeAnalysis.IFieldSymbol { ContainingType.IsValueType: true }
                    && GetInvalidByReferenceReason(field.Target, writes) is { } receiver
                        ? $"its value-type receiver is not writable storage: {receiver}" : null);
            case BoundArrayAccess access:
                return IsKnownNonArrayType(access.Array.Type) ? "an indexer element is not a variable" : null;
            case BoundArrayAccessExpression access:
                return IsKnownNonArrayType(access.Array.Type) ? "an indexer element is not a variable" : null;
            case BoundMultiDimArrayAccess access:
                return IsKnownNonArrayType(access.Array.Type) ? "an indexer element is not a variable" : null;
            case BoundThisExpression:
                return "'this' cannot be passed by reference";
            default:
                return "the operand is a value (a literal, computed expression or call result), not a variable";
        }
    }

    /// <summary>
    /// #943 (D1 RO-LV-1/2): the in-module instance members a dotted path names from a type, or
    /// null when a segment is not an in-module field or property.
    /// </summary>
    private List<VariableSymbol>? MemberPath(string typeName, IReadOnlyList<string> segments)
    {
        var path = new List<VariableSymbol>();
        foreach (var segment in segments)
        {
            if (ResolveClass(path.Count == 0 ? typeName : path[^1].TypeName) is not { } owner
                || ResolveAccessibleMembers(owner, segment).OfType<VariableSymbol>().FirstOrDefault(m => !m.IsStatic) is not { } member)
                return null;
            path.Add(member);
        }
        return path;
    }

    /// <summary>
    /// A member path is storage when its last member is a writable field and, while each
    /// member lives inside a struct value, its container is writable storage too. Crossing a
    /// reference-type owner ends the walk: that storage is on the heap.
    /// </summary>
    private string? MemberPathReason(BoundExpression root, string rootType, IReadOnlyList<string> segments, bool writes)
    {
        if (MemberPath(rootType, segments) is not { } path)
            return null;
        for (var index = path.Count - 1; index >= 0; index--)
        {
            var member = path[index];
            if (member.IsProperty)
                return $"'{member.Name}' is a property";
            if (writes && member.IsField && !member.IsMutable && !(index == 0 && root is BoundThisExpression && IsWritableReadonlyField(member)))
                return $"'{member.Name}' is a readonly field outside its constructor or of another instance";
            if (ResolveClass(member.DeclaringTypeName ?? "") is not { IsStruct: true })
                return null;
        }
        return root is not BoundThisExpression && GetInvalidByReferenceReason(root, writes) is { } reason
            ? $"its value-type receiver is not writable storage: {reason}" : null;
    }

    private static string? MetadataMemberReason(Microsoft.CodeAnalysis.ISymbol? member, string name, bool writes) => member switch
    {
        Microsoft.CodeAnalysis.IPropertySymbol => $"'{name}' is a property",
        Microsoft.CodeAnalysis.IFieldSymbol { IsConst: true } => $"'{name}' is a constant",
        Microsoft.CodeAnalysis.IFieldSymbol { IsReadOnly: true } when writes => $"'{name}' is a readonly field",
        _ => null,
    };

    private string? StaticMemberReason(VariableSymbol? member, string name, bool writes) => member switch
    {
        { IsProperty: true } => $"'{name}' is a property",
        { IsField: true, IsMutable: false } when writes && !IsWritableReadonlyField(member) => $"'{name}' is a readonly field outside its static constructor",
        _ => null,
    };

    private bool IsWritableReadonlyField(VariableSymbol field) =>
        _currentConstructor is { } ctor && ctor.IsStatic == field.IsStatic
        && string.Equals(field.DeclaringTypeName, _currentClassName, StringComparison.Ordinal);

    private bool IsKnownNonArrayType(BoundTypes.BoundType type)
    {
        if (type is BoundTypes.ArrayBoundType or BoundTypes.UnresolvedBoundType)
            return false;
        var name = type.DisplayString;
        return !name.EndsWith(']') && (type is BoundTypes.GenericInstantiationBoundType
            || type is BoundTypes.NominalBoundType { RoslynSymbol: not null } || ResolveClass(name) is not null
            || name is "STRING" or "str" or "string" or "System.String"
            || name.StartsWith("List<", StringComparison.Ordinal) || name.StartsWith("Dict", StringComparison.Ordinal));
    }

    /// <summary>
    /// #943 (D1 RO-NULL-2, RO-ANA-1's alias condition): in a callable with by-reference parameters,
    /// anything that may change the heap (a call, construction, element/field/property read or store)
    /// may write storage a by-reference parameter aliases.
    /// </summary>
    private bool MayChangeHeap(BoundExpression bound) =>
        _byReferenceParameters.Count > 0 && bound switch
        {
            // Only these kinds run no code and read no shared storage of their own; every other
            // kind (calls of any form, construction, element/field/property reads) may.
            BoundIntLiteral or BoundStringLiteral or BoundBoolLiteral or BoundFloatLiteral or BoundDecimalLiteral
                or BoundNullLiteral or BoundLambdaExpression => false,
            BoundThrowExpression { Exception: BoundNullLiteral } => false,
            BoundStructuralExpression
                { NodeTypeName: nameof(NullCoalesceNode), Children: [var receiver, BoundThrowExpression { Exception: BoundNullLiteral }] } =>
                MayChangeHeap(receiver),
            BoundFieldAccessExpression
                {
                    ResolvedMetadataMember: Microsoft.CodeAnalysis.IPropertySymbol
                    {
                        Name: "Length", IsStatic: false, Parameters.Length: 0,
                        ContainingType.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String
                    },
                    Target: var receiver
                } => MayChangeHeap(receiver),
            // An operator on a user type may be an overloaded operator method.
            BoundBinaryExpression binary => !IsBuiltInOperand(binary.Left) || !IsBuiltInOperand(binary.Right),
            // A built-in string or character operation (`len`, `upper`) over built-in operands runs no user code.
            BoundStructuralExpression { Children: var operands } and not BoundUnsupportedExpression => !operands.All(IsBuiltInOperand),
            BoundUnaryExpression unary => !IsBuiltInOperand(unary.Operand),
            // A dotted or unresolved name (`this.P`, an inherited member) may run a getter.
            BoundVariableExpression { Variable: var variable } =>
                variable.IsField || variable.IsProperty || variable.Id.IsNone || variable.Name.Contains('.'),
            _ => true,
        };

    private static bool IsBuiltInOperand(BoundExpression operand) =>
        operand is BoundNullLiteral || IsScalarStringTypeName(operand.Type.DisplayString)
        || IsBuiltInValueTypeName(TypeIdentity.Canonicalize(operand.Type.DisplayString));

    /// <summary>#943 (D1 RO-NULL-2): a statement that writes storage a by-reference parameter may alias.</summary>
    private bool WritesAliasedStorage(StatementNode statement, BoundStatement? bound)
    {
        if (_byReferenceParameters.Count == 0)
            return false;
        if (bound?.DirectWriteTarget is { } target)
            return AssignsAliasedStorage(target);
        return statement is not (IfStatementNode or WhileStatementNode or ForStatementNode or DoWhileStatementNode
            or TryStatementNode or MatchStatementNode or BindStatementNode or ReturnStatementNode or BreakStatementNode
            or ContinueStatementNode or GotoStatementNode or LabelStatementNode or ProofObligationNode
            or ThrowStatementNode or RethrowStatementNode or ExpressionStatementNode or PrintStatementNode
            or YieldReturnStatementNode or YieldBreakStatementNode or ForeachStatementNode or UsingStatementNode
            or CallStatementNode or SyncBlockNode or FallbackCommentNode or PreprocessorDirectiveNode);
    }

    private static IEnumerable<BoundNode> DescendantsAndSelf(BoundNode node) =>
        (node is BoundPreprocessorBlock preprocessor // #1678: either branch may run
            ? preprocessor.Branches.SelectMany(branch => branch)
            : node.ChildNodes).SelectMany(DescendantsAndSelf).Prepend(node);

    private static IEnumerable<string> ByReferenceWrittenNames(IReadOnlyList<string?> modifiers, IReadOnlyList<BoundExpression> arguments) =>
        modifiers.Select((modifier, index) => modifier is "ref" or "out" && index < arguments.Count
                && arguments[index] is BoundVariableExpression variable ? variable.Variable.Name : null)
            .OfType<string>();

    private bool AssignsAliasedStorage(BoundExpression target) =>
        target is not BoundVariableExpression { Variable: var variable }
        || variable.IsField || variable.IsProperty
        || _byReferenceParameters.Count > 1 && _byReferenceParameters.Contains(RootOf(variable));

    private VariableSymbol RootOf(VariableSymbol variable)
    {
        while (_flowNarrowingSources.TryGetValue(variable, out var previous))
            variable = previous;
        return variable;
    }

    private void InvalidateByReferenceParameterNarrowing()
    {
        _aliasInvalidations++;
        foreach (var parameter in _byReferenceParameters)
        {
            InvalidateMemberFlowNarrowing(parameter);
            if (_scope.Lookup(parameter.Name) is VariableSymbol visible
                && !ReferenceEquals(visible, parameter) && ReferenceEquals(RootOf(visible), parameter))
            {
                InvalidateMutableFlowNarrowing(visible, requireRebindable: false);
            }
        }
    }

    private static string MakeArrayType(string elementType, int rank) =>
        $"{elementType}[{new string(',', Math.Max(0, rank - 1))}]";

    private static string? GetIndexedElementType(string typeName)
    {
        const string arrayPrefix = "ARRAY[element=";
        if (typeName.StartsWith(arrayPrefix, StringComparison.OrdinalIgnoreCase)
            && typeName.EndsWith(']'))
            return typeName[arrayPrefix.Length..^1];

        if (typeName.StartsWith('[') && typeName.EndsWith(']') && typeName.Length > 2)
            return typeName[1..^1];

        var bracket = typeName.LastIndexOf('[');
        if (bracket > 0 && typeName.EndsWith(']'))
            return typeName[..bracket];

        var genericStart = typeName.IndexOf('<');
        if (genericStart > 0 && typeName.EndsWith('>'))
        {
            var genericName = typeName[..genericStart];
            var arguments = SplitTopLevelTypeArguments(typeName[(genericStart + 1)..^1]);
            if (genericName.Contains("Dictionary", StringComparison.OrdinalIgnoreCase)
                || genericName.Equals("Dict", StringComparison.OrdinalIgnoreCase))
                return arguments.Count > 1 ? arguments[1] : null;
            return arguments.Count > 0 ? arguments[0] : null;
        }

        return null;
    }

    private static IReadOnlyList<string> SplitTopLevelTypeArguments(string text)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '<':
                case '[':
                    depth++;
                    break;
                case '>':
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(text[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }
        result.Add(text[start..].Trim());
        return result;
    }

    private static string GetCommonType(string leftType, string rightType)
    {
        var left = NormalizeTypeName(leftType);
        var right = NormalizeTypeName(rightType);

        if (left == right)
            return left;
        if (left == "NEVER")
            return right;
        if (right == "NEVER")
            return left;
        if (left == "OBJECT" || right == "OBJECT")
            return "OBJECT";

        var leftRank = GetNumericRank(left);
        var rightRank = GetNumericRank(right);
        if (leftRank >= 0 && rightRank >= 0)
        {
            if ((left == "DECIMAL" && right.StartsWith("FLOAT", StringComparison.Ordinal))
                || (right == "DECIMAL" && left.StartsWith("FLOAT", StringComparison.Ordinal)))
                return "OBJECT";
            return leftRank >= rightRank ? left : right;
        }

        return "OBJECT";
    }

    private static string? GetCommonType(IEnumerable<string>? types)
    {
        if (types == null)
            return null;

        using var enumerator = types.GetEnumerator();
        if (!enumerator.MoveNext())
            return null;

        var result = enumerator.Current;
        while (enumerator.MoveNext())
            result = GetCommonType(result, enumerator.Current);
        return NormalizeTypeName(result);
    }

    private static int GetNumericRank(string typeName) => typeName switch
    {
        "INT[bits=8][signed=true]" or "INT[bits=8][signed=false]" => 0,
        "INT[bits=16][signed=true]" or "INT[bits=16][signed=false]" => 1,
        "INT" or "UINT" => 2,
        "LONG" or "ULONG" or "INT[bits=64][signed=true]" or "INT[bits=64][signed=false]" => 3,
        "FLOAT[bits=32]" => 4,
        "FLOAT" => 5,
        "DECIMAL" => 6,
        _ => -1,
    };

    private static string NormalizeTypeName(string typeName)
    {
        var trimmed = typeName.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "i8" or "sbyte" => "INT[bits=8][signed=true]",
            "int[bits=8][signed=true]" => "INT[bits=8][signed=true]",
            "u8" or "byte" => "INT[bits=8][signed=false]",
            "int[bits=8][signed=false]" => "INT[bits=8][signed=false]",
            "i16" or "short" => "INT[bits=16][signed=true]",
            "int[bits=16][signed=true]" => "INT[bits=16][signed=true]",
            "u16" or "ushort" => "INT[bits=16][signed=false]",
            "int[bits=16][signed=false]" => "INT[bits=16][signed=false]",
            "i32" or "int" or "int32" => "INT",
            "int[bits=32][signed=true]" => "INT",
            "u32" or "uint" or "uint32" => "UINT",
            "int[bits=32][signed=false]" => "UINT",
            "i64" or "long" or "int64" => "LONG",
            "int[bits=64][signed=true]" => "LONG",
            "u64" or "ulong" or "uint64" => "ULONG",
            "int[bits=64][signed=false]" => "ULONG",
            "f32" or "single" => "FLOAT[bits=32]",
            "float[bits=32]" => "FLOAT[bits=32]",
            // Only the lowercase C# keyword is single precision; `FLOAT` is the canonical double
            // (see TypeIdentity.Canonicalize).
            "float" when trimmed == "float" => "FLOAT[bits=32]",
            "f64" or "float" or "double" => "FLOAT",
            "dec" or "decimal" => "DECIMAL",
            "str" or "string" => "STRING",
            "bool" or "boolean" => "BOOL",
            "any" or "object" or "unknown" => "OBJECT",
            "never" => "NEVER",
            _ => trimmed,
        };
    }

    private static IEnumerable<string> CollectReturnTypes(IReadOnlyList<BoundStatement>? statements)
    {
        if (statements == null)
            yield break;

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BoundReturnStatement { Expression: not null } returnStatement:
                    yield return returnStatement.Expression.Type.DisplayString;
                    break;
                case BoundIfStatement ifStatement:
                    foreach (var type in CollectReturnTypes(ifStatement.ThenBody))
                        yield return type;
                    foreach (var clause in ifStatement.ElseIfClauses)
                        foreach (var type in CollectReturnTypes(clause.Body))
                            yield return type;
                    foreach (var type in CollectReturnTypes(ifStatement.ElseBody))
                        yield return type;
                    break;
                case BoundWhileStatement whileStatement:
                    foreach (var type in CollectReturnTypes(whileStatement.Body))
                        yield return type;
                    break;
                case BoundForStatement forStatement:
                    foreach (var type in CollectReturnTypes(forStatement.Body))
                        yield return type;
                    break;
                case BoundForeachStatement foreachStatement:
                    foreach (var type in CollectReturnTypes(foreachStatement.Body))
                        yield return type;
                    break;
                case BoundDoWhileStatement doWhileStatement:
                    foreach (var type in CollectReturnTypes(doWhileStatement.Body))
                        yield return type;
                    break;
                case BoundUsingStatement usingStatement:
                    foreach (var type in CollectReturnTypes(usingStatement.Body))
                        yield return type;
                    break;
                case BoundTryStatement tryStatement:
                    foreach (var type in CollectReturnTypes(tryStatement.TryBody))
                        yield return type;
                    foreach (var clause in tryStatement.CatchClauses)
                        foreach (var type in CollectReturnTypes(clause.Body))
                            yield return type;
                    foreach (var type in CollectReturnTypes(tryStatement.FinallyBody))
                        yield return type;
                    break;
            }
        }
    }

    /// <summary>
    /// Generates a unique name by appending a number suffix.
    /// </summary>
    private string GenerateUniqueName(string baseName)
    {
        var suffix = 2;
        var candidate = $"{baseName}{suffix}";
        while (_scope.Lookup(candidate) != null)
        {
            suffix++;
            candidate = $"{baseName}{suffix}";
        }
        return candidate;
    }

    private BoundTryStatement BindTryStatement(TryStatementNode tryStmt)
    {
        var baseline = CaptureFlowNarrowingState();
        var tryBody = BindExceptionalFlowBody(tryStmt.TryBody, out var exceptional);
        var branches = new List<FlowNarrowingState> { CaptureFlowNarrowingState() };
        var exceptionalExits = new List<FlowNarrowingState> { exceptional };

        var catchClauses = new List<BoundCatchClause>();
        foreach (var catchClause in tryStmt.CatchClauses)
        {
            RestoreFlowNarrowingState(exceptional);
            FlowNarrowingState afterFilter;
            using (PushScope(_scope.CreateChild()))
            {
                VariableSymbol? exceptionVar = null;
                if (catchClause.VariableName != null)
                {
                    var typeName = catchClause.ExceptionType ?? "Exception";
                    exceptionVar = CreateLocalVariable(
                        catchClause.VariableName,
                        typeName,
                        isMutable: false,
                        isParameter: false,
                        ParameterModifier.None,
                        catchClause.VariableSpan ?? catchClause.Span,
                        "catch",
                        nullableAnnotation: BoundTypes.NullableAnnotation.NotAnnotated);
                    _scope.TryDeclare(exceptionVar);
                }

                var filter = catchClause.Filter is null ? null : BindExpression(catchClause.Filter);
                afterFilter = CaptureFlowNarrowingState();
                if (catchClause.Filter is not null && filter is not null)
                    DeclareSuccessfulConditionPatterns(catchClause.Filter, filter);
                var catchBody = BindExceptionalFlowBody(catchClause.Body, out var catchExceptional);
                exceptionalExits.Add(catchExceptional);
                catchClauses.Add(new BoundCatchClause(
                    catchClause.Span, catchClause.ExceptionType, exceptionVar, catchBody, filter));
            }
            branches.Add(CaptureFlowNarrowingState());
            RestoreFlowNarrowingState(afterFilter);
            var filterFallthrough = CaptureFlowNarrowingState();
            JoinFlowBranchStates(exceptional, [exceptional, filterFallthrough]);
            exceptional = CaptureFlowNarrowingState();
        }
        exceptionalExits.Add(exceptional);
        JoinFlowBranchStates(baseline, branches);

        IReadOnlyList<BoundStatement>? finallyBody = null;
        if (tryStmt.FinallyBody != null && tryStmt.FinallyBody.Count > 0)
        {
            // Finally also runs when an exception leaves the try or a handler
            // before its last statement, not just after their normal exits.
            JoinFlowBranchStates(baseline, [.. branches, .. exceptionalExits]);
            using var _ = PushScope(_scope.CreateChild());
            finallyBody = BindStatements(tryStmt.FinallyBody);
        }
        return new BoundTryStatement(tryStmt.Span, tryBody, catchClauses, finallyBody);
    }

    private IReadOnlyList<BoundStatement> BindExceptionalFlowBody(
        IReadOnlyList<StatementNode> statements, out FlowNarrowingState exceptional)
    {
        var baseline = CaptureFlowNarrowingState();
        var enclosingWrites = _deferredCallableWrites;
        var writes = new List<CallableMutationWrite>();
        _deferredCallableWrites = writes;
        IReadOnlyList<BoundStatement> body;
        try
        {
            using var _ = PushScope(_scope.CreateChild());
            body = BindStatements(statements);
        }
        finally
        {
            _deferredCallableWrites = enclosingWrites;
            enclosingWrites?.AddRange(writes);
        }
        var normalExit = CaptureFlowNarrowingState();
        JoinFlowBranchStates(baseline, [baseline, normalExit]);
        // An exception may observe a prefix whose callable target was replaced
        // again before normal completion. Retain all observed writes and calls.
        ApplyCallableMutationSummary(new CallableMutationSummary
        {
            Mutations = GetDeferredMutations(null, body).ToArray(),
            Alternatives = GetDeferredCallEffects(null, body).ToArray(),
            Writes = writes
        });
        exceptional = CaptureFlowNarrowingState();
        RestoreFlowNarrowingState(normalExit);
        return body;
    }

    private BoundMatchStatement BindMatchStatement(MatchStatementNode matchStmt)
    {
        var target = BindExpression(matchStmt.Target);
        return new BoundMatchStatement(matchStmt.Span, target, BindMatchCases(matchStmt.Cases, target));
    }

    // ===== New statement binders for class member bodies =====

    private readonly HashSet<string> _unsupportedNodeTypes = new();

    /// <summary>
    /// #1550 (D1 RO-LV-1): refuses a write to an immutable <c>§B</c> binding or a <c>§L</c>
    /// loop variable. Every write form reports here: <c>§ASSIGN</c>, compound assignment,
    /// increment/decrement and a <c>ref</c>/<c>out</c> argument. Writes used to compile
    /// silently (the C# local is not readonly). A mutable rebind (<c>§B{~i}</c>) of a loop
    /// variable reports here too; a rebind of an immutable binding stays Calor0260.
    /// </summary>
    private void ReportWriteToReadOnlyLocal(BoundExpression target, Parsing.TextSpan span, string form)
    {
        if (target is BoundVariableExpression { Variable: var variable })
            ReportWriteToReadOnlyLocal(variable, span, form);
        else if (target is BoundTupleLiteral tuple) // #1550 review: `§ASSIGN (x, y) (2, 3)`
            foreach (var element in tuple.Elements)
                ReportWriteToReadOnlyLocal(element, element.Span, form);
    }

    private void ReportWriteToReadOnlyLocal(VariableSymbol variable, Parsing.TextSpan span, string form)
    {
        var storage = variable.StorageIdentity;
        if (storage.IsLoopVariable)
            ReportReadOnlyWrite(variable.Name, isLoopVariable: true, span, form);
        else if (storage.IsImmutableBinding)
            ReportReadOnlyWrite(variable.Name, isLoopVariable: false, span, form);
    }

    private void ReportReadOnlyWrite(string name, bool isLoopVariable, Parsing.TextSpan span, string form)
    {
        var what = form.Replace("{0}", $"'{name}'", StringComparison.Ordinal);
        _diagnostics.ReportError(span, DiagnosticCode.WriteToReadOnlyLocal, isLoopVariable
            ? $"Cannot {what}: it is a 'for' loop variable, which only the loop may update. " +
              $"Copy it into a mutable binding (var copy = {name}) or use a 'while' loop."
            : $"Cannot {what}: it is an immutable binding. " +
              $"Declare it mutable with 'var' instead of 'let' (var {name} = ... or var type:{name} = ...).");
    }

    private BoundAssignmentStatement BindAssignmentStatement(AssignmentStatementNode assign)
    {
        var target = BindExpression(assign.Target);
        var value = BindExpression(assign.Value);
        CheckMetadataArrayToCollection(value, target.Type.DisplayString, assign.Span, "Assignment");
        ReportWriteToReadOnlyLocal(target, assign.Target.Span, "assign to {0}");
        InvalidateMemberFlowNarrowing(target);
        if (target is BoundVariableExpression variable)
        {
            var restored = InvalidateMutableFlowNarrowing(variable.Variable,
                mayWriteNull: MayIntroduceNullInObliviousModule(value));
            if (!ReferenceEquals(restored, variable.Variable))
                target = new BoundVariableExpression(assign.Target.Span, restored);
            SetCallableState(restored, value);
        }
        else if (target is BoundFieldAccessExpression { ResolvedFields: [var member] }
            && member.EventDeclaration is not null)
        {
            InvalidateMutableFlowNarrowing(member, requireRebindable: false);
            SetCallableState(member, value);
        }
        return new BoundAssignmentStatement(assign.Span, target, value);
    }

    private void CheckMetadataArrayToCollection(
        BoundExpression value, string targetTypeName, Parsing.TextSpan span, string subject)
    {
        if (value.MetadataArrayResultType is not { } array
            || GetOrCreateMetadataBinder() is not { } metadata
            || ResolveComputedMetadataType(metadata.Context, targetTypeName)
                is not Microsoft.CodeAnalysis.INamedTypeSymbol { IsGenericType: true } target)
            return;
        var identity = $"{target.ContainingNamespace.ToDisplayString()}.{target.OriginalDefinition.MetadataName}";
        if (identity is not ("System.Collections.Generic.List`1" or "System.Collections.Generic.HashSet`1"
            or "System.Collections.Generic.SortedSet`1" or "System.Collections.Generic.Queue`1"
            or "System.Collections.Generic.Stack`1" or "System.Collections.Generic.LinkedList`1"
            or "System.Collections.ObjectModel.Collection`1" or "System.Collections.ObjectModel.ObservableCollection`1"
            or "System.Collections.Generic.SortedList`2"))
            return;
        _diagnostics.ReportError(span, DiagnosticCode.BindArrayToConcreteCollection,
            $"{subject} is a concrete collection ('{target.Name}<…>'), but the selected metadata member returns an array. "
            + $"An array is not implicitly convertible to '{target.Name}<…>' in C# (CS0029). "
            + $"Use '{array.ToDisplayString()}', or construct a new {target.Name} from the array.");
    }

    private BoundCompoundAssignment BindCompoundAssignment(CompoundAssignmentStatementNode compound)
    {
        if (compound.Operator is CompoundAssignmentOperator.Add or CompoundAssignmentOperator.Subtract
            && TryGetEventStorage(compound.Target, out _, out _, subscription: true)
            && BindEventSubscription(compound, compound.Target, compound.Value,
                compound.Operator == CompoundAssignmentOperator.Add) is BoundCompoundAssignment subscription)
            return subscription;
        var target = BindExpression(compound.Target);
        var value = BindExpression(compound.Value);
        ReportWriteToReadOnlyLocal(target, compound.Target.Span, "assign to {0}");
        InvalidateMemberFlowNarrowing(target);
        if (target is IBoundStorageExpression { ReferencedStorage: { EventDeclaration: not null } storage })
        {
            InvalidateMutableFlowNarrowing(storage, requireRebindable: false);
            SetCallableState(storage, value);
        }
        return new BoundCompoundAssignment(compound.Span, target, compound.Operator, value);
    }

    private BoundForeachStatement BindForeachStatement(ForeachStatementNode forEach)
    {
        using var _ = PushScope(_scope.CreateChild());

        var collection = BindExpression(forEach.Collection);
        var isTypeInferred = string.IsNullOrWhiteSpace(forEach.VariableType)
            || forEach.VariableType.Equals("var", StringComparison.OrdinalIgnoreCase);
        var inferredElementType = isTypeInferred
            ? GetIndexedElementType(collection.Type.DisplayString) : null;
        var variableType = isTypeInferred
            ? inferredElementType ?? "OBJECT" : forEach.VariableType;
        var hasUnknownInferredType = isTypeInferred
            && (inferredElementType is null || collection.HasUnknownInferredType);
        var loopVar = CreateLocalVariable(
            forEach.VariableName,
            variableType,
            isMutable: false,
            isParameter: false,
            ParameterModifier.None,
            forEach.VariableSpan,
            "foreach",
            // v0.14 nullability workstream (task #3) — inherit declared
            // STRING nullability on foreach loop variables. Scoped to
            // STRING per §D6; INT/other keep Oblivious.
            nullableAnnotation: TryReadDeclaredStringAnnotation(variableType),
            isTypeInferred: isTypeInferred,
            inferredReferenceType: isTypeInferred && !hasUnknownInferredType
                ? TryBuildReferenceType(variableType, _currentClassName, _currentNamespaceIdentity,
                    _currentMemberTypeParameters?.Select(parameter => parameter.Name))
                : null,
            hasUnknownInferredType: hasUnknownInferredType);
        _scope.TryDeclare(loopVar);

        if (forEach.IndexVariableName != null)
        {
            var indexVar = CreateLocalVariable(
                forEach.IndexVariableName,
                "INT",
                isMutable: true,
                isParameter: false,
                ParameterModifier.None,
                forEach.IndexVariableSpan ?? forEach.Span,
                "foreach-index");
            _scope.TryDeclare(indexVar);
        }

        PrepareLoopCallableState(forEach.Body);
        var headerCallables = CaptureCallableState();
        var body = BindStatements(forEach.Body);
        JoinCallableState(headerCallables);
        return new BoundForeachStatement(forEach.Span, loopVar, collection, body);
    }

    private BoundForeachStatement BindDeconstructionForeach(DictionaryForeachNode each)
    {
        using var _ = PushScope(_scope.CreateChild());
        var collection = BindExpression(each.Dictionary);
        string? keyType = null;
        string? valueType = null;
        if (TypeIdentity.TrySplitGeneric(collection.Type.DisplayString, out var typeName, out var arguments)
            && typeName is "Dictionary" or "System.Collections.Generic.Dictionary"
            && arguments.Count == 2)
        {
            keyType = arguments[0];
            valueType = arguments[1];
        }
        VariableSymbol Declare(string name, string? type, TextSpan span)
        {
            var symbol = CreateLocalVariable(name, type ?? "OBJECT", false, false,
                ParameterModifier.None, span, "foreach-deconstruction",
                isTypeInferred: true, hasUnknownInferredType: type == null);
            if (!_scope.TryDeclare(symbol))
                _diagnostics.ReportError(span, DiagnosticCode.DuplicateDefinition,
                    $"Variable '{name}' is already defined");
            return symbol;
        }
        var key = Declare(each.KeyName, keyType, each.KeySpan);
        var value = Declare(each.ValueName, valueType, each.ValueSpan);
        PrepareLoopCallableState(each.Body);
        var headerCallables = CaptureCallableState();
        var body = BindStatements(each.Body);
        JoinCallableState(headerCallables);
        return new BoundForeachStatement(each.Span, key, collection, body, value);
    }

    private BoundUsingStatement BindUsingStatement(UsingStatementNode usingStmt)
    {
        using var _ = PushScope(_scope.CreateChild());

        var resourceExpr = BindExpression(usingStmt.Resource);
        VariableSymbol? resource = null;
        if (usingStmt.VariableName != null)
        {
            if (usingStmt.VariableType is not null
                && TryBuildStringTarget(usingStmt.VariableType, out var target)
                && !IsObliviousFlowInObliviousModule(resourceExpr)
                && NullabilityChecker.IsPossiblyNullAssignedTo(resourceExpr, target!))
            {
                var (targetShapeLabel, fixHintTargetLabel) = DescribeStringTargetShape(target!);
                _diagnostics.Report(
                    resourceExpr.Span,
                    DiagnosticCode.NullableToNonNullableBinding,
                    DescribeClosedGenericReceiving(resourceExpr, target!) ??
                    $"Using resource '{usingStmt.VariableName}' declares non-nullable {targetShapeLabel} but its initializer " +
                    $"may be null (source annotation: '{DescribeAnnotation(resourceExpr)}'). " +
                    $"Change the target to {fixHintTargetLabel} or add an explicit non-null check at the interop boundary.",
                    SemanticsVersion.NullabilitySeverityFor(),
                    BindingDiagnosticContext.For(BindingReceivingBoundary.Initializer, target!));
            }
            var inferredReferenceType = usingStmt.VariableType is null
                ? GetInferredReferenceType(resourceExpr)
                : null;
            var typeName = usingStmt.VariableType
                ?? inferredReferenceType?.DisplayString
                ?? resourceExpr.Type.DisplayString;
            var annotation = usingStmt.VariableType is null
                ? inferredReferenceType?.NullableAnnotation
                    ?? NullabilityChecker.GetAnnotation(resourceExpr.Type)
                    ?? BoundTypes.NullableAnnotation.Oblivious
                : TryReadDeclaredStringAnnotation(usingStmt.VariableType);
            resource = CreateLocalVariable(
                usingStmt.VariableName,
                typeName,
                isMutable: false,
                isParameter: false,
                ParameterModifier.None,
                usingStmt.VariableSpan ?? usingStmt.Span,
                "using",
                nullableAnnotation: annotation,
                inferredReferenceType: inferredReferenceType,
                isTypeInferred: usingStmt.VariableType is null,
                hasUnknownInferredType: usingStmt.VariableType is null && resourceExpr.HasUnknownInferredType);
            _scope.TryDeclare(resource);
        }

        var body = BindStatements(usingStmt.Body);
        return new BoundUsingStatement(usingStmt.Span, resource, resourceExpr, body);
    }

    private BoundDoWhileStatement BindDoWhileStatement(DoWhileStatementNode doWhile)
    {
        using var _ = PushScope(_scope.CreateChild());

        PrepareLoopCallableState(doWhile.Body, doWhile.Condition, conditionAfterBody: true);
        var headerCallables = CaptureCallableState();
        var body = BindStatements(doWhile.Body);
        var condition = BindExpression(doWhile.Condition);
        JoinCallableState(headerCallables);
        return new BoundDoWhileStatement(doWhile.Span, condition, body);
    }

    private BoundStatement BindSyncBlock(SyncBlockNode sync)
    {
        // Model lock as a using-like scope block — lock semantics (mutual exclusion)
        // are out of scope for dataflow, but the body must be preserved for analysis
        var lockExpr = BindExpression(sync.LockExpression);
        var body = BindStatements(sync.Body);
        return new BoundUsingStatement(sync.Span, null, lockExpr, body);
    }

    /// <summary>
    /// #1678: binds both branches of a statement-level <c>§PP</c> (<c>#if</c>) block. Each
    /// branch is an alternative: it is bound in its own child scope from the same flow state, so
    /// a branch does not see the other branch's bindings, and the flow states are joined as for
    /// an <c>§IF</c> (a block without <c>§PPE</c> may also be skipped). An <c>#elif</c> chain is a
    /// <c>§PP</c> nested in <c>§PPE</c>, so it is bound by the same rule.
    /// <para><c>#if</c> opens no C# scope and the emitter writes the branch statements inline, so
    /// a binding declared at the top of either branch stays visible to the statements after the
    /// block, in the enclosing scope. When several branches declare the same name, the strictest
    /// declaration is the one that stays visible (an immutable binding over a mutable one), so a
    /// later write or rebind is checked against every branch. A branch binding that redeclares a
    /// name already declared in the enclosing block is <c>Calor0201</c>, as it is outside
    /// <c>§PP</c> (C# rejects it with CS0128 whenever that branch is active).</para>
    /// <para>The block contributes no bound statement: analysis passes still treat the body as
    /// opaque, as before. Only binding (scopes, read-only checks, types) sees it.</para>
    /// </summary>
    private BoundStatement? BindPreprocessorDirective(PreprocessorDirectiveNode preprocessor)
    {
        var baseline = CaptureFlowNarrowingState();
        var branches = new List<FlowNarrowingState>();
        var declared = new List<BranchDeclaration>();
        var bodies = new List<IReadOnlyList<BoundStatement>>();

        bodies.Add(BindPreprocessorBranch(baseline, branches, preprocessor.Body, declared, branch: 0));
        if (preprocessor.ElseBody is { } elseBody)
            bodies.Add(BindPreprocessorBranch(baseline, branches, elseBody, declared, branch: 1));
        else
            branches.Add(baseline);
        JoinFlowBranchStates(baseline, branches);
        // A branch holding verbatim C# (the converter keeps an inactive #if branch as §RAW) may
        // declare anything, so it counts as declaring every name.
        var opaqueBranches = new[] { preprocessor.Body, preprocessor.ElseBody }
            .Select((body, index) => (body, index))
            .Where(branch => branch.body is not null && ContainsVerbatimCSharp(branch.body))
            .Select(branch => branch.index)
            .ToArray();

        foreach (var group in declared.GroupBy(declaration => declaration.Storage.Name, StringComparer.Ordinal))
        {
            var declarations = group.ToArray();
            var alternatives = declarations.Select(declaration => declaration.Storage).ToArray();
            // Strictest first: an immutable §B, then any other non-mutable declaration.
            var visible = alternatives.FirstOrDefault(variable => variable.IsImmutableBinding)
                ?? alternatives.FirstOrDefault(variable => !variable.IsMutable)
                ?? alternatives[0];
            var existing = _scope.LookupLocal(visible.Name) as VariableSymbol;
            // A flow-narrowed copy of a field or property is not a declaration of this block;
            // a local may shadow the member, as in C#.
            var shadowsNarrowedMember = existing is { StorageIdentity: { IsField: true } or { IsProperty: true } };
            string? conflict = null;
            var hoistDespiteConflict = false;
            if (existing != null && !shadowsNarrowedMember)
            {
                conflict = $"Variable '{visible.Name}' is already defined";
            }
            else if ((shadowsNarrowedMember || _scope.Lookup(visible.Name) is VariableSymbol)
                     && declarations.Select(declaration => declaration.Branch)
                         .Concat(opaqueBranches).Distinct().Count() < 2)
            {
                // A path through the block that declares nothing leaves the name meaning the outer
                // field, parameter or local, so after the block it would mean different variables
                // depending on the active branch. The binding still hoists, so writes to it are
                // checked too.
                conflict = $"Variable '{visible.Name}' declared in a pp branch hides a variable or member of the " +
                    "same name, but not every branch declares it, so after the block the name would refer to " +
                    "different variables depending on the active branch. Declare it in every branch, or rename it";
                hoistDespiteConflict = true;
            }
            else if (alternatives.Select(variable => TypeIdentity.Canonicalize(variable.TypeName))
                     .Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                // The code after the block has one variable, so its type (and any refinement
                // the emitter guards writes with) must not depend on which branch is active.
                conflict = $"Variable '{visible.Name}' is declared with different types in the branches " +
                    $"of a pp block ({string.Join(", ", alternatives.Select(variable => $"'{AttributeHelper.ToSurfaceSpelling(variable.TypeName)}'").Distinct())}); " +
                    "declare it with the same type in every branch";
            }
            if (conflict != null)
            {
                foreach (var variable in alternatives)
                    _diagnostics.ReportError(variable.DeclarationSpan, DiagnosticCode.DuplicateDefinition, conflict);
                if (!hoistDespiteConflict)
                    continue;
            }

            // Calls through the visible binding may run any branch's value, and a callable captured
            // in another branch mutates the same variable after the block: the other declarations
            // share the visible one's storage from here on.
            foreach (var other in alternatives.Where(variable => !ReferenceEquals(variable, visible)))
            {
                StoreCallableState(visible, UnionCallableEffects(GetCallableState(visible), GetCallableState(other)));
                _callableState.Remove(other);
                visible.HasUnknownInferredType |= other.HasUnknownInferredType;
                other.StorageIdentity = visible;
            }
            // A flow fact (such as "not null") a branch established for its binding survives the
            // block only when every declaring branch established the same one.
            var current = declarations.First(declaration => ReferenceEquals(declaration.Storage, visible)).Current;
            var narrowed = !ReferenceEquals(current, visible)
                && declarations.All(declaration => !ReferenceEquals(declaration.Current, declaration.Storage)
                    && declaration.Current.NullableAnnotation == current.NullableAnnotation
                    && string.Equals(declaration.Current.TypeName, current.TypeName, StringComparison.Ordinal));
            var hoisted = narrowed ? current : visible;
            if (narrowed)
                _flowNarrowingSources[hoisted] = visible;
            if (existing != null)
                _scope.TryReplaceLocal(existing, hoisted);
            else
                _scope.TryDeclare(hoisted);
        }
        // Opaque to analysis passes; the binder's own deferred-effect walks read the branches.
        return new BoundPreprocessorBlock(preprocessor.Span, bodies);
    }

    private static bool ContainsVerbatimCSharp(IReadOnlyList<StatementNode> body) =>
        body.Any(statement => statement switch
        {
            RawCSharpNode => true,
            PreprocessorDirectiveNode nested => ContainsVerbatimCSharp(nested.Body)
                || (nested.ElseBody is { } nestedElse && ContainsVerbatimCSharp(nestedElse)),
            _ => false
        });

    /// <summary>
    /// #1678: binds one <c>§PP</c> branch in a child scope, from the flow state before the block.
    /// Unlike <see cref="BindIsolatedFlowBranch"/>, the branch scope is not seeded with the visible
    /// flow-narrowed copies: <c>#if</c> opens no C# scope, so a branch binding may shadow a narrowed
    /// member exactly where it could outside the block.
    /// </summary>
    private IReadOnlyList<BoundStatement> BindPreprocessorBranch(
        FlowNarrowingState baseline,
        List<FlowNarrowingState> branches,
        IReadOnlyList<StatementNode> body,
        List<BranchDeclaration> declared,
        int branch)
    {
        RestoreFlowNarrowingState(baseline);
        try
        {
            using var _ = PushScope(_scope.CreateChild());
            var bound = BindStatements(body);
            declared.AddRange(CollectBranchDeclarations().Select(declaration => declaration with { Branch = branch }));
            return bound;
        }
        finally
        {
            branches.Add(CaptureFlowNarrowingState());
            RestoreFlowNarrowingState(baseline);
        }
    }

    /// <summary>
    /// #1678: the bindings a <c>§PP</c> branch declared in its own scope (nested <c>§PP</c> blocks
    /// have already hoisted theirs into it). A flow-narrowed copy of an outer variable is not a
    /// declaration; a narrowed copy of a branch binding stands for the binding itself.
    /// </summary>
    private List<BranchDeclaration> CollectBranchDeclarations()
    {
        var outer = _scope.Parent;
        var result = new List<BranchDeclaration>();
        foreach (var symbol in _scope.GetDeclaredSymbols().OfType<VariableSymbol>())
        {
            var storage = symbol.StorageIdentity;
            if (outer?.Lookup(symbol.Name) is VariableSymbol enclosing
                && ReferenceEquals(enclosing.StorageIdentity, storage))
                continue;
            if (!result.Any(declaration => ReferenceEquals(declaration.Storage, storage)))
                result.Add(new BranchDeclaration(storage, symbol));
        }
        return result;
    }

    /// <summary>
    /// #1678: a binding a <c>§PP</c> branch declared (<see cref="Storage"/>) and the symbol visible
    /// for it at the end of the branch (<see cref="Current"/>, a flow-narrowed copy when the branch
    /// established, for example, that it is not null).
    /// </summary>
    private sealed record BranchDeclaration(VariableSymbol Storage, VariableSymbol Current, int Branch = 0);

    /// <summary>
    /// #1550 review: a statement the binder does not bind (an event subscription, an opaque
    /// statement such as <c>§PUSH</c> or <c>§EACHKV</c>) may still not write a read-only local.
    /// Walks its subtree with its own lexical scopes: a name declared inside the subtree is
    /// judged by that declaration (an immutable <c>§B</c> or a <c>§L</c> variable stays
    /// read-only; anything else is writable), and any other name by the enclosing scope.
    /// <c>§PP</c> blocks are bound instead (#1678, <see cref="BindPreprocessorDirective"/>).
    /// </summary>
    private BoundStatement? ScanUnboundWrites(StatementNode root)
    {
        ScanUnboundWrites(root, new Dictionary<string, UnboundLocal>(StringComparer.Ordinal));
        return null;
    }

    private enum UnboundLocal { Writable, Immutable, Loop }

    private void ScanUnboundWrites(AstNode node, Dictionary<string, UnboundLocal> scope)
    {
        void Check(ExpressionNode target, string form)
        {
            if (target is TupleLiteralNode tuple)
            {
                foreach (var element in tuple.Elements)
                    Check(element, form);
                return;
            }
            if (target is not ReferenceNode reference)
                return;
            if (scope.TryGetValue(reference.Name, out var local))
            {
                if (local != UnboundLocal.Writable)
                    ReportReadOnlyWrite(reference.Name, local == UnboundLocal.Loop, target.Span, form);
            }
            else if (_scope.Lookup(reference.Name) is VariableSymbol variable)
            {
                ReportWriteToReadOnlyLocal(variable, target.Span, form);
            }
        }

        void CheckArguments(IReadOnlyList<ExpressionNode> arguments, IReadOnlyList<string?>? modifiers)
        {
            for (var index = 0; modifiers != null && index < arguments.Count && index < modifiers.Count; index++)
            {
                if (modifiers[index] is "ref" or "out")
                    Check(arguments[index], $"pass {{0}} as a '{modifiers[index]}' argument");
            }
        }

        var childScope = new Dictionary<string, UnboundLocal>(scope, StringComparer.Ordinal);
        switch (node)
        {
            case AssignmentStatementNode assign: Check(assign.Target, "assign to {0}"); break;
            case CompoundAssignmentStatementNode compound: Check(compound.Target, "assign to {0}"); break;
            case UnaryOperationNode { Operator: UnaryOperator.PreIncrement or UnaryOperator.PostIncrement } inc:
                Check(inc.Operand, "increment {0}"); break;
            case UnaryOperationNode { Operator: UnaryOperator.PreDecrement or UnaryOperator.PostDecrement } dec:
                Check(dec.Operand, "decrement {0}"); break;
            case CallStatementNode call: CheckArguments(call.Arguments, call.ArgumentModifiers); break;
            case CallExpressionNode call: CheckArguments(call.Arguments, call.ArgumentModifiers); break;
            case ForStatementNode loop:
                childScope[loop.VariableName] = loop.ReevaluatesCondition
                    ? UnboundLocal.Writable : UnboundLocal.Loop;
                break;
            case ForeachStatementNode each: childScope[each.VariableName] = UnboundLocal.Writable; break;
            case DictionaryForeachNode each:
                childScope[each.KeyName] = UnboundLocal.Writable;
                childScope[each.ValueName] = UnboundLocal.Writable;
                break;
            case CatchClauseNode { VariableName: { } caught }: childScope[caught] = UnboundLocal.Writable; break;
            case LambdaExpressionNode lambda:
                foreach (var parameter in lambda.Parameters)
                    childScope[parameter.Name] = UnboundLocal.Writable;
                break;
        }

        // Each child property (a then-branch, an else-branch, a body) is its own scope.
        var baseScope = childScope;
        System.Reflection.PropertyInfo? currentProperty = null;
        foreach (var (child, property) in Analysis.RecursiveAstWalker.GetAllChildEdges(node))
        {
            if (!Equals(property, currentProperty))
            {
                currentProperty = property;
                childScope = new Dictionary<string, UnboundLocal>(baseScope, StringComparer.Ordinal);
            }
            ScanUnboundWrites(child, childScope);
            // A declaration is visible to the siblings after it, in the scope that holds them.
            switch (child)
            {
                case BindStatementNode { IsMutable: true } rebind
                    when childScope.ContainsKey(rebind.Name) || _scope.Lookup(rebind.Name) is VariableSymbol:
                    if (childScope.TryGetValue(rebind.Name, out var local))
                    {
                        if (local != UnboundLocal.Writable)
                            ReportReadOnlyWrite(rebind.Name, local == UnboundLocal.Loop, rebind.IdentifierSpan, "rebind {0}");
                    }
                    else if (_scope.Lookup(rebind.Name) is VariableSymbol outer)
                    {
                        ReportWriteToReadOnlyLocal(outer, rebind.IdentifierSpan, "rebind {0}");
                    }
                    break;
                case BindStatementNode bind:
                    childScope[bind.Name] = bind.IsMutable || bind.IsCollectionStatementForm
                        ? UnboundLocal.Writable : UnboundLocal.Immutable;
                    break;
                case PreprocessorDirectiveNode preprocessor: // #1678: #if opens no scope
                    AddPreprocessorDeclarations(preprocessor, childScope);
                    break;
                case IsPatternNode { VariableName: { } pattern }:
                    childScope[pattern] = UnboundLocal.Writable;
                    break;
            }
        }
    }

    /// <summary>
    /// #1678: inside an opaque statement, the bindings a <c>§PP</c> block's branches declare stay
    /// visible after it (nested blocks included), judged by the strictest branch.
    /// </summary>
    private void AddPreprocessorDeclarations(PreprocessorDirectiveNode preprocessor, Dictionary<string, UnboundLocal> scope)
    {
        foreach (var statement in preprocessor.Body.Concat(preprocessor.ElseBody ?? []))
        {
            if (statement is PreprocessorDirectiveNode nested)
            {
                AddPreprocessorDeclarations(nested, scope);
            }
            else if (statement is BindStatementNode bind
                     && !(bind.IsMutable && (scope.ContainsKey(bind.Name) || _scope.Lookup(bind.Name) is VariableSymbol)))
            {
                var kind = bind.IsMutable || bind.IsCollectionStatementForm ? UnboundLocal.Writable : UnboundLocal.Immutable;
                if (!scope.TryGetValue(bind.Name, out var existing) || existing == UnboundLocal.Writable)
                    scope[bind.Name] = kind;
            }
        }
    }

    private BoundStatement BindUnsupportedStatement(StatementNode stmt)
    {
        ScanUnboundWrites(stmt); // #1550 review
        var typeName = stmt.GetType().Name;
        if (_unsupportedNodeTypes.Add(typeName))
        {
            _diagnostics.ReportInfo(stmt.Span, DiagnosticCode.AnalysisUnsupportedNode,
                $"Statement type '{typeName}' is not fully supported in analysis; treated as opaque");
        }
        return new BoundUnsupportedStatement(stmt.Span, typeName);
    }

    // ===== Class member binding =====

    private void BindClassMembers(ClassDefinitionNode cls, List<BoundFunction> functions)
    {
        var className = _qualifiedClassNames[cls];
        var classScope = _classScopes[cls];

        using var _ = PushScope(classScope);
        var previousClassName = _currentClassName;
        var previousClass = _currentClass;
        var previousClassScope = _currentClassScope;
        var previousClassIdentity = _currentClassIdentity;
        var previousNamespaceIdentity = _currentNamespaceIdentity;
        var previousNamespaceScopeId = _currentNamespaceScopeId;
        _currentClassName = className;
        _currentClass = cls;
        _currentClassScope = classScope;
        _currentClassIdentity = _classSymbolIds[cls];
        _currentNamespaceIdentity = cls.NamespaceIdentity;
        _currentNamespaceScopeId = cls.NamespaceScopeId;

        try
        {
            // Methods
            foreach (var method in EnumerateMethods(cls))
            {
                if (method.Body.Count == 0 && !method.HasContracts
                    && _contractInheritance?.RuntimeContracts.ContainsKey(method) != true
                    && !method.Parameters.Any(parameter => parameter.InlineRefinement != null))
                    continue;
                var bound = TryBindMember(() => BindMethod(method, className), method.Span, className, method.Name);
                if (bound != null) functions.Add(bound);
            }

            // Constructors
            foreach (var ctor in EnumerateConstructors(cls))
            {
                if (ctor.Body.Count == 0 && ctor.Initializer == null && !cls.IsRecord
                    && ctor.Preconditions.Count == 0)
                    continue;
                var memberName = ctor.IsStatic ? ".cctor" : ".ctor";
                var bound = TryBindMember(
                    () => BindConstructor(ctor, className),
                    ctor.Span,
                    className,
                    memberName);
                if (bound != null) functions.Add(bound);
            }

            // Property accessors
            var properties = EnumerateProperties(cls).ToArray();
            for (var propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
            {
                var prop = properties[propertyIndex];
                if (prop.Getter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(
                            prop.Getter,
                            className,
                            prop.Name,
                            prop.TypeName,
                            prop.Id,
                            prop.IdentifierSpan,
                            prop.IsStatic),
                        prop.Getter.Span, className, $"{prop.Name}.get");
                    if (bound != null) functions.Add(bound);
                }
                if (prop.Setter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(
                            prop.Setter,
                            className,
                            prop.Name,
                            prop.TypeName,
                            prop.Id,
                            prop.IdentifierSpan,
                            prop.IsStatic),
                        prop.Setter.Span, className, $"{prop.Name}.set");
                    if (bound != null) functions.Add(bound);
                }
                if (prop.Initer is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(
                            prop.Initer,
                            className,
                            prop.Name,
                            prop.TypeName,
                            prop.Id,
                            prop.IdentifierSpan,
                            prop.IsStatic),
                        prop.Initer.Span, className, $"{prop.Name}.init");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Operator overloads
            var operators = EnumerateOperators(cls).ToArray();
            var ambiguousOperatorKeys = operators.GroupBy(op => CallableIdentityKey($"op_{op.Kind}", op.Parameters, []))
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            for (var operatorIndex = 0; operatorIndex < operators.Length; operatorIndex++)
            {
                var op = operators[operatorIndex];
                if (op.Body.Count == 0) continue;
                var fallbackKey = CallableIdentityKey($"op_{op.Kind}", op.Parameters, []);
                if (ambiguousOperatorKeys.Contains(fallbackKey))
                    fallbackKey += ":ambiguous-snapshot:" + _transientBindingId;
                var bound = TryBindMember(
                    () => BindOperator(op, className, fallbackKey),
                    op.Span,
                    className,
                    fallbackKey);
                if (bound != null) functions.Add(bound);
            }

            // Indexer accessors
            var indexers = EnumerateIndexers(cls).ToArray();
            for (var indexerIndex = 0; indexerIndex < indexers.Length; indexerIndex++)
            {
                var ixer = indexers[indexerIndex];
                if (ixer.Getter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindIndexerAccessor(
                            ixer.Getter,
                            ixer.Parameters,
                            className,
                            ixer.TypeName,
                            ixer.Id,
                            indexerIndex),
                        ixer.Getter.Span, className, "this[].get");
                    if (bound != null) functions.Add(bound);
                }
                if (ixer.Setter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindIndexerAccessor(
                            ixer.Setter,
                            ixer.Parameters,
                            className,
                            ixer.TypeName,
                            ixer.Id,
                            indexerIndex),
                        ixer.Setter.Span, className, "this[].set");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Event accessors
            var events = EnumerateEvents(cls).ToArray();
            for (var eventIndex = 0; eventIndex < events.Length; eventIndex++)
            {
                var evt = events[eventIndex];
                if (evt.AddBody != null && evt.AddBody.Count > 0)
                {
                    var bound = TryBindMember(
                        () => BindEventAccessor(
                            evt.AddBody,
                            className,
                            evt.Name,
                            "add",
                            evt.DelegateType,
                            evt.Id,
                            eventIndex,
                            evt.Span),
                        evt.Span, className, $"{evt.Name}.add");
                    if (bound != null) functions.Add(bound);
                }
                if (evt.RemoveBody != null && evt.RemoveBody.Count > 0)
                {
                    var bound = TryBindMember(
                        () => BindEventAccessor(
                            evt.RemoveBody,
                            className,
                            evt.Name,
                            "remove",
                            evt.DelegateType,
                            evt.Id,
                            eventIndex,
                            evt.Span),
                        evt.Span, className, $"{evt.Name}.remove");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Recurse into nested classes — isolate scope so nested classes
            // don't inherit outer class fields (C# semantics: nested classes
            // need explicit reference to access outer instance members)
            foreach (var nested in cls.NestedClasses)
                BindClassMembers(nested, functions);
        }
        finally
        {
            _currentClassName = previousClassName;
            _currentClass = previousClass;
            _currentClassScope = previousClassScope;
            _currentClassIdentity = previousClassIdentity;
            _currentNamespaceIdentity = previousNamespaceIdentity;
            _currentNamespaceScopeId = previousNamespaceScopeId;
        }
    }

    private BoundFunction? TryBindMember(Func<BoundFunction> bind, Parsing.TextSpan span, string className, string memberName)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = bind();
            sw.Stop();

            // Warn if binding took unusually long (not a hard timeout since binding is synchronous)
            if (sw.ElapsedMilliseconds > 5000)
            {
                _diagnostics.ReportWarning(span, DiagnosticCode.AnalysisSkipped,
                    $"Analysis of '{className}.{memberName}' took {sw.ElapsedMilliseconds}ms (slow binding)");
            }

            return result;
        }
        catch (NotSupportedException ex)
        {
            _diagnostics.ReportWarning(span, DiagnosticCode.AnalysisSkipped,
                $"Skipped analysis of '{className}.{memberName}': {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _diagnostics.ReportError(span, DiagnosticCode.AnalysisICE,
                $"Internal error analyzing '{className}.{memberName}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private BoundFunction BindMethod(MethodNode method, string className)
    {
        var localFunctionScope = DeclareLocalFunctions(
            method.Body, _functionSymbols[method].Id, out var localFunctions);
        var functionScope = localFunctionScope.CreateChild();
        using var _s = PushScope(functionScope);
        using var _c = PushStaticContext(method.IsStatic);
        using var _tp = PushMemberTypeParameters(method.TypeParameters);
        var functionSymbol = _functionSymbols[method];
        using var _identity = PushDeclarationContext(functionSymbol.Id);
        // v0.14 §S4 — expose the declared return type to BindReturnStatement.
        using var _returnCtx = PushReturnTypeContext(functionSymbol.BodyReturnType);

        DeclareParameters(functionSymbol.Parameters, method.Parameters);
        var inherited = !method.HasContracts
            ? _contractInheritance?.RuntimeContracts.GetValueOrDefault(method) : null;
        var contracts = BindContracts(inherited?.Preconditions ?? method.Preconditions,
            inherited?.Postconditions ?? method.Postconditions, functionSymbol.BodyReturnType);
        var boundBody = BindStatements(method.Body);
        BindLocalFunctions(localFunctions, localFunctionScope, functionSymbol.Id);
        var declaredEffects = ExtractMethodEffects(method.Effects);
        return new BoundFunction(method.Span, functionSymbol, boundBody, functionScope,
            declaredEffects, BoundMemberKind.Method, className) { ContractConditions = contracts };
    }

    private BoundFunction BindConstructor(ConstructorNode ctor, string className)
    {
        var functionScope = _scope.CreateChild();
        using var _s = PushScope(functionScope);
        using var _c = PushStaticContext(ctor.IsStatic);
        var functionSymbol = _functionSymbols[ctor];
        using var _identity = PushDeclarationContext(functionSymbol.Id);
        _currentConstructor = ctor;
        // v0.14 §S4 — ctors return VOID; TryBuildStringTarget filters them out
        // in BindReturnStatement, but push for consistency + resetting outer ctx.
        using var _returnCtx = PushReturnTypeContext(functionSymbol.ReturnType);

        DeclareParameters(functionSymbol.Parameters, ctor.Parameters);
        var contracts = BindContracts(ctor.Preconditions, [], "VOID");

        // Bind constructor initializer (: base(...) / : this(...)) as a call prepended to body.
        // This makes initializer arguments visible to bug pattern checkers (e.g., div-by-zero in base(x / y)).
        // Note: fields set by the chained constructor are NOT tracked (requires interprocedural analysis).
        var boundBody = new List<BoundStatement>();
        if (ctor.Initializer != null)
        {
            var initArgs = BindExpressions(ctor.Initializer.Arguments);
            var baseClass = ResolveBaseClass(_currentClass);
            var initTarget = ctor.Initializer.IsBaseCall
                ? baseClass == null
                    ? "base..ctor"
                    : $"{_qualifiedClassNames[baseClass]}..ctor"
                : $"{className}..ctor";
            var resolution = ResolveCall(
                ctor.Initializer.Span,
                initTarget,
                initArgs,
                argumentNames: null,
                argumentModifiers: null,
                typeArguments: null);
            boundBody.Add(new BoundCallStatement(
                ctor.Initializer.Span,
                initTarget,
                initArgs,
                resolution.Function));
        }
        boundBody.AddRange(BindStatements(ctor.Body));

        return new BoundFunction(ctor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(),
            ctor.IsStatic ? BoundMemberKind.StaticConstructor : BoundMemberKind.Constructor,
            className) { ContractConditions = contracts };
    }

    private BoundFunction BindPropertyAccessor(
        PropertyAccessorNode accessor,
        string className,
        string propName,
        string propType,
        string propertyId,
        Parsing.TextSpan propertyIdentifierSpan,
        bool isStatic)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);
        using var _staticGuard = PushStaticContext(isStatic);
        var propertyIdentity = _currentClassIdentity.Append("property", $"ast:{propertyId}");
        var functionId = propertyIdentity.Append($"accessor:{accessor.Kind}");
        using var _identity = PushDeclarationContext(functionId);

        var parameters = new List<VariableSymbol>();
        var memberKind = BoundMemberKind.PropertyGetter;

        if (accessor.Kind is PropertyAccessorNode.AccessorKind.Set
            or PropertyAccessorNode.AccessorKind.Init)
        {
            var valueParam = CreateLocalVariable(
                "value",
                propType,
                isMutable: false,
                isParameter: true,
                ParameterModifier.None,
                Parsing.TextSpan.Empty,
                "parameter",
                // v0.14 nullability workstream (task #3) — the synthetic
                // `value` parameter of a property setter/initer inherits
                // the property's declared STRING nullability. Scoped to
                // STRING per §D6.
                nullableAnnotation: TryReadDeclaredStringAnnotation(propType));
            _scope.TryDeclare(valueParam);
            parameters.Add(valueParam);
            memberKind = accessor.Kind == PropertyAccessorNode.AccessorKind.Set
                ? BoundMemberKind.PropertySetter : BoundMemberKind.PropertyInit;
        }

        var returnType = accessor.Kind == PropertyAccessorNode.AccessorKind.Get ? propType : "VOID";
        var qualifiedName = $"{className}.{propName}.{accessor.Kind.ToString().ToLowerInvariant()}";
        var functionSymbol = new FunctionSymbol(
            functionId,
            qualifiedName,
            returnType,
            parameters,
            declarationSpan: propertyIdentifierSpan,
            visibility: accessor.Visibility ?? Visibility.Public,
            containingTypeName: className,
            definitionSpan: accessor.Span);
        TrackSymbol(functionSymbol);
        // v0.14 §S4 — property getters that return :string flow through here.
        using var _returnCtx = PushReturnTypeContext(returnType);
        var contracts = BindContracts(accessor.Preconditions, [], returnType);
        var boundBody = BindStatements(accessor.Body);
        return new BoundFunction(accessor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), memberKind, className) { ContractConditions = contracts };
    }

    private BoundFunction BindOperator(
        OperatorOverloadNode op,
        string className,
        string fallbackKey)
    {
        var functionScope = _scope.CreateChild();
        using var _s = PushScope(functionScope);
        using var _c = PushStaticContext(true); // operators are always static in C#
        var functionId = CreateDeclarationId(
            _currentClassIdentity,
            "operator",
            op.Id,
            fallbackKey);
        using var _identity = PushDeclarationContext(functionId);

        var parameters = BindParameters(op.Parameters);
        var returnType = op.Output?.TypeName ?? "VOID";
        var qualifiedName = $"{className}.op_{op.Kind}";
        var functionSymbol = new FunctionSymbol(
            functionId,
            qualifiedName,
            returnType,
            parameters,
            declarationSpan: op.Span,
            visibility: op.Visibility,
            containingTypeName: className,
            definitionSpan: op.Span);
        TrackSymbol(functionSymbol);
        // v0.14 §S4 — operator overloads with a declared return type.
        using var _returnCtx = PushReturnTypeContext(returnType);
        var contracts = BindContracts(op.Preconditions, op.Postconditions, returnType);
        var boundBody = BindStatements(op.Body);
        var declaredEffects = op.Effects is null
            ? new[] { "*:*" }
            : ExtractMethodEffects(op.Effects);
        return new BoundFunction(op.Span, functionSymbol, boundBody, functionScope,
            declaredEffects, BoundMemberKind.OperatorOverload, className) { ContractConditions = contracts };
    }

    private BoundFunction BindIndexerAccessor(
        PropertyAccessorNode accessor, IReadOnlyList<ParameterNode> indexerParams,
        string className,
        string indexerType,
        string indexerId,
        int indexerIndex)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);
        var indexerIdentity = _currentClassIdentity.Append("indexer", $"ast:{indexerId}");
        var functionId = indexerIdentity.Append($"accessor:{accessor.Kind}");
        using var _identity = PushDeclarationContext(functionId);

        var parameters = BindParameters(indexerParams);
        var memberKind = BoundMemberKind.IndexerGetter;

        if (accessor.Kind is PropertyAccessorNode.AccessorKind.Set
            or PropertyAccessorNode.AccessorKind.Init)
        {
            var valueParam = CreateLocalVariable(
                "value",
                indexerType,
                isMutable: false,
                isParameter: true,
                ParameterModifier.None,
                Parsing.TextSpan.Empty,
                "parameter",
                // v0.14 nullability workstream (task #3) — synthetic
                // indexer setter `value` parameter inherits the
                // indexer's declared STRING nullability. Scoped per §D6.
                nullableAnnotation: TryReadDeclaredStringAnnotation(indexerType));
            _scope.TryDeclare(valueParam);
            parameters.Add(valueParam);
            memberKind = BoundMemberKind.IndexerSetter;
        }

        var returnType = accessor.Kind == PropertyAccessorNode.AccessorKind.Get ? indexerType : "VOID";
        var qualifiedName = $"{className}.this[].{accessor.Kind.ToString().ToLowerInvariant()}";
        var functionSymbol = new FunctionSymbol(
            functionId,
            qualifiedName,
            returnType,
            parameters,
            declarationSpan: accessor.Span,
            visibility: accessor.Visibility ?? Visibility.Public,
            containingTypeName: className,
            definitionSpan: accessor.Span);
        TrackSymbol(functionSymbol);
        // v0.14 §S4 — indexer getters that return :string flow through here.
        using var _returnCtx = PushReturnTypeContext(returnType);
        var contracts = BindContracts(accessor.Preconditions, [], returnType);
        var boundBody = BindStatements(accessor.Body);
        return new BoundFunction(accessor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), memberKind, className) { ContractConditions = contracts };
    }

    private BoundFunction BindEventAccessor(
        IReadOnlyList<StatementNode> body, string className, string eventName,
        string accessorKind,
        string delegateType,
        string eventId,
        int eventIndex,
        Parsing.TextSpan span)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);
        var eventIdentity = _currentClassIdentity.Append("event", $"ast:{eventId}");
        var functionId = eventIdentity.Append($"accessor:{accessorKind}");
        using var _identity = PushDeclarationContext(functionId);

        var valueParam = CreateLocalVariable(
            "value",
            delegateType,
            isMutable: false,
            isParameter: true,
            ParameterModifier.None,
            Parsing.TextSpan.Empty,
            "parameter",
            // v0.14 nullability workstream (task #3) — synthetic event
            // add/remove accessor `value` parameter. Delegate types are
            // not STRING, so the helper returns Oblivious here — kept
            // for uniformity across all synthetic value-parameter sites.
            nullableAnnotation: TryReadDeclaredStringAnnotation(delegateType));
        _scope.TryDeclare(valueParam);
        var parameters = new List<VariableSymbol> { valueParam };

        var memberKind = accessorKind == "add" ? BoundMemberKind.EventAdd : BoundMemberKind.EventRemove;
        var qualifiedName = $"{className}.{eventName}.{accessorKind}";
        var functionSymbol = new FunctionSymbol(
            functionId,
            qualifiedName,
            "VOID",
            parameters,
            declarationSpan: span,
            visibility: Visibility.Public,
            containingTypeName: className,
            definitionSpan: span);
        TrackSymbol(functionSymbol);
        var boundBody = BindStatements(body);
        return new BoundFunction(span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), memberKind, className);
    }

    // ===== v0.15 E2 slice b — effect rows (design-doc §3.5, §4, §5, §8.2) =====

    /// <summary>
    /// Every <c>§E</c> row already reported as misplaced, by REFERENCE. One row
    /// is reachable from more than one binder path (a parameter list is walked
    /// at registration and again at body binding), and pin P6 says Calor0405 is
    /// reported once per offending row, not once per visit.
    /// </summary>
    private readonly HashSet<EffectsNode> _misplacedRowsReported =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Simple names declared with <c>§DEL</c> anywhere in this compilation.
    /// Populated during type registration, which is why
    /// <see cref="CheckEffectRowPlacement"/> runs AFTER all registration rather
    /// than inside <c>CreateFunctionSymbol</c>: top-level functions are
    /// registered before <c>module.Delegates</c>, so a row on a
    /// <c>§DEL</c>-typed parameter would otherwise be reported as misplaced.
    /// </summary>
    private readonly HashSet<string> _delegateTypeNames = new(StringComparer.Ordinal);

    /// <summary>
    /// §3.5 / pin P6 — a <c>§E{…}</c> row on a position whose type is NOT a
    /// function type is Calor0405 <c>EffectRowMisplaced</c>. A non-function type
    /// performs no effects, so a row there has no meaning; before 0.15 the same
    /// <c>§E</c> was silently read as the DECLARATION's own row, which is why
    /// <c>Z9</c>/<c>Z9b</c>/<c>Z9c</c> compile on main.
    ///
    /// <para>This is the second of Calor0405's two situations. The first — a row
    /// that is not on the same line as the type it annotates — is reported by
    /// the parser (E2 slice a). One code, two ways of writing a row where a row
    /// cannot go.</para>
    ///
    /// <para>Runs over DECLARATIONS only. Position 7 (a <c>§B</c>'s row) is
    /// checked at <see cref="BindBindStatement"/> instead, because a binding can
    /// be nested arbitrarily deep inside a body and the binder already visits
    /// every one of them there.</para>
    /// </summary>
    private void CheckEffectRowPlacement(ModuleNode module)
    {
        foreach (var function in module.Functions)
            CheckCallableRows(function.Parameters, function.Output);

        foreach (var @delegate in module.Delegates)
            CheckCallableRows(@delegate.Parameters, @delegate.Output);

        foreach (var @interface in module.Interfaces)
            CheckInterfaceRows(@interface);

        foreach (var cls in module.Classes)
            CheckClassRows(cls);
    }

    private void CollectDelegateTypeNames(ModuleNode module)
    {
        foreach (var @delegate in module.Delegates)
            _delegateTypeNames.Add(@delegate.Name);
        foreach (var cls in module.Classes)
            CollectNestedDelegateTypeNames(cls);

        // v0.15 E3 slice a, review round 1 (F2) — a `delegate` declared inside a
        // §CSHARP interop block is a real type the rest of the module may use as
        // a parameter, field or return spelling, and a position of that type IS
        // function-typed. Before this, §3.5's placement check called it "not a
        // function type" and reported Calor0405 — a hard error on a legal
        // declaration. Measured on the frozen PP-E1 fixture A2.calr, whose
        // RequestHandlerDelegate<TResponse> is declared exactly this way.
        foreach (var block in module.InteropBlocks)
            CollectInteropDelegateTypeNames(block);
        foreach (var block in module.TypePreprocessorBlocks)
            CollectTypePreprocessorDelegateTypeNames(block);
        foreach (var cls in module.Classes)
            CollectNestedInteropDelegateTypeNames(cls);
    }

    private void CollectDelegateDefinitions(ModuleNode module)
    {
        foreach (var @delegate in module.Delegates)
            _delegateDefinitions.TryAdd(GetQualifiedTypeName(@delegate), @delegate);
        foreach (var cls in module.Classes)
            CollectNestedDelegateDefinitions(cls, containingTypeName: null);
    }

    private void CollectNestedDelegateDefinitions(
        ClassDefinitionNode cls,
        string? containingTypeName)
    {
        var qualifiedClassName = containingTypeName == null
            ? GetQualifiedTypeName(cls)
            : $"{containingTypeName}.{GetTypeNameWithArity(cls.Name, cls.TypeParameters.Count)}";
        foreach (var @delegate in cls.NestedDelegates)
            _delegateDefinitions.TryAdd($"{qualifiedClassName}.{@delegate.Name}", @delegate);
        foreach (var nested in cls.NestedClasses)
            CollectNestedDelegateDefinitions(nested, qualifiedClassName);
    }

    private void CollectInteropDelegateTypeNames(CSharpInteropBlockNode block)
    {
        foreach (var name in TypeIdentity.DelegateNamesDeclaredInInteropText(block.CSharpCode))
            _delegateTypeNames.Add(name);
    }

    private void CollectTypePreprocessorDelegateTypeNames(TypePreprocessorBlockNode block)
    {
        foreach (var @delegate in block.Delegates)
            _delegateTypeNames.Add(@delegate.Name);
        foreach (var interop in block.InteropBlocks)
            CollectInteropDelegateTypeNames(interop);
        foreach (var cls in block.Classes)
        {
            CollectNestedDelegateTypeNames(cls);
            CollectNestedInteropDelegateTypeNames(cls);
        }
        foreach (var nested in block.NestedBlocks)
            CollectTypePreprocessorDelegateTypeNames(nested);
        if (block.ElseBranch != null)
            CollectTypePreprocessorDelegateTypeNames(block.ElseBranch);
    }

    private void CollectNestedInteropDelegateTypeNames(ClassDefinitionNode cls)
    {
        foreach (var interop in cls.InteropBlocks)
            CollectInteropDelegateTypeNames(interop);
        foreach (var nested in cls.NestedClasses)
            CollectNestedInteropDelegateTypeNames(nested);
    }

    private void CollectNestedDelegateTypeNames(ClassDefinitionNode cls)
    {
        foreach (var @delegate in cls.NestedDelegates)
            _delegateTypeNames.Add(@delegate.Name);
        foreach (var nested in cls.NestedClasses)
            CollectNestedDelegateTypeNames(nested);
    }

    /// <summary>
    /// §8.2 — the <see cref="BoundTypes.FunctionBoundType"/> a row-carrying
    /// position denotes, or <c>null</c> when the position is not function-typed
    /// (the Calor0405 case) or carries no row.
    ///
    /// <para>The DISPLAY STRING is the position's <b>declared type name as the
    /// binder already holds it</b> — not §8.3's canonical
    /// <c>(p1, p2) -&gt; ret</c>. Consumers compare display strings
    /// byte-for-byte — <c>Binder.BindStatement</c> infers an untyped <c>§B</c>'s
    /// <c>TypeName</c> from one, the verifier caches on one, the LSP builds a
    /// call-graph key from one — so the string a position prints does not move.
    /// The KIND becomes a function type; the string does not. Same trade E1
    /// slice 2b made for lambdas.</para>
    ///
    /// <para><b>That name is the parser-EXPANDED spelling, not the source
    /// text.</b> <c>ExpandType</c> rewrites <c>Func&lt;i32,i32&gt;</c> to
    /// <c>Func&lt;INT, INT&gt;</c> before it reaches
    /// <c>ParameterNode.TypeName</c> / <c>OutputNode.TypeName</c>, at BOTH
    /// positions — an earlier revision of this comment and of design-doc §8.2
    /// said "keeps its surface spelling <c>Func&lt;i32,i32&gt;</c>", which was
    /// wrong (review round 1, MINOR 4). It is the right string regardless,
    /// because it is the same one <c>VariableSymbol.TypeName</c> and every
    /// existing consumer of these positions already carries; using the raw
    /// source text here would be the change that moves things.</para>
    ///
    /// <para><see cref="BoundTypes.FunctionBoundType.ParameterRows"/> is all
    /// <see cref="BoundTypes.EffectRow.Unknown"/> here, and that is not a
    /// shortcut: a row on a nested generic argument
    /// (<c>Func&lt;Func&lt;i32,i32&gt; §E{cw}, i32&gt;</c>) is unreachable in
    /// 0.15 — inline generic arguments are read as text, never as a type with a
    /// row (E2 slice a, deviation 4) — so there is no source that could supply
    /// one.</para>
    /// </summary>
    ///
    /// <para><b>v0.15 E3 slice a widens this to EVERY function-typed position,
    /// rowed or not</b> (§8.2). Slice b built one only where a row was written,
    /// because an unconditional one would make
    /// <c>EffectEnforcementPass.IsFunctionBoundType</c> answer true where it
    /// answered false and could move Calor0418 on row-free programs. E3 measured
    /// the fear rather than inheriting it: <c>VariableSymbol.FunctionType</c> and
    /// <c>FunctionSymbol.ReturnFunctionType</c> have <b>no reader anywhere in
    /// <c>src/</c></b> (<c>grep -rn '\.FunctionType\b' src/</c> → the declarations
    /// only), and <c>IsFunctionBoundType</c> reads
    /// <c>CallGraphAnalysis.BoundValueTypes</c>, which is built from bound call
    /// RECEIVER expressions and not from these two properties. So widening is
    /// behaviourally inert today, and the committed-corpus differential is the
    /// proof rather than the claim. A row-free position gets
    /// <see cref="BoundTypes.EffectRow.Unknown"/> — never pure.</para>
    private BoundTypes.FunctionBoundType? TryBuildFunctionType(
        string? typeName,
        EffectsNode? row,
        IReadOnlyList<EffectParameterInfo>? binders,
        string? lexicalTypeContext = null)
    {
        if (typeName == null) return null;
        if (!IsFunctionTypedSpelling(typeName, lexicalTypeContext)) return null;
        return BuildFunctionType(
            typeName,
            BindRow(row, binders),
            lexicalTypeContext ?? _currentClassName ?? _currentNamespaceIdentity);
    }

    private BoundTypes.FunctionBoundType? TryBuildParameterFunctionType(
        ParameterNode parameter, IReadOnlyList<EffectParameterInfo>? binders, string? lexicalTypeContext)
    {
        if (ResolveSourceCallConversions && _asyncTypeModule is not null)
        {
            // The projection's answer for a position whose emitted type can never be a
            // delegate is always null (it stores the non-delegate type), so do not build
            // the whole C# image just to learn that.
            if (EmitsAsNonDelegateType(parameter.TypeName))
                return null;
            SourceCallBindings();
            if (_sourceCallConversions.ParameterTypes.TryGetValue(parameter, out var type))
                return type is Microsoft.CodeAnalysis.INamedTypeSymbol { DelegateInvokeMethod: { } invoke }
                    ? new BoundTypes.FunctionBoundType(
                        System.Collections.Immutable.ImmutableArray.CreateRange(
                            invoke.Parameters.Select(item => Metadata.MetadataBinderResult.ToBoundType(item.Type))),
                        Metadata.MetadataBinderResult.ToBoundType(invoke.ReturnType),
                        displayOverride: parameter.TypeName,
                        row: BindRow(parameter.Row, binders))
                    : null;
        }
        return TryBuildFunctionType(parameter.TypeName, parameter.Row, binders, lexicalTypeContext);
    }

    private BoundTypes.FunctionBoundType? TryBuildReturnFunctionType(
        OutputNode? output, IReadOnlyList<EffectParameterInfo>? binders, string? lexicalTypeContext)
    {
        if (output == null)
            return null;
        if (ResolveSourceCallConversions && _asyncTypeModule is not null)
        {
            if (EmitsAsNonDelegateType(output.TypeName))
                return null;
            SourceCallBindings();
            if (_sourceCallConversions.OutputTypes.TryGetValue(output, out var type))
                return type is Microsoft.CodeAnalysis.INamedTypeSymbol { DelegateInvokeMethod: { } invoke }
                    ? new BoundTypes.FunctionBoundType(
                        System.Collections.Immutable.ImmutableArray.CreateRange(
                            invoke.Parameters.Select(item => Metadata.MetadataBinderResult.ToBoundType(item.Type))),
                        Metadata.MetadataBinderResult.ToBoundType(invoke.ReturnType),
                        displayOverride: output.TypeName,
                        row: BindRow(output.Row, binders))
                    : null;
        }
        return TryBuildFunctionType(output.TypeName, output.Row, binders, lexicalTypeContext);
    }

    /// <summary>
    /// Whether the C# type the emitter writes for this declared spelling can never
    /// denote a delegate, whatever the module declares: a C# keyword type
    /// (<c>int</c>, <c>string</c>, <c>void</c>, ...), or a nullable, array or pointer
    /// built only from one. Keywords cannot be redeclared or aliased in C#, so Roslyn
    /// binds such a type to the same non-delegate symbol in every image.
    /// <para>The source projection (<see cref="SourceCallBindings"/>) types a parameter or
    /// return as a callback only when that emitted type is a delegate. For these
    /// spellings it records the non-delegate type and the caller returns null, so
    /// skipping the projection gives the same answer. That keeps a module whose
    /// parameters are all scalars from paying for a full C# compilation at binding.
    /// Anything else (a named type, a generic, a tuple) still asks the projection.</para>
    /// <para>The spelling is mapped exactly as <c>CSharpEmitter.MapTypeName</c> maps it.
    /// A head that names a module refinement or indexed type is erased to its base
    /// type there, so it is never classified here. The check reads the emitted text
    /// rather than parsing it: it runs for every parameter and return of the module.</para>
    /// </summary>
    private bool EmitsAsNonDelegateType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || _asyncTypeModule is null)
            return false;
        var spelling = typeName.Trim();
        var generic = spelling.IndexOf('<');
        var head = generic > 0 ? spelling[..generic] : spelling;
        if (_asyncTypeModule.RefinementTypes.Any(type => type.Name == head)
            || _asyncTypeModule.IndexedTypes.Any(type => type.Name == head))
            return false;
        var emitted = Migration.TypeMapper.CalorToCSharp(spelling);
        if (string.IsNullOrWhiteSpace(emitted))
            return false;
        var core = emitted.AsSpan().Trim();
        while (true)
        {
            if (core.EndsWith("?") || core.EndsWith("*"))
                core = core[..^1].TrimEnd();
            else if (core.EndsWith("]"))
            {
                var open = core.LastIndexOf('[');
                if (open <= 0)
                    return false;
                foreach (var rank in core[(open + 1)..^1])
                    if (rank != ',' && !char.IsWhiteSpace(rank))
                        return false;
                core = core[..open].TrimEnd();
            }
            else
                break;
        }
        return SyntaxFacts.IsPredefinedType(SyntaxFacts.GetKeywordKind(core.ToString()));
    }

    /// <summary>
    /// The structural shape of a declared function-type spelling.
    /// <c>Func&lt;A,B&gt;</c> is <c>(A) -&gt; B</c>, <c>Action&lt;A&gt;</c> is
    /// <c>(A) -&gt; VOID</c>, <c>Predicate&lt;A&gt;</c> is <c>(A) -&gt; BOOL</c>.
    /// Anything else the string test admits — a bare <c>Delegate</c>, a
    /// <c>§DEL</c> name whose declaration the binder does not resolve here —
    /// gets an arity-0 shape, which is honest: the row is known, the signature
    /// is not, and nothing in slice b reads the signature.
    /// </summary>
    private BoundTypes.FunctionBoundType BuildFunctionType(
        string typeName,
        BoundTypes.EffectRow row,
        string? lexicalTypeContext = null)
    {
        var declared = typeName.Trim();
        var parameters = System.Collections.Immutable.ImmutableArray<BoundTypes.BoundType>.Empty;
        BoundTypes.BoundType returnType = new BoundTypes.NominalBoundType("VOID");

        if (TypeIdentity.TrySplitGeneric(declared.TrimEnd('?'), out var generic, out var arguments)
            && arguments.Count > 0)
        {
            switch (generic)
            {
                case "Func":
                    parameters = System.Collections.Immutable.ImmutableArray.CreateRange(
                        arguments.Take(arguments.Count - 1)
                            .Select(BuildDeclaredBoundType));
                    returnType = BuildDeclaredBoundType(arguments[^1]);
                    break;
                case "Predicate":
                    parameters = System.Collections.Immutable.ImmutableArray.CreateRange(
                        arguments.Select(BuildDeclaredBoundType));
                    returnType = BuildDeclaredBoundType("BOOL");
                    break;
                default:
                    parameters = System.Collections.Immutable.ImmutableArray.CreateRange(
                        arguments.Select(BuildDeclaredBoundType));
                    break;
            }
        }
        else if (ResolveDelegateDefinition(declared, lexicalTypeContext) is { } delegateDefinition)
        {
            parameters = System.Collections.Immutable.ImmutableArray.CreateRange(
                delegateDefinition.Parameters.Select(parameter => BuildDeclaredBoundType(parameter.TypeName)));
            returnType = BuildDeclaredBoundType(delegateDefinition.Output?.TypeName ?? "VOID");
        }

        return new BoundTypes.FunctionBoundType(
            parameters,
            returnType,
            displayOverride: declared,
            row: row);
    }

    private DelegateDefinitionNode? ResolveDelegateDefinition(
        string declaredTypeName,
        string? lexicalTypeContext)
    {
        var name = declaredTypeName.Trim();
        while (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(name, out var referent))
            name = referent.Trim();

        foreach (var lookupName in ReferenceLookupNames(name, lexicalTypeContext))
        {
            if (_delegateDefinitions.TryGetValue(lookupName, out var definition))
                return definition;
        }
        return null;
    }

    private BoundTypes.BoundType BuildDeclaredBoundType(string typeName)
    {
        var name = typeName.Trim();
        if (_declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious
            && TryBuildMethodInputArrayType(name, out var array))
            return array!;
        var annotation = _declaredReferenceAnnotation;
        if (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(name, out var referent))
        {
            name = referent;
            annotation = BoundTypes.NullableAnnotation.Annotated;
        }
        else if (!IsScalarStringTypeName(name))
        {
            annotation = BoundTypes.NullableAnnotation.Oblivious;
        }

        if (_declaredReferenceAnnotation == BoundTypes.NullableAnnotation.Oblivious
            && TypeIdentity.TrySplitGeneric(name, out var definition, out var arguments))
            return new BoundTypes.GenericInstantiationBoundType(
                new BoundTypes.NominalBoundType(definition),
                System.Collections.Immutable.ImmutableArray.CreateRange(arguments.Select(BuildDeclaredBoundType)),
                annotation);

        return new BoundTypes.NominalBoundType(name, annotation);
    }

    private static bool IsScalarStringTypeName(string typeName)
    {
        var name = typeName.Trim();
        while (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(name, out var referent))
            name = referent;
        return name is "STRING" or "string" or "str" or "System.String";
    }

    private void CheckInterfaceRows(InterfaceDefinitionNode @interface)
    {
        foreach (var method in @interface.Methods)
            CheckCallableRows(method.Parameters, method.Output);
        foreach (var indexer in @interface.Indexers)
            CheckCallableRows(indexer.Parameters, output: null);
    }

    private void CheckClassRows(ClassDefinitionNode cls)
    {
        foreach (var field in cls.Fields)
            CheckRowPosition(field.Row, field.TypeName, field.Name, "field");
        foreach (var method in cls.Methods)
            CheckCallableRows(method.Parameters, method.Output);
        foreach (var constructor in cls.Constructors)
            CheckCallableRows(constructor.Parameters, output: null);
        foreach (var indexer in cls.Indexers)
            CheckCallableRows(indexer.Parameters, output: null);
        foreach (var op in cls.OperatorOverloads)
            CheckCallableRows(op.Parameters, op.Output);
        foreach (var nested in cls.NestedClasses)
            CheckClassRows(nested);
        foreach (var nested in cls.NestedInterfaces)
            CheckInterfaceRows(nested);
        foreach (var nested in cls.NestedDelegates)
            CheckCallableRows(nested.Parameters, nested.Output);
    }

    private void CheckCallableRows(IReadOnlyList<ParameterNode> parameters, OutputNode? output)
    {
        foreach (var parameter in parameters)
            CheckRowPosition(parameter.Row, parameter.TypeName, parameter.Name, "parameter");
        if (output != null)
            CheckRowPosition(output.Row, output.TypeName, output.TypeName, "return");
    }

    private void CheckRowPosition(EffectsNode? row, string? typeName, string subject, string subjectKind)
    {
        if (row == null) return;
        if (IsFunctionTypedSpelling(typeName)) return;

        // v0.15 E3 slice a, review round 1 (F2) — fail OPEN. Calor0405 fires only
        // where the position PROVABLY cannot carry a row; an unknown nominal is
        // "I do not know what this is", which is not a verdict. `!IsFunctionTypedSpelling`
        // was the wrong complement of a LIST predicate and made a legal delegate
        // a hard error.
        if (!IsProvablyNonFunctionBindingType(typeName)) return;

        ReportRowOnNonFunctionType(row, typeName, subject, subjectKind);
    }

    /// <summary>
    /// "Can a value of this declared type carry an effect row?" — the STRING
    /// half of the function-typedness question (<c>TypeIdentity</c>), plus the
    /// <c>§DEL</c> names this compilation declares. The binder has no bound type
    /// for a declared parameter/field/return spelling, so the string test is the
    /// whole answer here; where a bound type IS in hand (a lambda) the type is a
    /// <c>FunctionBoundType</c> by construction and never reaches this path.
    /// </summary>
    private bool IsFunctionTypedSpelling(
        string? typeName,
        string? lexicalTypeContext = null)
    {
        var context = lexicalTypeContext ?? _currentClassName ?? _currentNamespaceIdentity;
        return !string.IsNullOrWhiteSpace(typeName)
            && ResolveDelegateDefinition(typeName, context) is not null
            || TypeIdentity.IsFunctionTypeName(
                typeName,
                name => _delegateTypeNames.Contains(name)
                    || ResolveDelegateDefinition(name, context) is not null);
    }

    private bool IsProvablyNonFunctionBindingType(string? typeName)
    {
        if (!string.IsNullOrWhiteSpace(typeName)
            && Parsing.AttributeHelper.TryUnwrapNullableAnnotation(typeName, out var referent)
            && ResolveTypeSymbol(referent) is { } declaration)
            return !declaration.IsDelegate;
        return TypeIdentity.IsProvablyNonFunctionType(typeName);
    }

    private void ReportRowOnNonFunctionType(
        EffectsNode row,
        string? typeName,
        string subject,
        string subjectKind)
    {
        if (!_misplacedRowsReported.Add(row)) return;

        // ONE vocabulary across all nine positions. The two return spellings do
        // not arrive equal: ExpandType rewrites §O{void} to VOID, but the arrow
        // form `-> void §E{cw}` reaches OutputNode.TypeName as the raw `void`,
        // so the same mistake was reported as 'VOID' in one spelling and 'void'
        // in the other (review round 1, MINOR 5). Canonicalize normalises HERE,
        // in the message only — expanding the arrow form in the parser would
        // move FunctionNode.Output.TypeName for every arrow-form function in the
        // corpus, which is a change with a blast radius and nothing to do with
        // rows.
        // Canonicalize throws on an empty/whitespace name (Binding/Scope.cs:323) --
        // a `§FLD{:c:priv}` parses with an empty type, so guard on whitespace, not null
        // (review round 2 of PR #1102, R2-A).
        var displayed = string.IsNullOrWhiteSpace(typeName)
            ? "?"
            : TypeIdentity.Canonicalize(typeName);

        // A return has no name of its own to quote, so it is described by its
        // kind. Every other position is named.
        var described = subjectKind == "return"
            ? $"The return type '{displayed}' is not a function type"
            : $"'{subject}' has type '{displayed}', which is not a function type";

        _diagnostics.ReportError(
            row.Span,
            DiagnosticCode.EffectRowMisplaced,
            $"{described}, so it cannot carry an effect row. Remove the '!' row, or — if this is "
            + "the function's own effect declaration — move it onto its own line.");
    }

    /// <summary>
    /// The row's effect codes in the internal <c>category:value</c> spelling
    /// <see cref="BoundTypes.EffectRow"/> carries. <c>EffectsNode.Effects</c>
    /// maps a category to its values COMMA-JOINED (<c>§E{cw, fs:w}</c> is one
    /// entry, <c>io -&gt; "console_write,filesystem_write"</c>), so the values
    /// have to be split back apart.
    /// </summary>
    private static IEnumerable<string> EnumerateRowCodes(EffectsNode row)
    {
        foreach (var (category, values) in row.Effects)
        {
            foreach (var value in values.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return $"{category.ToLowerInvariant()}:{value.Trim().ToLowerInvariant()}";
            }
        }
    }

    /// <summary>
    /// The <see cref="BoundTypes.EffectRow"/> an <c>§E{…}</c> denotes, resolving
    /// checking that any effect VARIABLES it mentions are bound by the
    /// declaration that owns the row (§7.2).
    ///
    /// <para>A row that mentions a variable is <see cref="BoundTypes.EffectRow.Unknown"/>
    /// in slice b: until §7.4's instantiation lands (E3) the variable's
    /// contribution genuinely is not known, and Unknown is the sound answer —
    /// it fits nothing and is fitted by nothing, so no assignment can slip
    /// through on an uninstantiated row.</para>
    ///
    /// <para>A variable with no binder on the owning declaration is Calor0404.
    /// It is <b>not reachable from source today</b>: the parser tracks the same
    /// scope while it parses the row and leaves an unbound name as an unknown
    /// effect CODE (Calor0403), so nothing lands in
    /// <c>EffectsNode.EffectVariables</c> that was not bound. The check is here
    /// because the binder is where scope is supposed to be decided, and it is
    /// pinned as a unit test on the helper rather than as a source
    /// transcript.</para>
    /// </summary>
    private BoundTypes.EffectRow BindRow(
        EffectsNode? row,
        IReadOnlyList<EffectParameterInfo>? binders)
    {
        if (row == null || row.IsUnknown) return BoundTypes.EffectRow.Unknown;

        var mentionsVariable = false;
        foreach (var variable in row.EffectVariables)
        {
            if (!IsEffectVariableBound(variable, binders))
            {
                _diagnostics.ReportError(
                    row.Span,
                    DiagnosticCode.EffectVariableScope,
                    $"Effect variable '{variable}' is not bound by this declaration. "
                    + "Declare it in the type-parameter list — func Name<! "
                    + $"{variable}> — or use an effect code from the taxonomy.");
            }
            mentionsVariable = true;
        }

        return mentionsVariable
            ? BoundTypes.EffectRow.Unknown
            : BoundTypes.EffectRow.Concrete(EnumerateRowCodes(row));
    }

    /// <summary>
    /// True when <paramref name="name"/> is bound by an <c>eff</c> binder on the
    /// declaration that owns the row. Comparison is by name and case-sensitive,
    /// because effect variables are ordinary identifiers.
    ///
    /// <para><b>This is a boundness CHECK, not a resolution.</b> An earlier
    /// revision returned the binder's ordinal and threw it away, while the
    /// working notes claimed E3 "inherits the resolution" — it does not, and
    /// review round 1 (MINOR 6) caught the mismatch. Slice b needs one bit
    /// ("is this bound?") because a row mentioning any variable becomes
    /// <see cref="BoundTypes.EffectRow.Unknown"/> until §7.4's instantiation
    /// lands. The ordinal, and the alpha-equivalence that makes it the right key
    /// rather than the name, belong to <b>E3</b>, which is the slice that
    /// substitutes an argument's row for a binder and therefore the first slice
    /// with somewhere to put it.</para>
    /// </summary>
    private static bool IsEffectVariableBound(
        string name,
        IReadOnlyList<EffectParameterInfo>? binders)
    {
        if (binders == null) return false;
        foreach (var binder in binders)
        {
            if (string.Equals(binder.Name, name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ===== Effect extraction =====

    /// <summary>
    /// Extracts effect declarations from a function node.
    /// </summary>
    private static IReadOnlyList<string> ExtractEffects(FunctionNode func)
        => ExtractMethodEffects(func.Effects);

    /// <summary>
    /// Extracts effect declarations from an EffectsNode.
    /// Returns effects in "category:value" format (e.g., "io:database_write").
    /// </summary>
    private static IReadOnlyList<string> ExtractMethodEffects(EffectsNode? effectsNode)
    {
        if (effectsNode?.Effects == null || effectsNode.Effects.Count == 0)
            return Array.Empty<string>();

        var effects = new List<string>();
        foreach (var (category, value) in effectsNode.Effects)
        {
            // Store as "category:value" - TaintAnalysis will parse this
            effects.Add($"{category.ToLowerInvariant()}:{value.ToLowerInvariant()}");
        }
        return effects;
    }
}
