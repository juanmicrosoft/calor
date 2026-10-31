using System.Reflection;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;
using Xunit.Abstractions;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Performance.Tests;

public sealed class IdentityAllocationTests(ITestOutputHelper output)
{
    private static readonly Func<ModuleNode, string> Snapshot =
        typeof(Binder).GetMethod("SnapshotIdentityKey", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<ModuleNode, string>>();

    [Fact]
    public void IdentityStageAllocationProfilePreservesTheSyntheticWorkload()
    {
        var source = SyntheticCodeGenerator.Generate(10, 50);
        for (var run = 0; run < 6; run++)
        {
            var diagnostics = new DiagnosticBag();
            var start = GC.GetAllocatedBytesForCurrentThread();
            var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
            var lex = GC.GetAllocatedBytesForCurrentThread();
            var ast = new Parser(tokens, diagnostics).Parse();
            var parse = GC.GetAllocatedBytesForCurrentThread();
            var fingerprint = Snapshot(ast);
            var snapshot = GC.GetAllocatedBytesForCurrentThread();
            var bound = new Binder(diagnostics).Bind(ast);
            var bind = GC.GetAllocatedBytesForCurrentThread();
            var graphs = bound.Functions.Select(ControlFlowGraph.Build).ToArray();
            var cfg = GC.GetAllocatedBytesForCurrentThread();
            var taint = new TaintAnalysisRunner(new DiagnosticBag());
            taint.Analyze(bound);
            var end = GC.GetAllocatedBytesForCurrentThread();
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            Assert.Equal(64, fingerprint.Length);
            Assert.Equal(4527, tokens.Count);
            Assert.Equal(1351, BoundNodeHelpers.DescendantsAndSelf(bound).Count());
            Assert.Equal(580, graphs.Sum(graph => graph.Blocks.Count));
            Assert.Equal(760, graphs.Sum(graph => graph.Blocks.Sum(block => block.Successors.Count)));
            Assert.Equal(580, taint.Iterations);
            if (run == 5)
                output.WriteLine($"lex={lex - start}; parse={parse - lex}; snapshot={snapshot - parse}; " +
                    $"bind={bind - snapshot}; cfg={cfg - bind}; taint={end - cfg}");
        }
    }
}
