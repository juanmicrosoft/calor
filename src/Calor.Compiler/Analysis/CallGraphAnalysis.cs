using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;

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
                else
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

    private static IEnumerable<BoundNode> DescendantsAndSelf(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
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
            yield return cls;
        foreach (var block in module.TypePreprocessorBlocks)
        {
            var branch = block;
            while (branch != null)
            {
                foreach (var cls in branch.Classes)
                    yield return cls;
                branch = branch.ElseBranch;
            }
        }
    }

    /// <summary>
    /// Enumerates all interfaces in a module, including §PP-wrapped ones.
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

    /// <summary>
    /// Builds a call graph analysis from a module AST.
    /// </summary>
    public static CallGraphAnalysis Build(ModuleNode ast)
    {
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

        // Index class methods and constructors (including §PP-wrapped members
        // and §PP-wrapped classes — W2 review C1)
        foreach (var cls in EnumerateClasses(ast))
        {
            foreach (var method in EnumerateMethods(cls))
            {
                var wrapped = ToFunctionNode(method, cls.Name);
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
             declaredFunctionTypes, declaredReturnTypes, declaredFieldTypes) =
            ResolveBoundCallSites(ast, functions);

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
                if (calleeIds.Count == 0)
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
        return analysis;
    }

    private static (
        Dictionary<AstCallKey, IReadOnlyList<string>> Resolved,
        HashSet<AstCallKey> BoundCallSites,
        bool Complete,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.BoundType>>
            BoundValueTypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType>>
            DeclaredFunctionTypes,
        IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType> DeclaredReturnTypes,
        IReadOnlyDictionary<string, Binding.BoundTypes.FunctionBoundType> DeclaredFieldTypes)
        ResolveBoundCallSites(
            ModuleNode ast,
            IReadOnlyDictionary<string, FunctionNode> functions)
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

        // The receiver of a call site, keyed by the receiver path exactly as the
        // target spells it ("sb" in "sb.Append", "a.b" in "a.b.M"). The pass
        // looks the receiver path up directly; a bare head also lands in the
        // same map through RecordValue above.
        void RecordReceiver(
            string callerId,
            string target,
            Binding.BoundExpression? receiverExpression)
        {
            if (receiverExpression is null)
                return;
            var lastDot = target.LastIndexOf('.');
            if (lastDot <= 0)
                return;
            RecordValue(callerId, target[..lastDot], receiverExpression.Type);
        }

        try
        {
            var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
            var boundModule = new Binder(diagnostics).Bind(ast);
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

            foreach (var symbol in boundModule.SymbolsById.Values)
            {
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
                if (!legacyIds.TryGetValue(function.SymbolId, out var callerId))
                    continue;

                if (function.Symbol.ReturnFunctionType is { } returnType)
                    declaredReturns[callerId] = returnType;
                foreach (var parameter in function.Symbol.Parameters)
                {
                    if (parameter.FunctionType is not { } parameterType) continue;
                    if (!declaredTypes.TryGetValue(callerId, out var byName))
                    {
                        byName = new Dictionary<string, Binding.BoundTypes.FunctionBoundType>(
                            StringComparer.Ordinal);
                        declaredTypes[callerId] = byName;
                    }
                    byName[parameter.Name] = parameterType;
                }

                foreach (var node in DescendantsAndSelf(function))
                {
                    string? target = null;
                    IReadOnlyList<FunctionSymbol>? callees = null;
                    VariableSymbol? receiver = null;
                    var inaccessibleCall = false;
                    var expressionTargetCall = false;
                    switch (node)
                    {
                        case BoundCallStatement statement:
                            target = statement.Target;
                            callees = statement.ResolvedSymbols;
                            receiver = statement.ReceiverSymbol;
                            inaccessibleCall = statement.IsInaccessibleCall;
                            RecordReceiver(callerId, statement.Target, statement.Receiver);
                            break;
                        case BoundCallExpression expression:
                            target = expression.Target;
                            callees = expression.ResolvedSymbols;
                            receiver = expression.ReceiverSymbol;
                            inaccessibleCall = expression.IsInaccessibleCall;
                            RecordReceiver(callerId, expression.Target, expression.Receiver);
                            break;
                        case BoundNewExpression creation:
                            target = $"{creation.Type.DisplayString}..ctor";
                            callees = creation.ResolvedConstructor == null
                                ? Array.Empty<FunctionSymbol>()
                                : [creation.ResolvedConstructor];
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

                    var key = new AstCallKey(callerId, target, node.Span.Start, node.Span.End);
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
                        boundCallSites.Add(key);
                        resolved[key] = calleeLegacyIds;
                    }
                    else if (expressionTargetCall
                             || receiver != null
                             || inaccessibleCall
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
                NoFunctionTypes);
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
            declaredFields);
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
            .Where(candidate => candidate.Span == boundFunction.Symbol.DefinitionSpan)
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

    private static FunctionNode ToFunctionNode(MethodNode method, string className)
    {
        var qualifiedId = $"{className}.{method.Id}";
        return new FunctionNode(
            method.Span,
            qualifiedId,
            method.Name,
            method.Visibility,
            method.TypeParameters,
            method.Parameters,
            method.Output,
            method.Effects,
            method.Preconditions,
            method.Postconditions,
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
            effects: null,
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
            effects: null,
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
