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

public class OptionContextRuntimeTests
{
    [Theory]
    [InlineData("§IF{if1} (> value 0)\n      §R §SM value\n    §EL\n      §R §NN")]
    [InlineData("§L{for1:i:0:1:1}\n      §IF{if1} (> value 0)\n        §R §SM value\n    §R §NN")]
    [InlineData("§R §IF{if1} (> value 0) → §SM value §EL → §NN")]
    [InlineData("§S true\n    §IF{if1} (> value 0)\n      §R §SM value\n    §R §NN")]
    [InlineData("§W{w1} value\n      §K 7\n        §R §SM value\n      §K _\n        §R §NN")]
    public void NestedReturns_UseDeclaredOptionElementType(string body)
    {
        var source = """
            §M{m1:OptionContext}
              §F{f1:Probe:pub} (i32:value) -> Option<i64>
                §E{alloc}
            """ + "\n    " + body;
        foreach (var text in RoundTrip(source))
        {
            var method = Compile(text).GetMethod("Probe")!;
            var present = Assert.IsType<Option<long>>(method.Invoke(null, [7]));
            Assert.True(present.IsSome);
            Assert.Equal(7L, present.Unwrap());
            var absent = Assert.IsType<Option<long>>(method.Invoke(null, [0]));
            Assert.True(absent.IsNone);
        }
    }

    [Fact]
    public void NestedOptionPayloads_KeepEachExpectedType()
    {
        const string source = """
            §M{m1:OptionContext}
              §F{f1:Probe:pub} (i32:value) -> Option<Option<i32>>
                §E{alloc}
                §IF{if1} (> value 0)
                  §R §SM §SM value
                §EI (== value 0)
                  §R §SM §NN
                §EL
                  §R §NN
            """;
        foreach (var text in RoundTrip(source))
        {
            var method = Compile(text).GetMethod("Probe")!;
            Assert.Equal(7, Assert.IsType<Option<Option<int>>>(method.Invoke(null, [7])).Unwrap().Unwrap());
            Assert.True(Assert.IsType<Option<Option<int>>>(method.Invoke(null, [0])).Unwrap().IsNone);
            Assert.True(Assert.IsType<Option<Option<int>>>(method.Invoke(null, [-1])).IsNone);
        }
    }

    [Fact]
    public void ExplicitNoneAnnotation_IsNotSilentlyOverridden()
    {
        var result = Program.Compile("""
            §M{m1:OptionContext}
              §F{f1:Probe:pub} () -> Option<i32>
                §E{alloc}
                §R §NN{str}
            """, "option-context.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.CodeGenCompilationError);
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
        var result = Program.Compile(source, "option-context.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("OptionContext_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("OptionContext.OptionContextModule", throwOnError: true)!;
    }
}
