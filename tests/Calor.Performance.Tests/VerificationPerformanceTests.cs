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
    private static readonly Lazy<bool> Warmed = new(() =>
    {
        foreach (var source in new[]
        {
            SyntheticCodeGenerator.Generate(functions: 2, statementsPerFunction: 50),
            SyntheticCodeGenerator.GenerateTaintModule(functions: 3, dataFlowDepth: 10),
            SyntheticCodeGenerator.GenerateControlFlowModule(functions: 1, branchDepth: 3),
            SyntheticCodeGenerator.GenerateLoopModule(functions: 1, loopsPerFunction: 2, nestingDepth: 3),
        })
            RunFullAnalysis(RequireParsedAndBound(source));
        return true;
    });

    public VerificationPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _ = Warmed.Value;
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

    private static BoundModule RequireParsedAndBound(string source)
    {
        var module = ParseAndBind(source, out var diagnostics);
        // Nullability diagnostics (Calor0272/0273/0274) promoted to Error under
        // SemanticsVersion.Major >= 2 (v0.14 §S5) are out-of-scope for these
        // performance tests — they exercise taint/verifier/CFG timings against
        // synthetically-generated modules that intentionally use ':string'
        // targets. Filter them here so the perf-timing assertions are not
        // silently blocked by a nullability finding on the fixture.
        var nonNullability = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && d.Code != DiagnosticCode.NullableToNonNullableBinding
                && d.Code != DiagnosticCode.NullableReturnFromNonNullable
                && d.Code != DiagnosticCode.NullableArgumentToNonNullableParameter)
            .ToArray();
        Assert.True(nonNullability.Length == 0, string.Join("\n", nonNullability.Select(d => d.Message)));
        return Assert.IsType<BoundModule>(module);
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

    private static long MeasureMemory(Func<BoundModule> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalMemory(true);
        var module = action();
        var after = GC.GetTotalMemory(true);
        GC.KeepAlive(module);

        return after - before;
    }

    #endregion

    #region Parsing Performance Tests

    [Fact]
    public void Parsing_SmallModule_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);

        var sw = Stopwatch.StartNew();
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        sw.Stop();

        _output.WriteLine($"Small module parsing: {sw.Elapsed.TotalMilliseconds}ms");
        _output.WriteLine($"Functions: 10, Statements/function: 50");

        Assert.True(sw.Elapsed.TotalMilliseconds < 25,
            $"Small module parsing took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 25ms");
    }

    [Fact]
    public void Parsing_MediumModule_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 50, statementsPerFunction: 100);

        var sw = Stopwatch.StartNew();
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        sw.Stop();

        _output.WriteLine($"Medium module parsing: {sw.Elapsed.TotalMilliseconds}ms");
        _output.WriteLine($"Functions: 50, Statements/function: 100");

        Assert.True(sw.Elapsed.TotalMilliseconds < 225,
            $"Medium module parsing took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 225ms");
    }

    [Fact]
    public void Parsing_LargeModule_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 200, statementsPerFunction: 200);

        var sw = Stopwatch.StartNew();
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        sw.Stop();

        _output.WriteLine($"Large module parsing: {sw.Elapsed.TotalMilliseconds}ms");
        _output.WriteLine($"Functions: 200, Statements/function: 200");

        Assert.True(sw.Elapsed.TotalMilliseconds < 2000,
            $"Large module parsing took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 2000ms");
    }

    #endregion

    #region Binding Performance Tests

    [Fact]
    public void Binding_SmallModule_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var sw = Stopwatch.StartNew();
        var binder = new Binder(diagnostics);
        var module = binder.Bind(ast);
        sw.Stop();

        _output.WriteLine($"Small module binding: {sw.Elapsed.TotalMilliseconds}ms");

        Assert.True(sw.Elapsed.TotalMilliseconds < 20,
            $"Small module binding took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 20ms");
    }

    [Fact]
    public void Binding_MediumModule_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 50, statementsPerFunction: 100);

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var sw = Stopwatch.StartNew();
        var binder = new Binder(diagnostics);
        var module = binder.Bind(ast);
        sw.Stop();

        _output.WriteLine($"Medium module binding: {sw.Elapsed.TotalMilliseconds}ms");

        Assert.True(sw.Elapsed.TotalMilliseconds < 400,
            $"Medium module binding took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 400ms");
    }

    private const int StatementsPerFunction = 100;
    private const int SmallFunctionCount = 20;
    private const int LargeFunctionCount = 160;

    /// <summary>
    /// Binding cost must grow linearly with the number of functions. The 0.22
    /// regression (loop discovery copying the module-wide symbol table at every
    /// loop, and callable-state snapshots at every loop, branch, and lambda
    /// copying an entry for every local in the module) made it quadratic: 8x the
    /// functions cost about 64x the time. The ratio is far less load-sensitive
    /// than an absolute threshold; linear scaling gives about 8x.
    /// </summary>
    [Fact]
    public void Binding_ScalesLinearlyWithFunctionCount()
    {
        // Sized to keep this test cheap inside the timed suite (#1536): the module is
        // parsed once per size and only binding is repeated.
        static double BestBindMs(int functions)
        {
            var source = SyntheticCodeGenerator.Generate(functions: functions, statementsPerFunction: StatementsPerFunction);
            var parseDiagnostics = new DiagnosticBag();
            var tokens = new Lexer(source, parseDiagnostics).TokenizeAllForParser();
            var ast = new Parser(tokens, parseDiagnostics).Parse();
            Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));
            var best = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                var sw = Stopwatch.StartNew();
                new Binder(new DiagnosticBag()).Bind(ast);
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        BestBindMs(10); // JIT warmup
        var small = BestBindMs(SmallFunctionCount);
        var large = BestBindMs(LargeFunctionCount);
        var ratio = large / Math.Max(small, 1.0);
        _output.WriteLine($"Binding {SmallFunctionCount} functions: {small:F1}ms, {LargeFunctionCount} functions: {large:F1}ms, ratio {ratio:F2}x");

        Assert.True(ratio < 24,
            $"Binding scaling is superlinear: 8x the functions took {ratio:F2}x the time (linear is ~8x, quadratic ~64x)");
    }

    #endregion

    #region Analysis Performance Tests

    [Fact]
    public void FullAnalysis_SmallModule_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);

        var module = RequireParsedAndBound(source);

        var sw = Stopwatch.StartNew();
        RunFullAnalysis(module);
        sw.Stop();

        _output.WriteLine($"Small module full analysis: {sw.Elapsed.TotalMilliseconds}ms");

        Assert.True(sw.Elapsed.TotalMilliseconds < 150,
            $"Small module analysis took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 150ms");
    }

    [Fact]
    public void FullAnalysis_MediumModule_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 50, statementsPerFunction: 100);

        var module = RequireParsedAndBound(source);

        var sw = Stopwatch.StartNew();
        RunFullAnalysis(module);
        sw.Stop();

        _output.WriteLine($"Medium module full analysis: {sw.Elapsed.TotalMilliseconds}ms");

        Assert.True(sw.Elapsed.TotalMilliseconds < 2000,
            $"Medium module analysis took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 2000ms");
    }

    [Fact]
    public void FullAnalysis_LargeModule_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 200, statementsPerFunction: 200);

        var module = RequireParsedAndBound(source);

        var sw = Stopwatch.StartNew();
        RunFullAnalysis(module);
        sw.Stop();

        _output.WriteLine($"Large module full analysis: {sw.Elapsed.TotalMilliseconds}ms");
        _output.WriteLine($"Functions: {module.Functions.Count}");

        Assert.True(sw.Elapsed.TotalMilliseconds < 23000,
            $"Large module analysis took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 23000ms");
    }

    #endregion

    #region Taint Analysis Performance

    [Fact]
    public void TaintAnalysis_DeepDataFlow_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.GenerateTaintModule(functions: 20, dataFlowDepth: 50);

        var module = RequireParsedAndBound(source);

        var sw = Stopwatch.StartNew();
        var taintDiagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(taintDiagnostics, TaintAnalysisOptions.Default);
        runner.Analyze(module);
        sw.Stop();

        _output.WriteLine($"Taint analysis (deep data flow): {sw.Elapsed.TotalMilliseconds}ms");
        _output.WriteLine($"Warnings found: {taintDiagnostics.Count()}");

        Assert.True(sw.Elapsed.TotalMilliseconds < 600,
            $"Taint analysis took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 600ms");
    }

    [Fact]
    public void TaintAnalysis_ManyFunctions_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.GenerateTaintModule(functions: 100, dataFlowDepth: 10);

        var module = RequireParsedAndBound(source);

        var sw = Stopwatch.StartNew();
        var taintDiagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(taintDiagnostics, TaintAnalysisOptions.Default);
        runner.Analyze(module);
        sw.Stop();

        _output.WriteLine($"Taint analysis (many functions): {sw.Elapsed.TotalMilliseconds}ms");
        _output.WriteLine($"Functions: {module.Functions.Count}");

        Assert.True(sw.Elapsed.TotalMilliseconds < 300,
            $"Taint analysis took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 300ms");
    }

    #endregion

    #region Dataflow Analysis Performance

    [Fact]
    public void DataflowAnalysis_ComplexCFG_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.GenerateControlFlowModule(functions: 20, branchDepth: 10);

        var module = RequireParsedAndBound(source);

        var sw = Stopwatch.StartNew();
        // Build CFGs for all functions (dataflow analysis foundation)
        foreach (var func in module.Functions)
        {
            var cfg = ControlFlowGraph.Build(func);
            // Traverse to ensure full construction
            _ = cfg.GetReversePostOrder();
        }
        sw.Stop();

        _output.WriteLine($"CFG construction (complex control flow): {sw.Elapsed.TotalMilliseconds}ms");

        Assert.True(sw.Elapsed.TotalMilliseconds < 25,
            $"CFG construction took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 25ms");
    }

    [Fact]
    public void DataflowAnalysis_NestedLoops_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.GenerateLoopModule(functions: 20, loopsPerFunction: 5, nestingDepth: 3);

        var module = RequireParsedAndBound(source);

        var sw = Stopwatch.StartNew();
        // Build CFGs for all functions with nested loops
        foreach (var func in module.Functions)
        {
            var cfg = ControlFlowGraph.Build(func);
            _ = cfg.GetReversePostOrder();
        }
        sw.Stop();

        _output.WriteLine($"CFG construction (nested loops): {sw.Elapsed.TotalMilliseconds}ms");

        Assert.True(sw.Elapsed.TotalMilliseconds < 25,
            $"CFG construction took {sw.Elapsed.TotalMilliseconds}ms, smoke budget < 25ms");
    }

    #endregion

    #region Memory Tests

    [Fact]
    public void Memory_SmallModule_Under10MB()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);

        var memoryUsed = MeasureMemory(() =>
        {
            var module = RequireParsedAndBound(source);
            RunFullAnalysis(module);
            return module;
        });

        var memoryMB = memoryUsed / 1024.0 / 1024.0;
        _output.WriteLine($"Small module memory: {memoryMB:F2} MB");

        Assert.True(memoryMB > 0 && memoryMB < 10,
            $"Small module used {memoryMB:F2} MB, expected < 10 MB");
    }

    [Fact]
    public void Memory_MediumModule_Under50MB()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 50, statementsPerFunction: 100);

        var memoryUsed = MeasureMemory(() =>
        {
            var module = RequireParsedAndBound(source);
            RunFullAnalysis(module);
            return module;
        });

        var memoryMB = memoryUsed / 1024.0 / 1024.0;
        _output.WriteLine($"Medium module memory: {memoryMB:F2} MB");

        Assert.True(memoryMB > 0 && memoryMB < 50,
            $"Medium module used {memoryMB:F2} MB, expected < 50 MB");
    }

    [Fact]
    public void Memory_LargeModule_Under100MB()
    {
        var source = SyntheticCodeGenerator.Generate(functions: 200, statementsPerFunction: 200);

        var memoryUsed = MeasureMemory(() =>
        {
            var module = RequireParsedAndBound(source);
            RunFullAnalysis(module);
            return module;
        });

        var memoryMB = memoryUsed / 1024.0 / 1024.0;
        _output.WriteLine($"Large module memory: {memoryMB:F2} MB");

        Assert.True(memoryMB > 0 && memoryMB < 100,
            $"Large module used {memoryMB:F2} MB, expected < 100 MB");
    }

    #endregion

    #region Scalability Tests

    [Fact]
    public void Scalability_FunctionGrowthBelowQuadraticSmokeBudget()
    {
        var times = new List<(int Functions, double TimeMs)>();

        foreach (var funcCount in new[] { 10, 20, 40, 80 })
        {
            var source = SyntheticCodeGenerator.Generate(functions: funcCount, statementsPerFunction: 50);
            var module = RequireParsedAndBound(source);

            var sw = Stopwatch.StartNew();
            RunFullAnalysis(module);
            sw.Stop();

            times.Add((funcCount, sw.Elapsed.TotalMilliseconds));
            _output.WriteLine($"Functions: {funcCount}, Time: {sw.Elapsed.TotalMilliseconds}ms");
        }

        Assert.Equal(4, times.Count);
        var ratio = (double)times[3].TimeMs / times[1].TimeMs;
        _output.WriteLine($"Time ratio (80/20 functions): {ratio:F2}x");

        Assert.True(ratio < 16,
            $"Analysis scaling is worse than O(n^2): ratio = {ratio:F2}");
    }

    [Fact]
    public void Scalability_StatementGrowthBelowQuadraticSmokeBudget()
    {
        var times = new List<(int Statements, double TimeMs)>();

        foreach (var stmtCount in new[] { 25, 50, 100, 200 })
        {
            var source = SyntheticCodeGenerator.Generate(functions: 20, statementsPerFunction: stmtCount);
            var module = RequireParsedAndBound(source);

            var sw = Stopwatch.StartNew();
            RunFullAnalysis(module);
            sw.Stop();

            times.Add((stmtCount, sw.Elapsed.TotalMilliseconds));
            _output.WriteLine($"Statements/function: {stmtCount}, Time: {sw.Elapsed.TotalMilliseconds}ms");
        }

        Assert.Equal(4, times.Count);
        var ratio = (double)times[3].TimeMs / times[1].TimeMs;
        _output.WriteLine($"Time ratio (200/50 statements): {ratio:F2}x");

        Assert.True(ratio < 16,
            $"Analysis scaling is worse than O(n^2): ratio = {ratio:F2}");
    }

    #endregion

    #region CFG Construction Performance

    [Fact]
    public void CFGConstruction_SmallFunction_IsTimingSmokeTest()
    {
        var source = @"
§M{m001:Test}
  §F{f001:SmallFunc:pub}
    §I{i32:x}
    §O{i32}
    §IF{if1} (> x INT:0)
      §R x
    §EL
      §R (- INT:0 x)";

        var module = RequireParsedAndBound(source);

        var func = module.Functions.First();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            var cfg = ControlFlowGraph.Build(func);
        }
        sw.Stop();

        var avgTime = sw.Elapsed.TotalMilliseconds / 100.0;
        _output.WriteLine($"CFG construction avg time: {avgTime:F2}ms");

        Assert.True(avgTime < 0.1,
            $"CFG construction took {avgTime:F2}ms, smoke budget < 0.1ms");
    }

    [Fact]
    public void CFGConstruction_ComplexFunction_IsTimingSmokeTest()
    {
        var source = SyntheticCodeGenerator.GenerateControlFlowModule(functions: 1, branchDepth: 10);

        var module = RequireParsedAndBound(source);

        var func = module.Functions.First();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            var cfg = ControlFlowGraph.Build(func);
        }
        sw.Stop();

        var avgTime = sw.Elapsed.TotalMilliseconds / 10.0;
        _output.WriteLine($"Complex CFG construction avg time: {avgTime:F2}ms");

        Assert.True(avgTime < 1,
            $"Complex CFG construction took {avgTime:F2}ms, smoke budget < 1ms");
    }

    #endregion
}
