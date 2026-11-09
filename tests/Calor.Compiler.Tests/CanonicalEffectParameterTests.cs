using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.Loader;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalEffectParameterTests
{
    [Fact]
    public void DefaultCompilationErasesOnlyTheBinderAndExecutesBothCallbackCalls()
    {
        var source = """
            module Polymorphic
              func RunTwice <! e> (Func<int>:g ! e) → int
                ! e
                return g() + g()
            """;
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var compilation = CSharpCompilation.Create("NativePolymorphic",
            [CSharpSyntaxTree.ParseText(result.GeneratedCode)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("NativePolymorphic", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var method = assembly.GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "RunTwice");
            var calls = 0;
            Func<int> next = () => ++calls;
            Assert.Equal(3, method.Invoke(null, [next]));
            Assert.Equal(2, calls);
            Assert.False(method.IsGenericMethod);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void CountedEffectBindersAreNotTypeArgumentsAndKeepTheirOrdinals()
    {
        var module = Parse("""
            module Polymorphic
              func RunTwice <! e> (Func<int>:g ! e) → int
                ! e
                return g() + g()
              func Handle <! e> (int:request, Func<int>:next ! e) → int
                ! e
                return RunTwice(next)
            """);
        var function = module.Functions[0];
        Assert.Empty(function.TypeParameters);
        Assert.Equal("e", Assert.Single(function.EffectParameters).Name);
        Assert.Equal(0, function.EffectParameters.Single().Ordinal);
        Assert.Equal(0, Assert.Single(function.Parameters.Single().Row!.EffectVariableOrdinals));
        Assert.Equal(0, Assert.Single(function.Effects!.EffectVariableOrdinals));
        var emitted = new CalorEmitter().Emit(module);
        Assert.Contains("func RunTwice <! e>", emitted);
        Assert.DoesNotContain("eff e", emitted);
        var reparsed = Parse(emitted).Functions[0];
        Assert.Empty(reparsed.TypeParameters);
        Assert.Equal(function.EffectParameters.Single().Ordinal, reparsed.EffectParameters.Single().Ordinal);
        Assert.DoesNotContain("<! e>", new CSharpEmitter().Emit(module));
    }

    [Fact]
    public void ParameterRowsDoNotSwallowTheFollowingTypedParameter()
    {
        var module = Parse("""
            module Polymorphic
              func Copy<T,! e> (Func<T>:first ! e, T:value, Func<T>:last ! e) → T
                ! e
                return value
            """);
        var function = module.Functions.Single();
        Assert.Single(function.TypeParameters);
        Assert.Equal(1, Assert.Single(function.EffectParameters).Ordinal);
        Assert.Equal(new[] { "first", "value", "last" }, function.Parameters.Select(parameter => parameter.Name));
        Assert.Null(function.Parameters[1].Row);
        Assert.Equal(0, function.Parameters[2].Row!.EffectVariableOrdinals.Single());
        var reparse = Parse(new CalorEmitter().Emit(module)).Functions.Single();
        Assert.Equal(3, reparse.Parameters.Count);
        Assert.Equal(1, reparse.EffectParameters.Single().Ordinal);
    }

    [Theory]
    [InlineData("Func<int>:first ! e, @fs:r", 2)]
    [InlineData("Func<int>:first ! e, fs:r, int:value", 2)]
    [InlineData("Func<int>:first ! mut,throw, Func<int>:last ! e", 2)]
    public void ConcreteRowsAndEscapedTypeNamesKeepParameterBoundaries(string parameters, int count)
    {
        var module = Parse($"module Rows\n  func F <! e> ({parameters}) → void\n    ! e,mut,throw,fs:r");
        var function = module.Functions.Single();
        Assert.Equal(count, function.Parameters.Count);
        var emitted = new CalorEmitter().Emit(module);
        var reparsed = Parse(emitted).Functions.Single();
        Assert.Equal(function.Parameters.Select(parameter => parameter.TypeName),
            reparsed.Parameters.Select(parameter => parameter.TypeName));
        Assert.Equal(function.Parameters.Select(parameter => parameter.Name),
            reparsed.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(function.Parameters[0].Row!.Effects, reparsed.Parameters[0].Row!.Effects);
    }

    [Theory]
    [InlineData("func Bad <eff e> () → void")]
    [InlineData("func Bad <! cw> () → void")]
    [InlineData("class Bad <! e>")]
    public void RetiredBindersAndTaxonomyCollisionsAreDiagnosed(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  {header}", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
