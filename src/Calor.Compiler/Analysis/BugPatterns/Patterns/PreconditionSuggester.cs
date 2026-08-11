using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
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
        var parameterIds = function.Symbol.Parameters
            .Where(parameter => !parameter.Id.IsNone)
            .Select(parameter => parameter.Id)
            .ToHashSet();
        if (parameterIds.Count == 0)
            return;

        // Get guarded params for this function
        HashSet<string>? guardedParams = null;
        _options.PreconditionGuardedParams?.TryGetValue(function.Symbol.Name, out guardedParams);
        IReadOnlySet<SymbolId>? guardedParameterIds = null;
        _options.PreconditionGuardedParameterIds?.TryGetValue(
            function.SymbolId,
            out guardedParameterIds);

        foreach (var stmt in function.Body)
        {
            CheckStatement(
                stmt,
                parameterIds,
                guardedParameterIds,
                guardedParams,
                diagnostics);
        }
    }

    private void CheckStatement(
        BoundStatement stmt,
        HashSet<SymbolId> parameterIds,
        IReadOnlySet<SymbolId>? guardedParameterIds,
        HashSet<string>? guardedParams,
        DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                if (bind.Initializer != null)
                    CheckExpression(bind.Initializer, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                    CheckExpression(ret.Expression, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundCallStatement call:
                foreach (var arg in call.Arguments)
                    CheckExpression(arg, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundIfStatement ifStmt:
                CheckExpression(ifStmt.Condition, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                foreach (var s in ifStmt.ThenBody)
                    CheckStatement(s, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    CheckExpression(elseIf.Condition, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                    foreach (var s in elseIf.Body)
                        CheckStatement(s, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                }
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        CheckStatement(s, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundWhileStatement whileStmt:
                CheckExpression(whileStmt.Condition, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                foreach (var s in whileStmt.Body)
                    CheckStatement(s, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundForStatement forStmt:
                CheckExpression(forStmt.From, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                CheckExpression(forStmt.To, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                if (forStmt.Step != null)
                    CheckExpression(forStmt.Step, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                foreach (var s in forStmt.Body)
                    CheckStatement(s, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundAssignmentStatement assign:
                CheckExpression(assign.Target, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                CheckExpression(assign.Value, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundCompoundAssignment compound:
                CheckExpression(compound.Target, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                CheckExpression(compound.Value, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundForeachStatement forEach:
                CheckExpression(forEach.Collection, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                foreach (var s in forEach.Body)
                    CheckStatement(s, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundDoWhileStatement doWhile:
                CheckExpression(doWhile.Condition, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                foreach (var s in doWhile.Body)
                    CheckStatement(s, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundUsingStatement usingStmt:
                CheckExpression(usingStmt.ResourceExpression, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                foreach (var s in usingStmt.Body)
                    CheckStatement(s, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundExpressionStatement exprStmt:
                CheckExpression(exprStmt.Expression, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            case BoundThrowStatement throwStmt:
                if (throwStmt.Expression != null)
                    CheckExpression(throwStmt.Expression, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;

            default:
                foreach (var expression in BoundNodeHelpers.GetImmediateExpressions(stmt))
                    CheckExpression(expression, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                foreach (var statement in BoundNodeHelpers.GetImmediateStatements(stmt))
                    CheckStatement(statement, parameterIds, guardedParameterIds, guardedParams, diagnostics);
                break;
        }
    }

    private void CheckExpression(
        BoundExpression expr,
        HashSet<SymbolId> parameterIds,
        IReadOnlySet<SymbolId>? guardedParameterIds,
        HashSet<string>? guardedParams,
        DiagnosticBag diagnostics)
    {
        if (expr is BoundBinaryExpression divisionExpr
            && divisionExpr.Operator is BinaryOperator.Divide or BinaryOperator.Modulo)
        {
            var divisor = BoundNodeHelpers.GetDivisor(divisionExpr);
            if (divisor is BoundVariableExpression varExpr
                && parameterIds.Contains(varExpr.Variable.Id))
            {
                var paramName = varExpr.Variable.Name;

                // Skip if already guarded by a precondition
                if (guardedParameterIds?.Contains(varExpr.Variable.Id) == true
                    || (_options.PreconditionGuardedParameterIds == null
                        && guardedParams?.Contains(paramName) == true))
                    return;

                // Suggestions are not verified bugs — suppress by default
                if (_options.ReportOnlyVerified)
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

        foreach (var child in BoundNodeHelpers.GetChildExpressions(expr))
            CheckExpression(child, parameterIds, guardedParameterIds, guardedParams, diagnostics);
    }
}
