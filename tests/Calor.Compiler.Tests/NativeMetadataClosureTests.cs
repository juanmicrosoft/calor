using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public sealed class NativeMetadataClosureTests
{
    [Theory]
    [InlineData("(Func<int>)(() => 9)")]
    [InlineData("(f)(1)")]
    [InlineData("(T)-n")]
    [InlineData("(T)!flag")]
    [InlineData("(T)~n")]
    [InlineData("(T)+n")]
    [InlineData("(Func<int>)-n")]
    [InlineData("(N.T)(value)")]
    [InlineData("(N.T)!flag")]
    [InlineData("(N.F<int>)(() => 9)")]
    [InlineData("(f)(1, 2)")]
    public void NativeCastDisambiguation_MatchesCSharpSyntax(string expression)
    {
        var expected = SyntaxFactory.ParseExpression(expression);
        Assert.False(expected.ContainsDiagnostics, expected.ToFullString());
        var module = Parse($"module CastSyntax\n  func Probe () → object\n    return {expression}");
        var actual = Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(module.Functions).Body)).Expression;
        Assert.True((expected is Microsoft.CodeAnalysis.CSharp.Syntax.CastExpressionSyntax)
            == (actual is TypeOperationNode { Operation: TypeOp.Cast }),
            $"{expression}: C# {expected.Kind()}, native {actual!.GetType().Name}");
    }

    [Theory]
    [InlineData("nameof(n).Length", "1")]
    [InlineData("(nameof(n)).Length", "1")]
    [InlineData("\"abc\".Length", "3")]
    public void NativeNameofAndLiteralLength_RetainStringMetadataAndPureInterpolation(string expression, string expected)
    {
        var authored = "module Native Native\n  func Probe (int:n) → string\n    return \"${" + expression + "}\"";
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            var interpolation = Assert.IsType<BoundInterpolatedStringExpression>(
                Assert.IsType<BoundReturnStatement>(Assert.Single(Assert.Single(bound.Functions).Body)).Expression);
            var member = Assert.IsType<BoundFieldAccessExpression>(Assert.Single(interpolation.Children));
            Assert.False(member.HasUnknownInferredType);
            Assert.Equal("INT", member.Type.DisplayString);
            var property = Assert.IsAssignableFrom<IPropertySymbol>(member.ResolvedMetadataMember);
            Assert.Equal(SpecialType.System_String, property.ContainingType.SpecialType);
            Assert.Equal(SpecialType.System_Int32, property.Type.SpecialType);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(expected, CompileAssembly(result.GeneratedCode).GetType("Native.NativeModule")!
                .GetMethod("Probe")!.Invoke(null, [12]));
        }
    }

    [Fact]
    public void NativeNameofLength_DoesNotMakeUnknownStringMembersPure()
    {
        var result = Compile("""
            module UnknownNameofMember
              func Probe (int:n) → int
                return nameof(n).Missing
            """);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void NativeCastLambdaInvocation_RetainsTheOriginalNineWithoutAnOpaqueAssumption()
    {
        const string authored = """
            module Native Native
              func Probe () → string
                return "${((Func<int>)(() => 9))()}"
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            var interpolation = Assert.IsType<BoundInterpolatedStringExpression>(
                Assert.IsType<BoundReturnStatement>(Assert.Single(Assert.Single(bound.Functions).Body)).Expression);
            var call = Assert.IsType<BoundExpressionCall>(Assert.Single(interpolation.Children));
            var cast = Assert.IsType<BoundTypeOperationExpression>(call.Target);
            var lambda = Assert.IsType<BoundLambdaExpression>(cast.Operand);
            Assert.Equal(9, Assert.IsType<BoundIntLiteral>(lambda.ExpressionBody).Value);
            var signature = Assert.IsType<Calor.Compiler.Binding.BoundTypes.FunctionBoundType>(cast.Type);
            Assert.Empty(signature.ParameterTypes);
            Assert.Equal(signature.ReturnType, call.Type);
            Assert.False(call.HasUnknownInferredType);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Calor04", StringComparison.Ordinal));
            Assert.Equal("9", CompileAssembly(result.GeneratedCode).GetType("Native.NativeModule")!
                .GetMethod("Probe")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("Func<int>", "int")]
    [InlineData("System.Func<int>", "int")]
    [InlineData("Func<object>", "object")]
    public void NativeCastLambdaInvocation_RetainsTheAuthoredDelegateReturnOverload(string type, string expected)
    {
        var authored = $$"""
            module DelegateReturnIdentity
              func Pick (int:value) → string
                return "int"
              func Pick (object:value) → string
                return "object"
              func Probe () → string
                return Pick((({{type}})(() => 9))())
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Probe");
            Assert.Equal(expected, method.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("((Func<int>)(() => Next()))()")]
    [InlineData("((Func<int,int>)((x) => x))(Next())")]
    public void NativeCastLambdaInvocation_ChargesBodyAndArgumentEffects(string expression)
    {
        var authored = $$"""
            module DelegateEffects
              func Next () → int
                ! cw
                System.Console.WriteLine("next")
                return 9
              func Probe () → int
                return {{expression}}
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
        }
    }

    [Fact]
    public void NativeCastUnknownDelegate_DoesNotAcquireALiteralBodyProof()
    {
        var result = Compile("""
            module UnknownDelegate
              func Probe (Func<int>:value) → int
                return ((Func<int>)value)()
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0419");
    }

    [Theory]
    [InlineData("module Demo\n  func Run () → void\n    let items = new System.Collections.Generic.List<int> {1, 2}")]
    [InlineData("module Demo\n  func Run () → void\n    let items = new System.Collections.Generic.Dictionary<string,int> {{\"a\", 1}}")]
    [InlineData("module Demo\n  func Run () → void\n    let items = new System.Collections.Generic.HashSet<string> {\"a\"}")]
    [InlineData("module Demo\n  func Run () → void\n    let System.Collections.Generic.List<int>:items = new System.Collections.Generic.List<int> {1, 2}")]
    public void OriginalIdentityFreeCollectionInitializers_RetainTheirNativeTypes(string source)
    {
        var module = Parse(source);
        var creation = Assert.IsType<NewExpressionNode>(
            Assert.IsType<BindStatementNode>(Assert.Single(Assert.Single(module.Functions).Body)).Initializer);
        Assert.NotEmpty(creation.CollectionInitializers);
        Assert.Empty(creation.Initializers);
        var generated = new CSharpEmitter().Emit(module);
        Assert.Contains("new System.Collections.Generic.", generated);
        var method = CompileAssembly(generated).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Run");
        method.Invoke(null, null);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<int>", "1, 2")]
    [InlineData("System.Collections.Generic.HashSet<string>", "\"a\", \"a\", \"b\"")]
    [InlineData("System.Collections.Generic.Dictionary<string,int>", "{\"a\", 1}, {\"b\", 2}")]
    public void NativeCollectionInitializers_RetainClrAddSemanticsAndReemit(string typeName, string entries)
    {
        var authored = $$"""
            module Collections
              func Make () → {{typeName}}
                ! alloc, mut
                return new {{typeName}} { {{entries}} }
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Make");
            var value = method.Invoke(null, null);
            switch (value)
            {
                case List<int> list:
                    Assert.Equal(new[] { 1, 2 }, list);
                    break;
                case HashSet<string> set:
                    Assert.Equal(new[] { "a", "b" }, set.Order().ToArray());
                    break;
                case Dictionary<string, int> dictionary:
                    Assert.Equal(2, dictionary.Count);
                    Assert.Equal(1, dictionary["a"]);
                    Assert.Equal(2, dictionary["b"]);
                    break;
                case System.Collections.ArrayList arrayList:
                    Assert.Equal(new object[] { 1, "two" }, arrayList.ToArray());
                    break;
                default:
                    Assert.Fail($"Unexpected collection runtime type: {value?.GetType()}");
                    break;
            }
        }
    }

    [Fact]
    public void NativeArrayListInitializer_DoesNotInventAnEffectContract()
    {
        const string source = """
            module UnknownCollectionEffects
              func Make () → System.Collections.ArrayList
                ! alloc, mut
                return new System.Collections.ArrayList {1, "two"}
            """;
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        var generated = new CSharpEmitter().Emit(Parse(source));
        var method = CompileAssembly(generated).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Make");
        var list = Assert.IsType<System.Collections.ArrayList>(method.Invoke(null, null));
        Assert.Equal(new object[] { 1, "two" }, list.ToArray());
    }

    [Fact]
    public void NativeCollectionInitializers_PreserveConstructorAndElementOrderAndConditionalLaziness()
    {
        const string authored = """
            module CollectionOrder
              using System.Collections.Generic
              func Next (ref int:counter) → int
                ! mut
                set counter (counter + 1)
                return counter
              func Make () → List<int>
                ! alloc, mut
                var int:counter = 0
                return new List<int>(Next(ref counter)) {Next(ref counter), Next(ref counter)}
              func Conditional (bool:take) → int
                ! alloc, mut
                var int:counter = 0
                let List<int>:values = take ? new List<int> {Next(ref counter)} : new List<int>()
                return counter
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var methods = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .ToArray();
            var list = Assert.IsType<List<int>>(methods.Single(method => method.Name == "Make").Invoke(null, null));
            Assert.Equal(new[] { 2, 3 }, list);
            Assert.Equal(2, list.Capacity);
            var conditional = methods.Single(method => method.Name == "Conditional");
            Assert.Equal(0, conditional.Invoke(null, [false]));
            Assert.Equal(1, conditional.Invoke(null, [true]));
        }
    }

    [Fact]
    public void NativeDictionaryInitializer_DuplicateKeysThrowRatherThanUpsert()
    {
        const string source = """
            module DuplicateKeys
              func Make () → System.Collections.Generic.Dictionary<string,int>
                ! alloc, mut
                return new System.Collections.Generic.Dictionary<string,int> {{"a", 1}, {"a", 2}}
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Make");
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, null));
        Assert.IsType<ArgumentException>(error.InnerException);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<int>", "\"wrong\"")]
    [InlineData("System.Collections.Generic.Dictionary<string,int>", "{\"a\"}")]
    [InlineData("System.Text.StringBuilder", "\"not an Add\"")]
    public void NativeCollectionInitializers_RejectMissingOrIncompatibleAddOverloads(string typeName, string entries)
    {
        var result = Compile($$"""
            module InvalidCollection
              func Make () → {{typeName}}
                ! alloc, mut
                return new {{typeName}} { {{entries}} }
            """);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void NativeSourceTypedList_RetainsTheOriginalNestedConstructorAndSourceIdentity()
    {
        const string authored = """
            module SourceList
              using System.Collections.Generic
              class WidgetException
                field int:Value public = 23
              func Make () → List<WidgetException>
                ! alloc, mut
                let List<WidgetException>:values = new List<WidgetException> {new WidgetException()}
                return values
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            var creation = Assert.IsType<BoundNewExpression>(
                Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]).Initializer);
            var entry = Assert.Single(creation.CollectionInitializers);
            var element = Assert.IsType<BoundNewExpression>(Assert.Single(entry.Arguments));
            Assert.Equal("WidgetException", element.ResolvedType!.Name);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Make");
            var list = Assert.IsAssignableFrom<System.Collections.IList>(method.Invoke(null, null));
            Assert.Equal(typeof(List<>), list.GetType().GetGenericTypeDefinition());
            var value = Assert.Single(list.Cast<object>());
            Assert.Equal("WidgetException", value.GetType().Name);
            Assert.Equal(23, value.GetType().GetField("Value")!.GetValue(value));
        }
    }

    [Fact]
    public void OriginalTypeRenameProgram_RetainsItsListAndNestedConstructor()
    {
        const string source = """
            module TypeRefactor
              using System.Collections.Generic
              interface IWidget
                func Marker () → void
              class WidgetException
                extends System.Exception
                implements IWidget
                func Create static () → WidgetException
                  return new WidgetException()
                func Marker () → void
                  System.Console.WriteLine("marker")
              class DerivedException
                extends WidgetException
                field int:Code
              func Use (object:value) → WidgetException
                let WidgetException[]:items = new WidgetException[1]
                let List<WidgetException>:values = new List<WidgetException> {new WidgetException()}
                try
                  return (WidgetException)value
                catch WidgetException:ex
                  return WidgetException.Create()
            """;
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var collection = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundBindStatement>(use.Body[1]).Initializer);
        var nested = Assert.IsType<BoundNewExpression>(
            Assert.Single(Assert.Single(collection.CollectionInitializers).Arguments));
        Assert.Equal("WidgetException", nested.ResolvedType!.Name);
        Assert.Equal("WidgetException", source.Substring(nested.TypeNameSpan.Start, nested.TypeNameSpan.Length));
        var widget = bound.SymbolsById.Values.OfType<TypeSymbol>().Single(type => type.Name == "WidgetException");
        var elementType = Assert.Single(collection.TypeReference.TypeArguments);
        Assert.Equal(widget.Id, elementType.ResolvedTypeSymbolId);
        Assert.Equal("WidgetException", source.Substring(elementType.Span.Start, elementType.Span.Length));
        var factory = Calor.Compiler.Analysis.Dataflow.BoundNodeHelpers.DescendantsAndSelf(bound)
            .OfType<BoundCallExpression>().Single(call => call.ResolvedSymbol?.Name == "WidgetException.Create");
        Assert.Equal(widget.Id, factory.ReceiverTypeSymbolId);
        Assert.Equal("WidgetException",
            source.Substring(factory.ReceiverSpan!.Value.Start, factory.ReceiverSpan.Value.Length));
        Assert.Contains("new List<WidgetException>", new CSharpEmitter().Emit(module));
        CompileAssembly(new CSharpEmitter().Emit(module));
    }

    [Fact]
    public void NativeCollectionAlias_RetainsActualAddIdentity()
    {
        const string source = """
            module CollectionAlias
              using Items = System.Collections.Generic.List<int>
              func Make () → Items
                ! alloc, mut
                return new Items {1, 2}
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Make");
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<List<int>>(method.Invoke(null, null)));
    }

    [Theory]
    [InlineData("Bag", "Bag")]
    [InlineData("Bag<T>", "Bag<int>")]
    public void NativeCollectionInitializer_DoesNotInferEnumerableFromAnAddName(string declaration, string constructed)
    {
        var source = $$"""
            module NonEnumerable
              class {{declaration}}
                func Add (int:value) → void
                  return
              func Make () → {{constructed}}
                ! alloc, mut
                return new {{constructed}} {1}
            """;
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(Parse(source));
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.TypeMismatch
            && diagnostic.Message.Contains("System.Collections.IEnumerable"));
        Assert.True(Compile(source).HasErrors);
    }

    [Fact]
    public void NativeCollectionInitializer_SimplificationRetainsOrderedAddArguments()
    {
        var module = Parse("""
            module SimplifiedCollection
              func Make () → System.Collections.Generic.List<int>
                ! alloc, mut
                return new System.Collections.Generic.List<int>(1 + 1) {1 + 2, 2 + 3}
            """);
        var statement = Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
        var simplifier = new Calor.Compiler.Verification.ExpressionSimplifier();
        var creation = Assert.IsType<NewExpressionNode>(simplifier.Simplify(statement.Expression!));
        Assert.True(simplifier.Changed);
        Assert.Equal(2, Assert.IsType<IntLiteralNode>(Assert.Single(creation.Arguments)).Value);
        Assert.Equal(new long[] { 3, 5 }, creation.CollectionInitializers.Select(entry =>
            Assert.IsType<IntLiteralNode>(Assert.Single(entry.Arguments)).Value));
        Assert.All(creation.CollectionInitializers, entry => Assert.Equal(TextSpan.Empty, entry.CalleeSpan));
    }

    [Fact]
    public void NativeCollectionInitializer_InheritedContractRewritingRetainsElements()
    {
        var module = Parse("""
            module RewrittenCollection
              func Make (int:value) → System.Collections.Generic.List<int>
                ! alloc, mut
                return new System.Collections.Generic.List<int>(value) {value, value + 1}
            """);
        var original = Assert.IsType<NewExpressionNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body)).Expression);
        var rewrite = typeof(Calor.Compiler.Verification.ContractInheritanceChecker)
            .GetMethod("RewriteReferences", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(rewrite);
        var creation = Assert.IsType<NewExpressionNode>(rewrite.Invoke(null,
            [original, new Dictionary<string, string> { ["value"] = "renamed" }, null]));
        Assert.Equal("renamed", Assert.IsType<ReferenceNode>(Assert.Single(creation.Arguments)).Name);
        Assert.Equal(2, creation.CollectionInitializers.Count);
        Assert.Equal("renamed", Assert.IsType<ReferenceNode>(
            Assert.Single(creation.CollectionInitializers[0].Arguments)).Name);
        Assert.Equal("renamed", Assert.IsType<ReferenceNode>(Assert.IsType<BinaryOperationNode>(
            Assert.Single(creation.CollectionInitializers[1].Arguments)).Left).Name);
        Assert.Equal(original.CollectionInitializers.Select(entry => entry.Span),
            creation.CollectionInitializers.Select(entry => entry.Span));
        Assert.All(creation.CollectionInitializers, entry => Assert.Equal(TextSpan.Empty, entry.CalleeSpan));
    }

    [Fact]
    public void NativeCollectionInitializer_RetainsMultilineAndInterpolationBoundaries()
    {
        const string authored = """
            module CollectionBoundaries
              using System.Collections.Generic
              func Make () → List<int>
                ! alloc, mut
                return new List<int> {
                  1,
                  2,
                }
              func Format () → string
                ! alloc, mut
                return "${new List<int> {1, 2}.Count}"
              func After () → int
                return 9
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var methods = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .ToArray();
            Assert.Equal(new[] { 1, 2 },
                Assert.IsType<List<int>>(methods.Single(method => method.Name == "Make").Invoke(null, null)));
            Assert.Equal("2", methods.Single(method => method.Name == "Format").Invoke(null, null));
            Assert.Equal(9, methods.Single(method => method.Name == "After").Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("alloc", "mut")]
    [InlineData("mut", "alloc")]
    public void NativeCollectionInitializer_ChargesAllocationAndAddEffects(string declared, string missing)
    {
        var result = Compile($$"""
            module CollectionEffects
              func Make () → System.Collections.Generic.List<int>
                ! {{declared}}
                return new System.Collections.Generic.List<int> {1}
            """);
        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains($"'{missing}'"));
    }

    [Fact]
    public void NativeCollectionInitializer_DoesNotDropElementCallEffects()
    {
        const string source = """
            module ElementEffects
              func Next () → int
                ! cw
                System.Console.WriteLine("element")
                return 1
              func Make () → System.Collections.Generic.List<int>
                ! alloc, mut
                return new System.Collections.Generic.List<int> {Next()}
            """;
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
    }

    [Fact]
    public void NativeCollectionInitializer_ChargesASourceAddRatherThanABclNameMatch()
    {
        const string source = """
            module SourceAdd
              class Tracked extends System.Collections.Generic.List<int>
                func Add (int:value) → void
                  ! cw
                  System.Console.WriteLine(value)
              func Make () → Tracked
                ! alloc, mut
                return new Tracked {1}
            """;
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
        Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Theory]
    [InlineData("new System.Collections.Generic.List<int> {1, Name = 2}")]
    [InlineData("new System.Collections.Generic.List<int> {{}}")]
    [InlineData("new System.Collections.Generic.List<int> {{1,}}")]
    public void NativeCollectionInitializer_RejectsMalformedOrMixedEntries(string expression)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer($"module Invalid\n  func Make () → void\n    let value = {expression}",
            diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void IndexedSubtraction_RetainsCastAndUtf16ScalarArithmetic()
    {
        const string authored = """
            module CodecArithmetic
              func Scalar (string:text, int:i, int:scalar) → int
                return ((65536 + ((scalar - 55296) * 1024)) + ((int)(text[i]) - 56320))
              func Previous (byte[]:values, int:i) → int
                return (values[i]) - 1
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var methods = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods()).ToArray();
            Assert.Equal(0x1F600, methods.Single(method => method.Name == "Scalar")
                .Invoke(null, ["\uD83D\uDE00", 1, 0xD83D]));
            Assert.Equal(254, methods.Single(method => method.Name == "Previous")
                .Invoke(null, [new byte[] { 255 }, 0]));
        }
    }

    [Theory]
    [InlineData("int[]")]
    [InlineData("int[,]")]
    [InlineData("int[][]")]
    public void ArrayCasts_RetainActualClrArrayIdentity(string type)
    {
        var authored = $"module ArrayCasts\n  func Preserve (object:value) → {type}\n    return ({type})value\n";
        object input = type switch
        {
            "int[]" => new[] { 42 },
            "int[,]" => new int[,] { { 42 } },
            "int[][]" => new[] { new[] { 42 } },
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Preserve");
            Assert.Equal(input.GetType(), method.ReturnType);
            Assert.Same(input, method.Invoke(null, [input]));
        }
    }

    [Theory]
    [InlineData("System.SByte", "int")]
    [InlineData("System.Byte", "int")]
    [InlineData("System.Int16", "int")]
    [InlineData("System.UInt16", "int")]
    [InlineData("System.Int32", "int")]
    [InlineData("System.UInt32", "uint")]
    [InlineData("System.Int64", "long")]
    [InlineData("System.UInt64", "ulong")]
    [InlineData("System.Single", "float")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Decimal", "decimal")]
    public void ClrNumericAliases_RetainArithmeticTypingAndRealParameterIdentity(string alias, string output)
    {
        var authored = $$"""
            module Numbers
              func Divide ({{alias}}:left, {{alias}}:right) → {{output}}
                let quotient = left / right
                return quotient
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Divide");
            var parameterType = Type.GetType(alias, throwOnError: true)!;
            Assert.All(method.GetParameters(), parameter => Assert.Equal(parameterType, parameter.ParameterType));
            var arguments = new[] { Convert.ChangeType(6, parameterType), Convert.ChangeType(3, parameterType) };
            Assert.Equal(Convert.ChangeType(2, method.ReturnType), method.Invoke(null, arguments));
        }
    }

    [Theory]
    [InlineData("System.Boolean")]
    [InlineData("System.String")]
    public void ClrNonNumericAliases_DoNotAcquireArithmeticOperations(string alias)
    {
        var source = $"module Numbers\n  func Divide ({alias}:left, {alias}:right) → int\n    let quotient = left / right\n    return quotient";
        var diagnostics = new DiagnosticBag();
        new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("Example.Int32")]
    [InlineData("System.int32")]
    [InlineData("System.Int32Thing")]
    public void ClrNumericAliases_DoNotResolveLookalikeTypeNames(string name)
        => Assert.Null(TypeChecking.PrimitiveType.FromName(name));

    [Theory]
    [InlineData("byte[]", 3)]
    [InlineData("int[]", 3)]
    [InlineData("int[,]", 6)]
    [InlineData("string", 3)]
    public void NativeLength_HasIntegerTypeInOrdinaryLoopConditions(string type, int expected)
    {
        var authored = $$"""
            module Lengths
              func Count ({{type}}:values) → int
                ! mut
                var int:i = 0
                while i < (values).Length
                  set i i + 1
                return i
            """;
        object input = type switch
        {
            "byte[]" => new byte[] { 1, 2, 3 },
            "int[]" => new[] { 1, 2, 3 },
            "int[,]" => new int[2, 3],
            "string" => "Ωab",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Count");
            Assert.Equal(expected, method.Invoke(null, [input]));
        }
    }

    [Theory]
    [InlineData("byte[]", "Count")]
    [InlineData("string", "length")]
    [InlineData("int", "Length")]
    public void NativeLength_DoesNotPermitUnmodeledMembersOrReceivers(string type, string member)
    {
        var source = $"module Lengths\n  func Count ({type}:values) → int\n    while (values).{member} > 0\n      return 1\n    return 0";
        var diagnostics = new DiagnosticBag();
        var module = Parse(source);
        Assert.Contains(Assert.Single(module.Functions).Body, statement => statement is WhileStatementNode);
        new TypeChecking.TypeChecker(diagnostics).Check(module);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("byte[]")]
    [InlineData("string")]
    public void NativeLength_DoesNotBecomeABooleanOrAnUnknownType(string type)
    {
        var source = $"module Lengths\n  func Count ({type}:values) → int\n    while (values).Length\n      return 1\n    return 0";
        var diagnostics = new DiagnosticBag();
        var module = Parse(source);
        Assert.Contains(Assert.Single(module.Functions).Body, statement => statement is WhileStatementNode);
        new TypeChecking.TypeChecker(diagnostics).Check(module);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

    [Fact]
    public void MetadataAliases_FollowLexicalAncestryRatherThanEqualNamespaceNames()
    {
        var parsed = Parse("""
            module ScopedAliases
              class Left
                func Make () → object
                  return new Builder()
              class Right
                func Make () → object
                  return new Builder()
              class Outside
                func Make () → object
                  return new Builder()
            """);
        var scopes = new[]
        {
            new NamespaceScopeInfo("outer", "Shared", "Shared", null, false, TextSpan.Empty),
            new NamespaceScopeInfo("left", "Repeated", "Shared.Repeated", "outer", false, TextSpan.Empty),
            new NamespaceScopeInfo("right", "Repeated", "Shared.Repeated", "outer", false, TextSpan.Empty)
        };
        var ids = new[] { "left", "right", "outer" };
        for (var index = 0; index < parsed.Classes.Count; index++)
        {
            parsed.Classes[index].NamespaceScopeId = ids[index];
            parsed.Classes[index].NamespaceIdentity = scopes.Single(scope => scope.Id == ids[index]).FullName;
        }
        var module = parsed.With(update =>
        {
            update.NamespaceScopes = scopes;
            update.Usings =
            [
                new UsingDirectiveNode(TextSpan.Empty, "System.Version", "Builder",
                    namespaceScopeId: "outer"),
                new UsingDirectiveNode(TextSpan.Empty, "System.Text.StringBuilder", "Builder",
                    namespaceScopeId: "left"),
                new UsingDirectiveNode(TextSpan.Empty, "System.IO.StringWriter", "Builder",
                    namespaceScopeId: "right")
            ];
        });
        var bound = new Binder(new DiagnosticBag()).Bind(module);
        var names = bound.Functions.Select(function =>
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(
                Assert.IsType<BoundNewExpression>(
                    Assert.IsType<BoundReturnStatement>(Assert.Single(function.Body)).Expression).Type)
            .RoslynSymbol!.ToDisplayString()).ToArray();
        Assert.Equal(new[] { "System.Text.StringBuilder", "System.IO.StringWriter", "System.Version" }, names);
    }

    [Theory]
    [InlineData("using StringBuilder = Missing.StringBuilder")]
    [InlineData("using StringBuilder = Other\n  using Other = System.Text.StringBuilder")]
    public void MetadataAliases_DoNotGuessMissingTargetsOrChainSiblingAliases(string imports)
    {
        var module = Parse($$"""
            module InvalidAlias
              {{imports}}
              func Make () → object
                return new StringBuilder()
            """);
        var bound = new Binder(new DiagnosticBag()).Bind(module);
        var creation = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(Assert.Single(bound.Functions).Body)).Expression);
        Assert.Null(Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(creation.Type).RoslynSymbol);
    }

    [Fact]
    public void MetadataAliases_RetainConstructedGenericTypeAndGetterIdentity()
    {
        const string authored = """
            module GenericAlias
              using Items = System.Collections.Generic.List<int>
              func Read () → int
                ! alloc
                let Items:items = new Items()
                return items.Count
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Read");
            Assert.Equal(0, method.Invoke(null, null));
            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            var created = Assert.IsType<BoundNewExpression>(
                Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]).Initializer);
            Assert.Equal("System.Collections.Generic.List<int>",
                Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(created.Type).RoslynSymbol!.ToDisplayString());
        }
    }

    [Fact]
    public void MetadataImports_DoNotReplaceAShadowingSourceTypeWithABclType()
    {
        const string source = """
            module SourceShadow
              using System.Text
              class StringBuilder
                field int:Length public = 17
              func Read () → int
                ! alloc
                let StringBuilder:item = new StringBuilder()
                return item.Length
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var created = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundBindStatement>(bound.Functions.Single(function => function.Symbol.Name == "Read").Body[0]).Initializer);
        Assert.NotNull(created.ResolvedType);
        Assert.Null(Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(created.Type).RoslynSymbol);
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Read");
        Assert.Equal(17, method.Invoke(null, null));
    }

    [Theory]
    [InlineData("Text = System.Text", "Text.StringBuilder")]
    [InlineData("Builder = System.Text.StringBuilder", "Builder")]
    [InlineData("System.Text", "StringBuilder")]
    public void NativeNamespaceImports_RetainActualMetadataConstructorAndPropertyIdentity(string import, string typeName)
    {
        var source = $$"""
            module NamespaceFile Contoso.Tools
              using {{import}}
              class Box
                func Read () → int
                  ! alloc
                  let {{typeName}}:builder = new {{typeName}}("example")
                  return builder.Length
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var binding = Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]);
        var created = Assert.IsType<BoundNewExpression>(binding.Initializer);
        Assert.Equal("System.Text.StringBuilder",
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(created.Type).RoslynSymbol!.ToDisplayString());
        var graph = Calor.Compiler.Analysis.CallGraphAnalysis.Build(Parse(source));
        var callerId = Assert.Single(graph.Functions).Key;
        Assert.NotNull(graph.MetadataCreationType(callerId, created.Span));
        var returned = Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[1]);
        var member = Assert.IsType<BoundFieldAccessExpression>(returned.Expression);
        Assert.NotNull(graph.MetadataValueType(callerId, member.Target.Span));
        var effects = new DiagnosticBag();
        new Calor.Compiler.Effects.EffectEnforcementPass(effects).Enforce(Parse(source));
        Assert.False(effects.HasErrors, string.Join(Environment.NewLine, effects));
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var type = CompileAssembly(result.GeneratedCode).GetType("Contoso.Tools.Box")!;
        var method = type.GetMethod("Read", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert.Equal(7, method.Invoke(Activator.CreateInstance(type), null));
    }

    [Fact]
    public void EscapedDefaultBinding_RemainsAVariableRatherThanADefaultInitializer()
    {
        const string authored = """
            module DefaultIdentifier
              func Run (int:@default) → int
                let int:value = @default
                return value
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Run");
            Assert.Equal(31, method.Invoke(null, [31]));
        }
    }

    [Theory]
    [InlineData("W1-F1-refout", false)]
    [InlineData("W1-F1-refout", true)]
    [InlineData("W1-F1-outvar-statement", false)]
    [InlineData("W1-F1-outvar-statement", true)]
    public void OriginalByReferenceFixtures_RetainNativeConverterCapability(string fixture, bool passthrough)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var source = File.ReadAllText(Path.Combine(root, "tests", "TestData", "InteropScope", "W1",
            "fixtures", fixture + ".cs.txt"));
        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            RescueUnusableMembers = true,
            PassthroughOnError = passthrough
        }).Convert(source);
        Assert.True(conversion.Success, string.Join(Environment.NewLine, conversion.Issues)
            + Environment.NewLine + conversion.CalorSource);
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        Assert.Equal(0, conversion.DropCount);
        Assert.DoesNotContain("@default", conversion.CalorSource);
        var compiled = Compile(conversion.CalorSource!);
        Assert.False(compiled.HasErrors, conversion.CalorSource + Environment.NewLine
            + string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
        var methods = CompileAssembly(compiled.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        if (fixture == "W1-F1-refout")
            Assert.Equal(44, methods.Single(method => method.Name == "Run").Invoke(null, null));
        else
        {
            var parse = methods.Single(method => method.Name == "ParseOrZero");
            Assert.Equal(42, parse.Invoke(null, ["42"]));
            Assert.Equal(0, parse.Invoke(null, ["bad"]));
        }
    }

    [Fact]
    public void NativeReferenceDefaults_RetainSourceAndMetadataOverloadIdentityAfterReemission()
    {
        const string authored = """
            module Defaults
              func Pick (object:value) → int
                return 1
              func Pick (string?:value) → int
                return 2
              func ObjectChoice () → int
                return Pick(default(object))
              func StringChoice () → int
                return Pick(default(string))
              func ObjectText () → string?
                return System.Convert.ToString(default(object))
              func StringText () → string?
                return System.Convert.ToString(default(string))
            """;
        var module = Parse(authored);
        string ReemitDefault(string functionName)
        {
            var statement = Assert.IsType<ReturnStatementNode>(
                module.Functions.Single(function => function.Name == functionName).Body[0]);
            return Assert.IsType<CallExpressionNode>(statement.Expression).Arguments[0].Accept(new CalorEmitter());
        }
        var objectDefault = ReemitDefault("ObjectChoice");
        var stringDefault = ReemitDefault("StringChoice");
        var reemitted = authored.Replace("default(object)", objectDefault, StringComparison.Ordinal)
            .Replace("default(string)", stringDefault, StringComparison.Ordinal);
        foreach (var source in new[] { authored, reemitted })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var methods = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .ToArray();
            Assert.Equal(1, methods.Single(method => method.Name == "ObjectChoice").Invoke(null, null));
            Assert.Equal(2, methods.Single(method => method.Name == "StringChoice").Invoke(null, null));
            Assert.Equal("", methods.Single(method => method.Name == "ObjectText").Invoke(null, null));
            Assert.Null(methods.Single(method => method.Name == "StringText").Invoke(null, null));

            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            foreach (var (name, selectedIndex) in new[] { ("ObjectChoice", 0), ("StringChoice", 1) })
            {
                var function = bound.Functions.Single(function => function.Symbol.Name == name);
                var call = Assert.IsType<BoundCallExpression>(
                    Assert.IsType<BoundReturnStatement>(function.Body[0]).Expression);
                Assert.Equal(bound.Functions[selectedIndex].Symbol.Id, call.ResolvedSymbol!.Id);
            }
            foreach (var (name, expectedType) in new[]
            {
                ("ObjectText", SpecialType.System_Object), ("StringText", SpecialType.System_String)
            })
            {
                var function = bound.Functions.Single(function => function.Symbol.Name == name);
                var call = Assert.IsType<BoundCallExpression>(
                    Assert.IsType<BoundReturnStatement>(function.Body[0]).Expression);
                Assert.Equal("System.Convert", call.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
                Assert.Equal(expectedType, Assert.Single(call.ResolvedMetadataMethod.Parameters).Type.SpecialType);
                Assert.Equal(Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Annotated,
                    Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(call.Arguments[0].Type).NullableAnnotation);
            }
        }
    }

    [Fact]
    public void NativeStringDefault_DoesNotAcquireANonNullGuaranteeFromItsCast()
    {
        var binding = Compile("""
            module DefaultNull
              func Run () → string
                let string:value = default(string)
                return value
            """);
        Assert.Contains(binding.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableToNonNullableBinding);
        var returning = Compile("""
            module DefaultNull
              func Run () → string
                return default(string)
            """);
        Assert.Contains(returning.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Fact]
    public void NativeInventory_ByReferenceCallsRetainMetadataThroughTheProjectDriver()
    {
        const string source = """
            module Inventory overflow unchecked
              using System.Collections.Generic

              module Inventory
                func ParseOr (string:text, int:fallback) → int
                  var int:n = default(int)
                  if int.TryParse(text, out n)
                    return n
                  return fallback

                func CountOf (Dictionary<string,int>:stock, string:item) → int
                  var int:count = default(int)
                  stock.TryGetValue(item, out count)
                  return count

                func IsNumber (string:text) → bool
                  return int.TryParse(text, out _)

                func Bump (ref int:x) → void
                  set x (x + 1)

                func Twice (in int:x) → int
                  return x * 2

                func Run () → int
                  var int:total = 1
                  Bump(ref total)
                  return Twice(in total) + ParseOr("40", 0)
            """;
        var directory = Path.Combine(Path.GetTempPath(), "calor-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = new FileInfo(Path.Combine(directory, "inventory.calr"));
            File.WriteAllText(file.FullName, source);
            var diagnostics = new DiagnosticBag();
            var result = CompilationDriver.CompileAll([file],
                _ => new CompilationOptions { StatusWriter = TextWriter.Null },
                crossModuleEnforcement: true, crossModulePolicy: Calor.Compiler.Effects.UnknownCallPolicy.Strict,
                diagnosticSink: diagnostics);
            Assert.False(result.AnyErrors, string.Join(Environment.NewLine, diagnostics));
            Assert.Empty(result.Skipped);
            var compiled = Assert.Single(result.Compiled);
            var methods = CompileAssembly(compiled.Result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .ToArray();
            Assert.Equal(44, methods.Single(method => method.Name == "Run").Invoke(null, null));
            var parse = methods.Single(method => method.Name == "ParseOr");
            Assert.Equal(13, parse.Invoke(null, ["13", 7]));
            Assert.Equal(7, parse.Invoke(null, ["bad", 7]));
            var count = methods.Single(method => method.Name == "CountOf");
            var stock = new Dictionary<string, int> { ["found"] = 9 };
            Assert.Equal(9, count.Invoke(null, [stock, "found"]));
            Assert.Equal(0, count.Invoke(null, [stock, "missing"]));
            var isNumber = methods.Single(method => method.Name == "IsNumber");
            Assert.Equal(true, isNumber.Invoke(null, ["13"]));
            Assert.Equal(false, isNumber.Invoke(null, ["bad"]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("int[]", "new int[] {10, 20, 30, 40}")]
    [InlineData("string", "\"abcd\"")]
    [InlineData("string[]", "new string[] {\"a\", \"b\", \"c\", \"d\"}")]
    public void NativeSlices_RetainTheirContainerType(string typeName, string value)
    {
        var source = $$"""
            module Slices
              func Slice ({{typeName}}:values) → {{typeName}}
                ! alloc
                let {{typeName}}:copy = values[1..^1]
                return copy
              func Direct () → {{typeName}}
                ! alloc
                return ({{value}})[1..^1]
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var assembly = CompileAssembly(result.GeneratedCode);
        var methods = assembly.GetTypes().SelectMany(type => type.GetMethods()).ToArray();
        var direct = methods.Single(method => method.Name == "Direct").Invoke(null, null);
        if (typeName == "int[]")
            Assert.Equal(new[] {20, 30}, Assert.IsType<int[]>(direct));
        else if (typeName == "string[]")
            Assert.Equal(new[] {"b", "c"}, Assert.IsType<string[]>(direct));
        else
            Assert.Equal("bc", direct);
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var slice = Assert.IsType<BoundArrayAccess>(
            Assert.IsType<BoundBindStatement>(bound.Functions[0].Body[0]).Initializer);
        if (typeName.EndsWith("[]", StringComparison.Ordinal))
            Assert.IsAssignableFrom<IArrayTypeSymbol>(slice.MetadataArrayResultType);
        else
            Assert.Equal(SpecialType.System_String,
                Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(slice.Type).RoslynSymbol!.SpecialType);
    }

    [Theory]
    [InlineData("values[\"bad\"..]")]
    [InlineData("values[1..true]")]
    [InlineData("values[^\"bad\"]")]
    public void NativeSlices_InvalidEndpointTypesRemainErrors(string expression)
    {
        var source = $"module InvalidSlice\n  func Slice (int[]:values) → int[]\n    ! alloc\n    return {expression}";
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("let List<string>:lines = File.ReadAllLines(path)\n    return lines.Count", "int")]
    [InlineData("return File.ReadAllLines(path)", "List<string>")]
    [InlineData("let List<string>:lines = System.IO.File.ReadAllLines(path)\n    return lines.Count", "int")]
    public void NativeArrayResults_RetainTheCalorLayerCollectionDiagnostic(string body, string output)
    {
        var source = $"module Files\n  func Get (string:path) → {output}\n    ! fs:r\n    {body}";
        var result = Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.BindArrayToConcreteCollection);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.CodeGenCompilationError);
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var statement = Assert.Single(bound.Functions).Body[0];
        var call = Assert.IsType<BoundCallExpression>(statement is BoundBindStatement binding
            ? binding.Initializer : Assert.IsType<BoundReturnStatement>(statement).Expression);
        Assert.Equal("System.IO.File", call.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
        Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod.ReturnType);
        Assert.NotNull(call.MetadataArrayResultType);
    }

    [Fact]
    public void NativeCharacterContract_RetainsTheUnicodePredicateAndActualMetadataTarget()
    {
        const string authored = """
            module AgentReference41
              func DigitValue (char:c) → int
                !
                requires char.IsDigit(c)
                return c - '0'
            """;
        var module = Parse(authored);
        var predicate = Assert.Single(Assert.Single(module.Functions).Preconditions).Condition.Accept(new CalorEmitter());
        foreach (var source in new[] { authored, authored.Replace("char.IsDigit(c)", predicate, StringComparison.Ordinal) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            Assert.Contains("char.IsDigit", result.GeneratedCode);
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "DigitValue");
            Assert.Equal(5, method.Invoke(null, ['5']));
            Assert.Equal('\u0665' - '0', method.Invoke(null, ['\u0665']));
        }
    }

    [Fact]
    public void StringIndexReceiver_UsesTheSelectedCharToStringMethodWithoutUnknownEffects()
    {
        const string authored = """
            module M
              func Test private (string:text) → string
                return text[0].ToString()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(authored));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.Equal(SpecialType.System_Char, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
        Assert.Equal("ToString", call.ResolvedMetadataMethod.Name);
        var result = Compile(authored);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Test");
        Assert.Equal("Ω", method.Invoke(null, ["Ωvalue"]));
    }

    [Theory]
    [InlineData("char")]
    [InlineData("int")]
    public void MissingEscapedPrimitiveReceiver_DoesNotBecomeAStaticMetadataType(string name)
    {
        var source = $"module M\n  func Test () → string\n    return @{name}.ToString()";
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.IsNotType<BoundTypeReferenceExpression>(call.Receiver);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Compile(source).HasErrors);
    }

    [Fact]
    public void EscapedPrimitiveValueReceiver_RetainsItsValueIdentity()
    {
        const string source = """
            module M
              func Test (char:@char) → string
                return @char.ToString()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.IsType<BoundVariableExpression>(call.Receiver);
        Assert.Equal(SpecialType.System_Char, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Contains("@char.ToString()", result.GeneratedCode);
    }

    [Fact]
    public void DeclaredGenericList_DoesNotAcquireTheBclCollectionIdentity()
    {
        const string source = """
            module M
              class List<T>
                func Read (string:path) → List<string>
                  ! fs:r
                  return File.ReadAllLines(path)
            """;
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(Parse(source));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.BindArrayToConcreteCollection);
    }

    [Fact]
    public void ByteListCalls_RetainExactMetadataOverloadsAndArrayResult()
    {
        const string source = """
            module Codec
              func Encode (List<byte>:bytes, byte:item) → byte[]
                ! mut
                bytes.Add(item)
                return bytes.ToArray()
              func Write (List<byte>:bytes, byte:item, string:path) → void
                ! mut, fs:w
                bytes.Add(item)
                File.WriteAllBytes(path, bytes.ToArray())
              func WriteEncoded (List<byte>:bytes, byte:item, string:path) → void
                ! mut, fs:w
                File.WriteAllBytes(path, Encode(bytes, item))
            """;
        var metadataDiagnostics = new DiagnosticBag();
        var metadataBound = new Binder(metadataDiagnostics).Bind(Parse(source));
        var write = metadataBound.Functions.Single(function => function.Symbol.Name == "WriteEncoded");
        var writeCall = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(write.Body[0]).Expression);
        Assert.True(writeCall.ResolvedMetadataMethod is not null,
            $"Array argument: {writeCall.Arguments[1].Type.DisplayString}; "
            + $"input element: {writeCall.Arguments[1].MethodInputArrayType?.ElementType.DisplayString}");
        Assert.Equal("System.IO.File", writeCall.ResolvedMetadataMethod.ContainingType.ToDisplayString());
        Assert.Equal(SpecialType.System_Byte, Assert.IsAssignableFrom<IArrayTypeSymbol>(
            writeCall.ResolvedMetadataMethod.Parameters[1].Type).ElementType.SpecialType);
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Encode");
        var values = new List<byte> { 7 };
        Assert.Equal(new byte[] { 7, 255 }, Assert.IsType<byte[]>(method.Invoke(null, [values, (byte)255])));
        var path = Path.Combine(Path.GetTempPath(), $"calor-byte-codec-{Guid.NewGuid():N}.bin");
        try
        {
            method.DeclaringType!.GetMethod("WriteEncoded")!.Invoke(null, [new List<byte> { 2 }, (byte)254, path]);
            Assert.Equal(new byte[] { 2, 254 }, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var encode = bound.Functions.Single(function => function.Symbol.Name == "Encode");
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(encode.Body[1]).Expression);
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod!.ReturnType);
        Assert.Equal(SpecialType.System_Byte, array.ElementType.SpecialType);
        Assert.Equal("System.Collections.Generic.List<byte>", call.ResolvedMetadataMethod.ContainingType.ToDisplayString());
    }

    [Fact]
    public void SourceGenericListCalls_RetainTheSourceTypeParameter()
    {
        const string source = """
            module GenericCollection
              func Append<T> (List<T>:items, T:item) → T[]
                ! mut
                items.Add(item)
                return items.ToArray()
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Append").MakeGenericMethod(typeof(string));
        Assert.Equal(new[] { "first", "second" },
            Assert.IsType<string[]>(method.Invoke(null, [new List<string> { "first" }, "second"])));
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var add = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.Equal("Add", add.ResolvedMetadataMethod!.Name);
        Assert.Equal("T", Assert.IsAssignableFrom<ITypeParameterSymbol>(
            Assert.Single(add.ResolvedMetadataMethod.Parameters).Type).Name);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[1]).Expression);
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod!.ReturnType);
        var parameter = Assert.IsAssignableFrom<ITypeParameterSymbol>(array.ElementType);
        Assert.Equal("T", parameter.Name);
        Assert.Equal(TypeKind.TypeParameter, parameter.TypeKind);
    }

    [Fact]
    public void ClassGenericListCalls_RetainInterfaceConstraints()
    {
        const string source = """
            module GenericClass
              class Repository<T>
                where T : IDisposable
                func Append (List<T>:items, T:item) → T[]
                  ! mut
                  items.Add(item)
                  return items.ToArray()
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var append = Assert.Single(bound.Functions);
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(append.Body[1]).Expression);
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod!.ReturnType);
        var parameter = Assert.IsAssignableFrom<ITypeParameterSymbol>(array.ElementType);
        Assert.Equal("System.IDisposable", Assert.Single(parameter.ConstraintTypes).ToDisplayString());
        var type = CompileAssembly(result.GeneratedCode).GetTypes().Single(type => type.Name == "Repository`1")
            .MakeGenericType(typeof(MemoryStream));
        using var item = new MemoryStream();
        Assert.Same(item, Assert.Single(Assert.IsType<MemoryStream[]>(
            type.GetMethod("Append")!.Invoke(Activator.CreateInstance(type), [new List<MemoryStream>(), item]))));
    }

    [Fact]
    public void SourceGenericList_DoesNotSubstituteObjectForAnInvalidArgument()
    {
        const string source = """
            module GenericCollection
              func Append<T> (List<T>:items) → void
                ! mut
                items.Add("not an arbitrary T")
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTryParseOut_RetainsLocalWritesAndDiscard(bool discard)
    {
        var source = $$"""
            module NativeOut
              func ParseNumber (string:text) → int
                var int:n = 0
                if int.TryParse(text, out {{(discard ? "_" : "n")}})
                  return {{(discard ? "1" : "n")}}
                return -1
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "ParseNumber");
        Assert.Equal(discard ? 1 : 42, method.Invoke(null, ["42"]));
        Assert.Equal(-1, method.Invoke(null, ["invalid"]));
    }

    [Fact]
    public void NativeRefAndIn_RetainStorageAndRejectNonLvalues()
    {
        const string source = """
            module NativeByRef
              func Increment (ref int:x) → void
                ! mut
                set x x + 1
              func Read (in int:x) → int
                return x
              func Run () → int
                ! mut
                var int:n = 41
                Increment(ref n)
                return Read(in n)
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Run");
        Assert.Equal(42, method.Invoke(null, null));
        var invalid = Compile(source.Replace("Increment(ref n)", "Increment(ref (n + 1))", StringComparison.Ordinal));
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidByReferenceOperand);
    }

    [Fact]
    public void NativeCallDiscard_DoesNotHideEscapedReferencesOrInvalidRefOperands()
    {
        const string source = """
            module NativeDiscard
              func ParseNumber (string:text) → bool
                return int.TryParse(text, out _)
            """;
        var escaped = Compile(source.Replace("out _", "out @_", StringComparison.Ordinal));
        Assert.Contains(escaped.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        var invalidRef = Compile(source.Replace("out _", "ref _", StringComparison.Ordinal));
        Assert.Contains(invalidRef.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidByReferenceOperand);
    }

    [Fact]
    public void NativeRecordClone_PreservesTheOriginalAndActualCopy()
    {
        const string source = """
            module RecordClone
              record Item (int:X, int:Y)
              func Copy (Item:value) → Item
                ! alloc, mut
                return value with { X = value.X + 1 }
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var assembly = CompileAssembly(result.GeneratedCode);
        var type = assembly.GetTypes().Single(type => type.Name == "Item");
        var original = Activator.CreateInstance(type, 4, 8);
        var method = assembly.GetTypes().SelectMany(type => type.GetMethods()).Single(method => method.Name == "Copy");
        var clone = method.Invoke(null, [original]);
        Assert.NotSame(original, clone);
        Assert.Equal(4, type.GetProperty("X")!.GetValue(original));
        Assert.Equal(5, type.GetProperty("X")!.GetValue(clone));
        Assert.Equal(8, type.GetProperty("Y")!.GetValue(clone));
        var pure = Compile(source.Replace("! alloc, mut", "!", StringComparison.Ordinal));
        Assert.Contains(pure.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    private static CompilationResult Compile(string source) => Program.Compile(source, "native-metadata.calr",
        new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }

    private static Assembly CompileAssembly(string source)
    {
        var compilation = CSharpCompilation.Create("NativeMetadata_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, source + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
