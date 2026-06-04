using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Validates <c>§B</c> binding declarations against the rules in
/// <c>docs/syntax-reference/binding.md</c>.
///
/// Currently enforces a single rule: a binding must carry either a
/// <c>:type</c> annotation or an initializer expression. The other
/// inference-related checks (<c>Calor0251</c>–<c>Calor0253</c>) are
/// reserved for a future strict-inference pass.
///
/// This pass walks the parsed AST directly and does not depend on the
/// <c>Binder</c>. The <c>Binder</c> still contains a defensive copy of
/// the check (used by <c>VerificationAnalysisPass</c> and unit tests)
/// so that direct binder invocations still surface the diagnostic.
/// </summary>
public sealed class BindValidationPass
{
    private readonly DiagnosticBag _diagnostics;

    public BindValidationPass(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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
        if (bind.TypeName == null && bind.Initializer == null)
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindRequiresTypeOrInitializer,
                $"Binding '{bind.Name}' has no type annotation and no initializer. " +
                "Add either ':type' (e.g. '§B{" + bind.Name + ":i32}') " +
                "or an initializer expression so the binder can infer the type.");
        }
    }
}
