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
    [InlineData("if (value > 0)\n      return value\n    else\n      return null")]
    [InlineData("for i from 0 to 1 step 1\n      if (value > 0)\n        return value\n    return null")]
    [InlineData("return ((value > 0) ? value : null)")]
    [InlineData("=> true\n    if (value > 0)\n      return value\n    return null")]
    [InlineData("match value\n      case 7\n        return value\n      case _\n        return null")]
    public void NestedReturns_UseDeclaredNullableElementType(string body)
    {
        var source = """
            module OptionContext OptionContext block
              func Probe (int:value) → long?
                ! alloc
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
            module OptionContext OptionContext block
              func Probe (Func<int?>:produce ! throw, Func<int>:fallback ! throw) → long
                ! throw
                return (produce() ?? fallback())
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
            §M{OptionContext}
              §F{Probe:pub} (i32:value) -> Option<Option<i32>>
                §E{alloc}
                §IF (> value 0)
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
            §M{OptionContext}
              §F{Probe:pub} () -> Option<i32>
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
