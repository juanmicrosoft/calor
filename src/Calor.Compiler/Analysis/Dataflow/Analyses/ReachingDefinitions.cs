using Calor.Compiler.Binding;

namespace Calor.Compiler.Analysis.Dataflow.Analyses;

/// <summary>
/// Represents a definition site (where a variable is assigned a value).
/// </summary>
public readonly record struct DefinitionSite(
    string VariableName,
    int BlockId,
    int StatementIndex,
    BoundStatement? Statement,
    SymbolId VariableId = default,
    int DefinitionOrdinal = -1,
    SyntheticOperation? SyntheticOperation = null)
{
    public override string ToString() =>
        $"{VariableName}@BB{BlockId}:{StatementIndex}#{DefinitionOrdinal}";
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
    public DataflowAnalysisResult<ImmutableHashSet<DefinitionSite>> AnalysisResult { get; }

    public ReachingDefinitionsAnalysis(ControlFlowGraph cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _allDefinitions = CollectAllDefinitions(cfg);
        IncompleteNodes = BoundNodeHelpers.GetAnalysisIncompleteNodes(cfg.Function).ToArray();

        var lattice = new SetLattice<DefinitionSite>(_allDefinitions);
        var transfer = new ReachingDefinitionsTransfer(_allDefinitions);
        var analysis = new DataflowAnalysis<ImmutableHashSet<DefinitionSite>>(
            lattice,
            transfer,
            DataflowDirection.Forward,
            ImmutableHashSet<DefinitionSite>.Empty,
            ImmutableHashSet<DefinitionSite>.Empty,
            ImmutableHashSet<DefinitionSite>.Empty);

        AnalysisResult = analysis.AnalyzeWithMetadata(cfg);
        _results = AnalysisResult.Blocks;
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
        var definitionOrdinal = 0;

        foreach (var block in cfg.ReachableBlocks)
        {
            for (var i = 0; i < block.Statements.Count; i++)
            {
                var stmt = block.Statements[i];
                var defined = BoundNodeHelpers.GetDefinedVariable(stmt);
                if (defined != null && !block.IsDefinitionDeferred(i))
                {
                    definitions.Add(new DefinitionSite(
                        defined.Name,
                        block.Id,
                        i,
                        stmt,
                        defined.Id,
                        definitionOrdinal++));
                }
            }

            for (var i = 0; i < block.SyntheticOperations.Count; i++)
            {
                var operation = block.SyntheticOperations[i];
                var defined = BoundNodeHelpers.GetDefinedVariable(operation);
                if (defined != null)
                {
                    definitions.Add(new DefinitionSite(
                        defined.Name,
                        block.Id,
                        block.Statements.Count + i,
                        operation.SourceStatement,
                        defined.Id,
                        definitionOrdinal++,
                        operation));
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
        => TransferDefinition(
            BoundNodeHelpers.GetDefinedVariable(statement),
            input,
            definition => ReferenceEquals(definition.Statement, statement));

    public ImmutableHashSet<DefinitionSite> Transfer(
        BasicBlock block,
        int statementIndex,
        BoundStatement statement,
        ImmutableHashSet<DefinitionSite> input)
        => block.IsDefinitionDeferred(statementIndex)
            ? input
            : TransferDefinition(
                BoundNodeHelpers.GetDefinedVariable(statement),
                input,
                definition => definition.BlockId == block.Id
                    && definition.StatementIndex == statementIndex
                    && ReferenceEquals(definition.Statement, statement));

    private ImmutableHashSet<DefinitionSite> TransferDefinition(
        VariableSymbol? defined,
        ImmutableHashSet<DefinitionSite> input,
        Func<DefinitionSite, bool> matchesDefinition)
    {
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
        var newDef = _allDefinitions.FirstOrDefault(definition =>
            matchesDefinition(definition) && SameVariable(definition, defined));

        if (newDef.DefinitionOrdinal >= 0)
        {
            return afterKill.Add(newDef);
        }

        return afterKill;
    }

    public ImmutableHashSet<DefinitionSite> TransferSynthetic(
        SyntheticOperation operation,
        ImmutableHashSet<DefinitionSite> input)
        => TransferSyntheticCore(operation, input, definition =>
            ReferenceEquals(definition.SyntheticOperation, operation));

    public ImmutableHashSet<DefinitionSite> TransferSynthetic(
        BasicBlock block,
        int operationIndex,
        SyntheticOperation operation,
        ImmutableHashSet<DefinitionSite> input)
        => TransferSyntheticCore(operation, input, definition =>
            definition.BlockId == block.Id
            && definition.StatementIndex == block.Statements.Count + operationIndex
            && ReferenceEquals(definition.SyntheticOperation, operation));

    private ImmutableHashSet<DefinitionSite> TransferSyntheticCore(
        SyntheticOperation operation,
        ImmutableHashSet<DefinitionSite> input,
        Func<DefinitionSite, bool> matchesDefinition)
    {
        var defined = BoundNodeHelpers.GetDefinedVariable(operation);
        if (defined == null)
            return input;

        var afterKill = input;
        foreach (var definition in input.AsEnumerable().Where(definition =>
                     SameVariable(definition, defined)))
        {
            afterKill = afterKill.Remove(definition);
        }

        var generated = _allDefinitions.FirstOrDefault(definition =>
            matchesDefinition(definition)
            && SameVariable(definition, defined));
        return generated.DefinitionOrdinal >= 0
            ? afterKill.Add(generated)
            : afterKill;
    }

    private static bool SameVariable(DefinitionSite definition, VariableSymbol variable)
    {
        if (!definition.VariableId.IsNone && !variable.Id.IsNone)
            return definition.VariableId == variable.Id;

        return ReferenceEquals(
            definition.Statement != null
                ? BoundNodeHelpers.GetDefinedVariable(definition.Statement)
                : definition.SyntheticOperation?.DefinedVariable,
            variable);
    }

    public ImmutableHashSet<DefinitionSite> TransferExpression(BoundExpression? expression, ImmutableHashSet<DefinitionSite> input)
    {
        // Expressions don't define variables
        return input;
    }
}
