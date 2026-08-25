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
/// vouch for the receiver's type: an <see cref="UnresolvedBoundType"/> (which
/// since slice 2a is how the binder reports both its inference fallback on an
/// inferred local and a member chain such as <c>a.b.M</c>), a function-typed
/// value, a receiver that is neither a bound variable nor written as a
/// type reference (<c>foo.Bar</c>, <c>this.f.M</c>), or a module whose binding
/// threw. In that
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
/// Receiver types come from the bound tree (v0.15 E1, metadata-binding S6).
/// Since slice 2a there is exactly one step: read
/// <see cref="BoundCallExpression.Receiver"/> /
/// <see cref="BoundCallStatement.Receiver"/> and classify its
/// <see cref="BoundType"/>. Nothing here reconstructs a receiver expression or
/// reads a symbol's type string; the binder decided, and this file reports what
/// it decided.
///
/// <para><b>What that did and did not change.</b> Slice 1's comment here said
/// step 1 "provably reduces to <c>ReceiverSymbol.TypeName</c>". That is
/// narrowed, not falsified: the reduction still describes what the bound-variable
/// shape yields today, but it is no longer a property of THIS file — the binder
/// owns the decision, and this collector can no longer reconstruct a different
/// answer than the binder's. Slice 2a resolved no receiver that did not resolve
/// before: <c>calor effects suggest --json</c> over every receiver shape below is
/// byte-identical to the pre-slice output. What changed is that "unresolved" is
/// now a type (<see cref="UnresolvedBoundType"/>) rather than an <c>OBJECT</c>
/// string this file had to second-guess.</para>
///
/// <para>What the four receiver shapes give, and what this collector does with
/// each:</para>
///
///   1. A bound local/parameter/field receiver arrives as a
///      <see cref="BoundVariableExpression"/>. Its <see cref="BoundType"/> is
///      where a type known only through binding or metadata (an inferred
///      <c>§B</c>, a BCL return type resolved by <c>MetadataBinder</c>) reaches
///      the effect system. The binder has already replaced its <c>OBJECT</c>
///      inference fallback with <see cref="UnresolvedBoundType"/>, so the only
///      guard left here is the function-typed one (<c>LAMBDA(...)</c>,
///      <c>Func&lt;…&gt;</c>): invoking a function value is not a nominal
///      receiver, and its row is E2/E4's business.
///   2. A member chain (<c>a.b.M</c>) arrives as a
///      <see cref="BoundFieldAccessExpression"/> typed
///      <see cref="UnresolvedBoundType"/> — reported unresolved, never
///      attributed to the head's type.
///   3. A static type receiver — a Calor-declared type, or one written as a type
///      reference (every dot-separated segment capitalized: <c>Console</c>,
///      <c>System.IO.File</c>, <c>OrderRepo</c>) — arrives as a
///      <see cref="BoundTypeReferenceExpression"/> and takes the binder's name
///      verbatim. This keeps <c>calor effects suggest</c> able to propose
///      manifest entries for external types metadata does not know.
///   4. Anything else (<c>foo.Bar</c>, <c>this.sb.Append</c>, <c>_items.Add</c>
///      with no bound symbol) arrives as a null <c>Receiver</c> and is reported
///      unresolved — the source text is never echoed as a type.
///
/// An unresolved receiver keeps its source text in <see cref="CollectedCall.TypeName"/>
/// for the report and sets <see cref="CollectedCall.ReceiverResolved"/> false; it
/// is never given a guessed type.
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
                            ResolveBoundReceiver(statement.Receiver);
                        break;
                    case BoundCallExpression expression when HasReceiver(expression.Target):
                        _boundReceiverTypes[
                            (expression.Span.Start, expression.Span.End, expression.Target)] =
                            ResolveBoundReceiver(expression.Receiver);
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
    /// v0.15 E1 slice 2a — the receiver's identity is read off the receiver
    /// <see cref="BoundExpression"/> the binder attached to the call node. There
    /// is no reconstruction here and no symbol-string path: a null
    /// <c>Receiver</c> is the binder saying it has nothing to vouch for, and an
    /// <see cref="UnresolvedBoundType"/> is the binder saying it looked and
    /// could not name the type (it reported Calor0270 at that site).
    ///
    /// <para>A <see cref="BoundTypeReferenceExpression"/> receiver takes the
    /// binder's name verbatim — that name is already the resolution decision
    /// (a Calor <c>TypeSymbol.QualifiedName</c>, or the source text with short
    /// BCL names expanded), so re-normalizing it here would second-guess the
    /// binder.</para>
    /// </summary>
    private static BoundReceiver ResolveBoundReceiver(BoundExpression? receiver)
    {
        if (receiver is null)
            return BoundReceiver.Unresolved;

        if (receiver is BoundTypeReferenceExpression)
        {
            return receiver.Type is UnresolvedBoundType
                ? BoundReceiver.Unresolved
                : new BoundReceiver(receiver.Type.FullyQualifiedName);
        }

        return FromBoundType(receiver.Type);
    }

    /// <summary>
    /// True when every dot-separated segment of <paramref name="receiver"/> is a
    /// capitalized identifier — the shape of a namespace/type reference written
    /// in source. Variables, fields, <c>this</c>, and member chains through them
    /// fail this test.
    ///
    /// <para>v0.15 E1 slice 2b — the predicate itself now lives in
    /// <see cref="TypeIdentity"/> so <c>Binding/</c> can use it without
    /// referencing <c>Effects/</c> (PR #1095 review finding 10). This forwarder
    /// keeps the collector's own call site and its tests on the current name.</para>
    /// </summary>
    internal static bool IsTypeQualifiedReference(string receiver) =>
        TypeIdentity.IsTypeQualifiedReference(receiver);

    /// <summary>
    /// Classifies a receiver's <see cref="BoundType"/>.
    ///
    /// <para>The <c>OBJECT</c> sentinel arm slice 1 carried here is gone: the
    /// binder now stamps <see cref="UnresolvedBoundType"/> on the receiver
    /// itself for an inferred local it could not type, so this method no longer
    /// needs the receiver's <see cref="VariableSymbol"/> to tell a genuine
    /// <c>object</c> declaration from the inference fallback. A receiver typed
    /// <c>OBJECT</c> that reaches this method is therefore a real <c>object</c>
    /// and resolves to <c>System.Object</c>.</para>
    ///
    /// <para>The function-type guard stays: invoking a function-typed value is
    /// not a nominal receiver, and effect rows on function types are E2/E4.
    /// Since E1 slice 2b it is answered STRUCTURALLY first — a lambda binds to
    /// <see cref="FunctionBoundType"/> and a Calor <c>§DEL</c> type carries
    /// <c>TypeSymbol.IsDelegate</c>, both tested by
    /// <see cref="EffectEnforcementPass.IsFunctionBoundType"/>. The string test
    /// below survives as the fallback for the shapes where a function type
    /// reaches this method only as text (§2.2's surviving fallbacks): a
    /// declared <c>Func&lt;…&gt;</c>/<c>Action</c> parameter or field, whose
    /// BoundType is a plain <c>NominalBoundType</c> built from the symbol's type
    /// string; and an untyped <c>§B</c> whose TypeName the binder inferred from
    /// a lambda's <c>DisplayString</c> (<c>Binder.cs:1320</c>), which is why the
    /// <c>LAMBDA(</c> prefixes stay in the list.</para>
    /// </summary>
    private static BoundReceiver FromBoundType(BoundType type)
    {
        switch (type)
        {
            case UnresolvedBoundType:
                return BoundReceiver.Unresolved;
            case var _ when EffectEnforcementPass.IsFunctionBoundType(type):
                return BoundReceiver.Unresolved;
            case NominalBoundType nominal when IsFunctionTypeName(nominal.QualifiedName):
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
