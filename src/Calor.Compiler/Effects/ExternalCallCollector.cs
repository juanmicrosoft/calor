using Calor.Compiler.Ast;

namespace Calor.Compiler.Effects;

/// <summary>
/// The kind of external call collected from the AST.
/// </summary>
public enum CallKind
{
    Method,
    Constructor,
    Getter,
    Setter
}

/// <summary>
/// A collected external call with its resolved type, method, and kind.
/// </summary>
public sealed record CollectedCall(string TypeName, string MethodName, CallKind Kind);

/// <summary>
/// A call target collected per-function, preserving the caller identity and
/// the raw target string (including bare-name calls that lack a dot).
/// Unlike <see cref="CollectedCall"/>, this is not deduplicated and is not
/// resolved to (type, method) pairs — it is the input to cross-module resolution
/// which needs to see bare-name calls as-is.
/// </summary>
public sealed record RawCall(string CallerName, string Target, bool IsConstructor);

/// <summary>
/// Walks the Calor AST to collect external method invocations.
/// Covers top-level functions, class methods, and constructors.
/// Resolves variable types via §NEW initializer scanning.
///
/// Two collection modes share the traversal logic:
///
///   1. Standard mode (<see cref="Collect"/>): returns <see cref="CollectedCall"/> list —
///      dotted targets resolved to (TypeName, MethodName, CallKind) tuples, deduped.
///      Bare-name targets (no dot) are dropped except for constructor calls.
///      Used by the <c>calor effects suggest</c> command and interop coverage.
///
///   2. Raw per-function mode (<see cref="CollectPerFunctionWithBareNames"/>): returns
///      <see cref="RawCall"/> list — each record tagged with its enclosing function name
///      and preserving the target string verbatim (including bare names). Not deduped.
///      Used by the cross-module effect enforcement pass.
///
/// Modes are selected by the factory method used; a single collector instance is
/// internal to one mode for one module — do not invoke both modes on the same instance.
/// </summary>
public sealed class ExternalCallCollector
{
    private readonly List<CollectedCall> _calls = new();
    private readonly List<RawCall> _rawCalls = new();
    private readonly Dictionary<string, string> _variableTypeMap = new(StringComparer.OrdinalIgnoreCase);

    // Set by CollectPerFunctionWithBareNames before visiting each function's body,
    // so TryAddCall can tag RawCalls with the enclosing caller identity.
    // Null in standard mode.
    private string? _currentCaller;

    // True when this instance is operating in raw per-function mode. Set once at
    // construction by the factory and never toggled.
    private bool _rawMode;

    /// <summary>
    /// Collect all external calls from a module (functions + classes).
    /// </summary>
    public static List<CollectedCall> Collect(ModuleNode module)
    {
        var collector = new ExternalCallCollector();

        foreach (var function in module.Functions)
        {
            collector.CollectFromFunctionBody(function.Body);
        }

        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                collector.CollectFromFunctionBody(method.Body);
            }
            foreach (var ctor in cls.Constructors)
            {
                collector.CollectFromFunctionBody(ctor.Body);
            }
        }

        return collector._calls.Distinct().ToList();
    }

    /// <summary>
    /// Collect raw call targets from a module, tagged with the enclosing caller's name.
    /// Unlike <see cref="Collect"/>, this retains bare-name targets (no dot) so that
    /// cross-module resolution can match against the <see cref="CrossModuleEffectRegistry"/>.
    /// </summary>
    public static List<RawCall> CollectPerFunctionWithBareNames(ModuleNode module)
    {
        var collector = new ExternalCallCollector { _rawMode = true };

        foreach (var function in module.Functions)
        {
            collector._currentCaller = function.Name;
            collector.CollectFromFunctionBody(function.Body);
        }

        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                collector._currentCaller = $"{cls.Name}.{method.Name}";
                collector.CollectFromFunctionBody(method.Body);
            }
            foreach (var ctor in cls.Constructors)
            {
                collector._currentCaller = $"{cls.Name}..ctor";
                collector.CollectFromFunctionBody(ctor.Body);
            }
        }

        return collector._rawCalls;
    }

    private void CollectFromFunctionBody(IReadOnlyList<StatementNode> body)
    {
        // Build variable type map from bind statements in this function
        _variableTypeMap.Clear();
        ScanVariableTypes(body);

        // Collect calls
        CollectFromStatements(body);
    }

    private void ScanVariableTypes(IEnumerable<StatementNode> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is BindStatementNode bind && bind.Initializer is NewExpressionNode newExpr)
            {
                _variableTypeMap[bind.Name] = EffectEnforcementPass.MapShortTypeNameToFullName(newExpr.TypeName);
            }
        }
    }

    private void CollectFromStatements(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
            CollectFromStatement(statement);
    }

    private void CollectFromStatement(StatementNode statement)
    {
        switch (statement)
        {
            case CallStatementNode call:
                TryAddCall(call.Target, CallKind.Method);
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
            case DictionaryForeachNode dictForeach:
                CollectFromStatements(dictForeach.Body);
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
                TryAddCall(call.Target, CallKind.Method);
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
                TryAddCall(newExpr.TypeName, CallKind.Constructor);
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

    private void TryAddCall(string target, CallKind defaultKind)
    {
        // Record the raw target (including bare names) when running in per-function mode.
        // The cross-module pass needs to see bare-name calls to resolve them against the registry.
        if (_rawMode && _currentCaller != null && !string.IsNullOrEmpty(target))
        {
            _rawCalls.Add(new RawCall(_currentCaller, target, defaultKind == CallKind.Constructor));
        }

        var lastDot = target.LastIndexOf('.');
        if (lastDot <= 0)
        {
            // No dot — could be a constructor call (from NewExpressionNode)
            if (defaultKind == CallKind.Constructor)
            {
                var resolvedType = EffectEnforcementPass.MapShortTypeNameToFullName(target);
                _calls.Add(new CollectedCall(resolvedType, ".ctor", CallKind.Constructor));
            }
            return;
        }

        var methodName = target[(lastDot + 1)..];
        var typePart = target[..lastDot];

        // Detect call kind from method name patterns
        var kind = defaultKind;
        if (methodName.StartsWith("get_"))
        {
            kind = CallKind.Getter;
            methodName = methodName[4..]; // strip get_ prefix
        }
        else if (methodName.StartsWith("set_"))
        {
            kind = CallKind.Setter;
            methodName = methodName[4..]; // strip set_ prefix
        }

        // Resolve type: try short name mapping first, then variable type map
        if (!typePart.Contains('.'))
        {
            var mapped = EffectEnforcementPass.MapShortTypeNameToFullName(typePart);
            if (mapped != typePart)
            {
                typePart = mapped;
            }
            else if (_variableTypeMap.TryGetValue(typePart, out var resolvedType))
            {
                typePart = resolvedType;
            }
        }
        else
        {
            // Chained call: try resolving first segment as variable
            var firstDot = typePart.IndexOf('.');
            var receiverName = firstDot > 0 ? typePart[..firstDot] : typePart;
            if (_variableTypeMap.TryGetValue(receiverName, out var resolvedType))
            {
                typePart = resolvedType;
            }
        }

        if (!string.IsNullOrEmpty(typePart) && !string.IsNullOrEmpty(methodName))
        {
            _calls.Add(new CollectedCall(typePart, methodName, kind));
        }
    }
}
