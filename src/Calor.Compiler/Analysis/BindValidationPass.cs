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

    // Lexical scope stack of name → declared type for the body currently being
    // walked, so the array-vs-collection check can validate §ASSIGN targets without
    // an inner-block declaration leaking out to shadow an outer variable. The base
    // scope is seeded with the member's parameters and enclosing class fields; each
    // nested block pushes a child scope. These instance fields make the pass
    // single-threaded / non-reentrant, which matches its current usage (one pass
    // per compilation); parallelizing over modules would need per-call state.
    private readonly List<Dictionary<string, string>> _scopes = [];

    // Declared return type of the function-like body currently being walked, so the
    // check can validate §R. Null for bodies with no value return (ctors, setters).
    private string? _currentReturnType;

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
        BuildUserReturnTypes(module);

        foreach (var func in module.Functions)
        {
            CheckBody(func.Body, func.Output?.TypeName, func.Parameters, fields: null);
        }

        foreach (var cls in module.Classes)
        {
            var fields = CollectFieldTypes(cls);

            foreach (var ctor in cls.Constructors)
            {
                CheckBody(ctor.Body, returnType: null, ctor.Parameters, fields);
            }

            foreach (var method in cls.Methods)
            {
                CheckBody(method.Body, method.Output?.TypeName, method.Parameters, fields);
            }

            foreach (var prop in cls.Properties)
            {
                // A getter returns the property's type; setters/initers do not.
                if (prop.Getter != null) CheckBody(prop.Getter.Body, prop.TypeName, parameters: null, fields);
                if (prop.Setter != null) CheckBody(prop.Setter.Body, returnType: null, parameters: null, fields);
                if (prop.Initer != null) CheckBody(prop.Initer.Body, returnType: null, parameters: null, fields);
            }

            foreach (var op in cls.OperatorOverloads)
            {
                CheckBody(op.Body, op.Output?.TypeName, op.Parameters, fields);
            }

            foreach (var idx in cls.Indexers)
            {
                if (idx.Getter != null) CheckBody(idx.Getter.Body, idx.TypeName, parameters: null, fields);
                if (idx.Setter != null) CheckBody(idx.Setter.Body, returnType: null, parameters: null, fields);
                if (idx.Initer != null) CheckBody(idx.Initer.Body, returnType: null, parameters: null, fields);
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
        IReadOnlyDictionary<string, string>? fields)
    {
        _currentReturnType = returnType;
        _scopes.Clear();
        var baseScope = new Dictionary<string, string>(StringComparer.Ordinal);
        if (fields != null)
        {
            foreach (var kv in fields) baseScope[kv.Key] = kv.Value;
        }

        if (parameters != null)
        {
            foreach (var p in parameters) baseScope[p.Name] = p.TypeName; // parameters shadow fields
        }

        _scopes.Add(baseScope);
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

    private bool TryLookupLocal(string name, out string type)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(name, out type!))
            {
                return true;
            }
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

            case ReturnStatementNode ret when ret.Expression != null && _currentReturnType != null:
                CheckArrayToCollection(
                    ret.Expression, _currentReturnType, ret.Span, "The declared return type");
                break;

            case AssignmentStatementNode assign
                when assign.Target is ReferenceNode target &&
                     TryLookupLocal(target.Name, out var targetType):
                CheckArrayToCollection(
                    assign.Value, targetType, assign.Span, $"Variable '{target.Name}'");
                break;

            case IfStatementNode ifStmt:
                CheckBlock(ifStmt.ThenBody);
                foreach (var ei in ifStmt.ElseIfClauses)
                    CheckBlock(ei.Body);
                if (ifStmt.ElseBody != null)
                    CheckBlock(ifStmt.ElseBody);
                break;

            case ForStatementNode forStmt:
                CheckBlock(forStmt.Body);
                break;

            case ForeachStatementNode forEach:
                CheckBlock(forEach.Body);
                break;

            case WhileStatementNode whileStmt:
                CheckBlock(whileStmt.Body);
                break;

            case DoWhileStatementNode doWhileStmt:
                CheckBlock(doWhileStmt.Body);
                break;

            case MatchStatementNode match:
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
                CheckBlock(usingStmt.Body);
                break;

            case SyncBlockNode sync:
                CheckBlock(sync.Body);
                break;

            case UnsafeBlockNode unsafeBlock:
                CheckBlock(unsafeBlock.Body);
                break;

            case FixedStatementNode fixedStmt:
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

        // Track the declared type so later §ASSIGN to this variable can be checked.
        if (bind.TypeName != null)
        {
            DeclareLocal(bind.Name, bind.TypeName);
        }

        // Calor0254 — always-on hard type error: an array bound to a concrete
        // generic collection (the E1a trap). Independent of strict inference,
        // since the emitted C# would fail with CS0029 regardless.
        if (bind.TypeName != null && bind.Initializer != null)
        {
            CheckArrayToCollection(
                bind.Initializer, bind.TypeName, bind.Span, $"Binding '{bind.Name}'");
        }

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
        // judged by its real return type, not the built-in assumption.
        if (_userReturnTypes.TryGetValue(call.Target, out var returnType))
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

    // Note: this resolves only functions/methods declared in the module being
    // checked. Array-returning functions imported from other modules are not
    // seen, so they are conservative false negatives (never a false Calor0254).
    private void BuildUserReturnTypes(ModuleNode module)
    {
        _userReturnTypes.Clear();
        foreach (var func in module.Functions)
        {
            if (func.Output?.TypeName is { } rt)
            {
                _userReturnTypes[func.Name] = rt;
            }
        }

        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                if (method.Output?.TypeName is { } rt)
                {
                    // Keyed by both the bare and the Type.Method spelling so either
                    // call form resolves; last write wins on rare name collisions.
                    _userReturnTypes[method.Name] = rt;
                    _userReturnTypes[$"{cls.Name}.{method.Name}"] = rt;
                }
            }
        }
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
