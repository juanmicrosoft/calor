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

public class MultilineCallExpressionTests
{
    [Theory]
    [InlineData("§B{value:i32} §C{Math.Max}\n  §A 3\n  §A 7\n§/C\n§R value", 7)]
    [InlineData("§R §C{Math.Max}\n  §A 3\n  §A 7\n§/C", 7)]
    [InlineData("§R (+ 1 §C{Math.Max}\n  §A 3\n  §A 7\n§/C)", 8)]
    [InlineData("§R §C{Math.Clamp}\n  §A[value] 7\n  §A[min] 0\n  §A[max] 5\n§/C", 5)]
    [InlineData("§B{value:i32} §C{Math.Max}\n  §A 3\n  §A 7\n§R value", 7)]
    [InlineData("§R §C{Math.Max}\n  §A 3\n  §A 7", 7)]
    [InlineData("§B{value:i32} §C{Math.Max} §A 3\n  §A 7\n§/C\n§R value", 7)]
    [InlineData("§B{value:i32} §C{Math.Max}\n  §A 3\n  §A 7 §/C\n§R value", 7)]
    [InlineData("§B{value:i32} §C{Math.Max}\n§A 3\n§A 7\n§/C\n§R value", 7)]
    [InlineData("§R §C{Math.Max}\n  §A §C{Math.Min}\n    §A 8\n    §A 3\n  §/C\n  §A 7\n§/C", 7)]
    [InlineData("§R §C{Math.Max}\n  §A (+ 1 §C{Math.Min}\n    §A 8\n    §A 3\n  §/C)\n  §A 7\n§/C", 7)]
    public void Arguments_CompileExecuteAndRoundTrip(string body, int expected)
    {
        var source = ProgramSource(body);
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
            Assert.Equal(expected, Compile(text).GetMethod("Probe")!.Invoke(null, null));
    }

    [Fact]
    public void NestedArgumentLists_LeaveLoopAndFollowingFunctionIntact()
    {
        const string source = """
            §M{m1:Calls}
              §F{f1:Probe:pub} (List<i32>:trace) -> void
                §E{mut}
                §L{l1:i:0:2:1}
                  §C{trace.Add}
                    §A §C{Math.Max}
                      §A i
                      §A 1
                    §/C
                  §/C
                §C{trace.Add} §A 99 §/C
              §F{f2:Other:pub} () -> i32
                §R 42
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(2, module.Functions.Count);
        Assert.Equal(2, module.Functions[0].Body.Count);
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var type = Compile(text);
            var trace = new List<int>();
            type.GetMethod("Probe")!.Invoke(null, [trace]);
            Assert.Equal(new[] { 1, 1, 2, 99 }, trace);
            Assert.Equal(42, type.GetMethod("Other")!.Invoke(null, null));
        }
    }

    [Fact]
    public void ElidedHeaderAndLispCalls_LeaveStatementsAndSiblingOperandsIntact()
    {
        const string source = """
            §M{m1:Calls}
              §F{f1:Truth:pub} () -> bool
                §E{}
                §R true
              §F{f2:Text:pub} () -> str
                §E{}
                §R "12"
              §F{f3:Never:pub} () -> bool
                §E{}
                §R false
              §F{f4:One:pub} () -> i32
                §E{}
                §R 1
              §F{f5:Probe:pub} () -> i32
                §E{}
                §B{~value:i32} 0
                §EACH{each1:ch} §C{Text}
                  §IF{if1} §C{Truth}
                    (inc value)
                §WH{w1} §C{Never}
                  §C{Math.Abs} §A 9 §/C
                §R (+ value (+ §C{One}
                  3))
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(4, module.Functions.Last().Body.Count);
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
            Assert.Equal(6, Compile(text).GetMethod("Probe")!.Invoke(null, null));
    }

    [Fact]
    public void ElidedZeroArgumentCall_DoesNotTakeConstructorSiblingArguments()
    {
        const string source = """
            §M{m1:Calls}
              §F{f1:One:pub} () -> i32
                §E{}
                §R 1
              §F{f2:Probe:pub} () -> i32
                §E{alloc}
                §B{pair:Tuple<i32,i32>} §NEW{Tuple<i32,i32>}
                  §A §C{One}
                  §A 2
                §/NEW
                §R (+ (* pair.Item1 10) pair.Item2)
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var reparsed = Parse(text, out diagnostics);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
            var initializers = reparsed.Functions[1].Body.OfType<BindStatementNode>()
                .Select(binding => binding.Initializer).ToArray();
            var constructor = Assert.Single(initializers.OfType<NewExpressionNode>());
            Assert.Equal(2, constructor.Arguments.Count);
            var call = constructor.Arguments[0] as CallExpressionNode
                ?? Assert.Single(initializers.OfType<CallExpressionNode>());
            Assert.Equal("One", call.Target);
            Assert.Empty(call.Arguments);
            Assert.Equal(2, Assert.IsType<IntLiteralNode>(constructor.Arguments[1]).Value);
        }
    }

    [Fact]
    public void OriginalFileReadRepro_ExecutesAndRoundTrips()
    {
        const string source = """
            §M{m1:Calls}
              §F{f1:ReadFile:pub} (str:path) -> str
                §E{fs:r}
                §B{str:content} §C{File.ReadAllText}
                  §A path
                §/C
                §R content
            """;
        var path = Path.Combine(AppContext.BaseDirectory, $"multiline-call-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "multiline arguments preserve the file path");
            var module = Parse(source, out var diagnostics);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
            foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
                Assert.Equal("multiline arguments preserve the file path",
                    Compile(text).GetMethod("ReadFile")!.Invoke(null, [path]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("§B{value:i32} §C{Math.Abs}\n  3\n§/C\n§R value", "§A")]
    [InlineData("§B{value:i32} §C{Math.Abs}\n§A 3\n§R value", "§/C")]
    public void MalformedArguments_NameTheCauseWithoutLosingFunctionScope(string body, string requiredToken)
    {
        var source = ProgramSource(body) + "\n  §F{f2:Other:pub} () -> i32\n    §R 42\n";
        var module = Parse(source, out var diagnostics);
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal(5, error.Span.Line);
        Assert.Contains("Math.Abs", error.Message);
        Assert.Contains(requiredToken, error.Message);
        Assert.Equal(2, module.Functions.Count);
        Assert.IsType<ReturnStatementNode>(module.Functions[0].Body.Last());
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[1].Body));
    }

    private static string ProgramSource(string body) =>
        "§M{m1:Calls}\n  §F{f1:Probe:pub} () -> i32\n    §E{}\n"
        + string.Join("\n", body.Split('\n').Select(line => "    " + line)) + "\n";

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        return new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
    }

    private static Type Compile(string source)
    {
        var result = Program.Compile(source, "calls.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("Calls_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("Calls.CallsModule", throwOnError: true)!;
    }
}
