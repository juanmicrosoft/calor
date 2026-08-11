using Calor.Compiler.Ast;
using Calor.Compiler.Analysis.Dataflow;
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
        var checkedVariables = new HashSet<SymbolId>();

        foreach (var stmt in function.Body)
        {
            CheckStatement(stmt, function, diagnostics, checkedVariables, new List<BoundExpression>());
        }
    }

    private void CheckStatement(
        BoundStatement stmt,
        BoundFunction function,
        DiagnosticBag diagnostics,
        HashSet<SymbolId> checkedVariables,
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
                CheckCallExpression(
                    call.Target,
                    call.ReceiverSymbol,
                    call.Span,
                    diagnostics,
                    checkedVariables,
                    pathConditions);
                foreach (var argument in call.Arguments)
                    CheckExpression(argument, function, diagnostics, checkedVariables, pathConditions);
                break;

            case BoundIfStatement ifStmt:
                // Check if the condition is an Option/Result check
                var conditionChecks = ExtractOptionChecks(ifStmt.Condition);
                var thenChecked = new HashSet<SymbolId>(checkedVariables);
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
                    var elseIfChecks = ExtractOptionChecks(elseIf.Condition);
                    var elseIfChecked = new HashSet<SymbolId>(checkedVariables);
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
                var whileChecks = ExtractOptionChecks(whileStmt.Condition);
                var whileChecked = new HashSet<SymbolId>(checkedVariables);
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

            default:
                foreach (var expression in BoundNodeHelpers.GetImmediateExpressions(stmt))
                    CheckExpression(expression, function, diagnostics, checkedVariables, pathConditions);
                foreach (var statement in BoundNodeHelpers.GetImmediateStatements(stmt))
                    CheckStatement(statement, function, diagnostics, checkedVariables, pathConditions);
                break;
        }
    }

    private void CheckExpression(
        BoundExpression expr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        HashSet<SymbolId> checkedVariables,
        List<BoundExpression> pathConditions)
    {
        if (expr is BoundCallExpression callExpr)
        {
            CheckCallExpression(
                callExpr.Target,
                callExpr.ReceiverSymbol,
                callExpr.Span,
                diagnostics,
                checkedVariables,
                pathConditions);
        }

        foreach (var child in BoundNodeHelpers.GetChildExpressions(expr))
            CheckExpression(child, function, diagnostics, checkedVariables, pathConditions);
    }

    private void CheckCallExpression(
        string target,
        VariableSymbol? receiverSymbol,
        Parsing.TextSpan span,
        DiagnosticBag diagnostics,
        HashSet<SymbolId> checkedVariables,
        List<BoundExpression> pathConditions)
    {
        var lowerTarget = target.ToLowerInvariant();

        // Safe unwrap calls provide fallbacks — skip checking
        if (IsSafeUnwrapCall(lowerTarget))
            return;

        // Check for unsafe unwrap calls
        if (IsUnsafeUnwrapCall(lowerTarget))
        {
            // Check if the receiver has been verified
            if (receiverSymbol != null && !checkedVariables.Contains(receiverSymbol.Id))
            {
                if (!HasSafetyCheck(receiverSymbol, pathConditions))
                {
                    diagnostics.ReportWarning(
                        span,
                        DiagnosticCode.UnsafeUnwrap,
                        $"Unsafe unwrap on '{receiverSymbol.Name}' without prior Some/Ok check");
                }
            }
            else if (receiverSymbol == null)
            {
                // Can't determine receiver - warn
                diagnostics.ReportWarning(
                    span,
                    DiagnosticCode.NullDereference,
                    "Potential unsafe unwrap without prior Option/Result check");
            }
        }

    }

    private static bool IsUnsafeUnwrapCall(string target)
    {
        // Detect unsafe unwrap patterns; the catch-all must exclude every
        // safe fallback form so the predicate stays consistent with
        // IsSafeUnwrapCall regardless of call order
        return target.EndsWith(".unwrap") ||
               target.EndsWith(".unwrap_unchecked") ||
               target.EndsWith(".expect") ||
               target.EndsWith(".get_unchecked") ||
               (!IsSafeUnwrapCall(target) && target.Contains("unwrap"));
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

    private static HashSet<SymbolId> ExtractOptionChecks(
        BoundExpression condition)
    {
        var checkedVariables = new HashSet<SymbolId>();

        if (condition is BoundCallExpression callExpr)
        {
            var lowerTarget = callExpr.Target.ToLowerInvariant();
            if (lowerTarget.EndsWith(".is_some")
                || lowerTarget.EndsWith(".is_ok")
                || lowerTarget.EndsWith(".has_value")
                || lowerTarget.EndsWith(".is_present"))
            {
                if (callExpr.ReceiverSymbolId is { IsNone: false } receiverId)
                    checkedVariables.Add(receiverId);
            }
            return checkedVariables;
        }

        if (condition is not BoundBinaryExpression binary)
            return checkedVariables;

        if (binary.Operator == BinaryOperator.NotEqual)
        {
            if (binary.Left is BoundVariableExpression left
                && IsNullLiteral(binary.Right)
                && !left.Variable.Id.IsNone)
            {
                checkedVariables.Add(left.Variable.Id);
            }
            else if (binary.Right is BoundVariableExpression right
                     && IsNullLiteral(binary.Left)
                     && !right.Variable.Id.IsNone)
            {
                checkedVariables.Add(right.Variable.Id);
            }
        }

        if (binary.Operator == BinaryOperator.And)
        {
            checkedVariables.UnionWith(ExtractOptionChecks(binary.Left));
            checkedVariables.UnionWith(ExtractOptionChecks(binary.Right));
        }

        return checkedVariables;
    }

    private static bool HasSafetyCheck(
        VariableSymbol variable,
        List<BoundExpression> pathConditions)
    {
        foreach (var condition in pathConditions)
        {
            var checks = ExtractOptionChecks(condition);
            if (checks.Contains(variable.Id))
                return true;
        }
        return false;
    }

    private static bool IsNullLiteral(BoundExpression expr)
    {
        // In Calor, None is the null equivalent
        // This would need to be extended when BoundNoneLiteral is added
        return expr is BoundCallExpression call
            && call.Target.Equals("None", StringComparison.OrdinalIgnoreCase);
    }
}
