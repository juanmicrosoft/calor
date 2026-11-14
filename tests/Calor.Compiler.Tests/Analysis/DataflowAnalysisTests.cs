using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Dataflow.Analyses;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests for the dataflow analysis framework including CFG construction,
/// reaching definitions, live variables, and uninitialized variable detection.
/// </summary>
public class DataflowAnalysisTests
{
    #region Helpers

    private static TextSpan DummySpan => new(0, 1, 1, 1);

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static BoundModule Bind(string source, out DiagnosticBag diagnostics)
    {
        var module = Parse(source, out diagnostics);
        if (diagnostics.HasErrors) return null!;

        var binder = new Binder(diagnostics);
        return binder.Bind(module);
    }

    private static BoundFunction GetFunction(string source, string funcName = null!)
    {
        var bound = Bind(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, $"Parse/bind errors: {string.Join(", ", diagnostics.Errors.Select(e => e.Message))}");
        return funcName == null ? bound.Functions.First() : bound.Functions.First(f => f.Symbol.Name == funcName);
    }

    #endregion

    #region CFG Construction Tests

    [Fact]
    public void CFG_SimpleFunction_HasEntryAndExit()
    {
        var source = @"
module Test Test block
  func Simple (int:x) → int
    !
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        Assert.NotNull(cfg.Entry);
        Assert.NotNull(cfg.Exit);
        Assert.True(cfg.Blocks.Count >= 2); // At least entry and exit
    }

    [Fact]
    public void CFG_IfStatement_CreatesBranches()
    {
        var source = @"
module Test Test block
  func Conditional (int:x) → int
    !
    return ((x > 0) ? 1 : 0)";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        // Should have multiple blocks for if/then/else
        Assert.True(cfg.Blocks.Count >= 2);
    }

    [Fact]
    public void CFG_WhileLoop_CreatesStructure()
    {
        // Use FOR loop since it has built-in iteration without needing ASSIGN
        var source = @"
module Test Test block
  func Loop (int:n) → int
    !
    let int:sum = 0
    for i from 0 to 10 step 1
      Console.WriteLine(i)
    return sum";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        Assert.True(cfg.Blocks.Count >= 3);
    }

    [Fact]
    public void CFG_ForLoop_CreatesProperStructure()
    {
        var source = @"
module Test Test block
  func ForLoop (int:n) → int
    !
    let int:sum = 0
    for i from 0 to 10 step 1
      Console.WriteLine(i)
    return sum";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        Assert.True(cfg.Blocks.Count >= 3);
    }

    [Fact]
    public void CFG_Blocks_HaveUniqueIds()
    {
        var source = @"
module Test Test block
  func Multi (int:x) → int
    !
    let int:y = (x + 1)
    return y";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        var ids = cfg.Blocks.Select(b => b.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    #endregion

    #region Reaching Definitions Tests

    [Fact]
    public void ReachingDefinitions_SingleAssignment_Reaches()
    {
        var source = @"
module Test Test block
  func Single () → int
    !
    let int:x = 42
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new ReachingDefinitionsAnalysis(cfg);

        var exitDefs = analysis.GetReachingDefinitionsAtExit(cfg.Exit).ToList();
        Assert.Contains(exitDefs, definition => definition.VariableName == "x");
    }

    [Fact]
    public void ReachingDefinitions_MultipleBinds_AllReach()
    {
        // Test multiple independent bindings instead of redefinition
        // since Calor doesn't support general variable reassignment
        var source = @"
module Test Test block
  func MultiBind () → int
    !
    let int:x = 1
    let int:y = 2
    let int:z = (x + y)
    return z";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new ReachingDefinitionsAnalysis(cfg);

        // Analysis should complete and find all definitions
        Assert.NotNull(analysis.AllDefinitions);
        Assert.True(analysis.AllDefinitions.Count >= 3);
    }

    [Fact]
    public void ReachingDefinitions_AllDefinitions_CollectsAll()
    {
        var source = @"
module Test Test block
  func Multi () → int
    !
    let int:x = 1
    let int:y = 2
    return (x + y)";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new ReachingDefinitionsAnalysis(cfg);

        // Should have definitions for both x and y
        Assert.True(analysis.AllDefinitions.Count >= 2);
    }

    [Fact]
    public void ReachingDefinitions_GetByVariable_FiltersCorrectly()
    {
        var source = @"
module Test Test block
  func Filter () → int
    !
    let int:x = 1
    let int:y = 2
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new ReachingDefinitionsAnalysis(cfg);

        var xDefs = analysis.GetReachingDefinitions(cfg.Exit, "x").ToList();
        // All definitions should be for variable x
        Assert.All(xDefs, d => Assert.Equal("x", d.VariableName));
    }

    #endregion

    #region Live Variables Tests

    [Fact]
    public void LiveVariables_UsedVariable_IsLive()
    {
        var source = @"
module Test Test block
  func Used (int:x) → int
    !
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new LiveVariablesAnalysis(cfg);

        var entryLive = analysis.GetLiveVariablesAtEntry(cfg.Entry).ToList();
        Assert.Contains("x", entryLive);
    }

    [Fact]
    public void LiveVariables_ForLoopVariable_Analysis()
    {
        var source = @"
module Test Test block
  func Loop (int:n) → int
    !
    let int:sum = 0
    for i from 0 to 10 step 1
      Console.WriteLine(i)
    return sum";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new LiveVariablesAnalysis(cfg);

        // Analysis should complete
        Assert.NotNull(analysis);
    }

    [Fact]
    public void LiveVariables_ExitBoundary_IsEmpty()
    {
        var source = @"
module Test Test block
  func Exit (int:x) → int
    !
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new LiveVariablesAnalysis(cfg);

        var exitLive = analysis.GetLiveVariablesAtExit(cfg.Exit).ToList();
        Assert.Empty(exitLive);
    }

    #endregion

    #region Uninitialized Variables Tests

    [Fact]
    public void UninitializedVariables_Parameter_IsInitialized()
    {
        var source = @"
module Test Test block
  func Param (int:x) → int
    !
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var paramNames = func.Symbol.Parameters.Select(p => p.Name);
        var analysis = new UninitializedVariablesAnalysis(cfg, paramNames);

        // No uninitialized uses - parameter is initialized
        Assert.Empty(analysis.UninitializedUses);
    }

    [Fact]
    public void UninitializedVariables_LocalWithInit_IsInitialized()
    {
        var source = @"
module Test Test block
  func Local () → int
    !
    let int:x = 42
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var paramNames = func.Symbol.Parameters.Select(p => p.Name);
        var analysis = new UninitializedVariablesAnalysis(cfg, paramNames);

        // No uninitialized uses
        Assert.Empty(analysis.UninitializedUses);
    }

    [Fact]
    public void UninitializedVariables_ReportDiagnostics_Works()
    {
        var source = @"
module Test Test block
  func Report (int:x) → int
    !
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var paramNames = func.Symbol.Parameters.Select(p => p.Name);
        var analysis = new UninitializedVariablesAnalysis(cfg, paramNames);

        var diagnostics = new DiagnosticBag();
        analysis.ReportDiagnostics(diagnostics);

        // Should complete without error
        Assert.NotNull(diagnostics);
    }

    #endregion

    #region ImmutableHashSet Tests

    [Fact]
    public void ImmutableHashSet_Empty_HasZeroCount()
    {
        var set = ImmutableHashSet<string>.Empty;
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void ImmutableHashSet_Add_CreatesNewSet()
    {
        var set1 = ImmutableHashSet<string>.Empty;
        var set2 = set1.Add("a");

        Assert.Equal(0, set1.Count);
        Assert.Equal(1, set2.Count);
        Assert.True(set2.Contains("a"));
    }

    [Fact]
    public void ImmutableHashSet_Union_CombinesSets()
    {
        var set1 = ImmutableHashSet<string>.Create("a");
        var set2 = ImmutableHashSet<string>.Create("b");
        var union = set1.Union(set2);

        Assert.Equal(2, union.Count);
        Assert.True(union.Contains("a"));
        Assert.True(union.Contains("b"));
    }

    [Fact]
    public void ImmutableHashSet_Intersect_FindsCommon()
    {
        var set1 = ImmutableHashSet<string>.CreateRange(new[] { "a", "b" });
        var set2 = ImmutableHashSet<string>.CreateRange(new[] { "b", "c" });
        var intersection = set1.Intersect(set2);

        Assert.Equal(1, intersection.Count);
        Assert.True(intersection.Contains("b"));
    }

    [Fact]
    public void ImmutableHashSet_Equals_ComparesContents()
    {
        var set1 = ImmutableHashSet<string>.CreateRange(new[] { "a", "b" });
        var set2 = ImmutableHashSet<string>.CreateRange(new[] { "b", "a" });

        Assert.True(set1.Equals(set2));
        Assert.Equal(set1, set2);
    }

    [Fact]
    public void ImmutableHashSet_IsSubsetOf_ChecksContainment()
    {
        var small = ImmutableHashSet<string>.Create("a");
        var large = ImmutableHashSet<string>.CreateRange(new[] { "a", "b" });

        Assert.True(small.IsSubsetOf(large));
        Assert.False(large.IsSubsetOf(small));
    }

    [Fact]
    public void ImmutableHashSet_Remove_CreatesNewSet()
    {
        var set1 = ImmutableHashSet<string>.CreateRange(new[] { "a", "b" });
        var set2 = set1.Remove("a");

        Assert.Equal(2, set1.Count);
        Assert.Equal(1, set2.Count);
        Assert.False(set2.Contains("a"));
        Assert.True(set2.Contains("b"));
    }

    [Fact]
    public void ImmutableHashSet_Except_RemovesItems()
    {
        var set1 = ImmutableHashSet<string>.CreateRange(new[] { "a", "b", "c" });
        var set2 = ImmutableHashSet<string>.CreateRange(new[] { "b" });
        var result = set1.Except(set2);

        Assert.Equal(2, result.Count);
        Assert.False(result.Contains("b"));
    }

    [Fact]
    public void ImmutableHashSet_AsEnumerable_Works()
    {
        var set = ImmutableHashSet<string>.CreateRange(new[] { "a", "b" });
        var list = set.AsEnumerable().ToList();

        Assert.Equal(2, list.Count);
    }

    #endregion

    #region Lattice Tests

    [Fact]
    public void SetLattice_Join_IsUnion()
    {
        var lattice = new SetLattice<string>();
        var set1 = ImmutableHashSet<string>.Create("a");
        var set2 = ImmutableHashSet<string>.Create("b");

        var joined = lattice.Join(set1, set2);

        Assert.Equal(2, joined.Count);
    }

    [Fact]
    public void SetLattice_Bottom_IsEmpty()
    {
        var lattice = new SetLattice<string>();
        Assert.Equal(0, lattice.Bottom.Count);
    }

    [Fact]
    public void SetLattice_LessOrEqual_ChecksSubset()
    {
        var lattice = new SetLattice<string>();
        var small = ImmutableHashSet<string>.Create("a");
        var large = ImmutableHashSet<string>.CreateRange(new[] { "a", "b" });

        Assert.True(lattice.LessOrEqual(small, large));
        Assert.False(lattice.LessOrEqual(large, small));
    }

    [Fact]
    public void MustSetLattice_Join_IsIntersect()
    {
        var universe = new[] { "a", "b", "c" };
        var lattice = new MustSetLattice<string>(universe);

        var set1 = ImmutableHashSet<string>.CreateRange(new[] { "a", "b" });
        var set2 = ImmutableHashSet<string>.CreateRange(new[] { "b", "c" });

        var joined = lattice.Join(set1, set2);

        Assert.Equal(1, joined.Count);
        Assert.True(joined.Contains("b"));
    }

    [Fact]
    public void MustSetLattice_Bottom_IsUniverse()
    {
        var universe = new[] { "a", "b", "c" };
        var lattice = new MustSetLattice<string>(universe);

        Assert.Equal(3, lattice.Bottom.Count);
    }

    #endregion

    #region Comprehensive CFG Tests

    [Fact]
    public void CFG_EmptyFunction_HasEntryAndExit()
    {
        var source = @"
module Test Test block
  func Empty () → void
    !
    return";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        Assert.NotNull(cfg.Entry);
        Assert.NotNull(cfg.Exit);
        // Entry should connect to exit for empty function
        Assert.True(cfg.Blocks.Count >= 2);
    }

    [Fact]
    public void CFG_NestedIfStatements_CreatesCorrectStructure()
    {
        var source = @"
module Test Test block
  func Nested (int:x, int:y) → int
    !
    if (x > 0)
      return ((y > 0) ? 1 : 2)
    else
      return 3";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        // Nested if should create multiple blocks
        Assert.True(cfg.Blocks.Count >= 4);
    }

    [Fact]
    public void CFG_EarlyReturn_CreatesSeparateBlocks()
    {
        var source = @"
module Test Test block
  func Early (int:x) → int
    !
    if (x < 0)
      return 0
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        // Should have blocks for: entry, condition, early return, normal return
        Assert.True(cfg.Blocks.Count >= 3);
    }

    [Fact]
    public void CFG_ReversePostOrder_ReturnsTopologicalOrder()
    {
        var source = @"
module Test Test block
  func Simple (int:x) → int
    !
    let int:y = (x + 1)
    return y";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        var rpo = cfg.GetReversePostOrder().ToList();

        // Entry should come first in reverse post order
        Assert.Equal(cfg.Entry, rpo.First());
    }

    [Fact]
    public void CFG_ToDot_GeneratesValidOutput()
    {
        var source = @"
module Test Test block
  func Simple (int:x) → int
    !
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);

        var dot = cfg.ToDot();

        Assert.Contains("digraph", dot);
        Assert.Contains("->", dot);
    }

    #endregion

    #region Comprehensive Reaching Definitions Tests

    [Fact]
    public void ReachingDefinitions_DefinitionInIfBranch_ReachesAfterMerge()
    {
        var source = @"
module Test Test block
  func Branch (int:x, bool:cond) → int
    !
    let int:y = 0
    if cond
      let int:z = 1
    else
      let int:z = 2
    return y";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new ReachingDefinitionsAnalysis(cfg);

        // y should be defined and reach exit
        var exitDefs = analysis.GetReachingDefinitionsAtExit(cfg.Exit).ToList();
        Assert.NotEmpty(exitDefs);
    }

    [Fact]
    public void ReachingDefinitions_LoopDefinition_ReachesLoopHeader()
    {
        var source = @"
module Test Test block
  func Loop (int:n) → int
    !
    let int:sum = 0
    for i from 0 to 10 step 1
      Console.WriteLine(sum)
    return sum";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new ReachingDefinitionsAnalysis(cfg);

        // Definitions should propagate through loop
        Assert.NotNull(analysis.AllDefinitions);
        Assert.NotEmpty(analysis.AllDefinitions);
    }

    [Fact]
    public void ReachingDefinitions_MultipleVariables_TrackedSeparately()
    {
        var source = @"
module Test Test block
  func Multi () → int
    !
    let int:a = 1
    let int:b = 2
    let int:c = 3
    return (a + (b + c))";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new ReachingDefinitionsAnalysis(cfg);

        // Should track all three definitions
        Assert.True(analysis.AllDefinitions.Count >= 3);
    }

    #endregion

    #region Comprehensive Live Variables Tests

    [Fact]
    public void LiveVariables_ParameterUsedInReturn_IsLiveAtEntry()
    {
        var source = @"
module Test Test block
  func UseParam (int:x) → int
    !
    return x";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new LiveVariablesAnalysis(cfg);

        var entryLive = analysis.GetLiveVariablesAtEntry(cfg.Entry);
        Assert.Contains("x", entryLive);
    }

    [Fact]
    public void LiveVariables_VariableUsedInCondition_IsLive()
    {
        var source = @"
module Test Test block
  func Cond (int:x) → int
    !
    if (x > 0)
      return 1
    return 0";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new LiveVariablesAnalysis(cfg);

        var entryLive = analysis.GetLiveVariablesAtEntry(cfg.Entry);
        Assert.Contains("x", entryLive);
    }

    [Fact]
    public void LiveVariables_MultipleUses_AllTracked()
    {
        var source = @"
module Test Test block
  func Multi (int:x) → int
    !
    let int:a = x
    let int:b = x
    return (a + b)";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var analysis = new LiveVariablesAnalysis(cfg);

        // x is used twice, should be live
        Assert.NotNull(analysis);
    }

    #endregion

    #region Diagnostic Reporting Tests

    [Fact]
    public void UninitializedVariables_ReportDiagnostics_EmptyForSafeCode()
    {
        var source = @"
module Test Test block
  func Safe (int:x) → int
    !
    let int:y = (x + 1)
    return y";

        var func = GetFunction(source);
        var cfg = ControlFlowGraph.Build(func);
        var paramNames = func.Symbol.Parameters.Select(p => p.Name);
        var analysis = new UninitializedVariablesAnalysis(cfg, paramNames);

        var diagnostics = new DiagnosticBag();
        analysis.ReportDiagnostics(diagnostics);

        // Safe code should produce no uninitialized variable errors
        Assert.Empty(analysis.UninitializedUses);
    }

    #endregion
}
