using Calor.Compiler.Binding;

namespace Calor.Compiler.Analysis.Dataflow.Analyses;

/// <summary>
/// Represents a definition site (where a variable is assigned a value).
/// </summary>
public readonly record struct DefinitionSite(
    string VariableName,
    int BlockId,
    int StatementIndex,
    BoundStatement Statement,
    SymbolId VariableId = default)
{
    public override string ToString() => $"{VariableName}@BB{BlockId}:{StatementIndex}";
}

/// <summary>
/// Reaching definitions analysis: determines which definitions may reach each program point.
/// This is a forward may-analysis (uses union at join points).
/// </summary>
public sealed class ReachingDefinitionsAnalysis
{
    private readonly ControlFlowGraph _cfg;
    private readonly Dictionary<BasicBlock, BlockDataflowResult<ImmutableHashSet<DefinitionSite>>> _results;
    private readonly List<DefinitionSite> _allDefinitions;
    public IReadOnlyList<BoundNode> IncompleteNodes { get; }
    public bool IsComplete => IncompleteNodes.Count == 0;

    public ReachingDefinitionsAnalysis(ControlFlowGraph cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _allDefinitions = CollectAllDefinitions(cfg);
        IncompleteNodes = BoundNodeHelpers.GetAnalysisIncompleteNodes(cfg.Function).ToArray();

        var lattice = new SetLattice<DefinitionSite>(_allDefinitions);
        var transfer = new ReachingDefinitionsTransfer(_allDefinitions);
        var analysis = new DataflowAnalysis<ImmutableHashSet<DefinitionSite>>(
            lattice, transfer, DataflowDirection.Forward);

        _results = analysis.Analyze(cfg);
    }

    /// <summary>
    /// Gets the definitions that may reach the entry of a block.
    /// </summary>
    public IEnumerable<DefinitionSite> GetReachingDefinitionsAtEntry(BasicBlock block)
    {
        if (_results.TryGetValue(block, out var result))
            return result.In.AsEnumerable();
        return Enumerable.Empty<DefinitionSite>();
    }

    /// <summary>
    /// Gets the definitions that may reach the exit of a block.
    /// </summary>
    public IEnumerable<DefinitionSite> GetReachingDefinitionsAtExit(BasicBlock block)
    {
        if (_results.TryGetValue(block, out var result))
            return result.Out.AsEnumerable();
        return Enumerable.Empty<DefinitionSite>();
    }

    /// <summary>
    /// Gets the definitions of a specific variable that may reach a program point.
    /// </summary>
    public IEnumerable<DefinitionSite> GetReachingDefinitions(BasicBlock block, string variableName)
    {
        return GetReachingDefinitionsAtEntry(block)
            .Where(d => d.VariableName == variableName);
    }

    public IEnumerable<DefinitionSite> GetReachingDefinitions(BasicBlock block, SymbolId variableId)
    {
        return GetReachingDefinitionsAtEntry(block)
            .Where(definition => definition.VariableId == variableId);
    }

    /// <summary>
    /// Checks if a variable has multiple reaching definitions at a point (potential issue).
    /// </summary>
    public bool HasMultipleReachingDefinitions(BasicBlock block, string variableName)
    {
        return GetReachingDefinitions(block, variableName).Count() > 1;
    }

    public bool HasMultipleReachingDefinitions(BasicBlock block, SymbolId variableId) =>
        GetReachingDefinitions(block, variableId).Count() > 1;

    /// <summary>
    /// Gets all definition sites in the function.
    /// </summary>
    public IReadOnlyList<DefinitionSite> AllDefinitions => _allDefinitions;

    private static List<DefinitionSite> CollectAllDefinitions(ControlFlowGraph cfg)
    {
        var definitions = new List<DefinitionSite>();

        foreach (var block in cfg.Blocks)
        {
            for (var i = 0; i < block.Statements.Count; i++)
            {
                var stmt = block.Statements[i];
                var defined = BoundNodeHelpers.GetDefinedVariable(stmt);
                if (defined != null)
                {
                    definitions.Add(new DefinitionSite(
                        defined.Name,
                        block.Id,
                        i,
                        stmt,
                        defined.Id));
                }
            }
        }

        return definitions;
    }
}

internal sealed class ReachingDefinitionsTransfer : ITransferFunction<ImmutableHashSet<DefinitionSite>>
{
    private readonly List<DefinitionSite> _allDefinitions;

    public ReachingDefinitionsTransfer(List<DefinitionSite> allDefinitions)
    {
        _allDefinitions = allDefinitions;
    }

    public ImmutableHashSet<DefinitionSite> Transfer(BoundStatement statement, ImmutableHashSet<DefinitionSite> input)
    {
        var defined = BoundNodeHelpers.GetDefinedVariable(statement);
        if (defined == null)
            return input;

        // Kill: remove all previous definitions of the same variable
        var afterKill = input;
        foreach (var def in input.AsEnumerable().Where(d =>
                     SameVariable(d, defined)))
        {
            afterKill = afterKill.Remove(def);
        }

        // Gen: add the new definition
        var newDef = _allDefinitions.FirstOrDefault(d =>
            d.Statement == statement && SameVariable(d, defined));

        if (newDef.Statement != null)
        {
            return afterKill.Add(newDef);
        }

        return afterKill;
    }

    private static bool SameVariable(DefinitionSite definition, VariableSymbol variable)
    {
        if (!definition.VariableId.IsNone && !variable.Id.IsNone)
            return definition.VariableId == variable.Id;

        return ReferenceEquals(
            BoundNodeHelpers.GetDefinedVariable(definition.Statement),
            variable);
    }

    public ImmutableHashSet<DefinitionSite> TransferExpression(BoundExpression? expression, ImmutableHashSet<DefinitionSite> input)
    {
        // Expressions don't define variables
        return input;
    }
}
