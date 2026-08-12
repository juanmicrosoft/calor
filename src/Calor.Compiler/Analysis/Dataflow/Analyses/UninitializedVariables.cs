using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis.Dataflow.Analyses;

/// <summary>
/// Represents the initialization state of variables.
/// </summary>
public enum InitializationState
{
    /// <summary>Variable is definitely not initialized.</summary>
    Uninitialized,
    /// <summary>Variable may or may not be initialized (path-dependent).</summary>
    MaybeInitialized,
    /// <summary>Variable is definitely initialized.</summary>
    Initialized
}

/// <summary>
/// Uninitialized variables analysis: detects use of potentially uninitialized variables.
/// This is a forward must-analysis that tracks which variables are definitely initialized.
/// </summary>
public sealed class UninitializedVariablesAnalysis
{
    private readonly ControlFlowGraph _cfg;
    private readonly Dictionary<BasicBlock, BlockDataflowResult<InitializationFacts>> _results;
    private readonly IReadOnlyDictionary<SymbolId, VariableSymbol> _allVariables;
    private readonly HashSet<SymbolId> _parameters;
    private readonly List<UninitializedUse> _uninitializedUses = new();
    private readonly UninitializedVariablesTransfer _transfer;
    public IReadOnlyList<BoundNode> IncompleteNodes { get; }
    public bool IsComplete => IncompleteNodes.Count == 0;
    public DataflowAnalysisResult<InitializationFacts> AnalysisResult { get; }

    public UninitializedVariablesAnalysis(ControlFlowGraph cfg, IEnumerable<string>? parameterNames = null)
        : this(
            cfg,
            cfg.Function.Symbol.Parameters
                .Where(parameter => parameterNames == null
                    || parameterNames.Contains(parameter.Name, StringComparer.Ordinal))
                .Select(parameter => parameter.Id))
    {
    }

    public UninitializedVariablesAnalysis(
        ControlFlowGraph cfg,
        IEnumerable<VariableSymbol> parameters)
        : this(cfg, parameters.Select(parameter => parameter.Id))
    {
    }

    private UninitializedVariablesAnalysis(
        ControlFlowGraph cfg,
        IEnumerable<SymbolId> parameterIds)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _allVariables = CollectAllVariables(cfg);
        IncompleteNodes = BoundNodeHelpers.GetAnalysisIncompleteNodes(cfg.Function).ToArray();
        _parameters = parameterIds
            .Where(parameterId => !parameterId.IsNone)
            .ToHashSet();

        var lattice = new InitializationLattice(_allVariables.Keys.ToHashSet());
        _transfer = new UninitializedVariablesTransfer();
        var entryBoundary = InitializationFacts.Create(
            _allVariables.Keys,
            _parameters);
        var analysis = new DataflowAnalysis<InitializationFacts>(
            lattice,
            _transfer,
            DataflowDirection.Forward,
            entryBoundary,
            InitializationFacts.Empty,
            InitializationFacts.Empty);

        AnalysisResult = analysis.AnalyzeWithMetadata(cfg);
        _results = AnalysisResult.Blocks;

        // Detect uninitialized uses
        DetectUninitializedUses();
    }

    /// <summary>
    /// Gets all detected uses of potentially uninitialized variables.
    /// </summary>
    public IReadOnlyList<UninitializedUse> UninitializedUses => _uninitializedUses;

    /// <summary>
    /// Reports uninitialized variable uses as diagnostics.
    /// </summary>
    public void ReportDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (var use in _uninitializedUses)
        {
            var severity = use.State == InitializationState.Uninitialized
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;

            var message = use.State == InitializationState.Uninitialized
                ? $"Variable '{use.VariableName}' is used before initialization"
                : $"Variable '{use.VariableName}' may not be initialized on all paths";

            diagnostics.Report(use.Span, DiagnosticCode.UninitializedVariable, message, severity);
        }
    }

    /// <summary>
    /// Gets the initialization state of a variable at a specific block entry.
    /// </summary>
    public InitializationState GetStateAtEntry(BasicBlock block, string variableName)
    {
        if (!_results.TryGetValue(block, out var result))
            return InitializationState.Uninitialized;

        var states = _allVariables
            .Where(pair => string.Equals(pair.Value.Name, variableName, StringComparison.Ordinal))
            .Select(pair => result.In.GetState(pair.Key))
            .Distinct()
            .ToArray();
        return states.Length switch
        {
            0 => InitializationState.Uninitialized,
            1 => states[0],
            _ => InitializationState.MaybeInitialized,
        };
    }

    public InitializationState GetStateAtEntry(BasicBlock block, SymbolId variableId) =>
        _results.TryGetValue(block, out var result)
            ? result.In.GetState(variableId)
            : InitializationState.Uninitialized;

    private void DetectUninitializedUses()
    {
        var usesByProgramPoint = new Dictionary<
            object,
            Dictionary<SymbolId, AggregatedUse>>(
            ReferenceEqualityComparer.Instance);

        foreach (var block in _cfg.ReachableBlocks)
        {
            if (!_results.TryGetValue(block, out var result))
                continue;

            var currentFacts = result.In;

            // Check each statement
            for (var index = 0; index < block.Statements.Count; index++)
            {
                var stmt = block.Statements[index];
                RecordUses(
                    stmt,
                    BoundNodeHelpers.GetUsedVariables(stmt),
                    stmt.Span,
                    currentFacts,
                    usesByProgramPoint);
                currentFacts = _transfer.Transfer(
                    block,
                    index,
                    stmt,
                    currentFacts);
            }

            foreach (var operation in block.SyntheticOperations)
            {
                RecordUses(
                    operation,
                    BoundNodeHelpers.GetUsedVariables(operation),
                    operation.Span,
                    currentFacts,
                    usesByProgramPoint);
                currentFacts = _transfer.TransferSynthetic(operation, currentFacts);
            }

            if (block.Terminator.Condition != null)
            {
                RecordUses(
                    block.Terminator.Condition,
                    BoundNodeHelpers.GetUsedVariables(block.Terminator.Condition),
                    block.Terminator.Condition.Span,
                    currentFacts,
                    usesByProgramPoint);
            }
        }

        foreach (var uses in usesByProgramPoint.Values)
        {
            foreach (var (variableId, use) in uses)
            {
                if (use.State != InitializationState.Initialized)
                {
                    _uninitializedUses.Add(new UninitializedUse(
                        use.VariableName,
                        use.Span,
                        use.State,
                        variableId));
                }
            }
        }
    }

    private void RecordUses(
        object programPoint,
        IEnumerable<VariableSymbol> variables,
        TextSpan span,
        InitializationFacts facts,
        Dictionary<object, Dictionary<SymbolId, AggregatedUse>> usesByProgramPoint)
    {
        if (!usesByProgramPoint.TryGetValue(programPoint, out var pointUses))
        {
            pointUses = new Dictionary<SymbolId, AggregatedUse>();
            usesByProgramPoint.Add(programPoint, pointUses);
        }

        foreach (var variable in variables)
        {
            var state = facts.GetState(variable.Id);
            if (pointUses.TryGetValue(variable.Id, out var existing))
            {
                pointUses[variable.Id] = existing with
                {
                    State = existing.State == state
                        ? state
                        : InitializationState.MaybeInitialized,
                };
            }
            else
            {
                pointUses.Add(variable.Id, new AggregatedUse(
                    variable.Name,
                    span,
                    state));
            }
        }
    }

    private readonly record struct AggregatedUse(
        string VariableName,
        TextSpan Span,
        InitializationState State);

    private static IReadOnlyDictionary<SymbolId, VariableSymbol> CollectAllVariables(ControlFlowGraph cfg)
    {
        var variables = new Dictionary<SymbolId, VariableSymbol>();

        foreach (var parameter in cfg.Function.Symbol.Parameters)
        {
            if (!parameter.Id.IsNone)
                variables.TryAdd(parameter.Id, parameter);
        }

        foreach (var block in cfg.ReachableBlocks)
        {
            foreach (var stmt in block.Statements)
            {
                var defined = BoundNodeHelpers.GetDefinedVariable(stmt);
                if (defined != null && !defined.Id.IsNone)
                    variables.TryAdd(defined.Id, defined);

                foreach (var used in BoundNodeHelpers.GetUsedVariables(stmt))
                {
                    if (!used.Id.IsNone)
                        variables.TryAdd(used.Id, used);
                }
            }

            foreach (var operation in block.SyntheticOperations)
            {
                var defined = BoundNodeHelpers.GetDefinedVariable(operation);
                if (defined != null && !defined.Id.IsNone)
                    variables.TryAdd(defined.Id, defined);

                foreach (var used in BoundNodeHelpers.GetUsedVariables(operation))
                {
                    if (!used.Id.IsNone)
                        variables.TryAdd(used.Id, used);
                }
            }

            if (block.Terminator.Condition != null)
            {
                foreach (var used in BoundNodeHelpers.GetUsedVariables(block.Terminator.Condition))
                {
                    if (!used.Id.IsNone)
                        variables.TryAdd(used.Id, used);
                }
            }
        }

        return variables;
    }
}

/// <summary>
/// Represents a use of a potentially uninitialized variable.
/// </summary>
public readonly record struct UninitializedUse(
    string VariableName,
    TextSpan Span,
    InitializationState State,
    SymbolId VariableId = default);

/// <summary>
/// Tracks initialization state for all variables.
/// </summary>
public readonly struct InitializationFacts : IEquatable<InitializationFacts>
{
    private readonly Dictionary<SymbolId, InitializationState>? _states;

    private InitializationFacts(Dictionary<SymbolId, InitializationState>? states)
    {
        _states = states;
    }

    public static InitializationFacts Empty => new(null);

    public static InitializationFacts Create(
        IEnumerable<SymbolId> variables,
        HashSet<SymbolId> initialized)
    {
        var states = new Dictionary<SymbolId, InitializationState>();
        foreach (var v in variables)
        {
            states[v] = initialized.Contains(v)
                ? InitializationState.Initialized
                : InitializationState.Uninitialized;
        }
        return new InitializationFacts(states);
    }

    public InitializationState GetState(SymbolId variableId)
    {
        if (_states == null)
            return InitializationState.Uninitialized;

        return _states.TryGetValue(variableId, out var state)
            ? state
            : InitializationState.Uninitialized;
    }

    public InitializationFacts SetInitialized(SymbolId variableId)
    {
        var newStates = _states != null
            ? new Dictionary<SymbolId, InitializationState>(_states)
            : new Dictionary<SymbolId, InitializationState>();

        newStates[variableId] = InitializationState.Initialized;
        return new InitializationFacts(newStates);
    }

    public InitializationFacts Join(InitializationFacts other, IEnumerable<SymbolId> allVariables)
    {
        if (_states == null)
            return other;
        if (other._states == null)
            return this;

        var newStates = new Dictionary<SymbolId, InitializationState>();

        foreach (var v in allVariables)
        {
            var thisState = GetState(v);
            var otherState = other.GetState(v);

            // Join: both must be initialized for the result to be initialized
            newStates[v] = (thisState, otherState) switch
            {
                (InitializationState.Initialized, InitializationState.Initialized) => InitializationState.Initialized,
                (InitializationState.Uninitialized, InitializationState.Uninitialized) => InitializationState.Uninitialized,
                _ => InitializationState.MaybeInitialized
            };
        }

        return new InitializationFacts(newStates);
    }

    public bool Equals(InitializationFacts other)
    {
        if (_states == null && other._states == null)
            return true;
        if (_states == null || other._states == null)
            return false;
        if (_states.Count != other._states.Count)
            return false;

        foreach (var (key, value) in _states)
        {
            if (!other._states.TryGetValue(key, out var otherValue) || value != otherValue)
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is InitializationFacts other && Equals(other);

    public override int GetHashCode()
    {
        if (_states == null)
            return 0;

        var hash = 0;
        foreach (var (key, value) in _states)
            hash ^= HashCode.Combine(key, value);
        return hash;
    }
}

internal sealed class InitializationLattice : IDataflowLattice<InitializationFacts>
{
    private readonly HashSet<SymbolId> _allVariables;

    public InitializationLattice(HashSet<SymbolId> allVariables)
    {
        _allVariables = allVariables;
    }

    // Empty is an explicit join identity; InitializationFacts.Join handles it
    // without interpreting missing entries as uninitialized program variables.
    public InitializationFacts Bottom => InitializationFacts.Empty;

    public InitializationFacts Top => InitializationFacts.Create(_allVariables, _allVariables);

    public InitializationFacts Join(InitializationFacts a, InitializationFacts b)
        => a.Join(b, _allVariables);

    public bool LessOrEqual(InitializationFacts a, InitializationFacts b)
    {
        foreach (var v in _allVariables)
        {
            var aState = a.GetState(v);
            var bState = b.GetState(v);

            if (aState != bState && bState != InitializationState.MaybeInitialized)
                return false;
        }
        return true;
    }
}

internal sealed class UninitializedVariablesTransfer : ITransferFunction<InitializationFacts>
{
    public InitializationFacts Transfer(
        BasicBlock block,
        int statementIndex,
        BoundStatement statement,
        InitializationFacts input) =>
        block.IsDefinitionDeferred(statementIndex)
            ? input
            : Transfer(statement, input);

    public InitializationFacts Transfer(BoundStatement statement, InitializationFacts input)
    {
        var defined = BoundNodeHelpers.GetDefinedVariable(statement);
        if (defined == null)
            return input;

        var initializes = statement switch
        {
            BoundBindStatement bind => bind.Initializer != null,
            BoundAssignmentStatement => true,
            BoundCompoundAssignment => true,
            _ => false,
        };
        if (initializes && !defined.Id.IsNone)
        {
            return input.SetInitialized(defined.Id);
        }

        return input;
    }

    public InitializationFacts TransferExpression(BoundExpression? expression, InitializationFacts input)
    {
        return input;
    }

    public InitializationFacts TransferSynthetic(
        SyntheticOperation operation,
        InitializationFacts input)
    {
        var defined = BoundNodeHelpers.GetDefinedVariable(operation);
        return defined != null && !defined.Id.IsNone
            ? input.SetInitialized(defined.Id)
            : input;
    }
}
