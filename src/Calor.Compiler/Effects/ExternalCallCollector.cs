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
/// <para><see cref="ReceiverResolved"/> is false when the binder could not
/// vouch for the receiver's type: an <see cref="UnresolvedBoundType"/>, the
/// binder's <c>OBJECT</c> fallback on an inferred local, a function-typed
/// value, a member chain (<c>a.b.M</c>, <c>this.f.M</c>) the bound tree does
/// not type, a receiver that is neither a bound variable nor written as a
/// type reference (<c>foo.Bar</c>), or a module whose binding threw. In that
/// case <see cref="TypeName"/> is the receiver exactly as written in source —
/// never a guessed type — and consumers must report the call as unresolved
/// rather than key a manifest entry on it. v0.15 E1 (roadmap §4.2),
/// metadata-binding scoping §3 S6 / D5.</para>
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
/// Receiver types come from the bound tree (v0.15 E1 slice 1, metadata-binding
/// S6). What that means today, stated plainly:
///
///   1. A receiver that is a bound variable resolves from the binder's symbol
///      type — the <see cref="BoundType"/> a <see cref="BoundVariableExpression"/>
///      for that variable carries, which in this slice is always a
///      <see cref="NominalBoundType"/> wrapping <see cref="VariableSymbol.TypeName"/>.
///      That string is where a type known only through binding or metadata (an
///      inferred <c>§B</c>, a BCL return type resolved by <c>MetadataBinder</c>)
///      reaches the effect system. On top of it this slice adds three honesty
///      guards: the binder's <c>OBJECT</c> fallback on an inferred local, a
///      member chain through the variable (<c>a.b.M</c>), and a function-typed
///      value (<c>LAMBDA(...)</c>, <c>Func&lt;…&gt;</c>) are all reported
///      unresolved instead of being attributed a type. A receiver
///      <c>BoundExpression</c> on the call nodes, and <c>UnresolvedBoundType</c>
///      emitted by the binder (scoping §D6), are slice 2.
///   2. A receiver that is a Calor-declared type used statically resolves to
///      <see cref="TypeSymbol.QualifiedName"/>.
///   3. A receiver that is neither, but is written as a type reference (every
///      dot-separated segment a capitalized identifier: <c>Console</c>,
///      <c>System.IO.File</c>, <c>OrderRepo</c>) takes the identity the binder
///      assigned it (<see cref="BoundCallExpression.ResolvedTypeName"/>: the
///      source text with short BCL names expanded). This keeps
///      <c>calor effects suggest</c> able to propose manifest entries for
///      external types metadata does not know. Anything else (<c>foo.Bar</c>,
///      <c>this.sb.Append</c>, <c>_items.Add</c> with no bound symbol) is
///      reported unresolved — the source text is never echoed as a type.
///
/// If binding the module throws, every dotted receiver is reported unresolved.
/// There is no AST-side variable-type map.
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

    // Set when Binder.Bind threw for the module: no receiver can then be vouched
    // for, so every dotted call is reported unresolved rather than echoed.
    private bool _indexingFailed;

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
        else if (_indexingFailed)
        {
            // Binding threw for this module: nothing can be vouched for.
            receiverResolved = false;
        }
        else if (IsTypeQualifiedReference(typePart))
        {
            // The binder produced no call node for this shape (e.g. a call
            // nested in an expression it wraps as unsupported), but the
            // receiver is written as a type reference; expand short BCL names.
            typePart = EffectEnforcementPass.MapShortTypeNameToFullName(typePart);
        }
        else
        {
            receiverResolved = false;
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
            // Without a bound tree no receiver can be vouched for: TryAddCall
            // reports every dotted receiver as unresolved (source text kept for
            // the report), and raw per-function mode still keeps the target
            // explicit.
            _indexingFailed = true;
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
    /// Resolution order (v0.15 E1 slice 1): the receiver variable's symbol type
    /// (with the OBJECT / chain / function-type guards), then a Calor-declared
    /// receiver type, then the binder's identity for a receiver written as a
    /// type reference. Everything else is unresolved.
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

            // The BoundType a read of this variable carries. In this slice that
            // is NominalBoundType(receiverSymbol.TypeName) — the binder's symbol
            // type string — because call nodes have no receiver BoundExpression
            // yet; see FromBoundType for what is live and what is forward-looking.
            return FromBoundType(new BoundVariableExpression(span, receiverSymbol).Type, receiverSymbol);
        }

        if (receiverTypeSymbol != null)
            return new BoundReceiver(receiverTypeSymbol.QualifiedName);

        // Not a bound variable and not a Calor type. The binder's
        // ResolvedTypeName for this shape is the source text (short BCL names
        // expanded), so it only counts when the receiver is WRITTEN as a type
        // reference — Console, System.IO.File, OrderRepo. A lowercase head
        // (foo.Bar, this.sb.Append, _items.Add) is not vouched for.
        return IsTypeQualifiedReference(receiverPath) && resolvedTypeName is { } sourceQualifiedType
            ? new BoundReceiver(sourceQualifiedType)
            : BoundReceiver.Unresolved;
    }

    /// <summary>
    /// True when every dot-separated segment of <paramref name="receiver"/> is a
    /// capitalized identifier — the shape of a namespace/type reference written
    /// in source. Variables, fields, <c>this</c>, and member chains through them
    /// fail this test.
    /// </summary>
    internal static bool IsTypeQualifiedReference(string receiver)
    {
        if (string.IsNullOrEmpty(receiver))
            return false;
        foreach (var segment in receiver.Split('.'))
        {
            if (segment.Length == 0 || !char.IsUpper(segment[0]))
                return false;
            for (var i = 1; i < segment.Length; i++)
            {
                if (!(char.IsLetterOrDigit(segment[i]) || segment[i] == '_' || segment[i] == '`'))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Classifies a receiver's <see cref="BoundType"/>. In slice 1 the only
    /// shape that reaches here is <see cref="NominalBoundType"/> (a
    /// <see cref="BoundVariableExpression"/> wraps the symbol's TypeName), so
    /// the nominal arms are the live path. The other arms are kept
    /// deliberately, as forward-compatible classification for slice 2, when
    /// call nodes carry a receiver <c>BoundExpression</c> and the binder emits
    /// <see cref="UnresolvedBoundType"/> / <see cref="FunctionBoundType"/>;
    /// no test exercises them today.
    /// </summary>
    private static BoundReceiver FromBoundType(BoundType type, VariableSymbol receiverSymbol)
    {
        switch (type)
        {
            case UnresolvedBoundType:
                // Slice 2: the binder does not emit this yet.
                return BoundReceiver.Unresolved;
            case FunctionBoundType:
                // Slice 2: lambdas are still NominalBoundType("LAMBDA(...)");
                // see the nominal arm's function-type guard.
                return BoundReceiver.Unresolved;
            case NominalBoundType nominal when IsFunctionTypeName(nominal.QualifiedName):
                // Invoking a function-typed value: the callee is not a nominal
                // receiver; effect rows on function types are E2/E4.
                return BoundReceiver.Unresolved;
            case NominalBoundType { QualifiedName: "OBJECT" }
                when !(receiverSymbol.IsParameter || receiverSymbol.IsField || receiverSymbol.IsProperty):
                // The binder's fallback for an inferred local it could not type
                // (unknown callee return, disagreeing conditional resolutions).
                // Until the binder emits UnresolvedBoundType (scoping §D6) this
                // sentinel is its unresolved marker; do not read it as
                // System.Object. Parameters, fields and properties always carry
                // a declared type, so their OBJECT is honoured below. A local
                // explicitly declared `§B{o:OBJECT}` (uppercase; the surface
                // `object` keyword stays lowercase and is unaffected) is
                // conflated with the sentinel — documented, not distinguished.
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

    /// <summary>
    /// Mirrors <c>EffectEnforcementPass.IsFunctionTypeName</c> (private to the
    /// pass) plus the binder's lambda spellings.
    /// </summary>
    private static bool IsFunctionTypeName(string typeName)
    {
        var t = typeName.Trim().TrimEnd('?');
        return t.StartsWith("LAMBDA(", StringComparison.Ordinal)
            || t.StartsWith("ASYNC_LAMBDA(", StringComparison.Ordinal)
            || t.Equals("Action", StringComparison.Ordinal)
            || t.StartsWith("Action<", StringComparison.Ordinal)
            || t.StartsWith("Func<", StringComparison.Ordinal)
            || t.StartsWith("Predicate<", StringComparison.Ordinal)
            || t.StartsWith("Comparison<", StringComparison.Ordinal)
            || t.StartsWith("Converter<", StringComparison.Ordinal)
            || t.Equals("Delegate", StringComparison.Ordinal)
            || t.Equals("MulticastDelegate", StringComparison.Ordinal)
            || t.Equals("EventHandler", StringComparison.Ordinal)
            || t.StartsWith("EventHandler<", StringComparison.Ordinal);
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
        if (string.IsNullOrEmpty(nominal))
            return BoundReceiver.Unresolved;
        var mapped = EffectEnforcementPass.MapShortTypeNameToFullName(nominal);
        return new BoundReceiver(mapped is "OBJECT" or "object" ? "System.Object" : mapped);
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
