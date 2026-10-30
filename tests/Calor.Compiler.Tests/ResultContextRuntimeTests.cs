using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ResultContextRuntimeTests
{
    [Theory]
    [InlineData("§R value")]
    [InlineData("§IF{if1} (> value 0)\n      §R value\n    §EL\n      §TH §NEW{InvalidOperationException} §A error §/NEW")]
    [InlineData("§L{for1:i:0:1:1}\n      §IF{if1} (> value 0)\n        §R value\n    §TH §NEW{InvalidOperationException} §A error §/NEW")]
    public void ReturnContexts_PreserveValueAndThrowMessage(string body)
    {
        var source = """
            §M{m1:ResultContext}
              §U{System}
              §F{f1:Probe:pub} (i32:value, str:error) -> i64
                §E{alloc,throw}
            """ + "\n    " + body;
        foreach (var text in RoundTrip(source))
        {
            var type = Compile(text);
            var method = type.GetMethod("Probe")!;
            Assert.Equal(7L, Assert.IsType<long>(method.Invoke(null, [7, "failed"])));
            if (body != "§R value")
            {
                var exception = Assert.Throws<TargetInvocationException>(
                    () => method.Invoke(null, [0, "failed"]));
                Assert.Equal("failed", Assert.IsType<InvalidOperationException>(exception.InnerException).Message);
            }
        }
    }

    [Fact]
    public void NestedFailure_PreservesEvaluationOrderAndStopsAfterThrow()
    {
        const string source = """
            §M{m1:ResultContext}
              §U{System}
              §F{f1:Probe:pub} (Func<i32>:first §E{throw}, Func<i32>:second §E{throw}, bool:fail) -> i64
                §E{alloc,throw}
                §B{left:i32} §C{first} §/C
                §IF{if1} fail
                  §TH §NEW{InvalidOperationException} §A "failed" §/NEW
                §R (+ left §C{second} §/C)
            """;
        foreach (var text in RoundTrip(source))
        {
            var method = Compile(text).GetMethod("Probe")!;
            var events = new List<string>();
            Func<int> first = () => { events.Add("first"); return 7; };
            Func<int> second = () => { events.Add("second"); return 5; };
            Assert.Equal(12L, method.Invoke(null, [first, second, false]));
            Assert.Equal(["first", "second"], events);
            events.Clear();
            var error = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(null, [first, second, true]));
            Assert.Equal("failed", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
            Assert.Equal(["first"], events);
            events.Clear();
            Func<int> throwingFirst = () =>
            {
                events.Add("first");
                throw new ApplicationException("producer failed");
            };
            var nested = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(null, [throwingFirst, second, false]));
            Assert.Equal("producer failed", Assert.IsType<ApplicationException>(nested.InnerException).Message);
            Assert.Equal(["first"], events);
        }
    }

    [Fact]
    public void NonStringErrorAndNestedResult_AreRejectedRatherThanFlattened()
    {
        const string source = """
            §M{m1:ResultContext}
              §F{f1:Probe:pub} (i32:value, i32:error) -> Result<Result<i64,i32>,i32>
                §E{alloc}
                §IF{if1} (> value 0)
                  §R §OK §OK value
                §EL
                  §R §OK §ERR error
            """;
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static IEnumerable<string> RoundTrip(string source)
    {
        yield return source;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        yield return new CalorEmitter().Emit(module);
    }

    private static Type Compile(string source)
    {
        var result = Program.Compile(source, "result-context.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("ResultContext_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("ResultContext.ResultContextModule", throwOnError: true)!;
    }
}
