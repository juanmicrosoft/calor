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

    // Well-known array-returning BCL methods, keyed by the call target as it
    // appears in Calor (§C{Type.Method}), mapped to the Calor element type. The
    // binder has no general BCL return-type model, so this small table is what
    // lets Calor0254 fire for the common file/dir readers. To extend: add another
    // array-returning method and its element type. Keep in step with
    // SelfCheck/ExemplarCompileChecker's array-trap list (the docs-level guard).
    private static readonly IReadOnlyDictionary<string, string> ArrayReturningBclMethods =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["File.ReadAllLines"] = "str",
            ["File.ReadAllBytes"] = "u8",
            ["Directory.GetFiles"] = "str",
            ["Directory.GetDirectories"] = "str",
            ["Directory.GetFileSystemEntries"] = "str",
        };

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
            foreach (var stmt in func.Body)
            {
                CheckStatement(stmt);
            }
        }

        foreach (var cls in module.Classes)
        {
            foreach (var ctor in cls.Constructors)
            {
                foreach (var stmt in ctor.Body)
                {
                    CheckStatement(stmt);
                }
            }

            foreach (var method in cls.Methods)
            {
                foreach (var stmt in method.Body)
                {
                    CheckStatement(stmt);
                }
            }

            foreach (var prop in cls.Properties)
            {
                if (prop.Getter != null)
                {
                    foreach (var stmt in prop.Getter.Body)
                    {
                        CheckStatement(stmt);
                    }
                }
                if (prop.Setter != null)
                {
                    foreach (var stmt in prop.Setter.Body)
                    {
                        CheckStatement(stmt);
                    }
                }
                if (prop.Initer != null)
                {
                    foreach (var stmt in prop.Initer.Body)
                    {
                        CheckStatement(stmt);
                    }
                }
            }

            foreach (var op in cls.OperatorOverloads)
            {
                foreach (var stmt in op.Body)
                {
                    CheckStatement(stmt);
                }
            }

            foreach (var idx in cls.Indexers)
            {
                if (idx.Getter != null)
                {
                    foreach (var stmt in idx.Getter.Body)
                    {
                        CheckStatement(stmt);
                    }
                }
                if (idx.Setter != null)
                {
                    foreach (var stmt in idx.Setter.Body)
                    {
                        CheckStatement(stmt);
                    }
                }
                if (idx.Initer != null)
                {
                    foreach (var stmt in idx.Initer.Body)
                    {
                        CheckStatement(stmt);
                    }
                }
            }
        }
    }

    private void CheckStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case BindStatementNode bind:
                CheckBind(bind);
                break;

            case IfStatementNode ifStmt:
                foreach (var s in ifStmt.ThenBody) CheckStatement(s);
                foreach (var ei in ifStmt.ElseIfClauses)
                    foreach (var s in ei.Body) CheckStatement(s);
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody) CheckStatement(s);
                break;

            case ForStatementNode forStmt:
                foreach (var s in forStmt.Body) CheckStatement(s);
                break;

            case WhileStatementNode whileStmt:
                foreach (var s in whileStmt.Body) CheckStatement(s);
                break;

            case DoWhileStatementNode doWhileStmt:
                foreach (var s in doWhileStmt.Body) CheckStatement(s);
                break;

            case MatchStatementNode match:
                foreach (var c in match.Cases)
                    foreach (var s in c.Body) CheckStatement(s);
                break;

            case TryStatementNode tryStmt:
                foreach (var s in tryStmt.TryBody) CheckStatement(s);
                foreach (var clause in tryStmt.CatchClauses)
                    foreach (var s in clause.Body) CheckStatement(s);
                if (tryStmt.FinallyBody != null)
                    foreach (var s in tryStmt.FinallyBody) CheckStatement(s);
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

        // Calor0254 — always-on hard type error: an array bound to a concrete
        // generic collection (the E1a trap). Independent of strict inference,
        // since the emitted C# would fail with CS0029 regardless.
        if (bind.TypeName != null && bind.Initializer != null)
        {
            CheckArrayToCollectionAssignability(bind, bind.Initializer);
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
    /// Rejects <c>§B{name:List&lt;T&gt;}</c> (or another concrete generic collection)
    /// bound to an array initializer — the E1a trap. Mirrors C#: an array satisfies
    /// the collection <em>interfaces</em> (<c>IList&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, …)
    /// but is not implicitly convertible to a concrete collection class, so the
    /// emitted C# would fail with CS0029. Caught here at compile time rather than
    /// left for a downstream <c>dotnet build</c> (#722).
    /// </summary>
    private void CheckArrayToCollectionAssignability(BindStatementNode bind, ExpressionNode init)
    {
        var declared = bind.TypeName!.Trim();
        if (!TryGetConcreteCollectionName(declared, out var collectionName))
        {
            return;
        }

        var elementType = InitializerArrayElement(init);
        if (elementType == null)
        {
            return;
        }

        var arrayForm = $"[{elementType}]";
        _diagnostics.ReportError(bind.Span, DiagnosticCode.BindArrayToConcreteCollection,
            $"Binding '{bind.Name}' declares '{declared}', but its initializer is an array. " +
            $"An array is not implicitly convertible to the concrete collection '{collectionName}<…>' " +
            $"(the emitted C# would fail with CS0029). Use the array form '{arrayForm}', or wrap the " +
            $"value explicitly (e.g. a new {collectionName} constructed from it).");
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

        if (ArrayReturningBclMethods.TryGetValue(call.Target, out var bclElement))
        {
            return bclElement;
        }

        if (_userReturnTypes.TryGetValue(call.Target, out var returnType) &&
            IsArrayTypeName(returnType))
        {
            return returnType.Trim().TrimStart('[').TrimEnd(']').Trim();
        }

        return null;
    }

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
