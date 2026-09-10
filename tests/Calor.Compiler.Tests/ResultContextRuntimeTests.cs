using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ResultContextRuntimeTests
{
    [Theory]
    [InlineData("§R §OK value", true)]
    [InlineData("§R §ERR error", false)]
    [InlineData("§IF{if1} (> value 0)\n      §R §OK value\n    §EL\n      §R §ERR error", true)]
    [InlineData("§R §IF{if1} (> value 0) → §OK value §EL → §ERR error", true)]
    public void ReturnContexts_PreserveBothGenericArguments(string body, bool isOk)
    {
        var source = """
            §M{m1:ResultContext}
              §F{f1:Probe:pub} (i32:value, str:error) -> Result<i32,str>
                §E{alloc}
            """ + "\n    " + body;
        foreach (var text in RoundTrip(source))
        {
            var type = Compile(text);
            var result = Assert.IsType<Result<int, string>>(type.GetMethod("Probe")!.Invoke(null, [7, "failed"]));
            Assert.Equal(isOk, result.IsOk);
            if (isOk)
                Assert.Equal(7, result.Unwrap());
            else
                Assert.Equal("failed", result.UnwrapErr());
        }
    }

    [Fact]
    public void NonStringErrorAndNestedResult_PreserveExpectedPayloadTypes()
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
        foreach (var text in RoundTrip(source))
        {
            var type = Compile(text);
            var method = type.GetMethod("Probe")!;
            var success = Assert.IsType<Result<Result<long, int>, int>>(method.Invoke(null, [7, 42]));
            Assert.Equal(7L, success.Unwrap().Unwrap());
            var failure = Assert.IsType<Result<Result<long, int>, int>>(method.Invoke(null, [0, 42]));
            Assert.Equal(42, failure.Unwrap().UnwrapErr());
        }
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
