using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

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
/// <para>Strict-mode checks (enabled by <c>--strict-bind-inference</c>):</para>
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

    /// <summary>
    /// Well-known generic factory calls whose return type cannot be inferred
    /// without an explicit type argument. Matched on the call's target
    /// string ending — so <c>Vec.empty</c>, <c>Vec&lt;T&gt;.empty</c>, and
    /// <c>some.module.Vec.empty</c> all match <c>Vec.empty</c>.
    /// </summary>
    private static readonly string[] GenericFactoryTargets =
    [
        "Vec.empty",
        "Vec.create",
        "List.empty",
        "List.create",
        "Array.empty",
        "Set.empty",
        "Map.empty",
        "Dictionary.empty",
        "Dict.empty",
        "Queue.empty",
        "Stack.empty",
    ];

    public BindValidationPass(DiagnosticBag diagnostics, bool strictInference = false)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _strictInference = strictInference;
    }

    public void Check(ModuleNode module)
    {
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

        // Strict-mode checks (Calor0251-0253) — only when --strict-bind-inference is set
        // and the binding has no explicit type annotation. An explicit :type always wins.
        if (!_strictInference || bind.TypeName != null || bind.Initializer == null)
        {
            return;
        }

        CheckStrictInitializer(bind, bind.Initializer);
    }

    private void CheckStrictInitializer(BindStatementNode bind, ExpressionNode init)
    {
        // Calor0251 — bare none/null cannot infer a type.
        if (init is NoneExpressionNode none && none.TypeName == null)
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindCannotInferNullLiteral,
                $"Binding '{bind.Name}' uses 'none' without a type. " +
                "Inference cannot pick a concrete element type. " +
                "Add ':Option<T>' (e.g. '§B{" + bind.Name + ":Option<i32>} none') " +
                "or use a typed §NN{type=...} form.");
            return;
        }

        if (init is ReferenceNode refNode && refNode.Name == "null")
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindCannotInferNullLiteral,
                $"Binding '{bind.Name}' is initialised to 'null' with no declared type. " +
                "Inference cannot pick a concrete type. " +
                "Add ':T?' or use an Option type (e.g. '§B{" + bind.Name + ":Option<T>} none').");
            return;
        }

        // Calor0252 — well-known generic factory call without explicit type.
        if (init is CallExpressionNode call && IsGenericFactoryTarget(call.Target))
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindCannotInferGenericReturn,
                $"Binding '{bind.Name}' is initialised from '{call.Target}' whose return type " +
                "is generic and has no resolved type argument. " +
                "Add an explicit type annotation, e.g. '§B{" + bind.Name + ":Vec<i32>} §C{Vec<i32>.empty} §/C'.");
            return;
        }

        // Calor0253 — binary op mixing integer and float literal operands.
        if (init is BinaryOperationNode bin && IsAmbiguousNumeric(bin))
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindAmbiguousNumeric,
                $"Binding '{bind.Name}' is initialised from a numeric expression that mixes " +
                "integer and floating-point literals; widening could pick more than one type. " +
                "Add an explicit numeric annotation, e.g. '§B{" + bind.Name + ":f64} ...' " +
                "or '§B{" + bind.Name + ":i32} ...'.");
            return;
        }
    }

    private static bool IsGenericFactoryTarget(string target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return false;
        }
        // Match either an exact target, or any target whose tail is a known
        // factory (so e.g. "my.module.Vec.empty" still matches "Vec.empty").
        foreach (var known in GenericFactoryTargets)
        {
            if (target == known || target.EndsWith("." + known, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAmbiguousNumeric(BinaryOperationNode bin)
    {
        return IsIntegerLiteral(bin.Left) && IsFloatLiteral(bin.Right)
            || IsFloatLiteral(bin.Left) && IsIntegerLiteral(bin.Right);
    }

    private static bool IsIntegerLiteral(ExpressionNode e) => e is IntLiteralNode;

    private static bool IsFloatLiteral(ExpressionNode e) => e is FloatLiteralNode or DecimalLiteralNode;
}
