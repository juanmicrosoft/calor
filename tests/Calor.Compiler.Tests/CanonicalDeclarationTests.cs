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
    [Theory]
    [InlineData("ctor ()")]
    [InlineData("property int:Count\n      get\n      set")]
    [InlineData("this[int:index] → int\n      get")]
    [InlineData("event System.Action:Changed")]
    public void EmptyMembersDoNotConsumeTheFollowingNativeFunction(string member)
    {
        var module = Parse($"module Demo\n  class Value\n    {member}\n    func Next () → int\n      !\n      return 42");
        var type = Assert.Single(module.Classes);
        Assert.Equal("Next", Assert.Single(type.Methods).Name);
        Assert.Single(type.Methods[0].Body);
        Assert.Equal(1, type.Constructors.Count + type.Properties.Count + type.Indexers.Count + type.Events.Count);
        var reparse = Parse(new CalorEmitter().Emit(module));
        Assert.Equal("Next", Assert.Single(Assert.Single(reparse.Classes).Methods).Name);
    }

    [Theory]
    [InlineData("checked")]
    [InlineData("unchecked")]
    public void ModuleOverflowHeadersAcceptEndOfFileWithoutANewline(string policy)
    {
        var module = Parse($"module Demo overflow {policy}");
        Assert.Equal(policy, module.Attributes["overflow"]);
        Assert.Equal(policy, Parse(new CalorEmitter().Emit(module)).Attributes["overflow"]);
    }

    [Theory]
    [InlineData("module Demo overflow bogus", "'checked' or 'unchecked'", 1, 22)]
    [InlineData("module Demo\n  func Run bogus () → void", "function modifier", 2, 12)]
    [InlineData("module Demo Outer bogus", "[block]", 1, 19)]
    public void InvalidNativeHeaderFieldsReportTheActualField(
        string source, string guidance, int line, int column)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var diagnostic = Assert.Single(diagnostics.Errors.Where(error => error.Message.Contains(guidance)));
        Assert.Contains("bogus", diagnostic.Message);
        Assert.Equal(line, diagnostic.Span.Line);
        Assert.Equal(column, diagnostic.Span.Column);
        Assert.Equal("bogus", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
    }

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
                  !
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
        var module = Parse($"module Types\n  func Copy ({type}:value) → {type}\n    !\n    return value");
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparsed = Parse(emitted);
        Assert.Equal(type, reparsed.Functions[0].Parameters[0].TypeName);
        Assert.Equal(type, reparsed.Functions[0].Output!.TypeName);
    }

    [Fact]
    public void GenericConstraintsRemainNativeAndKeepConstructorAndReferenceTypeRequirements()
    {
        var module = Parse("""
            module Generics
              class Holder<T>
                where T : class, new()
                field T:Value
              func Copy<T> (T:value) → T
                where T : class, new()
                !
                return value
            """);
        var expected = new[] { TypeConstraintKind.Class, TypeConstraintKind.New };
        Assert.Equal(expected, module.Classes.Single().TypeParameters.Single().Constraints.Select(constraint => constraint.Kind));
        Assert.Equal(expected, module.Functions.Single().TypeParameters.Single().Constraints.Select(constraint => constraint.Kind));
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted);
        Assert.Equal(expected, reparse.Functions.Single().TypeParameters.Single().Constraints.Select(constraint => constraint.Kind));
    }

    [Fact]
    public void CanonicalProgramUsesTheDefaultBindingAndValidationPipeline()
    {
        var result = Program.Compile("""
            module BoundApi
              func Copy (int:value) → int
                !
                let int:copy = value
                var next = copy + 1
                return next
            """);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal("int", result.Ast!.Functions.Single().Parameters.Single().TypeName);
        Assert.Contains("int Copy(int value)", result.GeneratedCode);
    }

    [Fact]
    public void ConstructorSignaturesAndBaseCallsRetainNativeMetadata()
    {
        var module = Parse("""
            module Constructors
              class Parent
                field int:Value protected
                ctor (int:value)
                  set Value value
              class Child
                extends Parent
                ctor internal (int:value)
                  base(value)
                  set Value value + 1
            """);
        var parent = module.Classes[0];
        var child = module.Classes[1];
        Assert.Equal(Visibility.Public, parent.Constructors.Single().Visibility);
        Assert.Equal(Visibility.Internal, child.Constructors.Single().Visibility);
        Assert.Equal("Parent", child.BaseClass);
        Assert.True(child.Constructors.Single().Initializer!.IsBaseCall);
        Assert.Single(child.Constructors.Single().Initializer!.Arguments);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparsed = Parse(emitted).Classes[1].Constructors.Single();
        Assert.Equal(Visibility.Internal, reparsed.Visibility);
        Assert.True(reparsed.Initializer!.IsBaseCall);
        Assert.Single(reparsed.Body);
    }

    [Fact]
    public void CanonicalTypedMembersRetainVisibilityDefaultsAndRestrictedAccessors()
    {
        var module = Parse("""
            module Members
              class Box
                field int:_value private readonly = 7
                field int:PublicValue = 1
                property string?:Name
                  get
                  set private
                property int:Value
                  get
                    return _value
            """);
        var type = module.Classes.Single();
        Assert.Equal(Visibility.Private, type.Fields[0].Visibility);
        Assert.True(type.Fields[0].Modifiers.HasFlag(MethodModifiers.Readonly));
        Assert.Equal(Visibility.Public, type.Fields[1].Visibility);
        Assert.Equal("string?", type.Properties[0].TypeName);
        Assert.Equal(Visibility.Private, type.Properties[0].Setter!.Visibility);
        Assert.Single(type.Properties[1].Getter!.Body);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted).Classes.Single();
        Assert.Equal(type.Fields.Select(field => field.Visibility), reparse.Fields.Select(field => field.Visibility));
        Assert.Equal(Visibility.Private, reparse.Properties[0].Setter!.Visibility);
        Assert.NotNull(reparse.Fields[0].DefaultValue);
        Assert.Single(reparse.Properties[1].Getter!.Body);
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
        _ = new Parser(new Lexer($"module Bad\n  class C\n    func F () → int\n      !\n      return 1\n      {clause}", diagnostics)
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
                !
                let int:copy = value
                return copy
              func Hidden private () → void
                return
              class Box
                func Echo (int:value) → int
                  !
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
    [InlineData("i32")]
    [InlineData("?string")]
    [InlineData("[int]")]
    [InlineData("Dictionary<string,i32>")]
    [InlineData("Func<str>")]
    public void RetiredTypeAliasesAndPrefixFormsAreExplicitErrors(string type)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  func F ({type}:value) → int\n    !\n    return 1", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
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
