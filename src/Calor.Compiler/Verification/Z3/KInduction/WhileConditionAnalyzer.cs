using Calor.Compiler.Ast;
using Calor.Compiler.Binding;

namespace Calor.Compiler.Verification.Z3.KInduction;

/// <summary>
/// Analyzes while loop conditions to extract information needed for k-induction.
/// </summary>
public static class WhileConditionAnalyzer
{
    /// <summary>
    /// Information extracted from a while loop for k-induction.
    /// </summary>
    public sealed record WhileLoopInfo(
        string? LoopVariable,
        long? LowerBound,
        long? UpperBound,
        bool IsDecrementing,
        string? ConditionOperator,
        BoundExpression? BoundValue)
    {
        /// <summary>
        /// Whether the loop has enough information for k-induction.
        /// </summary>
        public bool IsAnalyzable => LoopVariable != null && (LowerBound.HasValue || UpperBound.HasValue);
    }

    /// <summary>
    /// Extracts information from a while loop's body about how the loop variable changes.
    /// </summary>
    public sealed record TransitionInfo(
        string Variable,
        TransitionKind Kind,
        long? Delta)
    {
        /// <summary>
        /// Whether this is a well-understood transition.
        /// </summary>
        public bool IsWellFormed => Kind != TransitionKind.Unknown && (Delta.HasValue || Kind == TransitionKind.Unknown);
    }

    /// <summary>
    /// The kind of transition in a loop.
    /// </summary>
    public enum TransitionKind
    {
        Increment,   // i++, i = i + 1
        Decrement,   // i--, i = i - 1
        AddConstant, // i = i + c
        SubConstant, // i = i - c
        Unknown
    }

    /// <summary>
    /// Analyzes a while loop condition to extract loop bounds and variable information.
    /// </summary>
    /// <param name="condition">The loop condition expression.</param>
    /// <returns>Extracted information about the loop, or null if not analyzable.</returns>
    public static WhileLoopInfo? Analyze(BoundExpression condition)
    {
        // Pattern match common condition forms:
        // - i < n, i <= n (incrementing loop)
        // - i > 0, i >= 1 (decrementing loop)
        // - i != n (either direction)
        // - i < n && i >= 0 (bounded range)

        if (condition is BoundBinaryExpression binExpr)
        {
            return AnalyzeBinaryCondition(binExpr);
        }

        return null;
    }

    private static WhileLoopInfo? AnalyzeBinaryCondition(BoundBinaryExpression binExpr)
    {
        // Handle conjunction: i < n && i >= 0
        if (binExpr.Operator == BinaryOperator.And)
        {
            var leftInfo = Analyze(binExpr.Left);
            var rightInfo = Analyze(binExpr.Right);

            if (leftInfo != null && rightInfo != null &&
                leftInfo.LoopVariable == rightInfo.LoopVariable)
            {
                // Combine bounds
                return new WhileLoopInfo(
                    leftInfo.LoopVariable,
                    leftInfo.LowerBound ?? rightInfo.LowerBound,
                    leftInfo.UpperBound ?? rightInfo.UpperBound,
                    leftInfo.IsDecrementing || rightInfo.IsDecrementing,
                    leftInfo.ConditionOperator,
                    leftInfo.BoundValue ?? rightInfo.BoundValue);
            }

            // Return whichever one is valid
            return leftInfo ?? rightInfo;
        }

        // Handle simple comparisons
        return binExpr.Operator switch
        {
            BinaryOperator.LessThan => AnalyzeLessThan(binExpr),
            BinaryOperator.LessOrEqual => AnalyzeLessOrEqual(binExpr),
            BinaryOperator.GreaterThan => AnalyzeGreaterThan(binExpr),
            BinaryOperator.GreaterOrEqual => AnalyzeGreaterOrEqual(binExpr),
            BinaryOperator.NotEqual => AnalyzeNotEqual(binExpr),
            _ => null
        };
    }

    private static WhileLoopInfo? AnalyzeLessThan(BoundBinaryExpression binExpr)
    {
        // Pattern: i < n (incrementing loop)
        var varName = GetVariableName(binExpr.Left);
        var upperBound = GetIntValue(binExpr.Right);

        if (varName != null)
        {
            return new WhileLoopInfo(varName, null, upperBound, false, "<", binExpr.Right);
        }

        // Pattern: n < i is unusual but possible (decrementing)
        varName = GetVariableName(binExpr.Right);
        var lowerBound = GetIntValue(binExpr.Left);
        if (varName != null && lowerBound != null)
        {
            return new WhileLoopInfo(varName, lowerBound + 1, null, true, "<", binExpr.Left);
        }

        return null;
    }

    private static WhileLoopInfo? AnalyzeLessOrEqual(BoundBinaryExpression binExpr)
    {
        // Pattern: i <= n (incrementing loop)
        var varName = GetVariableName(binExpr.Left);
        var upperBound = GetIntValue(binExpr.Right);

        if (varName != null)
        {
            return new WhileLoopInfo(varName, null, upperBound, false, "<=", binExpr.Right);
        }

        return null;
    }

    private static WhileLoopInfo? AnalyzeGreaterThan(BoundBinaryExpression binExpr)
    {
        // Pattern: i > 0 (decrementing loop)
        var varName = GetVariableName(binExpr.Left);
        var lowerBound = GetIntValue(binExpr.Right);

        if (varName != null)
        {
            return new WhileLoopInfo(varName, lowerBound != null ? lowerBound + 1 : null, null, true, ">", binExpr.Right);
        }

        return null;
    }

    private static WhileLoopInfo? AnalyzeGreaterOrEqual(BoundBinaryExpression binExpr)
    {
        // Pattern: i >= 1 (decrementing loop)
        var varName = GetVariableName(binExpr.Left);
        var lowerBound = GetIntValue(binExpr.Right);

        if (varName != null)
        {
            return new WhileLoopInfo(varName, lowerBound, null, true, ">=", binExpr.Right);
        }

        return null;
    }

    private static WhileLoopInfo? AnalyzeNotEqual(BoundBinaryExpression binExpr)
    {
        // Pattern: i != n (direction unknown without body analysis)
        var varName = GetVariableName(binExpr.Left) ?? GetVariableName(binExpr.Right);
        var boundValue = GetIntValue(binExpr.Left) ?? GetIntValue(binExpr.Right);
        var boundExpr = GetVariableName(binExpr.Left) != null ? binExpr.Right : binExpr.Left;

        if (varName != null)
        {
            return new WhileLoopInfo(varName, null, boundValue, false, "!=", boundExpr);
        }

        return null;
    }

    /// <summary>
    /// Analyzes loop body to determine how the loop variable changes.
    /// </summary>
    /// <param name="body">The loop body statements.</param>
    /// <param name="loopVariable">The loop variable name to track.</param>
    /// <returns>Transition information for the loop variable.</returns>
    public static TransitionInfo? AnalyzeTransition(IReadOnlyList<BoundStatement> body, string loopVariable)
    {
        foreach (var stmt in body)
        {
            var transition = AnalyzeStatementTransition(stmt, loopVariable);
            if (transition != null)
                return transition;
        }

        return null;
    }

    private static TransitionInfo? AnalyzeStatementTransition(BoundStatement stmt, string loopVariable)
    {
        switch (stmt)
        {
            case BoundBindStatement bind when bind.Variable.Name == loopVariable:
                return AnalyzeBindingTransition(bind, loopVariable);

            case BoundIfStatement ifStmt:
                // Check all branches
                foreach (var s in ifStmt.ThenBody)
                {
                    var t = AnalyzeStatementTransition(s, loopVariable);
                    if (t != null) return t;
                }
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    foreach (var s in elseIf.Body)
                    {
                        var t = AnalyzeStatementTransition(s, loopVariable);
                        if (t != null) return t;
                    }
                }
                if (ifStmt.ElseBody != null)
                {
                    foreach (var s in ifStmt.ElseBody)
                    {
                        var t = AnalyzeStatementTransition(s, loopVariable);
                        if (t != null) return t;
                    }
                }
                break;

            case BoundForStatement forStmt:
                foreach (var s in forStmt.Body)
                {
                    var t = AnalyzeStatementTransition(s, loopVariable);
                    if (t != null) return t;
                }
                break;

            case BoundWhileStatement whileStmt:
                foreach (var s in whileStmt.Body)
                {
                    var t = AnalyzeStatementTransition(s, loopVariable);
                    if (t != null) return t;
                }
                break;
        }

        return null;
    }

    private static TransitionInfo? AnalyzeBindingTransition(BoundBindStatement bind, string loopVariable)
    {
        if (bind.Initializer == null)
            return null;

        // Pattern: i = i + 1, i = i - 1, i = i + c, i = i - c
        if (bind.Initializer is BoundBinaryExpression binExpr)
        {
            // Check if one side is the loop variable
            var leftVar = GetVariableName(binExpr.Left);
            var rightVar = GetVariableName(binExpr.Right);

            if (leftVar == loopVariable)
            {
                var delta = GetIntValue(binExpr.Right);
                if (delta != null)
                {
                    return binExpr.Operator switch
                    {
                        BinaryOperator.Add => new TransitionInfo(loopVariable, TransitionKind.AddConstant, delta),
                        BinaryOperator.Subtract => new TransitionInfo(loopVariable, TransitionKind.SubConstant, delta),
                        _ => null
                    };
                }
            }
            else if (rightVar == loopVariable)
            {
                var delta = GetIntValue(binExpr.Left);
                if (delta != null && binExpr.Operator == BinaryOperator.Add)
                {
                    return new TransitionInfo(loopVariable, TransitionKind.AddConstant, delta);
                }
            }
        }

        // Pattern: i = newValue (complete reassignment)
        // This is harder to analyze without more context

        return null;
    }

    private static string? GetVariableName(BoundExpression expr)
    {
        return expr switch
        {
            BoundVariableExpression varExpr => varExpr.Variable.Name,
            _ => null
        };
    }

    private static long? GetIntValue(BoundExpression expr)
    {
        return expr switch
        {
            BoundIntLiteral intLit => intLit.Value,
            _ => null
        };
    }

    /// <summary>
    /// Creates a loop context from while loop analysis for invariant synthesis.
    /// </summary>
    public static InvariantTemplates.LoopContext CreateLoopContext(
        BoundWhileStatement loop,
        WhileLoopInfo? loopInfo,
        TransitionInfo? transition)
    {
        var modifiedVars = new List<string>();
        var readVars = new List<string>();
        var arrayVars = new List<string>();

        foreach (var stmt in loop.Body)
        {
            CollectVariables(stmt, modifiedVars, readVars, arrayVars);
        }

        return new InvariantTemplates.LoopContext
        {
            LoopVariable = loopInfo?.LoopVariable,
            LowerBound = loopInfo?.LowerBound,
            UpperBound = loopInfo?.UpperBound,
            ModifiedVariables = modifiedVars.Distinct().ToList(),
            ReadVariables = readVars.Distinct().ToList(),
            ArrayVariables = arrayVars.Distinct().ToList(),
            ConditionExpression = GetConditionString(loop.Condition)
        };
    }

    private static void CollectVariables(
        BoundStatement stmt,
        List<string> modified,
        List<string> read,
        List<string> arrays)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                modified.Add(bind.Variable.Name);
                if (bind.Initializer != null)
                    CollectReadVariables(bind.Initializer, read, arrays);
                break;

            case BoundCallStatement call:
                foreach (var arg in call.Arguments)
                    CollectReadVariables(arg, read, arrays);
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                    CollectReadVariables(ret.Expression, read, arrays);
                break;

            case BoundIfStatement ifStmt:
                CollectReadVariables(ifStmt.Condition, read, arrays);
                foreach (var s in ifStmt.ThenBody)
                    CollectVariables(s, modified, read, arrays);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    CollectReadVariables(elseIf.Condition, read, arrays);
                    foreach (var s in elseIf.Body)
                        CollectVariables(s, modified, read, arrays);
                }
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        CollectVariables(s, modified, read, arrays);
                break;

            case BoundWhileStatement whileStmt:
                CollectReadVariables(whileStmt.Condition, read, arrays);
                foreach (var s in whileStmt.Body)
                    CollectVariables(s, modified, read, arrays);
                break;

            case BoundForStatement forStmt:
                modified.Add(forStmt.LoopVariable.Name);
                CollectReadVariables(forStmt.From, read, arrays);
                CollectReadVariables(forStmt.To, read, arrays);
                if (forStmt.Step != null)
                    CollectReadVariables(forStmt.Step, read, arrays);
                foreach (var s in forStmt.Body)
                    CollectVariables(s, modified, read, arrays);
                break;
        }
    }

    private static void CollectReadVariables(
        BoundExpression expr,
        List<string> read,
        List<string> arrays)
    {
        switch (expr)
        {
            case BoundVariableExpression varExpr:
                read.Add(varExpr.Variable.Name);
                break;

            case BoundBinaryExpression binExpr:
                CollectReadVariables(binExpr.Left, read, arrays);
                CollectReadVariables(binExpr.Right, read, arrays);
                break;

            case BoundUnaryExpression unaryExpr:
                CollectReadVariables(unaryExpr.Operand, read, arrays);
                break;

            case BoundCallExpression callExpr:
                // Heuristic: calls with [] or .get might be array access
                if (callExpr.Target.Contains("[]") || callExpr.Target.Contains(".get"))
                {
                    var arrayName = callExpr.Target.Split(new[] { '[', '.' })[0];
                    if (!string.IsNullOrEmpty(arrayName))
                        arrays.Add(arrayName);
                }
                foreach (var arg in callExpr.Arguments)
                    CollectReadVariables(arg, read, arrays);
                break;
        }
    }

    private static string? GetConditionString(BoundExpression condition)
    {
        return condition switch
        {
            BoundBinaryExpression binExpr => GetBinaryConditionString(binExpr),
            BoundVariableExpression varExpr => varExpr.Variable.Name,
            _ => null
        };
    }

    private static string? GetBinaryConditionString(BoundBinaryExpression binExpr)
    {
        var left = GetConditionString(binExpr.Left);
        var right = GetConditionString(binExpr.Right);
        var op = binExpr.Operator switch
        {
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterOrEqual => ">=",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.And => "&&",
            BinaryOperator.Or => "||",
            _ => "?"
        };

        if (left != null && right != null)
            return $"{left} {op} {right}";

        // For literals
        if (binExpr.Left is BoundIntLiteral leftInt)
            left = leftInt.Value.ToString();
        if (binExpr.Right is BoundIntLiteral rightInt)
            right = rightInt.Value.ToString();

        if (left != null && right != null)
            return $"{left} {op} {right}";

        return null;
    }
}
