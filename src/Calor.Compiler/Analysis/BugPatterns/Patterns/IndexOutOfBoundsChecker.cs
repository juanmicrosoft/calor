using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Microsoft.Z3;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

/// <summary>
/// Checks for potential array index out of bounds access.
/// Uses Z3 SMT solver to verify if the index can be out of bounds given the path conditions.
/// </summary>
public sealed class IndexOutOfBoundsChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public string Name => "INDEX_OOB";

    public IndexOutOfBoundsChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Check(BoundFunction function, DiagnosticBag diagnostics)
    {
        // Walk through all statements looking for array accesses
        foreach (var stmt in function.Body)
        {
            CheckStatement(stmt, function, diagnostics, new List<BoundExpression>());
        }
    }

    private void CheckStatement(
        BoundStatement stmt,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                if (bind.Initializer != null)
                {
                    CheckExpression(bind.Initializer, function, diagnostics, pathConditions);
                }
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                {
                    CheckExpression(ret.Expression, function, diagnostics, pathConditions);
                }
                break;

            case BoundCallStatement call:
                foreach (var arg in call.Arguments)
                {
                    CheckExpression(arg, function, diagnostics, pathConditions);
                }
                break;

            case BoundIfStatement ifStmt:
                CheckExpression(ifStmt.Condition, function, diagnostics, pathConditions);

                var thenConditions = new List<BoundExpression>(pathConditions) { ifStmt.Condition };
                foreach (var s in ifStmt.ThenBody)
                {
                    CheckStatement(s, function, diagnostics, thenConditions);
                }

                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    CheckExpression(elseIf.Condition, function, diagnostics, pathConditions);
                    var elseIfConditions = new List<BoundExpression>(pathConditions) { elseIf.Condition };
                    foreach (var s in elseIf.Body)
                    {
                        CheckStatement(s, function, diagnostics, elseIfConditions);
                    }
                }

                if (ifStmt.ElseBody != null)
                {
                    foreach (var s in ifStmt.ElseBody)
                    {
                        CheckStatement(s, function, diagnostics, pathConditions);
                    }
                }
                break;

            case BoundWhileStatement whileStmt:
                CheckExpression(whileStmt.Condition, function, diagnostics, pathConditions);
                var whileConditions = new List<BoundExpression>(pathConditions) { whileStmt.Condition };
                foreach (var s in whileStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, whileConditions);
                }
                break;

            case BoundForStatement forStmt:
                CheckExpression(forStmt.From, function, diagnostics, pathConditions);
                CheckExpression(forStmt.To, function, diagnostics, pathConditions);
                if (forStmt.Step != null)
                {
                    CheckExpression(forStmt.Step, function, diagnostics, pathConditions);
                }
                foreach (var s in forStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, pathConditions);
                }
                break;

            case BoundAssignmentStatement assign:
                CheckExpression(assign.Target, function, diagnostics, pathConditions);
                CheckExpression(assign.Value, function, diagnostics, pathConditions);
                break;

            case BoundCompoundAssignment compound:
                CheckExpression(compound.Target, function, diagnostics, pathConditions);
                CheckExpression(compound.Value, function, diagnostics, pathConditions);
                break;

            case BoundForeachStatement forEach:
                CheckExpression(forEach.Collection, function, diagnostics, pathConditions);
                foreach (var s in forEach.Body)
                {
                    CheckStatement(s, function, diagnostics, pathConditions);
                }
                break;

            case BoundDoWhileStatement doWhile:
                CheckExpression(doWhile.Condition, function, diagnostics, pathConditions);
                foreach (var s in doWhile.Body)
                {
                    CheckStatement(s, function, diagnostics, pathConditions);
                }
                break;

            case BoundUsingStatement usingStmt:
                CheckExpression(usingStmt.ResourceExpression, function, diagnostics, pathConditions);
                foreach (var s in usingStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, pathConditions);
                }
                break;

            case BoundExpressionStatement exprStmt:
                CheckExpression(exprStmt.Expression, function, diagnostics, pathConditions);
                break;

            case BoundThrowStatement throwStmt:
                if (throwStmt.Expression != null)
                {
                    CheckExpression(throwStmt.Expression, function, diagnostics, pathConditions);
                }
                break;
        }
    }

    private void CheckExpression(
        BoundExpression expr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        // Note: BoundNodes don't have BoundArrayAccessExpression yet
        // This checker is prepared for when it's added
        // For now, we check for patterns that suggest array access

        // Check subexpressions
        switch (expr)
        {
            case BoundBinaryExpression binExpr:
                CheckExpression(binExpr.Left, function, diagnostics, pathConditions);
                CheckExpression(binExpr.Right, function, diagnostics, pathConditions);
                break;

            case BoundUnaryExpression unaryExpr:
                CheckExpression(unaryExpr.Operand, function, diagnostics, pathConditions);
                break;

            case BoundCallExpression callExpr:
                // Check if this is an array access call (e.g., arr.get(index))
                if (IsArrayAccessCall(callExpr))
                {
                    CheckArrayAccess(callExpr, function, diagnostics, pathConditions);
                }
                foreach (var arg in callExpr.Arguments)
                {
                    CheckExpression(arg, function, diagnostics, pathConditions);
                }
                break;

            case BoundConditionalExpression condExpr:
                CheckExpression(condExpr.Condition, function, diagnostics, pathConditions);
                CheckExpression(condExpr.WhenTrue, function, diagnostics, pathConditions);
                CheckExpression(condExpr.WhenFalse, function, diagnostics, pathConditions);
                break;
        }
    }

    private static bool IsArrayAccessCall(BoundCallExpression callExpr)
    {
        // Detect common array access patterns
        var target = callExpr.Target.ToLowerInvariant();
        return target.EndsWith(".get") ||
               target.EndsWith(".at") ||
               target.EndsWith("[]") ||
               target.Contains("array_get") ||
               target.Contains("list_get");
    }

    private void CheckArrayAccess(
        BoundCallExpression callExpr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        // Assuming the first argument is the index
        if (callExpr.Arguments.Count == 0)
            return;

        var indexExpr = callExpr.Arguments[0];

        // Check for negative literal index
        if (indexExpr is BoundIntLiteral intLit && intLit.Value < 0)
        {
            diagnostics.ReportError(
                callExpr.Span,
                DiagnosticCode.IndexOutOfBounds,
                $"Array access with negative literal index: {intLit.Value}");
            return;
        }

        // Heuristic: Check if there's a bounds check in path conditions
        if (!HasBoundsCheck(indexExpr, pathConditions))
        {
            if (_options.UseZ3Verification)
            {
                var canBeNegative = CanIndexBeNegative(indexExpr, function, pathConditions);
                if (canBeNegative == true)
                {
                    diagnostics.ReportWarning(
                        callExpr.Span,
                        DiagnosticCode.IndexOutOfBounds,
                        "Potential array access with negative index");
                }
            }
            else
            {
                // Simple heuristic: warn if index is a variable without obvious bounds check
                if (indexExpr is BoundVariableExpression varExpr)
                {
                    diagnostics.ReportWarning(
                        callExpr.Span,
                        DiagnosticCode.IndexOutOfBounds,
                        $"Array access with '{varExpr.Variable.Name}' may be out of bounds");
                }
            }
        }
    }

    private static bool HasBoundsCheck(BoundExpression indexExpr, List<BoundExpression> pathConditions)
    {
        if (indexExpr is not BoundVariableExpression varExpr)
            return false;

        var indexName = varExpr.Variable.Name;

        foreach (var condition in pathConditions)
        {
            if (condition is BoundBinaryExpression binExpr)
            {
                // Check for i >= 0
                if (binExpr.Operator == BinaryOperator.GreaterOrEqual)
                {
                    if (IsVariableAndZero(binExpr.Left, binExpr.Right, indexName))
                        return true;
                }

                // Check for i < length or i <= length - 1
                if (binExpr.Operator == BinaryOperator.LessThan ||
                    binExpr.Operator == BinaryOperator.LessOrEqual)
                {
                    if (binExpr.Left is BoundVariableExpression leftVar &&
                        leftVar.Variable.Name == indexName)
                    {
                        return true; // Assumes comparison with some length
                    }
                }
            }
        }

        return false;
    }

    private bool? CanIndexBeNegative(
        BoundExpression indexExpr,
        BoundFunction function,
        List<BoundExpression> pathConditions)
    {
        try
        {
            using var ctx = new Context();
            var translator = new BoundExpressionTranslator(ctx);

            // Declare parameters
            foreach (var param in function.Symbol.Parameters)
            {
                translator.DeclareVariable(param.Name, param.TypeName);
            }

            // Translate path conditions
            var pathConstraints = new List<BoolExpr>();
            foreach (var condition in pathConditions)
            {
                var translated = translator.TranslateBoolExpr(condition);
                if (translated != null)
                {
                    pathConstraints.Add(translated);
                }
            }

            // Translate the index
            var indexZ3 = translator.TranslateExpr(indexExpr);
            if (indexZ3 == null || indexZ3 is not BitVecExpr bvIndex)
            {
                return null;
            }

            // Check if (path conditions && index < 0) is satisfiable
            var solver = ctx.MkSolver();
            solver.Set("timeout", _options.Z3TimeoutMs);

            foreach (var constraint in pathConstraints)
            {
                solver.Assert(constraint);
            }

            // Assert index < 0 (signed comparison)
            solver.Assert(ctx.MkBVSLT(bvIndex, ctx.MkBV(0, bvIndex.SortSize)));

            var status = solver.Check();

            return status switch
            {
                Status.SATISFIABLE => true,
                Status.UNSATISFIABLE => false,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsVariableAndZero(BoundExpression maybeVar, BoundExpression maybeZero, string variableName)
    {
        return maybeVar is BoundVariableExpression varExpr &&
               varExpr.Variable.Name == variableName &&
               maybeZero is BoundIntLiteral intLit &&
               intLit.Value == 0;
    }
}
