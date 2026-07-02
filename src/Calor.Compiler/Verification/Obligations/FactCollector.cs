using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// A fact together with the source range it governs. Guard facts (if/while
/// conditions, loop bounds) only hold inside the body they guard, so the
/// solver must not assert them for obligations outside that range.
/// </summary>
public sealed record ScopedFact(ExpressionNode Fact, int ScopeStart, int ScopeEnd)
{
    public static ScopedFact FunctionWide(ExpressionNode fact)
        => new(fact, 0, int.MaxValue);

    public bool AppliesTo(TextSpan span)
        => span.Start >= ScopeStart && span.End <= ScopeEnd;
}

/// <summary>
/// Collects flow-sensitive facts from the AST that can be used as Z3 assumptions
/// when verifying obligations. Extracts loop bounds, if-guard conditions, and
/// inline refinement predicates.
///
/// Facts are scoped to the statement range they dominate: an if-condition holds
/// only inside the then-body, an elseif-condition only inside its own body, a
/// while-condition only inside the loop body. Facts whose variables are rebound
/// inside the governed range are dropped entirely (conservative assignment kill)
/// because the guard may no longer hold at the obligation site.
/// </summary>
public sealed class FactCollector
{
    /// <summary>
    /// Collected facts with the source ranges they govern.
    /// </summary>
    public List<ScopedFact> ScopedFacts { get; } = new();

    /// <summary>
    /// Convenience view of the collected fact expressions (scope-erased).
    /// </summary>
    public IReadOnlyList<ExpressionNode> Facts
        => ScopedFacts.Select(f => f.Fact).ToList();

    /// <summary>
    /// Adds a fact that holds for the whole function (e.g., an indexed-type
    /// constraint), subject to no assignment kill.
    /// </summary>
    public void AddFunctionWideFact(ExpressionNode fact)
        => ScopedFacts.Add(ScopedFact.FunctionWide(fact));

    /// <summary>
    /// Collects facts from a function's body and parameter refinements that are relevant
    /// to proving obligations within the given statements.
    /// Inline refinements are added as facts for non-RefinementEntry obligations
    /// (e.g., IndexBounds can use parameter refinements as assumptions).
    /// </summary>
    public void CollectFromFunction(FunctionNode func)
    {
        // Parameter inline refinements hold on entry for the whole function —
        // unless the body rebinds the parameter name, in which case the
        // refinement may no longer describe the current value.
        var bodyAssigned = CollectAssignedNames(func.Body);
        foreach (var param in func.Parameters)
        {
            if (param.InlineRefinement != null && !bodyAssigned.Contains(param.Name))
            {
                ScopedFacts.Add(ScopedFact.FunctionWide(
                    SubstituteSelfRef(param.InlineRefinement.Predicate, param.Name)));
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
                // The while condition holds on entry to each iteration, but a
                // body that reassigns its variables invalidates it mid-body.
                AddGuardFact(whileStmt.Condition, whileStmt.Body);
                CollectFromStatements(whileStmt.Body);
                break;

            case IfStatementNode ifStmt:
                // Each condition is a fact only within the body it guards.
                AddGuardFact(ifStmt.Condition, ifStmt.ThenBody);
                CollectFromStatements(ifStmt.ThenBody);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    AddGuardFact(elseIf.Condition, elseIf.Body);
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
    /// §L{id:i:from:to:step} yields facts: i >= from AND i < to, scoped to the body.
    /// </summary>
    private void CollectFromForLoop(ForStatementNode forStmt)
    {
        var dummySpan = new TextSpan(0, 0, 1, 1);
        var loopVar = new ReferenceNode(dummySpan, forStmt.VariableName);

        var geFrom = new BinaryOperationNode(dummySpan, BinaryOperator.GreaterOrEqual, loopVar, forStmt.From);
        AddGuardFact(geFrom, forStmt.Body);

        var ltTo = new BinaryOperationNode(dummySpan, BinaryOperator.LessThan, loopVar, forStmt.To);
        AddGuardFact(ltTo, forStmt.Body);

        CollectFromStatements(forStmt.Body);
    }

    /// <summary>
    /// Records a guard fact scoped to the body it governs, unless the body
    /// rebinds a variable the fact mentions (conservative assignment kill).
    /// </summary>
    private void AddGuardFact(ExpressionNode fact, IReadOnlyList<StatementNode> body)
    {
        if (body.Count == 0)
            return;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        CollectReferencedNames(fact, referenced);

        var assigned = CollectAssignedNames(body);
        if (referenced.Overlaps(assigned))
            return;

        var scopeStart = body.Min(s => s.Span.Start);
        var scopeEnd = body.Max(s => s.Span.End);
        ScopedFacts.Add(new ScopedFact(fact, scopeStart, scopeEnd));
    }

    private static void CollectReferencedNames(ExpressionNode expr, HashSet<string> names)
    {
        switch (expr)
        {
            case ReferenceNode reference:
                names.Add(reference.Name);
                break;
            case BinaryOperationNode binOp:
                CollectReferencedNames(binOp.Left, names);
                CollectReferencedNames(binOp.Right, names);
                break;
            case UnaryOperationNode unOp:
                CollectReferencedNames(unOp.Operand, names);
                break;
        }
    }

    private static HashSet<string> CollectAssignedNames(IReadOnlyList<StatementNode> statements)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectAssignedNames(statements, names);
        return names;
    }

    private static void CollectAssignedNames(IReadOnlyList<StatementNode> statements, HashSet<string> names)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case BindStatementNode bind:
                    names.Add(bind.Name);
                    break;
                case ForStatementNode forStmt:
                    names.Add(forStmt.VariableName);
                    CollectAssignedNames(forStmt.Body, names);
                    break;
                case WhileStatementNode whileStmt:
                    CollectAssignedNames(whileStmt.Body, names);
                    break;
                case DoWhileStatementNode doWhile:
                    CollectAssignedNames(doWhile.Body, names);
                    break;
                case IfStatementNode ifStmt:
                    CollectAssignedNames(ifStmt.ThenBody, names);
                    foreach (var elseIf in ifStmt.ElseIfClauses)
                        CollectAssignedNames(elseIf.Body, names);
                    if (ifStmt.ElseBody != null)
                        CollectAssignedNames(ifStmt.ElseBody, names);
                    break;
                case ForeachStatementNode foreach_:
                    names.Add(foreach_.VariableName);
                    CollectAssignedNames(foreach_.Body, names);
                    break;
                case TryStatementNode tryStmt:
                    CollectAssignedNames(tryStmt.TryBody, names);
                    break;
            }
        }
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
