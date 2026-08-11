using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;

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
/// Resolves receiver types from bound SymbolId identities first, with §NEW
/// initializer scanning retained for AST-only compatibility.
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
    private readonly Dictionary<(int Start, int End, string Target), string> _boundReceiverTypes = new();

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
        collector.IndexBoundCallReceivers(module);

        foreach (var function in module.Functions)
        {
            collector.CollectFromFunctionBody(function.Body);
        }

        foreach (var cls in CallGraphAnalysis.EnumerateClasses(module))
        {
            foreach (var method in CallGraphAnalysis.EnumerateMethods(cls))
            {
                collector.CollectFromFunctionBody(method.Body);
            }
            foreach (var ctor in CallGraphAnalysis.EnumerateConstructors(cls))
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
        collector.IndexBoundCallReceivers(module);

        foreach (var function in module.Functions)
        {
            collector._currentCaller = function.Name;
            collector.CollectFromFunctionBody(function.Body);
        }

        foreach (var cls in CallGraphAnalysis.EnumerateClasses(module))
        {
            foreach (var method in CallGraphAnalysis.EnumerateMethods(cls))
            {
                collector._currentCaller = $"{cls.Name}.{method.Name}";
                collector.CollectFromFunctionBody(method.Body);
            }
            foreach (var ctor in CallGraphAnalysis.EnumerateConstructors(cls))
            {
                collector._currentCaller =
                    $"{cls.Name}.{(ctor.IsStatic ? ".cctor" : ".ctor")}";
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
            ScanVariableTypes(stmt);
    }

    private void ScanVariableTypes(AstNode node)
    {
        if (node is BindStatementNode
            {
                Initializer: NewExpressionNode creation,
            } bind)
        {
            _variableTypeMap[bind.Name] =
                EffectEnforcementPass.MapShortTypeNameToFullName(creation.TypeName);
        }

        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            ScanVariableTypes(child);
    }

    private void CollectFromStatements(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
            CollectFromNode(statement);
    }

    private void CollectFromNode(AstNode node)
    {
        switch (node)
        {
            case CallStatementNode call:
                TryAddCall(call.Target, CallKind.Method, call.Span);
                break;
            case CallExpressionNode call:
                TryAddCall(call.Target, CallKind.Method, call.Span);
                break;
            case NewExpressionNode newExpr:
                TryAddCall(newExpr.TypeName, CallKind.Constructor, newExpr.Span);
                break;
            case ExpressionCallNode:
                TryAddCall("<expression-call>", CallKind.Method, node.Span);
                break;
        }

        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            CollectFromNode(child);
    }

    private void TryAddCall(string target, CallKind defaultKind, TextSpan span)
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
        var boundReceiverType = _boundReceiverTypes.GetValueOrDefault(
            (span.Start, span.End, target));

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

        // Prefer the exact bound receiver symbol. The AST name map remains a
        // compatibility fallback for source shapes the Binder cannot represent.
        if (boundReceiverType != null)
        {
            typePart = EffectEnforcementPass.MapShortTypeNameToFullName(
                GetNominalTypeName(boundReceiverType));
        }
        else if (!typePart.Contains('.'))
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

    private void IndexBoundCallReceivers(ModuleNode module)
    {
        try
        {
            var bound = new Binder(new Calor.Compiler.Diagnostics.DiagnosticBag()).Bind(module);
            foreach (var node in Descendants(bound))
            {
                switch (node)
                {
                    case BoundCallStatement { ReceiverSymbol: not null } statement:
                        _boundReceiverTypes[
                            (statement.Span.Start, statement.Span.End, statement.Target)] =
                            statement.ReceiverSymbol.TypeName;
                        break;
                    case BoundCallExpression { ReceiverSymbol: not null } expression:
                        _boundReceiverTypes[
                            (expression.Span.Start, expression.Span.End, expression.Target)] =
                            expression.ReceiverSymbol.TypeName;
                        break;
                }
            }
        }
        catch
        {
            // Raw per-function mode still keeps the unresolved target explicit.
        }
    }

    private static IEnumerable<BoundNode> Descendants(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static string GetNominalTypeName(string typeName)
    {
        var type = typeName.Trim().TrimStart('?');
        var generic = type.IndexOf('<');
        if (generic > 0)
            type = type[..generic];
        var array = type.IndexOf('[');
        if (array > 0)
            type = type[..array];
        return type.TrimEnd('?', '*');
    }
}
