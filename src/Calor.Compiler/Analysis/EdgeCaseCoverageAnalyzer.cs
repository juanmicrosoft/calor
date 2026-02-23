using Calor.Compiler.Ast;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Result of edge case coverage analysis on a Calor module.
/// Counts defensive programming patterns in the AST.
/// </summary>
public sealed class EdgeCaseCoverageResult
{
    /// <summary>
    /// Number of if statements where the then-body contains only a return statement (early return guard).
    /// </summary>
    public int EarlyReturnGuards { get; init; }

    /// <summary>
    /// Number of if conditions that compare against literal 0 or 1 (boundary checks).
    /// </summary>
    public int BoundaryConditionChecks { get; init; }

    /// <summary>
    /// Number of if statements that have an else branch.
    /// </summary>
    public int ElseBranches { get; init; }

    /// <summary>
    /// Number of match expressions/statements with wildcard, None, or Err patterns (exhaustive).
    /// </summary>
    public int ExhaustiveMatches { get; init; }

    /// <summary>
    /// Number of functions that have at least one precondition or postcondition.
    /// </summary>
    public int FunctionsWithContracts { get; init; }

    /// <summary>
    /// Total number of functions in the module.
    /// </summary>
    public int TotalFunctions { get; init; }

    /// <summary>
    /// Normalized edge case coverage score (0.0 to 1.0).
    /// Calculated as total evidence / TotalFunctions, capped at 1.0.
    /// </summary>
    public double CoverageScore
    {
        get
        {
            if (TotalFunctions == 0) return 0.0;
            var evidence = EarlyReturnGuards + BoundaryConditionChecks +
                           ElseBranches + ExhaustiveMatches + FunctionsWithContracts;
            return Math.Min((double)evidence / TotalFunctions, 1.0);
        }
    }
}

/// <summary>
/// AST-walking analysis pass that counts evidence of edge case handling
/// in a parsed Calor module. Unlike bug pattern checkers (which find bugs),
/// this counts defensive programming patterns.
/// </summary>
public static class EdgeCaseCoverageAnalyzer
{
    /// <summary>
    /// Analyzes a parsed Calor module for edge case coverage patterns.
    /// </summary>
    public static EdgeCaseCoverageResult Analyze(ModuleNode module)
    {
        if (module == null) throw new ArgumentNullException(nameof(module));

        var earlyReturnGuards = 0;
        var boundaryConditionChecks = 0;
        var elseBranches = 0;
        var exhaustiveMatches = 0;
        var functionsWithContracts = 0;
        var totalFunctions = 0;

        foreach (var function in module.Functions)
        {
            totalFunctions++;

            if (function.Preconditions.Count > 0 || function.Postconditions.Count > 0)
                functionsWithContracts++;

            foreach (var stmt in function.Body)
                AnalyzeStatement(stmt, ref earlyReturnGuards, ref boundaryConditionChecks,
                    ref elseBranches, ref exhaustiveMatches);
        }

        // Also check functions inside classes
        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                totalFunctions++;

                if (method.Preconditions.Count > 0 || method.Postconditions.Count > 0)
                    functionsWithContracts++;

                foreach (var stmt in method.Body)
                    AnalyzeStatement(stmt, ref earlyReturnGuards, ref boundaryConditionChecks,
                        ref elseBranches, ref exhaustiveMatches);
            }
        }

        return new EdgeCaseCoverageResult
        {
            EarlyReturnGuards = earlyReturnGuards,
            BoundaryConditionChecks = boundaryConditionChecks,
            ElseBranches = elseBranches,
            ExhaustiveMatches = exhaustiveMatches,
            FunctionsWithContracts = functionsWithContracts,
            TotalFunctions = totalFunctions
        };
    }

    private static void AnalyzeStatement(
        StatementNode stmt,
        ref int earlyReturnGuards,
        ref int boundaryConditionChecks,
        ref int elseBranches,
        ref int exhaustiveMatches)
    {
        switch (stmt)
        {
            case IfStatementNode ifStmt:
                // Check for early return guard: if body contains only a return
                if (ifStmt.ThenBody.Count == 1 && ifStmt.ThenBody[0] is ReturnStatementNode)
                    earlyReturnGuards++;

                // Check for boundary condition: comparison against literal 0 or 1
                if (IsBoundaryCondition(ifStmt.Condition))
                    boundaryConditionChecks++;

                // Check for else branch completeness
                if (ifStmt.ElseBody != null && ifStmt.ElseBody.Count > 0)
                    elseBranches++;

                // Also count else-if clauses as evidence of completeness
                if (ifStmt.ElseIfClauses.Count > 0)
                    elseBranches++;

                // Recurse into then body
                foreach (var s in ifStmt.ThenBody)
                    AnalyzeStatement(s, ref earlyReturnGuards, ref boundaryConditionChecks,
                        ref elseBranches, ref exhaustiveMatches);

                // Recurse into else-if bodies
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    if (IsBoundaryCondition(elseIf.Condition))
                        boundaryConditionChecks++;
                    foreach (var s in elseIf.Body)
                        AnalyzeStatement(s, ref earlyReturnGuards, ref boundaryConditionChecks,
                            ref elseBranches, ref exhaustiveMatches);
                }

                // Recurse into else body
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        AnalyzeStatement(s, ref earlyReturnGuards, ref boundaryConditionChecks,
                            ref elseBranches, ref exhaustiveMatches);
                break;

            case MatchStatementNode matchStmt:
                if (HasExhaustivePattern(matchStmt.Cases))
                    exhaustiveMatches++;
                foreach (var matchCase in matchStmt.Cases)
                    foreach (var s in matchCase.Body)
                        AnalyzeStatement(s, ref earlyReturnGuards, ref boundaryConditionChecks,
                            ref elseBranches, ref exhaustiveMatches);
                break;

            // Check for match expressions inside return/bind statements
            case ReturnStatementNode retStmt:
                if (retStmt.Expression is MatchExpressionNode matchExpr)
                    if (HasExhaustivePattern(matchExpr.Cases))
                        exhaustiveMatches++;
                break;

            case BindStatementNode bindStmt:
                if (bindStmt.Initializer is MatchExpressionNode bindMatchExpr)
                    if (HasExhaustivePattern(bindMatchExpr.Cases))
                        exhaustiveMatches++;
                break;

            case ForStatementNode forStmt:
                foreach (var s in forStmt.Body)
                    AnalyzeStatement(s, ref earlyReturnGuards, ref boundaryConditionChecks,
                        ref elseBranches, ref exhaustiveMatches);
                break;

            case WhileStatementNode whileStmt:
                foreach (var s in whileStmt.Body)
                    AnalyzeStatement(s, ref earlyReturnGuards, ref boundaryConditionChecks,
                        ref elseBranches, ref exhaustiveMatches);
                break;

            case DoWhileStatementNode doWhileStmt:
                foreach (var s in doWhileStmt.Body)
                    AnalyzeStatement(s, ref earlyReturnGuards, ref boundaryConditionChecks,
                        ref elseBranches, ref exhaustiveMatches);
                break;
        }
    }

    /// <summary>
    /// Checks if a condition expression compares against literal 0 or 1 (boundary check).
    /// Matches patterns like (== n 0), (&lt;= n 1), (&lt; n 0), (== x 1), etc.
    /// </summary>
    private static bool IsBoundaryCondition(ExpressionNode condition)
    {
        if (condition is not BinaryOperationNode binOp)
            return false;

        // Only comparison operators
        if (binOp.Operator is not (BinaryOperator.Equal or BinaryOperator.NotEqual or
            BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual))
            return false;

        // Check if either side is a literal 0 or 1
        return IsSmallLiteral(binOp.Left) || IsSmallLiteral(binOp.Right);
    }

    private static bool IsSmallLiteral(ExpressionNode expr)
    {
        return expr is IntLiteralNode lit && (lit.Value == 0 || lit.Value == 1);
    }

    private static bool HasExhaustivePattern(IReadOnlyList<MatchCaseNode> cases)
    {
        foreach (var c in cases)
        {
            if (c.Pattern is WildcardPatternNode or NonePatternNode or ErrPatternNode)
                return true;
        }
        return false;
    }
}
