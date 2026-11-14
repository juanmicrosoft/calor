using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalInterfaceTests
{
    [Fact]
    public void NativeInterfacePreservesVarianceBasesSignaturesAndContractCopyVisibility()
    {
        var module = Parse("""
            module Api
              interface ISource<out T> internal
                extends System.IDisposable
                func Get () → T
                  !
                  => true && true
                func Close ()
                  !
              class Sibling
            """);
        var definition = Assert.Single(module.Interfaces);
        Assert.Equal(Visibility.Internal, definition.Visibility);
        Assert.Equal(Calor.Compiler.Ast.VarianceKind.Out, Assert.Single(definition.TypeParameters).Variance);
        Assert.Equal("System.IDisposable", Assert.Single(definition.BaseInterfaces));
        Assert.Equal(2, definition.Methods.Count);
        var simplified = new ContractSimplificationPass(new DiagnosticBag()).Simplify(module);
        var replacement = Assert.Single(simplified.Interfaces);
        Assert.NotSame(definition, replacement);
        Assert.Equal(Visibility.Internal, replacement.Visibility);
        Assert.Single(replacement.Methods[0].Postconditions);
        var emitted = new CalorEmitter().Emit(simplified);
        Assert.DoesNotContain('§', emitted);
        Assert.Contains("func Close ()", emitted);
        var reparsed = Parse(emitted);
        Assert.Equal(Visibility.Internal, reparsed.Interfaces.Single().Visibility);
        Assert.Equal(2, reparsed.Interfaces.Single().Methods.Count);
        Assert.Single(reparsed.Classes);
        AssertCompiles(new CSharpEmitter().Emit(reparsed));
    }

    [Theory]
    [InlineData("", Visibility.Internal)]
    [InlineData("public", Visibility.Public)]
    [InlineData("internal", Visibility.Internal)]
    public void RoslynVisitorPreservesTopLevelEffectiveVisibility(string modifier, Visibility expected)
    {
        var converted = ConvertAst($"{modifier} interface ISource {{ int Get(); }}");
        var definition = Assert.Single(converted.Interfaces);
        Assert.Equal(expected, definition.Visibility);
        var generated = new CSharpEmitter().Emit(converted);
        AssertCompiles(generated);
        Assert.Contains($"{(expected == Visibility.Public ? "public" : "internal")} interface ISource", generated);
    }

    [Theory]
    [InlineData("", Visibility.Private)]
    [InlineData("private", Visibility.Private)]
    [InlineData("public", Visibility.Public)]
    [InlineData("internal", Visibility.Internal)]
    public void RoslynVisitorPreservesNestedInterfaceVisibility(string modifier, Visibility expected)
    {
        var converted = ConvertAst(
            $"public class Owner {{ {modifier} interface ISource {{ int Get(); }} }}");
        var definition = Assert.Single(Assert.Single(converted.Classes).NestedInterfaces);
        Assert.Equal(expected, definition.Visibility);
        AssertCompiles(new CSharpEmitter().Emit(converted));
    }

    [Theory]
    [InlineData("func Get<T> private () → T")]
    [InlineData("func Get<T> mystery () → T")]
    [InlineData("func Get → int")]
    public void MalformedSignaturesAreNotPromotedToPublicNativeMethods(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  interface ISource\n    {header}", diagnostics)
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

    private static ModuleNode ConvertAst(string source)
    {
        var context = new ConversionContext();
        var module = new RoslynSyntaxVisitor(context).Convert(
            CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot(), "Visibility");
        Assert.False(context.HasErrors);
        return module;
    }

    private static void AssertCompiles(string source)
    {
        var compilation = CSharpCompilation.Create("InterfaceVisibility",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }
}
