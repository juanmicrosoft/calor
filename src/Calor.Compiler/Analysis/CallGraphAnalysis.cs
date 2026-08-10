using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis;

public sealed record ResolvedCallSite(SymbolId Callee, string Target, TextSpan Span);
public sealed record UnresolvedCallSite(SymbolId Caller, string Target, TextSpan Span);

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
                SymbolId? callee = null;

                switch (node)
                {
                    case BoundCallStatement statement:
                        target = statement.Target;
                        span = statement.Span;
                        callee = statement.ResolvedSymbolId;
                        break;
                    case BoundCallExpression expression:
                        target = expression.Target;
                        span = expression.Span;
                        callee = expression.ResolvedSymbolId;
                        break;
                }

                if (target == null)
                    continue;

                if (callee is { } resolved && !resolved.IsNone)
                {
                    forward[function.SymbolId].Add(new ResolvedCallSite(resolved, target, span));
                    if (!reverse.TryGetValue(resolved, out var callers))
                    {
                        callers = new List<SymbolId>();
                        reverse[resolved] = callers;
                    }
                    callers.Add(function.SymbolId);
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
    /// Strongly connected components in reverse topological order.
    /// </summary>
    public List<List<string>> StronglyConnectedComponents { get; }

    private CallGraphAnalysis(
        Dictionary<string, List<(string Callee, TextSpan Span)>> forwardGraph,
        Dictionary<string, List<string>> reverseGraph,
        Dictionary<string, FunctionNode> functions,
        Dictionary<string, string> functionNameToId,
        Dictionary<string, List<string>> methodNameToIds,
        List<List<string>> sccs)
    {
        ForwardGraph = forwardGraph;
        ReverseGraph = reverseGraph;
        Functions = functions;
        FunctionNameToId = functionNameToId;
        MethodNameToIds = methodNameToIds;
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
        var methodNameToIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // callee → callers
        var calleeToCallers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // caller → callees with spans
        var callerToCallees = new Dictionary<string, List<(string, TextSpan)>>(StringComparer.Ordinal);

        // Index all top-level functions
        foreach (var function in ast.Functions)
        {
            functions[function.Id] = function;
            functionNameToId[function.Name] = function.Id;
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
                functionNameToId[wrapped.Name] = wrapped.Id;
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
                calleeToCallers[wrapped.Id] = new List<string>();
                callerToCallees[wrapped.Id] = new List<(string, TextSpan)>();
            }
        }

        // Build call edges
        foreach (var function in functions.Values)
        {
            var calls = CollectCalls(function);
            callerToCallees[function.Id] = calls;

            foreach (var (callee, _) in calls)
            {
                var calleeIds = ResolveToAllInternalIds(callee, functions, functionNameToId, methodNameToIds);
                foreach (var calleeId in calleeIds)
                {
                    if (!calleeToCallers.ContainsKey(calleeId))
                        calleeToCallers[calleeId] = new List<string>();
                    calleeToCallers[calleeId].Add(function.Id);
                }
            }
        }

        // Compute SCCs
        var sccs = ComputeSccs(functions, callerToCallees, functionNameToId, methodNameToIds);

        return new CallGraphAnalysis(callerToCallees, calleeToCallers, functions, functionNameToId, methodNameToIds, sccs);
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
            var resolvedId = ResolveToInternalId(callee);
            result.Add((resolvedId ?? callee, callee, span));
        }
        return result;
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
        CollectCallsFromStatements(function.Body, calls);
        return calls;
    }

    private static void CollectCallsFromStatements(IEnumerable<StatementNode> statements, List<(string, TextSpan)> calls)
    {
        foreach (var statement in statements)
            CollectCallsFromStatement(statement, calls);
    }

    private static void CollectCallsFromStatement(StatementNode statement, List<(string, TextSpan)> calls)
    {
        switch (statement)
        {
            case CallStatementNode call:
                calls.Add((call.Target, call.Span));
                CollectCallsFromExpressions(call.Arguments, calls);
                break;
            case IfStatementNode ifStmt:
                CollectCallsFromExpression(ifStmt.Condition, calls);
                CollectCallsFromStatements(ifStmt.ThenBody, calls);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    CollectCallsFromExpression(elseIf.Condition, calls);
                    CollectCallsFromStatements(elseIf.Body, calls);
                }
                if (ifStmt.ElseBody != null)
                    CollectCallsFromStatements(ifStmt.ElseBody, calls);
                break;
            case ForStatementNode forStmt:
                CollectCallsFromStatements(forStmt.Body, calls);
                break;
            case WhileStatementNode whileStmt:
                CollectCallsFromExpression(whileStmt.Condition, calls);
                CollectCallsFromStatements(whileStmt.Body, calls);
                break;
            case DoWhileStatementNode doWhile:
                CollectCallsFromStatements(doWhile.Body, calls);
                CollectCallsFromExpression(doWhile.Condition, calls);
                break;
            case ForeachStatementNode foreach_:
                CollectCallsFromExpression(foreach_.Collection, calls);
                CollectCallsFromStatements(foreach_.Body, calls);
                break;
            case MatchStatementNode matchStmt:
                CollectCallsFromExpression(matchStmt.Target, calls);
                foreach (var matchCase in matchStmt.Cases)
                    CollectCallsFromStatements(matchCase.Body, calls);
                break;
            case TryStatementNode tryStmt:
                CollectCallsFromStatements(tryStmt.TryBody, calls);
                foreach (var catchClause in tryStmt.CatchClauses)
                    CollectCallsFromStatements(catchClause.Body, calls);
                if (tryStmt.FinallyBody != null)
                    CollectCallsFromStatements(tryStmt.FinallyBody, calls);
                break;
            case ReturnStatementNode ret:
                if (ret.Expression != null)
                    CollectCallsFromExpression(ret.Expression, calls);
                break;
            case BindStatementNode bind:
                if (bind.Initializer != null)
                    CollectCallsFromExpression(bind.Initializer, calls);
                break;
            case AssignmentStatementNode assign:
                CollectCallsFromExpression(assign.Target, calls);
                CollectCallsFromExpression(assign.Value, calls);
                break;
            case PreprocessorDirectiveNode pp:
                // Conditional-compilation branches may be active: collect call
                // edges from every branch (W2 review C1).
                CollectCallsFromStatements(pp.Body, calls);
                if (pp.ElseBody != null)
                    CollectCallsFromStatements(pp.ElseBody, calls);
                break;
        }
    }

    private static void CollectCallsFromExpressions(IEnumerable<ExpressionNode> expressions, List<(string, TextSpan)> calls)
    {
        foreach (var expr in expressions)
            CollectCallsFromExpression(expr, calls);
    }

    private static void CollectCallsFromExpression(ExpressionNode expr, List<(string, TextSpan)> calls)
    {
        switch (expr)
        {
            case CallExpressionNode call:
                calls.Add((call.Target, call.Span));
                CollectCallsFromExpressions(call.Arguments, calls);
                break;
            case BinaryOperationNode binOp:
                CollectCallsFromExpression(binOp.Left, calls);
                CollectCallsFromExpression(binOp.Right, calls);
                break;
            case UnaryOperationNode unOp:
                CollectCallsFromExpression(unOp.Operand, calls);
                break;
            case ConditionalExpressionNode cond:
                CollectCallsFromExpression(cond.Condition, calls);
                CollectCallsFromExpression(cond.WhenTrue, calls);
                CollectCallsFromExpression(cond.WhenFalse, calls);
                break;
            case MatchExpressionNode match:
                CollectCallsFromExpression(match.Target, calls);
                foreach (var matchCase in match.Cases)
                    CollectCallsFromStatements(matchCase.Body, calls);
                break;
            case NewExpressionNode newExpr:
                CollectCallsFromExpressions(newExpr.Arguments, calls);
                break;
            case FieldAccessNode field:
                CollectCallsFromExpression(field.Target, calls);
                break;
            case ArrayAccessNode array:
                CollectCallsFromExpression(array.Array, calls);
                CollectCallsFromExpression(array.Index, calls);
                break;
            case LambdaExpressionNode lambda:
                if (lambda.ExpressionBody != null)
                    CollectCallsFromExpression(lambda.ExpressionBody, calls);
                if (lambda.StatementBody != null)
                    CollectCallsFromStatements(lambda.StatementBody, calls);
                break;
            case AwaitExpressionNode await_:
                CollectCallsFromExpression(await_.Awaited, calls);
                break;
            case SomeExpressionNode some:
                CollectCallsFromExpression(some.Value, calls);
                break;
            case OkExpressionNode ok:
                CollectCallsFromExpression(ok.Value, calls);
                break;
            case ErrExpressionNode err:
                CollectCallsFromExpression(err.Error, calls);
                break;
        }
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
            if (methodNameToIds.TryGetValue(bareMethodName, out var candidates))
                return candidates;
        }

        return new List<string>();
    }

    private static List<List<string>> ComputeSccs(
        Dictionary<string, FunctionNode> functions,
        Dictionary<string, List<(string Callee, TextSpan Span)>> forwardGraph,
        Dictionary<string, string> functionNameToId,
        Dictionary<string, List<string>> methodNameToIds)
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
                    functions, forwardGraph, functionNameToId, methodNameToIds);
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
        Dictionary<string, List<string>> methodNameToIds)
    {
        indices[v] = index;
        lowlinks[v] = index;
        index++;
        stack.Push(v);
        onStack.Add(v);

        if (forwardGraph.TryGetValue(v, out var calls))
        {
            foreach (var (calleeName, _) in calls)
            {
                // Resolve to single internal ID
                string? calleeId = null;
                if (functionNameToId.TryGetValue(calleeName, out var id) && functions.ContainsKey(id))
                    calleeId = id;
                else if (functions.ContainsKey(calleeName))
                    calleeId = calleeName;
                else
                {
                    var lastDot = calleeName.LastIndexOf('.');
                    if (lastDot > 0)
                    {
                        var bare = calleeName[(lastDot + 1)..];
                        if (methodNameToIds.TryGetValue(bare, out var candidates) && candidates.Count == 1)
                            calleeId = candidates[0];
                        else if (functionNameToId.TryGetValue(bare, out var bareId) && functions.ContainsKey(bareId))
                            calleeId = bareId;
                    }
                }

                if (calleeId == null) continue;

                if (!indices.ContainsKey(calleeId))
                {
                    Strongconnect(calleeId, ref index, indices, lowlinks, onStack, stack, sccs,
                        functions, forwardGraph, functionNameToId, methodNameToIds);
                    lowlinks[v] = Math.Min(lowlinks[v], lowlinks[calleeId]);
                }
                else if (onStack.Contains(calleeId))
                {
                    lowlinks[v] = Math.Min(lowlinks[v], indices[calleeId]);
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
