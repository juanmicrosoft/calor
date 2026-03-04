using Calor.Compiler.Ast;
using Calor.Compiler.Binding;

namespace Calor.Compiler.Analysis.Dataflow;

/// <summary>
/// Helper methods for analyzing bound nodes in dataflow analyses.
/// </summary>
public static class BoundNodeHelpers
{
    /// <summary>
    /// Gets all variable references (uses) in an expression.
    /// </summary>
    public static IEnumerable<VariableSymbol> GetUsedVariables(BoundExpression? expression)
    {
        if (expression == null)
            yield break;

        switch (expression)
        {
            case BoundVariableExpression varExpr:
                yield return varExpr.Variable;
                break;

            case BoundBinaryExpression binExpr:
                foreach (var v in GetUsedVariables(binExpr.Left))
                    yield return v;
                foreach (var v in GetUsedVariables(binExpr.Right))
                    yield return v;
                break;

            case BoundUnaryExpression unaryExpr:
                foreach (var v in GetUsedVariables(unaryExpr.Operand))
                    yield return v;
                break;

            case BoundCallExpression callExpr:
                foreach (var arg in callExpr.Arguments)
                    foreach (var v in GetUsedVariables(arg))
                        yield return v;
                break;
        }
    }

    /// <summary>
    /// Gets the variable being defined (if any) in a statement.
    /// </summary>
    public static VariableSymbol? GetDefinedVariable(BoundStatement statement)
    {
        return statement switch
        {
            BoundBindStatement bind => bind.Variable,
            _ => null
        };
    }

    /// <summary>
    /// Gets all variables used in a statement (excluding the defined variable).
    /// </summary>
    public static IEnumerable<VariableSymbol> GetUsedVariables(BoundStatement statement)
    {
        switch (statement)
        {
            case BoundBindStatement bind:
                foreach (var v in GetUsedVariables(bind.Initializer))
                    yield return v;
                break;

            case BoundCallStatement call:
                foreach (var arg in call.Arguments)
                    foreach (var v in GetUsedVariables(arg))
                        yield return v;
                break;

            case BoundReturnStatement ret:
                foreach (var v in GetUsedVariables(ret.Expression))
                    yield return v;
                break;

            case BoundIfStatement ifStmt:
                foreach (var v in GetUsedVariables(ifStmt.Condition))
                    yield return v;
                break;

            case BoundWhileStatement whileStmt:
                foreach (var v in GetUsedVariables(whileStmt.Condition))
                    yield return v;
                break;

            case BoundForStatement forStmt:
                foreach (var v in GetUsedVariables(forStmt.From))
                    yield return v;
                foreach (var v in GetUsedVariables(forStmt.To))
                    yield return v;
                if (forStmt.Step != null)
                    foreach (var v in GetUsedVariables(forStmt.Step))
                        yield return v;
                break;
        }
    }

    /// <summary>
    /// Checks if a statement potentially modifies a variable.
    /// </summary>
    public static bool DefinesVariable(BoundStatement statement, VariableSymbol variable)
    {
        var defined = GetDefinedVariable(statement);
        return defined != null && defined.Name == variable.Name;
    }

    /// <summary>
    /// Gets all variables defined in a function.
    /// </summary>
    public static IEnumerable<VariableSymbol> GetAllDefinedVariables(BoundFunction function)
    {
        foreach (var stmt in function.Body)
        {
            foreach (var v in GetAllDefinedVariablesInStatement(stmt))
                yield return v;
        }
    }

    private static IEnumerable<VariableSymbol> GetAllDefinedVariablesInStatement(BoundStatement statement)
    {
        switch (statement)
        {
            case BoundBindStatement bind:
                yield return bind.Variable;
                break;

            case BoundIfStatement ifStmt:
                foreach (var s in ifStmt.ThenBody)
                    foreach (var v in GetAllDefinedVariablesInStatement(s))
                        yield return v;
                foreach (var elseIf in ifStmt.ElseIfClauses)
                    foreach (var s in elseIf.Body)
                        foreach (var v in GetAllDefinedVariablesInStatement(s))
                            yield return v;
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        foreach (var v in GetAllDefinedVariablesInStatement(s))
                            yield return v;
                break;

            case BoundWhileStatement whileStmt:
                foreach (var s in whileStmt.Body)
                    foreach (var v in GetAllDefinedVariablesInStatement(s))
                        yield return v;
                break;

            case BoundForStatement forStmt:
                yield return forStmt.LoopVariable;
                foreach (var s in forStmt.Body)
                    foreach (var v in GetAllDefinedVariablesInStatement(s))
                        yield return v;
                break;
        }
    }

    /// <summary>
    /// Checks if an expression contains a division operation.
    /// </summary>
    public static bool ContainsDivision(BoundExpression? expression, out BoundBinaryExpression? divisionExpr)
    {
        divisionExpr = null;
        if (expression == null)
            return false;

        switch (expression)
        {
            case BoundBinaryExpression binExpr:
                if (binExpr.Operator == BinaryOperator.Divide || binExpr.Operator == BinaryOperator.Modulo)
                {
                    divisionExpr = binExpr;
                    return true;
                }
                if (ContainsDivision(binExpr.Left, out divisionExpr))
                    return true;
                if (ContainsDivision(binExpr.Right, out divisionExpr))
                    return true;
                break;

            case BoundUnaryExpression unaryExpr:
                return ContainsDivision(unaryExpr.Operand, out divisionExpr);

            case BoundCallExpression callExpr:
                foreach (var arg in callExpr.Arguments)
                    if (ContainsDivision(arg, out divisionExpr))
                        return true;
                break;
        }

        return false;
    }

    /// <summary>
    /// Checks if an expression contains array access.
    /// </summary>
    public static bool ContainsArrayAccess(BoundExpression? expression, out BoundExpression? arrayExpr, out BoundExpression? indexExpr)
    {
        arrayExpr = null;
        indexExpr = null;

        // Note: BoundNodes don't have BoundArrayAccessExpression yet
        // This is a placeholder for when it's added
        if (expression == null)
            return false;

        switch (expression)
        {
            case BoundBinaryExpression binExpr:
                if (ContainsArrayAccess(binExpr.Left, out arrayExpr, out indexExpr))
                    return true;
                return ContainsArrayAccess(binExpr.Right, out arrayExpr, out indexExpr);

            case BoundUnaryExpression unaryExpr:
                return ContainsArrayAccess(unaryExpr.Operand, out arrayExpr, out indexExpr);

            case BoundCallExpression callExpr:
                foreach (var arg in callExpr.Arguments)
                    if (ContainsArrayAccess(arg, out arrayExpr, out indexExpr))
                        return true;
                break;
        }

        return false;
    }

    /// <summary>
    /// Extracts the divisor expression from a division operation.
    /// </summary>
    public static BoundExpression? GetDivisor(BoundBinaryExpression divExpr)
    {
        if (divExpr.Operator == BinaryOperator.Divide || divExpr.Operator == BinaryOperator.Modulo)
            return divExpr.Right;
        return null;
    }

    /// <summary>
    /// Checks if an expression is a literal zero.
    /// </summary>
    public static bool IsLiteralZero(BoundExpression? expression)
    {
        return expression switch
        {
            BoundIntLiteral intLit => intLit.Value == 0,
            BoundFloatLiteral floatLit => floatLit.Value == 0.0,
            _ => false
        };
    }

    /// <summary>
    /// Checks if an expression is a constant (literal).
    /// </summary>
    public static bool IsConstant(BoundExpression? expression)
    {
        return expression is BoundIntLiteral or BoundFloatLiteral or BoundBoolLiteral or BoundStringLiteral;
    }

    /// <summary>
    /// Gets the integer value if the expression is an integer literal.
    /// </summary>
    public static long? GetIntLiteralValue(BoundExpression? expression)
    {
        return expression is BoundIntLiteral intLit ? intLit.Value : null;
    }
}
