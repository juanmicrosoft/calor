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
    [InlineData("return value")]
    [InlineData("if (value > 0)\n      return value\n    else\n      throw new InvalidOperationException(error)")]
    [InlineData("for i from 0 to 1 step 1\n      if (value > 0)\n        return value\n    throw new InvalidOperationException(error)")]
    public void ReturnContexts_PreserveValueAndThrowMessage(string body)
    {
        var source = """
            module ResultContext ResultContext block
              using System
              func Probe (int:value, string:error) → long
                ! alloc,throw
            """ + "\n    " + body;
        foreach (var text in RoundTrip(source))
        {
            var type = Compile(text);
            var method = type.GetMethod("Probe")!;
            Assert.Equal(7L, Assert.IsType<long>(method.Invoke(null, [7, "failed"])));
            if (body != "return value")
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
            module ResultContext ResultContext block
              using System
              func Probe (Func<int>:first ! throw, Func<int>:second ! throw, bool:fail) → long
                ! alloc,throw
                let int:left = first()
                if fail
                  throw new InvalidOperationException("failed")
                return (left + second())
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
            §M{ResultContext}
              §F{Probe:pub} (i32:value, i32:error) -> Result<Result<i64,i32>,i32>
                §E{alloc}
                §IF (> value 0)
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
