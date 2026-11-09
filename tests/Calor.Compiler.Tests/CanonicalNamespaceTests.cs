using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalNamespaceTests
{
    [Theory]
    [InlineData("module Grid Contoso.Tools", "Contoso.Tools", true)]
    [InlineData("module Grid Contoso.Tools block", "Contoso.Tools", false)]
    [InlineData("module Grid Contoso.@event", "Contoso.event", true)]
    [InlineData("module Grid @overflow block overflow unchecked", "overflow", false)]
    [InlineData("module Grid _global", "_global", true)]
    [InlineData("module Grid", "", true)]
    public void PositionalNamespaceIsSeparateFromWrapperAndStaticContainer(
        string header, string expectedNamespace, bool fileScoped)
    {
        var module = Parse($"{header}\n  module Area\n    func Square (int:side) → int\n      return side * side");
        Assert.Equal("Grid", module.Name);
        var scope = Assert.Single(module.NamespaceScopes);
        Assert.Equal(expectedNamespace, scope.FullName);
        Assert.Equal(fileScoped, scope.IsFileScoped);
        Assert.Equal(expectedNamespace.Length == 0, scope.IsGlobal);
        var container = Assert.Single(module.Classes);
        Assert.True(container.IsModule);
        Assert.Equal(expectedNamespace, container.NamespaceIdentity);
        Assert.Equal(scope.Id, container.NamespaceScopeId);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted);
        Assert.Equal(expectedNamespace, reparse.Classes.Single().NamespaceIdentity);
        Assert.Equal(fileScoped, reparse.NamespaceScopes.Single().IsFileScoped);
        var compilation = Compile(new CSharpEmitter().Emit(reparse));
        var typeName = expectedNamespace.Length == 0 ? "Area" : $"{expectedNamespace}.Area";
        Assert.NotNull(compilation.GetTypeByMetadataName(typeName));
        Assert.Null(compilation.GetTypeByMetadataName($"{expectedNamespace}.Grid"));
    }

    [Fact]
    public void NativeUsingFormsKeepCompilationUnitScopeAndTargets()
    {
        var module = Parse("""
            module Grid Contoso.Tools
              global using System
              using System.Text
              using static System.Math
              using Json = System.Text.Json.JsonDocument
              class Area
            """);
        Assert.Equal(4, module.Usings.Count);
        Assert.All(module.Usings, directive => Assert.Null(directive.NamespaceScopeId));
        Assert.True(module.Usings[2].IsStatic);
        Assert.Equal("Json", module.Usings[3].Alias);
        Assert.True(module.Usings[0].IsGlobal);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted);
        Assert.Equal(module.Usings.Select(directive => directive.Namespace),
            reparse.Usings.Select(directive => directive.Namespace));
        Compile(new CSharpEmitter().Emit(reparse));
    }

    [Theory]
    [InlineData("internal interface ISource { int Get(); }", "", "ISource", Accessibility.Internal)]
    [InlineData("namespace Contoso.Tools; internal interface ISource { int Get(); }",
        "Contoso.Tools", "ISource", Accessibility.Internal)]
    [InlineData("namespace Contoso.@event { public class Owner { private interface ISource { int Get(); } } }",
        "Contoso.event", "Owner+ISource", Accessibility.Private)]
    [InlineData("namespace _global { public class Owner { protected interface ISource { int Get(); } } }",
        "_global", "Owner+ISource", Accessibility.Protected)]
    public void RealConverterNativeSourceAndClrPreserveOriginalInterfaceExports(
        string source, string ns, string type, Accessibility expected)
    {
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        Assert.NotNull(conversion.CalorSource);
        Assert.DoesNotContain("raw ", conversion.CalorSource);
        Assert.DoesNotContain('§', conversion.CalorSource);
        var module = Parse(conversion.CalorSource);
        Assert.Equal(ns, module.NamespaceScopes.Single().FullName);
        var compiled = Compile(new CSharpEmitter().Emit(module));
        var original = Compile(source);
        var metadataName = ns.Length == 0 ? type : $"{ns}.{type}";
        Assert.Equal(expected, original.GetTypeByMetadataName(metadataName)!.DeclaredAccessibility);
        Assert.Equal(expected, compiled.GetTypeByMetadataName(metadataName)!.DeclaredAccessibility);
    }

    [Theory]
    [InlineData("namespace A { class C {} } namespace A { class D {} }")]
    [InlineData("namespace A { namespace B { class C {} } }")]
    [InlineData("namespace A { using System; class C {} }")]
    public void OtherLexicalTopologiesPreserveTheOriginalWholeFile(string source)
    {
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        var parsed = Parse(conversion.CalorSource!);
        var preserved = Assert.Single(parsed.InteropBlocks);
        Assert.True(preserved.IsCompilationUnitPassthrough);
        Assert.Equal(source, preserved.CSharpCode);
        Compile(new CSharpEmitter().Emit(parsed));
    }

    [Theory]
    [InlineData("module Grid overflow=checked")]
    [InlineData("module Grid Contoso.Tools Extra")]
    [InlineData("module Grid Contoso.event")]
    [InlineData("module Grid Contoso.Tools overflow impossible")]
    public void HeaderAliasesAndMalformedNamespaceFieldsAreDiagnosed(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"{header}\n  class Area", diagnostics)
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

    private static CSharpCompilation Compile(string source)
    {
        var compilation = CSharpCompilation.Create("NamespaceExports",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return compilation;
    }
}
