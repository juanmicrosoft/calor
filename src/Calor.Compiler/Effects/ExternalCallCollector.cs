using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
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
///
/// <para><see cref="ReceiverResolved"/> is false when the bound tree could not
/// type the receiver (an <see cref="UnresolvedBoundType"/>, the binder's
/// <c>OBJECT</c> fallback for an expression it could not type, a member chain
/// the bound tree does not type, or a function-typed value). In that case
/// <see cref="TypeName"/> is the receiver exactly as written in source — never
/// a guessed type — and consumers must report the call as unresolved rather
/// than key a manifest entry on it. v0.15 E1 (roadmap §4.2), metadata-binding
/// scoping §3 S6 / D5.</para>
/// </summary>
public sealed record CollectedCall(
    string TypeName,
    string MethodName,
    CallKind Kind,
    bool ReceiverResolved = true);

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
///
/// Receiver types come from the bound tree (v0.15 E1, metadata-binding S6):
///
///   1. A receiver that is a bound variable resolves through the
///      <see cref="BoundType"/> a read of that variable carries
///      (<see cref="BoundVariableExpression.Type"/>) — this is where a type
///      known only through binding or metadata (an inferred <c>§B</c>, a BCL
///      return type resolved by <c>MetadataBinder</c>) reaches the effect system.
///   2. A receiver that is a Calor-declared type used statically resolves to
///      <see cref="TypeSymbol.QualifiedName"/>.
///   3. A type-qualified receiver written in source (<c>System.Console</c>,
///      <c>Console</c>) takes the identity the binder assigned it
///      (<see cref="BoundCallExpression.ResolvedTypeName"/>).
///
/// A receiver the binder could not type is reported with
/// <see cref="CollectedCall.ReceiverResolved"/> == false and never receives a
/// guessed type. There is no AST-side variable-type map: when binding is
/// unavailable altogether, only the source-literal receiver text (with short
/// BCL names expanded) is reported.
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
    private readonly Dictionary<(int Start, int End, string Target), BoundReceiver> _boundReceiverTypes = new();

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
        CollectFromStatements(body);
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

        if (defaultKind == CallKind.Constructor)
        {
            // §NEW{Type}: the whole target is the constructed type, dotted
            // (System.Text.StringBuilder) or bare (Random, expanded here).
            var resolvedType = EffectEnforcementPass.MapShortTypeNameToFullName(target);
            _calls.Add(new CollectedCall(resolvedType, ".ctor", CallKind.Constructor));
            return;
        }

        var lastDot = target.LastIndexOf('.');
        if (lastDot <= 0)
        {
            // Bare-name call (a Calor function): no receiver to resolve.
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

        var receiverResolved = true;
        if (_boundReceiverTypes.TryGetValue((span.Start, span.End, target), out var boundReceiver))
        {
            // The bound tree is the authority. An unresolved receiver keeps its
            // source text and is flagged — it is never given a guessed type.
            if (boundReceiver.IsResolved)
                typePart = boundReceiver.TypeName!;
            else
                receiverResolved = false;
        }
        else if (!typePart.Contains('.'))
        {
            // No bound call site (binding unavailable for this module or this
            // node shape): the bare receiver is taken as a type name written in
            // source, with short BCL names expanded. Dotted receivers are kept
            // verbatim.
            typePart = EffectEnforcementPass.MapShortTypeNameToFullName(typePart);
        }

        if (!string.IsNullOrEmpty(typePart) && !string.IsNullOrEmpty(methodName))
        {
            _calls.Add(new CollectedCall(typePart, methodName, kind, receiverResolved));
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
                    case BoundCallStatement statement when HasReceiver(statement.Target):
                        _boundReceiverTypes[
                            (statement.Span.Start, statement.Span.End, statement.Target)] =
                            ResolveBoundReceiver(
                                statement.Span,
                                statement.Target,
                                statement.ReceiverSymbol,
                                statement.ReceiverTypeSymbol,
                                statement.ResolvedTypeName);
                        break;
                    case BoundCallExpression expression when HasReceiver(expression.Target):
                        _boundReceiverTypes[
                            (expression.Span.Start, expression.Span.End, expression.Target)] =
                            ResolveBoundReceiver(
                                expression.Span,
                                expression.Target,
                                expression.ReceiverSymbol,
                                expression.ReceiverTypeSymbol,
                                expression.ResolvedTypeName);
                        break;
                }
            }
        }
        catch
        {
            // Binding is an enrichment here: without it, TryAddCall reports the
            // source-literal receiver text, and raw per-function mode still
            // keeps the unresolved target explicit.
        }
    }

    private static bool HasReceiver(string target) => target.LastIndexOf('.') > 0;

    /// <summary>
    /// Receiver identity for one bound call site. <see cref="TypeName"/> is the
    /// fully-qualified receiver type when the bound tree resolves it and null
    /// when it does not; an unresolved receiver is reported as such, never
    /// guessed.
    /// </summary>
    private readonly record struct BoundReceiver(string? TypeName)
    {
        public static readonly BoundReceiver Unresolved = new((string?)null);
        public bool IsResolved => TypeName is not null;
    }

    /// <summary>
    /// Resolution order (v0.15 E1): the receiver variable's bound type, then a
    /// Calor-declared receiver type, then the binder's identity for a
    /// type-qualified receiver written in source.
    /// </summary>
    private static BoundReceiver ResolveBoundReceiver(
        TextSpan span,
        string target,
        VariableSymbol? receiverSymbol,
        TypeSymbol? receiverTypeSymbol,
        string? resolvedTypeName)
    {
        var receiverPath = target[..target.LastIndexOf('.')];

        if (receiverSymbol != null)
        {
            // The binder resolves the FIRST segment of the target as a variable.
            // For a member chain (a.b.Method) the bound tree types `a` only;
            // `a.b` has no bound type, so the receiver is unresolved rather than
            // attributed to `a`'s type.
            if (receiverPath.Contains('.'))
                return BoundReceiver.Unresolved;

            // The BoundType a read of this variable carries — the same shape a
            // BoundVariableExpression for the receiver would expose.
            return FromBoundType(new BoundVariableExpression(span, receiverSymbol).Type);
        }

        if (receiverTypeSymbol != null)
            return new BoundReceiver(receiverTypeSymbol.QualifiedName);

        // A type-qualified receiver written in source (System.Console, Console):
        // the binder's identity for it, which is the source text with short BCL
        // names expanded — not a variable-type guess.
        return resolvedTypeName is { } sourceQualifiedType
            ? new BoundReceiver(sourceQualifiedType)
            : BoundReceiver.Unresolved;
    }

    private static BoundReceiver FromBoundType(BoundType type)
    {
        switch (type)
        {
            case UnresolvedBoundType:
                return BoundReceiver.Unresolved;
            case FunctionBoundType:
                // Invoking a function-typed value: the callee is not a nominal
                // receiver; effect rows on function types are E2/E4.
                return BoundReceiver.Unresolved;
            case NominalBoundType { QualifiedName: "OBJECT" }:
                // The binder's fallback for an expression it could not type
                // (unknown callee return, disagreeing conditional resolutions).
                // Until the binder emits UnresolvedBoundType (scoping §D6) this
                // sentinel is its unresolved marker; do not read it as
                // System.Object.
                return BoundReceiver.Unresolved;
            case NominalBoundType nominal:
                return FromTypeName(nominal.FullyQualifiedName);
            case GenericInstantiationBoundType generic:
                return FromTypeName(generic.Definition.FullyQualifiedName);
            case ArrayBoundType:
                return new BoundReceiver("System.Array");
            case PrimitiveBoundType primitive:
                return FromTypeName(primitive.Name);
            default:
                return BoundReceiver.Unresolved;
        }
    }

    private static BoundReceiver FromTypeName(string typeName)
    {
        var trimmed = typeName.Trim().TrimStart('?').TrimEnd('?');
        var bracket = trimmed.IndexOf('[');
        if (bracket > 0 && trimmed.EndsWith(']')
            && trimmed[bracket..].All(c => c is '[' or ']' or ','))
        {
            // Array-typed variable (i32[], string[,]): members live on System.Array.
            return new BoundReceiver("System.Array");
        }

        var nominal = GetNominalTypeName(typeName);
        return string.IsNullOrEmpty(nominal)
            ? BoundReceiver.Unresolved
            : new BoundReceiver(EffectEnforcementPass.MapShortTypeNameToFullName(nominal));
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
