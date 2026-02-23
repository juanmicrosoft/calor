using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Reusable call graph analysis extracted from EffectEnforcementPass.
/// Builds forward and reverse call graphs, resolves function names to IDs,
/// and computes strongly connected components via Tarjan's algorithm.
/// </summary>
public sealed class CallGraphAnalysis
{
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

        // Index class methods and constructors
        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
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
            foreach (var ctor in cls.Constructors)
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
