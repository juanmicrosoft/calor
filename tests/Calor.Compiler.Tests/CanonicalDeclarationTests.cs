using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalDeclarationTests
{
    [Fact]
    public void CanonicalFunctionsPreserveTypesBindingsVisibilityAndPublicDefault()
    {
        var module = Parse("""
            module Demo
              func Pick (int:value, string?:text, int[]:items) → int
                !
                let int:copy = value
                var count = copy + 1
                return count
              func Hidden private () → void
                return
              class Box
                func Echo (int:value) → int
                  return value
            """);
        Assert.Equal("Demo", module.Name);
        var function = module.Functions[0];
        Assert.Equal(Visibility.Public, function.Visibility);
        Assert.Equal(new[] { "int", "string?", "int[]" }, function.Parameters.Select(parameter => parameter.TypeName));
        Assert.Equal("int", function.Output!.TypeName);
        var bindings = function.Body.OfType<BindStatementNode>().ToArray();
        Assert.Equal(2, bindings.Length);
        Assert.False(bindings[0].IsMutable);
        Assert.Equal("int", bindings[0].TypeName);
        Assert.True(bindings[1].IsMutable);
        Assert.Null(bindings[1].TypeName);
        Assert.Equal(Visibility.Private, module.Functions[1].Visibility);
        Assert.Equal(Visibility.Public, module.Classes[0].Visibility);
        Assert.Equal(Visibility.Public, module.Classes[0].Methods[0].Visibility);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("string?")]
    [InlineData("int[]")]
    [InlineData("int?[]")]
    [InlineData("Dictionary<string,List<int?[]>>")]
    [InlineData("(int X, string? Y)[]")]
    public void CanonicalTypesRoundTripWithoutLoss(string type)
    {
        var module = Parse($"module Types\n  func Copy ({type}:value) → {type}\n    return value");
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparsed = Parse(emitted);
        Assert.Equal(type, reparsed.Functions[0].Parameters[0].TypeName);
        Assert.Equal(type, reparsed.Functions[0].Output!.TypeName);
    }

    [Fact]
    public void DocumentationAndPureHeaderClausesRetainTheirSemantics()
    {
        var module = Parse("""
            module Contracts
              /// since 1.2.0
              func Copy (int:value) → int
                !
                => result == value
                return value
            """);
        var function = module.Functions[0];
        Assert.Equal("1.2.0", function.Since!.Version);
        Assert.Empty(function.Effects!.Effects);
        Assert.Single(function.Postconditions);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted).Functions[0];
        Assert.Equal("1.2.0", reparse.Since!.Version);
        Assert.NotNull(reparse.Effects);
        Assert.Single(reparse.Postconditions);
    }

    [Theory]
    [InlineData("! cw")]
    [InlineData("=> true")]
    public void HeaderClausesCannotFollowMethodStatements(string clause)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  class C\n    func F () → int\n      return 1\n      {clause}", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void NativeSignaturesPreserveGenericModifiersDefaultsAndEscapedNames()
    {
        var module = Parse("""
            module Names
              func @class<T> private async (ref T:@in, out int:@out, string:text = "value") → T
                var @return = text
                return @in
            """);
        var function = module.Functions[0];
        Assert.Equal("class", function.Name);
        Assert.True(function.IsAsync);
        Assert.Single(function.TypeParameters);
        Assert.Equal(ParameterModifier.Ref, function.Parameters[0].Modifier);
        Assert.Equal(ParameterModifier.Out, function.Parameters[1].Modifier);
        Assert.Equal("in", function.Parameters[0].Name);
        Assert.Equal("return", Assert.IsType<BindStatementNode>(function.Body[0]).Name);
        var roundTrip = Parse(new CalorEmitter().Emit(module)).Functions[0];
        Assert.Equal(function.Name, roundTrip.Name);
        Assert.Equal(function.Visibility, roundTrip.Visibility);
        Assert.True(roundTrip.IsAsync);
        Assert.NotNull(roundTrip.Parameters[2].DefaultValue);
    }

    [Fact]
    public void CanonicalPublicApiCompilesAndPreservesPrivateExports()
    {
        var module = Parse("""
            module Api
              func Pick (int:value) → int
                let int:copy = value
                return copy
              func Hidden private () → void
                return
              class Box
                func Echo (int:value) → int
                  return value
            """);
        var generated = new CSharpEmitter().Emit(module);
        var compilation = CSharpCompilation.Create("CanonicalPublicApi",
            [CSharpSyntaxTree.ParseText(generated)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var symbols = compilation.GlobalNamespace.GetNamespaceMembers()
            .SelectMany(ns => ns.GetTypeMembers()).Concat(compilation.GlobalNamespace.GetTypeMembers()).ToArray();
        var methods = symbols.SelectMany(symbol => symbol.GetMembers().OfType<IMethodSymbol>()).ToArray();
        Assert.Equal(Accessibility.Public, Assert.Single(methods.Where(method => method.Name == "Pick")).DeclaredAccessibility);
        Assert.Equal(Accessibility.Private, Assert.Single(methods.Where(method => method.Name == "Hidden")).DeclaredAccessibility);
    }

    [Theory]
    [InlineData("func F (int value) → int")]
    [InlineData("func F (int:value:ref) → int")]
    [InlineData("func F () -> int")]
    [InlineData("§F{F:pub} () → int")]
    public void RetiredHeadersAreNotNormalizedIntoPositiveInputs(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  {header}\n    return 1", diagnostics)
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
