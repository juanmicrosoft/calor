using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

/// <summary>
/// Detects operations on unconstrained parameters and suggests missing preconditions.
/// For example, division by a parameter without a §Q (!= param 0) contract.
/// </summary>
public sealed class PreconditionSuggester : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public string Name => "MISSING_PRECONDITION";

    public PreconditionSuggester(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Check(BoundFunction function, DiagnosticBag diagnostics)
    {
        var paramNames = function.Symbol.Parameters.Select(p => p.Name).ToHashSet();
        if (paramNames.Count == 0)
            return;

        // Get guarded params for this function
        HashSet<string>? guardedParams = null;
        _options.PreconditionGuardedParams?.TryGetValue(function.Symbol.Name, out guardedParams);

        foreach (var stmt in function.Body)
        {
            CheckStatement(stmt, paramNames, guardedParams, diagnostics);
        }
    }

    private void CheckStatement(
        BoundStatement stmt,
        HashSet<string> paramNames,
        HashSet<string>? guardedParams,
        DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                if (bind.Initializer != null)
                    CheckExpression(bind.Initializer, paramNames, guardedParams, diagnostics);
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                    CheckExpression(ret.Expression, paramNames, guardedParams, diagnostics);
                break;

            case BoundCallStatement call:
                foreach (var arg in call.Arguments)
                    CheckExpression(arg, paramNames, guardedParams, diagnostics);
                break;

            case BoundIfStatement ifStmt:
                CheckExpression(ifStmt.Condition, paramNames, guardedParams, diagnostics);
                foreach (var s in ifStmt.ThenBody)
                    CheckStatement(s, paramNames, guardedParams, diagnostics);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    CheckExpression(elseIf.Condition, paramNames, guardedParams, diagnostics);
                    foreach (var s in elseIf.Body)
                        CheckStatement(s, paramNames, guardedParams, diagnostics);
                }
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        CheckStatement(s, paramNames, guardedParams, diagnostics);
                break;

            case BoundWhileStatement whileStmt:
                CheckExpression(whileStmt.Condition, paramNames, guardedParams, diagnostics);
                foreach (var s in whileStmt.Body)
                    CheckStatement(s, paramNames, guardedParams, diagnostics);
                break;

            case BoundForStatement forStmt:
                CheckExpression(forStmt.From, paramNames, guardedParams, diagnostics);
                CheckExpression(forStmt.To, paramNames, guardedParams, diagnostics);
                if (forStmt.Step != null)
                    CheckExpression(forStmt.Step, paramNames, guardedParams, diagnostics);
                foreach (var s in forStmt.Body)
                    CheckStatement(s, paramNames, guardedParams, diagnostics);
                break;
        }
    }

    private void CheckExpression(
        BoundExpression expr,
        HashSet<string> paramNames,
        HashSet<string>? guardedParams,
        DiagnosticBag diagnostics)
    {
        if (BoundNodeHelpers.ContainsDivision(expr, out var divisionExpr) && divisionExpr != null)
        {
            var divisor = BoundNodeHelpers.GetDivisor(divisionExpr);
            if (divisor is BoundVariableExpression varExpr && paramNames.Contains(varExpr.Variable.Name))
            {
                var paramName = varExpr.Variable.Name;

                // Skip if already guarded by a precondition
                if (guardedParams != null && guardedParams.Contains(paramName))
                    return;

                var fix = new SuggestedFix(
                    $"Add precondition: §Q (!= {paramName} 0)",
                    TextEdit.Insert("", divisionExpr.Span.Line, 0,
                        $"  §Q (!= {paramName} 0)\n"));

                diagnostics.ReportWarningWithFix(
                    divisionExpr.Span,
                    DiagnosticCode.MissingPrecondition,
                    $"Division by '{paramName}' without precondition; consider adding §Q (!= {paramName} 0)",
                    fix);
            }
        }

        // Recurse into subexpressions
        switch (expr)
        {
            case BoundBinaryExpression binExpr:
                CheckExpression(binExpr.Left, paramNames, guardedParams, diagnostics);
                CheckExpression(binExpr.Right, paramNames, guardedParams, diagnostics);
                break;
            case BoundUnaryExpression unaryExpr:
                CheckExpression(unaryExpr.Operand, paramNames, guardedParams, diagnostics);
                break;
            case BoundCallExpression callExpr:
                foreach (var arg in callExpr.Arguments)
                    CheckExpression(arg, paramNames, guardedParams, diagnostics);
                break;
        }
    }
}
