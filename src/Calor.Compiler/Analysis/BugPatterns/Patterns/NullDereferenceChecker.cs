using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

/// <summary>
/// Checks for potential null/None dereference (Option unwrap without check).
/// Analyzes Option&lt;T&gt; and Result&lt;T,E&gt; unwrap patterns.
/// </summary>
public sealed class NullDereferenceChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public string Name => "NULL_DEREF";

    public NullDereferenceChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Check(BoundFunction function, DiagnosticBag diagnostics)
    {
        // Track which Option/Result variables have been checked
        var checkedVariables = new HashSet<string>();

        foreach (var stmt in function.Body)
        {
            CheckStatement(stmt, function, diagnostics, checkedVariables, new List<BoundExpression>());
        }
    }

    private void CheckStatement(
        BoundStatement stmt,
        BoundFunction function,
        DiagnosticBag diagnostics,
        HashSet<string> checkedVariables,
        List<BoundExpression> pathConditions)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                if (bind.Initializer != null)
                {
                    CheckExpression(bind.Initializer, function, diagnostics, checkedVariables, pathConditions);
                }
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                {
                    CheckExpression(ret.Expression, function, diagnostics, checkedVariables, pathConditions);
                }
                break;

            case BoundCallStatement call:
                CheckCallExpression(call.Target, call.Arguments, call.Span, function, diagnostics, checkedVariables, pathConditions);
                break;

            case BoundIfStatement ifStmt:
                // Check if the condition is an Option/Result check
                var (conditionChecks, isMatchCheck) = ExtractOptionChecks(ifStmt.Condition);
                var thenChecked = new HashSet<string>(checkedVariables);
                thenChecked.UnionWith(conditionChecks);

                CheckExpression(ifStmt.Condition, function, diagnostics, checkedVariables, pathConditions);

                var thenConditions = new List<BoundExpression>(pathConditions) { ifStmt.Condition };
                foreach (var s in ifStmt.ThenBody)
                {
                    CheckStatement(s, function, diagnostics, thenChecked, thenConditions);
                }

                // Handle else-if
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    var (elseIfChecks, _) = ExtractOptionChecks(elseIf.Condition);
                    var elseIfChecked = new HashSet<string>(checkedVariables);
                    elseIfChecked.UnionWith(elseIfChecks);

                    CheckExpression(elseIf.Condition, function, diagnostics, checkedVariables, pathConditions);

                    var elseIfConditions = new List<BoundExpression>(pathConditions) { elseIf.Condition };
                    foreach (var s in elseIf.Body)
                    {
                        CheckStatement(s, function, diagnostics, elseIfChecked, elseIfConditions);
                    }
                }

                // Handle else (the condition was false, so Option/Result might be None/Err)
                if (ifStmt.ElseBody != null)
                {
                    // In else, the opposite might be checked (e.g., is_none check in then means is_some in else)
                    foreach (var s in ifStmt.ElseBody)
                    {
                        CheckStatement(s, function, diagnostics, checkedVariables, pathConditions);
                    }
                }
                break;

            case BoundWhileStatement whileStmt:
                var (whileChecks, _) = ExtractOptionChecks(whileStmt.Condition);
                var whileChecked = new HashSet<string>(checkedVariables);
                whileChecked.UnionWith(whileChecks);

                CheckExpression(whileStmt.Condition, function, diagnostics, checkedVariables, pathConditions);

                var whileConditions = new List<BoundExpression>(pathConditions) { whileStmt.Condition };
                foreach (var s in whileStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, whileChecked, whileConditions);
                }
                break;

            case BoundForStatement forStmt:
                CheckExpression(forStmt.From, function, diagnostics, checkedVariables, pathConditions);
                CheckExpression(forStmt.To, function, diagnostics, checkedVariables, pathConditions);
                if (forStmt.Step != null)
                {
                    CheckExpression(forStmt.Step, function, diagnostics, checkedVariables, pathConditions);
                }
                foreach (var s in forStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, checkedVariables, pathConditions);
                }
                break;

            case BoundAssignmentStatement assign:
                CheckExpression(assign.Target, function, diagnostics, checkedVariables, pathConditions);
                CheckExpression(assign.Value, function, diagnostics, checkedVariables, pathConditions);
                break;

            case BoundCompoundAssignment compound:
                CheckExpression(compound.Target, function, diagnostics, checkedVariables, pathConditions);
                CheckExpression(compound.Value, function, diagnostics, checkedVariables, pathConditions);
                break;

            case BoundForeachStatement forEach:
                CheckExpression(forEach.Collection, function, diagnostics, checkedVariables, pathConditions);
                foreach (var s in forEach.Body)
                {
                    CheckStatement(s, function, diagnostics, checkedVariables, pathConditions);
                }
                break;

            case BoundDoWhileStatement doWhile:
                CheckExpression(doWhile.Condition, function, diagnostics, checkedVariables, pathConditions);
                foreach (var s in doWhile.Body)
                {
                    CheckStatement(s, function, diagnostics, checkedVariables, pathConditions);
                }
                break;

            case BoundUsingStatement usingStmt:
                CheckExpression(usingStmt.ResourceExpression, function, diagnostics, checkedVariables, pathConditions);
                foreach (var s in usingStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, checkedVariables, pathConditions);
                }
                break;

            case BoundExpressionStatement exprStmt:
                CheckExpression(exprStmt.Expression, function, diagnostics, checkedVariables, pathConditions);
                break;

            case BoundThrowStatement throwStmt:
                if (throwStmt.Expression != null)
                {
                    CheckExpression(throwStmt.Expression, function, diagnostics, checkedVariables, pathConditions);
                }
                break;
        }
    }

    private void CheckExpression(
        BoundExpression expr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        HashSet<string> checkedVariables,
        List<BoundExpression> pathConditions)
    {
        switch (expr)
        {
            case BoundCallExpression callExpr:
                CheckCallExpression(callExpr.Target, callExpr.Arguments, callExpr.Span, function, diagnostics, checkedVariables, pathConditions);
                break;

            case BoundBinaryExpression binExpr:
                CheckExpression(binExpr.Left, function, diagnostics, checkedVariables, pathConditions);
                CheckExpression(binExpr.Right, function, diagnostics, checkedVariables, pathConditions);
                break;

            case BoundUnaryExpression unaryExpr:
                CheckExpression(unaryExpr.Operand, function, diagnostics, checkedVariables, pathConditions);
                break;

            case BoundConditionalExpression condExpr:
                CheckExpression(condExpr.Condition, function, diagnostics, checkedVariables, pathConditions);
                CheckExpression(condExpr.WhenTrue, function, diagnostics, checkedVariables, pathConditions);
                CheckExpression(condExpr.WhenFalse, function, diagnostics, checkedVariables, pathConditions);
                break;
        }
    }

    private void CheckCallExpression(
        string target,
        IReadOnlyList<BoundExpression> arguments,
        Parsing.TextSpan span,
        BoundFunction function,
        DiagnosticBag diagnostics,
        HashSet<string> checkedVariables,
        List<BoundExpression> pathConditions)
    {
        var lowerTarget = target.ToLowerInvariant();

        // Safe unwrap calls provide fallbacks — skip checking
        if (IsSafeUnwrapCall(lowerTarget))
        {
            // Still check arguments recursively
            foreach (var arg in arguments)
            {
                CheckExpression(arg, function, diagnostics, checkedVariables, pathConditions);
            }
            return;
        }

        // Check for unsafe unwrap calls
        if (IsUnsafeUnwrapCall(lowerTarget))
        {
            // Check if the receiver has been verified
            var receiverName = ExtractReceiverName(target);
            if (receiverName != null && !checkedVariables.Contains(receiverName))
            {
                if (!HasSafetyCheck(receiverName, pathConditions))
                {
                    diagnostics.ReportWarning(
                        span,
                        DiagnosticCode.UnsafeUnwrap,
                        $"Unsafe unwrap on '{receiverName}' without prior Some/Ok check");
                }
            }
            else if (receiverName == null)
            {
                // Can't determine receiver - warn
                diagnostics.ReportWarning(
                    span,
                    DiagnosticCode.NullDereference,
                    "Potential unsafe unwrap without prior Option/Result check");
            }
        }

        // Recursively check arguments
        foreach (var arg in arguments)
        {
            CheckExpression(arg, function, diagnostics, checkedVariables, pathConditions);
        }
    }

    private static bool IsUnsafeUnwrapCall(string target)
    {
        // Detect unsafe unwrap patterns
        return target.EndsWith(".unwrap") ||
               target.EndsWith(".unwrap_unchecked") ||
               target.EndsWith(".expect") ||
               target.EndsWith(".unwrap_or_else") == false && target.Contains("unwrap") ||
               target.EndsWith(".get_unchecked");
    }

    private static bool IsSafeUnwrapCall(string target)
    {
        // These are safe because they provide fallbacks
        return target.EndsWith(".unwrap_or") ||
               target.EndsWith(".unwrap_or_default") ||
               target.EndsWith(".unwrap_or_else") ||
               target.EndsWith(".get_or_insert") ||
               target.EndsWith(".map_or") ||
               target.EndsWith(".map_or_else");
    }

    private static string? ExtractReceiverName(string target)
    {
        // Extract variable name from "variable.method" pattern
        var dotIndex = target.LastIndexOf('.');
        if (dotIndex > 0)
        {
            return target[..dotIndex];
        }
        return null;
    }

    private static (HashSet<string> CheckedVariables, bool IsMatchCheck) ExtractOptionChecks(BoundExpression condition)
    {
        var checkedVariables = new HashSet<string>();
        var isMatchCheck = false;

        switch (condition)
        {
            case BoundCallExpression callExpr:
                var lowerTarget = callExpr.Target.ToLowerInvariant();

                // Check for is_some, is_ok patterns
                if (lowerTarget.EndsWith(".is_some") ||
                    lowerTarget.EndsWith(".is_ok") ||
                    lowerTarget.EndsWith(".has_value") ||
                    lowerTarget.EndsWith(".is_present"))
                {
                    var receiver = ExtractReceiverName(callExpr.Target);
                    if (receiver != null)
                    {
                        checkedVariables.Add(receiver);
                    }
                }
                break;

            case BoundBinaryExpression binExpr:
                // Handle "x != null" or "x != None" patterns
                if (binExpr.Operator == BinaryOperator.NotEqual)
                {
                    if (binExpr.Left is BoundVariableExpression varExpr)
                    {
                        // Check if right side is null/None literal
                        if (IsNullLiteral(binExpr.Right))
                        {
                            checkedVariables.Add(varExpr.Variable.Name);
                        }
                    }
                    else if (binExpr.Right is BoundVariableExpression varExpr2)
                    {
                        if (IsNullLiteral(binExpr.Left))
                        {
                            checkedVariables.Add(varExpr2.Variable.Name);
                        }
                    }
                }

                // Recurse into logical combinations
                if (binExpr.Operator == BinaryOperator.And)
                {
                    var (leftChecks, _) = ExtractOptionChecks(binExpr.Left);
                    var (rightChecks, _) = ExtractOptionChecks(binExpr.Right);
                    checkedVariables.UnionWith(leftChecks);
                    checkedVariables.UnionWith(rightChecks);
                }
                break;
        }

        return (checkedVariables, isMatchCheck);
    }

    private static bool HasSafetyCheck(string variableName, List<BoundExpression> pathConditions)
    {
        foreach (var condition in pathConditions)
        {
            var (checks, _) = ExtractOptionChecks(condition);
            if (checks.Contains(variableName))
                return true;
        }
        return false;
    }

    private static bool IsNullLiteral(BoundExpression expr)
    {
        // In Calor, None is the null equivalent
        // This would need to be extended when BoundNoneLiteral is added
        return expr switch
        {
            BoundCallExpression call when call.Target.Equals("None", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }
}
