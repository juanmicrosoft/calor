using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalClassPolicyTests
{
    [Theory]
    [InlineData("public", Visibility.Public, Accessibility.Public)]
    [InlineData("internal", Visibility.Internal, Accessibility.Internal)]
    [InlineData("private", Visibility.Private, Accessibility.Private)]
    [InlineData("protected", Visibility.Protected, Accessibility.Protected)]
    public void NativeNestedClassVisibilityIsNotRejectedAsRetiredSyntax(
        string word, Visibility visibility, Accessibility accessibility)
    {
        var source = $"module Visibility\n  class Owner\n    class Scope {word} sealed";
        var module = Parse(source);
        Assert.Equal(visibility, module.Classes.Single().NestedClasses.Single().Visibility);
        var roundtrip = Parse(new CalorEmitter().Emit(module));
        Assert.Equal(visibility, roundtrip.Classes.Single().NestedClasses.Single().Visibility);
        var compilation = Compile(new CSharpEmitter().Emit(roundtrip));
        Assert.Equal(accessibility, compilation.GetTypeByMetadataName("Owner+Scope")!.DeclaredAccessibility);
    }

    [Fact]
    public void StaticClassIsRejectedWithNativeNamedModuleGuidance()
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer("""
            module StaticTypes
              class Counter static
                func Read static () → int
                  return 7
            """, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var error = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.InvalidModifier));
        Assert.Contains("module Counter", error.Message);
        Assert.DoesNotContain('§', error.Message);
    }

    [Fact]
    public void NamedModuleKeepsItsClrIdentityAndOrdinaryClassesKeepStaticMembers()
    {
        var source = """
            module StaticTypes
              module Counter
                func Read () → int
                  return 7
              class Ordinary
                func Read static () → int
                  return 9
            """;
        var module = Parse(source);
        Assert.True(module.Classes[0].IsModule);
        Assert.False(module.Classes[1].IsModule);
        var result = Program.Compile(new CalorEmitter().Emit(module));
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var compilation = Compile(result.GeneratedCode);
        Assert.True(compilation.GetTypeByMetadataName("Counter")!.IsStatic);
        Assert.True(compilation.GetTypeByMetadataName("Counter")!.GetMembers("Read").OfType<IMethodSymbol>().Single().IsStatic);
        Assert.False(compilation.GetTypeByMetadataName("Ordinary")!.IsStatic);
        Assert.True(compilation.GetTypeByMetadataName("Ordinary")!.GetMembers("Read").OfType<IMethodSymbol>().Single().IsStatic);
        Assert.Null(compilation.GetTypeByMetadataName("StaticTypes"));
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static CSharpCompilation Compile(string source)
    {
        var compilation = CSharpCompilation.Create("ClassPolicy",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return compilation;
    }
}
