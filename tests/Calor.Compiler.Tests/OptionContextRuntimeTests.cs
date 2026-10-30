using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class OptionContextRuntimeTests
{
    [Theory]
    [InlineData("§IF{if1} (> value 0)\n      §R value\n    §EL\n      §R null")]
    [InlineData("§L{for1:i:0:1:1}\n      §IF{if1} (> value 0)\n        §R value\n    §R null")]
    [InlineData("§R §IF{if1} (> value 0) → value §EL → null")]
    [InlineData("§S true\n    §IF{if1} (> value 0)\n      §R value\n    §R null")]
    [InlineData("§W{w1} value\n      §K 7\n        §R value\n      §K _\n        §R null")]
    public void NestedReturns_UseDeclaredNullableElementType(string body)
    {
        var source = """
            §M{m1:OptionContext}
              §F{f1:Probe:pub} (i32:value) -> ?i64
                §E{alloc}
            """ + "\n    " + body;
        foreach (var text in RoundTrip(source))
        {
            var method = Compile(text).GetMethod("Probe")!;
            Assert.Equal(typeof(long?), method.ReturnType);
            Assert.Equal(7L, Assert.IsType<long>(method.Invoke(null, [7])));
            Assert.Null(method.Invoke(null, [0]));
        }
    }

    [Fact]
    public void NullableCoalesce_EvaluatesProducerOnceAndFallbackOnlyForNull()
    {
        const string source = """
            §M{m1:OptionContext}
              §F{f1:Probe:pub} (Func<i32?>:produce §E{throw}, Func<i32>:fallback §E{throw}) -> i64
                §E{throw}
                §R (?? §C{produce} §/C §C{fallback} §/C)
            """;
        foreach (var text in RoundTrip(source))
        {
            var method = Compile(text).GetMethod("Probe")!;
            var events = new List<string>();
            Func<int?> present = () => { events.Add("produce"); return 7; };
            Func<int?> missing = () => { events.Add("produce"); return null; };
            Func<int> fallback = () => { events.Add("fallback"); return 19; };

            Assert.Equal(7L, method.Invoke(null, [present, fallback]));
            Assert.Equal(["produce"], events);
            events.Clear();
            Assert.Equal(19L, method.Invoke(null, [missing, fallback]));
            Assert.Equal(["produce", "fallback"], events);
            events.Clear();
            Func<int> throwingFallback = () =>
            {
                events.Add("fallback");
                throw new InvalidOperationException("missing");
            };
            Assert.Equal(7L, method.Invoke(null, [present, throwingFallback]));
            Assert.Equal(["produce"], events);
            events.Clear();
            var error = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(null, [missing, throwingFallback]));
            Assert.Equal("missing", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
            Assert.Equal(["produce", "fallback"], events);
        }
    }

    [Fact]
    public void NestedOptionPayloads_AreRejectedRatherThanFlattened()
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
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void ExplicitNoneAnnotation_IsRejected()
    {
        var result = Program.Compile("""
            §M{m1:OptionContext}
              §F{f1:Probe:pub} () -> Option<i32>
                §E{alloc}
                §R §NN{str}
            """, "option-context.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
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
