using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public sealed class CanonicalEnumDelegateEventTests
{
    [Theory]
    [InlineData("", "Color.Red", "red")]
    [InlineData("", "Color.Blue", "blue")]
    [InlineData("", "Shade.Red", "red")]
    [InlineData(" Demo", "Color.Red", "red")]
    [InlineData(" Demo", "Demo.Color.Red", "red")]
    [InlineData(" Demo", "Shade.Blue", "blue")]
    public void NativeEnumConstantsSelectActualStaticExtensionOverload(
        string ns, string argument, string expected)
    {
        var source = $$"""
            module NativeEnums{{ns}}
              using Shade = {{(ns.Length == 0 ? "" : "Demo.")}}Color
              enum Color byte
                Red = 7
                Blue = 9
              enum OtherColor byte
                Red = 11
              module ColorExtensions
                func Pick (this Color:value) → string
                  !
                  return value == Color.Red ? "red" : "blue"
                func Pick (OtherColor:value) → string
                  ! cw
                  return "other"
                func Pick (int:value) → string
                  ! alloc
                  return "integer"
              func Main () → string
                !
                return ColorExtensions.Pick(value: {{argument}})
            """;
        var ast = Parse(source);
        var bound = BindNativeEnums(ast);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        var constant = Assert.IsType<BoundFieldAccessExpression>(Assert.Single(call.Arguments));
        var owner = Assert.IsType<BoundTypeReferenceExpression>(constant.Target);
        var enumType = Assert.IsType<NominalBoundType>(constant.Type);
        var declaration = ast.Enums.Single(type => type.Name == "Color");
        Assert.True(owner.ResolvedType!.IsEnum);
        Assert.Same(owner.ResolvedType, enumType.Declaration);
        Assert.Same(bound.SymbolsById[owner.ResolvedType.Id], enumType.Declaration);
        Assert.Same(bound.SymbolsById[constant.ResolvedSymbolId!.Value], constant.ResolvedField);
        Assert.True(constant.ResolvedField!.IsEnumMember);
        Assert.False(constant.ResolvedField.IsMutable);
        Assert.True(constant.ResolvedField.IsStatic);
        Assert.Null(constant.ResolvedMetadataMember);
        Assert.Null(enumType.RoslynSymbol);
        Assert.Equal(declaration.IdentifierSpan, owner.ResolvedType.DeclarationSpan);
        Assert.Equal("Color", Slice(source, owner.ResolvedType.DeclarationSpan));
        Assert.Equal(constant.FieldName, Slice(source, constant.ResolvedField.DeclarationSpan));
        Assert.Equal(argument, Slice(source, constant.Span));
        Assert.Equal(constant.FieldName, Slice(source, constant.FieldNameSpan));
        Assert.Equal("Pick", Slice(source, call.CalleeSpan));
        Assert.Equal("Pick", Slice(source, call.ResolvedSymbol!.DeclarationSpan));
        var selected = Assert.Single(call.SelectedOverloadMatches);
        Assert.Same(call.ResolvedSymbol, selected.Function);
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name.EndsWith(".Pick", StringComparison.Ordinal)
            && function.Symbol.Parameters.Single().TypeName == "Color").Symbol, selected.Function);
        Assert.Equal("global::" + enumType.Declaration!.QualifiedName, Assert.Single(selected.Arguments).ParameterType);
        Assert.Equal(0, Assert.Single(selected.Arguments).ArgumentIndex);
        Assert.Equal(0, Assert.Single(selected.Arguments).ParameterIndex);
        Assert.Equal("value", Assert.Single(call.ArgumentNames!));
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.False(call.HasOpaqueOverloads);
        var graph = CallGraphAnalysis.Build(ast);
        Assert.True(graph.IsBoundResolutionComplete);
        Assert.Equal(constant.ResolvedField.Id,
            graph.SourceEnumConstant(ast.Functions.Single().Id, constant.Span)!.Id);
        Assert.Null(graph.MetadataEnumConstant(ast.Functions.Single().Id, constant.Span));
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code is
            DiagnosticCode.UnknownExternalCall or DiagnosticCode.AssumedEffects);
        Execute(compiled.GeneratedCode, assembly => Assert.Equal(expected, assembly.GetType(
            ns.Length == 0 ? "GlobalModule" : "Demo.DemoModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

    [Theory]
    [InlineData("OtherColor.Red")]
    [InlineData("1")]
    [InlineData("Color.Missing")]
    public void WrongEnumIntegerAndUnknownMemberNeverSelectTheEnumContract(string argument)
    {
        var source = $$"""
            module RejectedEnums
              enum Color
                Red
              enum OtherColor
                Red
              module Extensions
                func Pick (this Color:value) → int
                  !
                  return 7
              func Main () → int
                !
                return Extensions.Pick({{argument}})
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == (argument == "Color.Missing"
            ? DiagnosticCode.UndefinedReference : DiagnosticCode.NoMatchingOverload));
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
    }

    [Fact]
    public void SourceEnumOverloadChargesItsActualDeclaredEffects()
    {
        const string source = """
            module EffectfulEnums
              enum Quiet
                Value
              enum Loud
                Value
              module Extensions
                func Pick (this Quiet:value) → int
                  !
                  return 1
                func Pick (this Loud:value) → int
                  ! cw
                  System.Console.WriteLine("selected")
                  return 2
              func Main () → int
                !
                return Extensions.Pick(Loud.Value)
            """;
        var bound = BindNativeEnums(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        var selected = bound.Functions.Single(function => function.Symbol == call.ResolvedSymbol);
        Assert.Equal("Loud", selected.Symbol.Parameters.Single().TypeName);
        Assert.Equal(new[] { "io:console_write" }, selected.DeclaredEffects);
        var rejected = Program.Compile(source);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        var admitted = Program.Compile(source.Replace("func Main () → int\n    !",
            "func Main () → int\n    ! cw", StringComparison.Ordinal));
        Assert.False(admitted.HasErrors, string.Join("\n", admitted.Diagnostics));
        Execute(admitted.GeneratedCode, assembly =>
            Assert.Equal(2, assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

    [Fact]
    public void NestedSameNamedEnumAndAliasKeepTheirOwnIdentity()
    {
        const string source = """
            module EnumScopes
              using Shade = First.Color
              module First
                enum Color
                  Red = 1
                func Read (Color:value) → int
                  !
                  return value == Color.Red ? 1 : 0
              module Second
                enum Color
                  Red = 2
                func Read (Color:value) → int
                  !
                  return value == Color.Red ? 2 : 0
              func Main () → int
                !
                return First.Read(Shade.Red) + Second.Read(Second.Color.Red)
            """;
        var bound = BindNativeEnums(Parse(source));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>().ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(new[] { "First.Read", "Second.Read" }, calls.Select(call => call.ResolvedSymbol!.Name));
        var types = calls.Select(call => Assert.IsType<NominalBoundType>(call.Arguments.Single().Type)).ToArray();
        Assert.NotEqual(types[0].Declaration!.Id, types[1].Declaration!.Id);
        Assert.Equal(new[] { "First.Color", "Second.Color" }, types.Select(type => type.Declaration!.QualifiedName));
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
            Assert.Equal(3, assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
        var wrong = source.Replace("Second.Read(Second.Color.Red)", "Second.Read(Shade.Red)", StringComparison.Ordinal);
        Assert.Contains(Program.Compile(wrong).Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void SameNamedMetadataEnumDoesNotAcquireSourceEnumIdentity()
    {
        const string source = """
            module EnumOrigins
              enum ConsoleColor
                Red = 1
              module Extensions
                func Pick (ConsoleColor:value) → int
                  !
                  return 1
                func Pick (System.ConsoleColor:value) → int
                  ! cw
                  return 2
              func Native () → int
                !
                return Extensions.Pick(ConsoleColor.Red)
              func Metadata () → int
                ! cw
                return Extensions.Pick(System.ConsoleColor.Red)
            """;
        var ast = Parse(source);
        var bound = BindNativeEnums(ast);
        var native = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Native")).OfType<BoundCallExpression>());
        var metadata = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Metadata")).OfType<BoundCallExpression>());
        Assert.NotSame(native.ResolvedSymbol, metadata.ResolvedSymbol);
        Assert.NotNull(Assert.IsType<NominalBoundType>(native.Arguments.Single().Type).Declaration);
        Assert.Null(Assert.IsType<NominalBoundType>(metadata.Arguments.Single().Type).Declaration);
        Assert.Equal(TypeKind.Enum, Assert.IsType<NominalBoundType>(metadata.Arguments.Single().Type).RoslynSymbol!.TypeKind);
        Assert.Equal("ConsoleColor", native.ResolvedSymbol!.Parameters.Single().TypeName);
        Assert.Equal("System.ConsoleColor", metadata.ResolvedSymbol!.Parameters.Single().TypeName);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
        {
            var type = assembly.GetType("GlobalModule")!;
            Assert.Equal(1, type.GetMethod("Native")!.Invoke(null, null));
            Assert.Equal(2, type.GetMethod("Metadata")!.Invoke(null, null));
        });
    }

    [Theory]
    [InlineData("Color.Red.Pick()")]
    [InlineData("color.Pick()")]
    public void ReceiverStyleExtensionLookupRemainsUnresolved(string expression)
    {
        var source = $$"""
            module ReceiverLimit
              enum Color
                Red
              module Extensions
                func Pick (this Color:value) → int
                  !
                  return 7
              func Main (Color:color) → int
                !
                return {{expression}}
            """;
        var bound = BindNativeEnums(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Contains(Program.Compile(source).Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("Color.Missing", "")]
    [InlineData("Color.Red", "  enum Color\n    Red\n")]
    [InlineData("Color.Red", "  using Color = System.ConsoleColor\n")]
    public void MissingAmbiguousAndAliasConflictedOwnersHaveNoNativeCallAuthority(string value, string extra)
    {
        var source = $$"""
            module UnknownEnums
            {{extra}}  enum Color
                Red
              module Extensions
                func Pick (Color:value) → int
                  !
                  return 7
              func Main () → int
                !
                return Extensions.Pick({{value}})
            """;
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source);
        var bound = new Binder(diagnostics).Bind(ast);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.False(CallGraphAnalysis.Build(ast).IsResolvedEnumConstant(ast.Functions.Single().Id, call.Arguments.Single().Span));
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("Color", "Color.Red")]
    [InlineData("ColorExtensions", "ColorExtensions.Pick(Color.Red)")]
    public void ValueShadowingDoesNotGrantAnEnumOrStaticContainerIdentity(string parameter, string expression)
    {
        var source = $$"""
            module ShadowedEnums
              enum Color
                Red
              module ColorExtensions
                func Pick (this Color:value) → int
                  !
                  return 7
              class Value
                func Pick (Color:value) → int
                  ! cw
                  System.Console.WriteLine("instance")
                  return 3
              func Main (Value:{{parameter}}) → int
                !
                return {{(parameter == "Color" ? "ColorExtensions.Pick(" + expression + ")" : expression)}}
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.NotEqual("ColorExtensions.Pick", call.ResolvedSymbol?.Name);
        if (parameter == "ColorExtensions")
        {
            Assert.Equal("Value.Pick", call.ResolvedSymbol!.Name);
            Assert.Equal("io:console_write", Assert.Single(bound.Functions.Single(function =>
                function.Symbol == call.ResolvedSymbol).DeclaredEffects));
        }
        Assert.True(Program.Compile(source).HasErrors);
        if (parameter == "ColorExtensions")
        {
            var admitted = Program.Compile(source.Replace("func Main (Value:ColorExtensions) → int\n    !",
                "func Main (Value:ColorExtensions) → int\n    ! cw", StringComparison.Ordinal));
            Assert.False(admitted.HasErrors, string.Join("\n", admitted.Diagnostics));
            Execute(admitted.GeneratedCode, assembly =>
                Assert.Equal(3, assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null,
                    [Activator.CreateInstance(assembly.GetType("Value")!)])));
        }
    }

    [Fact]
    public void InaccessibleNestedEnumConstantCannotSelectAnOtherwisePublicCallable()
    {
        const string source = """
            module PrivateEnums
              class Owner
                enum Color private
                  Red
                func Read static (Color:value) → int
                  !
                  return 7
              func Main () → int
                !
                return Owner.Read(Owner.Color.Red)
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void EnumArgumentsPreserveGenericNamedAndByReferenceMappings()
    {
        const string source = """
            module EnumMappings
              enum Color
                Red = 7
                Blue = 9
              module Extensions
                func Choose<T> (Color:value, T:other) → T
                  !
                  return other
                func Update (ref Color:value, in int:increment, out int:answer) → void
                  !
                  set value Color.Blue
                  set answer increment + 1
              func Main () → int
                !
                var Color:value = Color.Red
                let int:increment = 3
                var int:answer = 0
                Extensions.Update(answer: out answer, increment: in increment, value: ref value)
                return Extensions.Choose<int>(other: answer, value: value)
            """;
        var ast = Parse(source);
        var bound = BindNativeEnums(ast);
        var main = bound.Functions.Single(function => function.Symbol.Name == "Main");
        var update = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(main).OfType<BoundCallExpression>()
            .Where(call => call.ResolvedSymbol?.Name == "Extensions.Update"));
        Assert.Equal(new[] { "out", "in", "ref" }, update.ArgumentModifiers);
        Assert.Equal(new[] { 2, 1, 0 }, Assert.Single(update.SelectedOverloadMatches).Arguments
            .Select(mapping => mapping.ParameterIndex));
        var choose = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(main).OfType<BoundCallExpression>()
            .Where(call => call.ResolvedSymbol?.Name == "Extensions.Choose"));
        Assert.Equal(new[] { 1, 0 }, Assert.Single(choose.SelectedOverloadMatches).Arguments
            .Select(mapping => mapping.ParameterIndex));
        Assert.Equal("INT", TypeIdentity.Canonicalize(choose.Type.DisplayString));
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
            Assert.Equal(4, assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

    [Fact]
    public void AmbiguousEnumOverloadsDoNotSelectAContract()
    {
        const string source = """
            module AmbiguousEnums
              enum Color
                Red
              module Extensions
                func Pick<T> (Color:left, T:right) → int
                  !
                  return 1
                func Pick<T> (T:left, Color:right) → int
                  ! cw
                  return 2
              func Main () → int
                !
                return Extensions.Pick(Color.Red, Color.Red)
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void PreservedOverloadPreventsStaticInstanceAndInheritedEnumCallAuthority(bool isStatic, bool derived)
    {
        var typeName = derived ? "Derived" : "Extensions";
        var parameter = isStatic ? "" : typeName + " target";
        var original = $$"""
            public enum Color { Red }
            public {{(isStatic ? "static " : "")}}class Extensions
            {
                public {{(isStatic ? "static " : "")}}int Pick(Color value) => 7;
                public {{(isStatic ? "static " : "")}}int Pick(scoped in Color value) => 9;
            }
            {{(derived ? "public class Derived : Extensions { }" : "")}}
            public static class Entry
            {
                public static int Main({{parameter}}) => {{(isStatic ? "Extensions" : "target")}}.Pick(Color.Red);
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(original);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        Assert.NotNull(conversion.CalorSource);
        Assert.Contains("raw ", conversion.CalorSource);
        var header = $"func Main ({(isStatic ? "" : typeName + ":target")}) → int\n";
        Assert.Contains(header, conversion.CalorSource);
        var source = conversion.CalorSource.Replace(header, header + "      !\n", StringComparison.Ordinal);
        var ast = Parse(source);
        var bound = BindNativeEnums(ast);
        var main = bound.Functions.Single(function => function.Symbol.Name.EndsWith(".Main", StringComparison.Ordinal));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(main).OfType<BoundCallExpression>());
        Assert.True(call.HasOpaqueOverloads);
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Contains(Program.Compile(source).Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Execute(original, assembly =>
            Assert.Equal(7, assembly.GetType("Entry")!.GetMethod("Main")!.Invoke(null,
                isStatic ? null : [Activator.CreateInstance(assembly.GetType(typeName)!)])));
    }

    [Theory]
    [InlineData("Extensions.Echo(Color.Red)")]
    [InlineData("Extensions.Current()")]
    [InlineData("Extensions.Identity(Color.Red)")]
    [InlineData("Extensions.Identity<Color>(Color.Red)")]
    [InlineData("(Color)0")]
    public void EnumValueProducersRetainTheDeclaringContext(string argument)
    {
        var source = $$"""
            module EnumValues Demo
              enum Color
                Red
              module Extensions
                func Identity<T> (T:value) → T
                  !
                  return value
                func Echo (Color:value) → Color
                  !
                  return value
                func Current () → Color
                  !
                  return Color.Red
                func Pick (Color:value) → int
                  !
                  return 7
              func Main () → int
                !
                return Extensions.Pick({{argument}})
            """;
        var bound = BindNativeEnums(Parse(source));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>().ToArray();
        Assert.All(calls, call => Assert.NotNull(call.ResolvedSymbol));
        var argumentType = Assert.IsType<NominalBoundType>(calls.Single(call =>
            call.ResolvedSymbol!.Name.EndsWith(".Pick", StringComparison.Ordinal)).Arguments.Single().Type);
        Assert.Equal("Demo.Color", argumentType.Declaration!.QualifiedName);
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Execute(result.GeneratedCode, assembly =>
            Assert.Equal(7, assembly.GetType("Demo.DemoModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

    [Fact]
    public void EscapedEnumMemberKeepsSpansButStillHitsAstTypeCheckerLimit()
    {
        const string source = """
            module EscapedEnum
              enum Color
                @event = 7
              module Extensions
                func Pick (Color:value) → int
                  !
                  return 7
              func Main () → int
                !
                return Extensions.Pick(Color.@event)
            """;
        var bound = BindNativeEnums(Parse(source));
        var constant = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundFieldAccessExpression>());
        Assert.Equal("@event", Slice(source, constant.ResolvedField!.DeclarationSpan));
        Assert.Equal("@event", Slice(source, constant.FieldNameSpan));
        var compiled = Program.Compile(source);
        Assert.Equal(DiagnosticCode.UndefinedReference, Assert.Single(compiled.Diagnostics.Where(diagnostic =>
            diagnostic.Severity == Calor.Compiler.Diagnostics.DiagnosticSeverity.Error)).Code);
    }

    [Fact]
    public void DuplicateMembersKeepDistinctSymbolsWithoutConstantOrCallAuthority()
    {
        const string source = """
            module DuplicateMembers
              enum Color
                Red = 1
                Red = 2
              module Extensions
                func Pick (Color:value) → int
                  !
                  return 7
              func Main () → int
                !
                return Extensions.Pick(Color.Red)
            """;
        var ast = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        var members = bound.SymbolsById.Values.OfType<VariableSymbol>()
            .Where(symbol => symbol.IsEnumMember).ToArray();
        Assert.Equal(2, members.Length);
        Assert.NotEqual(members[0].Id, members[1].Id);
        Assert.NotEqual(members[0].DeclarationSpan, members[1].DeclarationSpan);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        var member = Assert.IsType<BoundFieldAccessExpression>(Assert.Single(call.Arguments));
        Assert.Null(member.ResolvedField);
        Assert.False(CallGraphAnalysis.Build(ast).IsResolvedEnumConstant(ast.Functions.Single().Id, member.Span));
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void EnumValuesKeepFieldDeclarationIdentityThroughAnotherScope()
    {
        const string source = """
            module EnumFields Demo
              class Storage
                enum Color
                  Red
                field Color:Value
                func Read () → int
                  !
                  return Extensions.Pick(this.Value)
              module Extensions
                func Pick (Storage.Color:value) → int
                  !
                  return 7
                func Pick (int:value) → int
                  ! cw
                  return 9
              func Main () → int
                ! alloc
                let Storage:item = new Storage()
                return item.Read()
            """;
        var bound = BindNativeEnums(Parse(source));
        var field = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Demo.Storage.Read")).OfType<BoundFieldAccessExpression>());
        Assert.Equal("Demo.Storage.Color", Assert.IsType<NominalBoundType>(field.Type).Declaration!.QualifiedName);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
            Assert.Equal(7, assembly.GetType("Demo.DemoModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

    [Fact]
    public void StaticEnumArgumentsEvaluateOnceInSourceOrderDespiteNamedMapping()
    {
        const string source = """
            module EnumOrder
              enum Color
                Red
              module Extensions
                field int:Count
                func Next () → Color
                  ! mut
                  set Count Count * 10 + 1
                  return Color.Red
                func Argument () → int
                  ! mut
                  set Count Count * 10 + 2
                  return 7
                func Pick (this Color:value, int:other) → int
                  !
                  return other
                func Observe () → int
                  ! mut
                  set Count 0
                  let int:answer = Extensions.Pick(other: Argument(), value: Next())
                  return Count * 10 + answer
              func Main () → int
                ! mut
                return Extensions.Observe()
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
            Assert.Equal(217, assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

    [Theory]
    [InlineData("String", "\"not an enum\"")]
    [InlineData("Int32", "1")]
    public void QualifiedSourceEnumIdentityDoesNotCollapseToABuiltinAlias(string name, string wrong)
    {
        var source = $$"""
            module EnumAliases
              enum {{name}}
                Red
              module Extensions
                func Pick (global::{{name}}:value) → int
                  !
                  return 7
              func Main () → int
                !
                return Extensions.Pick({{name}}.Red)
            """;
        var ast = Parse(source);
        var bound = BindNativeEnums(ast);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        var constant = Assert.IsType<BoundFieldAccessExpression>(call.Arguments.Single());
        Assert.Equal(name, Assert.IsType<NominalBoundType>(constant.Type).Declaration!.Name);
        Assert.Equal("global::" + name, Assert.Single(Assert.Single(call.SelectedOverloadMatches).Arguments).ParameterType);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
            Assert.Equal(7, assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
        var rejected = source.Replace(name + ".Red", wrong, StringComparison.Ordinal);
        Assert.Contains(Program.Compile(rejected).Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void GlobalQualifierInAnEnumCallArgumentRemainsUnadmitted()
    {
        const string source = """
            module GlobalEnum
              enum Color
                Red
              module Extensions
                func Pick (Color:value) → int
                  !
                  return 7
              func Main (int:Color) → int
                !
                return Extensions.Pick(global::Color.Red)
            """;
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken
            && diagnostic.Message.Contains("Colon", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericTypeParameterShadowDoesNotAcquireSourceEnumIdentity(bool genericMethod)
    {
        var source = $$"""
            module GenericEnumShadow
              enum Color
                Red
              module Extensions
                func Pick (Color:value) → int
                  !
                  return 7
              class Worker{{(genericMethod ? "" : "<Color>")}}
                func Forward{{(genericMethod ? "<Color>" : "")}} (Color:value) → int
                  !
                  return Extensions.Pick(value)
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name.Contains(".Forward", StringComparison.Ordinal))).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(Assert.IsType<NominalBoundType>(call.Arguments.Single().Type).Declaration);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        Assert.True(Program.Compile(source).HasErrors);
    }

    private static BoundModule BindNativeEnums(ModuleNode ast)
    {
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return bound;
    }

    private static string Slice(string source, TextSpan span) => source.Substring(span.Start, span.Length);

    private const string Source = """
        module Retained
          enum Mode byte
            None = 0
            @event = 7
          delegate Handler (int:value) → void
          class Source
            field int:Count public = 0
            event Handler:Changed
              add
                set Count (Count + 1)
              remove
                set Count (Count + 10)
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeDeclarationsPreserveClrSignaturesAndAccessorExecution(bool roundtrip)
    {
        var source = roundtrip ? new CalorEmitter().Emit(Parse(Source)) : Source;
        Assert.DoesNotContain('§', source);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
        {
            var mode = assembly.GetType("Mode", throwOnError: true)!;
            Assert.Equal(typeof(byte), Enum.GetUnderlyingType(mode));
            Assert.Equal((byte)7, Convert.ChangeType(Enum.Parse(mode, "event"), typeof(byte)));
            var handlerType = assembly.GetType("Handler", throwOnError: true)!;
            Assert.Equal(typeof(void), handlerType.GetMethod("Invoke")!.ReturnType);
            Assert.Equal(typeof(int), handlerType.GetMethod("Invoke")!.GetParameters().Single().ParameterType);
            var type = assembly.GetType("Source", throwOnError: true)!;
            var instance = Activator.CreateInstance(type)!;
            var handler = Delegate.CreateDelegate(handlerType, typeof(CanonicalEnumDelegateEventTests)
                .GetMethod(nameof(Observe), BindingFlags.Static | BindingFlags.NonPublic)!);
            var changed = type.GetEvent("Changed")!;
            changed.AddEventHandler(instance, handler);
            changed.RemoveEventHandler(instance, handler);
            Assert.Equal(11, type.GetField("Count")!.GetValue(instance));
        });
    }

    [Theory]
    [InlineData("public enum Mode : byte { None = 0, @event = 7 }")]
    [InlineData("public delegate int Transform(int value);")]
    [InlineData("public class Source { private int count; public event System.Action Changed { add { count += 1; } remove { count += 10; } } public int Count => count; }")]
    [InlineData("public class Source { public event System.Action Changed; }")]
    [InlineData("public class Source { protected internal event System.Action Changed; }")]
    [InlineData("public class Source { private protected event System.Action Changed; }")]
    public void RealConverterEmitsNativeRepresentableDeclarations(string original)
    {
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.DoesNotContain("raw ", converted.CalorSource!);
        Assert.DoesNotContain('§', converted.CalorSource!);
        var compiled = Program.Compile(converted.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        foreach (var source in new[] { original, compiled.GeneratedCode })
            Execute(source, assembly =>
            {
                if (!original.Contains("count += 1", StringComparison.Ordinal)) return;
                var type = assembly.GetType("Source", throwOnError: true)!;
                var instance = Activator.CreateInstance(type)!;
                Action handler = () => { };
                type.GetEvent("Changed")!.AddEventHandler(instance, handler);
                type.GetEvent("Changed")!.RemoveEventHandler(instance, handler);
                Assert.Equal(11, type.GetProperty("Count")!.GetValue(instance));
            });
    }

    [Theory]
    [InlineData("delegate D private () → void")]
    [InlineData("delegate D static () → void")]
    [InlineData("class Source\n    event System.Action:Changed abstract")]
    [InlineData("class Source\n    event System.Action:Changed\n      add\n        return")]
    [InlineData("class Source\n    event System.Action:Changed\n      add\n        return\n      add\n        return\n      remove\n        return")]
    public void UnrepresentedOrIncompleteNativeDeclarationsDiagnose(string declaration)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer("module Invalid\n  " + declaration, diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static void Observe(int value) { }

    [Theory]
    [InlineData("")]
    [InlineData("cw")]
    [InlineData("mut,throw")]
    public void IndentedDelegateEffectRowsRoundTripAndApplyToCalls(string effects)
    {
        var source = $"module DelegateEffects\n  delegate Handler (int:value) → void\n    ! {effects}\n"
            + $"  func Invoke (Handler:handler ! {effects}) → void\n    ! {effects}\n    handler(7)";
        var parsed = Parse(source);
        var declared = parsed.Delegates.Single().Effects!;
        var emitted = new CalorEmitter().Emit(parsed);
        Assert.DoesNotContain('§', emitted);
        Assert.Equal(declared.Effects, Parse(emitted).Delegates.Single().Effects!.Effects);
        foreach (var input in new[] { source, emitted })
        {
            var compiled = Program.Compile(input, filePath: null, options: new CompilationOptions { EnforceEffects = true });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Execute(compiled.GeneratedCode, assembly =>
            {
                var receiver = new Receiver();
                var handler = Delegate.CreateDelegate(assembly.GetType("Handler", throwOnError: true)!,
                    receiver, typeof(Receiver).GetMethod(nameof(Receiver.Handle))!);
                assembly.GetType("GlobalModule", throwOnError: true)!.GetMethod("Invoke")!.Invoke(null, [handler]);
                Assert.Equal(7, receiver.Value);
            });
        }
    }

    [Fact]
    public void EffectfulDelegateInvocationCannotBeLaunderedThroughAPureCaller()
    {
        const string source = """
            module DelegateEffects
              delegate Handler (int:value) → void
                ! cw
              func Invoke (Handler:handler ! cw) → void
                !
                handler(7)
            """;
        var compiled = Program.Compile(source, filePath: null, options: new CompilationOptions { EnforceEffects = true });
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
    }

    private sealed class Receiver
    {
        public int Value { get; private set; }
        public void Handle(int value) => Value = value;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsafeCallableModifierDoesNotTerminateTheNativeHeader(bool roundtrip)
    {
        const string input = """
            module UnsafeHeaders
              class Native
                func Change private unsafe (in int:value) → int
                  return value + 1
            """;
        var source = roundtrip ? new CalorEmitter().Emit(Parse(input)) : input;
        var method = Parse(source).Classes.Single().Methods.Single();
        Assert.True(method.IsUnsafe);
        Assert.Equal(Visibility.Private, method.Visibility);
        Assert.Equal(Calor.Compiler.Ast.ParameterModifier.In, method.Parameters.Single().Modifier);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
        {
            var type = assembly.GetType("Native", throwOnError: true)!;
            var change = type.GetMethod("Change", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.True(change.GetParameters().Single().IsIn);
            Assert.Equal(42, change.Invoke(Activator.CreateInstance(type), [41]));
        }, allowUnsafe: true);
    }

    [Fact]
    public void StaticEventConversionStillPreservesUnsupportedCSharpVerbatim()
    {
        const string original = "public class Source { public static event System.Action Changed; }";
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.Contains("raw ", converted.CalorSource!);
        Assert.DoesNotContain("post-validation-fallback", converted.CalorSource!);
        var compiled = Program.Compile(converted.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
            Assert.True(assembly.GetType("Source", throwOnError: true)!.GetEvent("Changed")!.AddMethod!.IsStatic));
    }

    [Theory]
    [InlineData("module Source", "")]
    [InlineData("class Source", " static")]
    public void NamedModulesAndExplicitStaticEventsKeepStaticClrAccessors(string owner, string modifier)
    {
        var original = $"""
            module StaticEvents
              {owner}
                field int:Count public static = 0
                event System.Action:Changed{modifier}
                  add
                    set Count (Count + 1)
                  remove
                    set Count (Count + 10)
            """;
        foreach (var source in new[] { original, new CalorEmitter().Emit(Parse(original)) })
        {
            Assert.True(Parse(source).Classes.Single().Events.Single().IsStatic);
            var compiled = Program.Compile(source);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Execute(compiled.GeneratedCode, assembly =>
            {
                var type = assembly.GetType("Source", throwOnError: true)!;
                var changed = type.GetEvent("Changed")!;
                Assert.True(changed.AddMethod!.IsStatic);
                Assert.True(changed.RemoveMethod!.IsStatic);
                Action handler = () => { };
                changed.AddEventHandler(null, handler);
                changed.RemoveEventHandler(null, handler);
                Assert.Equal(11, type.GetField("Count")!.GetValue(null));
            });
        }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static void Execute(string source, Action<Assembly> observe, bool allowUnsafe = false)
    {
        var compilation = CSharpCompilation.Create("NativeEnumDelegateEvents",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("NativeEnumDelegateEvents", isCollectible: true);
        try { observe(context.LoadFromStream(stream)); }
        finally { context.Unload(); }
    }
}
