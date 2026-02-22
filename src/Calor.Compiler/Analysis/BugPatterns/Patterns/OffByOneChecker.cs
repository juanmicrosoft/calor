using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

/// <summary>
/// Detects potential off-by-one errors in for loops where the loop bound
/// references a length/count/size property without subtracting 1, and the
/// loop body accesses an array at the loop variable index.
/// </summary>
public sealed class OffByOneChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public string Name => "OFF_BY_ONE";

    public OffByOneChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Check(BoundFunction function, DiagnosticBag diagnostics)
    {
        foreach (var stmt in function.Body)
        {
            CheckStatement(stmt, diagnostics);
        }
    }

    private void CheckStatement(BoundStatement stmt, DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case BoundForStatement forStmt:
                CheckForLoop(forStmt, diagnostics);
                foreach (var s in forStmt.Body)
                    CheckStatement(s, diagnostics);
                break;

            case BoundIfStatement ifStmt:
                foreach (var s in ifStmt.ThenBody)
                    CheckStatement(s, diagnostics);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                    foreach (var s in elseIf.Body)
                        CheckStatement(s, diagnostics);
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        CheckStatement(s, diagnostics);
                break;

            case BoundWhileStatement whileStmt:
                foreach (var s in whileStmt.Body)
                    CheckStatement(s, diagnostics);
                break;
        }
    }

    private void CheckForLoop(BoundForStatement forStmt, DiagnosticBag diagnostics)
    {
        // Check if the To bound references a length-like property
        if (!IsLengthLikeBound(forStmt.To))
            return;

        // Check if loop body contains array access using the loop variable
        var loopVarName = forStmt.LoopVariable.Name;
        if (!BodyContainsArrayAccessAtLoopVar(forStmt.Body, loopVarName))
            return;

        diagnostics.ReportWarning(
            forStmt.Span,
            DiagnosticCode.OffByOne,
            $"Loop iterates to length/count without subtracting 1; potential off-by-one error with array access at '{loopVarName}'");
    }

    /// <summary>
    /// Checks if the expression looks like a length/count/size property reference
    /// without a -1 adjustment.
    /// </summary>
    private static bool IsLengthLikeBound(BoundExpression expr)
    {
        // If it's a subtraction from a length-like value, it's probably correct
        if (expr is BoundBinaryExpression binExpr && binExpr.Operator == BinaryOperator.Subtract)
        {
            // e.g. arr.length - 1 — this is the correct pattern
            return false;
        }

        // Check if variable name looks like a length/count/size
        if (expr is BoundVariableExpression varExpr)
        {
            var name = varExpr.Variable.Name.ToLowerInvariant();
            // Direct name heuristics
            if (name.Contains("length") || name.Contains("count") || name.Contains("size") || name == "len")
                return true;

            // Single-letter parameter names commonly used as array bounds (n, m, k)
            // Only flag if the variable is a parameter (not a local)
            if (varExpr.Variable.IsParameter && name.Length == 1 && "nmk".Contains(name))
                return true;
        }

        // Check if it's a call to .Length, .Count, .Size
        if (expr is BoundCallExpression callExpr)
        {
            var target = callExpr.Target.ToLowerInvariant();
            return target.Contains("length") || target.Contains("count") || target.Contains("size");
        }

        return false;
    }

    /// <summary>
    /// Checks if any statement in the loop body accesses an array at the loop variable index.
    /// </summary>
    private static bool BodyContainsArrayAccessAtLoopVar(
        IReadOnlyList<BoundStatement> body,
        string loopVarName)
    {
        foreach (var stmt in body)
        {
            if (StatementReferencesLoopVar(stmt, loopVarName))
                return true;
        }
        return false;
    }

    private static bool StatementReferencesLoopVar(BoundStatement stmt, string loopVarName)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                return bind.Initializer != null && ExpressionUsesVariable(bind.Initializer, loopVarName);
            case BoundReturnStatement ret:
                return ret.Expression != null && ExpressionUsesVariable(ret.Expression, loopVarName);
            case BoundCallStatement call:
                return call.Arguments.Any(a => ExpressionUsesVariable(a, loopVarName));
            case BoundIfStatement ifStmt:
                return BodyContainsArrayAccessAtLoopVar(ifStmt.ThenBody, loopVarName) ||
                       ifStmt.ElseIfClauses.Any(c => BodyContainsArrayAccessAtLoopVar(c.Body, loopVarName)) ||
                       (ifStmt.ElseBody != null && BodyContainsArrayAccessAtLoopVar(ifStmt.ElseBody, loopVarName));
            case BoundWhileStatement whileStmt:
                return BodyContainsArrayAccessAtLoopVar(whileStmt.Body, loopVarName);
            case BoundForStatement forStmt:
                return BodyContainsArrayAccessAtLoopVar(forStmt.Body, loopVarName);
            default:
                return false;
        }
    }

    private static bool ExpressionUsesVariable(BoundExpression expr, string varName)
    {
        return BoundNodeHelpers.GetUsedVariables(expr).Any(v => v.Name == varName);
    }
}
