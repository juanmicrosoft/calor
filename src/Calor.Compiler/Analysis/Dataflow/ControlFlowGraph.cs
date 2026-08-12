using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis.Dataflow;

public enum ControlFlowEdgeKind
{
    True,
    False,
    FallThrough,
    BackEdge,
    Break,
    Continue,
    Return,
    Throw,
    Catch,
    Finally,
    Dispatch,
    Unsupported,
}

public enum ControlFlowTerminatorKind
{
    FallThrough,
    Conditional,
    Return,
    Throw,
    Break,
    Continue,
    Continuation,
    Dispatch,
    Unsupported,
    Exit,
}

public enum SyntheticOperationKind
{
    ForInitialization,
    ForStep,
    ForeachCollectionEvaluation,
    ForeachIteration,
    CatchInitialization,
    UsingResourceEvaluation,
    UsingResourceInitialization,
    UsingDispose,
    MatchTargetEvaluation,
    ExpressionEvaluation,
    StatementDefinition,
}

/// <summary>
/// A CFG-only operation used when the bound tree represents an implicit language operation.
/// </summary>
public sealed class SyntheticOperation
{
    public int Ordinal { get; internal set; } = -1;
    public SyntheticOperationKind Kind { get; }
    public VariableSymbol? DefinedVariable { get; }
    public BoundExpression? Expression { get; }
    public bool ReadsDefinedVariable { get; }
    public bool MayThrow { get; }
    public BoundStatement? SourceStatement { get; }
    public TextSpan Span { get; }
    public bool IsDefinition => DefinedVariable != null;

    public SyntheticOperation(
        SyntheticOperationKind kind,
        TextSpan span,
        VariableSymbol? definedVariable = null,
        BoundExpression? expression = null,
        bool readsDefinedVariable = false,
        bool mayThrow = false,
        BoundStatement? sourceStatement = null)
    {
        Kind = kind;
        Span = span;
        DefinedVariable = definedVariable;
        Expression = expression;
        ReadsDefinedVariable = readsDefinedVariable;
        MayThrow = mayThrow;
        SourceStatement = sourceStatement;
    }
}

public sealed class ControlFlowEdge
{
    public BasicBlock Source { get; }
    public BasicBlock Target { get; }
    public ControlFlowEdgeKind Kind { get; }

    internal ControlFlowEdge(BasicBlock source, BasicBlock target, ControlFlowEdgeKind kind)
    {
        Source = source;
        Target = target;
        Kind = kind;
    }

    public override string ToString() => $"BB{Source.Id} -{Kind}-> BB{Target.Id}";
}

public abstract class ControlFlowTerminator
{
    public ControlFlowTerminatorKind Kind { get; }
    public virtual BoundExpression? Condition => null;
    public abstract IReadOnlyList<ControlFlowEdge> OutgoingEdges { get; }
    public virtual bool IsAbrupt => false;

    protected ControlFlowTerminator(ControlFlowTerminatorKind kind)
    {
        Kind = kind;
    }
}

public sealed class FallThroughTerminator : ControlFlowTerminator
{
    public ControlFlowEdge Edge { get; }
    public override IReadOnlyList<ControlFlowEdge> OutgoingEdges => [Edge];

    internal FallThroughTerminator(ControlFlowEdge edge)
        : base(ControlFlowTerminatorKind.FallThrough)
    {
        Edge = edge;
    }
}

public sealed class ConditionalTerminator : ControlFlowTerminator
{
    private readonly IReadOnlyList<ControlFlowEdge> _edges;

    public override BoundExpression Condition { get; }
    public ControlFlowEdge TrueEdge { get; }
    public ControlFlowEdge FalseEdge { get; }
    public ControlFlowEdge? ExceptionalEdge { get; }
    public override IReadOnlyList<ControlFlowEdge> OutgoingEdges => _edges;

    internal ConditionalTerminator(
        BoundExpression condition,
        ControlFlowEdge trueEdge,
        ControlFlowEdge falseEdge,
        ControlFlowEdge? exceptionalEdge)
        : base(ControlFlowTerminatorKind.Conditional)
    {
        Condition = condition;
        TrueEdge = trueEdge;
        FalseEdge = falseEdge;
        ExceptionalEdge = exceptionalEdge;
        _edges = exceptionalEdge == null
            ? [trueEdge, falseEdge]
            : [trueEdge, falseEdge, exceptionalEdge];
    }
}

public sealed class AbruptTerminator : ControlFlowTerminator
{
    public ControlFlowEdge Edge { get; }
    public override IReadOnlyList<ControlFlowEdge> OutgoingEdges => [Edge];
    public override bool IsAbrupt => true;

    internal AbruptTerminator(ControlFlowTerminatorKind kind, ControlFlowEdge edge)
        : base(kind)
    {
        Edge = edge;
    }
}

public sealed class ContinuationTerminator : ControlFlowTerminator
{
    public ControlFlowEdge Edge { get; }
    public override IReadOnlyList<ControlFlowEdge> OutgoingEdges => [Edge];

    internal ContinuationTerminator(ControlFlowEdge edge)
        : base(ControlFlowTerminatorKind.Continuation)
    {
        Edge = edge;
    }
}

public class DispatchTerminator : ControlFlowTerminator
{
    private readonly IReadOnlyList<ControlFlowEdge> _edges;

    public override BoundExpression? Condition { get; }
    public override IReadOnlyList<ControlFlowEdge> OutgoingEdges => _edges;

    internal DispatchTerminator(
        BoundExpression? condition,
        IReadOnlyList<ControlFlowEdge> edges,
        ControlFlowTerminatorKind kind = ControlFlowTerminatorKind.Dispatch)
        : base(kind)
    {
        Condition = condition;
        _edges = edges;
    }
}

public sealed class UnsupportedTerminator : DispatchTerminator
{
    internal UnsupportedTerminator(IReadOnlyList<ControlFlowEdge> edges)
        : base(null, edges, ControlFlowTerminatorKind.Unsupported)
    {
    }
}

public sealed class ExitTerminator : ControlFlowTerminator
{
    public override IReadOnlyList<ControlFlowEdge> OutgoingEdges => Array.Empty<ControlFlowEdge>();

    internal ExitTerminator()
        : base(ControlFlowTerminatorKind.Exit)
    {
    }
}

/// <summary>
/// Represents a basic block in a control flow graph.
/// </summary>
public sealed class BasicBlock
{
    private readonly List<ControlFlowEdge> _incomingEdges = new();
    private readonly List<ControlFlowEdge> _outgoingEdges = new();
    private readonly HashSet<int> _deferredDefinitionStatementIndices = new();
    private ControlFlowTerminator? _terminator;

    public int Id { get; internal set; }
    public int Ordinal { get; internal set; }
    public List<BoundStatement> Statements { get; } = new();
    public List<SyntheticOperation> SyntheticOperations { get; } = new();
    public IReadOnlyList<ControlFlowEdge> IncomingEdges => _incomingEdges;
    public IReadOnlyList<ControlFlowEdge> OutgoingEdges => _outgoingEdges;
    public IReadOnlyList<BasicBlock> Predecessors =>
        _incomingEdges.Select(edge => edge.Source).Distinct().ToArray();
    public IReadOnlyList<BasicBlock> Successors =>
        _outgoingEdges.Select(edge => edge.Target).Distinct().ToArray();
    public BoundExpression? BranchCondition => _terminator?.Condition;
    public bool IsExit { get; internal set; }
    public bool IsEntry { get; internal set; }
    public bool HasTerminator => _terminator != null;
    public ControlFlowTerminator Terminator =>
        _terminator ?? throw new InvalidOperationException($"BB{Id} has no terminator");

    public TextSpan Span
    {
        get
        {
            if (Statements.Count > 0)
            {
                return new TextSpan(
                    Statements[0].Span.Start,
                    Statements[^1].Span.End,
                    Statements[0].Span.Line,
                    Statements[0].Span.Column);
            }

            if (SyntheticOperations.Count > 0)
                return SyntheticOperations[0].Span;

            return BranchCondition?.Span ?? default;
        }
    }

    public BasicBlock()
    {
    }

    internal void SetTerminator(ControlFlowTerminator terminator)
    {
        if (_terminator != null)
            throw new InvalidOperationException($"BB{Id} already has a terminator");

        _terminator = terminator;
    }

    internal void AddOutgoingEdge(ControlFlowEdge edge) => _outgoingEdges.Add(edge);
    internal void AddIncomingEdge(ControlFlowEdge edge) => _incomingEdges.Add(edge);
    internal void RemoveOutgoingEdge(ControlFlowEdge edge) => _outgoingEdges.Remove(edge);
    internal void RemoveIncomingEdge(ControlFlowEdge edge) => _incomingEdges.Remove(edge);
    internal void DeferDefinition(int statementIndex) =>
        _deferredDefinitionStatementIndices.Add(statementIndex);
    public bool IsDefinitionDeferred(int statementIndex) =>
        _deferredDefinitionStatementIndices.Contains(statementIndex);

    public override string ToString() => $"BB{Id} ({Statements.Count} stmts)";
}

public sealed class ControlFlowGraphValidationException : InvalidOperationException
{
    public IReadOnlyList<string> Violations { get; }

    public ControlFlowGraphValidationException(IReadOnlyList<string> violations)
        : base($"Invalid control-flow graph: {string.Join("; ", violations)}")
    {
        Violations = violations;
    }
}

/// <summary>
/// Represents a control flow graph for a function.
/// </summary>
public sealed class ControlFlowGraph
{
    private readonly IReadOnlyList<BasicBlock> _reachableBlocks;

    public BasicBlock Entry { get; }
    public BasicBlock Exit { get; }
    public IReadOnlyList<BasicBlock> Blocks { get; }
    public IReadOnlyList<BasicBlock> ReachableBlocks => _reachableBlocks;
    public BoundFunction Function { get; }

    private ControlFlowGraph(
        BoundFunction function,
        BasicBlock entry,
        BasicBlock exit,
        IReadOnlyList<BasicBlock> blocks)
    {
        Function = function;
        Entry = entry;
        Exit = exit;
        Blocks = blocks;
        _reachableBlocks = ComputeReachableBlocks(entry);
    }

    public static ControlFlowGraph Build(BoundFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new CfgBuilder().Build(function);
    }

    public IReadOnlyList<BasicBlock> GetReversePostOrder()
    {
        var postOrder = GetPostOrder().ToList();
        postOrder.Reverse();
        return postOrder;
    }

    public IReadOnlyList<BasicBlock> GetPostOrder()
    {
        var visited = new HashSet<BasicBlock>();
        var postOrder = new List<BasicBlock>();

        void Visit(BasicBlock block)
        {
            if (!visited.Add(block))
                return;

            foreach (var edge in block.OutgoingEdges.OrderBy(edge => edge.Target.Ordinal))
                Visit(edge.Target);

            postOrder.Add(block);
        }

        Visit(Entry);
        return postOrder;
    }

    public void Validate()
    {
        var violations = new List<string>();
        var blockSet = Blocks.ToHashSet();

        if (!blockSet.Contains(Entry))
            violations.Add("entry block is not in Blocks");
        if (!blockSet.Contains(Exit))
            violations.Add("exit block is not in Blocks");
        if (!Entry.IsEntry)
            violations.Add("entry block is not marked as entry");
        if (!Exit.IsExit)
            violations.Add("exit block is not marked as exit");
        if (Entry.IncomingEdges.Count != 0)
            violations.Add("entry block has predecessors");

        for (var index = 0; index < Blocks.Count; index++)
        {
            var block = Blocks[index];
            if (block.Id != index || block.Ordinal != index)
                violations.Add($"BB{block.Id} has unstable ordinal {block.Ordinal}, expected {index}");
            if (!block.HasTerminator)
            {
                violations.Add($"BB{block.Id} has no terminator");
                continue;
            }

            if (!block.OutgoingEdges.SequenceEqual(block.Terminator.OutgoingEdges))
                violations.Add($"BB{block.Id} terminator edges do not match block edges");

            foreach (var edge in block.OutgoingEdges)
            {
                if (!ReferenceEquals(edge.Source, block))
                    violations.Add($"BB{block.Id} owns an edge with a different source");
                if (!blockSet.Contains(edge.Target))
                    violations.Add($"BB{block.Id} targets a block outside the graph");
                if (!edge.Target.IncomingEdges.Contains(edge))
                    violations.Add($"BB{block.Id} edge is missing from target predecessors");
            }

            foreach (var edge in block.IncomingEdges)
            {
                if (!ReferenceEquals(edge.Target, block))
                    violations.Add($"BB{block.Id} owns an incoming edge with a different target");
                if (!blockSet.Contains(edge.Source))
                    violations.Add($"BB{block.Id} has a predecessor outside the graph");
                if (!edge.Source.OutgoingEdges.Contains(edge))
                    violations.Add($"BB{block.Id} incoming edge is missing from source successors");
            }

            ValidateTerminator(block, violations);
        }

        var reachable = ComputeReachableBlocks(Entry).ToHashSet();
        foreach (var block in Blocks)
        {
            if (!reachable.Contains(block) && !ReferenceEquals(block, Exit))
                violations.Add($"BB{block.Id} is unreachable");
        }

        if (violations.Count > 0)
            throw new ControlFlowGraphValidationException(violations);
    }

    private static void ValidateTerminator(BasicBlock block, List<string> violations)
    {
        var edges = block.OutgoingEdges;
        switch (block.Terminator)
        {
            case ExitTerminator:
                if (edges.Count != 0)
                    violations.Add($"BB{block.Id} exit terminator has outgoing edges");
                break;

            case FallThroughTerminator:
                if (edges.Count != 1 || edges[0].Kind != ControlFlowEdgeKind.FallThrough)
                    violations.Add($"BB{block.Id} fallthrough terminator has illegal edges");
                break;

            case ConditionalTerminator conditional:
                if (edges.Count is < 2 or > 3
                    || edges.Count(edge => edge.Kind == ControlFlowEdgeKind.True) != 1
                    || edges.Count(edge => edge.Kind == ControlFlowEdgeKind.False) != 1)
                {
                    violations.Add($"BB{block.Id} conditional terminator has illegal cardinality");
                }

                if (conditional.ExceptionalEdge != null
                    && conditional.ExceptionalEdge.Kind
                        is not (ControlFlowEdgeKind.Catch
                            or ControlFlowEdgeKind.Throw
                            or ControlFlowEdgeKind.Finally))
                {
                    violations.Add($"BB{block.Id} conditional has an illegal exceptional edge");
                }
                break;

            case AbruptTerminator abrupt:
                if (edges.Count != 1 || !IsLegalAbruptEdge(abrupt.Kind, edges[0].Kind))
                    violations.Add($"BB{block.Id} abrupt terminator has an ordinary or illegal edge");
                break;

            case UnsupportedTerminator:
                if (edges.Count != 2
                    || edges.Count(edge => edge.Kind == ControlFlowEdgeKind.FallThrough) != 1
                    || edges.Count(edge => edge.Kind
                        is ControlFlowEdgeKind.Catch
                            or ControlFlowEdgeKind.Throw
                            or ControlFlowEdgeKind.Finally) != 1)
                {
                    violations.Add($"BB{block.Id} unsupported terminator has illegal edges");
                }
                break;

            case DispatchTerminator:
                if (edges.Count == 0
                    || edges.Any(edge => edge.Kind
                        is ControlFlowEdgeKind.True
                            or ControlFlowEdgeKind.False
                            or ControlFlowEdgeKind.BackEdge
                            or ControlFlowEdgeKind.Break
                            or ControlFlowEdgeKind.Continue
                            or ControlFlowEdgeKind.Return))
                {
                    violations.Add($"BB{block.Id} dispatch terminator has illegal edges");
                }
                break;

            case ContinuationTerminator:
                if (edges.Count != 1
                    || edges[0].Kind is ControlFlowEdgeKind.True or ControlFlowEdgeKind.False)
                {
                    violations.Add($"BB{block.Id} continuation terminator has illegal edges");
                }
                break;
        }
    }

    private static bool IsLegalAbruptEdge(
        ControlFlowTerminatorKind terminatorKind,
        ControlFlowEdgeKind edgeKind) =>
        terminatorKind switch
        {
            ControlFlowTerminatorKind.Return =>
                edgeKind is ControlFlowEdgeKind.Return or ControlFlowEdgeKind.Finally,
            ControlFlowTerminatorKind.Throw =>
                edgeKind is ControlFlowEdgeKind.Throw
                    or ControlFlowEdgeKind.Catch
                    or ControlFlowEdgeKind.Finally,
            ControlFlowTerminatorKind.Break =>
                edgeKind is ControlFlowEdgeKind.Break or ControlFlowEdgeKind.Finally,
            ControlFlowTerminatorKind.Continue =>
                edgeKind is ControlFlowEdgeKind.Continue or ControlFlowEdgeKind.Finally,
            _ => false,
        };

    private static IReadOnlyList<BasicBlock> ComputeReachableBlocks(BasicBlock entry)
    {
        var visited = new HashSet<BasicBlock>();
        var worklist = new Stack<BasicBlock>();
        worklist.Push(entry);

        while (worklist.Count > 0)
        {
            var block = worklist.Pop();
            if (!visited.Add(block))
                continue;

            foreach (var edge in block.OutgoingEdges
                         .OrderByDescending(edge => edge.Target.Ordinal))
            {
                worklist.Push(edge.Target);
            }
        }

        return visited.OrderBy(block => block.Ordinal).ToArray();
    }

    private sealed class CfgBuilder
    {
        private readonly List<BasicBlock> _blocks = new();
        private readonly Dictionary<string, BasicBlock> _labelTargets =
            new(StringComparer.Ordinal);
        private BasicBlock _entryBlock = null!;
        private BasicBlock _exitBlock = null!;

        public ControlFlowGraph Build(BoundFunction function)
        {
            _entryBlock = CreateBlock();
            _entryBlock.IsEntry = true;
            _exitBlock = CreateBlock();
            _exitBlock.IsExit = true;

            RegisterLabels(function);

            var rootContext = new BuildContext(
                Array.Empty<LoopContext>(),
                Array.Empty<FinallyRegion>(),
                new FlowDestination(_exitBlock, ControlFlowEdgeKind.Throw, 0));

            var end = BuildStatements(function.Body, _entryBlock, rootContext);
            if (end != null)
            {
                TerminateFallThrough(end, _exitBlock);
            }

            _exitBlock.SetTerminator(new ExitTerminator());
            PruneUnreachableBlocks();
            AssignOrdinals();

            var cfg = new ControlFlowGraph(
                function,
                _entryBlock,
                _exitBlock,
                _blocks.ToArray());
            cfg.Validate();
            return cfg;
        }

        private void RegisterLabels(BoundFunction function)
        {
            foreach (var label in BoundNodeHelpers.DescendantsAndSelf(function)
                         .OfType<BoundLabelStatement>())
            {
                if (!_labelTargets.TryAdd(label.Label, CreateBlock()))
                {
                    throw new ControlFlowGraphValidationException(
                        [$"duplicate label '{label.Label}'"]);
                }
            }
        }

        private BasicBlock CreateBlock()
        {
            var block = new BasicBlock();
            _blocks.Add(block);
            return block;
        }

        private BasicBlock? BuildStatements(
            IReadOnlyList<BoundStatement> statements,
            BasicBlock? current,
            BuildContext context)
        {
            foreach (var statement in statements)
            {
                if (current == null && statement is not BoundLabelStatement)
                    continue;

                current = BuildStatement(statement, current, context);
            }

            return current;
        }

        private BasicBlock? BuildStatement(
            BoundStatement statement,
            BasicBlock? current,
            BuildContext context)
        {
            if (statement is BoundLabelStatement label)
                return BuildLabel(label, current);

            if (current == null)
                return null;

            switch (statement)
            {
                case BoundIfStatement ifStatement:
                    return BuildIf(ifStatement, current, context);
                case BoundWhileStatement whileStatement:
                    return BuildWhile(whileStatement, current, context);
                case BoundForStatement forStatement:
                    return BuildFor(forStatement, current, context);
                case BoundForeachStatement foreachStatement:
                    return BuildForeach(foreachStatement, current, context);
                case BoundDoWhileStatement doWhileStatement:
                    return BuildDoWhile(doWhileStatement, current, context);
                case BoundMatchStatement matchStatement:
                    return BuildMatch(matchStatement, current, context);
                case BoundTryStatement tryStatement:
                    return BuildTry(tryStatement, current, context);
                case BoundUsingStatement usingStatement:
                    return BuildUsing(usingStatement, current, context);
                case BoundReturnStatement returnStatement:
                    return BuildReturn(returnStatement, current, context);
                case BoundThrowStatement throwStatement:
                    return BuildThrow(throwStatement, current, context);
                case BoundBreakStatement breakStatement:
                    return BuildBreak(breakStatement, current, context);
                case BoundContinueStatement continueStatement:
                    return BuildContinue(continueStatement, current, context);
                case BoundGotoStatement gotoStatement:
                    return BuildGoto(gotoStatement, current);
                case BoundUnsupportedStatement unsupported:
                    return BuildUnsupported(unsupported, current, context);
                default:
                    current.Statements.Add(statement);
                    if (!StatementMayThrow(statement))
                        return current;

                    if (BoundNodeHelpers.GetDefinedVariable(statement) is { } defined
                        && statement is BoundBindStatement
                            or BoundAssignmentStatement
                            or BoundCompoundAssignment)
                    {
                        current.DeferDefinition(current.Statements.Count - 1);
                        current = SplitExceptionalFlow(current, context);
                        current.SyntheticOperations.Add(new SyntheticOperation(
                            SyntheticOperationKind.StatementDefinition,
                            statement.Span,
                            defined,
                            sourceStatement: statement));
                        var continuation = CreateBlock();
                        TerminateFallThrough(current, continuation);
                        return continuation;
                    }

                    return SplitExceptionalFlow(current, context);
            }
        }

        private BasicBlock BuildLabel(BoundLabelStatement label, BasicBlock? current)
        {
            var target = _labelTargets[label.Label];
            if (current != null && !ReferenceEquals(current, target))
                TerminateFallThrough(current, target);
            target.Statements.Add(label);
            return target;
        }

        private BasicBlock? BuildGoto(BoundGotoStatement statement, BasicBlock current)
        {
            current.Statements.Add(statement);
            if (!_labelTargets.TryGetValue(statement.Label, out var target))
            {
                throw new ControlFlowGraphValidationException(
                    [$"goto target '{statement.Label}' does not exist"]);
            }

            TerminateContinuation(current, target, ControlFlowEdgeKind.Dispatch);
            return null;
        }

        private BasicBlock? BuildReturn(
            BoundReturnStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            if (ExpressionMayThrow(statement.Expression))
            {
                current.SyntheticOperations.Add(new SyntheticOperation(
                    SyntheticOperationKind.ExpressionEvaluation,
                    statement.Span,
                    expression: statement.Expression));
                current = SplitExceptionalFlow(current, context);
            }
            else
            {
                current.Statements.Add(statement);
            }

            var destination = RouteDestination(
                new FlowDestination(_exitBlock, ControlFlowEdgeKind.Return, 0),
                context);
            TerminateAbrupt(
                current,
                ControlFlowTerminatorKind.Return,
                destination);
            return null;
        }

        private BasicBlock? BuildThrow(
            BoundThrowStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            if (ExpressionMayThrow(statement.Expression))
            {
                current.SyntheticOperations.Add(new SyntheticOperation(
                    SyntheticOperationKind.ExpressionEvaluation,
                    statement.Span,
                    expression: statement.Expression));
                current = SplitExceptionalFlow(current, context);
            }
            else
            {
                current.Statements.Add(statement);
            }

            var destination = RouteDestination(context.ExceptionDestination, context);
            TerminateAbrupt(
                current,
                ControlFlowTerminatorKind.Throw,
                destination);
            return null;
        }

        private BasicBlock? BuildBreak(
            BoundBreakStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            current.Statements.Add(statement);
            var loop = context.Loops.LastOrDefault()
                ?? throw new ControlFlowGraphValidationException(
                    ["break statement is not inside a loop"]);
            var destination = RouteDestination(loop.BreakDestination, context);
            TerminateAbrupt(
                current,
                ControlFlowTerminatorKind.Break,
                destination);
            return null;
        }

        private BasicBlock? BuildContinue(
            BoundContinueStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            current.Statements.Add(statement);
            var loop = context.Loops.LastOrDefault()
                ?? throw new ControlFlowGraphValidationException(
                    ["continue statement is not inside a loop"]);
            var destination = RouteDestination(loop.ContinueDestination, context);
            TerminateAbrupt(
                current,
                ControlFlowTerminatorKind.Continue,
                destination);
            return null;
        }

        private BasicBlock BuildUnsupported(
            BoundUnsupportedStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            current.Statements.Add(statement);
            var normal = CreateBlock();
            var exceptional = RouteDestination(context.ExceptionDestination, context);
            var edges = new[]
            {
                AddEdge(current, normal, ControlFlowEdgeKind.FallThrough),
                AddEdge(current, exceptional.Target, exceptional.EdgeKind),
            };
            current.SetTerminator(new UnsupportedTerminator(edges));
            return normal;
        }

        private BasicBlock? BuildIf(
            BoundIfStatement statement,
            BasicBlock conditionBlock,
            BuildContext context)
        {
            var merge = CreateBlock();
            var thenBlock = CreateBlock();
            var falseTarget = statement.ElseIfClauses.Count > 0 || statement.ElseBody != null
                ? CreateBlock()
                : merge;

            TerminateConditional(
                conditionBlock,
                statement.Condition,
                thenBlock,
                falseTarget,
                context);

            var thenEnd = BuildStatements(statement.ThenBody, thenBlock, context);
            if (thenEnd != null)
                TerminateFallThrough(thenEnd, merge);

            var nextCondition = falseTarget;
            for (var index = 0; index < statement.ElseIfClauses.Count; index++)
            {
                var clause = statement.ElseIfClauses[index];
                var body = CreateBlock();
                var next = index + 1 < statement.ElseIfClauses.Count
                           || statement.ElseBody != null
                    ? CreateBlock()
                    : merge;

                TerminateConditional(
                    nextCondition,
                    clause.Condition,
                    body,
                    next,
                    context);

                var bodyEnd = BuildStatements(clause.Body, body, context);
                if (bodyEnd != null)
                    TerminateFallThrough(bodyEnd, merge);
                nextCondition = next;
            }

            if (statement.ElseBody != null)
            {
                var elseEnd = BuildStatements(statement.ElseBody, nextCondition, context);
                if (elseEnd != null)
                    TerminateFallThrough(elseEnd, merge);
            }

            return merge.IncomingEdges.Count > 0 ? merge : null;
        }

        private BasicBlock BuildWhile(
            BoundWhileStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            var condition = CreateBlock();
            var body = CreateBlock();
            var after = CreateBlock();
            TerminateFallThrough(current, condition);
            TerminateConditional(condition, statement.Condition, body, after, context);

            var depth = context.FinallyRegions.Count;
            var loopContext = context.WithLoop(new LoopContext(
                new FlowDestination(after, ControlFlowEdgeKind.Break, depth),
                new FlowDestination(condition, ControlFlowEdgeKind.Continue, depth)));
            var bodyEnd = BuildStatements(statement.Body, body, loopContext);
            if (bodyEnd != null)
                TerminateContinuation(bodyEnd, condition, ControlFlowEdgeKind.BackEdge);

            return after;
        }

        private BasicBlock BuildFor(
            BoundForStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            current = AddSyntheticDefinition(
                current,
                SyntheticOperationKind.ForInitialization,
                statement.LoopVariable,
                statement.From,
                statement.Span,
                readsVariable: false,
                context);

            var condition = CreateBlock();
            var body = CreateBlock();
            var step = CreateBlock();
            var after = CreateBlock();
            TerminateFallThrough(current, condition);

            var conditionExpression = new BoundStructuralExpression(
                statement.Span,
                "ForCondition",
                "BOOL",
                [
                    new BoundVariableExpression(statement.Span, statement.LoopVariable),
                    statement.To,
                ]);
            TerminateConditional(condition, conditionExpression, body, after, context);

            var depth = context.FinallyRegions.Count;
            var loopContext = context.WithLoop(new LoopContext(
                new FlowDestination(after, ControlFlowEdgeKind.Break, depth),
                new FlowDestination(step, ControlFlowEdgeKind.Continue, depth)));
            var bodyEnd = BuildStatements(statement.Body, body, loopContext);
            if (bodyEnd != null)
                TerminateFallThrough(bodyEnd, step);

            var stepExpression = new BoundStructuralExpression(
                statement.Span,
                "ForStep",
                statement.LoopVariable.TypeName,
                statement.Step == null
                    ? [new BoundVariableExpression(statement.Span, statement.LoopVariable)]
                    : [
                        new BoundVariableExpression(statement.Span, statement.LoopVariable),
                        statement.Step,
                    ]);
            var stepEnd = AddSyntheticDefinition(
                step,
                SyntheticOperationKind.ForStep,
                statement.LoopVariable,
                stepExpression,
                statement.Span,
                readsVariable: false,
                context);
            TerminateContinuation(stepEnd, condition, ControlFlowEdgeKind.BackEdge);

            return after;
        }

        private BasicBlock BuildForeach(
            BoundForeachStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            current = AddSyntheticEvaluation(
                current,
                SyntheticOperationKind.ForeachCollectionEvaluation,
                statement.Collection,
                statement.Span,
                context);

            var condition = CreateBlock();
            var iteration = CreateBlock();
            var body = CreateBlock();
            var after = CreateBlock();
            iteration.SyntheticOperations.Add(new SyntheticOperation(
                SyntheticOperationKind.ForeachIteration,
                statement.Span,
                statement.LoopVariable));
            TerminateFallThrough(iteration, body);

            TerminateFallThrough(current, condition);
            var moveNext = new BoundStructuralExpression(
                statement.Span,
                "ForeachMoveNext",
                "BOOL");
            TerminateConditional(
                condition,
                moveNext,
                iteration,
                after,
                context,
                forceExceptionalEdge: true);

            var depth = context.FinallyRegions.Count;
            var loopContext = context.WithLoop(new LoopContext(
                new FlowDestination(after, ControlFlowEdgeKind.Break, depth),
                new FlowDestination(condition, ControlFlowEdgeKind.Continue, depth)));
            var bodyEnd = BuildStatements(statement.Body, body, loopContext);
            if (bodyEnd != null)
                TerminateContinuation(bodyEnd, condition, ControlFlowEdgeKind.BackEdge);

            return after;
        }

        private BasicBlock BuildDoWhile(
            BoundDoWhileStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            var body = CreateBlock();
            var condition = CreateBlock();
            var after = CreateBlock();
            TerminateFallThrough(current, body);

            var depth = context.FinallyRegions.Count;
            var loopContext = context.WithLoop(new LoopContext(
                new FlowDestination(after, ControlFlowEdgeKind.Break, depth),
                new FlowDestination(condition, ControlFlowEdgeKind.Continue, depth)));
            var bodyEnd = BuildStatements(statement.Body, body, loopContext);
            if (bodyEnd != null)
                TerminateFallThrough(bodyEnd, condition);

            TerminateConditional(condition, statement.Condition, body, after, context);
            return after;
        }

        private BasicBlock? BuildMatch(
            BoundMatchStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            current = AddSyntheticEvaluation(
                current,
                SyntheticOperationKind.MatchTargetEvaluation,
                statement.Target,
                statement.Span,
                context);

            var merge = CreateBlock();
            var next = CreateBlock();
            TerminateFallThrough(current, next);

            for (var index = 0; index < statement.Cases.Count; index++)
            {
                var matchCase = statement.Cases[index];
                var body = CreateBlock();

                if (matchCase.IsDefault)
                {
                    TerminateFallThrough(next, body);
                }
                else
                {
                    var falseTarget = index + 1 < statement.Cases.Count
                        ? CreateBlock()
                        : merge;
                    var conditionChildren = matchCase.Pattern.Expressions
                        .Concat(matchCase.Guard == null
                            ? Array.Empty<BoundExpression>()
                            : [matchCase.Guard])
                        .ToArray();
                    var condition = new BoundStructuralExpression(
                        matchCase.Span,
                        "MatchCaseCondition",
                        "BOOL",
                        conditionChildren);
                    TerminateConditional(
                        next,
                        condition,
                        body,
                        falseTarget,
                        context);
                    next = falseTarget;
                }

                var bodyEnd = BuildStatements(matchCase.Body, body, context);
                if (bodyEnd != null)
                    TerminateFallThrough(bodyEnd, merge);

                if (matchCase.IsDefault)
                {
                    next = merge;
                    break;
                }
            }

            if (!next.HasTerminator && !ReferenceEquals(next, merge))
                TerminateFallThrough(next, merge);

            return merge.IncomingEdges.Count > 0 ? merge : null;
        }

        private BasicBlock? BuildTry(
            BoundTryStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            var tryEntry = CreateBlock();
            var after = CreateBlock();
            TerminateFallThrough(current, tryEntry);

            var protectedContext = context;
            if (statement.FinallyBody is { Count: > 0 })
            {
                var region = new FinallyRegion(
                    statement.FinallyBody,
                    context,
                    null);
                protectedContext = context.WithFinally(region);
            }

            BasicBlock? catchDispatch = null;
            if (statement.CatchClauses.Count > 0)
                catchDispatch = CreateBlock();

            var tryContext = catchDispatch == null
                ? protectedContext
                : protectedContext.WithException(new FlowDestination(
                    catchDispatch,
                    ControlFlowEdgeKind.Throw,
                    protectedContext.FinallyRegions.Count));

            var tryEnd = BuildStatements(statement.TryBody, tryEntry, tryContext);
            if (tryEnd != null)
            {
                TerminateRouted(
                    tryEnd,
                    new FlowDestination(
                        after,
                        ControlFlowEdgeKind.FallThrough,
                        context.FinallyRegions.Count),
                    protectedContext);
            }

            if (catchDispatch != null)
            {
                var dispatchEdges = new List<ControlFlowEdge>();
                var hasCatchAll = false;
                foreach (var catchClause in statement.CatchClauses)
                {
                    var catchEntry = CreateBlock();
                    dispatchEdges.Add(AddEdge(
                        catchDispatch,
                        catchEntry,
                        ControlFlowEdgeKind.Catch));
                    hasCatchAll |= catchClause.ExceptionTypeName == null;

                    if (catchClause.ExceptionVariable != null)
                    {
                        catchEntry.SyntheticOperations.Add(new SyntheticOperation(
                            SyntheticOperationKind.CatchInitialization,
                            catchClause.Span,
                            catchClause.ExceptionVariable));
                    }

                    var catchBody = catchClause.ExceptionVariable == null
                        ? catchEntry
                        : CreateBlock();
                    if (!ReferenceEquals(catchBody, catchEntry))
                        TerminateFallThrough(catchEntry, catchBody);

                    var catchEnd = BuildStatements(
                        catchClause.Body,
                        catchBody,
                        protectedContext.WithException(context.ExceptionDestination));
                    if (catchEnd != null)
                    {
                        TerminateRouted(
                            catchEnd,
                            new FlowDestination(
                                after,
                                ControlFlowEdgeKind.FallThrough,
                                context.FinallyRegions.Count),
                            protectedContext);
                    }
                }

                if (!hasCatchAll)
                {
                    var uncaught = RouteDestination(
                        context.ExceptionDestination,
                        protectedContext);
                    dispatchEdges.Add(AddEdge(
                        catchDispatch,
                        uncaught.Target,
                        uncaught.EdgeKind));
                }

                catchDispatch.SetTerminator(new DispatchTerminator(null, dispatchEdges));
            }

            return after.IncomingEdges.Count > 0 ? after : null;
        }

        private BasicBlock? BuildUsing(
            BoundUsingStatement statement,
            BasicBlock current,
            BuildContext context)
        {
            if (statement.Resource != null)
            {
                current = AddSyntheticDefinition(
                    current,
                    SyntheticOperationKind.UsingResourceInitialization,
                    statement.Resource,
                    statement.ResourceExpression,
                    statement.Span,
                    readsVariable: false,
                    context);
            }
            else
            {
                current = AddSyntheticEvaluation(
                    current,
                    SyntheticOperationKind.UsingResourceEvaluation,
                    statement.ResourceExpression,
                    statement.Span,
                    context);
            }

            var body = CreateBlock();
            var after = CreateBlock();
            TerminateFallThrough(current, body);

            var dispose = new SyntheticOperation(
                SyntheticOperationKind.UsingDispose,
                statement.Span,
                statement.Resource,
                readsDefinedVariable: statement.Resource != null,
                mayThrow: true);
            var region = new FinallyRegion(
                Array.Empty<BoundStatement>(),
                context,
                dispose);
            var protectedContext = context.WithFinally(region);

            var bodyEnd = BuildStatements(statement.Body, body, protectedContext);
            if (bodyEnd != null)
            {
                TerminateRouted(
                    bodyEnd,
                    new FlowDestination(
                        after,
                        ControlFlowEdgeKind.FallThrough,
                        context.FinallyRegions.Count),
                    protectedContext);
            }

            return after.IncomingEdges.Count > 0 ? after : null;
        }

        private BasicBlock AddSyntheticDefinition(
            BasicBlock current,
            SyntheticOperationKind kind,
            VariableSymbol variable,
            BoundExpression expression,
            TextSpan span,
            bool readsVariable,
            BuildContext context)
        {
            if (ExpressionMayThrow(expression))
            {
                current.SyntheticOperations.Add(new SyntheticOperation(
                    SyntheticOperationKind.ExpressionEvaluation,
                    span,
                    expression: expression));
                current = SplitExceptionalFlow(current, context);
                current.SyntheticOperations.Add(new SyntheticOperation(
                    kind,
                    span,
                    variable,
                    readsDefinedVariable: readsVariable));
                var continuation = CreateBlock();
                TerminateFallThrough(current, continuation);
                return continuation;
            }

            current.SyntheticOperations.Add(new SyntheticOperation(
                kind,
                span,
                variable,
                expression,
                readsVariable));
            return current;
        }

        private BasicBlock AddSyntheticEvaluation(
            BasicBlock current,
            SyntheticOperationKind kind,
            BoundExpression expression,
            TextSpan span,
            BuildContext context)
        {
            current.SyntheticOperations.Add(new SyntheticOperation(
                kind,
                span,
                expression: expression));
            return ExpressionMayThrow(expression)
                ? SplitExceptionalFlow(current, context)
                : current;
        }

        private BasicBlock SplitExceptionalFlow(
            BasicBlock current,
            BuildContext context)
        {
            var normal = CreateBlock();
            var exceptional = RouteDestination(context.ExceptionDestination, context);
            var edges = new[]
            {
                AddEdge(current, normal, ControlFlowEdgeKind.FallThrough),
                AddEdge(current, exceptional.Target, exceptional.EdgeKind),
            };
            current.SetTerminator(new DispatchTerminator(null, edges));
            return normal;
        }

        private void TerminateConditional(
            BasicBlock source,
            BoundExpression condition,
            BasicBlock trueTarget,
            BasicBlock falseTarget,
            BuildContext context,
            bool forceExceptionalEdge = false)
        {
            var trueEdge = AddEdge(source, trueTarget, ControlFlowEdgeKind.True);
            var falseEdge = AddEdge(source, falseTarget, ControlFlowEdgeKind.False);
            ControlFlowEdge? exceptionalEdge = null;
            if (forceExceptionalEdge || ExpressionMayThrow(condition))
            {
                var exceptional = RouteDestination(
                    context.ExceptionDestination,
                    context);
                exceptionalEdge = AddEdge(
                    source,
                    exceptional.Target,
                    exceptional.EdgeKind);
            }

            source.SetTerminator(new ConditionalTerminator(
                condition,
                trueEdge,
                falseEdge,
                exceptionalEdge));
        }

        private void TerminateFallThrough(BasicBlock source, BasicBlock target)
        {
            var edge = AddEdge(source, target, ControlFlowEdgeKind.FallThrough);
            source.SetTerminator(new FallThroughTerminator(edge));
        }

        private void TerminateContinuation(
            BasicBlock source,
            BasicBlock target,
            ControlFlowEdgeKind edgeKind)
        {
            var edge = AddEdge(source, target, edgeKind);
            source.SetTerminator(new ContinuationTerminator(edge));
        }

        private void TerminateRouted(
            BasicBlock source,
            FlowDestination destination,
            BuildContext context)
        {
            var routed = RouteDestination(destination, context);
            TerminateContinuation(source, routed.Target, routed.EdgeKind);
        }

        private void TerminateAbrupt(
            BasicBlock source,
            ControlFlowTerminatorKind kind,
            FlowDestination destination)
        {
            var edge = AddEdge(source, destination.Target, destination.EdgeKind);
            source.SetTerminator(new AbruptTerminator(kind, edge));
        }

        private ControlFlowEdge AddEdge(
            BasicBlock source,
            BasicBlock target,
            ControlFlowEdgeKind kind)
        {
            var edge = new ControlFlowEdge(source, target, kind);
            source.AddOutgoingEdge(edge);
            target.AddIncomingEdge(edge);
            return edge;
        }

        private FlowDestination RouteDestination(
            FlowDestination destination,
            BuildContext context)
        {
            if (destination.FinallyDepth > context.FinallyRegions.Count)
            {
                throw new ControlFlowGraphValidationException(
                    ["control-flow destination crosses into a protected region"]);
            }

            var routed = destination;
            for (var index = destination.FinallyDepth;
                 index < context.FinallyRegions.Count;
                 index++)
            {
                var entry = GetOrCreateFinallyEntry(
                    context.FinallyRegions[index],
                    routed);
                routed = new FlowDestination(
                    entry,
                    ControlFlowEdgeKind.Finally,
                    index + 1);
            }

            return routed;
        }

        private BasicBlock GetOrCreateFinallyEntry(
            FinallyRegion region,
            FlowDestination downstream)
        {
            var key = new FinallyContinuationKey(
                downstream.Target,
                downstream.EdgeKind);
            if (region.Entries.TryGetValue(key, out var cached))
                return cached;

            var entry = CreateBlock();
            region.Entries.Add(key, entry);
            var current = entry;

            if (region.SyntheticOperation != null)
            {
                var template = region.SyntheticOperation;
                current.SyntheticOperations.Add(new SyntheticOperation(
                    template.Kind,
                    template.Span,
                    template.DefinedVariable,
                    template.Expression,
                    template.ReadsDefinedVariable,
                    template.MayThrow,
                    template.SourceStatement));
                if (template.MayThrow)
                    current = SplitExceptionalFlow(current, region.OuterContext);
            }

            var end = BuildStatements(region.Body, current, region.OuterContext);
            if (end != null)
            {
                TerminateContinuation(
                    end,
                    downstream.Target,
                    downstream.EdgeKind);
            }

            return entry;
        }

        private static bool StatementMayThrow(BoundStatement statement) =>
            statement is BoundCallStatement
            || BoundNodeHelpers.DescendantsAndSelf(statement).Any(node =>
                node is BoundCallExpression
                    or BoundThrowExpression
                    or BoundUnsupportedExpression
                    or BoundInteropExpression);

        private static bool ExpressionMayThrow(BoundExpression? expression) =>
            BoundNodeHelpers.DescendantsAndSelf(expression).Any(node =>
                node is BoundCallExpression
                    or BoundThrowExpression
                    or BoundUnsupportedExpression
                    or BoundInteropExpression);

        private void PruneUnreachableBlocks()
        {
            var reachable = ComputeReachableBlocks(_entryBlock).ToHashSet();
            var removed = _blocks
                .Where(block =>
                    !reachable.Contains(block) && !ReferenceEquals(block, _exitBlock))
                .ToHashSet();
            foreach (var block in removed)
            {
                foreach (var edge in block.OutgoingEdges.ToArray())
                {
                    block.RemoveOutgoingEdge(edge);
                    edge.Target.RemoveIncomingEdge(edge);
                }

                foreach (var edge in block.IncomingEdges.ToArray())
                {
                    edge.Source.RemoveOutgoingEdge(edge);
                    block.RemoveIncomingEdge(edge);
                }
            }

            _blocks.RemoveAll(removed.Contains);
        }

        private void AssignOrdinals()
        {
            var operationOrdinal = 0;
            for (var index = 0; index < _blocks.Count; index++)
            {
                var block = _blocks[index];
                block.Id = index;
                block.Ordinal = index;
                foreach (var operation in block.SyntheticOperations)
                    operation.Ordinal = operationOrdinal++;
            }
        }

        private sealed record FlowDestination(
            BasicBlock Target,
            ControlFlowEdgeKind EdgeKind,
            int FinallyDepth);

        private sealed record LoopContext(
            FlowDestination BreakDestination,
            FlowDestination ContinueDestination);

        private sealed record FinallyContinuationKey(
            BasicBlock Target,
            ControlFlowEdgeKind EdgeKind);

        private sealed class FinallyRegion
        {
            public IReadOnlyList<BoundStatement> Body { get; }
            public BuildContext OuterContext { get; }
            public SyntheticOperation? SyntheticOperation { get; }
            public Dictionary<FinallyContinuationKey, BasicBlock> Entries { get; } = new();

            public FinallyRegion(
                IReadOnlyList<BoundStatement> body,
                BuildContext outerContext,
                SyntheticOperation? syntheticOperation)
            {
                Body = body;
                OuterContext = outerContext;
                SyntheticOperation = syntheticOperation;
            }
        }

        private sealed record BuildContext(
            IReadOnlyList<LoopContext> Loops,
            IReadOnlyList<FinallyRegion> FinallyRegions,
            FlowDestination ExceptionDestination)
        {
            public BuildContext WithLoop(LoopContext loop) =>
                this with { Loops = [.. Loops, loop] };

            public BuildContext WithFinally(FinallyRegion region) =>
                this with { FinallyRegions = [.. FinallyRegions, region] };

            public BuildContext WithException(FlowDestination destination) =>
                this with { ExceptionDestination = destination };
        }
    }

    public string ToDot()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("digraph CFG {");
        builder.AppendLine("  node [shape=box];");

        foreach (var block in Blocks)
        {
            var label = $"BB{block.Id}";
            if (block.IsEntry)
                label += " (entry)";
            if (block.IsExit)
                label += " (exit)";
            label += $"\\n{block.Terminator.Kind}";
            if (block.Statements.Count > 0)
                label += $"\\n{block.Statements.Count} stmts";
            if (block.SyntheticOperations.Count > 0)
                label += $"\\n{block.SyntheticOperations.Count} synthetic";

            builder.AppendLine($"  BB{block.Id} [label=\"{label}\"];");
            foreach (var edge in block.OutgoingEdges)
            {
                builder.AppendLine(
                    $"  BB{block.Id} -> BB{edge.Target.Id} [label=\"{edge.Kind}\"];");
            }
        }

        builder.AppendLine("}");
        return builder.ToString();
    }
}
