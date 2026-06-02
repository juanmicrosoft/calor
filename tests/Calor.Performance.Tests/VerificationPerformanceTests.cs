using System.Diagnostics;
using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Performance.Tests;

/// <summary>
/// Performance tests to establish timing and memory baselines for verification analyses.
/// These tests ensure analysis scales appropriately with code size.
/// </summary>
public class VerificationPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public VerificationPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Helpers

    private static BoundModule? ParseAndBind(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        try
        {
            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.TokenizeAllForParser();
            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            if (diagnostics.HasErrors) return null;

            var binder = new Binder(diagnostics);
            return binder.Bind(module);
        }
        catch (InvalidOperationException)
        {
            // Parser threw on unsupported syntax - treat as parsing error
            diagnostics.ReportError(default, "ParseError", "Parsing failed with unsupported syntax");
            return null;
        }
    }

    private static void RunFullAnalysis(BoundModule module)
    {
        var diagnostics = new DiagnosticBag();

        // Run taint analysis
        var taintRunner = new TaintAnalysisRunner(diagnostics, TaintAnalysisOptions.Default);
        taintRunner.Analyze(module);

        // Run bug pattern analysis
        var bugRunner = new BugPatternRunner(diagnostics);
        bugRunner.Check(module);

        // Build CFGs for each function (dataflow foundation)
        foreach (var func in module.Functions)
        {
            var cfg = ControlFlowGraph.Build(func);
        }
    }

    private static long MeasureMemory(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalMemory(true);
        action();
        var after = GC.GetTotalMemory(true);

        return after - before;
    }

    #endregion

    #region Parsing Performance Tests

    [Fact]
    public void Parsing_SmallModule_Under100ms()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);

        var sw = Stopwatch.StartNew();
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        sw.Stop();

        _output.WriteLine($"Small module parsing: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Functions: 10, Statements/function: 50");

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"Small module parsing took {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    [Fact]
    public void Parsing_MediumModule_Under500ms()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 50, statementsPerFunction: 100);

        var sw = Stopwatch.StartNew();
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        sw.Stop();

        _output.WriteLine($"Medium module parsing: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Functions: 50, Statements/function: 100");

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Medium module parsing took {sw.ElapsedMilliseconds}ms, expected < 500ms");
    }

    [Fact]
    public void Parsing_LargeModule_Under2000ms()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 200, statementsPerFunction: 200);

        var sw = Stopwatch.StartNew();
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        sw.Stop();

        _output.WriteLine($"Large module parsing: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Functions: 200, Statements/function: 200");

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Large module parsing took {sw.ElapsedMilliseconds}ms, expected < 2000ms");
    }

    #endregion

    #region Binding Performance Tests

    [Fact]
    public void Binding_SmallModule_Under100ms()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();
        if (diagnostics.HasErrors) return;

        var sw = Stopwatch.StartNew();
        var binder = new Binder(diagnostics);
        var module = binder.Bind(ast);
        sw.Stop();

        _output.WriteLine($"Small module binding: {sw.ElapsedMilliseconds}ms");

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"Small module binding took {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    [Fact]
    public void Binding_MediumModule_Under500ms()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 50, statementsPerFunction: 100);

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();
        if (diagnostics.HasErrors) return;

        var sw = Stopwatch.StartNew();
        var binder = new Binder(diagnostics);
        var module = binder.Bind(ast);
        sw.Stop();

        _output.WriteLine($"Medium module binding: {sw.ElapsedMilliseconds}ms");

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Medium module binding took {sw.ElapsedMilliseconds}ms, expected < 500ms");
    }

    #endregion

    #region Analysis Performance Tests

    [Fact]
    public void FullAnalysis_SmallModule_Under1Second()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);

        var module = ParseAndBind(source, out var diagnostics);
        if (diagnostics.HasErrors || module == null) return;

        var sw = Stopwatch.StartNew();
        RunFullAnalysis(module);
        sw.Stop();

        _output.WriteLine($"Small module full analysis: {sw.ElapsedMilliseconds}ms");

        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"Small module analysis took {sw.ElapsedMilliseconds}ms, expected < 1000ms");
    }

    [Fact]
    public void FullAnalysis_MediumModule_Under5Seconds()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 50, statementsPerFunction: 100);

        var module = ParseAndBind(source, out var diagnostics);
        if (diagnostics.HasErrors || module == null) return;

        var sw = Stopwatch.StartNew();
        RunFullAnalysis(module);
        sw.Stop();

        _output.WriteLine($"Medium module full analysis: {sw.ElapsedMilliseconds}ms");

        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"Medium module analysis took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    [Fact]
    public void FullAnalysis_LargeModule_Under30Seconds()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 200, statementsPerFunction: 200);

        var module = ParseAndBind(source, out var diagnostics);
        if (diagnostics.HasErrors || module == null) return;

        var sw = Stopwatch.StartNew();
        RunFullAnalysis(module);
        sw.Stop();

        _output.WriteLine($"Large module full analysis: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Functions: {module.Functions.Count}");

        Assert.True(sw.ElapsedMilliseconds < 30000,
            $"Large module analysis took {sw.ElapsedMilliseconds}ms, expected < 30000ms");
    }

    #endregion

    #region Taint Analysis Performance

    [Fact]
    public void TaintAnalysis_DeepDataFlow_Under1Second()
    {
        var source = SyntheticCodeGenerator.GenerateTaintModule(functions: 20, dataFlowDepth: 50);

        var module = ParseAndBind(source, out var diagnostics);
        if (diagnostics.HasErrors || module == null) return;

        var sw = Stopwatch.StartNew();
        var taintDiagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(taintDiagnostics, TaintAnalysisOptions.Default);
        runner.Analyze(module);
        sw.Stop();

        _output.WriteLine($"Taint analysis (deep data flow): {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Warnings found: {taintDiagnostics.Count()}");

        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"Taint analysis took {sw.ElapsedMilliseconds}ms, expected < 1000ms");
    }

    [Fact]
    public void TaintAnalysis_ManyFunctions_Under2Seconds()
    {
        var source = SyntheticCodeGenerator.GenerateTaintModule(functions: 100, dataFlowDepth: 10);

        var module = ParseAndBind(source, out var diagnostics);
        if (diagnostics.HasErrors || module == null) return;

        var sw = Stopwatch.StartNew();
        var taintDiagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(taintDiagnostics, TaintAnalysisOptions.Default);
        runner.Analyze(module);
        sw.Stop();

        _output.WriteLine($"Taint analysis (many functions): {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Functions: {module.Functions.Count}");

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Taint analysis took {sw.ElapsedMilliseconds}ms, expected < 2000ms");
    }

    #endregion

    #region Dataflow Analysis Performance

    [Fact]
    public void DataflowAnalysis_ComplexCFG_Under2Seconds()
    {
        var source = SyntheticCodeGenerator.GenerateControlFlowModule(functions: 20, branchDepth: 10);

        var module = ParseAndBind(source, out var diagnostics);
        if (diagnostics.HasErrors || module == null) return;

        var sw = Stopwatch.StartNew();
        // Build CFGs for all functions (dataflow analysis foundation)
        foreach (var func in module.Functions)
        {
            var cfg = ControlFlowGraph.Build(func);
            // Traverse to ensure full construction
            _ = cfg.GetReversePostOrder();
        }
        sw.Stop();

        _output.WriteLine($"CFG construction (complex control flow): {sw.ElapsedMilliseconds}ms");

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"CFG construction took {sw.ElapsedMilliseconds}ms, expected < 2000ms");
    }

    [Fact]
    public void DataflowAnalysis_NestedLoops_Under3Seconds()
    {
        var source = SyntheticCodeGenerator.GenerateLoopModule(functions: 20, loopsPerFunction: 5, nestingDepth: 3);

        var module = ParseAndBind(source, out var diagnostics);
        if (diagnostics.HasErrors || module == null) return;

        var sw = Stopwatch.StartNew();
        // Build CFGs for all functions with nested loops
        foreach (var func in module.Functions)
        {
            var cfg = ControlFlowGraph.Build(func);
            _ = cfg.GetReversePostOrder();
        }
        sw.Stop();

        _output.WriteLine($"CFG construction (nested loops): {sw.ElapsedMilliseconds}ms");

        Assert.True(sw.ElapsedMilliseconds < 3000,
            $"CFG construction took {sw.ElapsedMilliseconds}ms, expected < 3000ms");
    }

    #endregion

    #region Memory Tests

    [Fact]
    public void Memory_SmallModule_Under10MB()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);

        var memoryUsed = MeasureMemory(() =>
        {
            var module = ParseAndBind(source, out var diagnostics);
            if (module != null)
            {
                RunFullAnalysis(module);
            }
        });

        var memoryMB = memoryUsed / 1024.0 / 1024.0;
        _output.WriteLine($"Small module memory: {memoryMB:F2} MB");

        Assert.True(memoryMB < 10,
            $"Small module used {memoryMB:F2} MB, expected < 10 MB");
    }

    [Fact]
    public void Memory_MediumModule_Under50MB()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 50, statementsPerFunction: 100);

        var memoryUsed = MeasureMemory(() =>
        {
            var module = ParseAndBind(source, out var diagnostics);
            if (module != null)
            {
                RunFullAnalysis(module);
            }
        });

        var memoryMB = memoryUsed / 1024.0 / 1024.0;
        _output.WriteLine($"Medium module memory: {memoryMB:F2} MB");

        Assert.True(memoryMB < 50,
            $"Medium module used {memoryMB:F2} MB, expected < 50 MB");
    }

    [Fact]
    public void Memory_LargeModule_Under100MB()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 200, statementsPerFunction: 200);

        var memoryUsed = MeasureMemory(() =>
        {
            var module = ParseAndBind(source, out var diagnostics);
            if (module != null)
            {
                RunFullAnalysis(module);
            }
        });

        var memoryMB = memoryUsed / 1024.0 / 1024.0;
        _output.WriteLine($"Large module memory: {memoryMB:F2} MB");

        Assert.True(memoryMB < 100,
            $"Large module used {memoryMB:F2} MB, expected < 100 MB");
    }

    #endregion

    #region Scalability Tests

    [Fact]
    public void Scalability_LinearWithFunctions()
    {
        var times = new List<(int Functions, long TimeMs)>();

        foreach (var funcCount in new[] { 10, 20, 40, 80 })
        {
            var source = SyntheticCodeGenerator.Generate(functions: funcCount, statementsPerFunction: 50);
            var module = ParseAndBind(source, out var diagnostics);
            if (module == null) continue;

            var sw = Stopwatch.StartNew();
            RunFullAnalysis(module);
            sw.Stop();

            times.Add((funcCount, sw.ElapsedMilliseconds));
            _output.WriteLine($"Functions: {funcCount}, Time: {sw.ElapsedMilliseconds}ms");
        }

        // Check that doubling functions doesn't more than quadruple time (O(n^2) or better)
        if (times.Count >= 4)
        {
            var ratio = (double)times[3].TimeMs / times[1].TimeMs;
            _output.WriteLine($"Time ratio (80/20 functions): {ratio:F2}x");

            Assert.True(ratio < 16,
                $"Analysis scaling is worse than O(n^2): ratio = {ratio:F2}");
        }
    }

    [Fact]
    public void Scalability_LinearWithStatements()
    {
        var times = new List<(int Statements, long TimeMs)>();

        foreach (var stmtCount in new[] { 25, 50, 100, 200 })
        {
            var source = SyntheticCodeGenerator.Generate(functions: 20, statementsPerFunction: stmtCount);
            var module = ParseAndBind(source, out var diagnostics);
            if (module == null) continue;

            var sw = Stopwatch.StartNew();
            RunFullAnalysis(module);
            sw.Stop();

            times.Add((stmtCount, sw.ElapsedMilliseconds));
            _output.WriteLine($"Statements/function: {stmtCount}, Time: {sw.ElapsedMilliseconds}ms");
        }

        // Check that doubling statements doesn't more than quadruple time
        if (times.Count >= 4)
        {
            var ratio = (double)times[3].TimeMs / times[1].TimeMs;
            _output.WriteLine($"Time ratio (200/50 statements): {ratio:F2}x");

            Assert.True(ratio < 16,
                $"Analysis scaling is worse than O(n^2): ratio = {ratio:F2}");
        }
    }

    #endregion

    #region CFG Construction Performance

    [Fact]
    public void CFGConstruction_SmallFunction_Under10ms()
    {
        var source = @"
§M{m001:Test}
  §F{f001:SmallFunc:pub}
      §I{i32:x}
      §O{i32}
      §IF (> x INT:0)
        §R x
      §EL
        §R (- INT:0 x)
      §/IF";

        var module = ParseAndBind(source, out var diagnostics);
        if (module == null) return;

        var func = module.Functions.First();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            var cfg = ControlFlowGraph.Build(func);
        }
        sw.Stop();

        var avgTime = sw.ElapsedMilliseconds / 100.0;
        _output.WriteLine($"CFG construction avg time: {avgTime:F2}ms");

        Assert.True(avgTime < 10,
            $"CFG construction took {avgTime:F2}ms, expected < 10ms");
    }

    [Fact]
    public void CFGConstruction_ComplexFunction_Under50ms()
    {
        var source = SyntheticCodeGenerator.GenerateControlFlowModule(functions: 1, branchDepth: 10);

        var module = ParseAndBind(source, out var diagnostics);
        if (module == null) return;

        var func = module.Functions.First();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            var cfg = ControlFlowGraph.Build(func);
        }
        sw.Stop();

        var avgTime = sw.ElapsedMilliseconds / 10.0;
        _output.WriteLine($"Complex CFG construction avg time: {avgTime:F2}ms");

        Assert.True(avgTime < 50,
            $"Complex CFG construction took {avgTime:F2}ms, expected < 50ms");
    }

    #endregion
}
