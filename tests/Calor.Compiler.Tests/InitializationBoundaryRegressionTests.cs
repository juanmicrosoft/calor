using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class InitializationBoundaryRegressionTests
{
    [Theory]
    [InlineData("System.Collections.Generic.List<int>", "()", null)]
    [InlineData("System.Collections.Generic.Dictionary<string,int>", "()", null)]
    [InlineData("System.Random", "()", "rand")]
    [InlineData("System.Collections.Generic.List<int>", "(true)", "unknown")]
    public void NativeInitializer_MetadataConstructorRequiresSelectedContract(
        string type, string arguments, string? rejectedEffect)
    {
        var authored = $$"""
            module InitializerAuthority
              class Holder
                field {{type}}:Value public = new {{type}}{{arguments}}
                ctor ()
              func Make public () → Holder
                ! alloc
                return new Holder()
            """;
        var diagnostics = new DiagnosticBag();
        var parsed = new Parser(new Lexer(authored, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        foreach (var source in new[] { authored, new CalorEmitter().Emit(parsed) })
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions
            {
                EnableTypeChecking = typed,
                DeferGeneratedOutputValidation = true,
                StatusWriter = TextWriter.Null
            });
            if (rejectedEffect != null)
            {
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable
                    && diagnostic.Message.Contains($"'{rejectedEffect}'", StringComparison.Ordinal));
                continue;
            }
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            var assembly = Load(result.GeneratedCode);
            var make = assembly.GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Make");
            var holder = make.Invoke(null, null)!;
            Assert.NotNull(holder.GetType().GetField("Value")!.GetValue(holder));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticRead_DistinguishesPreservedAccessorsFromPreservedInitialization(bool staticInitializer)
    {
        var original = $$"""
            using System.Collections.Generic;
            public class Holder
            {
                public static int Value = 9;
                {{(staticInitializer
                    ? "public static Dictionary<int,int> Map = new Dictionary<int,int> { [1] = 2 };"
                    : "public IEnumerable<int> Items { get { yield return 1; } }")}}
            }
            public static class Probe
            {
                public static int Run() => Holder.Value;
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: true);
        var diagnostics = new DiagnosticBag();
        var parsed = new Parser(new Lexer(converted.CalorSource!, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        foreach (var source in new[] { converted.CalorSource!, new CalorEmitter().Emit(parsed) })
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions
            {
                EnableTypeChecking = typed,
                StatusWriter = TextWriter.Null
            });
            if (staticInitializer)
            {
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                    && diagnostic.Message.Contains("Holder..cctor", StringComparison.Ordinal));
                Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            }
            else
            {
                Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
                Assert.Equal(9, Load(result.GeneratedCode).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
            }
        }
    }

    private static Assembly Load(string source)
    {
        var compilation = CSharpCompilation.Create("InitializerAuthority_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
