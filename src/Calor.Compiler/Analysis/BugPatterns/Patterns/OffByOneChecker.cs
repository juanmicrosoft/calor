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
        foreach (var loop in BoundNodeHelpers.DescendantsAndSelf(function)
                     .OfType<BoundForStatement>())
            CheckForLoop(loop, diagnostics);
    }

    private void CheckForLoop(BoundForStatement forStmt, DiagnosticBag diagnostics)
    {
        // Check if the To bound references a length-like property
        if (!IsLengthLikeBound(forStmt.To))
            return;

        // Check if loop body contains array access using the loop variable
        var loopVariable = forStmt.LoopVariable;
        if (!BodyContainsArrayAccessAtLoopVar(forStmt.Body, loopVariable))
            return;

        diagnostics.ReportWarning(
            forStmt.Span,
            DiagnosticCode.OffByOne,
            $"Loop iterates to length/count without subtracting 1; potential off-by-one error with array access at '{loopVariable.Name}'");
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
        VariableSymbol loopVariable)
    {
        foreach (var stmt in body)
        {
            foreach (var node in BoundNodeHelpers.DescendantsAndSelf(stmt))
            {
                if (node is BoundVariableExpression variable
                    && BoundNodeHelpers.SameSymbol(variable.Variable, loopVariable))
                {
                    return true;
                }

                if (node is BoundArrayAccessExpression access
                    && access.Indices.Any(index => ExpressionUsesVariable(index, loopVariable)))
                {
                    return true;
                }

                if (node is BoundCallExpression call
                    && IsArrayAccessCall(call)
                    && call.Arguments.Any(argument => ExpressionUsesVariable(argument, loopVariable)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsArrayAccessCall(BoundCallExpression call)
    {
        var target = call.Target.ToLowerInvariant();
        return target.EndsWith(".get")
               || target.EndsWith(".at")
               || target.EndsWith("[]")
               || target.Contains("array_get")
               || target.Contains("list_get");
    }

    private static bool ExpressionUsesVariable(
        BoundExpression expr,
        VariableSymbol variable)
    {
        return BoundNodeHelpers.GetUsedVariables(expr)
            .Any(used => BoundNodeHelpers.SameSymbol(used, variable));
    }
}
