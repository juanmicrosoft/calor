using Calor.Compiler.Binding;

namespace Calor.Compiler.Analysis.Dataflow.Analyses;

/// <summary>
/// Live variables analysis: determines which variables may be used before being redefined.
/// This is a backward may-analysis.
/// A variable is live at a point if there exists a path from that point to a use of the variable
/// that doesn't pass through a definition of the variable.
/// </summary>
public sealed class LiveVariablesAnalysis
{
    private readonly ControlFlowGraph _cfg;
    private readonly Dictionary<BasicBlock, BlockDataflowResult<ImmutableHashSet<SymbolId>>> _results;
    private readonly IReadOnlyDictionary<SymbolId, VariableSymbol> _allVariables;
    public IReadOnlyList<BoundNode> IncompleteNodes { get; }
    public bool IsComplete => IncompleteNodes.Count == 0;

    public LiveVariablesAnalysis(ControlFlowGraph cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _allVariables = CollectAllVariables(cfg);
        IncompleteNodes = BoundNodeHelpers.GetAnalysisIncompleteNodes(cfg.Function).ToArray();

        var lattice = new SetLattice<SymbolId>(_allVariables.Keys);
        var transfer = new LiveVariablesTransfer();
        var analysis = new DataflowAnalysis<ImmutableHashSet<SymbolId>>(
            lattice, transfer, DataflowDirection.Backward);

        _results = analysis.Analyze(cfg);
    }

    /// <summary>
    /// Gets the variables that are live at the entry of a block.
    /// </summary>
    public IEnumerable<string> GetLiveVariablesAtEntry(BasicBlock block)
    {
        return GetLiveSymbolIdsAtEntry(block)
            .Select(id => _allVariables.TryGetValue(id, out var variable) ? variable.Name : id.ToString());
    }

    public IEnumerable<SymbolId> GetLiveSymbolIdsAtEntry(BasicBlock block) =>
        _results.TryGetValue(block, out var result)
            ? result.In.AsEnumerable()
            : Enumerable.Empty<SymbolId>();

    /// <summary>
    /// Gets the variables that are live at the exit of a block.
    /// </summary>
    public IEnumerable<string> GetLiveVariablesAtExit(BasicBlock block)
    {
        return GetLiveSymbolIdsAtExit(block)
            .Select(id => _allVariables.TryGetValue(id, out var variable) ? variable.Name : id.ToString());
    }

    public IEnumerable<SymbolId> GetLiveSymbolIdsAtExit(BasicBlock block) =>
        _results.TryGetValue(block, out var result)
            ? result.Out.AsEnumerable()
            : Enumerable.Empty<SymbolId>();

    /// <summary>
    /// Checks if a variable is live at a specific point.
    /// </summary>
    public bool IsLive(BasicBlock block, string variableName, bool atEntry = true)
    {
        if (_results.TryGetValue(block, out var result))
        {
            var facts = atEntry ? result.In : result.Out;
            return facts.AsEnumerable().Any(id =>
                _allVariables.TryGetValue(id, out var variable)
                && string.Equals(variable.Name, variableName, StringComparison.Ordinal));
        }
        return false;
    }

    public bool IsLive(BasicBlock block, SymbolId variableId, bool atEntry = true)
    {
        if (!_results.TryGetValue(block, out var result))
            return false;

        return (atEntry ? result.In : result.Out).Contains(variableId);
    }

    /// <summary>
    /// Finds dead assignments (definitions where the variable is not live after).
    /// </summary>
    public IEnumerable<(BasicBlock Block, BoundStatement Statement, string Variable)> FindDeadAssignments()
    {
        return FindDeadAssignmentsWithSymbols()
            .Select(item => (item.Block, item.Statement, item.Variable.Name));
    }

    public IEnumerable<(BasicBlock Block, BoundStatement Statement, VariableSymbol Variable)> FindDeadAssignmentsWithSymbols()
    {
        foreach (var block in _cfg.Blocks)
        {
            if (!_results.TryGetValue(block, out var result))
                continue;

            var liveAfter = result.Out;

            // Process statements in reverse order
            for (var i = block.Statements.Count - 1; i >= 0; i--)
            {
                var stmt = block.Statements[i];
                var defined = BoundNodeHelpers.GetDefinedVariable(stmt);

                if (defined != null && !liveAfter.Contains(defined.Id))
                {
                    // This definition is not live after - it's dead
                    yield return (block, stmt, defined);
                }

                // Update liveAfter for the next iteration (going backwards)
                // Gen: add used variables
                foreach (var used in BoundNodeHelpers.GetUsedVariables(stmt))
                {
                    liveAfter = liveAfter.Add(used.Id);
                }

                // Kill: remove defined variables
                if (defined != null)
                {
                    liveAfter = liveAfter.Remove(defined.Id);
                }
            }
        }
    }

    private static IReadOnlyDictionary<SymbolId, VariableSymbol> CollectAllVariables(ControlFlowGraph cfg)
    {
        var variables = new Dictionary<SymbolId, VariableSymbol>();

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

internal sealed class LiveVariablesTransfer : ITransferFunction<ImmutableHashSet<SymbolId>>
{
    public ImmutableHashSet<SymbolId> Transfer(BoundStatement statement, ImmutableHashSet<SymbolId> input)
    {
        var result = input;

        // Gen: add used variables (they must be live before this statement)
        foreach (var used in BoundNodeHelpers.GetUsedVariables(statement))
        {
            if (!used.Id.IsNone)
                result = result.Add(used.Id);
        }

        // Kill: remove defined variables (they don't need to be live before this definition)
        var defined = BoundNodeHelpers.GetDefinedVariable(statement);
        if (defined != null && !defined.Id.IsNone)
        {
            result = result.Remove(defined.Id);
        }

        return result;
    }

    public ImmutableHashSet<SymbolId> TransferExpression(BoundExpression? expression, ImmutableHashSet<SymbolId> input)
    {
        if (expression == null)
            return input;

        var result = input;
        foreach (var used in BoundNodeHelpers.GetUsedVariables(expression))
        {
            if (!used.Id.IsNone)
                result = result.Add(used.Id);
        }

        return result;
    }
}
