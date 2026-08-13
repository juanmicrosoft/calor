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
        => CollectFromCallable(func.Parameters, func.Body);

    /// <summary>
    /// Collects facts from a class method using the same rules as module functions.
    /// </summary>
    public void CollectFromMethod(MethodNode method)
        => CollectFromCallable(method.Parameters, method.Body);

    private void CollectFromCallable(
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<StatementNode> body)
    {
        // Parameter inline refinements hold on entry for the whole function —
        // unless the body rebinds the parameter name, in which case the
        // refinement may no longer describe the current value.
        var bodyAssigned = CollectAssignedNames(body);
        foreach (var param in parameters)
        {
            if (param.InlineRefinement != null && !bodyAssigned.Contains(param.Name))
            {
                ScopedFacts.Add(ScopedFact.FunctionWide(
                    SubstituteSelfRefStatic(
                        param.InlineRefinement.Predicate,
                        param.Name)));
            }
        }

        CollectFromStatements(body);
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
    /// §L{id:i:from:to:step} uses inclusive bounds, matching C# emission.
    /// </summary>
    private void CollectFromForLoop(ForStatementNode forStmt)
    {
        var dummySpan = new TextSpan(0, 0, 1, 1);
        var loopVar = new ReferenceNode(dummySpan, forStmt.VariableName);

        var isPositiveStep = forStmt.Step is IntLiteralNode { Value: > 0 }
            or UnaryOperationNode
            {
                Operator: UnaryOperator.Negate,
                Operand: IntLiteralNode { Value: < 0 }
            };
        var isNegativeStep = forStmt.Step is IntLiteralNode { Value: < 0 }
            or UnaryOperationNode
            {
                Operator: UnaryOperator.Negate,
                Operand: IntLiteralNode { Value: > 0 }
            };
        if (isPositiveStep)
        {
            AddGuardFact(
                new BinaryOperationNode(
                    dummySpan,
                    BinaryOperator.GreaterOrEqual,
                    loopVar,
                    forStmt.From),
                forStmt.Body);
            AddGuardFact(
                new BinaryOperationNode(
                    dummySpan,
                    BinaryOperator.LessOrEqual,
                    loopVar,
                    forStmt.To),
                forStmt.Body);
        }
        else if (isNegativeStep)
        {
            AddGuardFact(
                new BinaryOperationNode(
                    dummySpan,
                    BinaryOperator.LessOrEqual,
                    loopVar,
                    forStmt.From),
                forStmt.Body);
            AddGuardFact(
                new BinaryOperationNode(
                    dummySpan,
                    BinaryOperator.GreaterOrEqual,
                    loopVar,
                    forStmt.To),
                forStmt.Body);
        }

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
        foreach (var node in DescendantsAndSelf(expr))
        {
            if (node is ReferenceNode reference)
                names.Add(reference.Name);
        }
    }

    private static IEnumerable<AstNode> DescendantsAndSelf(AstNode node)
    {
        yield return node;
        foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private static HashSet<string> CollectAssignedNames(IReadOnlyList<StatementNode> statements)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in statements.SelectMany(DescendantsAndSelf))
        {
            switch (node)
            {
                case BindStatementNode bind:
                    names.Add(bind.Name);
                    break;
                case AssignmentStatementNode { Target: ReferenceNode target }:
                    names.Add(target.Name);
                    break;
                case CompoundAssignmentStatementNode { Target: ReferenceNode compoundTarget }:
                    names.Add(compoundTarget.Name);
                    break;
                case UnaryOperationNode
                {
                    Operator: UnaryOperator.PreIncrement
                        or UnaryOperator.PreDecrement
                        or UnaryOperator.PostIncrement
                        or UnaryOperator.PostDecrement,
                    Operand: ReferenceNode unaryTarget
                }:
                    names.Add(unaryTarget.Name);
                    break;
                case ForStatementNode forStmt:
                    names.Add(forStmt.VariableName);
                    break;
                case ForeachStatementNode foreachStmt:
                    names.Add(foreachStmt.VariableName);
                    break;
            }
        }

        return names;
    }

    /// <summary>
    /// Substitutes SelfRefNode (#) with a ReferenceNode for the given variable name.
    /// Returns a new expression tree with substitutions applied.
    /// </summary>
    public static ExpressionNode SubstituteSelfRefStatic(ExpressionNode expr, string variableName)
        => SubstituteSelfRefStatic(
            expr,
            new ReferenceNode(expr.Span, variableName));

    /// <summary>
    /// Substitutes SelfRefNode (#) with an arbitrary expression.
    /// </summary>
    public static ExpressionNode SubstituteSelfRefStatic(
        ExpressionNode expr,
        ExpressionNode replacement)
    {
        var substituted = SubstituteSelfRef(expr, replacement);
        return ContainsSelfRef(substituted)
            ? new BoolLiteralNode(expr.Span, false)
            : substituted;
    }

    private static ExpressionNode SubstituteSelfRef(
        ExpressionNode expr,
        ExpressionNode replacement)
    {
        if (expr is SelfRefNode)
        {
            return replacement;
        }

        if (expr is BinaryOperationNode binOp)
        {
            var left = SubstituteSelfRef(binOp.Left, replacement);
            var right = SubstituteSelfRef(binOp.Right, replacement);
            if (!ReferenceEquals(left, binOp.Left) || !ReferenceEquals(right, binOp.Right))
                return new BinaryOperationNode(binOp.Span, binOp.Operator, left, right);
            return binOp;
        }

        if (expr is UnaryOperationNode unOp)
        {
            var operand = SubstituteSelfRef(unOp.Operand, replacement);
            if (operand != unOp.Operand)
                return new UnaryOperationNode(unOp.Span, unOp.Operator, operand);
            return unOp;
        }

        if (expr is ConditionalExpressionNode conditional)
        {
            var condition = SubstituteSelfRef(
                conditional.Condition,
                replacement);
            var whenTrue = SubstituteSelfRef(
                conditional.WhenTrue,
                replacement);
            var whenFalse = SubstituteSelfRef(
                conditional.WhenFalse,
                replacement);
            return ReferenceEquals(condition, conditional.Condition)
                    && ReferenceEquals(whenTrue, conditional.WhenTrue)
                    && ReferenceEquals(whenFalse, conditional.WhenFalse)
                ? conditional
                : new ConditionalExpressionNode(
                    conditional.Span,
                    condition,
                    whenTrue,
                    whenFalse);
        }

        if (expr is ArrayAccessNode arrayAccess)
        {
            var array = SubstituteSelfRef(arrayAccess.Array, replacement);
            var index = SubstituteSelfRef(arrayAccess.Index, replacement);
            return ReferenceEquals(array, arrayAccess.Array)
                    && ReferenceEquals(index, arrayAccess.Index)
                ? arrayAccess
                : new ArrayAccessNode(arrayAccess.Span, array, index);
        }

        if (expr is ArrayLengthNode arrayLength)
        {
            var array = SubstituteSelfRef(arrayLength.Array, replacement);
            return ReferenceEquals(array, arrayLength.Array)
                ? arrayLength
                : new ArrayLengthNode(arrayLength.Span, array);
        }

        if (expr is FieldAccessNode fieldAccess)
        {
            var target = SubstituteSelfRef(fieldAccess.Target, replacement);
            return ReferenceEquals(target, fieldAccess.Target)
                ? fieldAccess
                : new FieldAccessNode(
                    fieldAccess.Span,
                    target,
                    fieldAccess.FieldName);
        }

        if (expr is StringOperationNode stringOperation)
        {
            var arguments = stringOperation.Arguments
                .Select(argument => SubstituteSelfRef(argument, replacement))
                .ToArray();
            return arguments
                .Zip(
                    stringOperation.Arguments,
                    ReferenceEquals)
                .All(unchanged => unchanged)
                ? stringOperation
                : new StringOperationNode(
                    stringOperation.Span,
                    stringOperation.Operation,
                    arguments,
                    stringOperation.ComparisonMode);
        }

        if (expr is ImplicationExpressionNode implication)
        {
            var antecedent = SubstituteSelfRef(
                implication.Antecedent,
                replacement);
            var consequent = SubstituteSelfRef(
                implication.Consequent,
                replacement);
            return ReferenceEquals(antecedent, implication.Antecedent)
                    && ReferenceEquals(consequent, implication.Consequent)
                ? implication
                : new ImplicationExpressionNode(
                    implication.Span,
                    antecedent,
                    consequent);
        }

        if (expr is ForallExpressionNode forall)
        {
            if (ReplacementCouldBeCaptured(
                    replacement,
                    forall.BoundVariables))
            {
                return forall;
            }
            var body = SubstituteSelfRef(forall.Body, replacement);
            return ReferenceEquals(body, forall.Body)
                ? forall
                : new ForallExpressionNode(
                    forall.Span,
                    forall.BoundVariables,
                    body);
        }

        if (expr is ExistsExpressionNode exists)
        {
            if (ReplacementCouldBeCaptured(
                    replacement,
                    exists.BoundVariables))
            {
                return exists;
            }
            var body = SubstituteSelfRef(exists.Body, replacement);
            return ReferenceEquals(body, exists.Body)
                ? exists
                : new ExistsExpressionNode(
                    exists.Span,
                    exists.BoundVariables,
                    body);
        }

        return expr;
    }

    private static bool ReplacementCouldBeCaptured(
        ExpressionNode replacement,
        IReadOnlyList<QuantifierVariableNode> boundVariables)
    {
        var boundNames = boundVariables
            .Select(variable => variable.Name)
            .ToHashSet(StringComparer.Ordinal);
        return EnumerateDescendantsAndSelf(replacement)
            .OfType<ReferenceNode>()
            .Any(reference => boundNames.Contains(reference.Name));
    }

    private static bool ContainsSelfRef(ExpressionNode expression)
    {
        if (expression is SelfRefNode)
            return true;

        return Calor.Compiler.Analysis.RecursiveAstWalker
            .GetAllChildren(expression)
            .OfType<ExpressionNode>()
            .Any(ContainsSelfRef);
    }

    private static IEnumerable<AstNode> EnumerateDescendantsAndSelf(
        AstNode node)
    {
        yield return node;
        foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker
                     .GetAllChildren(node))
        {
            foreach (var descendant in EnumerateDescendantsAndSelf(child))
                yield return descendant;
        }
    }
}
