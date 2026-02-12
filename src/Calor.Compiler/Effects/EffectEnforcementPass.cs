using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Effects;

/// <summary>
/// SCC-based interprocedural effect enforcement pass.
/// Uses Tarjan's algorithm to compute strongly connected components,
/// then processes them in reverse topological order to infer and verify effects.
/// </summary>
public sealed class EffectEnforcementPass
{
    private readonly DiagnosticBag _diagnostics;
    private readonly EffectsCatalog _catalog;
    private readonly EffectResolver _resolver;
    private readonly UnknownCallPolicy _policy;
    private readonly bool _strictEffects;

    // Maps function ID to its node
    private readonly Dictionary<string, FunctionNode> _functions = new(StringComparer.Ordinal);

    // Maps function ID to computed effects
    private readonly Dictionary<string, EffectSet> _computedEffects = new(StringComparer.Ordinal);

    // Call graph: callee → list of callers
    private readonly Dictionary<string, List<string>> _callGraph = new(StringComparer.Ordinal);

    // Reverse call graph: caller → list of callees
    private readonly Dictionary<string, List<(string Callee, TextSpan Span)>> _reverseCallGraph = new(StringComparer.Ordinal);

    // Maps function name to ID for resolving internal calls
    private readonly Dictionary<string, string> _functionNameToId = new(StringComparer.Ordinal);

    public EffectEnforcementPass(
        DiagnosticBag diagnostics,
        EffectsCatalog? catalog = null,
        UnknownCallPolicy policy = UnknownCallPolicy.Strict,
        EffectResolver? resolver = null,
        bool strictEffects = false,
        string? projectDirectory = null,
        string? solutionDirectory = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _catalog = catalog ?? EffectsCatalog.CreateDefault();
        _policy = policy;
        _strictEffects = strictEffects;

        // Initialize the effect resolver with manifests
        _resolver = resolver ?? new EffectResolver(null, _catalog);
        _resolver.Initialize(projectDirectory, solutionDirectory);
    }

    /// <summary>
    /// Enforces effect declarations across all functions in the module.
    /// </summary>
    public void Enforce(ModuleNode module)
    {
        // Phase 1: Build function map and call graph
        BuildCallGraph(module);

        // Phase 2: Compute SCCs using Tarjan's algorithm
        var sccs = ComputeSccs();

        // Phase 3: Process SCCs in reverse topological order
        // (Tarjan produces them in reverse topological order already)
        foreach (var scc in sccs)
        {
            ProcessScc(scc);
        }

        // Phase 4: Check each function's computed effects against declared effects
        foreach (var function in module.Functions)
        {
            CheckEffects(function);
        }
    }

    private void BuildCallGraph(ModuleNode module)
    {
        // Index all functions by ID and name
        foreach (var function in module.Functions)
        {
            _functions[function.Id] = function;
            _functionNameToId[function.Name] = function.Id;
            _callGraph[function.Id] = new List<string>();
            _reverseCallGraph[function.Id] = new List<(string, TextSpan)>();
        }

        // Build call edges
        foreach (var function in module.Functions)
        {
            var calls = CollectCalls(function);
            foreach (var (callee, span) in calls)
            {
                _reverseCallGraph[function.Id].Add((callee, span));

                // Resolve callee name to ID for internal calls
                var calleeId = _functionNameToId.TryGetValue(callee, out var id) ? id : callee;

                // Only track internal calls for SCC computation
                if (_functions.ContainsKey(calleeId))
                {
                    if (!_callGraph.ContainsKey(calleeId))
                    {
                        _callGraph[calleeId] = new List<string>();
                    }
                    _callGraph[calleeId].Add(function.Id);
                }
            }
        }
    }

    private List<(string Callee, TextSpan Span)> CollectCalls(FunctionNode function)
    {
        var calls = new List<(string, TextSpan)>();
        var collector = new CallCollector(calls);
        collector.CollectFromStatements(function.Body);
        return calls;
    }

    /// <summary>
    /// Computes SCCs using Tarjan's algorithm.
    /// Returns SCCs in reverse topological order (leaves first).
    /// </summary>
    private List<List<string>> ComputeSccs()
    {
        var sccs = new List<List<string>>();
        var index = 0;
        var indices = new Dictionary<string, int>();
        var lowlinks = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var stack = new Stack<string>();

        foreach (var functionId in _functions.Keys)
        {
            if (!indices.ContainsKey(functionId))
            {
                Strongconnect(functionId, ref index, indices, lowlinks, onStack, stack, sccs);
            }
        }

        return sccs;
    }

    private void Strongconnect(
        string v,
        ref int index,
        Dictionary<string, int> indices,
        Dictionary<string, int> lowlinks,
        HashSet<string> onStack,
        Stack<string> stack,
        List<List<string>> sccs)
    {
        indices[v] = index;
        lowlinks[v] = index;
        index++;
        stack.Push(v);
        onStack.Add(v);

        // Process successors (functions that v calls)
        foreach (var (calleeName, _) in _reverseCallGraph.GetValueOrDefault(v, new List<(string, TextSpan)>()))
        {
            // Resolve callee name to ID for internal calls
            var calleeId = _functionNameToId.TryGetValue(calleeName, out var id) ? id : calleeName;

            // Only consider internal functions for SCC
            if (!_functions.ContainsKey(calleeId))
                continue;

            if (!indices.ContainsKey(calleeId))
            {
                Strongconnect(calleeId, ref index, indices, lowlinks, onStack, stack, sccs);
                lowlinks[v] = Math.Min(lowlinks[v], lowlinks[calleeId]);
            }
            else if (onStack.Contains(calleeId))
            {
                lowlinks[v] = Math.Min(lowlinks[v], indices[calleeId]);
            }
        }

        // If v is a root node, pop the SCC
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

    private void ProcessScc(List<string> scc)
    {
        // For single-function SCCs with no self-recursion, compute effects directly
        if (scc.Count == 1)
        {
            var functionId = scc[0];
            var function = _functions[functionId];
            var effects = InferEffects(function, new HashSet<string>());
            _computedEffects[functionId] = effects;
            return;
        }

        // For multi-function SCCs (mutual recursion), iterate until fixpoint
        var changed = true;
        var iterations = 0;
        const int maxIterations = 100;

        // Initialize with empty effects
        foreach (var functionId in scc)
        {
            _computedEffects[functionId] = EffectSet.Empty;
        }

        while (changed && iterations < maxIterations)
        {
            changed = false;
            iterations++;

            foreach (var functionId in scc)
            {
                var function = _functions[functionId];
                var newEffects = InferEffects(function, new HashSet<string>(scc));
                var oldEffects = _computedEffects[functionId];

                if (!newEffects.Equals(oldEffects))
                {
                    _computedEffects[functionId] = newEffects;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _diagnostics.ReportWarning(
                _functions[scc[0]].Span,
                "Calor0600",
                $"Effect fixpoint iteration did not converge after {maxIterations} iterations for mutually recursive functions. Effects may be incomplete.");
        }
    }

    private EffectSet InferEffects(FunctionNode function, HashSet<string> sccMembers)
    {
        var context = new InferenceContext(_catalog, _resolver, _computedEffects, _functions, sccMembers, _policy, _strictEffects, _diagnostics, function.Id);
        var inferrer = new EffectInferrer(context);
        return inferrer.InferFromStatements(function.Body);
    }

    private void CheckEffects(FunctionNode function)
    {
        var declaredEffects = GetDeclaredEffects(function);
        var computedEffects = _computedEffects.GetValueOrDefault(function.Id, EffectSet.Empty);

        // Check if computed effects are a subset of declared effects
        if (!computedEffects.IsSubsetOf(declaredEffects))
        {
            var forbidden = computedEffects.Except(declaredEffects).ToList();

            foreach (var (kind, value) in forbidden)
            {
                // Find the call chain that leads to this effect
                var chain = FindCallChain(function.Id, kind, value);
                var chainStr = chain.Count > 0 ? $"\n  Call chain: {string.Join(" → ", chain)}" : "";

                _diagnostics.Report(
                    function.Effects?.Span ?? function.Span,
                    DiagnosticCode.ForbiddenEffect,
                    $"Function '{function.Name}' uses effect '{EffectSetExtensions.ToSurfaceCode(kind, value)}' but does not declare it{chainStr}",
                    DiagnosticSeverity.Error);
            }
        }
    }

    private EffectSet GetDeclaredEffects(FunctionNode function)
    {
        if (function.Effects == null || function.Effects.Effects.Count == 0)
        {
            return EffectSet.Empty;
        }

        // Effects dictionary is key:value like {"io": "console_write"} or {"io": "console_write,filesystem_write"}
        // Values can be comma-separated if multiple effects were declared in the same category
        var surfaceCodes = new List<string>();
        foreach (var kv in function.Effects.Effects)
        {
            // Split comma-separated values
            var values = kv.Value.Split(',');
            foreach (var value in values)
            {
                var trimmedValue = value.Trim();
                if (kv.Key.Contains(':') || IsSurfaceCode(trimmedValue))
                {
                    surfaceCodes.Add(trimmedValue);
                }
                else
                {
                    surfaceCodes.Add($"{kv.Key}:{trimmedValue}");
                }
            }
        }
        return EffectSet.From(surfaceCodes.ToArray());
    }

    private static bool IsSurfaceCode(string code)
    {
        // Check if the value is already a surface code
        return code.ToLowerInvariant() switch
        {
            "cw" or "cr" or "fw" or "fr" or "net" or "http" or "db" or "time" or "rand" or "mut" or "throw" => true,
            _ => false
        };
    }

    private List<string> FindCallChain(string startFunctionId, EffectKind targetKind, string targetValue)
    {
        // BFS to find shortest path to the effect
        var queue = new Queue<(string FunctionId, List<string> Path)>();
        var visited = new HashSet<string>();

        queue.Enqueue((startFunctionId, new List<string> { _functions[startFunctionId].Name }));
        visited.Add(startFunctionId);

        while (queue.Count > 0)
        {
            var (currentId, path) = queue.Dequeue();

            // Check direct effects from this function's body
            if (_reverseCallGraph.TryGetValue(currentId, out var calls))
            {
                foreach (var (calleeName, span) in calls)
                {
                    // Resolve callee name to ID for internal calls
                    var calleeId = _functionNameToId.TryGetValue(calleeName, out var id) ? id : calleeName;

                    // Check external calls
                    if (!_functions.ContainsKey(calleeId))
                    {
                        var effects = _catalog.TryGetEffects(calleeName);
                        if (effects != null && effects.Contains(targetKind, targetValue))
                        {
                            var result = new List<string>(path) { calleeName };
                            return result;
                        }
                    }
                    // Check internal calls
                    else if (!visited.Contains(calleeId))
                    {
                        visited.Add(calleeId);
                        var newPath = new List<string>(path) { _functions[calleeId].Name };
                        queue.Enqueue((calleeId, newPath));
                    }
                }
            }
        }

        return new List<string>();
    }

    /// <summary>
    /// Context for effect inference.
    /// </summary>
    private sealed class InferenceContext
    {
        public EffectsCatalog Catalog { get; }
        public EffectResolver Resolver { get; }
        public Dictionary<string, EffectSet> ComputedEffects { get; }
        public Dictionary<string, FunctionNode> Functions { get; }
        public HashSet<string> SccMembers { get; }
        public UnknownCallPolicy Policy { get; }
        public bool StrictEffects { get; }
        public DiagnosticBag Diagnostics { get; }
        public string CurrentFunctionId { get; }

        public InferenceContext(
            EffectsCatalog catalog,
            EffectResolver resolver,
            Dictionary<string, EffectSet> computedEffects,
            Dictionary<string, FunctionNode> functions,
            HashSet<string> sccMembers,
            UnknownCallPolicy policy,
            bool strictEffects,
            DiagnosticBag diagnostics,
            string currentFunctionId)
        {
            Catalog = catalog;
            Resolver = resolver;
            ComputedEffects = computedEffects;
            Functions = functions;
            SccMembers = sccMembers;
            Policy = policy;
            StrictEffects = strictEffects;
            Diagnostics = diagnostics;
            CurrentFunctionId = currentFunctionId;
        }
    }

    /// <summary>
    /// Infers effects from AST nodes.
    /// </summary>
    private sealed class EffectInferrer
    {
        private readonly InferenceContext _context;

        public EffectInferrer(InferenceContext context)
        {
            _context = context;
        }

        public EffectSet InferFromStatements(IEnumerable<StatementNode> statements)
        {
            var effects = EffectSet.Empty;
            foreach (var statement in statements)
            {
                effects = effects.Union(InferFromStatement(statement));
            }
            return effects;
        }

        private EffectSet InferFromStatement(StatementNode statement)
        {
            return statement switch
            {
                PrintStatementNode => EffectSet.From("cw"),
                CallStatementNode call => InferFromCallStatement(call),
                IfStatementNode ifStmt => InferFromIf(ifStmt),
                ForStatementNode forStmt => InferFromFor(forStmt),
                WhileStatementNode whileStmt => InferFromExpression(whileStmt.Condition).Union(InferFromStatements(whileStmt.Body)),
                DoWhileStatementNode doWhile => InferFromExpression(doWhile.Condition).Union(InferFromStatements(doWhile.Body)),
                ForeachStatementNode foreach_ => InferFromExpression(foreach_.Collection).Union(InferFromStatements(foreach_.Body)),
                MatchStatementNode matchStmt => InferFromMatch(matchStmt),
                TryStatementNode tryStmt => InferFromTry(tryStmt),
                ThrowStatementNode => EffectSet.From("throw"),
                RethrowStatementNode => EffectSet.From("throw"),
                ReturnStatementNode ret => ret.Expression != null ? InferFromExpression(ret.Expression) : EffectSet.Empty,
                BindStatementNode bind => bind.Initializer != null ? InferFromExpression(bind.Initializer) : EffectSet.Empty,
                AssignmentStatementNode assign => InferFromAssignment(assign),
                _ => EffectSet.Empty
            };
        }

        private EffectSet InferFromCallStatement(CallStatementNode call)
        {
            var effects = InferFromCallTarget(call.Target, call.Span);

            // Also infer from arguments
            foreach (var arg in call.Arguments)
            {
                effects = effects.Union(InferFromExpression(arg));
            }

            return effects;
        }

        private EffectSet InferFromCallTarget(string target, TextSpan span)
        {
            // Check if it's an internal function call by name
            var internalFunc = FindInternalFunctionByName(target);
            if (internalFunc != null)
            {
                if (_context.ComputedEffects.TryGetValue(internalFunc.Id, out var computed))
                {
                    return computed;
                }
                // If in same SCC, return current approximation
                if (_context.SccMembers.Contains(internalFunc.Id))
                {
                    return _context.ComputedEffects.GetValueOrDefault(internalFunc.Id, EffectSet.Empty);
                }
            }

            // Try to resolve using the EffectResolver (manifest-based)
            var (typeName, methodName) = ParseCallTarget(target);
            if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName))
            {
                var resolution = _context.Resolver.Resolve(typeName, methodName);
                if (resolution.Status != EffectResolutionStatus.Unknown)
                {
                    return resolution.Effects;
                }
            }

            // Fall back to legacy signature-based lookup
            var signatures = BuildPotentialSignatures(target);
            foreach (var sig in signatures)
            {
                var effects = _context.Catalog.TryGetEffects(sig);
                if (effects != null)
                {
                    return effects;
                }
            }

            // Unknown external call - report diagnostic based on policy
            ReportUnknownCall(target, span);
            return EffectSet.Unknown;
        }

        private void ReportUnknownCall(string target, TextSpan span)
        {
            // Calor0411: Unknown external call
            var severity = _context.StrictEffects
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;

            if (_context.Policy == UnknownCallPolicy.Strict || _context.StrictEffects)
            {
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.UnknownExternalCall,
                    $"Unknown external call to '{target}'. Add effect declaration in a manifest or calor.effects.json.",
                    severity);
            }
            else if (_context.Policy == UnknownCallPolicy.Warn)
            {
                _context.Diagnostics.Report(
                    span,
                    DiagnosticCode.UnknownExternalCall,
                    $"Unknown external call to '{target}' - assuming worst-case effects. Consider adding to manifest.",
                    DiagnosticSeverity.Warning);
            }
        }

        private static (string TypeName, string MethodName) ParseCallTarget(string target)
        {
            // Handle patterns like "Console.WriteLine", "File.ReadAllText", "System.IO.File.ReadAllText"
            var lastDot = target.LastIndexOf('.');
            if (lastDot <= 0)
                return ("", "");

            var methodName = target[(lastDot + 1)..];
            var typePart = target[..lastDot];

            // If type part doesn't contain a dot, try common namespaces
            if (!typePart.Contains('.'))
            {
                // Map common short names to full types
                typePart = typePart switch
                {
                    "Console" => "System.Console",
                    "File" => "System.IO.File",
                    "Directory" => "System.IO.Directory",
                    "Path" => "System.IO.Path",
                    "Random" => "System.Random",
                    "DateTime" => "System.DateTime",
                    "Environment" => "System.Environment",
                    "Process" => "System.Diagnostics.Process",
                    "HttpClient" => "System.Net.Http.HttpClient",
                    "Math" => "System.Math",
                    "Guid" => "System.Guid",
                    _ => typePart
                };
            }

            return (typePart, methodName);
        }

        private FunctionNode? FindInternalFunctionByName(string name)
        {
            foreach (var kvp in _context.ComputedEffects)
            {
                if (_context.Functions.TryGetValue(kvp.Key, out var func) &&
                    func.Name.Equals(name, StringComparison.Ordinal))
                {
                    return func;
                }
            }
            return null;
        }

        private List<string> BuildPotentialSignatures(string target)
        {
            var signatures = new List<string>();

            // Handle common patterns
            // Console.WriteLine -> System.Console::WriteLine(...)
            var parts = target.Split('.');
            if (parts.Length == 2)
            {
                var typeName = parts[0];
                var methodName = parts[1];

                // Try common BCL namespaces
                var namespaces = new[]
                {
                    "System",
                    "System.IO",
                    "System.Net.Http",
                    "System.Threading",
                    "System.Threading.Tasks",
                    "System.Diagnostics"
                };

                foreach (var ns in namespaces)
                {
                    // Try with various common parameter patterns
                    signatures.Add($"{ns}.{typeName}::{methodName}()");
                    signatures.Add($"{ns}.{typeName}::{methodName}(System.String)");
                    signatures.Add($"{ns}.{typeName}::{methodName}(System.Object)");
                    signatures.Add($"{ns}.{typeName}::{methodName}(System.Int32)");
                    signatures.Add($"{ns}.{typeName}::get_{methodName}()");  // Property getter
                }
            }

            return signatures;
        }

        private EffectSet InferFromIf(IfStatementNode ifStmt)
        {
            var effects = InferFromExpression(ifStmt.Condition);
            effects = effects.Union(InferFromStatements(ifStmt.ThenBody));

            foreach (var elseIf in ifStmt.ElseIfClauses)
            {
                effects = effects.Union(InferFromExpression(elseIf.Condition));
                effects = effects.Union(InferFromStatements(elseIf.Body));
            }

            if (ifStmt.ElseBody != null)
            {
                effects = effects.Union(InferFromStatements(ifStmt.ElseBody));
            }

            return effects;
        }

        private EffectSet InferFromFor(ForStatementNode forStmt)
        {
            var effects = InferFromExpression(forStmt.From);
            effects = effects.Union(InferFromExpression(forStmt.To));
            if (forStmt.Step != null)
            {
                effects = effects.Union(InferFromExpression(forStmt.Step));
            }
            effects = effects.Union(InferFromStatements(forStmt.Body));
            return effects;
        }

        private EffectSet InferFromMatch(MatchStatementNode matchStmt)
        {
            var effects = InferFromExpression(matchStmt.Target);
            foreach (var matchCase in matchStmt.Cases)
            {
                effects = effects.Union(InferFromStatements(matchCase.Body));
            }
            return effects;
        }

        private EffectSet InferFromTry(TryStatementNode tryStmt)
        {
            var effects = InferFromStatements(tryStmt.TryBody);

            foreach (var catchClause in tryStmt.CatchClauses)
            {
                effects = effects.Union(InferFromStatements(catchClause.Body));
            }

            if (tryStmt.FinallyBody != null)
            {
                effects = effects.Union(InferFromStatements(tryStmt.FinallyBody));
            }

            return effects;
        }

        private EffectSet InferFromAssignment(AssignmentStatementNode assign)
        {
            var effects = InferFromExpression(assign.Value);

            // Check if this is a mutation (writing to non-local object)
            if (assign.Target is FieldAccessNode)
            {
                effects = effects.Union(EffectSet.From("mut"));
            }

            return effects;
        }

        private EffectSet InferFromExpression(ExpressionNode expr)
        {
            return expr switch
            {
                CallExpressionNode call => InferFromCallExpression(call),
                MatchExpressionNode match => InferFromMatchExpression(match),
                BinaryOperationNode binOp => InferFromExpression(binOp.Left).Union(InferFromExpression(binOp.Right)),
                UnaryOperationNode unOp => InferFromExpression(unOp.Operand),
                ConditionalExpressionNode cond => InferFromExpression(cond.Condition)
                    .Union(InferFromExpression(cond.WhenTrue))
                    .Union(InferFromExpression(cond.WhenFalse)),
                SomeExpressionNode some => InferFromExpression(some.Value),
                OkExpressionNode ok => InferFromExpression(ok.Value),
                ErrExpressionNode err => InferFromExpression(err.Error),
                NewExpressionNode newExpr => InferFromNewExpression(newExpr),
                FieldAccessNode field => InferFromExpression(field.Target),
                ArrayAccessNode array => InferFromExpression(array.Array).Union(InferFromExpression(array.Index)),
                LambdaExpressionNode lambda => InferFromLambda(lambda),
                AwaitExpressionNode await_ => InferFromExpression(await_.Awaited),
                _ => EffectSet.Empty
            };
        }

        private EffectSet InferFromCallExpression(CallExpressionNode call)
        {
            var effects = InferFromCallTarget(call.Target, call.Span);

            foreach (var arg in call.Arguments)
            {
                effects = effects.Union(InferFromExpression(arg));
            }

            return effects;
        }

        private EffectSet InferFromMatchExpression(MatchExpressionNode match)
        {
            var effects = InferFromExpression(match.Target);
            foreach (var matchCase in match.Cases)
            {
                effects = effects.Union(InferFromStatements(matchCase.Body));
            }
            return effects;
        }

        private EffectSet InferFromNewExpression(NewExpressionNode newExpr)
        {
            var effects = EffectSet.Empty;

            // Check if constructor has effects
            var signature = EffectsCatalog.BuildConstructorSignature("", newExpr.TypeName);
            var ctorEffects = _context.Catalog.TryGetEffects(signature);
            if (ctorEffects != null)
            {
                effects = effects.Union(ctorEffects);
            }

            foreach (var arg in newExpr.Arguments)
            {
                effects = effects.Union(InferFromExpression(arg));
            }

            return effects;
        }

        private EffectSet InferFromLambda(LambdaExpressionNode lambda)
        {
            // Lambda body contributes effects to enclosing function
            if (lambda.ExpressionBody != null)
            {
                return InferFromExpression(lambda.ExpressionBody);
            }
            if (lambda.StatementBody != null)
            {
                return InferFromStatements(lambda.StatementBody);
            }
            return EffectSet.Empty;
        }
    }

    /// <summary>
    /// Collects all call targets from statements.
    /// </summary>
    private sealed class CallCollector
    {
        private readonly List<(string, TextSpan)> _calls;

        public CallCollector(List<(string, TextSpan)> calls)
        {
            _calls = calls;
        }

        public void CollectFromStatements(IEnumerable<StatementNode> statements)
        {
            foreach (var statement in statements)
            {
                CollectFromStatement(statement);
            }
        }

        private void CollectFromStatement(StatementNode statement)
        {
            switch (statement)
            {
                case CallStatementNode call:
                    _calls.Add((call.Target, call.Span));
                    CollectFromExpressions(call.Arguments);
                    break;
                case IfStatementNode ifStmt:
                    CollectFromExpression(ifStmt.Condition);
                    CollectFromStatements(ifStmt.ThenBody);
                    foreach (var elseIf in ifStmt.ElseIfClauses)
                    {
                        CollectFromExpression(elseIf.Condition);
                        CollectFromStatements(elseIf.Body);
                    }
                    if (ifStmt.ElseBody != null)
                        CollectFromStatements(ifStmt.ElseBody);
                    break;
                case ForStatementNode forStmt:
                    CollectFromStatements(forStmt.Body);
                    break;
                case WhileStatementNode whileStmt:
                    CollectFromExpression(whileStmt.Condition);
                    CollectFromStatements(whileStmt.Body);
                    break;
                case DoWhileStatementNode doWhile:
                    CollectFromStatements(doWhile.Body);
                    CollectFromExpression(doWhile.Condition);
                    break;
                case ForeachStatementNode foreach_:
                    CollectFromExpression(foreach_.Collection);
                    CollectFromStatements(foreach_.Body);
                    break;
                case MatchStatementNode matchStmt:
                    CollectFromExpression(matchStmt.Target);
                    foreach (var matchCase in matchStmt.Cases)
                        CollectFromStatements(matchCase.Body);
                    break;
                case TryStatementNode tryStmt:
                    CollectFromStatements(tryStmt.TryBody);
                    foreach (var catchClause in tryStmt.CatchClauses)
                        CollectFromStatements(catchClause.Body);
                    if (tryStmt.FinallyBody != null)
                        CollectFromStatements(tryStmt.FinallyBody);
                    break;
                case ReturnStatementNode ret:
                    if (ret.Expression != null)
                        CollectFromExpression(ret.Expression);
                    break;
                case BindStatementNode bind:
                    if (bind.Initializer != null)
                        CollectFromExpression(bind.Initializer);
                    break;
                case AssignmentStatementNode assign:
                    CollectFromExpression(assign.Target);
                    CollectFromExpression(assign.Value);
                    break;
            }
        }

        private void CollectFromExpressions(IEnumerable<ExpressionNode> expressions)
        {
            foreach (var expr in expressions)
                CollectFromExpression(expr);
        }

        private void CollectFromExpression(ExpressionNode expr)
        {
            switch (expr)
            {
                case CallExpressionNode call:
                    _calls.Add((call.Target, call.Span));
                    CollectFromExpressions(call.Arguments);
                    break;
                case BinaryOperationNode binOp:
                    CollectFromExpression(binOp.Left);
                    CollectFromExpression(binOp.Right);
                    break;
                case UnaryOperationNode unOp:
                    CollectFromExpression(unOp.Operand);
                    break;
                case ConditionalExpressionNode cond:
                    CollectFromExpression(cond.Condition);
                    CollectFromExpression(cond.WhenTrue);
                    CollectFromExpression(cond.WhenFalse);
                    break;
                case MatchExpressionNode match:
                    CollectFromExpression(match.Target);
                    foreach (var matchCase in match.Cases)
                        CollectFromStatements(matchCase.Body);
                    break;
                case NewExpressionNode newExpr:
                    CollectFromExpressions(newExpr.Arguments);
                    break;
                case FieldAccessNode field:
                    CollectFromExpression(field.Target);
                    break;
                case ArrayAccessNode array:
                    CollectFromExpression(array.Array);
                    CollectFromExpression(array.Index);
                    break;
                case LambdaExpressionNode lambda:
                    if (lambda.ExpressionBody != null)
                        CollectFromExpression(lambda.ExpressionBody);
                    if (lambda.StatementBody != null)
                        CollectFromStatements(lambda.StatementBody);
                    break;
                case AwaitExpressionNode await_:
                    CollectFromExpression(await_.Awaited);
                    break;
                case SomeExpressionNode some:
                    CollectFromExpression(some.Value);
                    break;
                case OkExpressionNode ok:
                    CollectFromExpression(ok.Value);
                    break;
                case ErrExpressionNode err:
                    CollectFromExpression(err.Error);
                    break;
            }
        }
    }
}

/// <summary>
/// Policy for handling unknown external calls.
/// </summary>
public enum UnknownCallPolicy
{
    /// <summary>
    /// Unknown calls are errors (v1 default).
    /// </summary>
    Strict,

    /// <summary>
    /// Unknown calls produce warnings, assume worst-case effects.
    /// </summary>
    Warn,

    /// <summary>
    /// Unknown calls are errors unless stubbed.
    /// </summary>
    StubRequired
}

/// <summary>
/// Extension methods for EffectSet display.
/// </summary>
internal static class EffectSetExtensions
{
    public static string ToSurfaceCode(EffectKind kind, string value)
    {
        return (kind, value) switch
        {
            // Console I/O
            (EffectKind.IO, "console_write") => "cw",
            (EffectKind.IO, "console_read") => "cr",

            // Filesystem effects
            (EffectKind.IO, "filesystem_read") => "fs:r",
            (EffectKind.IO, "filesystem_write") => "fs:w",
            (EffectKind.IO, "filesystem_readwrite") => "fs:rw",

            // Network effects
            (EffectKind.IO, "network_read") => "net:r",
            (EffectKind.IO, "network_write") => "net:w",
            (EffectKind.IO, "network_readwrite") => "net:rw",

            // Database effects
            (EffectKind.IO, "database_read") => "db:r",
            (EffectKind.IO, "database_write") => "db:w",
            (EffectKind.IO, "database_readwrite") => "db:rw",

            // Environment effects
            (EffectKind.IO, "environment_read") => "env:r",
            (EffectKind.IO, "environment_write") => "env:w",

            // System
            (EffectKind.IO, "process") => "proc",

            // Memory effects
            (EffectKind.Memory, "allocation") => "alloc",
            (EffectKind.Memory, "unsafe") => "unsafe",

            // Nondeterminism
            (EffectKind.Nondeterminism, "time") => "time",
            (EffectKind.Nondeterminism, "random") => "rand",

            // Mutation/Exception
            (EffectKind.Mutation, "heap_write") => "mut",
            (EffectKind.Exception, "intentional") => "throw",

            _ => $"{kind}:{value}"
        };
    }
}
