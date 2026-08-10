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
                        target = $"{creation.TypeName}..ctor";
                        span = creation.Span;
                        callees = creation.ResolvedConstructor == null
                            ? Array.Empty<FunctionSymbol>()
                            : [creation.ResolvedConstructor];
                        break;
                    case BoundExpressionCallExpression expressionCall:
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
        }

        var (resolvedCallIds, boundCallSites, boundResolutionComplete) =
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

        return new CallGraphAnalysis(
            callerToCallees,
            calleeToCallers,
            functions,
            functionNameToId,
            methodNameToIds,
            unresolvedCalls,
            resolvedCallIds,
            boundCallSites,
            boundResolutionComplete,
            sccs);
    }

    private static (
        Dictionary<AstCallKey, IReadOnlyList<string>> Resolved,
        HashSet<AstCallKey> BoundCallSites,
        bool Complete)
        ResolveBoundCallSites(
            ModuleNode ast,
            IReadOnlyDictionary<string, FunctionNode> functions)
    {
        var resolved = new Dictionary<AstCallKey, IReadOnlyList<string>>();
        var boundCallSites = new HashSet<AstCallKey>();

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
                    LegacyId: functions.Values
                        .Where(candidate => candidate.Span == function.Symbol.DefinitionSpan)
                        .Select(candidate => candidate.Id)
                        .FirstOrDefault()))
                .Where(item => item.LegacyId != null)
                .ToDictionary(item => item.SymbolId, item => item.LegacyId!);

            foreach (var function in boundModule.Functions)
            {
                if (!legacyIds.TryGetValue(function.SymbolId, out var callerId))
                    continue;

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
                            break;
                        case BoundCallExpression expression:
                            target = expression.Target;
                            callees = expression.ResolvedSymbols;
                            receiver = expression.ReceiverSymbol;
                            inaccessibleCall = expression.IsInaccessibleCall;
                            break;
                        case BoundNewExpression creation:
                            target = $"{creation.TypeName}..ctor";
                            callees = creation.ResolvedConstructor == null
                                ? Array.Empty<FunctionSymbol>()
                                : [creation.ResolvedConstructor];
                            break;
                        case BoundExpressionCallExpression:
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
            // bound fall back only to unambiguous name resolution below.
            return (resolved, boundCallSites, false);
        }

        return (resolved, boundCallSites, true);
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
            method.Parameters,
            method.Output,
            method.Effects,
            method.Body,
            method.Attributes);
    }

    private static FunctionNode ToCtorFunctionNode(ConstructorNode ctor, string className)
    {
        var qualifiedId = $"{className}.{ctor.Id}";
        return new FunctionNode(
            ctor.Span,
            qualifiedId,
            $"{className}..ctor",
            ctor.Visibility,
            ctor.Parameters,
            output: null,
            effects: null,
            ctor.Body,
            ctor.Attributes);
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
