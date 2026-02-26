using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// Collects flow-sensitive facts from the AST that can be used as Z3 assumptions
/// when verifying obligations. Extracts loop bounds, if-guard conditions, and
/// inline refinement predicates.
/// </summary>
public sealed class FactCollector
{
    /// <summary>
    /// Collected facts as expression nodes suitable for Z3 translation.
    /// </summary>
    public List<ExpressionNode> Facts { get; } = new();

    /// <summary>
    /// Collects facts from a function's body and parameter refinements that are relevant
    /// to proving obligations within the given statements.
    /// Inline refinements are added as facts for non-RefinementEntry obligations
    /// (e.g., IndexBounds can use parameter refinements as assumptions).
    /// </summary>
    public void CollectFromFunction(FunctionNode func)
    {
        // Collect parameter inline refinements as facts
        // These serve as assumptions for IndexBounds and other obligations
        foreach (var param in func.Parameters)
        {
            if (param.InlineRefinement != null)
            {
                Facts.Add(SubstituteSelfRef(param.InlineRefinement.Predicate, param.Name));
            }
        }

        CollectFromStatements(func.Body);
    }

    /// <summary>
    /// Collects facts from a list of statements.
    /// </summary>
    public void CollectFromStatements(IReadOnlyList<StatementNode> statements)
    {
        foreach (var stmt in statements)
        {
            CollectFromStatement(stmt);
        }
    }

    private void CollectFromStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case ForStatementNode forStmt:
                CollectFromForLoop(forStmt);
                break;

            case WhileStatementNode whileStmt:
                // The while condition holds inside the loop body
                Facts.Add(whileStmt.Condition);
                CollectFromStatements(whileStmt.Body);
                break;

            case IfStatementNode ifStmt:
                // The if-condition is a fact within the then-body.
                // We add it as a general assumption since obligations inside
                // the then-body are only reachable when the condition holds.
                Facts.Add(ifStmt.Condition);
                CollectFromStatements(ifStmt.ThenBody);
                // ElseIf clauses have their own conditions
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    Facts.Add(elseIf.Condition);
                    CollectFromStatements(elseIf.Body);
                }
                if (ifStmt.ElseBody != null)
                    CollectFromStatements(ifStmt.ElseBody);
                break;

            case DoWhileStatementNode doWhile:
                CollectFromStatements(doWhile.Body);
                break;

            case ForeachStatementNode foreach_:
                CollectFromStatements(foreach_.Body);
                break;

            case TryStatementNode tryStmt:
                CollectFromStatements(tryStmt.TryBody);
                break;
        }
    }

    /// <summary>
    /// Extracts loop bounds from a for-loop.
    /// §L{id:i:from:to:step} yields facts: i >= from AND i < to
    /// </summary>
    private void CollectFromForLoop(ForStatementNode forStmt)
    {
        var dummySpan = new TextSpan(0, 0, 1, 1);
        var loopVar = new ReferenceNode(dummySpan, forStmt.VariableName);

        // Fact: loopVar >= from
        var geFrom = new BinaryOperationNode(dummySpan, BinaryOperator.GreaterOrEqual, loopVar, forStmt.From);
        Facts.Add(geFrom);

        // Fact: loopVar < to
        var ltTo = new BinaryOperationNode(dummySpan, BinaryOperator.LessThan, loopVar, forStmt.To);
        Facts.Add(ltTo);

        // Recurse into loop body
        CollectFromStatements(forStmt.Body);
    }

    /// <summary>
    /// Substitutes SelfRefNode (#) with a ReferenceNode for the given variable name.
    /// Returns a new expression tree with substitutions applied.
    /// </summary>
    public static ExpressionNode SubstituteSelfRefStatic(ExpressionNode expr, string variableName)
        => SubstituteSelfRef(expr, variableName);

    private static ExpressionNode SubstituteSelfRef(ExpressionNode expr, string variableName)
    {
        if (expr is SelfRefNode)
        {
            return new ReferenceNode(expr.Span, variableName);
        }

        if (expr is BinaryOperationNode binOp)
        {
            var left = SubstituteSelfRef(binOp.Left, variableName);
            var right = SubstituteSelfRef(binOp.Right, variableName);
            if (!ReferenceEquals(left, binOp.Left) || !ReferenceEquals(right, binOp.Right))
                return new BinaryOperationNode(binOp.Span, binOp.Operator, left, right);
            return binOp;
        }

        if (expr is UnaryOperationNode unOp)
        {
            var operand = SubstituteSelfRef(unOp.Operand, variableName);
            if (operand != unOp.Operand)
                return new UnaryOperationNode(unOp.Span, unOp.Operator, operand);
            return unOp;
        }

        return expr;
    }
}
