using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Performance.Tests;

public sealed class DeterministicPerformanceTests(ITestOutputHelper output)
{
    private sealed record Counters(int Tokens, int BoundNodes, int Blocks, int Edges, int TaintIterations);

    private static Counters Measure(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var ast = new Parser(tokens, diagnostics).Parse();
        var module = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var graphs = module.Functions.Select(ControlFlowGraph.Build).ToArray();
        var taint = new TaintAnalysisRunner(new DiagnosticBag());
        taint.Analyze(module);
        return new Counters(
            tokens.Count,
            BoundNodeHelpers.DescendantsAndSelf(module).Count(),
            graphs.Sum(graph => graph.Blocks.Count),
            graphs.Sum(graph => graph.Blocks.Sum(block => block.Successors.Count)),
            taint.Iterations);
    }

    [Fact]
    public void PipelineWorkAndWarmAllocationsMatchTheRegisteredBaseline()
    {
        Assert.True(AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out var tiered));
        Assert.False(tiered);
        var source = SyntheticCodeGenerator.Generate(functions: 10, statementsPerFunction: 50);
        for (var warmup = 0; warmup < 5; warmup++)
            Measure(source);

#if DEBUG
        const long baselineBytes = 11_681_288;
#else
        const long baselineBytes = 11_266_312;
#endif
        for (var run = 0; run < 5; run++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var actual = Measure(source);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"{actual}; allocated bytes: {allocated}");
            Assert.Equal(new Counters(4729, 1351, 580, 760, 580), actual);
            Assert.InRange(allocated, baselineBytes * 98 / 100, baselineBytes * 102 / 100);
        }
    }

    [Fact]
    public void TaintIterationCounterResetsRatherThanAccumulating()
    {
        var diagnostics = new DiagnosticBag();
        var source = """
            §M{m001:TaintCounters}
              §F{f001:Identity:pub} (i32:value) -> i32
                §R value
              §F{f002:Caller:pub} (i32:value) -> i32
                §WH{w001} (> value INT:0)
                  §B{copy:i32} §C{Identity} §A value §/C
                  §R copy
                §R INT:0
            """;
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Binder(diagnostics).Bind(new Parser(tokens, diagnostics).Parse());
        Assert.False(diagnostics.HasErrors);
        var runner = new TaintAnalysisRunner(new DiagnosticBag());
        runner.Analyze(module);
        var iterations = runner.Iterations;
        output.WriteLine($"Interprocedural taint iterations: {iterations}");
        Assert.Equal(11, iterations);
        Assert.True(iterations > module.Functions.Sum(function => new TaintAnalysis(function).Iterations));
        runner.Analyze(module);
        Assert.Equal(iterations, runner.Iterations);
        runner.AnalyzeFunction(module.Functions[0]);
        Assert.Equal(new TaintAnalysis(module.Functions[0]).Iterations, runner.Iterations);
    }
}
