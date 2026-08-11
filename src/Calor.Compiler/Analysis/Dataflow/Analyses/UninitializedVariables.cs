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
    public IReadOnlyList<BoundNode> IncompleteNodes { get; }
    public bool IsComplete => IncompleteNodes.Count == 0;

    public UninitializedVariablesAnalysis(ControlFlowGraph cfg, IEnumerable<string>? parameterNames = null)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _allVariables = CollectAllVariables(cfg);
        IncompleteNodes = BoundNodeHelpers.GetAnalysisIncompleteNodes(cfg.Function).ToArray();
        var names = parameterNames?.ToHashSet(StringComparer.Ordinal);
        _parameters = cfg.Function.Symbol.Parameters
            .Where(parameter => names == null || names.Contains(parameter.Name))
            .Where(parameter => !parameter.Id.IsNone)
            .Select(parameter => parameter.Id)
            .ToHashSet();

        var lattice = new InitializationLattice(_allVariables.Keys.ToHashSet(), _parameters);
        var transfer = new UninitializedVariablesTransfer();
        var analysis = new DataflowAnalysis<InitializationFacts>(
            lattice, transfer, DataflowDirection.Forward);

        _results = analysis.Analyze(cfg);

        // Detect uninitialized uses
        DetectUninitializedUses();
    }

    public UninitializedVariablesAnalysis(
        ControlFlowGraph cfg,
        IEnumerable<VariableSymbol> parameters)
        : this(cfg, parameters.Select(parameter => parameter.Name))
    {
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
        foreach (var block in _cfg.Blocks)
        {
            if (!_results.TryGetValue(block, out var result))
                continue;

            var currentFacts = result.In;

            // Check condition expression
            if (block.BranchCondition != null)
            {
                foreach (var v in BoundNodeHelpers.GetUsedVariables(block.BranchCondition))
                {
                    if (v.IsParameter)
                        continue;
                    var state = currentFacts.GetState(v.Id);
                    if (state != InitializationState.Initialized)
                    {
                        _uninitializedUses.Add(new UninitializedUse(v.Name, block.Span, state, v.Id));
                    }
                }
            }

            // Check each statement
            foreach (var stmt in block.Statements)
            {
                // Check uses first
                foreach (var v in BoundNodeHelpers.GetUsedVariables(stmt))
                {
                    if (v.IsParameter)
                        continue;
                    var state = currentFacts.GetState(v.Id);
                    if (state != InitializationState.Initialized)
                    {
                        _uninitializedUses.Add(new UninitializedUse(v.Name, stmt.Span, state, v.Id));
                    }
                }

                // Update facts for the next statement
                var defined = BoundNodeHelpers.GetDefinedVariable(stmt);
                if (defined != null)
                {
                    // Check if the variable has an initializer
                    var hasInitializer = stmt is BoundBindStatement bind && bind.Initializer != null;
                    if (hasInitializer)
                    {
                        currentFacts = currentFacts.SetInitialized(defined.Id);
                    }
                }
            }
        }
    }

    private static IReadOnlyDictionary<SymbolId, VariableSymbol> CollectAllVariables(ControlFlowGraph cfg)
    {
        var variables = new Dictionary<SymbolId, VariableSymbol>();

        foreach (var parameter in cfg.Function.Symbol.Parameters)
        {
            if (!parameter.Id.IsNone)
                variables.TryAdd(parameter.Id, parameter);
        }

        foreach (var block in cfg.Blocks)
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

            if (block.BranchCondition != null)
            {
                foreach (var used in BoundNodeHelpers.GetUsedVariables(block.BranchCondition))
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
    private readonly HashSet<SymbolId> _parameters;

    public InitializationLattice(HashSet<SymbolId> allVariables, HashSet<SymbolId> parameters)
    {
        _allVariables = allVariables;
        _parameters = parameters;
    }

    // Empty is an explicit join identity; InitializationFacts.Join handles it
    // without interpreting missing entries as uninitialized program variables.
    public InitializationFacts Bottom => InitializationFacts.Empty;

    // Top: all variables are initialized (includes parameters)
    public InitializationFacts Top => InitializationFacts.Create(_allVariables, _parameters);

    public InitializationFacts Join(InitializationFacts a, InitializationFacts b)
        => a.Join(b, _allVariables);

    public bool LessOrEqual(InitializationFacts a, InitializationFacts b)
    {
        foreach (var v in _allVariables)
        {
            var aState = a.GetState(v);
            var bState = b.GetState(v);

            // a <= b if for all variables, a's state is "less certain" than b's
            // Uninitialized < MaybeInitialized < Initialized
            if ((int)aState > (int)bState)
                return false;
        }
        return true;
    }
}

internal sealed class UninitializedVariablesTransfer : ITransferFunction<InitializationFacts>
{
    public InitializationFacts Transfer(BoundStatement statement, InitializationFacts input)
    {
        var defined = BoundNodeHelpers.GetDefinedVariable(statement);
        if (defined == null)
            return input;

        // Check if the variable has an initializer
        var hasInitializer = statement is BoundBindStatement bind && bind.Initializer != null;
        if (hasInitializer)
        {
            return input.SetInitialized(defined.Id);
        }

        return input;
    }

    public InitializationFacts TransferExpression(BoundExpression? expression, InitializationFacts input)
    {
        return input;
    }
}
