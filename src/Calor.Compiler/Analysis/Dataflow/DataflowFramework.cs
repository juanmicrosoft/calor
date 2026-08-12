using Calor.Compiler.Binding;

namespace Calor.Compiler.Analysis.Dataflow;

/// <summary>
/// Represents the lattice for a dataflow analysis.
/// </summary>
/// <typeparam name="T">The type of dataflow facts.</typeparam>
public interface IDataflowLattice<T> where T : IEquatable<T>
{
    /// <summary>
    /// The bottom element of the lattice (most optimistic/least information).
    /// </summary>
    T Bottom { get; }

    /// <summary>
    /// The top element of the lattice (most conservative/full information).
    /// </summary>
    T Top { get; }

    /// <summary>
    /// Joins two lattice elements (least upper bound / meet operation).
    /// For forward may-analyses, this is typically union.
    /// For backward must-analyses, this is typically intersection.
    /// </summary>
    T Join(T a, T b);

    /// <summary>
    /// Checks if a is less than or equal to b in the lattice ordering.
    /// </summary>
    bool LessOrEqual(T a, T b);
}

/// <summary>
/// Represents a transfer function that computes the effect of a statement on dataflow facts.
/// </summary>
/// <typeparam name="T">The type of dataflow facts.</typeparam>
public interface ITransferFunction<T> where T : IEquatable<T>
{
    /// <summary>
    /// Computes the dataflow facts after executing a statement, given the facts before.
    /// </summary>
    T Transfer(BoundStatement statement, T input);

    T Transfer(
        BasicBlock block,
        int statementIndex,
        BoundStatement statement,
        T input) => Transfer(statement, input);

    /// <summary>
    /// Computes the dataflow facts after evaluating an expression (for condition blocks).
    /// </summary>
    T TransferExpression(BoundExpression? expression, T input);

    /// <summary>
    /// Computes the dataflow facts after an implicit CFG operation.
    /// </summary>
    T TransferSynthetic(SyntheticOperation operation, T input) => input;

    T TransferSynthetic(
        BasicBlock block,
        int operationIndex,
        SyntheticOperation operation,
        T input) => TransferSynthetic(operation, input);
}

/// <summary>
/// Direction of the dataflow analysis.
/// </summary>
public enum DataflowDirection
{
    Forward,
    Backward
}

/// <summary>
/// Results of a dataflow analysis for a single basic block.
/// </summary>
/// <typeparam name="T">The type of dataflow facts.</typeparam>
public sealed class BlockDataflowResult<T>
{
    public T In { get; set; }
    public T Out { get; set; }

    public BlockDataflowResult(T initial)
    {
        In = initial;
        Out = initial;
    }
}

public sealed class DataflowAnalysisResult<T> where T : IEquatable<T>
{
    public Dictionary<BasicBlock, BlockDataflowResult<T>> Blocks { get; }
    public int Iterations { get; }
    public bool IsConverged { get; }
    public IReadOnlyList<BasicBlock> ReachableBlocks { get; }

    internal DataflowAnalysisResult(
        Dictionary<BasicBlock, BlockDataflowResult<T>> blocks,
        int iterations,
        bool isConverged,
        IReadOnlyList<BasicBlock> reachableBlocks)
    {
        Blocks = blocks;
        Iterations = iterations;
        IsConverged = isConverged;
        ReachableBlocks = reachableBlocks;
    }
}

public sealed class DataflowConvergenceException : InvalidOperationException
{
    public int Iterations { get; }
    public int MaximumIterations { get; }

    public DataflowConvergenceException(int iterations, int maximumIterations)
        : base($"Dataflow analysis did not converge within {maximumIterations} iterations")
    {
        Iterations = iterations;
        MaximumIterations = maximumIterations;
    }
}

/// <summary>
/// Generic dataflow analysis framework using worklist algorithm.
/// </summary>
/// <typeparam name="T">The type of dataflow facts.</typeparam>
public sealed class DataflowAnalysis<T> where T : IEquatable<T>
{
    private readonly IDataflowLattice<T> _lattice;
    private readonly ITransferFunction<T> _transfer;
    private readonly DataflowDirection _direction;
    private readonly T _entryBoundary;
    private readonly T _exitBoundary;
    private readonly T _joinIdentity;
    private readonly int _maxIterations;
    public DataflowAnalysisResult<T>? LastResult { get; private set; }

    public DataflowAnalysis(
        IDataflowLattice<T> lattice,
        ITransferFunction<T> transfer,
        DataflowDirection direction = DataflowDirection.Forward,
        int maxIterations = 1000)
        : this(
            lattice,
            transfer,
            direction,
            direction == DataflowDirection.Forward ? lattice.Top : lattice.Bottom,
            direction == DataflowDirection.Backward ? lattice.Top : lattice.Bottom,
            lattice.Bottom,
            maxIterations)
    {
    }

    public DataflowAnalysis(
        IDataflowLattice<T> lattice,
        ITransferFunction<T> transfer,
        DataflowDirection direction,
        T entryBoundary,
        T exitBoundary,
        T joinIdentity,
        int maxIterations = 1000)
    {
        _lattice = lattice ?? throw new ArgumentNullException(nameof(lattice));
        _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
        _direction = direction;
        _entryBoundary = entryBoundary;
        _exitBoundary = exitBoundary;
        _joinIdentity = joinIdentity;
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations));
        _maxIterations = maxIterations;
    }

    /// <summary>
    /// Runs the dataflow analysis on a control flow graph.
    /// </summary>
    /// <returns>A dictionary mapping each block to its dataflow results.</returns>
    public Dictionary<BasicBlock, BlockDataflowResult<T>> Analyze(ControlFlowGraph cfg) =>
        AnalyzeWithMetadata(cfg).Blocks;

    public DataflowAnalysisResult<T> AnalyzeWithMetadata(ControlFlowGraph cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        LastResult = null;
        var results = new Dictionary<BasicBlock, BlockDataflowResult<T>>();

        // Retain compatibility for callers that query an unreachable Exit block, but
        // only reachable blocks participate in fixed-point computation.
        foreach (var block in cfg.Blocks)
        {
            results[block] = new BlockDataflowResult<T>(_joinIdentity);
        }

        results[cfg.Entry].In = _entryBoundary;
        results[cfg.Exit].Out = _exitBoundary;

        var orderedBlocks = _direction == DataflowDirection.Forward
            ? cfg.GetReversePostOrder()
            : cfg.GetPostOrder();
        var reachable = cfg.ReachableBlocks.ToHashSet();
        var worklist = new Queue<BasicBlock>(orderedBlocks);
        var inWorklist = new HashSet<BasicBlock>(orderedBlocks);
        var iterations = 0;

        while (worklist.Count > 0)
        {
            if (iterations >= _maxIterations)
                throw new DataflowConvergenceException(iterations, _maxIterations);

            iterations++;
            var block = worklist.Dequeue();
            inWorklist.Remove(block);
            var result = results[block];

            if (_direction == DataflowDirection.Forward)
            {
                var newIn = ReferenceEquals(block, cfg.Entry)
                    ? _entryBoundary
                    : JoinFacts(
                        block.IncomingEdges
                            .Where(edge => reachable.Contains(edge.Source))
                            .OrderBy(edge => edge.Source.Ordinal)
                            .Select(edge => results[edge.Source].Out));
                var currentFacts = newIn;
                for (var index = 0; index < block.Statements.Count; index++)
                {
                    currentFacts = _transfer.Transfer(
                        block,
                        index,
                        block.Statements[index],
                        currentFacts);
                }
                for (var index = 0; index < block.SyntheticOperations.Count; index++)
                {
                    currentFacts = _transfer.TransferSynthetic(
                        block,
                        index,
                        block.SyntheticOperations[index],
                        currentFacts);
                }
                currentFacts = _transfer.TransferExpression(
                    block.Terminator.Condition,
                    currentFacts);

                var changed = !newIn.Equals(result.In)
                    || !currentFacts.Equals(result.Out);
                result.In = newIn;
                result.Out = currentFacts;

                if (changed)
                {
                    Enqueue(
                        block.OutgoingEdges
                            .Where(edge => reachable.Contains(edge.Target))
                            .Select(edge => edge.Target),
                        worklist,
                        inWorklist);
                }
            }
            else
            {
                var newOut = ReferenceEquals(block, cfg.Exit)
                    ? _exitBoundary
                    : JoinFacts(
                        block.OutgoingEdges
                            .Where(edge => reachable.Contains(edge.Target))
                            .OrderBy(edge => edge.Target.Ordinal)
                            .Select(edge => results[edge.Target].In));
                var currentFacts = _transfer.TransferExpression(
                    block.Terminator.Condition,
                    newOut);
                for (var index = block.SyntheticOperations.Count - 1; index >= 0; index--)
                {
                    currentFacts = _transfer.TransferSynthetic(
                        block,
                        index,
                        block.SyntheticOperations[index],
                        currentFacts);
                }
                for (var index = block.Statements.Count - 1; index >= 0; index--)
                {
                    currentFacts = _transfer.Transfer(
                        block,
                        index,
                        block.Statements[index],
                        currentFacts);
                }

                var changed = !newOut.Equals(result.Out)
                    || !currentFacts.Equals(result.In);
                result.Out = newOut;
                result.In = currentFacts;

                if (changed)
                {
                    Enqueue(
                        block.IncomingEdges
                            .Where(edge => reachable.Contains(edge.Source))
                            .Select(edge => edge.Source),
                        worklist,
                        inWorklist);
                }
            }
        }

        LastResult = new DataflowAnalysisResult<T>(
            results,
            iterations,
            isConverged: true,
            cfg.ReachableBlocks);
        return LastResult;
    }

    private T JoinFacts(IEnumerable<T> facts)
    {
        using var enumerator = facts.GetEnumerator();
        if (!enumerator.MoveNext())
            return _joinIdentity;

        var result = enumerator.Current;
        while (enumerator.MoveNext())
            result = _lattice.Join(result, enumerator.Current);
        return result;
    }

    private static void Enqueue(
        IEnumerable<BasicBlock> blocks,
        Queue<BasicBlock> worklist,
        HashSet<BasicBlock> inWorklist)
    {
        foreach (var block in blocks.Distinct().OrderBy(block => block.Ordinal))
        {
            if (inWorklist.Add(block))
                worklist.Enqueue(block);
        }
    }
}

/// <summary>
/// A set-based lattice for dataflow facts.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
public sealed class SetLattice<T> : IDataflowLattice<ImmutableHashSet<T>> where T : notnull
{
    private readonly ImmutableHashSet<T> _universe;

    public SetLattice(IEnumerable<T>? universe = null)
    {
        _universe = universe != null
            ? ImmutableHashSet<T>.CreateRange(universe)
            : ImmutableHashSet<T>.Empty;
    }

    public ImmutableHashSet<T> Bottom => ImmutableHashSet<T>.Empty;
    public ImmutableHashSet<T> Top => _universe;

    public ImmutableHashSet<T> Join(ImmutableHashSet<T> a, ImmutableHashSet<T> b)
        => a.Union(b);

    public bool LessOrEqual(ImmutableHashSet<T> a, ImmutableHashSet<T> b)
        => a.IsSubsetOf(b);
}

/// <summary>
/// An intersection-based lattice for must-analyses.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
public sealed class MustSetLattice<T> : IDataflowLattice<ImmutableHashSet<T>> where T : notnull
{
    private readonly ImmutableHashSet<T> _universe;

    public MustSetLattice(IEnumerable<T> universe)
    {
        _universe = ImmutableHashSet<T>.CreateRange(universe);
    }

    public ImmutableHashSet<T> Bottom => _universe; // Start with everything
    public ImmutableHashSet<T> Top => ImmutableHashSet<T>.Empty; // End with nothing

    public ImmutableHashSet<T> Join(ImmutableHashSet<T> a, ImmutableHashSet<T> b)
        => a.Intersect(b); // Must be in both paths

    public bool LessOrEqual(ImmutableHashSet<T> a, ImmutableHashSet<T> b)
        => b.IsSubsetOf(a); // Reversed for must-analysis
}

/// <summary>
/// Represents a variable definition (assignment site).
/// </summary>
public readonly record struct Definition(string VariableName, int BlockId, int StatementIndex);

/// <summary>
/// Immutable hash set wrapper that implements IEquatable for dataflow analysis.
/// </summary>
public readonly struct ImmutableHashSet<T> : IEquatable<ImmutableHashSet<T>> where T : notnull
{
    private readonly HashSet<T>? _set;

    private ImmutableHashSet(HashSet<T>? set)
    {
        _set = set;
    }

    public static ImmutableHashSet<T> Empty => new(null);

    public static ImmutableHashSet<T> CreateRange(IEnumerable<T> items)
        => new(new HashSet<T>(items));

    public static ImmutableHashSet<T> Create(T item)
        => new(new HashSet<T> { item });

    public int Count => _set?.Count ?? 0;

    public bool Contains(T item) => _set?.Contains(item) ?? false;

    public ImmutableHashSet<T> Add(T item)
    {
        var newSet = _set != null ? new HashSet<T>(_set) : new HashSet<T>();
        newSet.Add(item);
        return new ImmutableHashSet<T>(newSet);
    }

    public ImmutableHashSet<T> Remove(T item)
    {
        if (_set == null || !_set.Contains(item))
            return this;

        var newSet = new HashSet<T>(_set);
        newSet.Remove(item);
        return new ImmutableHashSet<T>(newSet);
    }

    public ImmutableHashSet<T> Union(ImmutableHashSet<T> other)
    {
        if (_set == null)
            return other;
        if (other._set == null)
            return this;

        var newSet = new HashSet<T>(_set);
        newSet.UnionWith(other._set);
        return new ImmutableHashSet<T>(newSet);
    }

    public ImmutableHashSet<T> Intersect(ImmutableHashSet<T> other)
    {
        if (_set == null || other._set == null)
            return Empty;

        var newSet = new HashSet<T>(_set);
        newSet.IntersectWith(other._set);
        return new ImmutableHashSet<T>(newSet);
    }

    public ImmutableHashSet<T> Except(ImmutableHashSet<T> other)
    {
        if (_set == null)
            return Empty;
        if (other._set == null)
            return this;

        var newSet = new HashSet<T>(_set);
        newSet.ExceptWith(other._set);
        return new ImmutableHashSet<T>(newSet);
    }

    public bool IsSubsetOf(ImmutableHashSet<T> other)
    {
        if (_set == null)
            return true;
        if (other._set == null)
            return _set.Count == 0;

        return _set.IsSubsetOf(other._set);
    }

    public IEnumerable<T> AsEnumerable() => _set ?? Enumerable.Empty<T>();

    public bool Equals(ImmutableHashSet<T> other)
    {
        if (_set == null && other._set == null)
            return true;
        if (_set == null || other._set == null)
            return false;

        return _set.SetEquals(other._set);
    }

    public override bool Equals(object? obj) => obj is ImmutableHashSet<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_set == null)
            return 0;

        // Order-independent hash
        var hash = 0;
        foreach (var item in _set)
            hash ^= item.GetHashCode();
        return hash;
    }

    public static bool operator ==(ImmutableHashSet<T> left, ImmutableHashSet<T> right) => left.Equals(right);
    public static bool operator !=(ImmutableHashSet<T> left, ImmutableHashSet<T> right) => !left.Equals(right);
}
