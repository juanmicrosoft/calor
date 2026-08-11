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
    private readonly HashSet<string> _allVariables;
    private readonly HashSet<string> _parameters;
    private readonly List<UninitializedUse> _uninitializedUses = new();

    public UninitializedVariablesAnalysis(ControlFlowGraph cfg, IEnumerable<string>? parameterNames = null)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _allVariables = CollectAllVariables(cfg);
        _parameters = parameterNames != null ? new HashSet<string>(parameterNames) : new HashSet<string>();

        var lattice = new InitializationLattice(_allVariables, _parameters);
        var transfer = new UninitializedVariablesTransfer(_allVariables);
        var analysis = new DataflowAnalysis<InitializationFacts>(
            lattice, transfer, DataflowDirection.Forward);

        _results = analysis.Analyze(cfg);

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
        if (_results.TryGetValue(block, out var result))
            return result.In.GetState(variableName);
        return InitializationState.Uninitialized;
    }

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
                    // Parameters are initialized by their caller: function parameters
                    // are seeded Initialized at entry, and a LAMBDA parameter (a scope
                    // this name-keyed analysis cannot see) is always initialized when
                    // its body runs — flagging it is wrong on any execution (#908 F2).
                    if (v.IsParameter)
                        continue;
                    var state = currentFacts.GetState(v.Name);
                    if (state != InitializationState.Initialized)
                    {
                        _uninitializedUses.Add(new UninitializedUse(v.Name, block.Span, state));
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
                    var state = currentFacts.GetState(v.Name);
                    if (state != InitializationState.Initialized)
                    {
                        _uninitializedUses.Add(new UninitializedUse(v.Name, stmt.Span, state));
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
                        currentFacts = currentFacts.SetInitialized(defined.Name);
                    }
                }
            }
        }
    }

    private static HashSet<string> CollectAllVariables(ControlFlowGraph cfg)
    {
        var variables = new HashSet<string>();

        foreach (var block in cfg.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                var defined = BoundNodeHelpers.GetDefinedVariable(stmt);
                if (defined != null)
                    variables.Add(defined.Name);

                foreach (var used in BoundNodeHelpers.GetUsedVariables(stmt))
                    variables.Add(used.Name);
            }

            if (block.BranchCondition != null)
            {
                foreach (var used in BoundNodeHelpers.GetUsedVariables(block.BranchCondition))
                    variables.Add(used.Name);
            }
        }

        return variables;
    }
}

/// <summary>
/// Represents a use of a potentially uninitialized variable.
/// </summary>
public readonly record struct UninitializedUse(string VariableName, TextSpan Span, InitializationState State);

/// <summary>
/// Tracks initialization state for all variables.
/// </summary>
public readonly struct InitializationFacts : IEquatable<InitializationFacts>
{
    private readonly Dictionary<string, InitializationState>? _states;

    private InitializationFacts(Dictionary<string, InitializationState>? states)
    {
        _states = states;
    }

    public static InitializationFacts Empty => new(null);

    public static InitializationFacts Create(IEnumerable<string> variables, HashSet<string> initialized)
    {
        var states = new Dictionary<string, InitializationState>();
        foreach (var v in variables)
        {
            states[v] = initialized.Contains(v)
                ? InitializationState.Initialized
                : InitializationState.Uninitialized;
        }
        return new InitializationFacts(states);
    }

    public InitializationState GetState(string variableName)
    {
        if (_states == null)
            return InitializationState.Uninitialized;

        return _states.TryGetValue(variableName, out var state)
            ? state
            : InitializationState.Uninitialized;
    }

    public InitializationFacts SetInitialized(string variableName)
    {
        var newStates = _states != null
            ? new Dictionary<string, InitializationState>(_states)
            : new Dictionary<string, InitializationState>();

        newStates[variableName] = InitializationState.Initialized;
        return new InitializationFacts(newStates);
    }

    public InitializationFacts Join(InitializationFacts other, IEnumerable<string> allVariables)
    {
        var newStates = new Dictionary<string, InitializationState>();

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
    private readonly HashSet<string> _allVariables;
    private readonly HashSet<string> _parameters;

    public InitializationLattice(HashSet<string> allVariables, HashSet<string> parameters)
    {
        _allVariables = allVariables;
        _parameters = parameters;
    }

    // Bottom: nothing is definitely initialized (most optimistic for detecting errors)
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
    private readonly HashSet<string> _allVariables;

    public UninitializedVariablesTransfer(HashSet<string> allVariables)
    {
        _allVariables = allVariables;
    }

    public InitializationFacts Transfer(BoundStatement statement, InitializationFacts input)
    {
        var defined = BoundNodeHelpers.GetDefinedVariable(statement);
        if (defined == null)
            return input;

        // Check if the variable has an initializer
        var hasInitializer = statement is BoundBindStatement bind && bind.Initializer != null;
        if (hasInitializer)
        {
            return input.SetInitialized(defined.Name);
        }

        return input;
    }

    public InitializationFacts TransferExpression(BoundExpression? expression, InitializationFacts input)
    {
        return input;
    }
}
