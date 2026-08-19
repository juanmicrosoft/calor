using System.Collections.Immutable;
using System.Numerics;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis.BugPatterns;

[Flags]
internal enum TypedBugPatternKind
{
    None = 0,
    DivisionByZero = 1 << 0,
    IndexOutOfBounds = 1 << 1,
    NullDereference = 1 << 2,
    IntegerOverflow = 1 << 3,
    OffByOne = 1 << 4,
    All = DivisionByZero
        | IndexOutOfBounds
        | NullDereference
        | IntegerOverflow
        | OffByOne,
}

/// <summary>
/// Typed, edge-sensitive bug-pattern analysis over the shared CFG. Facts are keyed by
/// SymbolId, assignments are strong updates, and conditional edges refine opposite facts.
/// </summary>
internal sealed class TypedBugPatternAnalysis
{
    private readonly BoundFunction _function;
    private readonly DiagnosticBag _diagnostics;
    private readonly BugPatternOptions _options;
    private readonly TypedBugPatternKind _enabled;
    private readonly HashSet<DiagnosticKey> _reported = [];
    private readonly Dictionary<SymbolId, BoundForStatement> _loops;
    private readonly Dictionary<SymbolId, FlowState> _loopEntryStates = [];

    private TypedBugPatternAnalysis(
        BoundFunction function,
        DiagnosticBag diagnostics,
        BugPatternOptions options,
        TypedBugPatternKind enabled)
    {
        _function = function;
        _diagnostics = diagnostics;
        _options = options;
        _enabled = enabled;
        _loops = BoundNodeHelpers.DescendantsAndSelf(function)
            .OfType<BoundForStatement>()
            .Where(loop => !loop.LoopVariable.Id.IsNone)
            .GroupBy(loop => loop.LoopVariable.Id)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public static void Check(
        BoundFunction function,
        DiagnosticBag diagnostics,
        BugPatternOptions options,
        TypedBugPatternKind enabled)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(options);

        new TypedBugPatternAnalysis(function, diagnostics, options, enabled).Run();
    }

    private void Run()
    {
        foreach (var incomplete in BoundNodeHelpers.GetAnalysisIncompleteNodes(_function))
        {
            ReportIncomplete(
                incomplete.Span,
                $"Bug-pattern analysis is incomplete at '{incomplete.GetType().Name}'");
        }

        ControlFlowGraph cfg;
        try
        {
            cfg = ControlFlowGraph.Build(_function);
        }
        catch (ControlFlowGraphValidationException exception)
        {
            ReportIncomplete(
                _function.Span,
                $"Bug-pattern CFG is incomplete: {exception.Message}");
            return;
        }

        var entryStates = ComputeEntryStates(cfg);
        Inspect(cfg, entryStates);

        if (_enabled.HasFlag(TypedBugPatternKind.OffByOne))
            CheckOffByOne();
    }

    private Dictionary<BasicBlock, FlowState> ComputeEntryStates(ControlFlowGraph cfg)
    {
        var entries = new Dictionary<BasicBlock, FlowState>
        {
            [cfg.Entry] = CreateInitialState(),
        };
        var queue = new Queue<BasicBlock>();
        var queued = new HashSet<BasicBlock>();
        queue.Enqueue(cfg.Entry);
        queued.Add(cfg.Entry);

        var iterations = 0;
        while (queue.Count > 0)
        {
            if (++iterations > 10_000)
            {
                ReportIncomplete(
                    _function.Span,
                    "Bug-pattern dataflow did not converge");
                break;
            }

            var block = queue.Dequeue();
            queued.Remove(block);
            var state = TransferBlock(block, entries[block]);

            foreach (var edge in block.OutgoingEdges)
            {
                var edgeState = RefineEdge(state, block, edge);
                if (!edgeState.IsReachable)
                    continue;
                var changed = false;
                if (entries.TryGetValue(edge.Target, out var existing))
                {
                    var joined = FlowState.Join(existing, edgeState);
                    if (!joined.Equals(existing))
                    {
                        entries[edge.Target] = joined;
                        changed = true;
                    }
                }
                else
                {
                    entries[edge.Target] = edgeState;
                    changed = true;
                }

                if (changed && queued.Add(edge.Target))
                    queue.Enqueue(edge.Target);
            }
        }

        return entries;
    }

    private FlowState CreateInitialState()
    {
        var state = FlowState.Empty;
        var guardedIds = _options.PreconditionGuardedParameterIds != null
            && _options.PreconditionGuardedParameterIds.TryGetValue(
                _function.Symbol.Id,
                out var ids)
                ? ids
                : null;
        var guardedNames = _options.PreconditionGuardedParameterIds == null
            && _options.PreconditionGuardedParams != null
            && _options.PreconditionGuardedParams.TryGetValue(
                _function.Symbol.Name,
                out var names)
                ? names
                : null;

        foreach (var parameter in _function.Symbol.Parameters)
        {
            var value = UnknownForVariable(parameter);
            if ((guardedIds?.Contains(parameter.Id) == true
                 || guardedNames?.Contains(parameter.Name) == true)
                && value.Numeric is { } numeric)
            {
                value = value.WithNumeric(numeric with { ExcludesZero = true });
            }
            state = state.StrongUpdate(parameter, value);
        }
        return state;
    }

    private FlowState TransferBlock(BasicBlock block, FlowState input)
    {
        var state = input;
        for (var index = 0; index < block.Statements.Count; index++)
        {
            state = TransferStatement(
                block.Statements[index],
                state,
                applyDefinition: !block.IsDefinitionDeferred(index));
        }
        foreach (var operation in block.SyntheticOperations)
            state = TransferSynthetic(operation, state);
        return state;
    }

    private FlowState TransferStatement(
        BoundStatement statement,
        FlowState state,
        bool applyDefinition)
    {
        if (applyDefinition
            && statement is BoundBindStatement bind)
        {
            var value = bind.Initializer == null
                ? UnknownForVariable(bind.Variable)
                : Evaluate(bind.Initializer, state)
                    .Retype(bind.Variable.TypeName)
                    .WithFreshIdentityWhenNeeded(bind.Variable, bind.Span);
            if (bind.Initializer != null)
                state = InvalidateRefArguments(bind.Initializer, state);
            return state.StrongUpdate(
                bind.Variable,
                value);
        }

        if (applyDefinition
            && statement is BoundAssignmentStatement assignment
            && assignment.Target is BoundVariableExpression target)
        {
            var value = Evaluate(assignment.Value, state)
                .Retype(target.Variable.TypeName)
                .WithFreshIdentityWhenNeeded(target.Variable, assignment.Span);
            state = InvalidateRefArguments(assignment.Target, state);
            state = InvalidateRefArguments(assignment.Value, state);
            return state.StrongUpdate(
                target.Variable,
                value);
        }

        if (applyDefinition
            && statement is BoundCompoundAssignment compound
            && compound.Target is BoundVariableExpression compoundTarget)
        {
            var result = EvaluateCompound(compound, state)
                .Retype(compoundTarget.Variable.TypeName)
                .WithFreshIdentityWhenNeeded(compoundTarget.Variable, compound.Span);
            state = InvalidateRefArguments(compound.Target, state);
            state = InvalidateRefArguments(compound.Value, state);
            return state.StrongUpdate(compoundTarget.Variable, result);
        }

        return InvalidateRefArguments(statement, state);
    }

    private FlowState TransferSynthetic(SyntheticOperation operation, FlowState state)
    {
        if (operation.Kind == SyntheticOperationKind.ForInitialization
            && operation.DefinedVariable != null)
        {
            _loopEntryStates[operation.DefinedVariable.Id] = state;
            var value = operation.Expression == null
                ? UnknownForVariable(operation.DefinedVariable)
                : Evaluate(operation.Expression, state)
                    .Retype(operation.DefinedVariable.TypeName)
                    .WithFreshIdentityWhenNeeded(
                        operation.DefinedVariable,
                        operation.Span);
            if (operation.Expression != null)
                state = InvalidateRefArguments(operation.Expression, state);
            return state.StrongUpdate(
                operation.DefinedVariable,
                value);
        }

        if (operation.Kind == SyntheticOperationKind.ForStep
            && operation.DefinedVariable != null)
        {
            // A widening strong update prevents loop fixed points from enumerating every
            // integral value. The true ForCondition edge restores precise loop bounds.
            return state.StrongUpdate(
                operation.DefinedVariable,
                UnknownForVariable(operation.DefinedVariable));
        }

        if (operation.Kind == SyntheticOperationKind.StatementDefinition
            && operation.SourceStatement != null)
        {
            return TransferStatement(
                operation.SourceStatement,
                state,
                applyDefinition: true);
        }

        if (operation.IsDefinition && operation.DefinedVariable != null)
        {
            var value = operation.Expression == null
                ? UnknownForVariable(operation.DefinedVariable)
                : Evaluate(operation.Expression, state)
                    .Retype(operation.DefinedVariable.TypeName)
                    .WithFreshIdentityWhenNeeded(
                        operation.DefinedVariable,
                        operation.Span);
            if (operation.Expression != null)
                state = InvalidateRefArguments(operation.Expression, state);
            return state.StrongUpdate(
                operation.DefinedVariable,
                value);
        }

        return operation.Expression == null
            ? state
            : InvalidateRefArguments(operation.Expression, state);
    }

    private FlowState RefineEdge(
        FlowState state,
        BasicBlock block,
        ControlFlowEdge edge)
    {
        if (block.Terminator is not ConditionalTerminator conditional)
            return state;
        if (edge.Kind == ControlFlowEdgeKind.True)
            return RefineCondition(conditional.Condition, assumeTrue: true, state);
        if (edge.Kind == ControlFlowEdgeKind.False)
            return RefineCondition(conditional.Condition, assumeTrue: false, state);
        return state;
    }

    private FlowState RefineCondition(
        BoundExpression condition,
        bool assumeTrue,
        FlowState state)
    {
        if (condition is BoundUnaryExpression unary
            && unary.Operator == UnaryOperator.Not)
        {
            return RefineCondition(unary.Operand, !assumeTrue, state);
        }

        if (condition is BoundBinaryExpression logical
            && logical.Operator == BinaryOperator.And)
        {
            if (assumeTrue)
            {
                var leftTrue = RefineCondition(
                    logical.Left,
                    assumeTrue: true,
                    state);
                return leftTrue.IsReachable
                    ? RefineCondition(
                    logical.Right,
                    assumeTrue: true,
                        leftTrue)
                    : FlowState.Unreachable;
            }
            var leftFalse = RefineCondition(
                logical.Left,
                assumeTrue: false,
                state);
            var leftTrueForRight = RefineCondition(
                logical.Left,
                assumeTrue: true,
                state);
            var rightFalse = leftTrueForRight.IsReachable
                ? RefineCondition(
                    logical.Right,
                    assumeTrue: false,
                    leftTrueForRight)
                : FlowState.Unreachable;
            return FlowState.Join(
                leftFalse,
                rightFalse);
        }

        if (condition is BoundBinaryExpression disjunction
            && disjunction.Operator == BinaryOperator.Or)
        {
            if (!assumeTrue)
            {
                var leftFalse = RefineCondition(
                    disjunction.Left,
                    assumeTrue: false,
                    state);
                return leftFalse.IsReachable
                    ? RefineCondition(
                    disjunction.Right,
                    assumeTrue: false,
                        leftFalse)
                    : FlowState.Unreachable;
            }
            var leftTrue = RefineCondition(
                disjunction.Left,
                assumeTrue: true,
                state);
            var leftFalseForRight = RefineCondition(
                disjunction.Left,
                assumeTrue: false,
                state);
            var rightTrue = leftFalseForRight.IsReachable
                ? RefineCondition(
                    disjunction.Right,
                    assumeTrue: true,
                    leftFalseForRight)
                : FlowState.Unreachable;
            return FlowState.Join(
                leftTrue,
                rightTrue);
        }

        var evaluatedState = InvalidateRefArguments(condition, state);
        if (TryEvaluateCondition(condition, evaluatedState, out var constant))
        {
            return constant == assumeTrue
                ? evaluatedState
                : FlowState.Unreachable;
        }

        if (condition is BoundStructuralExpression structural
            && structural.NodeTypeName == "ForCondition"
            && structural.Children.Count >= 2
            && structural.Children[0] is BoundVariableExpression loopVariable
            && _loops.TryGetValue(loopVariable.Variable.Id, out var loop))
        {
            return RefineForCondition(loop, assumeTrue, evaluatedState);
        }

        if (TryGetOptionPredicate(
                condition,
                out var optionVariable,
                out var trueVariant,
                out var falseVariant))
        {
            return RefineVariant(
                evaluatedState,
                optionVariable,
                assumeTrue ? trueVariant : falseVariant);
        }

        if (condition is not BoundBinaryExpression comparison
            || !IsComparison(comparison.Operator))
        {
            return evaluatedState;
        }

        var op = assumeTrue
            ? comparison.Operator
            : NegateComparison(comparison.Operator);

        if (TryGetNoneComparison(
                comparison.Left,
                comparison.Right,
                op,
                out var option,
                out var variant))
        {
            return RefineVariant(evaluatedState, option, variant);
        }

        evaluatedState = RefineNumericComparison(
            comparison.Left,
            comparison.Right,
            op,
            evaluatedState);
        evaluatedState = RefineNumericComparison(
            comparison.Right,
            comparison.Left,
            ReverseComparison(op),
            evaluatedState);
        evaluatedState = RefineSequenceBound(
            comparison.Left,
            comparison.Right,
            op,
            evaluatedState);
        return RefineSequenceBound(
            comparison.Right,
            comparison.Left,
            ReverseComparison(op),
            evaluatedState);
    }

    private bool TryEvaluateCondition(
        BoundExpression expression,
        FlowState state,
        out bool value)
    {
        if (expression is BoundBoolLiteral boolean)
        {
            value = boolean.Value;
            return true;
        }
        if (expression is BoundUnaryExpression
            {
                Operator: UnaryOperator.Not,
            } unary
            && TryEvaluateCondition(unary.Operand, state, out var operand))
        {
            value = !operand;
            return true;
        }
        if (expression is BoundBinaryExpression logical
            && logical.Operator is BinaryOperator.And or BinaryOperator.Or
            && TryEvaluateCondition(logical.Left, state, out var leftBoolean))
        {
            var evaluateRightWhen = logical.Operator == BinaryOperator.And;
            if (leftBoolean != evaluateRightWhen)
            {
                value = leftBoolean;
                return true;
            }
            var rightState = InvalidateRefArguments(logical.Left, state);
            if (TryEvaluateCondition(logical.Right, rightState, out var rightBoolean))
            {
                value = rightBoolean;
                return true;
            }
        }
        if (expression is BoundBinaryExpression comparison
            && IsComparison(comparison.Operator))
        {
            var left = Evaluate(comparison.Left, state);
            var rightState = InvalidateRefArguments(comparison.Left, state);
            var right = Evaluate(comparison.Right, rightState);
            if (left.Numeric is { IsExact: true } leftNumeric
                && right.Numeric is { IsExact: true } rightNumeric)
            {
                value = comparison.Operator switch
                {
                    BinaryOperator.Equal => leftNumeric.Minimum == rightNumeric.Minimum,
                    BinaryOperator.NotEqual => leftNumeric.Minimum != rightNumeric.Minimum,
                    BinaryOperator.LessThan => leftNumeric.Minimum < rightNumeric.Minimum,
                    BinaryOperator.LessOrEqual => leftNumeric.Minimum <= rightNumeric.Minimum,
                    BinaryOperator.GreaterThan => leftNumeric.Minimum > rightNumeric.Minimum,
                    BinaryOperator.GreaterOrEqual => leftNumeric.Minimum >= rightNumeric.Minimum,
                    _ => false,
                };
                return true;
            }
            if (left.Decimal is { Exact: not null } leftDecimal
                && right.Decimal is { Exact: not null } rightDecimal)
            {
                value = comparison.Operator switch
                {
                    BinaryOperator.Equal => leftDecimal.Exact.Value == rightDecimal.Exact.Value,
                    BinaryOperator.NotEqual => leftDecimal.Exact.Value != rightDecimal.Exact.Value,
                    BinaryOperator.LessThan => leftDecimal.Exact.Value < rightDecimal.Exact.Value,
                    BinaryOperator.LessOrEqual => leftDecimal.Exact.Value <= rightDecimal.Exact.Value,
                    BinaryOperator.GreaterThan => leftDecimal.Exact.Value > rightDecimal.Exact.Value,
                    BinaryOperator.GreaterOrEqual => leftDecimal.Exact.Value >= rightDecimal.Exact.Value,
                    _ => false,
                };
                return true;
            }
        }
        value = false;
        return false;
    }

    private FlowState RefineForCondition(
        BoundForStatement loop,
        bool assumeTrue,
        FlowState state)
    {
        if (!LoopStepSemantics.TryEvaluate(loop.Step, out var step)
            || step.IsZero)
            return state;

        var op = step.Sign > 0
            ? BinaryOperator.LessOrEqual
            : BinaryOperator.GreaterOrEqual;
        if (!assumeTrue)
            op = NegateComparison(op);

        var loopReference = new BoundVariableExpression(
            loop.Span,
            loop.LoopVariable);
        if (assumeTrue && !LoopBodyDefines(loop.Body, loop.LoopVariable))
        {
            var fromOperation = step.Sign > 0
                ? BinaryOperator.GreaterOrEqual
                : BinaryOperator.LessOrEqual;
            state = RefineNumericComparison(
                loopReference,
                loop.From,
                fromOperation,
                state);
            state = RefineSequenceBound(
                loopReference,
                loop.From,
                fromOperation,
                state);
        }

        state = RefineNumericComparison(
            loopReference,
            loop.To,
            op,
            state);
        return assumeTrue
            ? RefineSequenceBound(
                loopReference,
                loop.To,
                op,
                state)
            : state;
    }

    private static bool LoopBodyDefines(
        IReadOnlyList<BoundStatement> body,
        VariableSymbol variable) =>
        body.SelectMany(BoundNodeHelpers.DescendantsAndSelf)
            .OfType<BoundStatement>()
            .Any(statement => BoundNodeHelpers.DefinesVariable(statement, variable));

    private FlowState RefineNumericComparison(
        BoundExpression maybeVariable,
        BoundExpression limitExpression,
        BinaryOperator op,
        FlowState state)
    {
        if (maybeVariable is not BoundVariableExpression variable
            || !state.TryGet(variable.Variable.Id, out var current))
        {
            return state;
        }

        var limit = Evaluate(limitExpression, state);
        if (current.Decimal is { } decimalValue
            && limit.Decimal is { Exact: not null } decimalLimit)
        {
            return RefineDecimalComparison(
                variable.Variable,
                current,
                decimalValue,
                decimalLimit.Exact.Value,
                op,
                state);
        }

        if (current.Numeric is not { } numeric)
            return state;
        if (!TryGetExactInteger(limit, out var exact))
            return state;

        var refined = numeric;
        switch (op)
        {
            case BinaryOperator.Equal:
                if (!numeric.Contains(exact))
                    return FlowState.Unreachable;
                refined = NumericDomain.Exact(exact);
                break;
            case BinaryOperator.NotEqual:
                if (numeric.IsExact && numeric.Minimum == exact)
                    return FlowState.Unreachable;
                if (exact.IsZero)
                    refined = refined with { ExcludesZero = true };
                break;
            case BinaryOperator.LessThan:
                if (refined.IntersectMaximum(exact - BigInteger.One) is not { } lessThan)
                    return FlowState.Unreachable;
                refined = lessThan;
                break;
            case BinaryOperator.LessOrEqual:
                if (refined.IntersectMaximum(exact) is not { } lessOrEqual)
                    return FlowState.Unreachable;
                refined = lessOrEqual;
                break;
            case BinaryOperator.GreaterThan:
                if (refined.IntersectMinimum(exact + BigInteger.One) is not { } greaterThan)
                    return FlowState.Unreachable;
                refined = greaterThan;
                break;
            case BinaryOperator.GreaterOrEqual:
                if (refined.IntersectMinimum(exact) is not { } greaterOrEqual)
                    return FlowState.Unreachable;
                refined = greaterOrEqual;
                break;
        }

        return state.StrongUpdate(
            variable.Variable,
            current.WithNumeric(refined),
            preserveDependentFacts: true);
    }

    private static FlowState RefineDecimalComparison(
        VariableSymbol variable,
        AbstractValue current,
        DecimalDomain domain,
        decimal limit,
        BinaryOperator operation,
        FlowState state)
    {
        if (domain.Exact is { } exact)
        {
            var condition = operation switch
            {
                BinaryOperator.Equal => exact == limit,
                BinaryOperator.NotEqual => exact != limit,
                BinaryOperator.LessThan => exact < limit,
                BinaryOperator.LessOrEqual => exact <= limit,
                BinaryOperator.GreaterThan => exact > limit,
                BinaryOperator.GreaterOrEqual => exact >= limit,
                _ => true,
            };
            return condition ? state : FlowState.Unreachable;
        }

        var refined = operation switch
        {
            BinaryOperator.Equal => DecimalDomain.Constant(limit),
            BinaryOperator.NotEqual when limit == 0m => domain.ExcludeZero(),
            BinaryOperator.LessThan when limit <= 0m => domain.ExcludeZero(),
            BinaryOperator.GreaterThan when limit >= 0m => domain.ExcludeZero(),
            _ => domain,
        };
        return state.StrongUpdate(
            variable,
            current.WithDecimal(refined),
            preserveDependentFacts: true);
    }

    private FlowState RefineSequenceBound(
        BoundExpression left,
        BoundExpression right,
        BinaryOperator op,
        FlowState state)
    {
        if (left is not BoundVariableExpression index
            || index.Variable.Id.IsNone)
        {
            return state;
        }

        if (TryResolveLengthTerm(right, state, out var length))
        {
            if (op == BinaryOperator.LessThan && length.Offset.IsZero)
            {
                return state.AddBound(new SequenceBound(
                    index.Variable.Id,
                    length.ReferenceId,
                    length.Dimension,
                    SequenceBoundKind.UpperExclusive));
            }
            if (op == BinaryOperator.LessOrEqual
                && length.Offset == -BigInteger.One)
            {
                return state.AddBound(new SequenceBound(
                    index.Variable.Id,
                    length.ReferenceId,
                    length.Dimension,
                    SequenceBoundKind.UpperExclusive));
            }
        }

        if (TryGetExactInteger(Evaluate(right, state), out var exact)
            && exact.IsZero
            && op is BinaryOperator.GreaterOrEqual or BinaryOperator.GreaterThan)
        {
            return state.AddBound(new SequenceBound(
                index.Variable.Id,
                ReferenceId: string.Empty,
                Dimension: -1,
                SequenceBoundKind.LowerZero));
        }

        return state;
    }

    private FlowState RefineVariant(
        FlowState state,
        VariableSymbol variable,
        VariantState variant)
    {
        if (!state.TryGet(variable.Id, out var current))
            current = UnknownForVariable(variable);

        if (current.Variant is not (VariantState.Unknown or VariantState.Maybe)
            && current.Variant != variant)
        {
            return FlowState.Unreachable;
        }

        if (current.ValueIdentity == null)
        {
            return state.StrongUpdate(
                variable,
                current.WithVariant(variant),
                preserveDependentFacts: true);
        }

        var result = state;
        foreach (var pair in state.Values)
        {
            if (pair.Value.ValueIdentity == current.ValueIdentity)
            {
                result = result.Set(
                    pair.Key,
                    pair.Value.WithVariant(variant));
            }
        }
        return result;
    }

    private void Inspect(
        ControlFlowGraph cfg,
        IReadOnlyDictionary<BasicBlock, FlowState> entries)
    {
        foreach (var block in cfg.ReachableBlocks.OrderBy(item => item.Ordinal))
        {
            if (!entries.TryGetValue(block, out var state))
                continue;
            if (!state.IsReachable)
                continue;

            for (var index = 0; index < block.Statements.Count; index++)
            {
                InspectStatement(block.Statements[index], state);
                state = TransferStatement(
                    block.Statements[index],
                    state,
                    applyDefinition: !block.IsDefinitionDeferred(index));
            }

            foreach (var operation in block.SyntheticOperations)
            {
                if (operation.Expression != null)
                    InspectExpression(operation.Expression, state);
                state = TransferSynthetic(operation, state);
            }

            if (block.Terminator.Condition != null)
                InspectExpression(block.Terminator.Condition, state);
        }
    }

    private void InspectStatement(BoundStatement statement, FlowState state)
    {
        if (_enabled.HasFlag(TypedBugPatternKind.IntegerOverflow))
            CheckTargetConversionOverflow(statement, state);

        if (statement is BoundCompoundAssignment compound)
        {
            var valueState = InvalidateRefArguments(compound.Target, state);
            if (_enabled.HasFlag(TypedBugPatternKind.DivisionByZero)
                && compound.Operator is CompoundAssignmentOperator.Divide
                    or CompoundAssignmentOperator.Modulo)
            {
                CheckDivisor(
                    compound.Value,
                    compound.Span,
                    GetArithmeticType(
                        compound.Target,
                        compound.Value,
                        compound.Target.TypeName),
                    valueState);
            }
            if (_enabled.HasFlag(TypedBugPatternKind.IntegerOverflow)
                && compound.Operator is CompoundAssignmentOperator.Divide
                    or CompoundAssignmentOperator.Modulo)
            {
                CheckMinimumDividedByNegativeOne(
                    compound.Target,
                    compound.Value,
                    compound.Span,
                    GetArithmeticType(
                        compound.Target,
                        compound.Value,
                        compound.Target.TypeName),
                    state,
                    valueState,
                    compound.Operator == CompoundAssignmentOperator.Divide
                        ? "/"
                        : "%");
            }
            CheckCompoundOverflow(compound, state, valueState);
        }

        if (statement is BoundCallStatement callStatement)
        {
            InspectCall(
                callStatement.ReceiverSymbol,
                callStatement.ResolvedMethodName,
                callStatement.Span,
                state);
        }

        foreach (var expression in BoundNodeHelpers.GetImmediateExpressions(statement))
            InspectExpression(expression, state);
    }

    private void CheckTargetConversionOverflow(
        BoundStatement statement,
        FlowState state)
    {
        VariableSymbol? target = null;
        BoundExpression? value = null;
        if (statement is BoundBindStatement bind && bind.Initializer != null)
        {
            target = bind.Variable;
            value = bind.Initializer;
        }
        else if (statement is BoundAssignmentStatement
                 {
                     Target: BoundVariableExpression variable,
                 } assignment)
        {
            target = variable.Variable;
            value = assignment.Value;
        }

        if (target == null
            || value == null
            || !TryGetIntegralType(target.TypeName, out var integral))
        {
            return;
        }

        var numeric = Evaluate(value, state).Numeric;
        if (numeric == null)
            return;
        ReportOverflowIfOutside(
            statement.Span,
            numeric.Value,
            integral,
            "target conversion");
    }

    private void InspectExpression(BoundExpression expression, FlowState state)
    {
        if (expression is BoundConditionalExpression conditional)
        {
            InspectExpression(conditional.Condition, state);
            var conditionState = InvalidateRefArguments(
                conditional.Condition,
                state);
            if (TryEvaluateCondition(
                    conditional.Condition,
                    conditionState,
                    out var constant))
            {
                var branchState = RefineCondition(
                    conditional.Condition,
                    constant,
                    conditionState);
                if (branchState.IsReachable)
                {
                    InspectExpression(
                        constant
                            ? conditional.WhenTrue
                            : conditional.WhenFalse,
                        branchState);
                }
                return;
            }

            var whenTrue = RefineCondition(
                conditional.Condition,
                assumeTrue: true,
                conditionState);
            if (whenTrue.IsReachable)
                InspectExpression(conditional.WhenTrue, whenTrue);
            var whenFalse = RefineCondition(
                conditional.Condition,
                assumeTrue: false,
                conditionState);
            if (whenFalse.IsReachable)
                InspectExpression(conditional.WhenFalse, whenFalse);
            return;
        }

        if (expression is BoundBinaryExpression logical
            && logical.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            InspectExpression(logical.Left, state);
            var leftState = InvalidateRefArguments(logical.Left, state);
            var evaluateRightWhen = logical.Operator == BinaryOperator.And;
            if (TryEvaluateCondition(logical.Left, leftState, out var leftConstant))
            {
                if (leftConstant == evaluateRightWhen)
                {
                    var rightState = RefineCondition(
                        logical.Left,
                        leftConstant,
                        leftState);
                    if (rightState.IsReachable)
                        InspectExpression(logical.Right, rightState);
                }
                return;
            }

            var conditionalRightState = RefineCondition(
                logical.Left,
                evaluateRightWhen,
                leftState);
            if (conditionalRightState.IsReachable)
                InspectExpression(logical.Right, conditionalRightState);
            return;
        }

        if (expression is BoundBinaryExpression binary)
        {
            InspectExpression(binary.Left, state);
            var rightState = InvalidateRefArguments(binary.Left, state);
            InspectExpression(binary.Right, rightState);
            if (_enabled.HasFlag(TypedBugPatternKind.DivisionByZero)
                && binary.Operator is BinaryOperator.Divide or BinaryOperator.Modulo)
            {
                CheckDivision(binary, state, rightState);
            }
            if (_enabled.HasFlag(TypedBugPatternKind.IntegerOverflow))
                CheckBinaryOverflow(binary, state, rightState);
            return;
        }
        else if (expression is BoundUnaryExpression unary
                 && _enabled.HasFlag(TypedBugPatternKind.IntegerOverflow))
        {
            CheckUnaryOverflow(unary, state);
        }
        else if (expression is BoundTypeOperationExpression
                 {
                     Operation: TypeOp.Cast,
                 } conversion
                 && _enabled.HasFlag(TypedBugPatternKind.IntegerOverflow))
        {
            CheckExplicitCastOverflow(conversion, state);
        }
        else if (expression is BoundArrayAccess access)
        {
            InspectSequenceOperands(access.Array, [access.Index], state);
            InspectDereference(access.Array, access.Span, state);
            if (_enabled.HasFlag(TypedBugPatternKind.IndexOutOfBounds))
                CheckSequenceAccess(access.Array, [access.Index], access.Span, state);
            return;
        }
        else if (expression is BoundMultiDimArrayAccess multi)
        {
            InspectSequenceOperands(multi.Array, multi.Indices, state);
            InspectDereference(multi.Array, multi.Span, state);
            if (_enabled.HasFlag(TypedBugPatternKind.IndexOutOfBounds))
                CheckSequenceAccess(multi.Array, multi.Indices, multi.Span, state);
            return;
        }
        else if (expression is BoundArrayAccessExpression legacy)
        {
            InspectSequenceOperands(legacy.Array, legacy.Indices, state);
            InspectDereference(legacy.Array, legacy.Span, state);
            if (_enabled.HasFlag(TypedBugPatternKind.IndexOutOfBounds))
                CheckSequenceAccess(legacy.Array, legacy.Indices, legacy.Span, state);
            return;
        }
        else if (expression is BoundArrayLength length)
        {
            InspectDereference(length.Array, length.Span, state);
        }
        else if (expression is BoundFieldAccessExpression field)
        {
            InspectDereference(field.Target, field.Span, state);
        }
        else if (expression is BoundCallExpression call)
        {
            InspectCall(
                call.ReceiverSymbol,
                call.ResolvedMethodName,
                call.Span,
                state);
        }

        var childState = state;
        foreach (var child in expression.Children)
        {
            InspectExpression(child, childState);
            childState = InvalidateRefArguments(child, childState);
        }
    }

    private void InspectSequenceOperands(
        BoundExpression sequence,
        IReadOnlyList<BoundExpression> indices,
        FlowState state)
    {
        InspectExpression(sequence, state);
        var operandState = InvalidateRefArguments(sequence, state);
        foreach (var index in indices)
        {
            InspectExpression(index, operandState);
            operandState = InvalidateRefArguments(index, operandState);
        }
    }

    private void CheckDivision(
        BoundBinaryExpression division,
        FlowState state,
        FlowState rightState)
    {
        var type = GetArithmeticType(
            division.Left,
            division.Right,
            division.TypeName);
        CheckDivisor(division.Right, division.Span, type, rightState);
    }

    private void CheckDivisor(
        BoundExpression divisorExpression,
        TextSpan span,
        string type,
        FlowState state)
    {
        var divisor = Evaluate(divisorExpression, state);
        if (!TryGetIntegralType(type, out _)
            && TypeIdentity.Canonicalize(type) != "DECIMAL")
        {
            // IEEE floating-point division by zero is defined and is not a
            // DivideByZeroException in generated C#.
            if (TypeIdentity.Canonicalize(type).StartsWith(
                    "FLOAT",
                    StringComparison.Ordinal))
            {
                return;
            }
            ReportUnknown(
                TypedBugPatternKind.DivisionByZero,
                span,
                "Division semantics are unavailable for the operand type");
            return;
        }

        if (BoundNodeHelpers.IsLiteralZero(divisorExpression)
            || IsExactZero(divisor))
        {
            Report(
                span,
                DiagnosticCode.DivisionByZero,
                DiagnosticSeverity.Error,
                "Division by zero is guaranteed on this path");
            return;
        }

        if (divisor.Decimal is { } decimalValue)
        {
            if (!decimalValue.ContainsZero)
                return;
            if (decimalValue.Exact == 0m)
            {
                Report(
                    span,
                    DiagnosticCode.DivisionByZero,
                    DiagnosticSeverity.Error,
                    "Division by decimal zero is guaranteed on this path");
                return;
            }
            Report(
                span,
                DiagnosticCode.DivisionByZero,
                DiagnosticSeverity.Warning,
                "Division by decimal zero is reachable on this path");
            return;
        }

        if (divisor.Numeric is { } numeric)
        {
            if (!numeric.ContainsZero)
                return;
            Report(
                span,
                DiagnosticCode.DivisionByZero,
                DiagnosticSeverity.Warning,
                "Division by zero is reachable on this path");
            return;
        }

        ReportUnknown(
            TypedBugPatternKind.DivisionByZero,
            span,
            "Division-by-zero analysis lacks a typed numeric divisor");
    }

    private void CheckBinaryOverflow(
        BoundBinaryExpression binary,
        FlowState state,
        FlowState rightState)
    {
        if (binary.Operator is BinaryOperator.Divide or BinaryOperator.Modulo)
        {
            CheckMinimumDividedByNegativeOne(
                binary.Left,
                binary.Right,
                binary.Span,
                GetArithmeticType(
                    binary.Left,
                    binary.Right,
                    binary.TypeName),
                state,
                rightState,
                binary.Operator == BinaryOperator.Divide
                    ? "/"
                    : "%");
            return;
        }

        if (binary.Operator is not (
                BinaryOperator.Add
                or BinaryOperator.Subtract
                or BinaryOperator.Multiply
                or BinaryOperator.LeftShift))
        {
            return;
        }

        var targetType = binary.Operator == BinaryOperator.LeftShift
            ? GetShiftResultType(binary.Left.TypeName)
            : GetArithmeticType(
                binary.Left,
                binary.Right,
                binary.TypeName);
        if (!TryGetIntegralType(targetType, out var integral))
        {
            if (IsNumericType(targetType))
                return;
            ReportUnknown(
                TypedBugPatternKind.IntegerOverflow,
                binary.Span,
                "Overflow analysis lacks an integral target type");
            return;
        }

        if (binary.Operator == BinaryOperator.Subtract
            && AreProvablySameValue(binary.Left, binary.Right))
        {
            return;
        }

        var left = Evaluate(binary.Left, state).Numeric;
        var right = Evaluate(binary.Right, rightState).Numeric;
        if (left == null || right == null)
        {
            ReportUnknown(
                TypedBugPatternKind.IntegerOverflow,
                binary.Span,
                "Overflow analysis lacks typed operand ranges");
            return;
        }

        NumericDomain mathematical;
        if (binary.Operator == BinaryOperator.LeftShift)
        {
            if (!TryGetMaskedShiftCount(
                    right.Value,
                    integral,
                    out var shiftCount))
            {
                ReportUnknown(
                    TypedBugPatternKind.IntegerOverflow,
                    binary.Span,
                    "Shift count range is incomplete");
                return;
            }
            mathematical = left.Value.ShiftLeft(shiftCount);
        }
        else
        {
            mathematical = binary.Operator switch
            {
                BinaryOperator.Add => left.Value.Add(right.Value),
                BinaryOperator.Subtract => left.Value.Subtract(right.Value),
                BinaryOperator.Multiply => left.Value.Multiply(right.Value),
                _ => left.Value,
            };
        }

        ReportOverflowIfOutside(binary.Span, mathematical, integral, "arithmetic");
    }

    private void CheckCompoundOverflow(
        BoundCompoundAssignment compound,
        FlowState state,
        FlowState valueState)
    {
        if (!_enabled.HasFlag(TypedBugPatternKind.IntegerOverflow)
            || compound.Target is not BoundVariableExpression target)
        {
            return;
        }

        var left = Evaluate(compound.Target, state).Numeric;
        var right = Evaluate(compound.Value, valueState).Numeric;
        if (left == null
            || right == null
            || !TryGetIntegralType(target.Variable.TypeName, out var integral))
        {
            ReportUnknown(
                TypedBugPatternKind.IntegerOverflow,
                compound.Span,
                "Compound-assignment overflow analysis is incomplete");
            return;
        }

        var mathematical = compound.Operator switch
        {
            CompoundAssignmentOperator.Add => left.Value.Add(right.Value),
            CompoundAssignmentOperator.Subtract => left.Value.Subtract(right.Value),
            CompoundAssignmentOperator.Multiply => left.Value.Multiply(right.Value),
            CompoundAssignmentOperator.LeftShift
                when TryGetMaskedShiftCount(
                    right.Value,
                    integral,
                    out var shiftCount)
                => left.Value.ShiftLeft(shiftCount),
            _ => (NumericDomain?)null,
        };
        if (mathematical == null)
            return;
        ReportOverflowIfOutside(
            compound.Span,
            mathematical.Value,
            integral,
            "compound assignment");
    }

    private void CheckUnaryOverflow(BoundUnaryExpression unary, FlowState state)
    {
        if (unary.Operator != UnaryOperator.Negate)
            return;

        var promotedType = GetUnaryArithmeticType(unary.Operand.TypeName);
        if (!TryGetIntegralType(promotedType, out var integral))
        {
            if (TypeIdentity.Canonicalize(promotedType) == "ULONG")
            {
                ReportUnknown(
                    TypedBugPatternKind.IntegerOverflow,
                    unary.Span,
                    "Unary negation is not defined for u64");
            }
            return;
        }

        var operand = Evaluate(unary.Operand, state).Numeric;
        if (operand == null)
        {
            ReportUnknown(
                TypedBugPatternKind.IntegerOverflow,
                unary.Span,
                "Negation overflow analysis lacks an operand range");
            return;
        }
        ReportOverflowIfOutside(
            unary.Span,
            operand.Value.Negate(),
            integral,
            "negation");
    }

    private void CheckExplicitCastOverflow(
        BoundTypeOperationExpression conversion,
        FlowState state)
    {
        if (!TryGetIntegralType(conversion.TargetType, out var target))
            return;

        var operand = Evaluate(conversion.Operand, state);
        if (operand.Decimal is { } decimalValue)
        {
            if (decimalValue.Exact is { } exact)
            {
                var outside = exact < (decimal)target.Minimum
                    || exact > (decimal)target.Maximum;
                if (outside)
                {
                    Report(
                        conversion.Span,
                        DiagnosticCode.IntegerOverflow,
                        DiagnosticSeverity.Error,
                        $"Explicit decimal-to-{target.DisplayName} cast is outside the target range");
                }
                return;
            }

            Report(
                conversion.Span,
                DiagnosticCode.IntegerOverflow,
                DiagnosticSeverity.Warning,
                $"Explicit decimal-to-{target.DisplayName} cast can overflow");
            return;
        }

        if (operand.Numeric is { } numeric)
        {
            ReportOverflowIfOutside(
                conversion.Span,
                numeric,
                target,
                $"explicit cast to {target.DisplayName}");
            return;
        }

        if (IsNumericType(conversion.Operand.TypeName))
        {
            ReportUnknown(
                TypedBugPatternKind.IntegerOverflow,
                conversion.Span,
                $"Explicit cast to {target.DisplayName} lacks an exact source range");
        }
    }

    private void CheckMinimumDividedByNegativeOne(
        BoundExpression leftExpression,
        BoundExpression rightExpression,
        TextSpan span,
        string type,
        FlowState leftState,
        FlowState rightState,
        string operation)
    {
        if (!TryGetIntegralType(type, out var integral) || !integral.Signed)
            return;

        var left = Evaluate(leftExpression, leftState).Numeric;
        var right = Evaluate(rightExpression, rightState).Numeric;
        if (left == null || right == null)
            return;

        if (!left.Value.Contains(integral.Minimum)
            || !right.Value.Contains(-BigInteger.One))
        {
            return;
        }

        Report(
            span,
            DiagnosticCode.IntegerOverflow,
            DiagnosticSeverity.Warning,
            $"{integral.DisplayName}.MinValue {operation} -1 overflow is reachable");
    }

    private void ReportOverflowIfOutside(
        TextSpan span,
        NumericDomain mathematical,
        IntegralType target,
        string operation)
    {
        if (mathematical.Minimum >= target.Minimum
            && mathematical.Maximum <= target.Maximum)
        {
            return;
        }

        var guaranteed = mathematical.Maximum < target.Minimum
            || mathematical.Minimum > target.Maximum;
        Report(
            span,
            DiagnosticCode.IntegerOverflow,
            guaranteed ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            guaranteed
                ? $"{operation} is outside the {target.DisplayName} range"
                : $"{operation} can overflow the {target.DisplayName} range");
    }

    private void CheckSequenceAccess(
        BoundExpression sequenceExpression,
        IReadOnlyList<BoundExpression> indices,
        TextSpan span,
        FlowState state)
    {
        var sequence = Evaluate(sequenceExpression, state).Sequence;
        var indexState = InvalidateRefArguments(sequenceExpression, state);
        if (sequence == null)
        {
            ReportUnknown(
                TypedBugPatternKind.IndexOutOfBounds,
                span,
                "Index analysis requires an explicit array, range, or string sequence");
            return;
        }

        if (indices.Count == 0)
        {
            ReportUnknown(
                TypedBugPatternKind.IndexOutOfBounds,
                span,
                "Index operation has no bound index");
            return;
        }

        if (indices.Count != sequence.Dimensions.Length
            && sequence.Kind == SequenceKind.MultiDimensionalArray)
        {
            Report(
                span,
                DiagnosticCode.IndexOutOfBounds,
                DiagnosticSeverity.Error,
                $"Array rank is {sequence.Dimensions.Length}, but {indices.Count} indices were supplied");
            return;
        }

        for (var dimension = 0; dimension < indices.Count; dimension++)
        {
            var length = dimension < sequence.Dimensions.Length
                ? sequence.Dimensions[dimension]
                : null;
            CheckIndex(
                indices[dimension],
                sequence,
                dimension,
                length,
                span,
                indexState);
            indexState = InvalidateRefArguments(
                indices[dimension],
                indexState);
        }
    }

    private void CheckIndex(
        BoundExpression indexExpression,
        SequenceShape sequence,
        int dimension,
        NumericDomain? length,
        TextSpan span,
        FlowState state)
    {
        if (indexExpression is BoundRangeExpression range)
        {
            CheckRange(range, sequence, dimension, length, span, state);
            return;
        }

        if (indexExpression is BoundIndexFromEnd fromEnd)
        {
            var offset = Evaluate(fromEnd.Offset, state).Numeric;
            CheckNumericIndex(
                offset,
                sequence,
                dimension,
                length,
                span,
                state,
                fromEnd: true);
            return;
        }

        CheckNumericIndex(
            Evaluate(indexExpression, state).Numeric,
            sequence,
            dimension,
            length,
            span,
            state,
            indexExpression is BoundVariableExpression variable
                ? variable.Variable.Id
                : SymbolId.None,
            fromEnd: false);
    }

    private void CheckNumericIndex(
        NumericDomain? index,
        SequenceShape sequence,
        int dimension,
        NumericDomain? length,
        TextSpan span,
        FlowState state,
        SymbolId indexId = default,
        bool fromEnd = false)
    {
        if (index == null)
        {
            ReportUnknown(
                TypedBugPatternKind.IndexOutOfBounds,
                span,
                "Index analysis lacks an integral index range");
            return;
        }

        var lowerSafe = fromEnd
            ? index.Value.Minimum >= BigInteger.One
            : index.Value.Minimum >= BigInteger.Zero
              || (!indexId.IsNone
                  && state.HasBound(
                      indexId,
                      sequence.ReferenceId,
                      dimension,
                      SequenceBoundKind.LowerZero));
        var lowerInvalid = fromEnd
            ? index.Value.Maximum < BigInteger.One
            : index.Value.Maximum < BigInteger.Zero;

        var upperSafe = false;
        var upperInvalid = false;
        if (length != null)
        {
            upperSafe = index.Value.Maximum < length.Value.Minimum
                || (fromEnd && index.Value.Maximum <= length.Value.Minimum);
            upperInvalid = index.Value.Minimum >= length.Value.Maximum
                && (!fromEnd || index.Value.Minimum > length.Value.Maximum);
        }
        else if (!indexId.IsNone && !fromEnd)
        {
            upperSafe = state.HasBound(
                indexId,
                sequence.ReferenceId,
                dimension,
                SequenceBoundKind.UpperExclusive);
        }

        if (lowerSafe && upperSafe)
            return;

        if (lowerInvalid || upperInvalid)
        {
            Report(
                span,
                DiagnosticCode.IndexOutOfBounds,
                DiagnosticSeverity.Error,
                $"Index is outside dimension {dimension} on every reachable value");
            return;
        }

        Report(
            span,
            DiagnosticCode.IndexOutOfBounds,
            DiagnosticSeverity.Warning,
            $"Index can be outside dimension {dimension}");
    }

    private void CheckRange(
        BoundRangeExpression range,
        SequenceShape sequence,
        int dimension,
        NumericDomain? length,
        TextSpan span,
        FlowState state)
    {
        var start = EvaluateRangeEndpoint(
            range.Start,
            length,
            defaultValue: BigInteger.Zero,
            state);
        var end = EvaluateRangeEndpoint(
            range.End,
            length,
            defaultValue: length?.Maximum,
            state);

        if (start == null || end == null || length == null)
        {
            Report(
                span,
                DiagnosticCode.IndexOutOfBounds,
                DiagnosticSeverity.Warning,
                $"Range bounds can be outside dimension {dimension}");
            return;
        }

        var guaranteedInvalid = start.Value.Maximum < 0
            || end.Value.Maximum < 0
            || start.Value.Minimum > length.Value.Maximum
            || end.Value.Minimum > length.Value.Maximum
            || start.Value.Minimum > end.Value.Maximum;
        if (guaranteedInvalid)
        {
            Report(
                span,
                DiagnosticCode.IndexOutOfBounds,
                DiagnosticSeverity.Error,
                $"Range is invalid for dimension {dimension}");
            return;
        }

        var safe = start.Value.Minimum >= 0
            && end.Value.Minimum >= 0
            && start.Value.Maximum <= length.Value.Minimum
            && end.Value.Maximum <= length.Value.Minimum
            && start.Value.Maximum <= end.Value.Minimum;
        if (!safe)
        {
            Report(
                span,
                DiagnosticCode.IndexOutOfBounds,
                DiagnosticSeverity.Warning,
                $"Range bounds can be outside dimension {dimension}");
        }
    }

    private NumericDomain? EvaluateRangeEndpoint(
        BoundExpression? endpoint,
        NumericDomain? length,
        BigInteger? defaultValue,
        FlowState state)
    {
        if (endpoint == null)
            return defaultValue == null ? null : NumericDomain.Exact(defaultValue.Value);
        if (endpoint is not BoundIndexFromEnd fromEnd)
            return Evaluate(endpoint, state).Numeric;
        if (length == null)
            return null;
        var offset = Evaluate(fromEnd.Offset, state).Numeric;
        return offset == null ? null : length.Value.Subtract(offset.Value);
    }

    private void InspectCall(
        VariableSymbol? receiver,
        string? methodName,
        TextSpan span,
        FlowState state)
    {
        if (!_enabled.HasFlag(TypedBugPatternKind.NullDereference)
            || receiver == null)
        {
            return;
        }

        if (!state.TryGet(receiver.Id, out var value))
            value = UnknownForVariable(receiver);

        var optionKind = GetOptionKind(receiver.TypeName);
        if (optionKind != OptionKind.None)
        {
            if (methodName == null)
            {
                ReportUnknown(
                    TypedBugPatternKind.NullDereference,
                    span,
                    "Option/Result call lacks resolved method identity");
                return;
            }

            if (IsSafeOptionOperation(optionKind, methodName)
                || IsOptionPredicate(optionKind, methodName, out _, out _))
            {
                return;
            }

            if (!TryGetUnsafeOptionRequirement(
                    optionKind,
                    methodName,
                    out var requiredVariant))
                return;

            if (value.Variant == requiredVariant)
                return;
            if (value.Variant is not (VariantState.Unknown or VariantState.Maybe))
            {
                Report(
                    span,
                    DiagnosticCode.UnsafeUnwrap,
                    DiagnosticSeverity.Error,
                    $"Unsafe {methodName} is invalid for {value.Variant}");
                return;
            }
            Report(
                span,
                DiagnosticCode.UnsafeUnwrap,
                DiagnosticSeverity.Warning,
                $"Unsafe {methodName} can observe the wrong {optionKind} variant");
            return;
        }

        InspectDereference(
            new BoundVariableExpression(span, receiver),
            span,
            state);
    }

    private void InspectDereference(
        BoundExpression target,
        TextSpan span,
        FlowState state)
    {
        if (!_enabled.HasFlag(TypedBugPatternKind.NullDereference))
            return;

        var value = Evaluate(target, state);
        if (value.Presence == Presence.Absent)
        {
            Report(
                span,
                DiagnosticCode.NullDereference,
                DiagnosticSeverity.Error,
                "Null dereference is guaranteed on this path");
        }
        else if (value.Presence == Presence.Maybe)
        {
            Report(
                span,
                DiagnosticCode.NullDereference,
                DiagnosticSeverity.Warning,
                "Null dereference is reachable on this path");
        }
    }

    private void CheckOffByOne()
    {
        foreach (var loop in _loops.Values)
        {
            if (!_loopEntryStates.TryGetValue(loop.LoopVariable.Id, out var entry))
                entry = CreateInitialState();
            if (LoopBodyDefines(loop.Body, loop.LoopVariable)
                || BoundNodeHelpers.GetUsedVariables(loop.From)
                    .Concat(BoundNodeHelpers.GetUsedVariables(loop.To))
                    .Any(variable => LoopBodyDefines(loop.Body, variable)))
            {
                ReportIncomplete(
                    loop.Span,
                    "Off-by-one analysis is incomplete because the induction or bound state changes inside the loop");
                continue;
            }

            if (!LoopStepSemantics.TryEvaluate(loop.Step, out var step))
            {
                ReportIncomplete(
                    loop.Span,
                    "Off-by-one analysis is incomplete because the loop step direction is unknown");
                continue;
            }
            if (step.IsZero)
            {
                ReportIncomplete(
                    loop.Span,
                    "Off-by-one analysis is incomplete because the loop step is zero");
                continue;
            }

            var from = Evaluate(loop.From, entry).Numeric;
            var to = Evaluate(loop.To, entry).Numeric;
            var lengthBound = Evaluate(
                step.Sign > 0 ? loop.To : loop.From,
                entry).Numeric;
            if (lengthBound?.Length == null)
                continue;
            if (from == null || to == null)
            {
                ReportIncomplete(
                    loop.Span,
                    "Off-by-one analysis is incomplete because the initial loop condition is unknown");
                continue;
            }
            var bodyIsReachable = step.Sign > 0
                ? from.Value.Minimum <= to.Value.Maximum
                : from.Value.Maximum >= to.Value.Minimum;
            if (!bodyIsReachable)
                continue;

            foreach (var access in EnumerateSequenceAccesses(loop.Body))
            {
                if (access.Sequence is BoundVariableExpression sequenceVariable
                    && LoopBodyDefines(loop.Body, sequenceVariable.Variable))
                {
                    ReportIncomplete(
                        access.Span,
                        "Off-by-one analysis is incomplete because the indexed sequence is reassigned inside the loop");
                    continue;
                }
                var sequence = Evaluate(access.Sequence, entry).Sequence;
                if (sequence == null
                    || sequence.ReferenceId
                    != lengthBound.Value.Length.Value.ReferenceId)
                {
                    continue;
                }

                for (var dimension = 0; dimension < access.Indices.Count; dimension++)
                {
                    if (!TryGetInductionOffset(
                            access.Indices[dimension],
                            loop.LoopVariable,
                            out var indexOffset))
                    {
                        continue;
                    }

                    var lengthTerm = lengthBound.Value.Length.Value;
                    if (lengthTerm.Dimension != dimension)
                        continue;

                    var dimensionLength = dimension < sequence.Dimensions.Length
                        ? sequence.Dimensions[dimension]
                        : null;
                    if (from.Value.IsExact
                        && to.Value.IsExact
                        && dimensionLength is { IsExact: true } exactLength)
                    {
                        var last = GetLastReachableLoopValue(
                            from.Value.Minimum,
                            to.Value.Minimum,
                            step);
                        var firstIndex = from.Value.Minimum + indexOffset;
                        var lastIndex = last + indexOffset;
                        var minimumIndex = BigInteger.Min(firstIndex, lastIndex);
                        var maximumIndex = BigInteger.Max(firstIndex, lastIndex);
                        if (minimumIndex < 0
                            || maximumIndex >= exactLength.Minimum)
                        {
                            Report(
                                access.Span,
                                DiagnosticCode.OffByOne,
                                DiagnosticSeverity.Warning,
                                $"Inclusive loop bound reaches outside dimension {dimension} for '{loop.LoopVariable.Name}'");
                        }
                        continue;
                    }

                    if (BigInteger.Abs(step) > BigInteger.One)
                    {
                        ReportIncomplete(
                            access.Span,
                            "Off-by-one analysis is incomplete because endpoint reachability depends on step divisibility");
                        continue;
                    }

                    var upperViolation = lengthTerm.Offset + indexOffset >= 0;
                    var lowerViolation = false;
                    var terminal = Evaluate(
                        step.Sign > 0 ? loop.From : loop.To,
                        entry);
                    if (TryGetExactInteger(terminal, out var terminalValue))
                    {
                        lowerViolation = terminalValue + indexOffset < 0;
                    }
                    if (!upperViolation && !lowerViolation)
                        continue;

                    Report(
                        access.Span,
                        DiagnosticCode.OffByOne,
                        DiagnosticSeverity.Warning,
                        $"Inclusive loop bound reaches outside dimension {dimension} for '{loop.LoopVariable.Name}'");
                }
            }
        }
    }

    private static BigInteger GetLastReachableLoopValue(
        BigInteger from,
        BigInteger to,
        BigInteger step)
    {
        if (step.Sign > 0)
            return from + ((to - from) / step) * step;
        var magnitude = BigInteger.Abs(step);
        return from - ((from - to) / magnitude) * magnitude;
    }

    private static IEnumerable<SequenceAccess> EnumerateSequenceAccesses(
        IReadOnlyList<BoundStatement> body)
    {
        foreach (var statement in body)
        {
            foreach (var node in BoundNodeHelpers.DescendantsAndSelf(statement))
            {
                if (node is BoundArrayAccess access)
                    yield return new SequenceAccess(access.Array, [access.Index], access.Span);
                else if (node is BoundMultiDimArrayAccess multi)
                    yield return new SequenceAccess(multi.Array, multi.Indices, multi.Span);
                else if (node is BoundArrayAccessExpression legacy)
                    yield return new SequenceAccess(legacy.Array, legacy.Indices, legacy.Span);
            }
        }
    }

    private static bool TryGetInductionOffset(
        BoundExpression expression,
        VariableSymbol induction,
        out BigInteger offset)
    {
        if (expression is BoundVariableExpression variable
            && BoundNodeHelpers.SameSymbol(variable.Variable, induction))
        {
            offset = BigInteger.Zero;
            return true;
        }

        if (expression is BoundBinaryExpression binary
            && binary.Left is BoundVariableExpression left
            && BoundNodeHelpers.SameSymbol(left.Variable, induction)
            && TryGetLiteralInteger(binary.Right, out var literal))
        {
            if (binary.Operator == BinaryOperator.Add)
            {
                offset = literal;
                return true;
            }
            if (binary.Operator == BinaryOperator.Subtract)
            {
                offset = -literal;
                return true;
            }
        }

        offset = default;
        return false;
    }

    private AbstractValue Evaluate(BoundExpression expression, FlowState state)
    {
        if (expression is BoundVariableExpression variable)
        {
            return state.TryGet(variable.Variable.Id, out var value)
                ? value
                : UnknownForVariable(variable.Variable);
        }
        if (expression is BoundIntLiteral integer)
        {
            var value = integer.IsUnsigned
                ? new BigInteger(integer.UnsignedValue)
                : new BigInteger(integer.Value);
            return AbstractValue.NumericValue(
                expression.Type.DisplayString,
                NumericDomain.Exact(value));
        }
        if (expression is BoundFloatLiteral)
            return AbstractValue.Unknown(expression.Type.DisplayString);
        if (expression is BoundDecimalLiteral decimalLiteral)
        {
            return AbstractValue.DecimalValue(
                expression.Type.DisplayString,
                DecimalDomain.Constant(decimalLiteral.Value));
        }
        if (expression is BoundNoneLiteral)
        {
            return AbstractValue.VariantValue(
                expression.Type.DisplayString,
                VariantState.OptionNone,
                $"none:{expression.Span.Start}");
        }
        if (expression is BoundSomeExpression)
        {
            return AbstractValue.VariantValue(
                expression.Type.DisplayString,
                VariantState.OptionSome,
                $"some:{expression.Span.Start}");
        }
        if (expression is BoundOkExpression)
        {
            return AbstractValue.VariantValue(
                expression.Type.DisplayString,
                VariantState.ResultOk,
                $"ok:{expression.Span.Start}");
        }
        if (expression is BoundErrExpression)
        {
            return AbstractValue.VariantValue(
                expression.Type.DisplayString,
                VariantState.ResultErr,
                $"err:{expression.Span.Start}");
        }
        if (expression is BoundStringLiteral text)
        {
            return AbstractValue.SequenceValue(
                expression.Type.DisplayString,
                new SequenceShape(
                    SequenceKind.String,
                    $"string:{expression.Span.Start}",
                    [NumericDomain.Exact(text.Value.Length)]),
                Presence.Present);
        }
        if (expression is BoundArrayCreation array)
        {
            var arraySize = array.Size == null
                ? NumericDomain.Exact(array.Initializer.Count)
                : Evaluate(array.Size, state).Numeric;
            return AbstractValue.SequenceValue(
                expression.Type.DisplayString,
                new SequenceShape(
                    SequenceKind.Array,
                    $"array:{array.Span.Start}",
                    [arraySize]),
                Presence.Present);
        }
        if (expression is BoundMultiDimArrayCreation multi)
        {
            return AbstractValue.SequenceValue(
                expression.Type.DisplayString,
                new SequenceShape(
                    SequenceKind.MultiDimensionalArray,
                    $"array:{multi.Span.Start}",
                    multi.DimensionSizes
                        .Select(size => Evaluate(size, state).Numeric)
                        .ToArray()),
                Presence.Present);
        }
        if (expression is BoundArrayLength length)
        {
            var sequence = Evaluate(length.Array, state).Sequence;
            if (sequence == null)
                return AbstractValue.Unknown(expression.Type.DisplayString);

            NumericDomain? numeric;
            var dimension = sequence.Kind == SequenceKind.MultiDimensionalArray
                ? -1
                : 0;
            if (sequence.Dimensions.All(item => item is { IsExact: true }))
            {
                var product = BigInteger.One;
                foreach (var item in sequence.Dimensions)
                    product *= item!.Value.Minimum;
                numeric = NumericDomain.Exact(product);
            }
            else
            {
                numeric = new NumericDomain(BigInteger.Zero, int.MaxValue);
            }

            return AbstractValue.NumericValue(
                expression.Type.DisplayString,
                numeric.Value with
                {
                    Length = new LengthTerm(
                        sequence.ReferenceId,
                        dimension,
                        BigInteger.Zero),
                });
        }
        if (expression is BoundTypeOperationExpression conversion
            && conversion.Operation == TypeOp.Cast)
        {
            if (TryEvaluateIntegralConstant(conversion, out var constant)
                && TryGetIntegralType(conversion.TargetType, out var integral))
            {
                return AbstractValue.NumericValue(
                    conversion.TargetType,
                    Wrap(NumericDomain.Exact(constant), integral));
            }
            return ConvertValue(
                Evaluate(conversion.Operand, state),
                conversion.TargetType);
        }
        if (expression is BoundUnaryExpression unary)
        {
            var operand = Evaluate(unary.Operand, state);
            if (unary.Operator == UnaryOperator.Negate
                && operand.Numeric is { } numeric
                && TryGetIntegralType(
                    GetUnaryArithmeticType(unary.Operand.TypeName),
                    out var integral))
            {
                return AbstractValue.NumericValue(
                    integral.CanonicalName,
                    Wrap(numeric.Negate(), integral));
            }
            return AbstractValue.Unknown(expression.Type.DisplayString);
        }
        if (expression is BoundBinaryExpression binary)
            return EvaluateBinary(binary, state);
        if (expression is BoundConditionalExpression conditional)
        {
            var conditionState = InvalidateRefArguments(
                conditional.Condition,
                state);
            if (TryEvaluateCondition(
                    conditional.Condition,
                    conditionState,
                    out var constant))
            {
                return Evaluate(
                    constant
                        ? conditional.WhenTrue
                        : conditional.WhenFalse,
                    RefineCondition(
                        conditional.Condition,
                        constant,
                        conditionState));
            }
            var whenTrueState = RefineCondition(
                conditional.Condition,
                assumeTrue: true,
                conditionState);
            var whenFalseState = RefineCondition(
                conditional.Condition,
                assumeTrue: false,
                conditionState);
            if (!whenTrueState.IsReachable)
                return Evaluate(conditional.WhenFalse, whenFalseState);
            if (!whenFalseState.IsReachable)
                return Evaluate(conditional.WhenTrue, whenTrueState);
            return AbstractValue.Join(
                Evaluate(conditional.WhenTrue, whenTrueState),
                Evaluate(conditional.WhenFalse, whenFalseState));
        }

        var canonicalType = TypeIdentity.Canonicalize(expression.Type.DisplayString);
        if (TryGetSequenceKind(canonicalType, out var kind, out var rank))
        {
            return AbstractValue.SequenceValue(
                expression.Type.DisplayString,
                new SequenceShape(
                    kind,
                    $"expression:{expression.Span.Start}",
                    Enumerable.Repeat<NumericDomain?>(null, rank).ToArray()),
                Presence.Unknown);
        }

        return AbstractValue.Unknown(expression.Type.DisplayString);
    }

    private AbstractValue EvaluateBinary(
        BoundBinaryExpression binary,
        FlowState state)
    {
        if (IsComparison(binary.Operator)
            || binary.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            return AbstractValue.Unknown("BOOL");
        }

        var left = Evaluate(binary.Left, state);
        var rightState = InvalidateRefArguments(binary.Left, state);
        var right = Evaluate(binary.Right, rightState);
        var type = binary.Operator is BinaryOperator.LeftShift
            or BinaryOperator.RightShift
            ? GetShiftResultType(binary.Left.TypeName)
            : GetArithmeticType(
                binary.Left,
                binary.Right,
                binary.TypeName);
        if (TypeIdentity.Canonicalize(type) == "DECIMAL")
        {
            if (left.Decimal == null || right.Decimal == null)
                return AbstractValue.Unknown(binary.TypeName);
            var decimalDomain = binary.Operator == BinaryOperator.Subtract
                && AreProvablySameValue(binary.Left, binary.Right)
                    ? DecimalDomain.Constant(0m)
                    : DecimalDomain.Apply(
                        binary.Operator,
                        left.Decimal.Value,
                        right.Decimal.Value);
            return decimalDomain == null
                ? AbstractValue.Unknown(binary.TypeName)
                : AbstractValue.DecimalValue(type, decimalDomain.Value);
        }

        if (left.Numeric == null
            || right.Numeric == null
            || !TryGetIntegralType(type, out var integral))
        {
            return AbstractValue.Unknown(binary.TypeName);
        }

        NumericDomain mathematical;
        if (binary.Operator == BinaryOperator.Add)
            mathematical = left.Numeric.Value.Add(right.Numeric.Value);
        else if (binary.Operator == BinaryOperator.Subtract)
        {
            mathematical = AreProvablySameValue(binary.Left, binary.Right)
                ? NumericDomain.Exact(BigInteger.Zero)
                : left.Numeric.Value.Subtract(right.Numeric.Value);
        }
        else if (binary.Operator == BinaryOperator.Multiply)
            mathematical = left.Numeric.Value.Multiply(right.Numeric.Value);
        else if (binary.Operator == BinaryOperator.Divide)
            mathematical = left.Numeric.Value.Divide(right.Numeric.Value);
        else if (binary.Operator == BinaryOperator.Modulo)
            mathematical = NumericDomain.ForType(type);
        else if (binary.Operator == BinaryOperator.LeftShift
                 && TryGetMaskedShiftCount(
                     right.Numeric.Value,
                     integral,
                     out var leftShiftCount))
        {
            mathematical = left.Numeric.Value.ShiftLeft(leftShiftCount);
        }
        else if (binary.Operator == BinaryOperator.RightShift
                 && TryGetMaskedShiftCount(
                     right.Numeric.Value,
                     integral,
                     out var rightShiftCount))
        {
            mathematical = left.Numeric.Value.ShiftRight(rightShiftCount);
        }
        else
        {
            return AbstractValue.Unknown(binary.TypeName);
        }

        return AbstractValue.NumericValue(type, Wrap(mathematical, integral));
    }

    private AbstractValue EvaluateCompound(
        BoundCompoundAssignment compound,
        FlowState state)
    {
        if (compound.Target is not BoundVariableExpression target
            || !state.TryGet(target.Variable.Id, out var left)
            || left.Numeric == null)
        {
            return AbstractValue.Unknown(compound.Target.TypeName);
        }

        var right = Evaluate(compound.Value, state);
        if (right.Numeric == null
            || !TryGetIntegralType(target.Variable.TypeName, out var integral))
        {
            return AbstractValue.Unknown(target.Variable.TypeName);
        }

        var mathematical = compound.Operator switch
        {
            CompoundAssignmentOperator.Add => left.Numeric.Value.Add(right.Numeric.Value),
            CompoundAssignmentOperator.Subtract => left.Numeric.Value.Subtract(right.Numeric.Value),
            CompoundAssignmentOperator.Multiply => left.Numeric.Value.Multiply(right.Numeric.Value),
            CompoundAssignmentOperator.Divide => left.Numeric.Value.Divide(right.Numeric.Value),
            CompoundAssignmentOperator.Modulo => NumericDomain.ForType(target.Variable.TypeName),
            CompoundAssignmentOperator.LeftShift
                when TryGetMaskedShiftCount(
                    right.Numeric.Value,
                    integral,
                    out var leftShiftCount)
                => left.Numeric.Value.ShiftLeft(leftShiftCount),
            CompoundAssignmentOperator.RightShift
                when TryGetMaskedShiftCount(
                    right.Numeric.Value,
                    integral,
                    out var rightShiftCount)
                => left.Numeric.Value.ShiftRight(rightShiftCount),
            _ => NumericDomain.ForType(target.Variable.TypeName),
        };
        return AbstractValue.NumericValue(
            target.Variable.TypeName,
            Wrap(mathematical, integral));
    }

    private AbstractValue ConvertValue(AbstractValue value, string targetType)
    {
        if (value.Numeric is not { } numeric
            || !TryGetIntegralType(targetType, out var target))
        {
            return value.Retype(targetType);
        }
        return AbstractValue.NumericValue(targetType, Wrap(numeric, target));
    }

    private AbstractValue UnknownForVariable(VariableSymbol variable)
    {
        var canonical = TypeIdentity.Canonicalize(variable.TypeName);
        if (TryGetIntegralType(canonical, out _))
        {
            return AbstractValue.NumericValue(
                variable.TypeName,
                NumericDomain.ForType(variable.TypeName));
        }

        var optionKind = GetOptionKind(canonical);
        if (optionKind != OptionKind.None)
        {
            return AbstractValue.VariantValue(
                variable.TypeName,
                VariantState.Maybe,
                $"value:{variable.Id.Value}");
        }

        if (canonical == "DECIMAL")
        {
            return AbstractValue.DecimalValue(
                variable.TypeName,
                new DecimalDomain(null));
        }

        if (TryGetSequenceKind(canonical, out var sequenceKind, out var rank))
        {
            return AbstractValue.SequenceValue(
                variable.TypeName,
                new SequenceShape(
                    sequenceKind,
                    $"sequence:{variable.Id.Value}",
                    Enumerable.Repeat<NumericDomain?>(null, rank).ToArray()),
                Presence.Present);
        }

        return AbstractValue.Unknown(variable.TypeName);
    }

    private FlowState InvalidateRefArguments(
        BoundStatement statement,
        FlowState state)
    {
        var result = state;
        if (statement is BoundCallStatement call)
            result = InvalidateCallArguments(call.Arguments, call.ArgumentModifiers, result);
        foreach (var expression in BoundNodeHelpers.GetImmediateExpressions(statement))
            result = InvalidateRefArguments(expression, result);
        return result;
    }

    private FlowState InvalidateRefArguments(
        BoundExpression expression,
        FlowState state)
    {
        if (expression is BoundConditionalExpression conditional)
        {
            var conditionState = InvalidateRefArguments(
                conditional.Condition,
                state);
            if (TryEvaluateCondition(
                    conditional.Condition,
                    conditionState,
                    out var constant))
            {
                return InvalidateRefArguments(
                    constant
                        ? conditional.WhenTrue
                        : conditional.WhenFalse,
                    RefineCondition(
                        conditional.Condition,
                        constant,
                        conditionState));
            }
            var whenTrue = RefineCondition(
                conditional.Condition,
                assumeTrue: true,
                conditionState);
            var whenFalse = RefineCondition(
                conditional.Condition,
                assumeTrue: false,
                conditionState);
            if (whenTrue.IsReachable)
                whenTrue = InvalidateRefArguments(conditional.WhenTrue, whenTrue);
            if (whenFalse.IsReachable)
                whenFalse = InvalidateRefArguments(conditional.WhenFalse, whenFalse);
            return FlowState.Join(whenTrue, whenFalse);
        }

        if (expression is BoundBinaryExpression logical
            && logical.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            var leftState = InvalidateRefArguments(logical.Left, state);
            var evaluateRightWhen = logical.Operator == BinaryOperator.And;
            if (TryEvaluateCondition(logical.Left, leftState, out var leftConstant))
            {
                return leftConstant == evaluateRightWhen
                    ? InvalidateRefArguments(
                        logical.Right,
                        RefineCondition(logical.Left, leftConstant, leftState))
                    : leftState;
            }
            var skipState = RefineCondition(
                logical.Left,
                !evaluateRightWhen,
                leftState);
            var rightState = RefineCondition(
                logical.Left,
                evaluateRightWhen,
                leftState);
            if (rightState.IsReachable)
                rightState = InvalidateRefArguments(logical.Right, rightState);
            return FlowState.Join(skipState, rightState);
        }

        var result = state;
        foreach (var child in expression.Children)
            result = InvalidateRefArguments(child, result);
        if (expression is BoundCallExpression call)
        {
            result = InvalidateCallArguments(
                call.Arguments,
                call.ArgumentModifiers,
                result);
        }
        return result;
    }

    private FlowState InvalidateCallArguments(
        IReadOnlyList<BoundExpression> arguments,
        IReadOnlyList<string?>? modifiers,
        FlowState state)
    {
        if (modifiers == null)
            return state;
        var result = state;
        for (var index = 0;
             index < arguments.Count && index < modifiers.Count;
             index++)
        {
            var modifier = modifiers[index];
            if (!string.Equals(modifier, "ref", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(modifier, "out", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (arguments[index] is not BoundVariableExpression variable)
            {
                continue;
            }
            result = result.StrongUpdate(
                variable.Variable,
                UnknownForVariable(variable.Variable));
        }
        return result;
    }

    private static NumericDomain Wrap(
        NumericDomain value,
        IntegralType target)
    {
        if (value.Minimum >= target.Minimum && value.Maximum <= target.Maximum)
            return value;
        if (!value.IsExact)
            return NumericDomain.ForType(target.CanonicalName);

        var modulus = BigInteger.One << target.Bits;
        var wrapped = value.Minimum % modulus;
        if (wrapped.Sign < 0)
            wrapped += modulus;
        if (target.Signed && wrapped > target.Maximum)
            wrapped -= modulus;
        return NumericDomain.Exact(wrapped);
    }

    private static bool TryResolveLengthTerm(
        BoundExpression expression,
        FlowState state,
        out LengthTerm length)
    {
        var analyzer = new LightweightEvaluator(state);
        var value = analyzer.EvaluateLength(expression);
        if (value != null)
        {
            length = value.Value;
            return true;
        }
        length = default;
        return false;
    }

    private static bool TryGetNoneComparison(
        BoundExpression left,
        BoundExpression right,
        BinaryOperator op,
        out VariableSymbol variable,
        out VariantState variant)
    {
        if (left is BoundVariableExpression candidate
            && right is BoundNoneLiteral
            && GetOptionKind(candidate.Variable.TypeName) == OptionKind.Option
            && op is BinaryOperator.Equal or BinaryOperator.NotEqual)
        {
            variable = candidate.Variable;
            variant = op == BinaryOperator.Equal
                ? VariantState.OptionNone
                : VariantState.OptionSome;
            return true;
        }
        if (right is BoundVariableExpression reversedCandidate
            && left is BoundNoneLiteral
            && GetOptionKind(reversedCandidate.Variable.TypeName) == OptionKind.Option
            && op is BinaryOperator.Equal or BinaryOperator.NotEqual)
        {
            variable = reversedCandidate.Variable;
            variant = op == BinaryOperator.Equal
                ? VariantState.OptionNone
                : VariantState.OptionSome;
            return true;
        }
        variable = null!;
        variant = VariantState.Unknown;
        return false;
    }

    private static bool TryGetOptionPredicate(
        BoundExpression expression,
        out VariableSymbol variable,
        out VariantState trueVariant,
        out VariantState falseVariant)
    {
        if (expression is BoundCallExpression call
            && call.ReceiverSymbol != null
            && call.ResolvedMethodName != null
            && IsOptionPredicate(
                GetOptionKind(call.ReceiverSymbol.TypeName),
                call.ResolvedMethodName,
                out trueVariant,
                out falseVariant))
        {
            variable = call.ReceiverSymbol;
            return true;
        }
        if (expression is BoundFieldAccessExpression
            {
                Target: BoundVariableExpression target,
            } field
            && IsOptionPredicate(
                GetOptionKind(target.Variable.TypeName),
                field.FieldName,
                out trueVariant,
                out falseVariant))
        {
            variable = target.Variable;
            return true;
        }
        variable = null!;
        trueVariant = VariantState.Unknown;
        falseVariant = VariantState.Unknown;
        return false;
    }

    private static bool IsOptionPredicate(
        OptionKind kind,
        string methodName,
        out VariantState trueVariant,
        out VariantState falseVariant)
    {
        trueVariant = VariantState.Unknown;
        falseVariant = VariantState.Unknown;
        if (kind == OptionKind.Option
            && MatchesExact(methodName, "IsSome", "is_some", "HasValue", "has_value"))
        {
            trueVariant = VariantState.OptionSome;
            falseVariant = VariantState.OptionNone;
            return true;
        }
        if (kind == OptionKind.Option
            && MatchesExact(methodName, "IsNone", "is_none"))
        {
            trueVariant = VariantState.OptionNone;
            falseVariant = VariantState.OptionSome;
            return true;
        }
        if (kind == OptionKind.Result
            && MatchesExact(methodName, "IsOk", "is_ok"))
        {
            trueVariant = VariantState.ResultOk;
            falseVariant = VariantState.ResultErr;
            return true;
        }
        if (kind == OptionKind.Result
            && MatchesExact(methodName, "IsErr", "is_err"))
        {
            trueVariant = VariantState.ResultErr;
            falseVariant = VariantState.ResultOk;
            return true;
        }
        return false;
    }

    private static bool TryGetUnsafeOptionRequirement(
        OptionKind kind,
        string methodName,
        out VariantState requiredVariant)
    {
        requiredVariant = VariantState.Unknown;
        if (kind == OptionKind.Option
            && MatchesExact(
                methodName,
                "Unwrap",
                "unwrap",
                "UnwrapUnchecked",
                "unwrap_unchecked",
                "Expect",
                "expect",
                "GetUnchecked",
                "get_unchecked"))
        {
            requiredVariant = VariantState.OptionSome;
            return true;
        }
        if (kind == OptionKind.Result
            && MatchesExact(
                methodName,
                "Unwrap",
                "unwrap",
                "UnwrapUnchecked",
                "unwrap_unchecked",
                "Expect",
                "expect",
                "GetUnchecked",
                "get_unchecked"))
        {
            requiredVariant = VariantState.ResultOk;
            return true;
        }
        if (kind == OptionKind.Result
            && MatchesExact(methodName, "UnwrapErr", "unwrap_err"))
        {
            requiredVariant = VariantState.ResultErr;
            return true;
        }
        if (kind == OptionKind.None)
            return false;
        return false;
    }

    private static bool IsSafeOptionOperation(
        OptionKind kind,
        string methodName)
    {
        if (kind == OptionKind.None)
            return false;
        return MatchesExact(
            methodName,
            "UnwrapOr",
            "unwrap_or",
            "UnwrapOrDefault",
            "unwrap_or_default",
            "UnwrapOrElse",
            "unwrap_or_else",
            "GetOrInsert",
            "get_or_insert",
            "MapOr",
            "map_or",
            "MapOrElse",
            "map_or_else");
    }

    private static bool MatchesExact(string value, params string[] candidates) =>
        candidates.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static OptionKind GetOptionKind(string typeName)
    {
        var canonical = TypeIdentity.Canonicalize(typeName);
        if (canonical.StartsWith("OPTION<", StringComparison.Ordinal))
            return OptionKind.Option;
        if (canonical.StartsWith("Result<", StringComparison.OrdinalIgnoreCase))
            return OptionKind.Result;
        return OptionKind.None;
    }

    private static bool TryGetSequenceKind(
        string canonicalType,
        out SequenceKind kind,
        out int rank)
    {
        if (canonicalType == "STRING")
        {
            kind = SequenceKind.String;
            rank = 1;
            return true;
        }

        var open = canonicalType.LastIndexOf('[');
        if (open > 0 && canonicalType.EndsWith(']'))
        {
            var commas = canonicalType.AsSpan(open + 1, canonicalType.Length - open - 2)
                .Count(',');
            rank = commas + 1;
            kind = rank == 1
                ? SequenceKind.Array
                : SequenceKind.MultiDimensionalArray;
            return true;
        }

        kind = default;
        rank = 0;
        return false;
    }

    private static bool TryGetIntegralType(
        string typeName,
        out IntegralType type)
    {
        var canonical = TypeIdentity.Canonicalize(typeName);
        type = canonical switch
        {
            "INT[bits=8][signed=true]" => IntegralType.Create(8, true, canonical, "i8"),
            "INT[bits=8][signed=false]" => IntegralType.Create(8, false, canonical, "u8"),
            "INT[bits=16][signed=true]" => IntegralType.Create(16, true, canonical, "i16"),
            "INT[bits=16][signed=false]" => IntegralType.Create(16, false, canonical, "u16"),
            "INT" => IntegralType.Create(32, true, canonical, "i32"),
            "UINT" => IntegralType.Create(32, false, canonical, "u32"),
            "LONG" => IntegralType.Create(64, true, canonical, "i64"),
            "ULONG" => IntegralType.Create(64, false, canonical, "u64"),
            _ => default,
        };
        return type.Bits != 0;
    }

    private static bool IsNumericType(string typeName)
    {
        var canonical = TypeIdentity.Canonicalize(typeName);
        return TryGetIntegralType(canonical, out _)
            || canonical is "FLOAT[bits=32]" or "FLOAT" or "DECIMAL";
    }

    private static string GetArithmeticType(
        BoundExpression leftExpression,
        BoundExpression rightExpression,
        string boundResultType)
    {
        var left = TypeIdentity.Canonicalize(leftExpression.TypeName);
        var right = TypeIdentity.Canonicalize(rightExpression.TypeName);
        if (!TryGetIntegralType(left, out var leftIntegral)
            || !TryGetIntegralType(right, out var rightIntegral))
        {
            return TypeIdentity.Canonicalize(boundResultType);
        }
        var promotedLeft = PromoteNarrowIntegral(leftIntegral);
        var promotedRight = PromoteNarrowIntegral(rightIntegral);

        if (promotedLeft.CanonicalName == "ULONG")
        {
            return GetUlongArithmeticType(
                rightExpression,
                right,
                promotedRight);
        }
        if (promotedRight.CanonicalName == "ULONG")
        {
            return GetUlongArithmeticType(
                leftExpression,
                left,
                promotedLeft);
        }
        if (promotedLeft.CanonicalName == "LONG"
            || promotedRight.CanonicalName == "LONG")
        {
            return "LONG";
        }
        if (promotedLeft.CanonicalName == "UINT")
        {
            return GetUintArithmeticType(
                rightExpression,
                right,
                promotedRight);
        }
        if (promotedRight.CanonicalName == "UINT")
        {
            return GetUintArithmeticType(
                leftExpression,
                left,
                promotedLeft);
        }
        return "INT";
    }

    private static IntegralType PromoteNarrowIntegral(IntegralType type) =>
        type.Bits < 32
            ? IntegralType.Create(32, true, "INT", "i32")
            : type;

    private static string GetUintArithmeticType(
        BoundExpression otherExpression,
        string originalType,
        IntegralType otherType)
    {
        if (!otherType.Signed)
            return "UINT";
        if (originalType == "INT"
            && IsIntegralConstantInRange(
                otherExpression,
                BigInteger.Zero,
                uint.MaxValue))
        {
            return "UINT";
        }
        return "LONG";
    }

    private static string GetUlongArithmeticType(
        BoundExpression otherExpression,
        string originalType,
        IntegralType otherType)
    {
        if (!otherType.Signed)
            return "ULONG";
        if (originalType is "INT" or "LONG"
            && IsIntegralConstantInRange(
                otherExpression,
                BigInteger.Zero,
                ulong.MaxValue))
        {
            return "ULONG";
        }
        return "OBJECT";
    }

    private static bool IsIntegralConstantInRange(
        BoundExpression expression,
        BigInteger minimum,
        BigInteger maximum) =>
        TryEvaluateIntegralConstant(expression, out var value)
        && value >= minimum
        && value <= maximum;

    private static string GetUnaryArithmeticType(string operandType)
    {
        if (!TryGetIntegralType(operandType, out var integral))
            return TypeIdentity.Canonicalize(operandType);
        return integral.CanonicalName switch
        {
            "ULONG" => "ULONG",
            "UINT" => "LONG",
            "LONG" => "LONG",
            _ => "INT",
        };
    }

    private static string GetShiftResultType(string leftType)
    {
        if (!TryGetIntegralType(leftType, out var integral))
            return TypeIdentity.Canonicalize(leftType);
        return integral.Bits < 32 ? "INT" : integral.CanonicalName;
    }

    private static bool TryGetMaskedShiftCount(
        NumericDomain count,
        IntegralType leftType,
        out int maskedCount)
    {
        if (!count.IsExact)
        {
            maskedCount = 0;
            return false;
        }
        var mask = leftType.Bits == 64 ? 0x3f : 0x1f;
        maskedCount = (int)(count.Minimum & mask);
        return true;
    }

    private static bool TryGetLiteralInteger(
        BoundExpression expression,
        out BigInteger value)
    {
        if (expression is BoundIntLiteral integer)
        {
            value = integer.IsUnsigned
                ? new BigInteger(integer.UnsignedValue)
                : new BigInteger(integer.Value);
            return true;
        }
        value = default;
        return false;
    }

    private static bool AreProvablySameValue(
        BoundExpression left,
        BoundExpression right) =>
        left is BoundVariableExpression leftVariable
        && right is BoundVariableExpression rightVariable
        && BoundNodeHelpers.SameSymbol(
            leftVariable.Variable,
            rightVariable.Variable);

    private static bool TryEvaluateIntegralConstant(
        BoundExpression expression,
        out BigInteger value)
    {
        if (TryGetLiteralInteger(expression, out value))
            return true;
        if (expression is BoundFloatLiteral floating
            && double.IsFinite(floating.Value))
        {
            value = new BigInteger(Math.Truncate(floating.Value));
            return true;
        }
        if (expression is BoundDecimalLiteral decimalLiteral)
        {
            value = new BigInteger(decimal.Truncate(decimalLiteral.Value));
            return true;
        }
        if (expression is BoundTypeOperationExpression
            {
                Operation: TypeOp.Cast,
            } conversion
            && TryEvaluateIntegralConstant(conversion.Operand, out value))
        {
            return true;
        }
        value = default;
        return false;
    }

    private static bool TryGetExactInteger(
        AbstractValue value,
        out BigInteger integer)
    {
        if (value.Numeric is { IsExact: true } numeric)
        {
            integer = numeric.Minimum;
            return true;
        }
        integer = default;
        return false;
    }

    private static bool IsExactZero(AbstractValue value) =>
        value.Numeric is { IsExact: true, Minimum.IsZero: true };

    private static bool IsComparison(BinaryOperator op) =>
        op is BinaryOperator.Equal
            or BinaryOperator.NotEqual
            or BinaryOperator.LessThan
            or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterThan
            or BinaryOperator.GreaterOrEqual;

    private static BinaryOperator NegateComparison(BinaryOperator op) => op switch
    {
        BinaryOperator.Equal => BinaryOperator.NotEqual,
        BinaryOperator.NotEqual => BinaryOperator.Equal,
        BinaryOperator.LessThan => BinaryOperator.GreaterOrEqual,
        BinaryOperator.LessOrEqual => BinaryOperator.GreaterThan,
        BinaryOperator.GreaterThan => BinaryOperator.LessOrEqual,
        BinaryOperator.GreaterOrEqual => BinaryOperator.LessThan,
        _ => op,
    };

    private static BinaryOperator ReverseComparison(BinaryOperator op) => op switch
    {
        BinaryOperator.LessThan => BinaryOperator.GreaterThan,
        BinaryOperator.LessOrEqual => BinaryOperator.GreaterOrEqual,
        BinaryOperator.GreaterThan => BinaryOperator.LessThan,
        BinaryOperator.GreaterOrEqual => BinaryOperator.LessOrEqual,
        _ => op,
    };

    private void ReportUnknown(
        TypedBugPatternKind kind,
        TextSpan span,
        string message)
    {
        ReportIncomplete(span, message);
        if (_options.ReportOnlyVerified)
            return;

        var code = kind switch
        {
            TypedBugPatternKind.DivisionByZero => DiagnosticCode.DivisionByZeroHint,
            TypedBugPatternKind.IndexOutOfBounds => DiagnosticCode.IndexOutOfBoundsHint,
            TypedBugPatternKind.NullDereference => DiagnosticCode.NullDereferenceHint,
            TypedBugPatternKind.IntegerOverflow => DiagnosticCode.IntegerOverflowHint,
            TypedBugPatternKind.OffByOne => DiagnosticCode.OffByOneHint,
            _ => DiagnosticCode.BugPatternAnalysisIncomplete,
        };
        Report(span, code, DiagnosticSeverity.Info, $"Heuristic hint: {message}");
    }

    private void ReportIncomplete(TextSpan span, string message) =>
        Report(
            span,
            DiagnosticCode.BugPatternAnalysisIncomplete,
            DiagnosticSeverity.Info,
            message);

    private void Report(
        TextSpan span,
        string code,
        DiagnosticSeverity severity,
        string message)
    {
        if (!_reported.Add(new DiagnosticKey(span.Start, span.End, code, message)))
            return;

        if (severity == DiagnosticSeverity.Error)
            _diagnostics.ReportError(span, code, message);
        else if (severity == DiagnosticSeverity.Warning)
            _diagnostics.ReportWarning(span, code, message);
        else
            _diagnostics.ReportInfo(span, code, message);
    }

    private readonly record struct DiagnosticKey(
        int Start,
        int End,
        string Code,
        string Message);

    private readonly record struct SequenceAccess(
        BoundExpression Sequence,
        IReadOnlyList<BoundExpression> Indices,
        TextSpan Span);

    private enum OptionKind
    {
        None,
        Option,
        Result,
    }

    private enum Presence
    {
        Unknown,
        Absent,
        Present,
        Maybe,
    }

    private enum VariantState
    {
        Unknown,
        Maybe,
        OptionNone,
        OptionSome,
        ResultErr,
        ResultOk,
    }

    private enum SequenceKind
    {
        Array,
        MultiDimensionalArray,
        String,
    }

    private enum SequenceBoundKind
    {
        LowerZero,
        UpperExclusive,
    }

    private readonly record struct SequenceBound(
        SymbolId IndexId,
        string ReferenceId,
        int Dimension,
        SequenceBoundKind Kind);

    private readonly record struct LengthTerm(
        string ReferenceId,
        int Dimension,
        BigInteger Offset);

    private readonly record struct IntegralType(
        int Bits,
        bool Signed,
        string CanonicalName,
        string DisplayName,
        BigInteger Minimum,
        BigInteger Maximum)
    {
        public static IntegralType Create(
            int bits,
            bool signed,
            string canonicalName,
            string displayName)
        {
            var minimum = signed
                ? -(BigInteger.One << (bits - 1))
                : BigInteger.Zero;
            var maximum = signed
                ? (BigInteger.One << (bits - 1)) - BigInteger.One
                : (BigInteger.One << bits) - BigInteger.One;
            return new IntegralType(
                bits,
                signed,
                canonicalName,
                displayName,
                minimum,
                maximum);
        }
    }

    private readonly record struct NumericDomain(
        BigInteger Minimum,
        BigInteger Maximum,
        bool ExcludesZero = false,
        LengthTerm? Length = null)
    {
        public bool IsExact => Minimum == Maximum;
        public bool ContainsZero =>
            Minimum <= BigInteger.Zero
            && Maximum >= BigInteger.Zero
            && !ExcludesZero;

        public static NumericDomain Exact(BigInteger value) =>
            new(value, value, ExcludesZero: !value.IsZero);

        public static NumericDomain ForType(string typeName)
        {
            return TryGetIntegralType(typeName, out var integral)
                ? new NumericDomain(integral.Minimum, integral.Maximum)
                : new NumericDomain(long.MinValue, long.MaxValue);
        }

        public bool Contains(BigInteger value) =>
            value >= Minimum
            && value <= Maximum
            && (!value.IsZero || !ExcludesZero);

        public NumericDomain? IntersectMinimum(BigInteger minimum)
        {
            var next = BigInteger.Max(Minimum, minimum);
            return next > Maximum
                ? null
                : this with
                {
                    Minimum = next,
                    ExcludesZero = ExcludesZero || next > 0,
                    Length = null,
                };
        }

        public NumericDomain? IntersectMaximum(BigInteger maximum)
        {
            var next = BigInteger.Min(Maximum, maximum);
            return next < Minimum
                ? null
                : this with
                {
                    Maximum = next,
                    ExcludesZero = ExcludesZero || next < 0,
                    Length = null,
                };
        }

        public NumericDomain Add(NumericDomain other)
        {
            LengthTerm? length = Length != null && other.IsExact
                ? Length.Value with { Offset = Length.Value.Offset + other.Minimum }
                : other.Length != null && IsExact
                    ? other.Length.Value with { Offset = other.Length.Value.Offset + Minimum }
                    : null;
            return new NumericDomain(
                Minimum + other.Minimum,
                Maximum + other.Maximum,
                Length: length);
        }

        public NumericDomain Subtract(NumericDomain other)
        {
            LengthTerm? length = Length != null && other.IsExact
                ? Length.Value with { Offset = Length.Value.Offset - other.Minimum }
                : null;
            return new NumericDomain(
                Minimum - other.Maximum,
                Maximum - other.Minimum,
                Length: length);
        }

        public NumericDomain Multiply(NumericDomain other)
        {
            var products = new[]
            {
                Minimum * other.Minimum,
                Minimum * other.Maximum,
                Maximum * other.Minimum,
                Maximum * other.Maximum,
            };
            return new NumericDomain(products.Min(), products.Max());
        }

        public NumericDomain Divide(NumericDomain other)
        {
            var divisors = new HashSet<BigInteger>();
            if (!other.Minimum.IsZero)
                divisors.Add(other.Minimum);
            if (!other.Maximum.IsZero)
                divisors.Add(other.Maximum);
            if (other.Minimum <= -BigInteger.One
                && other.Maximum >= -BigInteger.One)
            {
                divisors.Add(-BigInteger.One);
            }
            if (other.Minimum <= BigInteger.One
                && other.Maximum >= BigInteger.One)
            {
                divisors.Add(BigInteger.One);
            }
            if (divisors.Count == 0)
            {
                var magnitude = BigInteger.One << 128;
                return new NumericDomain(-magnitude, magnitude);
            }

            var numerators = new HashSet<BigInteger>
            {
                Minimum,
                Maximum,
            };
            if (Minimum <= BigInteger.Zero && Maximum >= BigInteger.Zero)
                numerators.Add(BigInteger.Zero);
            var quotients = numerators
                .SelectMany(numerator =>
                    divisors.Select(divisor => numerator / divisor))
                .ToArray();
            return new NumericDomain(quotients.Min(), quotients.Max());
        }

        public NumericDomain Negate() =>
            new(-Maximum, -Minimum, ExcludesZero, Length: null);

        public NumericDomain ShiftLeft(int count) =>
            new(Minimum << count, Maximum << count, Length: null);

        public NumericDomain ShiftRight(int count) =>
            new(Minimum >> count, Maximum >> count, Length: null);

        public static NumericDomain Join(NumericDomain left, NumericDomain right) =>
            new(
                BigInteger.Min(left.Minimum, right.Minimum),
                BigInteger.Max(left.Maximum, right.Maximum),
                left.ExcludesZero && right.ExcludesZero,
                left.Length == right.Length ? left.Length : null);
    }

    private readonly record struct DecimalDomain(
        decimal? Exact,
        bool ExcludesZero = false)
    {
        public bool ContainsZero =>
            Exact == 0m || (Exact == null && !ExcludesZero);

        public static DecimalDomain Constant(decimal value) =>
            new(value, ExcludesZero: value != 0m);

        public DecimalDomain ExcludeZero() =>
            this with { ExcludesZero = true };

        public static DecimalDomain? Apply(
            BinaryOperator operation,
            DecimalDomain left,
            DecimalDomain right)
        {
            if (left.Exact == null || right.Exact == null)
                return new DecimalDomain(null);
            try
            {
                var value = operation switch
                {
                    BinaryOperator.Add => checked(left.Exact.Value + right.Exact.Value),
                    BinaryOperator.Subtract => checked(left.Exact.Value - right.Exact.Value),
                    BinaryOperator.Multiply => checked(left.Exact.Value * right.Exact.Value),
                    _ => (decimal?)null,
                };
                return value == null ? null : Constant(value.Value);
            }
            catch (OverflowException)
            {
                return new DecimalDomain(null);
            }
        }

        public static DecimalDomain Join(
            DecimalDomain left,
            DecimalDomain right) =>
            left.Exact == right.Exact
                ? new DecimalDomain(
                    left.Exact,
                    left.ExcludesZero && right.ExcludesZero)
                : new DecimalDomain(
                    null,
                    left.ExcludesZero && right.ExcludesZero);
    }

    private sealed class SequenceShape : IEquatable<SequenceShape>
    {
        public SequenceKind Kind { get; }
        public string ReferenceId { get; }
        public NumericDomain?[] Dimensions { get; }

        public SequenceShape(
            SequenceKind kind,
            string referenceId,
            NumericDomain?[] dimensions)
        {
            Kind = kind;
            ReferenceId = referenceId;
            Dimensions = dimensions;
        }

        public bool Equals(SequenceShape? other) =>
            other != null
            && Kind == other.Kind
            && ReferenceId == other.ReferenceId
            && Dimensions.SequenceEqual(other.Dimensions);

        public override bool Equals(object? obj) =>
            obj is SequenceShape other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Kind);
            hash.Add(ReferenceId);
            foreach (var dimension in Dimensions)
                hash.Add(dimension);
            return hash.ToHashCode();
        }

        public static SequenceShape? Join(
            SequenceShape? left,
            SequenceShape? right)
        {
            if (left == null || right == null
                || left.Kind != right.Kind
                || left.ReferenceId != right.ReferenceId
                || left.Dimensions.Length != right.Dimensions.Length)
            {
                return null;
            }

            return new SequenceShape(
                left.Kind,
                left.ReferenceId,
                Enumerable.Range(0, left.Dimensions.Length)
                    .Select<int, NumericDomain?>(index =>
                        left.Dimensions[index] == null
                        || right.Dimensions[index] == null
                            ? (NumericDomain?)null
                            : NumericDomain.Join(
                                left.Dimensions[index]!.Value,
                                right.Dimensions[index]!.Value))
                    .ToArray());
        }
    }

    private sealed class AbstractValue : IEquatable<AbstractValue>
    {
        public string TypeName { get; }
        public NumericDomain? Numeric { get; }
        public DecimalDomain? Decimal { get; }
        public Presence Presence { get; }
        public VariantState Variant { get; }
        public SequenceShape? Sequence { get; }
        public string? ValueIdentity { get; }

        private AbstractValue(
            string typeName,
            NumericDomain? numeric,
            DecimalDomain? decimalValue,
            Presence presence,
            VariantState variant,
            SequenceShape? sequence,
            string? valueIdentity)
        {
            TypeName = typeName;
            Numeric = numeric;
            Decimal = decimalValue;
            Presence = presence;
            Variant = variant;
            Sequence = sequence;
            ValueIdentity = valueIdentity;
        }

        public static AbstractValue Unknown(string typeName) =>
            new(
                typeName,
                null,
                null,
                Presence.Unknown,
                VariantState.Unknown,
                null,
                null);

        public static AbstractValue NumericValue(
            string typeName,
            NumericDomain numeric) =>
            new(
                typeName,
                numeric,
                null,
                Presence.Unknown,
                VariantState.Unknown,
                null,
                null);

        public static AbstractValue VariantValue(
            string typeName,
            VariantState variant,
            string? identity) =>
            new(
                typeName,
                null,
                null,
                Presence.Unknown,
                variant,
                null,
                identity);

        public static AbstractValue SequenceValue(
            string typeName,
            SequenceShape sequence,
            Presence presence) =>
            new(
                typeName,
                null,
                null,
                presence,
                VariantState.Unknown,
                sequence,
                sequence.ReferenceId);

        public static AbstractValue DecimalValue(
            string typeName,
            DecimalDomain value) =>
            new(
                typeName,
                null,
                value,
                Presence.Unknown,
                VariantState.Unknown,
                null,
                null);

        public AbstractValue WithNumeric(NumericDomain numeric) =>
            new(
                TypeName,
                numeric,
                Decimal,
                Presence,
                Variant,
                Sequence,
                ValueIdentity);

        public AbstractValue WithDecimal(DecimalDomain value) =>
            new(
                TypeName,
                Numeric,
                value,
                Presence,
                Variant,
                Sequence,
                ValueIdentity);

        public AbstractValue WithPresence(Presence presence) =>
            new(
                TypeName,
                Numeric,
                Decimal,
                presence,
                Variant,
                Sequence,
                ValueIdentity);

        public AbstractValue WithVariant(VariantState variant) =>
            new(
                TypeName,
                Numeric,
                Decimal,
                Presence,
                variant,
                Sequence,
                ValueIdentity);

        public AbstractValue Retype(string typeName) =>
            new(
                typeName,
                Numeric,
                Decimal,
                Presence,
                Variant,
                Sequence,
                ValueIdentity);

        public AbstractValue WithFreshIdentityWhenNeeded(
            VariableSymbol variable,
            TextSpan span)
        {
            if (ValueIdentity != null)
                return this;
            if (GetOptionKind(variable.TypeName) == OptionKind.None)
                return this;
            return new AbstractValue(
                TypeName,
                Numeric,
                Decimal,
                Presence,
                Variant,
                Sequence,
                $"value:{variable.Id.Value}:{span.Start}");
        }

        public bool Equals(AbstractValue? other) =>
            other != null
            && TypeName == other.TypeName
            && Numeric == other.Numeric
            && Decimal == other.Decimal
            && Presence == other.Presence
            && Variant == other.Variant
            && Equals(Sequence, other.Sequence)
            && ValueIdentity == other.ValueIdentity;

        public override bool Equals(object? obj) =>
            obj is AbstractValue other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                TypeName,
                Numeric,
                Decimal,
                Presence,
                Variant,
                Sequence,
                ValueIdentity);

        public static AbstractValue Join(
            AbstractValue left,
            AbstractValue right)
        {
            NumericDomain? numeric = left.Numeric != null && right.Numeric != null
                ? NumericDomain.Join(left.Numeric.Value, right.Numeric.Value)
                : null;
            DecimalDomain? decimalValue = left.Decimal != null && right.Decimal != null
                ? DecimalDomain.Join(left.Decimal.Value, right.Decimal.Value)
                : null;
            var presence = left.Presence == right.Presence
                ? left.Presence
                : Presence.Maybe;
            var variant = left.Variant == right.Variant
                ? left.Variant
                : VariantState.Maybe;
            return new AbstractValue(
                left.TypeName == right.TypeName ? left.TypeName : "OBJECT",
                numeric,
                decimalValue,
                presence,
                variant,
                SequenceShape.Join(left.Sequence, right.Sequence),
                left.ValueIdentity == right.ValueIdentity
                    ? left.ValueIdentity
                    : null);
        }
    }

    private sealed class FlowState : IEquatable<FlowState>
    {
        public static FlowState Empty { get; } = new(
            ImmutableDictionary<SymbolId, AbstractValue>.Empty,
            System.Collections.Immutable.ImmutableHashSet<SequenceBound>.Empty,
            isReachable: true);
        public static FlowState Unreachable { get; } = new(
            ImmutableDictionary<SymbolId, AbstractValue>.Empty,
            System.Collections.Immutable.ImmutableHashSet<SequenceBound>.Empty,
            isReachable: false);

        public ImmutableDictionary<SymbolId, AbstractValue> Values { get; }
        private System.Collections.Immutable.ImmutableHashSet<SequenceBound> Bounds { get; }
        public bool IsReachable { get; }

        private FlowState(
            ImmutableDictionary<SymbolId, AbstractValue> values,
            System.Collections.Immutable.ImmutableHashSet<SequenceBound> bounds,
            bool isReachable)
        {
            Values = values;
            Bounds = bounds;
            IsReachable = isReachable;
        }

        public bool TryGet(SymbolId id, out AbstractValue value) =>
            Values.TryGetValue(id, out value!);

        public FlowState Set(SymbolId id, AbstractValue value) =>
            id.IsNone
                ? this
                : new FlowState(Values.SetItem(id, value), Bounds, IsReachable);

        public FlowState StrongUpdate(
            VariableSymbol variable,
            AbstractValue value,
            bool preserveDependentFacts = false)
        {
            if (variable.Id.IsNone)
                return this;
            var oldReference = Values.TryGetValue(variable.Id, out var old)
                ? old.Sequence?.ReferenceId
                : null;
            var bounds = preserveDependentFacts
                ? Bounds
                : Bounds.Except(Bounds.Where(bound =>
                        bound.IndexId == variable.Id
                        || (oldReference != null
                            && bound.ReferenceId == oldReference)))
                    .ToImmutableHashSet();
            return new FlowState(
                Values.SetItem(variable.Id, value),
                bounds,
                IsReachable);
        }

        public FlowState AddBound(SequenceBound bound) =>
            new(Values, Bounds.Add(bound), IsReachable);

        public bool HasBound(
            SymbolId indexId,
            string referenceId,
            int dimension,
            SequenceBoundKind kind)
        {
            if (kind == SequenceBoundKind.LowerZero)
            {
                return Bounds.Any(bound =>
                    bound.IndexId == indexId
                    && bound.Kind == kind);
            }
            return Bounds.Contains(new SequenceBound(
                indexId,
                referenceId,
                dimension,
                kind));
        }

        public bool Equals(FlowState? other)
        {
            if (other == null
                || IsReachable != other.IsReachable
                || Bounds.Count != other.Bounds.Count
                || Values.Count != other.Values.Count
                || !Bounds.SetEquals(other.Bounds))
            {
                return false;
            }
            return Values.All(pair =>
                other.Values.TryGetValue(pair.Key, out var value)
                && pair.Value.Equals(value));
        }

        public override bool Equals(object? obj) =>
            obj is FlowState other && Equals(other);

        public override int GetHashCode() => Values.Count ^ Bounds.Count;

        public static FlowState Join(FlowState left, FlowState right)
        {
            if (!left.IsReachable)
                return right;
            if (!right.IsReachable)
                return left;

            var values = ImmutableDictionary.CreateBuilder<SymbolId, AbstractValue>();
            foreach (var id in left.Values.Keys.Union(right.Values.Keys))
            {
                if (left.Values.TryGetValue(id, out var leftValue)
                    && right.Values.TryGetValue(id, out var rightValue))
                {
                    values[id] = AbstractValue.Join(leftValue, rightValue);
                }
                else if (left.Values.TryGetValue(id, out leftValue))
                {
                    values[id] = AbstractValue.Join(
                        leftValue,
                        AbstractValue.Unknown(leftValue.TypeName));
                }
                else
                {
                    var onlyRightValue = right.Values[id];
                    values[id] = AbstractValue.Join(
                        AbstractValue.Unknown(onlyRightValue.TypeName),
                        onlyRightValue);
                }
            }
            return new FlowState(
                values.ToImmutable(),
                left.Bounds.Intersect(right.Bounds),
                isReachable: true);
        }
    }

    private sealed class LightweightEvaluator
    {
        private readonly FlowState _state;

        public LightweightEvaluator(FlowState state)
        {
            _state = state;
        }

        public LengthTerm? EvaluateLength(BoundExpression expression)
        {
            if (expression is BoundVariableExpression variable
                && _state.TryGet(variable.Variable.Id, out var variableValue))
            {
                return variableValue.Numeric?.Length;
            }
            if (expression is BoundArrayLength arrayLength)
            {
                var sequence = ResolveSequence(arrayLength.Array);
                if (sequence == null)
                    return null;
                return new LengthTerm(
                    sequence.ReferenceId,
                    sequence.Kind == SequenceKind.MultiDimensionalArray ? -1 : 0,
                    BigInteger.Zero);
            }
            if (expression is BoundBinaryExpression binary
                && binary.Operator is BinaryOperator.Add or BinaryOperator.Subtract
                && EvaluateLength(binary.Left) is { } length
                && TryGetLiteralInteger(binary.Right, out var literal))
            {
                return length with
                {
                    Offset = binary.Operator == BinaryOperator.Add
                        ? length.Offset + literal
                        : length.Offset - literal,
                };
            }
            return null;
        }

        private SequenceShape? ResolveSequence(BoundExpression expression)
        {
            if (expression is BoundVariableExpression variable
                && _state.TryGet(variable.Variable.Id, out var value))
            {
                return value.Sequence;
            }
            if (expression is BoundStringLiteral text)
            {
                return new SequenceShape(
                    SequenceKind.String,
                    $"string:{text.Span.Start}",
                    [NumericDomain.Exact(text.Value.Length)]);
            }
            return null;
        }
    }
}

internal sealed class TypedBugPatternChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;
    private readonly TypedBugPatternKind _enabled;

    public TypedBugPatternChecker(
        BugPatternOptions options,
        TypedBugPatternKind enabled)
    {
        _options = options;
        _enabled = enabled;
    }

    public string Name => "TYPED_CFG_BUG_PATTERNS";

    public void Check(BoundFunction function, DiagnosticBag diagnostics) =>
        TypedBugPatternAnalysis.Check(function, diagnostics, _options, _enabled);
}
