using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ArrowConditionalRuntimeTests
{
    [Fact]
    public void ArrowReturn_DoesNotStealLoopDedent()
    {
        const string source = """
            §M{m1:Arrow}
              §F{f1:HasNegative:pub} ([i32]:arr) -> bool
                §E{}
                §L{for1:i:0:(- (len arr) 1):1}
                  §IF{if1} (< arr{i} 0) → §R true
                §R false
            """;
        var module = Parse(source);
        var function = Assert.Single(module.Functions);
        Assert.IsType<ForStatementNode>(function.Body[0]);
        Assert.IsType<ReturnStatementNode>(function.Body[1]);
        var type = Compile(source);
        Assert.Equal(false, Invoke(type, "HasNegative", Array.Empty<int>()));
        Assert.Equal(false, Invoke(type, "HasNegative", new[] { 1, 2, 3 }));
        Assert.Equal(true, Invoke(type, "HasNegative", new[] { 1, 2, -3 }));
    }

    [Theory]
    [InlineData("§IF{if1} (< i 0) → §R\n")]
    [InlineData("§IF{if1} (&&\n        (< i 0)\n        (> i -10)) → §R\n")]
    [InlineData("§IF{if1} (< i 0) → §R\n      §EI (&&\n        (> i 9)\n        (< i 20)) → §R\n")]
    [InlineData("§IF{if1} (< i 0) → §R\n      §EI (> i 9) → §R\n")]
    [InlineData("§IF{if1} (< i 0) → §R\n      §EL → §C{trace.Add} §A i §/C\n")]
    [InlineData("§IF{if1} (< i 0) → §R\n      §EL\n        §C{trace.Add} §A i §/C\n")]
    [InlineData("§IF{if1} (< i 0) →\n        §R\n      §EI (> i 9)\n        §R\n      §EL → §C{trace.Add} §A i §/C\n")]
    public void FollowingNonReturnStatement_ExecutesOnceAfterLoop(string branch)
    {
        var source = """
            §M{m1:Arrow}
              §F{f1:Probe:pub} (List<i32>:trace) -> void
                §E{mut}
                §L{for1:i:0:2:1}
                  §C{trace.Add} §A i §/C
            """ + "\n      " + branch + "    §C{trace.Add} §A 99 §/C\n";
        var module = Parse(source);
        var function = Assert.Single(module.Functions);
        Assert.Equal(2, function.Body.Count);
        var loop = Assert.IsType<ForStatementNode>(function.Body[0]);
        Assert.Equal(2, loop.Body.Count);
        Assert.IsType<CallStatementNode>(function.Body[1]);
        var expected = branch.Contains("§EL") ? new[] { 0, 0, 1, 1, 2, 2, 99 } : new[] { 0, 1, 2, 99 };
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var trace = new List<int>();
            Invoke(Compile(text), "Probe", trace);
            Assert.Equal(expected, trace);
        }
    }

    [Fact]
    public void NestedInlineIf_LeavesOuterElseAndNextFunctionIntact()
    {
        var type = Compile("""
            §M{m1:Arrow}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §IF{if1} (> x 0)
                  §IF{if2} (> x 1) → §R 2
                §EL
                  §R -1
                §R 1
              §F{f2:Other:pub} () -> i32
                §E{}
                §IF{if3} true → §R 3
                §R 4
            """);
        Assert.Equal(-1, Invoke(type, "Probe", 0));
        Assert.Equal(1, Invoke(type, "Probe", 1));
        Assert.Equal(2, Invoke(type, "Probe", 2));
        Assert.Equal(3, Invoke(type, "Other"));
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }

    private static object? Invoke(Type type, string name, params object?[] args) =>
        type.GetMethod(name)!.Invoke(null, args);

    private static Type Compile(string source)
    {
        var result = Program.Compile(source, "arrow.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("Arrow_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("Arrow.ArrowModule", throwOnError: true)!;
    }
}
