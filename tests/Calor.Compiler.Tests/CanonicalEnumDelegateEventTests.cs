using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;
using BoundNullability = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public sealed class CanonicalEnumDelegateEventTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSourceExtensionAllowsItsDeclaredNullableOrObliviousReceiverWithoutAnInstanceGuard(bool oblivious)
    {
        var type = oblivious ? "string" : "string?";
        var source = $$"""
            module SourceNullReceivers{{(oblivious ? " block overflow unchecked nullable oblivious" : "")}}
              module StringExtensions
                func IsNullOrEmpty (this {{type}}:s) → bool
                  return ((s == null) || (s.Length == 0))
              func Use ({{type}}:s) → int
                !
                let empty = s.IsNullOrEmpty()
                if empty
                  return 0
                return 1
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.True(call.IsSourceExtensionCall);
        Assert.Equal("StringExtensions.IsNullOrEmpty", call.ResolvedSymbol?.Name);
        Assert.Equal(oblivious ? BoundNullability.Oblivious : BoundNullability.Annotated,
            Assert.IsType<NominalBoundType>(call.Arguments[0].Type).NullableAnnotation);
        Assert.Null(call.ResolvedMetadataMethod);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Contains("s.IsNullOrEmpty()", compiled.GeneratedCode);
        Assert.DoesNotContain("ThrowIfNull", compiled.GeneratedCode);
        Execute(compiled.GeneratedCode, assembly =>
        {
            var use = assembly.GetType("GlobalModule")!.GetMethod("Use")!;
            Assert.Equal(0, use.Invoke(null, [null]));
            Assert.Equal(0, use.Invoke(null, [""]));
            Assert.Equal(1, use.Invoke(null, ["word"]));
        });
    }

    [Fact]
    public void NativeSourceExtensionKeepsReducedSyntaxWhenStaticSpellingWouldSelectAnotherOverload()
    {
        const string source = """
            module SourceStaticOverloads
              module Extensions
                func Pick (this object:value) → int
                  !
                  return 1
                func Pick (string:value) → int
                  ! cw
                  System.Console.WriteLine("wrong static overload")
                  return 2
              func Main (string:value) → int
                !
                return value.Pick()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.True(call.IsSourceExtensionCall);
        Assert.Equal("object", call.ResolvedSymbol!.Parameters[0].TypeName);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Contains("return value.Pick();", compiled.GeneratedCode);
        Assert.DoesNotContain("return global::Extensions.Pick(", compiled.GeneratedCode);
        Execute(compiled.GeneratedCode, assembly => Assert.Equal(1,
            assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, ["value"])));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void NativeSourceExtensionChargesAndEvaluatesReceiverAndNamedArgumentsOnceInOrder(
        bool selectedEffect, bool receiverEffect, bool argumentEffect)
    {
        var source = $$"""
            module SourceReceiverOrder
              module Extensions
                func Pick (this string:value, int:first, int:second) → int
                  !{{(selectedEffect ? " cw" : "")}}
                  {{(selectedEffect ? "System.Console.WriteLine(\"selected\")" : "")}}
                  return first + second
              func Receiver () → string
                !{{(receiverEffect ? " cw" : "")}}
                {{(receiverEffect ? "System.Console.WriteLine(\"receiver\")" : "")}}
                return "value"
              func First () → int
                !{{(argumentEffect ? " cw" : "")}}
                {{(argumentEffect ? "System.Console.WriteLine(\"first\")" : "")}}
                return 3
              func Second () → int
                !{{(argumentEffect ? " cw" : "")}}
                {{(argumentEffect ? "System.Console.WriteLine(\"second\")" : "")}}
                return 4
              func Main () → int
                ! cw
                return Receiver().Pick(second: Second(), first: First())
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var main = bound.Functions.Single(function => function.Symbol.Name == "Main");
        var calls = BoundNodeHelpers.DescendantsAndSelf(main).OfType<BoundCallExpression>().ToArray();
        Assert.Equal(4, calls.Length);
        var selected = Assert.Single(calls, call => call.IsSourceExtensionCall);
        Assert.Equal(new[] { 0, 2, 1 },
            Assert.Single(selected.SelectedOverloadMatches).Arguments.Select(argument => argument.ParameterIndex));
        Assert.Same(selected.EvaluatedReceiver, selected.Arguments[0]);
        Assert.Equal(1, selected.Children.Count(child => ReferenceEquals(child, selected.EvaluatedReceiver)));
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Execute(compiled.GeneratedCode, assembly => Assert.Equal(7,
                assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
        }
        finally
        {
            Console.SetOut(previous);
        }
        var expected = new List<string>();
        if (receiverEffect) expected.Add("receiver");
        if (argumentEffect) expected.AddRange(["second", "first"]);
        if (selectedEffect) expected.Add("selected");
        Assert.Equal(expected, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        var pureCaller = source.Replace("func Main () → int\n    ! cw", "func Main () → int\n    !",
            StringComparison.Ordinal);
        Assert.Contains(Program.Compile(pureCaller).Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("Function 'Main' uses effect 'cw'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "Color.Red")]
    [InlineData("", "color")]
    [InlineData("", "Shade.Red")]
    [InlineData(" Demo", "Demo.Color.Red")]
    [InlineData(" Demo", "color")]
    public void NativeEnumReceiverSelectsSourceSymbolsAndExecutes(string ns, string receiver)
    {
        var source = $$"""
            module EnumReceivers{{ns}}
              using Shade = {{(ns.Length == 0 ? "" : "Demo.")}}Color
              enum Color
                Red
              enum Other
                Red
              module Extensions
                func Pick (this Color:value, int:amount) → int
                  !
                  return value == Color.Red ? amount : 0
                func Pick (this Other:value, int:amount) → int
                  ! cw
                  return 0
                func Pick (int:value, int:amount) → int
                  ! alloc
                  return 0
              func Main (Color:color) → int
                !
                return {{receiver}}.Pick(amount: 7)
            """;
        var ast = Parse(source);
        var bound = BindNativeEnums(ast);
        var main = bound.Functions.Single(function => function.Symbol.Name == "Main");
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(main).OfType<BoundCallExpression>());
        Assert.True(call.IsSourceExtensionCall);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Same(call.EvaluatedReceiver, call.Arguments[0]);
        Assert.Equal(1, call.Children.Count(child => ReferenceEquals(child, call.EvaluatedReceiver)));
        var enumType = Assert.IsType<NominalBoundType>(call.Arguments[0].Type).Declaration!;
        Assert.True(enumType.IsEnum);
        Assert.Same(bound.SymbolsById[enumType.Id], enumType);
        Assert.Equal((ns.Length == 0 ? "" : "Demo.") + "Color", enumType.QualifiedName);
        var match = Assert.Single(call.SelectedOverloadMatches);
        Assert.Same(call.ResolvedSymbol, match.Function);
        Assert.Same(bound.SymbolsById[match.Function.Id], match.Function);
        Assert.Equal("Color", match.Function.Parameters[0].TypeName);
        Assert.True(match.Function.Parameters[0].Modifier.HasFlag(Calor.Compiler.Ast.ParameterModifier.This));
        Assert.Equal(new[] { 0, 1 }, match.Arguments.Select(argument => argument.ArgumentIndex));
        Assert.Equal(new[] { 0, 1 }, match.Arguments.Select(argument => argument.ParameterIndex));
        Assert.Equal("global::" + enumType.QualifiedName, match.Arguments[0].ParameterType);
        Assert.Same(bound.SymbolsById[match.Function.Parameters[0].Id], match.Function.Parameters[0]);
        Assert.Contains(bound.SymbolsById.Values.OfType<TypeSymbol>(),
            type => type.QualifiedName == match.Function.ContainingTypeName);
        Assert.Equal(new string?[] { null, "amount" }, call.ArgumentNames);
        Assert.Equal("Pick", Slice(source, call.CalleeSpan));
        Assert.Equal("Pick", Slice(source, match.Function.DeclarationSpan));
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.False(call.HasOpaqueOverloads);
        var selected = bound.Functions.Single(function => function.Symbol == match.Function);
        Assert.Empty(selected.DeclaredEffects);
        var graph = CallGraphAnalysis.Build(ast);
        Assert.True(graph.IsBoundResolutionComplete);
        if (call.Arguments[0] is BoundFieldAccessExpression constant)
        {
            Assert.Same(enumType, Assert.IsType<BoundTypeReferenceExpression>(constant.Target).ResolvedType);
            Assert.True(constant.ResolvedField!.IsEnumMember);
            Assert.Same(bound.SymbolsById[constant.ResolvedField.Id], constant.ResolvedField);
            Assert.Equal(constant.ResolvedField.Id,
                graph.SourceEnumConstant(ast.Functions.Single().Id, constant.Span)!.Id);
            Assert.Null(graph.MetadataEnumConstant(ast.Functions.Single().Id, constant.Span));
        }
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code is
            DiagnosticCode.UnknownExternalCall or DiagnosticCode.AssumedEffects);
        Assert.Contains("global::" + (ns.Length == 0 ? "" : "Demo.") + "Extensions.Pick(", compiled.GeneratedCode);
        Execute(compiled.GeneratedCode, assembly =>
        {
            var prefix = ns.Length == 0 ? "" : "Demo.";
            var value = Enum.Parse(assembly.GetType(prefix + "Color")!, "Red");
            Assert.Equal(7, assembly.GetType(ns.Length == 0 ? "GlobalModule" : "Demo.DemoModule")!
                .GetMethod("Main")!.Invoke(null, [value]));
        });
    }

    [Fact]
    public void NativeEnumReceiverExactReportedProgramExecutes()
    {
        const string source = """
            module EnumStatic
              enum Color
                Red
              module ColorExtensions
                func ToHex (this Color:value) → string
                  !
                  return "#FF0000"
              func Main () → string
                !
                return Color.Red.ToHex()
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code is
            DiagnosticCode.UnknownExternalCall or DiagnosticCode.AssumedEffects);
        Assert.Contains("return global::ColorExtensions.ToHex(Color.Red);", compiled.GeneratedCode);
        Execute(compiled.GeneratedCode, assembly => Assert.Equal("#FF0000",
            assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

    [Fact]
    public void NativeEnumReceiverChargesSelectedEffectsAndEvaluatesReceiverAndArgumentsOnceInOrder()
    {
        const string source = """
            module ReceiverOrder
              enum Quiet
                Value
              enum Loud
                Value
              module Extensions
                func Pick (this Quiet:value, int:amount) → int
                  !
                  return 0
                func Pick (this Loud:value, int:amount) → int
                  ! cw
                  System.Console.WriteLine("selected")
                  return amount
              func Receiver () → Loud
                ! cw
                System.Console.WriteLine("receiver")
                return Loud.Value
              func Argument () → int
                ! cw
                System.Console.WriteLine("argument")
                return 7
              func Main () → int
                ! cw
                return Receiver().Pick(Argument())
            """;
        var bound = BindNativeEnums(Parse(source));
        var main = bound.Functions.Single(function => function.Symbol.Name == "Main");
        var calls = BoundNodeHelpers.DescendantsAndSelf(main).OfType<BoundCallExpression>().ToArray();
        Assert.Equal(3, calls.Length);
        var extension = Assert.Single(calls.Where(call => call.IsSourceExtensionCall));
        Assert.Equal("Loud", extension.ResolvedSymbol!.Parameters[0].TypeName);
        Assert.Equal(new[] { "io:console_write" }, bound.Functions.Single(function =>
            function.Symbol == extension.ResolvedSymbol).DeclaredEffects);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Execute(compiled.GeneratedCode, assembly => Assert.Equal(7,
                assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
        }
        finally
        {
            Console.SetOut(previous);
        }
        Assert.Equal(new[] { "receiver", "argument", "selected" },
            output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        var pureCaller = source.Replace("func Main () → int\n    ! cw", "func Main () → int\n    !",
            StringComparison.Ordinal);
        Assert.Contains(Program.Compile(pureCaller).Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        var selectedOnly = source.Replace("Receiver().Pick(Argument())", "Loud.Value.Pick(7)", StringComparison.Ordinal)
            .Replace("func Main () → int\n    ! cw", "func Main () → int\n    !", StringComparison.Ordinal);
        Assert.Contains(Program.Compile(selectedOnly).Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("Other.Red")]
    [InlineData("(1)")]
    [InlineData("Color.Missing")]
    [InlineData("System.ConsoleColor.Red")]
    public void NativeEnumReceiverWrongEnumIntegerUnknownAndMetadataNeverBorrowSourceRow(string receiver)
    {
        var source = $$"""
            module WrongReceivers
              enum Color
                Red
              enum Other
                Red
              module Extensions
                func Pick (this Color:value) → int
                  !
                  return 7
              func Main () → int
                !
                return {{receiver}}.Pick()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
    }

    [Theory]
    [InlineData("private", "")]
    [InlineData("", "  module OtherExtensions\n    func Pick (this Color:value) → int\n      !\n      return 8\n")]
    [InlineData("", "  module OpaqueExtensions\n    raw member 54\npublic static int Pick(this Color value) { return 9; }\n")]
    public void NativeEnumReceiverInaccessibleAmbiguousAndOpaqueCompetitionNeverBorrowPureRow(
        string visibility, string extra)
    {
        var source = $$"""
            module ReceiverCompetition
              enum Color
                Red
              module Extensions
                func Pick {{visibility}} (this Color:value) → int
                  !
                  return 7
            {{extra}}  func Main () → int
                !
                return Color.Red.Pick()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(extra.Contains("raw member", StringComparison.Ordinal) == call.HasOpaqueOverloads,
            $"extension={call.IsSourceExtensionCall}; receiver={call.Receiver?.Type}; unknown={call.HasUnresolvedArguments}; {string.Join("\n", diagnostics)}");
        if (extra.Contains("OtherExtensions", StringComparison.Ordinal))
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("object:Color", "Color.Red")]
    [InlineData("Color:value", "Color.Red", "<Color>")]
    [InlineData("object:value", "value")]
    public void NativeEnumReceiverShadowedAndUntypedValuesHaveNoSourceAuthority(
        string parameter, string receiver, string generic = "")
    {
        var source = $$"""
            module ShadowedReceivers
              enum Color
                Red
              module Extensions
                func Pick (this Color:value) → int
                  !
                  return 7
              func Main{{generic}} ({{parameter}}) → int
                !
                return {{receiver}}.Pick()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeEnumReceiverDoesNotTurnANonExtensionOrInheritedMetadataMemberIntoASourceCall(bool inherited)
    {
        var method = inherited ? "ToString" : "Pick";
        var source = $$"""
            module NonExtensionReceivers
              enum Color
                Red
              module Extensions
                func {{method}} ({{(inherited ? "this " : "")}}Color:value) → string
                  !
                  return "not selected"
              func Main () → string
                !
                return Color.Red.{{method}}()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Equal(inherited, call.HasOpaqueOverloads);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("", "object")]
    [InlineData("<T>", "T")]
    public void NativeEnumReceiverDoesNotBroadenAuthorityToNonEnumExtensionParameters(
        string generic, string parameterType)
    {
        var source = $$"""
            module NonEnumExtensionReceivers
              enum Color
                Red
              module Extensions
                func Pick{{generic}} (this {{parameterType}}:value) → int
                  !
                  return 7
              func Main () → int
                !
                return Color.Red.Pick()
            """;
        var bound = BindNativeEnums(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Contains(Program.Compile(source).Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("ConsoleColor.Red", true)]
    [InlineData("System.ConsoleColor.Red", false)]
    public void NativeEnumReceiverSameNamedMetadataEnumKeepsItsOrigin(string receiver, bool native)
    {
        var source = $$"""
            module EnumReceiverOrigins
              enum ConsoleColor
                Red
              module Extensions
                func Pick (this ConsoleColor:value) → int
                  !
                  return 7
              func Main () → int
                !
                return {{receiver}}.Pick()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>());
        var receiverType = Assert.IsType<NominalBoundType>(call.Receiver!.Type);
        Assert.Equal(native, receiverType.Declaration is not null);
        Assert.Equal(native, call.ResolvedSymbol is not null);
        if (!native)
        {
            Assert.Equal(TypeKind.Enum, receiverType.RoslynSymbol!.TypeKind);
            Assert.Empty(call.SelectedOverloadMatches);
        }
        Assert.Equal(!native, Program.Compile(source).HasErrors);
    }

    [Fact]
    public void NativeEnumReceiverRetainsGenericNamedOptionalAndStatementCalls()
    {
        const string source = """
            module ReceiverArguments
              enum Color
                Red
              module Extensions
                func Pick<T> (this Color:value, T:item, int:amount = 7) → int
                  !
                  return amount
              func Main () → int
                !
                let color = Color.Red
                color.Pick<int>(item: 3)
                color.Pick<Color>(item: Color.Red)
                return color.Pick<int>(amount: 9, item: 4)
            """;
        var bound = BindNativeEnums(Parse(source));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundCallExpression>().ToArray();
        Assert.Equal(3, calls.Length);
        Assert.All(calls, call =>
        {
            Assert.True(call.IsSourceExtensionCall);
            Assert.Equal(1, call.ResolvedSymbol!.GenericArity);
        });
        Assert.Equal(new[] { 0, 2, 1 },
            Assert.Single(calls[2].SelectedOverloadMatches).Arguments.Select(argument => argument.ParameterIndex));
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly => Assert.Equal(9,
            assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

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
        Assert.True(call.IsSourceExtensionCall);
        Assert.Equal("Extensions.Pick", call.ResolvedSymbol!.Name);
        Assert.Same(call.ResolvedSymbol, Assert.Single(call.SelectedOverloadMatches).Function);
        Assert.Equal("Color", call.ResolvedSymbol.Parameters.Single().TypeName);
        Assert.Null(call.ResolvedMetadataMethod);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code is
            DiagnosticCode.UnknownExternalCall or DiagnosticCode.AssumedEffects);
        Execute(compiled.GeneratedCode, assembly => Assert.Equal(7,
            assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null,
                [Enum.Parse(assembly.GetType("Color")!, "Red")])));
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
        var admitted = source
            .Replace("func Update (ref Color:value, in int:increment, out int:answer) → void\n      !",
                "func Update (ref Color:value, in int:increment, out int:answer) → void\n      ! mut",
                StringComparison.Ordinal)
            .Replace("func Main () → int\n    !", "func Main () → int\n    ! mut", StringComparison.Ordinal);
        Assert.NotEqual(source, admitted);
        var ast = Parse(admitted);
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
        foreach (var input in new[] { admitted, new CalorEmitter().Emit(ast) })
        {
            _ = Parse(input);
            var compiled = Program.Compile(input);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Execute(compiled.GeneratedCode, assembly =>
                Assert.Equal(4, assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
        }

        foreach (var (missing, expectedFunctions) in new[]
        {
            (source, new[] { "Update", "Main" }),
            (admitted.Replace("! mut\n      set value", "!\n      set value", StringComparison.Ordinal),
                new[] { "Update" }),
            (admitted.Replace("! mut\n    var Color:value", "!\n    var Color:value", StringComparison.Ordinal),
                new[] { "Main" }),
        })
        {
            Assert.NotEqual(admitted, missing);
            foreach (var input in new[] { missing, new CalorEmitter().Emit(Parse(missing)) })
            {
                _ = Parse(input);
                var rejected = Program.Compile(input, filePath: null,
                    options: new CompilationOptions { StrictEffects = true });
                Assert.True(rejected.HasErrors);
                var errors = rejected.Diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
                Assert.Equal(expectedFunctions.Select(function =>
                    $"Function '{function}' uses effect 'mut' but does not declare it").Order(),
                    errors.Select(error => error.Message).Order());
                Assert.DoesNotContain(rejected.Diagnostics, diagnostic => diagnostic.Code is
                    DiagnosticCode.UnknownExternalCall or DiagnosticCode.AssumedEffects);
            }
        }
        Execute(new CSharpEmitter().Emit(Parse(source)), assembly =>
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
        Assert.Empty(compiled.Diagnostics);
        Execute(compiled.GeneratedCode, assembly =>
            Assert.Equal(7, assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
    }

    [Theory]
    [InlineData("", "Color", "Color")]
    [InlineData("", "Shade", "Color")]
    [InlineData(" Demo", "Color", "Demo.Color")]
    [InlineData(" Demo", "Demo.Color", "Demo.Color")]
    [InlineData(" Demo", "Shade", "Demo.Color")]
    public void EscapedEnumConstantUsesActualBoundOwnerUnderNormalEnforcement(
        string ns, string receiver, string owner)
    {
        var source = $$"""
            module EscapedEnumScopes{{ns}}
              using Shade = {{owner}}
              enum Color
                @event = 7
              module Extensions
                func Pick (this Color:value) → int
                  !
                  return value == Color.@event ? 7 : 0
              func Main () → int
                !
                let color = {{receiver}}.@event
                return color.Pick()
            """;
        var ast = Parse(source);
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics);
        var bound = binder.Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var constant = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Main")).OfType<BoundFieldAccessExpression>());
        var enumType = Assert.IsType<NominalBoundType>(constant.Type).Declaration!;
        Assert.Equal(owner, enumType.QualifiedName);
        Assert.Same(bound.SymbolsById[enumType.Id], enumType);
        Assert.Same(bound.SymbolsById[constant.ResolvedField!.Id], constant.ResolvedField);
        Assert.Contains(binder.BoundSourceEnumConstants.Values, value => ReferenceEquals(value, constant));
        var compiled = Program.Compile(source);
        Assert.Empty(compiled.Diagnostics);
        Execute(compiled.GeneratedCode, assembly => Assert.Equal(7,
            assembly.GetType(ns.Length == 0 ? "GlobalModule" : "Demo.DemoModule")!
                .GetMethod("Main")!.Invoke(null, null)));
    }

    [Theory]
    [InlineData("Color.Missing")]
    [InlineData("Unknown.@event")]
    [InlineData("value.@event")]
    [InlineData("System.ConsoleColor.@event")]
    [InlineData("Color.@event", "object:Color")]
    [InlineData("Color.@event", "Color:Color")]
    public void UnresolvedEnumMemberCannotBorrowSourceConstantFacts(
        string expression, string parameter = "object:value")
    {
        var source = $$"""
            module UnknownEnumConstants
              enum Color
                @event = 7
              module Extensions
                func Pick (Color:value) → int
                  !
                  return 7
              func Main ({{parameter}}) → int
                !
                return Extensions.Pick({{expression}})
            """;
        var binder = new Binder(new DiagnosticBag());
        binder.Bind(Parse(source));
        Assert.Empty(binder.BoundSourceEnumConstants);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void SourceEnumConstantFactsRequireTheExactAstAndAreClearedOnRebind()
    {
        const string source = """
            module ScopedEnumConstants
              enum Color
                @event = 7
              func Main () → Color
                !
                return Color.@event
            """;
        var ast = Parse(source);
        var other = Parse(source);
        var binder = new Binder(new DiagnosticBag());
        binder.Bind(ast);
        var original = Assert.Single(binder.BoundSourceEnumConstants.Keys);
        var diagnostics = new DiagnosticBag();
        new TypeChecking.TypeChecker(diagnostics, binder.BoundAwaits, binder.BoundSourceEnumConstants).Check(ast);
        Assert.Empty(diagnostics);
        var unrelated = new DiagnosticBag();
        new TypeChecking.TypeChecker(unrelated, binder.BoundAwaits, binder.BoundSourceEnumConstants).Check(other);
        Assert.Contains(unrelated, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
            && diagnostic.Message == "Undefined variable 'Color'");
        binder.Bind(other);
        Assert.DoesNotContain(binder.BoundSourceEnumConstants.Keys, node => ReferenceEquals(node, original));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapedEnumConstantOrdinaryCliHasNoDiagnosticsAndExecutesActualGeneratedCode(bool receiverCall)
    {
        var source = $$"""
            module EscapedEnumCli
              enum Color
                @event = 7
              module Extensions
                func Pick (this Color:value) → int
                  !
                  return 7
              func Main () → int
                !
                return {{(receiverCall ? "Color.@event.Pick()" : "Extensions.Pick(Color.@event)")}}
            """;
        var directory = Path.Combine(Path.GetTempPath(), "calor-escaped-enum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "Escaped.calr");
            var output = Path.Combine(directory, "Escaped.g.cs");
            File.WriteAllText(input, source);
            var run = CliTestHarness.RunCli(directory, "--no-telemetry", "-i", input,
                "--format", "json", "-o", output);
            Assert.True(run.ExitCode == 0, run.StdOut + run.StdErr);
            using var json = System.Text.Json.JsonDocument.Parse(run.StdOut);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            Execute(File.ReadAllText(output), assembly => Assert.Equal(7,
                assembly.GetType("GlobalModule")!.GetMethod("Main")!.Invoke(null, null)));
        }
        finally { Directory.Delete(directory, recursive: true); }
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
