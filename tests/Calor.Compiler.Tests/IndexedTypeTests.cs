using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for indexed type (§ITYPE) parsing, emission, obligation generation,
/// and Z3-based bounds checking (Milestone 2).
/// </summary>
public sealed class IndexedTypeTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string ParseAndEmitCSharp(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors:\n{string.Join("\n", diagnostics.Select(d => d.Message))}");

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string ParseAndEmitCalor(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors:\n{string.Join("\n", diagnostics.Select(d => d.Message))}");

        var emitter = new CalorEmitter();
        return emitter.Emit(module);
    }

    // ───── Tokenization ─────

    [Fact]
    public void Tokenize_IndexedType_ReturnsCorrectTokenKind()
    {
        var tokens = Tokenize("§ITYPE", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.IndexedType, tokens[0].Kind);
    }

    [Fact]
    public void Tokenize_EndIndexedType_ReturnsCorrectTokenKind()
    {
        var tokens = Tokenize("§/ITYPE", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.EndIndexedType, tokens[0].Kind);
    }

    [Fact]
    public void Tokenize_IndexedType_IsKeyword()
    {
        var tokens = Tokenize("§ITYPE", out _);
        Assert.True(tokens[0].IsKeyword);
    }

    // ───── Parsing: Basic ─────

    [Fact]
    public void Parse_IndexedTypeWithoutConstraint_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:List:n}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Single(module.IndexedTypes);

        var itype = module.IndexedTypes[0];
        Assert.Equal("it1", itype.Id);
        Assert.Equal("SizedList", itype.Name);
        Assert.Equal("List", itype.BaseTypeName);
        Assert.Equal("n", itype.SizeParam);
        Assert.Null(itype.Constraint);
    }

    [Fact]
    public void Parse_IndexedTypeWithConstraint_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it2:NonEmptyArr:IntArr:n} (> # INT:0)
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Single(module.IndexedTypes);

        var itype = module.IndexedTypes[0];
        Assert.Equal("it2", itype.Id);
        Assert.Equal("NonEmptyArr", itype.Name);
        Assert.Equal("IntArr", itype.BaseTypeName);
        Assert.Equal("n", itype.SizeParam);
        Assert.NotNull(itype.Constraint);
        Assert.IsType<BinaryOperationNode>(itype.Constraint);
    }

    [Fact]
    public void Parse_MultipleIndexedTypes_ParsesAll()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:List:n}
            §ITYPE{it2:NonEmptyArr:IntArr:m} (> # INT:0)
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Equal(2, module.IndexedTypes.Count);
        Assert.Equal("SizedList", module.IndexedTypes[0].Name);
        Assert.Equal("NonEmptyArr", module.IndexedTypes[1].Name);
    }

    [Fact]
    public void Parse_IndexedTypeWithAndConstraint_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:BoundedArr:IntArr:n} (&& (> # INT:0) (< # INT:1000))
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Single(module.IndexedTypes);

        var itype = module.IndexedTypes[0];
        Assert.Equal("BoundedArr", itype.Name);
        Assert.NotNull(itype.Constraint);
    }

    // ───── C# Emission: Erasure ─────

    [Fact]
    public void CSharpEmit_IndexedTypeDefinition_ErasesToNothing()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:List:n}
            §F{f001:Main:pub}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var csharp = ParseAndEmitCSharp(source);

        Assert.DoesNotContain("ITYPE", csharp);
        Assert.DoesNotContain("SizedList", csharp);
        Assert.DoesNotContain("§", csharp);
        // Function should still be emitted
        Assert.Contains("Main", csharp);
    }

    [Fact]
    public void CSharpEmit_IndexedTypeWithConstraint_ErasesToNothing()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:NonEmptyArr:IntArr:n} (> # INT:0)
            §F{f001:Main:pub}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var csharp = ParseAndEmitCSharp(source);

        Assert.DoesNotContain("ITYPE", csharp);
        Assert.DoesNotContain("NonEmptyArr", csharp);
        Assert.DoesNotContain("§", csharp);
    }

    // ───── CalorEmitter: Round-trip ─────

    [Fact]
    public void CalorEmit_IndexedTypeWithoutConstraint_RoundTrips()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:List:n}
            §/M{m001}
            """;

        var calor = ParseAndEmitCalor(source);

        Assert.Contains("§ITYPE{it1:SizedList:List:n}", calor);
    }

    [Fact]
    public void CalorEmit_IndexedTypeWithConstraint_RoundTrips()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it2:NonEmptyArr:IntArr:n} (> # INT:0)
            §/M{m001}
            """;

        var calor = ParseAndEmitCalor(source);

        Assert.Contains("§ITYPE{it2:NonEmptyArr:IntArr:n}", calor);
        Assert.Contains("#", calor);
    }

    // ───── IdScanner ─────

    [Fact]
    public void IdScanner_IndexedType_CollectsId()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:List:n}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var scanner = new Ids.IdScanner();
        var entries = scanner.Scan(module, "test.calr");

        Assert.Contains(entries, e => e.Id == "it1" && e.Kind == Ids.IdKind.IndexedType);
    }

    // ───── Feature Support ─────

    [Fact]
    public void FeatureSupport_IndexedType_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("indexed-type"));
    }

    [Fact]
    public void FeatureSupport_IndexBounds_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("index-bounds"));
    }

    // ───── Obligation Generation: IndexBounds ─────

    [Fact]
    public void Generate_ArrayAccessOnIndexedType_CreatesIndexBoundsObligation()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:Sum:priv}
              §I{SizedList:items}
              §I{i32:i}
              §O{i32}
              §R §IDX items i
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Contains(tracker.Obligations, o => o.Kind == ObligationKind.IndexBounds);
        var indexObl = tracker.Obligations.First(o => o.Kind == ObligationKind.IndexBounds);
        Assert.Equal("f001", indexObl.FunctionId);
        Assert.Contains("items", indexObl.Description);
    }

    [Fact]
    public void Generate_ArrayAccessOnNonIndexedType_NoIndexBoundsObligation()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Sum:priv}
              §I{[i32]:items}
              §I{i32:i}
              §O{i32}
              §R §IDX items i
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.DoesNotContain(tracker.Obligations, o => o.Kind == ObligationKind.IndexBounds);
    }

    [Fact]
    public void Generate_PublicFunctionIndexBounds_SetsBoundaryStatus()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:Sum:pub}
              §I{SizedList:items}
              §I{i32:i}
              §O{i32}
              §R §IDX items i
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        var indexObl = tracker.Obligations.FirstOrDefault(o => o.Kind == ObligationKind.IndexBounds);
        Assert.NotNull(indexObl);
        Assert.Equal(ObligationStatus.Boundary, indexObl.Status);
    }

    // ───── Z3 Solving: Index Bounds ─────

    [SkippableFact]
    public void Solve_BoundedAccess_Discharged()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Inline refinement constrains i to [0, n).
        // The IndexBounds obligation checks (>= i 0) && (< i n), which matches.
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:Sum:priv}
              §I{SizedList:items}
              §I{i32:n}
              §I{i32:i} | (&& (>= # INT:0) (< # n))
              §O{i32}
              §R §IDX items i
            §/F{f001}
            §/M{m001}
            """;

        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            EnableTypeChecking = false,
            EnforceEffects = false
        };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        var indexObl = options.ObligationResults.Obligations
            .FirstOrDefault(o => o.Kind == ObligationKind.IndexBounds);
        Assert.NotNull(indexObl);
        Assert.Equal(ObligationStatus.Discharged, indexObl.Status);
    }

    [SkippableFact]
    public void Solve_UnboundedAccess_FailsWithCounterexample()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // No precondition on i, so the access can't be proven safe
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:BadAccess:priv}
              §I{SizedList:items}
              §I{i32:n}
              §I{i32:i}
              §O{i32}
              §R §IDX items i
            §/F{f001}
            §/M{m001}
            """;

        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            EnableTypeChecking = false,
            EnforceEffects = false
        };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        var indexObl = options.ObligationResults.Obligations
            .FirstOrDefault(o => o.Kind == ObligationKind.IndexBounds);
        Assert.NotNull(indexObl);
        Assert.Equal(ObligationStatus.Failed, indexObl.Status);
        Assert.NotNull(indexObl.CounterexampleDescription);
        Assert.Contains("Counterexample", indexObl.CounterexampleDescription);
    }

    // ───── FactCollector ─────

    [Fact]
    public void FactCollector_ForLoop_ExtractsLoopBounds()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:Sum:priv}
              §I{SizedList:items}
              §O{i32}
              §L{l1:i:INT:0:n:INT:1}
                §R §IDX items i
              §/L{l1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        var collector = new FactCollector();
        collector.CollectFromFunction(func);

        // Should have 2 facts: i >= 0 and i < n
        Assert.Equal(2, collector.Facts.Count);
        Assert.All(collector.Facts, f => Assert.IsType<BinaryOperationNode>(f));
    }

    // ───── MCP Tool ─────

    [Fact]
    public async Task BoundsCheckTool_BasicSource_ReturnsResult()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:Sum:priv}
              §I{SizedList:items}
              §I{i32:i}
              §O{i32}
              §R §IDX items i
            §/F{f001}
            §/M{m001}
            """;

        var tool = new Calor.Compiler.Mcp.Tools.RefineTool();

        var argsJson = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new { action = "bounds", source }));

        var result = await tool.ExecuteAsync(argsJson.RootElement);
        Assert.False(result.IsError, "RefineTool bounds action returned error");
    }

    // ───── Mixed: Indexed + Refinement Types ─────

    [Fact]
    public void Parse_IndexedAndRefinementTypes_ParseBoth()
    {
        var source = """
            §M{m001:Test}
            §RTYPE{r1:NatInt:i32} (>= # INT:0)
            §ITYPE{it1:SizedList:List:n}
            §F{f001:Main:pub}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Single(module.RefinementTypes);
        Assert.Single(module.IndexedTypes);
    }

    // ───── Gap Fix: Trailing [] in ITYPE base type ─────

    [Fact]
    public void Parse_IndexedTypeWithArrayBrackets_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedArr:i32[]:n}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Single(module.IndexedTypes);

        var itype = module.IndexedTypes[0];
        Assert.Equal("i32[]", itype.BaseTypeName);
        Assert.Equal("n", itype.SizeParam);
    }

    [Fact]
    public void Parse_IndexedTypeWithArrayBracketsAndConstraint_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:NonEmptyArr:i32[]:n} (> # INT:0)
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var itype = module.IndexedTypes[0];
        Assert.Equal("i32[]", itype.BaseTypeName);
        Assert.NotNull(itype.Constraint);
    }

    // ───── Gap Fix: Type erasure mapping ─────

    [Fact]
    public void CSharpEmit_IndexedTypeParameter_ErasesToBaseType()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:List:n}
            §F{f001:Sum:pub}
              §I{SizedList:items}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var csharp = ParseAndEmitCSharp(source);

        // The parameter type SizedList should be erased to List
        Assert.DoesNotContain("SizedList", csharp);
        Assert.Contains("List", csharp);
    }

    [Fact]
    public void CSharpEmit_GenericIndexedTypeParameter_ErasesWithGenericArgs()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:List:n}
            §F{f001:Sum:pub}
              §I{SizedList<i32>:items}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var csharp = ParseAndEmitCSharp(source);

        // SizedList<i32> should erase to List<int>
        Assert.DoesNotContain("SizedList", csharp);
        Assert.Contains("List<int>", csharp);
    }

    // ───── Gap Fix: ContractVerifier accepts size params ─────

    [Fact]
    public void ContractVerifier_PreconditionWithSizeParam_NoError()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:Sum:priv}
              §I{SizedList:items}
              §I{i32:i}
              §O{i32}
              §Q (< i n)
              §R §IDX items i
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Parse errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var verifier = new Verification.ContractVerifier(diagnostics);
        verifier.Verify(module);

        // n is a size param from §ITYPE, should be accepted
        Assert.False(diagnostics.HasErrors,
            $"Contract errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    // ───── Gap Fix: Deep expression walker ─────

    [Fact]
    public void Generate_ArrayAccessInsideCallArg_CreatesObligation()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:Sum:priv}
              §I{SizedList:items}
              §I{i32:i}
              §O{void}
              §C{Console.WriteLine}
                §A §IDX items i
              §/C
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Contains(tracker.Obligations, o => o.Kind == ObligationKind.IndexBounds);
    }

    [Fact]
    public void Generate_ArrayAccessInsideWhileLoop_CreatesObligation()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:Loop:priv}
              §I{SizedList:items}
              §I{i32:i}
              §O{void}
              §WH{w1} (< i n)
                §B{x:i32} §IDX items i
              §/WH{w1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Contains(tracker.Obligations, o => o.Kind == ObligationKind.IndexBounds);
    }

    // ───── Gap Fix: FactCollector if-guard extraction ─────

    [Fact]
    public void FactCollector_IfGuard_ExtractsCondition()
    {
        var source = """
            §M{m001:Test}
            §ITYPE{it1:SizedList:IntArr:n}
            §F{f001:SafeGet:priv}
              §I{SizedList:items}
              §I{i32:i}
              §O{i32}
              §IF{if1} (< i n)
                §R §IDX items i
              §/I{if1}
              §R INT:0
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        var collector = new FactCollector();
        collector.CollectFromFunction(func);

        // Should have the if-condition as a fact: (< i n)
        Assert.True(collector.Facts.Count >= 1);
        Assert.Contains(collector.Facts, f => f is BinaryOperationNode);
    }

    // ───── Gap Fix: Expanded MCP tool tests ─────

    [Fact]
    public async Task BoundsCheckTool_NoIndexedTypes_ReturnsEmptyAccessSites()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var tool = new Calor.Compiler.Mcp.Tools.RefineTool();
        var argsJson = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new { action = "bounds", source }));

        var result = await tool.ExecuteAsync(argsJson.RootElement);
        Assert.False(result.IsError);

        // Result should indicate safe (no access sites)
        var json = result.Content[0].Text;
        Assert.Contains("\"safe\":true", json);
        Assert.Contains("\"total_access_sites\":0", json);
    }

    [Fact]
    public async Task BoundsCheckTool_WithFunctionIdFilter_AcceptsParameter()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var tool = new Calor.Compiler.Mcp.Tools.RefineTool();
        var argsJson = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new { action = "bounds", source, function_id = "f001" }));

        var result = await tool.ExecuteAsync(argsJson.RootElement);
        Assert.False(result.IsError);

        // With no indexed types, should still succeed and report safe
        var json = result.Content[0].Text;
        Assert.Contains("\"safe\":true", json);
    }

    [Fact]
    public async Task BoundsCheckTool_MissingSource_ReturnsError()
    {
        var tool = new Calor.Compiler.Mcp.Tools.RefineTool();
        var argsJson = System.Text.Json.JsonDocument.Parse("""{"action":"bounds"}""");

        var result = await tool.ExecuteAsync(argsJson.RootElement);
        Assert.True(result.IsError);
    }
}
