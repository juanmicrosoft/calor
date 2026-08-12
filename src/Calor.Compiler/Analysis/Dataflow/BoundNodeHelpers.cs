using System.Runtime.CompilerServices;
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
    /// Gets expressions evaluated directly by a statement or helper node without
    /// descending into nested statements, which are analyzed at their own CFG point.
    /// </summary>
    public static IEnumerable<BoundExpression> GetImmediateExpressions(BoundNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is BoundExpression expression)
            {
                yield return expression;
            }
            else if (child is not BoundStatement)
            {
                foreach (var nested in GetImmediateExpressions(child))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Gets directly nested statements through structural helper nodes such as
    /// catch clauses and match cases, without entering expression-owned bodies.
    /// </summary>
    public static IEnumerable<BoundStatement> GetImmediateStatements(BoundNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is BoundStatement statement)
            {
                yield return statement;
            }
            else if (child is not BoundExpression)
            {
                foreach (var nested in GetImmediateStatements(child))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Gets all variable references (uses) in an expression.
    /// </summary>
    public static IEnumerable<VariableSymbol> GetUsedVariables(BoundExpression? expression)
    {
        if (expression == null)
            return Array.Empty<VariableSymbol>();

        return GetUsedVariablesCore(
                expression,
                new HashSet<VariableSymbol>(VariableSymbolIdentityComparer.Instance))
            .Distinct(VariableSymbolIdentityComparer.Instance);
    }

    private static IEnumerable<VariableSymbol> GetUsedVariablesCore(
        BoundNode node,
        HashSet<VariableSymbol> locallyBound)
    {
        if (node is BoundVariableExpression variable)
        {
            if (!locallyBound.Contains(variable.Variable))
                yield return variable.Variable;
            yield break;
        }

        if (node is BoundCallExpression { ReceiverSymbol: not null } call
            && !locallyBound.Contains(call.ReceiverSymbol))
        {
            yield return call.ReceiverSymbol;
        }

        var nestedLocals = locallyBound;
        if (node is BoundLambdaExpression lambda)
        {
            nestedLocals = new HashSet<VariableSymbol>(
                locallyBound,
                VariableSymbolIdentityComparer.Instance);
            nestedLocals.UnionWith(lambda.Parameters);
            nestedLocals.UnionWith(GetDeclaredVariables(lambda));
        }
        else if (node is BoundQuantifierExpression quantifier)
        {
            nestedLocals = new HashSet<VariableSymbol>(
                locallyBound,
                VariableSymbolIdentityComparer.Instance);
            nestedLocals.UnionWith(quantifier.BoundVariables);
        }

        foreach (var child in node.ChildNodes)
        {
            foreach (var used in GetUsedVariablesCore(child, nestedLocals))
                yield return used;
        }
    }

    private static IEnumerable<VariableSymbol> GetDeclaredVariables(BoundNode node)
    {
        foreach (var descendant in DescendantsAndSelf(node))
        {
            switch (descendant)
            {
                case BoundBindStatement bind:
                    yield return bind.Variable;
                    break;
                case BoundForStatement forStatement:
                    yield return forStatement.LoopVariable;
                    break;
                case BoundForeachStatement forEach:
                    yield return forEach.LoopVariable;
                    break;
                case BoundUsingStatement { Resource: not null } usingStatement:
                    yield return usingStatement.Resource;
                    break;
                case BoundCatchClause { ExceptionVariable: not null } catchClause:
                    yield return catchClause.ExceptionVariable;
                    break;
            }
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

    public static VariableSymbol? GetDefinedVariable(SyntheticOperation operation) =>
        operation.DefinedVariable;

    /// <summary>
    /// Gets all variables used in a statement (excluding the defined variable).
    /// </summary>
    public static IEnumerable<VariableSymbol> GetUsedVariables(BoundStatement statement)
    {
        var seen = new HashSet<VariableSymbol>(VariableSymbolIdentityComparer.Instance);
        if (statement is BoundCallStatement { ReceiverSymbol: not null } call
            && seen.Add(call.ReceiverSymbol))
        {
            yield return call.ReceiverSymbol;
        }

        if (statement is BoundAssignmentStatement
            {
                Target: BoundVariableExpression,
            } assignment)
        {
            foreach (var variable in GetUsedVariables(assignment.Value))
            {
                if (seen.Add(variable))
                    yield return variable;
            }
            yield break;
        }

        foreach (var expression in GetImmediateExpressions(statement))
        {
            foreach (var variable in GetUsedVariables(expression))
            {
                if (seen.Add(variable))
                    yield return variable;
            }
        }
    }

    public static IEnumerable<VariableSymbol> GetUsedVariables(SyntheticOperation operation)
    {
        var seen = new HashSet<VariableSymbol>(VariableSymbolIdentityComparer.Instance);
        if (operation.ReadsDefinedVariable
            && operation.DefinedVariable != null
            && seen.Add(operation.DefinedVariable))
        {
            yield return operation.DefinedVariable;
        }

        foreach (var variable in GetUsedVariables(operation.Expression))
        {
            if (seen.Add(variable))
                yield return variable;
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
        return ReferenceEquals(left, right);
    }

    /// <summary>
    /// Gets all variables defined in a function.
    /// </summary>
    public static IEnumerable<VariableSymbol> GetAllDefinedVariables(BoundFunction function)
    {
        foreach (var node in DescendantsAndSelf(function))
        {
            switch (node)
            {
                case BoundBindStatement bind:
                    yield return bind.Variable;
                    break;
                case BoundForStatement forStatement:
                    yield return forStatement.LoopVariable;
                    break;
                case BoundForeachStatement forEach:
                    yield return forEach.LoopVariable;
                    break;
                case BoundUsingStatement { Resource: not null } usingStatement:
                    yield return usingStatement.Resource;
                    break;
                case BoundCatchClause { ExceptionVariable: not null } catchClause:
                    yield return catchClause.ExceptionVariable;
                    break;
            }
        }
    }

    public static IEnumerable<BoundNode> GetAnalysisIncompleteNodes(BoundNode node)
    {
        return DescendantsAndSelf(node)
            .Where(descendant =>
                descendant is BoundIncompleteExpression
                    or BoundUnsupportedExpression
                    or BoundInteropExpression
                    or BoundUnsupportedStatement);
    }

    private sealed class VariableSymbolIdentityComparer : IEqualityComparer<VariableSymbol>
    {
        public static VariableSymbolIdentityComparer Instance { get; } = new();

        public bool Equals(VariableSymbol? x, VariableSymbol? y) =>
            x != null && y != null && SameSymbol(x, y);

        public int GetHashCode(VariableSymbol obj) =>
            obj.Id.IsNone
                ? RuntimeHelpers.GetHashCode(obj)
                : obj.Id.GetHashCode();
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
        // #762 B3: the STRUCTURAL array-access nodes this helper was named for finally
        // exist — match them first (the B3 review found the old "placeholder for when
        // it's added" comment left false by the family PR that added them).
        if (expression is BoundArrayAccess structural)
        {
            arrayExpr = structural.Array;
            indexExpr = structural.Index;
            return true;
        }
        if (expression is BoundMultiDimArrayAccess multi)
        {
            arrayExpr = multi.Array;
            indexExpr = multi.Indices.Count > 0 ? multi.Indices[0] : null;
            return true;
        }
        return ContainsArrayAccessLegacy(expression, out arrayExpr, out indexExpr);
    }

    private static bool ContainsArrayAccessLegacy(BoundExpression? expression, out BoundExpression? arrayExpr, out BoundExpression? indexExpr)
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
