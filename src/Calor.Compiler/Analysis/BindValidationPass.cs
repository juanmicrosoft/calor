using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Validates <c>§B</c> binding declarations against the rules in
/// <c>docs/syntax-reference/binding.md</c>.
///
/// <para>Always-on checks (the bug-fix baseline):</para>
/// <list type="bullet">
///   <item><c>Calor0250 BindRequiresTypeOrInitializer</c> —
///   a binding must carry either a <c>:type</c> annotation or
///   an initializer expression.</item>
/// </list>
///
/// <para>Strict-mode checks (default-on since v0.6.3; disable with
/// <c>--no-strict-bind-inference</c>):</para>
/// <list type="bullet">
///   <item><c>Calor0251 BindCannotInferNullLiteral</c> —
///   <c>§B{x} none</c> / <c>§B{x} null</c> with no <c>:type</c> annotation.</item>
///   <item><c>Calor0252 BindCannotInferGenericReturn</c> —
///   <c>§B{x} §C{Vec.empty}</c> and similar well-known generic
///   factory calls without a <c>:type</c> annotation.</item>
///   <item><c>Calor0253 BindAmbiguousNumeric</c> —
///   <c>§B{x} (+ INT:0 FLOAT:0.0)</c> mixing integer and floating-point
///   literal operands without a <c>:type</c> annotation.</item>
/// </list>
///
/// <para>This pass walks the parsed AST directly and does not depend on the
/// <c>Binder</c>. The <c>Binder</c> still contains a defensive copy of
/// the <c>Calor0250</c> check (used by <c>VerificationAnalysisPass</c>
/// and unit tests) so that direct binder invocations still surface
/// the diagnostic.</para>
/// </summary>
public sealed class BindValidationPass
{
    private readonly DiagnosticBag _diagnostics;
    private readonly bool _strictInference;
    private readonly string? _source;

    // Return type of each user function/method by name — built per module so the
    // array-vs-collection check (Calor0254) can see when an initializer call
    // returns an array (e.g. §F ... -> [str]). Rebuilt on every Check(module).
    private readonly Dictionary<string, string> _userReturnTypes = new(StringComparer.Ordinal);

    // Parameter types of each user function/method, keyed by "name/arity" (and
    // "Type.Method/arity"), so an array passed to a concrete-collection parameter
    // at a call site can be flagged (#725). Rebuilt on every Check(module).
    private readonly Dictionary<string, List<string>> _userParamTypes = new(StringComparer.Ordinal);

    // Lexical scope stack of name → declared type for the body currently being
    // walked, so the array-vs-collection check can validate §ASSIGN targets without
    // an inner-block declaration leaking out to shadow an outer variable. The base
    // scope is seeded with the member's parameters and enclosing class fields; each
    // nested block pushes a child scope. These instance fields make the pass
    // single-threaded / non-reentrant, which matches its current usage (one pass
    // per compilation); parallelizing over modules would need per-call state.
    private readonly List<Dictionary<string, string>> _scopes = [];

    // Fields of the class whose member is currently being walked (null at module
    // level). Kept separate from _scopes: a local may legally shadow a field, so
    // fields must not count toward the CS0136 shadowing check (Calor0255), but they
    // are still a §ASSIGN type-lookup fallback.
    private IReadOnlyDictionary<string, string>? _fieldTypes;

    // Declared return type of the function-like body currently being walked, so the
    // check can validate §R. Null for bodies with no value return (ctors, setters).
    private string? _currentReturnType;

    // Name of the class whose member is currently being walked (null at module
    // level). Signatures resolve context-sensitively: an unqualified call inside
    // class C prefers C's member (implicit `this`); otherwise the free function.
    // This is what keeps a same-named method and free function from colliding.
    private string? _currentClassName;

    // Concrete generic collection classes an array is NOT implicitly convertible
    // to in C# (unlike the collection interfaces IList<T>/IEnumerable<T>/…, which
    // arrays satisfy). Binding an array to one of these is CS0029.
    private static readonly string[] ConcreteCollectionTypes =
    [
        "List", "HashSet", "SortedSet", "Queue", "Stack",
        "LinkedList", "Collection", "ObservableCollection", "SortedList",
    ];

    /// <summary>
    /// Well-known generic factory calls whose return type cannot be inferred
    /// without an explicit type argument. The value is a placeholder type
    /// template used in LSP quick-fixes (e.g. <c>Vec.empty</c> → <c>Vec&lt;object&gt;</c>).
    /// Matched on the call's target string ending — so <c>Vec.empty</c>,
    /// <c>Vec&lt;T&gt;.empty</c>, and <c>some.module.Vec.empty</c> all match
    /// <c>Vec.empty</c>. The placeholder uses <c>object</c> so the inserted
    /// annotation compiles; users typically replace it with a concrete type.
    /// </summary>
    private static readonly (string Target, string Template)[] GenericFactoryTargets =
    [
        ("Vec.empty",        "Vec<object>"),
        ("Vec.create",       "Vec<object>"),
        ("List.empty",       "List<object>"),
        ("List.create",      "List<object>"),
        ("Array.empty",      "Array<object>"),
        ("Set.empty",        "Set<object>"),
        ("Map.empty",        "Map<object, object>"),
        ("Dictionary.empty", "Dictionary<object, object>"),
        ("Dict.empty",       "Dict<object, object>"),
        ("Queue.empty",      "Queue<object>"),
        ("Stack.empty",      "Stack<object>"),
    ];

    public BindValidationPass(DiagnosticBag diagnostics, bool strictInference = true)
        : this(diagnostics, source: null, strictInference)
    {
    }

    /// <summary>
    /// Creates a new bind-validation pass. When <paramref name="source"/> is
    /// non-null, strict-mode diagnostics (Calor0251-0253) are emitted with
    /// an attached <see cref="SuggestedFix"/> that inserts the recommended
    /// <c>:type</c> annotation at the matching <c>}</c> of the bind's
    /// attribute block. Without source, the same diagnostics fire without
    /// fixes (used by call sites that don't carry source text).
    /// </summary>
    public BindValidationPass(DiagnosticBag diagnostics, string? source, bool strictInference = true)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _strictInference = strictInference;
        _source = source;
    }

    public void Check(ModuleNode module)
    {
        BuildUserSignatures(module);

        foreach (var func in module.Functions)
        {
            CheckBody(func.Body, func.Output?.TypeName, func.Parameters, fields: null, className: null);
        }

        foreach (var cls in module.Classes)
        {
            var fields = CollectFieldTypes(cls);

            foreach (var ctor in cls.Constructors)
            {
                CheckBody(ctor.Body, returnType: null, ctor.Parameters, fields, cls.Name);
            }

            foreach (var method in cls.Methods)
            {
                CheckBody(method.Body, method.Output?.TypeName, method.Parameters, fields, cls.Name);
            }

            foreach (var prop in cls.Properties)
            {
                // A getter returns the property's type; setters/initers do not.
                if (prop.Getter != null) CheckBody(prop.Getter.Body, prop.TypeName, parameters: null, fields, cls.Name);
                if (prop.Setter != null) CheckBody(prop.Setter.Body, returnType: null, parameters: null, fields, cls.Name);
                if (prop.Initer != null) CheckBody(prop.Initer.Body, returnType: null, parameters: null, fields, cls.Name);
            }

            foreach (var op in cls.OperatorOverloads)
            {
                CheckBody(op.Body, op.Output?.TypeName, op.Parameters, fields, cls.Name);
            }

            foreach (var idx in cls.Indexers)
            {
                if (idx.Getter != null) CheckBody(idx.Getter.Body, idx.TypeName, parameters: null, fields, cls.Name);
                if (idx.Setter != null) CheckBody(idx.Setter.Body, returnType: null, parameters: null, fields, cls.Name);
                if (idx.Initer != null) CheckBody(idx.Initer.Body, returnType: null, parameters: null, fields, cls.Name);
            }
        }
    }

    private static Dictionary<string, string> CollectFieldTypes(ClassDefinitionNode cls)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in cls.Fields)
        {
            fields[field.Name] = field.TypeName;
        }

        return fields;
    }

    // Walks one function-like body. Establishes the return-type context (for §R)
    // and a fresh scope stack whose base scope holds the enclosing class fields and
    // this member's parameters (so §ASSIGN to a field or parameter is checked, and a
    // parameter correctly shadows a field of the same name). All reset per body.
    private void CheckBody(
        IReadOnlyList<StatementNode> body,
        string? returnType,
        IReadOnlyList<ParameterNode>? parameters,
        IReadOnlyDictionary<string, string>? fields,
        string? className)
    {
        _currentReturnType = returnType;
        _currentClassName = className;

        // Fields are kept OUT of the scope stack: a local may legally shadow a field
        // in C# (the local wins), so field names must not trip the CS0136 check —
        // but they are still consulted by §ASSIGN type lookup as a fallback.
        _fieldTypes = fields;

        // Scope 0 holds parameters; scope 1 is the method-body scope. Keeping them
        // separate means a body-top-level §B that reuses a parameter name is caught
        // (the parameter is in an enclosing scope), matching C#'s CS0136.
        _scopes.Clear();
        var paramScope = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parameters != null)
        {
            // Empty-string type is benign in the downstream lookups (the §ASSIGN /
            // Calor0254 checks call TryGetConcreteCollectionName, which fails on "");
            // the entry is only load-bearing for the shadowing check, which needs the
            // name, not the type.
            foreach (var p in parameters) paramScope[p.Name] = p.TypeName ?? "";
        }

        _scopes.Add(paramScope);
        _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        foreach (var stmt in body)
        {
            CheckStatement(stmt);
        }
    }

    // Walks a nested block in its own child scope so bindings declared inside it do
    // not leak out to shadow (and mis-type) an outer variable of the same name.
    private void CheckBlock(IReadOnlyList<StatementNode> body)
    {
        _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        foreach (var stmt in body)
        {
            CheckStatement(stmt);
        }

        _scopes.RemoveAt(_scopes.Count - 1);
    }

    private void DeclareLocal(string name, string type) => _scopes[^1][name] = type;

    // Names of §EACH/§EACHKV iteration variables currently in scope. Foreach iteration
    // variables are read-only in C#, so a mutable §B rebinding one is neither a valid
    // reassignment (CS1656) nor a re-declaration (CS0136) — it is rejected (Calor0257,
    // #738). §L for-loop variables are reassignable and are NOT tracked here.
    private readonly HashSet<string> _foreachIterationVars = new(StringComparer.Ordinal);

    // Walks a nested loop body whose scope is pre-seeded with the loop's variables,
    // so an inner §B reusing a loop variable's name is caught (CS0136) — the loop
    // variable lives in a scope enclosing the body, exactly as in C#.
    //
    // <paramref name="readOnlyVars"/> are foreach iteration variables that are NOT
    // assignable in C# — a §EACH item variable (`foreach (T x in …)`) and a §EACHKV
    // key/value (`foreach (var (k, v) in …)`). They are tracked so a rebind of one is
    // rejected (Calor0257). <paramref name="scopeVars"/> are reassignable loop-scoped
    // locals — a §L for-loop variable (`for (int i = …)`) and a §EACH index variable
    // (emitted as `var i = -1; … i++;`); they are seeded into scope for shadowing but
    // NOT read-only, so rebinding them stays a legal `x = …`.
    private void CheckLoopBlock(
        Parsing.TextSpan loopSpan,
        IReadOnlyList<StatementNode> body,
        IReadOnlyList<string?> readOnlyVars,
        IReadOnlyList<string?> scopeVars)
    {
        var loopScope = new Dictionary<string, string>(StringComparer.Ordinal);
        var trackedIterVars = new List<string>();
        foreach (var name in readOnlyVars)
        {
            if (string.IsNullOrEmpty(name)) continue;
            CheckLoopVarShadows(name, loopSpan);
            loopScope[name] = ""; // type irrelevant to shadowing
            if (_foreachIterationVars.Add(name))
            {
                trackedIterVars.Add(name); // only remove the ones this frame added (nested same-name safe)
            }
        }
        foreach (var name in scopeVars)
        {
            if (string.IsNullOrEmpty(name)) continue;
            CheckLoopVarShadows(name, loopSpan);
            loopScope[name] = "";
        }

        _scopes.Add(loopScope);
        CheckBlock(body);
        _scopes.RemoveAt(_scopes.Count - 1);
        foreach (var name in trackedIterVars) _foreachIterationVars.Remove(name);
    }

    // A loop variable (§L for-var, §EACH item/index, §EACHKV key/value) that reuses the
    // name of a local/parameter/loop-variable already in scope is CS0136 in C# — e.g.
    // `int x = 0;` then `for (var x = …)`. Same rule and diagnostic (Calor0255) as an
    // inner §B shadowing declaration; a loop var may still legally shadow a field.
    private void CheckLoopVarShadows(string name, Parsing.TextSpan span)
    {
        if (IsDeclaredInAnyLiveScope(name))
        {
            _diagnostics.ReportError(span, DiagnosticCode.BindShadowsEnclosingScope,
                $"Loop variable '{name}' has the same name as a variable already in scope. " +
                "C# forbids a loop variable from shadowing an enclosing local or parameter " +
                $"(CS0136) — the generated code would not compile. Rename the loop variable (e.g. '{name}2').");
        }
    }

    private static readonly string?[] NoLoopVars = System.Array.Empty<string?>();

    /// <summary>True if <paramref name="name"/> is already a local, parameter, or
    /// loop variable in an <em>enclosing</em> scope (strictly above the current one)
    /// — the CS0136 condition a new §B declaration would violate. Fields are excluded
    /// from the scope stack, so a local may legally shadow a field, as in C#. Same-
    /// scope duplicates (CS0128) are deliberately NOT flagged here (#731): the C#→Calor
    /// converter emits array/list/dict reassignments as same-name creation blocks,
    /// so that case needs the converter's cooperation, not a blanket reject.</summary>
    private bool IsShadowingEnclosingScope(string name)
    {
        for (var i = 0; i < _scopes.Count - 1; i++)
        {
            if (_scopes[i].ContainsKey(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if <paramref name="name"/> is a local declared in any live scope —
    /// the current block or an enclosing one. Mirrors the (scope-aware, #732) emitter's
    /// rule for classifying a mutable §B rebind: it is a reassignment (<c>x = …</c>) only
    /// when the name is still visible, and otherwise a new declaration (<c>var x = …</c>)
    /// — so a rebind in a now-closed sibling block re-declares rather than reassigning an
    /// out-of-scope local (CS0103).</summary>
    private bool IsDeclaredInAnyLiveScope(string name)
    {
        for (var i = 0; i < _scopes.Count; i++)
        {
            if (_scopes[i].ContainsKey(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The expanded type of a literal initializer, or null if the initializer is
    /// null or not a statically-typed literal. Used as the leaf case of
    /// <see cref="TryInferValueType"/>.</summary>
    private static string? LiteralTypeOrNull(ExpressionNode? initializer) => initializer switch
    {
        IntLiteralNode => "INT",
        StringLiteralNode => "STRING",
        BoolLiteralNode => "BOOL",
        FloatLiteralNode or DecimalLiteralNode => "FLOAT",
        _ => null,
    };

    /// <summary>
    /// Infers the type of a rebind value well enough to compare its <em>category</em>
    /// against a declared type (#740). Handles the cases we can be certain about: a
    /// literal, a reference to a typed local/parameter, and a call whose return type is
    /// known (a user function/method, or a curated BCL method). Returns false for
    /// anything else — a conservative miss, never a false positive on the hard-error
    /// <c>Calor0256</c>.
    /// </summary>
    private bool TryInferValueType(ExpressionNode? value, out string type)
    {
        type = "";
        switch (value)
        {
            case null:
                return false;

            case ReferenceNode reference:
                // A reference to another local/parameter carries its declared type. An
                // untyped (inferred) local has "" — not usable, so treat as unknown.
                return TryLookupLocal(reference.Name, out type) && !string.IsNullOrEmpty(type);

            case CallExpressionNode call:
                if (TryResolveReturnType(call.Target, out type) && !string.IsNullOrEmpty(type))
                {
                    return true;
                }
                if (ScalarReturningBcl.Methods.TryGetValue(call.Target, out type!))
                {
                    return true;
                }
                type = "";
                return false;

            default:
                var literal = LiteralTypeOrNull(value);
                if (literal != null)
                {
                    type = literal;
                    return true;
                }
                return false;
        }
    }

    /// <summary>The primitive category of a Calor type name, accepting both the surface
    /// spelling (<c>i32</c>, <c>str</c>, <c>bool</c>) and the expanded internal form
    /// (<c>INT</c>, <c>STRING</c>, <c>BOOL</c>, <c>INT[bits=64]…</c>). These three
    /// categories have no implicit conversions between them in C#, so a cross-category
    /// rebind is always CS0029. Everything else — <c>char</c> (implicitly convertible to
    /// the numeric types), <c>object</c>, user/reference types, arrays, generics —
    /// classifies as <see cref="TypeCategory.Unknown"/> and is never flagged.</summary>
    private enum TypeCategory { Unknown, String, Boolean, Numeric }

    private static TypeCategory ClassifyType(string type)
    {
        var head = type.Trim();
        var bracket = head.IndexOf('[');           // strip expanded attributes: INT[bits=64]…
        if (bracket >= 0) head = head.Substring(0, bracket);

        switch (head)
        {
            case "str": case "string": case "STRING":
                return TypeCategory.String;
            case "bool": case "boolean": case "BOOL":
                return TypeCategory.Boolean;
            case "int": case "uint": case "long": case "ulong":
            case "short": case "ushort": case "byte": case "sbyte":
            case "nint": case "nuint": case "float": case "double": case "decimal":
            case "i8": case "i16": case "i32": case "i64":
            case "u8": case "u16": case "u32": case "u64":
            case "f32": case "f64":
            case "INT": case "UINT": case "FLOAT": case "DECIMAL":
                return TypeCategory.Numeric;
            default:
                return TypeCategory.Unknown;
        }
    }

    /// <summary>True only when <paramref name="declared"/> and <paramref name="value"/>
    /// are both known primitive categories that differ — the CS0029 case with no implicit
    /// conversion. Numeric-to-numeric (e.g. <c>i32</c>→<c>i64</c>) shares a category and is
    /// never flagged, so implicit widening is never a false positive. The trade-off is that
    /// within-<c>Numeric</c> conversions that require an explicit cast are conservative
    /// misses (accepted though Roslyn rejects them with CS0266): an integral narrowing
    /// (e.g. <c>i64</c>→<c>i32</c>) and the <c>decimal</c>↔<c>float</c>/<c>double</c> pair
    /// (which has an explicit conversion but no implicit one, so CS0266 — not CS0029, since
    /// a conversion does exist). All spell as <c>Numeric</c> here; a precise fix for the
    /// decimal pair would split <c>decimal</c> into its own category AND reclassify decimal
    /// literals (currently modeled as <c>FLOAT</c>). The differential suite pins the gap
    /// (see <c>KnownGap_DecimalToFloatRebind_EmitsCS0266</c>).</summary>
    private static bool AreDefinitelyIncompatible(string declared, string value)
    {
        var a = ClassifyType(declared);
        var b = ClassifyType(value);
        return a != TypeCategory.Unknown && b != TypeCategory.Unknown && a != b;
    }

    private bool TryLookupLocal(string name, out string type)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(name, out type!))
            {
                return true;
            }
        }

        if (_fieldTypes != null && _fieldTypes.TryGetValue(name, out type!))
        {
            return true;
        }

        type = "";
        return false;
    }

    private void CheckStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case BindStatementNode bind:
                CheckBind(bind);
                break;

            case ReturnStatementNode ret:
                if (ret.Expression != null)
                {
                    if (_currentReturnType != null)
                        CheckArrayToCollection(
                            ret.Expression, _currentReturnType, ret.Span, "The declared return type");
                    ScanExpressionForCalls(ret.Expression);
                }
                break;

            case AssignmentStatementNode assign:
                if (assign.Target is ReferenceNode target)
                {
                    if (_foreachIterationVars.Contains(target.Name))
                    {
                        // Calor0257 — §ASSIGN to a §EACH/§EACHKV iteration variable. The
                        // emitter emits `x = value` inside the foreach, which is CS1656
                        // (cannot assign to an iteration variable). Same defect as a §B
                        // rebind, spelled with the other assignment form.
                        _diagnostics.ReportError(assign.Span, DiagnosticCode.BindReassignsIterationVariable,
                            $"'{target.Name}' is a foreach iteration variable, which is read-only " +
                            "in C# — assigning to it would not compile (CS1656). Copy the value " +
                            "into a new variable instead.");
                    }
                    else if (TryLookupLocal(target.Name, out var targetType))
                    {
                        CheckArrayToCollection(
                            assign.Value, targetType, assign.Span, $"Variable '{target.Name}'");
                    }
                }
                ScanExpressionForCalls(assign.Value);
                break;

            case CallStatementNode callStmt:
                CheckCallArguments(callStmt.Target, callStmt.Arguments);
                foreach (var a in callStmt.Arguments) ScanExpressionForCalls(a);
                break;

            case PrintStatementNode print:
                ScanExpressionForCalls(print.Expression);
                break;

            case ExpressionStatementNode exprStmt:
                ScanExpressionForCalls(exprStmt.Expression);
                break;

            case IfStatementNode ifStmt:
                ScanExpressionForCalls(ifStmt.Condition);
                CheckBlock(ifStmt.ThenBody);
                foreach (var ei in ifStmt.ElseIfClauses)
                {
                    ScanExpressionForCalls(ei.Condition);
                    CheckBlock(ei.Body);
                }
                if (ifStmt.ElseBody != null)
                    CheckBlock(ifStmt.ElseBody);
                break;

            case ForStatementNode forStmt:
                // §L for-loop variables ARE reassignable, so not read-only iteration vars.
                CheckLoopBlock(forStmt.Span, forStmt.Body, NoLoopVars, new[] { forStmt.VariableName });
                break;

            case ForeachStatementNode forEach:
                ScanExpressionForCalls(forEach.Collection);
                // The item variable is a read-only foreach variable; the optional index
                // variable is emitted as a plain reassignable local (`var i = -1; … i++`).
                CheckLoopBlock(
                    forEach.Span,
                    forEach.Body,
                    new[] { forEach.VariableName },
                    new[] { forEach.IndexVariableName });
                break;

            case DictionaryForeachNode dictForEach:
                ScanExpressionForCalls(dictForEach.Dictionary);
                // §EACHKV emits `foreach (var (k, v) in …)`: both are read-only.
                CheckLoopBlock(
                    dictForEach.Span,
                    dictForEach.Body,
                    new[] { dictForEach.KeyName, dictForEach.ValueName },
                    NoLoopVars);
                break;

            case WhileStatementNode whileStmt:
                ScanExpressionForCalls(whileStmt.Condition);
                CheckBlock(whileStmt.Body);
                break;

            case DoWhileStatementNode doWhileStmt:
                ScanExpressionForCalls(doWhileStmt.Condition);
                CheckBlock(doWhileStmt.Body);
                break;

            case MatchStatementNode match:
                ScanExpressionForCalls(match.Target);
                foreach (var c in match.Cases)
                    CheckBlock(c.Body);
                break;

            case TryStatementNode tryStmt:
                CheckBlock(tryStmt.TryBody);
                foreach (var clause in tryStmt.CatchClauses)
                    CheckBlock(clause.Body);
                if (tryStmt.FinallyBody != null)
                    CheckBlock(tryStmt.FinallyBody);
                break;

            case UsingStatementNode usingStmt:
                ScanExpressionForCalls(usingStmt.Resource);
                CheckBlock(usingStmt.Body);
                break;

            case SyncBlockNode sync:
                ScanExpressionForCalls(sync.LockExpression);
                CheckBlock(sync.Body);
                break;

            case UnsafeBlockNode unsafeBlock:
                CheckBlock(unsafeBlock.Body);
                break;

            case FixedStatementNode fixedStmt:
                ScanExpressionForCalls(fixedStmt.Initializer);
                CheckBlock(fixedStmt.Body);
                break;
        }
    }

    private void CheckBind(BindStatementNode bind)
    {
        // Calor0250 — always-on baseline.
        if (bind.TypeName == null && bind.Initializer == null)
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindRequiresTypeOrInitializer,
                $"Binding '{bind.Name}' has no type annotation and no initializer. " +
                "Add either ':type' (e.g. '§B{" + bind.Name + ":i32}') " +
                "or an initializer expression so the binder can infer the type.");
            return;
        }

        // A mutable §B whose name is visible in a live scope is a reassignment (the
        // scope-aware emitter emits `x = …`), not a new local — so it neither shadows
        // nor gets a fresh scope entry. A mutable rebind whose earlier declaration lives
        // in a now-closed sibling block is NOT visible, so it is a new declaration, just
        // like the emitter now emits (#732). Everything else is a new declaration too.
        var isReassignment = bind.IsMutable && IsDeclaredInAnyLiveScope(bind.Name);
        if (isReassignment && _foreachIterationVars.Contains(bind.Name))
        {
            // Calor0257 — the reassignment target is a §EACH/§EACHKV iteration variable,
            // which is read-only in C#. There is no valid emission (CS1656 as a
            // reassignment, CS0136 as a re-declaration), so reject it outright — this
            // supersedes the type-mismatch check below.
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindReassignsIterationVariable,
                $"Binding '{bind.Name}' rebinds a foreach iteration variable, which is read-only " +
                "in C# — the generated code would not compile (CS1656). Rename this binding " +
                $"(e.g. '{bind.Name}2'), or copy the value into a new variable.");
        }
        else if (isReassignment)
        {
            // Calor0256 — a mutable rebind whose value is of a type category that is not
            // implicitly convertible to the variable's declaration. The variable's type is
            // fixed at first declaration; the emitter emits `x = value` against that type,
            // so a cross-category value (e.g. a string into an int) fails to compile
            // (CS0029). The rebind's type is the explicit annotation when present, otherwise
            // inferred from the value — a literal, a reference to a typed local, or a call
            // with a known return type (#740, superseding the earlier literal-only lane).
            // Comparison is by category (string / bool / numeric), so implicit numeric
            // conversions (i32→i64, i32→f64) are never false-positived and an unknown/
            // reference type is a conservative miss.
            var rebindType = bind.TypeName != null
                ? Parsing.AttributeHelper.ExpandType(bind.TypeName.Trim())
                : (TryInferValueType(bind.Initializer, out var inferred) ? inferred : null);

            // TryLookupLocal's field fallback is unreachable here: this branch runs only
            // when isReassignment, which already requires the name in a live LOCAL scope,
            // so declaredType is the variable's local/parameter type, never a field's.
            if (rebindType != null &&
                TryLookupLocal(bind.Name, out var declaredType) &&
                !string.IsNullOrEmpty(declaredType) &&
                AreDefinitelyIncompatible(declaredType.Trim(), rebindType))
            {
                // Surface-spell both sides so the message never teaches an internal
                // spelling the user can't write (i32/str, not INT/STRING).
                var declaredSurface = Parsing.AttributeHelper.ToSurfaceSpelling(declaredType.Trim());
                var rebindSurface = Parsing.AttributeHelper.ToSurfaceSpelling(rebindType);
                _diagnostics.ReportError(bind.Span, DiagnosticCode.BindRebindTypeMismatch,
                    $"Mutable binding '{bind.Name}' was declared '{declaredSurface}' but is rebound " +
                    $"as '{rebindSurface}'. A mutable rebind is a reassignment; its type is fixed at the " +
                    "first declaration, and there is no implicit conversion between these types, so the " +
                    "emitted C# would fail to compile (CS0029). " +
                    "Keep the original type, or use a differently-named binding.");
            }
        }
        else if (_scopes[^1].ContainsKey(bind.Name))
        {
            // Calor0258 — a second §B reusing a name ALREADY declared in the SAME scope
            // (the innermost). The emitter emits `int x = …; int x = …;` → CS0128. This is
            // distinct from Calor0255 (shadowing an enclosing scope, CS0136). It is safe to
            // reject now that the C#→Calor converter emits `arr = new T[]{…}` reassignments
            // as §ASSIGN rather than a fresh same-name §B creation block (#731).
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindDuplicateInScope,
                $"Binding '{bind.Name}' is already declared in this scope. C# forbids two " +
                "locals of the same name in one scope (CS0128), so the generated code would " +
                $"not compile — rename this binding (e.g. '{bind.Name}2'), or use a mutable " +
                $"rebind ('§B{{~{bind.Name}}}') if a reassignment was intended.");
            // Keep the existing declaration; don't overwrite its type with the duplicate's.
        }
        else
        {
            // Calor0255 — a new local that reuses a local/parameter/loop-variable
            // name in an enclosing scope is CS0136 in the emitted C#. Fields are
            // excluded from the scope stack, so shadowing a field is allowed (as C#).
            if (IsShadowingEnclosingScope(bind.Name))
            {
                _diagnostics.ReportError(bind.Span, DiagnosticCode.BindShadowsEnclosingScope,
                    $"Binding '{bind.Name}' shadows a local, parameter, or loop variable of the same " +
                    "name already in an enclosing scope. C# forbids this (CS0136), so the generated " +
                    $"code would not compile — rename this binding (e.g. '{bind.Name}2').");
            }

            // Track the name (with its declared type, or empty when untyped) so later
            // §ASSIGN can be type-checked and nested §B can detect shadowing of it.
            DeclareLocal(bind.Name, bind.TypeName ?? "");
        }

        // Calor0254 — always-on hard type error: an array bound to a concrete
        // generic collection (the E1a trap). Independent of strict inference,
        // since the emitted C# would fail with CS0029 regardless.
        if (bind.TypeName != null && bind.Initializer != null)
        {
            CheckArrayToCollection(
                bind.Initializer, bind.TypeName, bind.Span, $"Binding '{bind.Name}'");
        }

        // Argument position (#725): calls inside the initializer (e.g. an array
        // passed to a List<T> parameter of a user function) are checked too.
        ScanExpressionForCalls(bind.Initializer);

        // Strict-mode checks (Calor0251-0253) — only when --strict-bind-inference is set
        // and the binding has no explicit type annotation. An explicit :type always wins.
        if (!_strictInference || bind.TypeName != null || bind.Initializer == null)
        {
            return;
        }

        CheckStrictInitializer(bind, bind.Initializer);
    }

    /// <summary>
    /// Reports Calor0254 when <paramref name="value"/> is an array and
    /// <paramref name="declaredType"/> is a concrete generic collection. Shared by
    /// binding, return, and assignment positions; <paramref name="subject"/> names
    /// the offending declaration (e.g. "Binding 'lines'"). The message references
    /// the collection by name only — it never echoes the internal normalized type
    /// spelling, so the surface-syntax recommendation ('[str]') is not paired with
    /// a mismatched convention.
    /// </summary>
    private void CheckArrayToCollection(
        ExpressionNode value, string declaredType, Parsing.TextSpan span, string subject)
    {
        if (!TryGetConcreteCollectionName(declaredType.Trim(), out var collectionName))
        {
            return;
        }

        var elementType = InitializerArrayElement(value);
        if (elementType == null)
        {
            return;
        }

        _diagnostics.ReportError(span, DiagnosticCode.BindArrayToConcreteCollection,
            $"{subject} is a concrete collection ('{collectionName}<…>'), but the value is an array. " +
            $"An array is not implicitly convertible to '{collectionName}<…>' in C# (CS0029). Use the " +
            $"array form '[{elementType}]', or construct a new {collectionName} from the array.");
    }

    /// <summary>
    /// Recursively finds every call in <paramref name="expr"/> and checks each
    /// argument against the callee's declared parameter types (#725 — argument
    /// position). Only user functions/methods are resolved (BCL callees have no
    /// parameter-type registry, a conservative false negative). The walker covers
    /// the common composite expressions; unhandled node types simply stop the
    /// descent (a safe false negative, never a false positive).
    /// </summary>
    private void ScanExpressionForCalls(ExpressionNode? expr)
    {
        switch (expr)
        {
            case CallExpressionNode call:
                CheckCallArguments(call.Target, call.Arguments);
                foreach (var arg in call.Arguments) ScanExpressionForCalls(arg);
                break;
            case BinaryOperationNode bin:
                ScanExpressionForCalls(bin.Left);
                ScanExpressionForCalls(bin.Right);
                break;
            case UnaryOperationNode un:
                ScanExpressionForCalls(un.Operand);
                break;
            case ConditionalExpressionNode cond:
                ScanExpressionForCalls(cond.Condition);
                ScanExpressionForCalls(cond.WhenTrue);
                ScanExpressionForCalls(cond.WhenFalse);
                break;
            case TypeOperationNode typeOp:
                ScanExpressionForCalls(typeOp.Operand);
                break;
        }
    }

    /// <summary>
    /// Flags an array argument passed to a concrete-collection parameter, matched
    /// positionally against the resolved callee. Resolution is context-sensitive
    /// (current class before module level) and keyed by arity so Calor's
    /// arity-based overloads pick the right signature.
    /// </summary>
    private void CheckCallArguments(string target, IReadOnlyList<ExpressionNode> args)
    {
        if (!TryResolveParamTypes(target, args.Count, out var paramTypes))
        {
            return;
        }

        for (var i = 0; i < args.Count && i < paramTypes.Count; i++)
        {
            CheckArrayToCollection(
                args[i], paramTypes[i], args[i].Span, $"Parameter {i + 1} of '{target}'");
        }
    }

    // Resolves an unqualified call target the way Calor does: a call inside class C
    // first tries C's member (implicit `this`), then falls back to a module-level
    // free function (or an already-qualified target). Storing methods only under
    // "Class.Member" and free functions under the bare name — and picking by
    // context here — is what prevents a same-named method and free function from
    // clobbering each other (PR #728 review finding 1). When neither is present the
    // callee is unknown (BCL / cross-module): a conservative false negative.
    private bool TryResolveReturnType(string target, out string returnType)
    {
        if (_currentClassName != null &&
            _userReturnTypes.TryGetValue($"{_currentClassName}.{target}", out returnType!))
        {
            return true;
        }

        return _userReturnTypes.TryGetValue(target, out returnType!);
    }

    private bool TryResolveParamTypes(string target, int arity, out List<string> paramTypes)
    {
        if (_currentClassName != null &&
            _userParamTypes.TryGetValue($"{_currentClassName}.{target}/{arity}", out paramTypes!))
        {
            return true;
        }

        return _userParamTypes.TryGetValue($"{target}/{arity}", out paramTypes!);
    }

    /// <summary>
    /// Returns the Calor element type if <paramref name="init"/> is known to be an
    /// array — a call to a known array-returning BCL method, or a call to a user
    /// function whose declared return type is an array (<c>[T]</c>). Null otherwise.
    /// </summary>
    private string? InitializerArrayElement(ExpressionNode init)
    {
        if (init is not CallExpressionNode call)
        {
            return null;
        }

        // User declarations win over the BCL heuristic, so a user type/method that
        // shadows a BCL name (e.g. a local `File.ReadAllLines -> List<str>`) is
        // judged by its real return type, not the built-in assumption. Resolution
        // is context-sensitive (current class before module level).
        if (TryResolveReturnType(call.Target, out var returnType))
        {
            return IsArrayTypeName(returnType)
                ? returnType.Trim().TrimStart('[').TrimEnd(']').Trim()
                : null;
        }

        if (ArrayReturningBcl.Methods.TryGetValue(call.Target, out var bclElement))
        {
            return bclElement;
        }

        return null;
    }

    // Resolves only functions/methods declared in the module being checked; cross-
    // module callees are conservative false negatives. Free functions are stored
    // under the bare name; methods ONLY under "Class.Member" — never the bare name —
    // so a method cannot clobber a same-named free function's signature (PR #728
    // review finding 1). Unqualified calls are matched context-sensitively at the
    // call site (see TryResolveReturnType / TryResolveParamTypes). Constructors,
    // operators, and indexers are intentionally not registered (§NEW / operator /
    // indexer call forms are unchecked — a documented conservative false negative).
    private void BuildUserSignatures(ModuleNode module)
    {
        _userReturnTypes.Clear();
        _userParamTypes.Clear();

        foreach (var func in module.Functions)
        {
            if (func.Output?.TypeName is { } rt)
            {
                _userReturnTypes[func.Name] = rt;
            }

            RegisterParams(func.Name, func.Parameters);
        }

        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var key = $"{cls.Name}.{method.Name}";
                if (method.Output?.TypeName is { } rt)
                {
                    _userReturnTypes[key] = rt;
                }

                RegisterParams(key, method.Parameters);
            }
        }
    }

    private void RegisterParams(string name, IReadOnlyList<ParameterNode> parameters)
    {
        // ParameterNode.TypeName is non-null (parser invariant); the ?? guard keeps
        // a future nullable-annotation change from feeding null into the check.
        _userParamTypes[$"{name}/{parameters.Count}"] =
            parameters.Select(p => p.TypeName ?? "").ToList();
    }

    private static bool IsArrayTypeName(string? typeName)
        => typeName != null && typeName.TrimStart().StartsWith("[", StringComparison.Ordinal);

    private static bool TryGetConcreteCollectionName(string declared, out string collectionName)
    {
        var open = declared.IndexOf('<');
        if (open > 0)
        {
            var head = declared[..open].Trim();
            var lastDot = head.LastIndexOf('.');
            var simple = lastDot >= 0 ? head[(lastDot + 1)..] : head;
            if (Array.Exists(ConcreteCollectionTypes, t => t == simple))
            {
                collectionName = simple;
                return true;
            }
        }

        collectionName = "";
        return false;
    }

    private void CheckStrictInitializer(BindStatementNode bind, ExpressionNode init)
    {
        // Calor0251 — bare none/null cannot infer a type.
        if (init is NoneExpressionNode none && none.TypeName == null)
        {
            var msg = $"Binding '{bind.Name}' uses 'none' without a type. " +
                "Inference cannot pick a concrete element type. " +
                "Add ':Option<T>' (e.g. '§B{" + bind.Name + ":Option<i32>} none') " +
                "or use a typed §NN{type=...} form.";
            ReportStrictWithMaybeFix(bind, DiagnosticCode.BindCannotInferNullLiteral, msg,
                "Annotate binding with ':Option<object>'", ":Option<object>");
            return;
        }

        if (init is ReferenceNode refNode && refNode.Name == "null")
        {
            var msg = $"Binding '{bind.Name}' is initialised to 'null' with no declared type. " +
                "Inference cannot pick a concrete type. " +
                "Add ':T?' or use an Option type (e.g. '§B{" + bind.Name + ":Option<T>} none').";
            ReportStrictWithMaybeFix(bind, DiagnosticCode.BindCannotInferNullLiteral, msg,
                "Annotate binding with ':object?'", ":object?");
            return;
        }

        // Calor0252 — well-known generic factory call without explicit type.
        if (init is CallExpressionNode call && TryGetGenericFactoryTemplate(call.Target, out var template))
        {
            var msg = $"Binding '{bind.Name}' is initialised from '{call.Target}' whose return type " +
                "is generic and has no resolved type argument. " +
                "Add an explicit type annotation, e.g. '§B{" + bind.Name + ":Vec<i32>} §C{Vec<i32>.empty} §/C'.";
            ReportStrictWithMaybeFix(bind, DiagnosticCode.BindCannotInferGenericReturn, msg,
                $"Annotate binding with ':{template}'", $":{template}");
            return;
        }

        // Calor0253 — binary op mixing integer and float literal operands.
        if (init is BinaryOperationNode bin && IsAmbiguousNumeric(bin))
        {
            var msg = $"Binding '{bind.Name}' is initialised from a numeric expression that mixes " +
                "integer and floating-point literals; widening could pick more than one type. " +
                "Add an explicit numeric annotation, e.g. '§B{" + bind.Name + ":f64} ...' " +
                "or '§B{" + bind.Name + ":i32} ...'.";
            ReportStrictWithMaybeFix(bind, DiagnosticCode.BindAmbiguousNumeric, msg,
                "Annotate binding with ':f64' (widening default)", ":f64");
            return;
        }
    }

    /// <summary>
    /// Emits a strict-bind-inference diagnostic. If <see cref="_source"/> is
    /// available and the bind's attribute block is in canonical
    /// <c>§B{name}</c> / <c>§B{~name}</c> form, attaches a SuggestedFix that
    /// inserts <paramref name="typeAnnotation"/> immediately before the
    /// closing <c>}</c>. Otherwise emits the diagnostic without a fix.
    /// </summary>
    private void ReportStrictWithMaybeFix(
        BindStatementNode bind, string code, string message,
        string fixDescription, string typeAnnotation)
    {
        var fix = TryBuildBindTypeAnnotationFix(bind, fixDescription, typeAnnotation);
        if (fix != null)
        {
            _diagnostics.ReportErrorWithFix(bind.Span, code, message, fix);
        }
        else
        {
            _diagnostics.ReportError(bind.Span, code, message);
        }
    }

    /// <summary>
    /// Builds a SuggestedFix that inserts a <c>:type</c> annotation right
    /// before the closing <c>}</c> of a canonical bind attribute block.
    /// Returns null when source is unavailable, the bind isn't in the
    /// expected form, or the close brace can't be located safely.
    /// </summary>
    private SuggestedFix? TryBuildBindTypeAnnotationFix(
        BindStatementNode bind, string description, string typeAnnotation)
    {
        if (_source == null)
        {
            return null;
        }

        var start = bind.Span.Start;
        if (start < 0 || start >= _source.Length)
        {
            return null;
        }

        // Locate the '{' that opens the bind attribute block. Must come right
        // after '§B' and on the same line as bind.Span.
        var openBrace = _source.IndexOf('{', start);
        if (openBrace < 0)
        {
            return null;
        }

        // Attribute blocks must not span lines per the lexer grammar.
        var newlineBefore = _source.IndexOf('\n', start, openBrace - start);
        if (newlineBefore >= 0)
        {
            return null;
        }

        // Find the matching '}' (attribute blocks don't nest).
        var closeBrace = _source.IndexOf('}', openBrace + 1);
        if (closeBrace < 0)
        {
            return null;
        }

        // Reject if the attribute block spans a newline.
        var newlineInside = _source.IndexOf('\n', openBrace + 1, closeBrace - openBrace - 1);
        if (newlineInside >= 0)
        {
            return null;
        }

        // Only fire on canonical shapes: optional '~' followed by identifier
        // characters and no embedded ':' (no existing annotation) — protects
        // against unusual attribute forms that strict-mode might still reach.
        var inside = _source.Substring(openBrace + 1, closeBrace - openBrace - 1);
        if (!IsCanonicalBindAttribute(inside))
        {
            return null;
        }

        // Column math: bind.Span.Column is 1-indexed for the '§' character;
        // closeBrace is char-offset of the '}' (UTF-16). Distance from start
        // to closeBrace is the column delta on the same line.
        var closeBraceColumn = bind.Span.Column + (closeBrace - start);

        var filePath = _diagnostics.CurrentFilePath ?? "";
        var edit = TextEdit.Insert(filePath, bind.Span.Line, closeBraceColumn, typeAnnotation);
        return new SuggestedFix(description, edit);
    }

    private static bool IsCanonicalBindAttribute(string inside)
    {
        if (string.IsNullOrEmpty(inside))
        {
            return false;
        }
        var i = 0;
        if (inside[0] == '~')
        {
            i = 1;
        }
        if (i >= inside.Length)
        {
            return false;
        }
        for (; i < inside.Length; i++)
        {
            var c = inside[i];
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryGetGenericFactoryTemplate(string target, out string template)
    {
        template = "";
        if (string.IsNullOrEmpty(target))
        {
            return false;
        }
        // Match either an exact target, or any target whose tail is a known
        // factory (so e.g. "my.module.Vec.empty" still matches "Vec.empty").
        foreach (var (known, tmpl) in GenericFactoryTargets)
        {
            if (target == known || target.EndsWith("." + known, StringComparison.Ordinal))
            {
                template = tmpl;
                return true;
            }
        }
        return false;
    }

    private static bool IsGenericFactoryTarget(string target)
        => TryGetGenericFactoryTemplate(target, out _);

    private static bool IsAmbiguousNumeric(BinaryOperationNode bin)
    {
        return IsIntegerLiteral(bin.Left) && IsFloatLiteral(bin.Right)
            || IsFloatLiteral(bin.Left) && IsIntegerLiteral(bin.Right);
    }

    private static bool IsIntegerLiteral(ExpressionNode e) => e is IntLiteralNode;

    private static bool IsFloatLiteral(ExpressionNode e) => e is FloatLiteralNode or DecimalLiteralNode;
}
