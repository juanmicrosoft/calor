using Calor.Compiler.Ast;
using Calor.Compiler.Binding;

namespace Calor.Compiler.Analysis.Dataflow;

/// <summary>
/// Helper methods for analyzing bound nodes in dataflow analyses.
/// </summary>
public static class BoundNodeHelpers
{
    /// <summary>
    /// Enumerates a bound node and every structurally retained descendant.
    /// </summary>
    public static IEnumerable<BoundNode> DescendantsAndSelf(BoundNode? node)
    {
        if (node == null)
            yield break;

        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    /// <summary>
    /// Gets the nearest child expressions below a node, traversing through
    /// structural helper nodes such as match cases, patterns, and initializers.
    /// </summary>
    public static IEnumerable<BoundExpression> GetChildExpressions(BoundNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is BoundExpression expression)
            {
                yield return expression;
                continue;
            }

            foreach (var nested in GetChildExpressions(child))
                yield return nested;
        }
    }

    /// <summary>
    /// Gets all variable references (uses) in an expression.
    /// </summary>
    public static IEnumerable<VariableSymbol> GetUsedVariables(BoundExpression? expression)
    {
        if (expression == null)
            return Array.Empty<VariableSymbol>();

        return GetUsedVariablesCore(expression, new HashSet<string>(StringComparer.Ordinal));
    }

    private static IEnumerable<VariableSymbol> GetUsedVariablesCore(
        BoundNode node,
        HashSet<string> locallyBound)
    {
        if (node is BoundVariableExpression variable)
        {
            if (!locallyBound.Contains(variable.Variable.IdentityKey))
                yield return variable.Variable;
            yield break;
        }

        var nestedLocals = locallyBound;
        if (node is BoundLambdaExpression lambda)
        {
            nestedLocals = new HashSet<string>(locallyBound, StringComparer.Ordinal);
            nestedLocals.UnionWith(lambda.Parameters.Select(parameter => parameter.IdentityKey));
        }
        else if (node is BoundQuantifierExpression quantifier)
        {
            nestedLocals = new HashSet<string>(locallyBound, StringComparer.Ordinal);
            nestedLocals.UnionWith(quantifier.BoundVariables.Select(variable => variable.IdentityKey));
        }

        foreach (var child in node.ChildNodes)
        {
            foreach (var used in GetUsedVariablesCore(child, nestedLocals))
                yield return used;
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
            BoundAssignmentStatement assign when assign.Target is BoundVariableExpression varExpr => varExpr.Variable,
            BoundAssignmentStatement assign when assign.Target is BoundFieldAccessExpression { ResolvedField: not null } field =>
                field.ResolvedField,
            BoundCompoundAssignment compound when compound.Target is BoundVariableExpression varExpr => varExpr.Variable,
            BoundCompoundAssignment compound when compound.Target is BoundFieldAccessExpression { ResolvedField: not null } field =>
                field.ResolvedField,
            BoundForeachStatement forEach => forEach.LoopVariable,
            BoundUsingStatement usingStmt => usingStmt.Resource,
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

            case BoundAssignmentStatement assign:
                // For simple variable targets (x = expr), the target is defined, not used.
                // Only yield uses from sub-expressions of the target (e.g., this.field → yield this).
                if (assign.Target is not BoundVariableExpression)
                    foreach (var v in GetUsedVariables(assign.Target))
                        yield return v;
                foreach (var v in GetUsedVariables(assign.Value))
                    yield return v;
                break;

            case BoundCompoundAssignment compound:
                foreach (var v in GetUsedVariables(compound.Target))
                    yield return v;
                foreach (var v in GetUsedVariables(compound.Value))
                    yield return v;
                break;

            case BoundForeachStatement forEach:
                foreach (var v in GetUsedVariables(forEach.Collection))
                    yield return v;
                break;

            case BoundUsingStatement usingStmt:
                foreach (var v in GetUsedVariables(usingStmt.ResourceExpression))
                    yield return v;
                break;

            case BoundThrowStatement throwStmt:
                foreach (var v in GetUsedVariables(throwStmt.Expression))
                    yield return v;
                break;

            case BoundDoWhileStatement doWhile:
                foreach (var v in GetUsedVariables(doWhile.Condition))
                    yield return v;
                break;

            case BoundExpressionStatement exprStmt:
                foreach (var v in GetUsedVariables(exprStmt.Expression))
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
        return defined != null && SameSymbol(defined, variable);
    }

    public static bool SameSymbol(Symbol left, Symbol right)
    {
        if (!left.Id.IsNone && !right.Id.IsNone)
            return left.Id == right.Id;
        return ReferenceEquals(left, right)
            || string.Equals(left.Name, right.Name, StringComparison.Ordinal);
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

            case BoundForeachStatement forEach:
                yield return forEach.LoopVariable;
                foreach (var s in forEach.Body)
                    foreach (var v in GetAllDefinedVariablesInStatement(s))
                        yield return v;
                break;

            case BoundDoWhileStatement doWhile:
                foreach (var s in doWhile.Body)
                    foreach (var v in GetAllDefinedVariablesInStatement(s))
                        yield return v;
                break;

            case BoundUsingStatement usingStmt:
                if (usingStmt.Resource != null)
                    yield return usingStmt.Resource;
                foreach (var s in usingStmt.Body)
                    foreach (var v in GetAllDefinedVariablesInStatement(s))
                        yield return v;
                break;

            case BoundTryStatement tryStmt:
                foreach (var s in tryStmt.TryBody)
                    foreach (var v in GetAllDefinedVariablesInStatement(s))
                        yield return v;
                foreach (var catchClause in tryStmt.CatchClauses)
                    foreach (var s in catchClause.Body)
                        foreach (var v in GetAllDefinedVariablesInStatement(s))
                            yield return v;
                if (tryStmt.FinallyBody != null)
                    foreach (var s in tryStmt.FinallyBody)
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
        divisionExpr = DescendantsAndSelf(expression)
            .OfType<BoundBinaryExpression>()
            .FirstOrDefault(binary =>
                binary.Operator is BinaryOperator.Divide or BinaryOperator.Modulo);
        return divisionExpr != null;
    }

    /// <summary>
    /// Checks if an expression contains array access.
    /// </summary>
    public static bool ContainsArrayAccess(BoundExpression? expression, out BoundExpression? arrayExpr, out BoundExpression? indexExpr)
    {
        arrayExpr = null;
        indexExpr = null;

        var access = DescendantsAndSelf(expression)
            .OfType<BoundArrayAccessExpression>()
            .FirstOrDefault();
        if (access == null)
            return false;

        arrayExpr = access.Array;
        indexExpr = access.Indices.FirstOrDefault();
        return true;
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
            BoundDecimalLiteral decimalLiteral => decimalLiteral.Value == 0m,
            _ => false
        };
    }

    /// <summary>
    /// Checks if an expression is a constant (literal).
    /// </summary>
    public static bool IsConstant(BoundExpression? expression)
    {
        return expression is BoundIntLiteral
            or BoundFloatLiteral
            or BoundDecimalLiteral
            or BoundBoolLiteral
            or BoundStringLiteral;
    }

    /// <summary>
    /// Gets the integer value if the expression is an integer literal.
    /// </summary>
    public static long? GetIntLiteralValue(BoundExpression? expression)
    {
        return expression is BoundIntLiteral intLit ? intLit.Value : null;
    }
}
