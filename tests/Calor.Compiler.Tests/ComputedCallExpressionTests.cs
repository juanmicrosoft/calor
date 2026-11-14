using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using BoundTypes = Calor.Compiler.Binding.BoundTypes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class ComputedCallExpressionTests
{
    [Theory]
    [InlineData("IEnumerable<Node?>", false, DiagnosticCode.NullableReturnFromNonNullable)]
    [InlineData("List<Node?>", false, DiagnosticCode.NullableReturnFromNonNullable)]
    [InlineData("List<Node>?", false, DiagnosticCode.NullableArgumentToNonNullableParameter)]
    [InlineData("IEnumerable<Node>", false, null)]
    [InlineData("List<Node>", false, null)]
    [InlineData("List<Node>?", true, null)]
    [InlineData("IEnumerable<Node>?", true, null)]
    [InlineData("List<Node?>", true, DiagnosticCode.NullableReturnFromNonNullable)]
    public void ContextualSourceGenericNullability_ToListRetainsSelectedFactsWithoutEffectPermission(
        string input, bool guarded, string? receivingError)
    {
        var source = $$"""
            module ContextualSource Calls
              using System.Linq
              using System.Collections.Generic
              class Node
                field int:Id public
              func Use ({{input}}:values) → List<Node>
                ! throw
                {{(guarded ? "if values != null\n      return values.ToList()\n    throw null" : "return values.ToList()")}}
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var (ast, bound, diagnostics) = Bind(candidate);
            var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
            var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
            Assert.NotNull(selected.ReducedFrom);
            Assert.Equal("System.Linq.Enumerable", selected.ContainingType.ToDisplayString());
            Assert.Empty(selected.DeclaringSyntaxReferences);
            var payload = Assert.IsAssignableFrom<INamedTypeSymbol>(Assert.Single(selected.TypeArguments));
            Assert.Equal("Calls.Node", payload.ToDisplayString().TrimEnd('?'));
            Assert.True(Assert.Single(payload.Locations).IsInSource);
            Assert.Equal(input.Contains("Node?", StringComparison.Ordinal)
                ? Microsoft.CodeAnalysis.NullableAnnotation.Annotated
                : Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated, payload.NullableAnnotation);
            var receiver = Assert.IsAssignableFrom<INamedTypeSymbol>(call.ResolvedMetadataReceiverType);
            Assert.Equal(input.StartsWith("List", StringComparison.Ordinal) ? "List" : "IEnumerable", receiver.Name);
            Assert.Same(call.Receiver, call.EvaluatedReceiver);
            Assert.Empty(call.Arguments);
            Assert.Empty(call.ArgumentParameterIndices!);
            var result = Assert.IsType<BoundTypes.GenericInstantiationBoundType>(call.GenericNullabilityType);
            Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(payload,
                Assert.IsType<BoundTypes.NominalBoundType>(Assert.Single(result.TypeArguments)).RoslynSymbol));
            if (receivingError is null)
                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code is
                    DiagnosticCode.NullableReturnFromNonNullable or DiagnosticCode.NullableArgumentToNonNullableParameter);
            else
                Assert.Contains(diagnostics, diagnostic => diagnostic.Code == receivingError
                    && diagnostic.IsError && BindingDiagnosticPolicy.IsCompilationError(diagnostic));

            // The effect row comes from the restored 0.29 System.Linq namespace contract
            // (pure), not from the nullability selection above: ToList charges nothing.
            var effects = new DiagnosticBag();
            new EffectEnforcementPass(effects, resolver: new EffectResolver(
                new ManifestLoader(loadUserLevelManifests: false))).Enforce(ast);
            Assert.DoesNotContain(effects, diagnostic => diagnostic.Code is
                DiagnosticCode.UnknownExternalCall or DiagnosticCode.ForbiddenEffect);
            foreach (var typing in new[] { false, true })
            {
                var compiled = Program.Compile(candidate, "contextual-source.calr",
                    new CompilationOptions { EnableTypeChecking = typing });
                Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code is
                    DiagnosticCode.UnknownExternalCall or DiagnosticCode.ForbiddenEffect);
                if (receivingError is not null)
                {
                    Assert.True(compiled.HasErrors);
                    Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == receivingError
                        && diagnostic.IsError && BindingDiagnosticPolicy.IsCompilationError(diagnostic));
                }
                else
                {
                    Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
                    Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code is
                        DiagnosticCode.NullableReturnFromNonNullable or DiagnosticCode.NullableArgumentToNonNullableParameter);
                }
            }
        }
    }

    [Theory]
    [InlineData("values.Concat(second: values)", new[] { 0 })]
    [InlineData("Enumerable.Concat(second: values, first: values)", new[] { 1, 0 })]
    [InlineData("Enumerable.ToList(source: values)", new[] { 0 })]
    public void ContextualSourceGenericNullability_PreservesTheSelectedNamedMap(string expression, int[] indices)
    {
        var source = $$"""
            module ContextualNamedSource
              using System.Linq
              using System.Collections.Generic
              class Node
              func Use (IEnumerable<Node>:values) → object
                !
                return {{expression}}
            """;
        var (ast, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.NotNull(call.ResolvedMetadataMethod);
        Assert.Equal(indices, call.ArgumentParameterIndices);
        var selection = Assert.Single(CallGraphAnalysis.ResolveOperatorCalls(ast).ContextualMetadataCalls).Value;
        Assert.Equal(indices, selection.Arguments.Select(argument => argument.Parameter.Ordinal));
        Assert.All(selection.Arguments, argument => Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(
            argument.TargetType, argument.Parameter.Type)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid-image")]
    [InlineData("invalid-configuration")]
    [InlineData("ambiguous")]
    [InlineData("user-conversion")]
    [InlineData("field-receiver")]
    public void ContextualSourceGenericNullability_UnsupportedContextsStayUnselected(string boundary)
    {
        var source = $$"""
            module RejectedContextualSource
              using System.Linq
              using System.Collections.Generic
              class Node
              {{(boundary == "ambiguous" ? """
              module First
                func ToList (this IEnumerable<Node>:value) → List<Node>
                  !
                  return null
              module Second
                func ToList (this IEnumerable<Node>:value) → List<Node>
                  !
                  return null
              """.Replace("\n", "\n  ", StringComparison.Ordinal) : "")}}
              class Holder
                field List<Node>:Values public
                op implicit (Holder:value) → IEnumerable<Node>
                  !
                  return null
              func Use (IEnumerable<Node>:values, Holder:holder) → object
                !
                {{(boundary == "invalid-image" ? "let marker = Missing()" : "")}}
                {{(boundary == "invalid-configuration" ? "pp FEATURE\n      let marker = Missing()\n    pp else\n      let marker = 0" : "")}}
                return {{(boundary switch
                {
                    "missing" => "values.Missing()",
                    "user-conversion" => "Enumerable.ToList(holder)",
                    "field-receiver" => "holder.Values.ToList()",
                    _ => "values.ToList()"
                })}}
            """;
        var (ast, bound, _) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.ResolvedMethodName is "Missing" or "ToList");
        Assert.Null(call.ResolvedMetadataMethod);
        if (boundary == "ambiguous")
        {
            var image = CSharpCompilation.Create("AmbiguousSourceContainers",
                [CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(ast))], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.Contains(image.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0121");
        }
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
    }

    [Fact]
    public void ContextualSourceGenericNullability_StillEvaluatesReceiverArgumentsAndCallback()
    {
        const string source = """
            module ContextualSourceEvaluation
              using System.Linq
              using System.Collections.Generic
              class Node
              func Receiver (IEnumerable<Node>:values) → IEnumerable<Node>
                ! cw
                System.Console.WriteLine("receiver")
                return values
              func Argument (IEnumerable<Node>:values) → IEnumerable<Node>
                ! cw
                System.Console.WriteLine("argument")
                return values
              func Loud (Node:value) → Node
                ! cw
                System.Console.WriteLine("callback")
                return value
              func Use (IEnumerable<Node>:values) → List<Node>
                !
                return Receiver(values).Concat(second: Argument(values)).Select((Node:value) ! cw => Loud(value)).ToList()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
        Assert.Equal(new[] { "Concat", "Select", "ToList" }, calls
            .Where(call => call.ResolvedMetadataMethod?.ContainingType.ToDisplayString() == "System.Linq.Enumerable")
            .Select(call => call.ResolvedMetadataMethod!.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Argument", "Loud", "Receiver" }, calls
            .Where(call => call.ResolvedSymbol is not null).Select(call => call.ResolvedSymbol!.Name)
            .Order(StringComparer.Ordinal));
        // Concat, Select and ToList resolve to the restored 0.29 System.Linq contract
        // (pure), so the default compile charges exactly the receiver, argument and
        // callback 'cw' effects, and declaring 'cw' is enough.
        var compiled = Program.Compile(source);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
        var permissive = Program.Compile(source, null, new CompilationOptions
        {
            UnknownCallPolicy = UnknownCallPolicy.Permissive,
            StatusWriter = TextWriter.Null
        });
        Assert.Contains(permissive.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
        var declared = Program.Compile(source.Replace("    !\n    return Receiver", "    ! cw\n    return Receiver",
            StringComparison.Ordinal));
        Assert.False(declared.HasErrors, string.Join("\n", declared.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextualSourceGenericNullability_ConfigurationIdentityIsNotANameGuess(bool conflicting)
    {
        var source = $$"""
            module ContextualSourceConfigurations Cases
              using System.Linq
              using System.Collections.Generic
              pp FEATURE
                using Selected = {{(conflicting ? "Cases.Other" : "Cases")}}.Node
              pp else
                using Selected = Cases.Node
              class Node
              class Other
                class Node
              func Use (IEnumerable<Selected>:values) → object
                !
                return values.ToList()
            """;
        var ast = Parse(source);
        Assert.Equal(!conflicting, CallGraphAnalysis.ResolveOperatorCalls(ast).ContextualMetadataCalls.Count == 1);
        var (_, bound, _) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal(!conflicting, call.ResolvedMetadataMethod is not null);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ContextualMetadataExtension_PreservesTheActuallySelectedLambdaOverload(bool indexed, bool lifted)
    {
        var lambda = indexed ? "(value, index) ! => value.Length + index" : "(value) ! => value.Length";
        if (lifted)
            lambda = indexed ? "(string:value, int:index) ! => value.Length + index" : "(string:value) ! => value.Length";
        var source = $$"""
            module ContextualSelections
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<string>:values) → IEnumerable<int>
                !
                {{(lifted ? $"let projection = {lambda}" : "")}}
                return values.Select({{(lifted ? "projection" : lambda)}})
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.NotNull(selected.ReducedFrom);
        Assert.Equal("System.Linq.Enumerable", selected.ContainingType.ToDisplayString());
        Assert.Equal(new[] { "string", "int" }, selected.TypeArguments.Select(type => type.ToDisplayString()));
        var callback = Assert.IsAssignableFrom<INamedTypeSymbol>(Assert.Single(selected.Parameters).Type);
        Assert.Equal(indexed ? 2 : 1, callback.DelegateInvokeMethod!.Parameters.Length);
        Assert.Equal(new[] { 0 }, call.ArgumentParameterIndices);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
    }

    [Fact]
    public void ContextualMetadataExtension_PreservesTheStaticNamedArgumentMap()
    {
        const string source = """
            module ContextualNamedArguments
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<string>:values) → IEnumerable<int>
                !
                let projection = (string:value) ! => value.Length
                return Enumerable.Select(selector: projection, source: values)
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.True(call.ResolvedMetadataMethod?.IsStatic);
        Assert.Equal(new[] { 1, 0 }, call.ArgumentParameterIndices);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
    }

    [Fact]
    public void ContextualMetadataExtension_PreservesTheSelectedNullableResult()
    {
        const string source = """
            module ContextualNullableResult
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<string>:values) → IEnumerable<string?>
                !
                return values.Select((string:value) ! => value.Length == 0 ? null : value)
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.Annotated, selected.TypeArguments[1].NullableAnnotation);
        var callback = Assert.IsAssignableFrom<INamedTypeSymbol>(Assert.Single(selected.Parameters).Type);
        Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.Annotated, callback.DelegateInvokeMethod!.ReturnType.NullableAnnotation);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextualMetadataExtension_StillChargesItsEffectfulCallback(bool lifted)
    {
        const string lambda = "(string:value) ! cw => Loud(value)";
        var source = $$"""
            module ContextualEffects
              using System.Linq
              using System.Collections.Generic
              func Loud (string:value) → int
                ! cw
                System.Console.WriteLine(value)
                return value.Length
              func Use (IEnumerable<string>:values) → IEnumerable<int>
                !
                {{(lifted ? $"let projection = {lambda}" : "")}}
                return values.Select({{(lifted ? "projection" : lambda)}})
            """;
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("values.Where((string:value) ! => 1)", "IEnumerable<string>")]
    [InlineData("values.Select((string:value) ! => value)", "IEnumerable<int>")]
    [InlineData("values.Select((string:value, int:index, int:extra) ! => index)", "IEnumerable<int>")]
    public void ContextualMetadataExtension_InvalidNativeContextCannotAuthorizeACall(string expression, string output)
    {
        var source = $$"""
            module InvalidContextualSelection
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<string>:values) → {{output}}
                !
                return {{expression}}
            """;
        var (_, bound, _) = Bind(source);
        Assert.All(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => Assert.Null(call.ResolvedMetadataMethod));
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void ContextualMetadataExtension_DoesNotReselectAnExpressionTreeAsADelegate()
    {
        const string source = """
            module ExpressionTreeBoundary
              using System.Linq
              func Use (IQueryable<string>:values) → IQueryable<int>
                !
                return values.Select((string:value) ! => value.Length)
            """;
        var ast = Parse(source);
        var tree = CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(ast));
        var image = CSharpCompilation.Create("ExpressionTreeBoundary", [tree],
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(image.GetDiagnostics(), item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>());
        var actual = Assert.IsAssignableFrom<IMethodSymbol>(image.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol);
        Assert.Equal("System.Linq.Queryable", actual.ContainingType.ToDisplayString());
        Assert.Empty(CallGraphAnalysis.ResolveOperatorCalls(ast).ContextualMetadataCalls);
        var (_, bound, _) = Bind(source);
        Assert.Null(Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound)
            .OfType<BoundCallExpression>()).ResolvedMetadataMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextualMetadataExtension_RequiresAValidImageInEveryActiveConfiguration(bool valid)
    {
        var source = $$"""
            module ContextualConfigurations
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<string>:values) → IEnumerable<int>
                !
                pp FEATURE
                  let marker = {{(valid ? "1" : "Missing()")}}
                pp else
                  let marker = 0
                return values.Select((string:value) ! => value.Length)
            """;
        var (_, bound, _) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.ResolvedMethodName == "Select");
        Assert.Equal(valid, call.ResolvedMetadataMethod is not null);
        var compiled = Program.Compile(source);
        Assert.Equal(!valid, compiled.HasErrors);
    }

    [Theory]
    [InlineData("values.Count()")]
    [InlineData("values.Skip(1).Count()")]
    [InlineData("values.Concat(values).Count()")]
    public void SelectedMetadataExtension_UsesItsActualProviderIncludingChains(string expression)
    {
        var source = $$"""
            module SelectedExtensions
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<int>:values) → int
                !
                return {{expression}}
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
        Assert.NotEmpty(calls);
        Assert.All(calls, call =>
        {
            var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
            Assert.NotNull(selected.ReducedFrom);
            Assert.Equal("System.Linq.Enumerable", selected.ContainingType.ToDisplayString());
            Assert.False(selected.Locations.Any(location => location.IsInSource));
        });
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
    }

    [Theory]
    [InlineData("int", false, "cw")]
    [InlineData("int", true, "cw")]
    [InlineData("string", false, "fs:r")]
    [InlineData("string", true, "fs:r")]
    public void SelectedMetadataExtension_UsesTheConstructedUnreducedSignature(
        string elementType, bool staticCall, string effect)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module SelectedSignature
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<{{elementType}}>:values) → int
                !
                return {{(staticCall ? "Enumerable.Count(values)" : "values.Count()")}}
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(staticCall, selected.IsStatic);
        Assert.Equal(elementType, Assert.Single(selected.TypeArguments).ToDisplayString());
        var loader = new ManifestLoader(loadUserLevelManifests: false);
        loader.LoadFromJson("""
            {
              "version": "1.0",
              "mappings": [{
                "type": "System.Linq.Enumerable",
                "methods": {
                  "Count(System.Collections.Generic.IEnumerable<int>)": ["cw"],
                  "Count(System.Collections.Generic.IEnumerable<string>)": ["fs:r"]
                }
              }]
            }
            """, "selected-extension-signatures");
        new EffectEnforcementPass(diagnostics, resolver: new EffectResolver(loader)).Enforce(ast);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains($"'{effect}'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("values.Where()")]
    [InlineData("values.Where(1)")]
    [InlineData("values.Count(true)")]
    [InlineData("values.Missing()")]
    public void UnselectedMetadataExtension_CannotBorrowANameOnlyContract(string expression)
    {
        var source = $$"""
            module InvalidExtension
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<int>:values) → object
                !
                return {{expression}}
            """;
        var (_, bound, _) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void SelectedMetadataExtension_AnotherProviderUsesOnlyItsOwnAuthority()
    {
        const string source = """
            module OtherProvider
              using System.Linq
              func Use (IQueryable<int>:values) → int
                !
                return values.Count()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("System.Linq.Queryable", call.ResolvedMetadataMethod?.ContainingType.ToDisplayString());
        var loader = new ManifestLoader(loadUserLevelManifests: false);
        loader.LoadFromJson("""
            {
              "version": "1.0",
              "mappings": [{
                "type": "System.Linq.Queryable",
                "methods": { "Count(System.Linq.IQueryable<int>)": ["cw"] }
              }]
            }
            """, "selected-queryable-signature");
        var ast = Parse(source);
        new EffectEnforcementPass(diagnostics, resolver: new EffectResolver(loader)).Enforce(ast);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceExtension_NativeImageOwnsConstructedOverloadAndNamedArgumentIdentity(bool explicitType)
    {
        var source = $$"""
            module NativeSourceSelections
              module Extensions
                func Pick<T> (this string?:value, T:item, int:amount = 7) → int
                  !
                  return amount
                func Pick<T> (this object?:value, T:item, int:amount = 7) → int
                  ! cw
                  return 0
              func Use (string?:value) → int
                !
                return value.Pick{{(explicitType ? "<int>" : "")}}(amount: 9, item: 4)
            """;
        var (ast, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var caller = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(caller).OfType<BoundCallExpression>());
        Assert.True(call.IsSourceExtensionCall);
        Assert.Null(call.ResolvedMetadataMethod);
        var match = Assert.Single(call.SelectedOverloadMatches);
        Assert.Same(call.ResolvedSymbol, match.Function);
        Assert.Same(bound.SymbolsById[match.Function.Id], match.Function);
        Assert.Equal("Extensions.Pick", match.Function.Name);
        Assert.Equal(1, match.Function.GenericArity);
        Assert.Contains("STRING", match.Function.Parameters[0].TypeName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { 0, 1, 2 }, match.Arguments.Select(argument => argument.ArgumentIndex));
        Assert.Equal(new[] { 0, 2, 1 }, match.Arguments.Select(argument => argument.ParameterIndex));
        Assert.Equal("int", match.Arguments[2].ParameterType);
        Assert.Equal(new string?[] { null, "amount", "item" }, call.ArgumentNames);
        Assert.Same(call.EvaluatedReceiver, call.Arguments[0]);
        Assert.Equal(1, call.Children.Count(child => ReferenceEquals(child, call.EvaluatedReceiver)));
        Assert.Empty(bound.Functions.Single(function => function.Symbol == match.Function).DeclaredEffects);
        Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[caller.SymbolId],
            edge => edge.Callee == match.Function.Id && edge.Span == call.Span);
        var selected = CallGraphAnalysis.ResolveOperatorCalls(ast).SelectedCalls[call.Span];
        Assert.Same(ast.Classes.Single().Methods[0], selected.Declaration);
        Assert.Equal(match.Arguments, selected.Arguments);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Contains($"value.Pick{(explicitType ? "<int>" : "")}(amount: 9, item: 4)", compiled.GeneratedCode);
    }

    [Theory]
    [InlineData("! cw\n      System.Console.WriteLine(value)\n      return 1", "cw")]
    [InlineData("!\n      System.GC.KeepAlive(value)\n      return 1", "unknown")]
    public void SourceExtension_ChargesTheSelectedNativeBodyWithoutMetadataAuthority(string body, string effect)
    {
        var source = $$"""
            module NativeSourceEffects
              module Extensions
                func Count (this string?:value) → int
                  !
                  {{body}}
              func Use (string?:value) → int
                !
                return value.Count()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.IsSourceExtensionCall);
        Assert.Equal("Extensions.Count", call.ResolvedSymbol?.Name);
        Assert.Null(call.ResolvedMetadataMethod);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains($"Function 'Use' uses effect '{effect}'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceExtension_SourceAndMetadataShadowingKeepTheActuallySelectedOwner(bool instance)
    {
        var source = $$"""
            module NativeExtensionShadowing
              using System.Linq
              module Extensions
                func {{(instance ? "Contains" : "Count")}} (this string:value{{(instance ? ", string:item" : "")}}) → {{(instance ? "bool" : "int")}}
                  ! cw
                  System.Console.WriteLine(value)
                  return {{(instance ? "true" : "1")}}
              func Use (string:value) → {{(instance ? "bool" : "int")}}
                !
                return value.{{(instance ? "Contains(\"x\")" : "Count()")}}
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(
            function => function.Symbol.Name == "Use")).OfType<BoundCallExpression>());
        Assert.Equal(!instance, call.IsSourceExtensionCall);
        Assert.Equal(instance, call.ResolvedMetadataMethod is not null);
        Assert.Equal(instance, call.ResolvedSymbol is null);
        var compiled = Program.Compile(source);
        Assert.Equal(!instance, compiled.HasErrors);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        if (!instance)
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "value.Count(true)")]
    [InlineData("  module Other\n    func Count (this string:value) → int\n      !\n      return 2\n", "value.Count()")]
    [InlineData("raw", "value.Count()")]
    [InlineData("raw-only", "value.Count()")]
    public void SourceExtension_UnresolvedOrAmbiguousNativeCallsCannotBorrowLinqPurity(string extra, string expression)
    {
        var native = extra == "raw-only" ? "" : """
              module Extensions
                func Count (this string:value) → int
                  !
                  return 1

            """;
        if (extra is "raw" or "raw-only")
        {
            const string raw = "public static int Count(this string value) => 2;";
            extra = $"  module Other\n    raw member {raw.Length}\n{raw}\n";
        }
        var source = $$"""
            module NativeExtensionBoundaries
              using System.Linq
            {{native}}{{extra}}  func Use (string:value) → int
                !
                return {{expression}}
            """;
        var (ast, bound, _) = Bind(source);
        var caller = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(caller).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(call.HasOpaqueOverloads);
        Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[caller.SymbolId]);
        Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions.Single().Id, "<expression-call>", call.Span));
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        var effects = new DiagnosticBag();
        new EffectEnforcementPass(effects, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Contains(effects.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceExtension_RequiresACompleteNativeImageInEveryConfiguration(bool valid)
    {
        var source = $$"""
            module NativeExtensionConfigurations
              module Extensions partial
                func Read (this string:value) → int
                  !
                  return 1
              func Use (string:value) → int
                !
                pp FEATURE
                  let marker = {{(valid ? "1" : "MissingSibling()")}}
                pp else
                  let marker = 0
                return value.Read()
            """;
        var (_, bound, _) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.ResolvedMethodName == "Read");
        Assert.Equal(valid, call.ResolvedSymbol is not null);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Equal(!valid, Program.Compile(source).HasErrors);
    }

    [Fact]
    public void SourceExtension_AnApplicableNativeInstanceMemberStillShadowsTheExtension()
    {
        const string source = """
            module NativeInstanceShadowing
              class Holder
                func Read () → int
                  ! cw
                  System.Console.WriteLine("instance")
                  return 1
              module Extensions
                func Read (this Holder:value) → int
                  !
                  return 2
              func Use (Holder:value) → int
                !
                return value.Read()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Use")).OfType<BoundCallExpression>());
        Assert.False(call.IsSourceExtensionCall);
        Assert.Equal("Holder.Read", call.ResolvedSymbol?.Name);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Contains(Program.Compile(source).Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("string", true)]
    [InlineData("string?", false)]
    public void SourceExtension_StillChecksTheActualDeclaredReceiverParameter(string receiverType, bool mismatch)
    {
        var source = $$"""
            module SourceReceiverNullability
              module Extensions
                func Read (this {{receiverType}}:value) → int
                  !
                  return 1
              func Use (string?:value) → int
                !
                return value.Read()
            """;
        var (_, bound, diagnostics) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.True(call.IsSourceExtensionCall);
        Assert.Equal("Extensions.Read", call.ResolvedSymbol?.Name);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Equal(mismatch, diagnostics.Any(diagnostic => diagnostic.Code == "Calor0274"));
        Assert.Equal(mismatch, Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("Missing().Read(1)")]
    [InlineData("value.Read(Missing())")]
    [InlineData("(1).Read(1)")]
    public void SourceExtension_UnresolvedInputsCannotAcquireTheNativeCalleeRow(string expression)
    {
        var source = $$"""
            module UnresolvedSourceInputs
              module Extensions
                func Read (this string:value, int:amount) → int
                  !
                  return amount
              func Use (string:value) → int
                !
                return {{expression}}
            """;
        var (_, bound, _) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.ResolvedMethodName == "Read");
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void SourceExtension_ConflictingNativeDeclarationsAcrossConfigurationsRemainOpaque()
    {
        const string source = """
            module SourceSelectionConfigurations
              module Extensions
                pp FEATURE
                  func Read (this string:value) → int
                    ! cw
                    return 1
                pp else
                  func Read (this string:value) → int
                    !
                    return 2
              func Use (string:value) → int
                !
                return value.Read()
            """;
        var (_, bound, _) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(call.HasOpaqueOverloads);
        Assert.Contains(Program.Compile(source).Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void SourceExtension_DoesNotBorrowTheSameNamedMetadataContract()
    {
        const string source = """
            module SourceExtensions
              enum Mode
                First
              module Enumerable
                func Count (this Mode:value) → int
                  ! cw
                  System.Console.WriteLine("source extension")
                  return 1
              func Use (Mode:value) → int
                !
                return value.Count()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.IsSourceExtensionCall);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.NotNull(call.ResolvedSymbol);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(" ! cw", "", false, true)]
    [InlineData(" ! cw", "cw", false, false)]
    [InlineData("", "cw", false, true)]
    [InlineData(" !", "", false, false)]
    [InlineData(" ! cw", "", true, true)]
    [InlineData(" ! cw", "cw", true, false)]
    [InlineData("", "cw", true, true)]
    [InlineData(" !", "", true, false)]
    public void SelectedMetadataExtension_ChargesTheSelectedCallbackRowAndNamedArgumentMap(
        string callbackRow, string callerEffects, bool staticCall, bool error)
    {
        var source = $$"""
            module SelectedCallbackRows
              using System.Linq
              using System.Collections.Generic
              func Use (IEnumerable<int>:values, Func<int,int>:selector{{(callbackRow.Length == 0 ? " ! unknown" : callbackRow)}}) → IEnumerable<int>
                ! {{callerEffects}}
                return {{(staticCall ? "Enumerable.Select(selector: selector, source: values)" : "values.Select(selector: selector)")}}
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("Select", call.ResolvedMetadataMethod?.Name);
        Assert.Equal(staticCall ? new[] { 1, 0 } : new[] { 0 }, call.ArgumentParameterIndices);
        var compiled = Program.Compile(source);
        Assert.Equal(error, compiled.HasErrors);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        if (callbackRow.Length == 0)
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
        else if (error)
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectedMetadataExtension_StillChargesATypedLambdaValue()
    {
        const string source = """
            module TypedExtensionCallbacks
              using System.Linq
              using System.Collections.Generic
              func Loud (int:value) → int
                ! cw
                System.Console.WriteLine(value)
                return value
              func Use (IEnumerable<int>:values) → IEnumerable<int>
                !
                let Func<int,int>:selector ! cw = (int:value) ! cw => Loud(value)
                return values.Select(selector)
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.ResolvedMetadataMethod?.Name == "Select");
        Assert.NotNull(call.ResolvedMetadataMethod!.ReducedFrom);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedMetadataExtension_PreservesPolymorphicCallbackCharges(bool declaresVariable)
    {
        var source = $$"""
            module PolymorphicExtension
              using System.Linq
              using System.Collections.Generic
              func Use <! e> (IEnumerable<int>:values, Func<int,int>:selector ! e) → IEnumerable<int>
                ! {{(declaresVariable ? "e" : "")}}
                return values.Select(selector)
            """;
        var compiled = Program.Compile(source);
        Assert.Equal(!declaresVariable, compiled.HasErrors);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        if (!declaresVariable)
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("effect variable 'e'", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectedMetadataExtension_StillEvaluatesReceiverAndOrdinaryArguments()
    {
        const string source = """
            module ExtensionEvaluation
              using System.Linq
              using System.Collections.Generic
              func Receiver (IEnumerable<int>:values) → IEnumerable<int>
                ! cw
                System.Console.WriteLine("receiver")
                return values
              func Argument () → int
                ! cw
                System.Console.WriteLine("argument")
                return 1
              func Use (IEnumerable<int>:values) → int
                !
                return Receiver(values).Skip(Argument()).Count()
            """;
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    // A lambda argument lets Select bind to System.Linq.Enumerable, whose restored 0.29
    // contract is pure, so the callback's 'cw' is what fails. A method group leaves the
    // call unselected, so it stays Unknown.
    [Theory]
    [InlineData("Loud", "unknown")]
    [InlineData("(int:value) ! cw => Loud(value)", "cw")]
    public void MetadataExtension_CallbackEffectsRemainChargedEvenWithoutMetadataSelection(string callback, string charged)
    {
        var source = $$"""
            module ExtensionCallbacks
              using System.Linq
              using System.Collections.Generic
              func Loud (int:value) → int
                ! cw
                System.Console.WriteLine(value)
                return value
              func Use (IEnumerable<int>:values) → IEnumerable<int>
                !
                return values.Select({{callback}})
            """;
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Equal(charged == "unknown",
            compiled.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall));
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains($"'{charged}'", StringComparison.Ordinal));
        var permissive = Program.Compile(source, null, new CompilationOptions
        {
            UnknownCallPolicy = UnknownCallPolicy.Permissive,
            StatusWriter = TextWriter.Null
        });
        Assert.Contains(permissive.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("\"Q\"")]
    public void NativeStringIndex_BindsTheActualCharMethodAndPreservesRuntimeValue(string receiver)
    {
        var source = $$"""
            module NativeCharIndex
              func Test private (string:text) → string
                return {{receiver}}[0].ToString()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var method = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(SpecialType.System_Char, method.ContainingType.SpecialType);
        Assert.Equal("ToString", method.Name);
        Assert.Empty(method.Parameters);
        Assert.Equal(SpecialType.System_String, method.ReturnType.SpecialType);
        var resolver = new EffectResolver();
        var receiverType = Assert.IsAssignableFrom<BoundExpression>(call.Receiver).Type;
        var exactKey = EffectResolverKey.FromBoundReceiver(receiverType, "ToString", []);
        Assert.Equal("System.Char", exactKey.DeclaringType);
        var exact = resolver.Resolve(exactKey);
        Assert.Equal(EffectResolutionStatus.PureExplicit, exact.Status);
        Assert.True(exact.Effects.IsEmpty);
        Assert.Equal(EffectResolutionStatus.Unknown,
            resolver.Resolve(EffectResolverKey.FromBoundReceiver(receiverType, "ToString", ["Int32"])).Status);

        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var compilation = CSharpCompilation.Create("CharIndex_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + compiled.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var test = Assert.Single(assembly.GetTypes().SelectMany(type => type.GetMethods(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)),
            method => method.Name == "Test");
        Assert.Equal("Q", test.Invoke(null, ["Q"]));
    }

    [Fact]
    public void NativeStringNamedTypeParameter_DoesNotBorrowStringIndexerMetadata()
    {
        var (_, bound, _) = Bind("""
            module GenericCharIndex
              func Test<@String> (@String:text) → string
                !
                return text[0].ToString()
            """);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Program.Compile("""
            module GenericCharIndex
              func Test<@String> (@String:text) → string
                return text[0].ToString()
            """).HasErrors);
    }

    [Theory]
    [InlineData(BoundTypes.NullableAnnotation.NotAnnotated)]
    [InlineData(BoundTypes.NullableAnnotation.Annotated)]
    [InlineData(BoundTypes.NullableAnnotation.Oblivious)]
    public void IndexedPayloadType_DoesNotPromoteExistingAnnotations(BoundTypes.NullableAnnotation annotation)
    {
        var payload = new BoundTypes.NominalBoundType("STRING", annotation);
        var array = new BoundStructuralExpression(default, "Probe", "List<string>");
        var access = new BoundArrayAccess(default, array, new BoundIntLiteral(default, 0), resolvedType: payload);
        Assert.Same(payload, access.Type);
        Assert.False(access.HasUnknownInferredType);
        Assert.Equal(annotation != BoundTypes.NullableAnnotation.NotAnnotated,
            NullabilityChecker.IsPossiblyNullAssignedTo(access,
                new BoundTypes.NominalBoundType("STRING", BoundTypes.NullableAnnotation.NotAnnotated)));
    }

    [Fact]
    public void IndexedDeclaredField_PreservesExecutableCSharp()
    {
        var (ast, _, diagnostics) = Bind("""
            module Probe Probe
              class Chain public
                field List<string>:_memberNames private readonly = new List<string>(2)
                func Run public () → string
                  !
                  _memberNames.Add("root")
                  let string:last = _memberNames[0]
                  return _memberNames[0]
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        const string original = """
            namespace Probe;
            public class Chain
            {
                private readonly System.Collections.Generic.List<string> _memberNames = new(2);
                public string Run()
                {
                    _memberNames.Add("root");
                    string last = _memberNames[0];
                    return _memberNames[0];
                }
            }
            """;
        foreach (var code in new[] { original, new CSharpEmitter().Emit(ast) })
        {
            var compilation = CSharpCompilation.Create("Indexed_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emission = compilation.Emit(stream);
            Assert.True(emission.Success, string.Join("\n", emission.Diagnostics) + "\n" + code);
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            var chain = assembly.GetType("Probe.Chain", throwOnError: true)!;
            Assert.Equal("root", chain.GetMethod("Run")!.Invoke(Activator.CreateInstance(chain), null));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IndexedDeclaredField_PreservesStringPayloadNullability(bool nullable, bool throughThis)
    {
        var element = nullable ? "string?" : "string";
        var receiver = throughThis ? "this._memberNames" : "_memberNames";
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              class Chain public
                field List<{{element}}>:_memberNames private readonly = new List<{{element}}>(2)
                func Last public () → string
                  !
                  let string:last = {{receiver}}[0]
                  return {{receiver}}[0]
            """);
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.Equal(nullable ? 2 : 0, diagnostics.Errors.Count());
        var accesses = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundArrayAccess>().ToArray();
        Assert.Equal(2, accesses.Length);
        foreach (var access in accesses)
        {
            Assert.False(access.HasUnknownInferredType);
            Assert.Equal(nullable ? BoundTypes.NullableAnnotation.Annotated : BoundTypes.NullableAnnotation.NotAnnotated,
                Assert.IsType<BoundTypes.NominalBoundType>(access.Type).NullableAnnotation);
        }
    }

    [Theory]
    [InlineData("items", false)]
    [InlineData("items", true)]
    [InlineData("Make()", false)]
    [InlineData("Make()", true)]
    public void IndexedInferredCollection_PreservesKnownStringPayload(string initializer, bool nullable)
    {
        var element = nullable ? "string?" : "string";
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (List<{{element}}>:items) → string
                !
                let source = {{initializer}}
                let value = source[0]
                let string:copy = value
                return value
              func Make public () → List<{{element}}>
                !
                return new List<{{element}}>()
            """);
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.Equal(nullable ? 2 : 0, diagnostics.Errors.Count());
        var access = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundArrayAccess>());
        Assert.False(access.HasUnknownInferredType);
        Assert.Equal(nullable ? BoundTypes.NullableAnnotation.Annotated : BoundTypes.NullableAnnotation.NotAnnotated,
            Assert.IsType<BoundTypes.NominalBoundType>(access.Type).NullableAnnotation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedDeclaredReference_PreservesPayloadIdentityAndNullability(bool nullable)
    {
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (List<{{(nullable ? "Widget?" : "Widget")}}>:items) → Widget
                !
                let value = items[0]
                let Widget:copy = value
                return value
              class Widget public
            """);
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable));
        var access = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundArrayAccess>());
        var type = Assert.IsType<BoundTypes.NominalBoundType>(access.Type);
        Assert.NotNull(type.Declaration);
        Assert.Equal("Widget", type.Declaration.Name);
        Assert.Equal(nullable ? BoundTypes.NullableAnnotation.Annotated : BoundTypes.NullableAnnotation.NotAnnotated,
            type.NullableAnnotation);
    }

    [Theory]
    [InlineData("External.Make().Value", false)]
    [InlineData("External.GetFields()[0].Value", false)]
    [InlineData("await External.MakeAsync()", false)]
    [InlineData("await input", false)]
    [InlineData("External.Make() ?? input", false)]
    [InlineData("External.Make() ?? null", false)]
    [InlineData("null ?? External.Make()", false)]
    [InlineData("new Holder().Value", true)]
    [InlineData("((Holder)External.Make()).Value", true)]
    [InlineData("await task", true)]
    [InlineData("await valueTask", true)]
    [InlineData("await (Task<object>)External.Make()", true)]
    [InlineData("null ?? input", true)]
    [InlineData("input ?? null", true)]
    [InlineData("null ?? (object)External.Make()", true)]
    public void WrapperResultInference_RetainsOnlyEstablishedAuthority(string initializer, bool knownResult)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (object:input, Task<object>:task, ValueTask<object>:valueTask, External:External) → void
                !
                let value = {{initializer}}
                let alias = value
                alias.ToString()
                Pick(alias)
              func Pick public (object:item) → void
                !
              class Holder public struct
                field object:Value public
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().ToArray();
        var receiverCall = MemberCall(use, "alias", "ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        Assert.Equal(!knownResult, Assert.Single(pick.Arguments).HasUnknownInferredType);
        if (knownResult)
        {
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.True(pick.HasUnresolvedArguments);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
        }
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Equal(!knownResult, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span));
    }

    [Theory]
    [InlineData("List", false, false, false)]
    [InlineData("Dictionary", false, false, false)]
    [InlineData("List", true, false, false)]
    [InlineData("Dictionary", true, false, false)]
    [InlineData("List", true, true, false)]
    [InlineData("Dictionary", true, true, false)]
    [InlineData("List", true, true, true)]
    [InlineData("Dictionary", true, true, true)]
    public void IndexedElementInference_RequiresActualBclCollectionIdentity(
        string family, bool sourceDeclared, bool hasIndexer, bool bclQualified)
    {
        var typeName = (bclQualified ? "System.Collections.Generic." : sourceDeclared ? "" : "Foreign.")
            + family + (family == "List" ? "<object>" : "<string,object>");
        var index = family == "List" ? "0" : "\"key\"";
        var declaration = sourceDeclared
            ? $"  class {family}<{(family == "List" ? "T" : "K,V")}> public\n"
                + (hasIndexer ? $$"""
                        this[{{(family == "List" ? "int" : "string")}}:index] → int public
                          get
                            return 1
                    """ : "")
            : "";
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public ({{typeName}}:items) → void
                !
                let element = items[{{index}}]
                element.ToString()
                Pick(element)
              func Pick public (object:item) → void
                !
            {{declaration}}
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().ToArray();
        var receiverCall = MemberCall(use, "element", "ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        if (bclQualified)
        {
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.True(pick.HasUnresolvedArguments);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span);
        }
    }

    [Theory]
    [InlineData(false, "object[]", "object", false)]
    [InlineData(false, "List<object>", "object", false)]
    [InlineData(true, "object[]", "object", false)]
    [InlineData(true, "List<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.List<object>", "object", false)]
    [InlineData(true, "IList<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IList<object>", "object", false)]
    [InlineData(true, "IReadOnlyList<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IReadOnlyList<object>", "object", false)]
    [InlineData(true, "Dictionary<string,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.Dictionary<string,object>", "object", false)]
    [InlineData(true, "IDictionary<string,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IDictionary<string,object>", "object", false)]
    [InlineData(true, "IReadOnlyDictionary<string,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IReadOnlyDictionary<string,object>", "object", false)]
    [InlineData(true, "int[]", "int", false)]
    [InlineData(true, "List<int>", "int", false)]
    [InlineData(true, "object", "object", false)]
    [InlineData(true, "UnresolvedCollection<object>", "object", false)]
    [InlineData(true, "IEnumerable<object>", "object", false)]
    [InlineData(false, "object[,]", "object", true)]
    [InlineData(true, "object[,]", "object", true)]
    [InlineData(true, "object", "object", true)]
    public void IndexedElementInference_DistinguishesUnknownFromKnownElements(
        bool known, string collectionType, string elementType, bool multiDimensional)
    {
        var index = collectionType.Contains("Dictionary", StringComparison.Ordinal) ? "\"key\"" : "0";
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public ({{collectionType}}:items, External:External) → void
                !
                let source = {{(known ? "items" : "External.GetFields()")}}
                let element = source[{{(multiDimensional ? "0, 0" : index)}}]
                let alias = element
                alias.ToString()
                Pick(alias)
              func Pick public ({{elementType}}:item) → void
                !
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().ToArray();
        var receiverCall = MemberCall(use, "alias", "ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        if (known && collectionType is not ("object" or "UnresolvedCollection<object>" or "IEnumerable<object>"))
        {
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
            Assert.False(Assert.Single(pick.Arguments).HasUnknownInferredType);
            Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId],
                call => call.Callee == pick.ResolvedSymbolId);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.True(Assert.Single(pick.Arguments).HasUnknownInferredType);
            Assert.True(pick.HasUnresolvedArguments);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span);
        }
    }

    [Theory]
    [InlineData("raw", false)]
    [InlineData("raw", true)]
    [InlineData("fallback", false)]
    [InlineData("fallback", true)]
    [InlineData("native", false)]
    [InlineData("native", true)]
    public void OpaqueExpressionInference_DoesNotAcquireObjectAuthority(string kind, bool explicitType)
    {
        const string rawCode = "(new object())";
        var ast = Parse($$"""
            module Test
              func Use public () → void
                !
                let {{(explicitType ? "object:" : "")}}value = new object()
                let alias = value
                alias.ToString()
                Pick(alias)
                let text = System.Convert.ToString(alias)
              func Pick public (object:item) → void
                !
            """);
        if (kind != "native")
        {
            var use = ast.Functions[0];
            var body = use.Body.Select(statement =>
                statement is BindStatementNode { Name: "value", Initializer: { } initializer } binding
                    ? binding.CopyMetadataTo(new BindStatementNode(binding.Span, binding.Name, binding.TypeName, binding.IsMutable,
                        kind == "raw" ? new RawCSharpExpressionNode(initializer.Span, rawCode)
                            : new FallbackExpressionNode(initializer.Span, rawCode, "opaque-test"),
                        binding.Attributes, binding.IdentifierSpan, binding.TypeNameSpan))
                    : statement).ToArray();
            ast = WithBody(ast, use, body);
        }
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        if (kind != "native")
        {
            var interop = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundInteropExpression>());
            Assert.Equal(rawCode, interop.SourceText);
            Assert.True(interop.HasUnknownInferredType);
        }
        var useBound = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(useBound).OfType<BoundCallExpression>().ToArray();
        var receiverCall = MemberCall(useBound, "alias", "ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        var metadataCall = BoundNodeHelpers.DescendantsAndSelf(useBound).OfType<BoundCallExpression>()
            .Single(call => call.ResolvedTypeName == "System.Convert" && call.ResolvedMethodName == "ToString");
        if (explicitType || kind == "native")
        {
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved);
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
            Assert.False(metadataCall.HasUnresolvedArguments);
            Assert.False(Assert.Single(pick.Arguments).HasUnknownInferredType);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.Null(metadataCall.ResolvedMetadataMethod);
            Assert.True(metadataCall.HasUnresolvedArguments);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[useBound.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            foreach (var span in new[] { pick.Span, metadataCall.Span })
                Assert.Contains(diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == span);
        }
    }

    [Fact]
    public void OpaqueExpressionInference_PreservesExecutableCSharp()
    {
        const string rawCode = "(new Witness { Value = 7 })";
        var ast = Parse("""
            module Probe Probe
              class Runner public
                func Run public static () → int
                  !
                  let validator = new Witness()
                  return Pick(validator)
                func Pick private static (Witness:validator) → int
                  return validator.Value
            """);
        var runner = Assert.Single(ast.Classes);
        var run = runner.Methods[0];
        var binding = Assert.IsType<BindStatementNode>(run.Body[0]);
        var opaqueBinding = binding.CopyMetadataTo(new BindStatementNode(binding.Span, binding.Name, binding.TypeName,
            binding.IsMutable, new RawCSharpExpressionNode(binding.Initializer!.Span, rawCode),
            binding.Attributes, binding.IdentifierSpan, binding.TypeNameSpan));
        var opaqueRun = run.CopyMetadataTo(new MethodNode(run.Span, run.Id, run.Name, run.Visibility, run.Modifiers,
            run.TypeParameters, run.Parameters, run.Output, run.Effects, run.Preconditions, run.Postconditions,
            [opaqueBinding, run.Body[1]], run.Attributes, run.CSharpAttributes, identifierSpan: run.IdentifierSpan));
        var opaqueRunner = runner.CopyMetadataTo(new ClassDefinitionNode(runner.Span, runner.Id, runner.Name,
            runner.IsAbstract, runner.IsSealed, runner.IsPartial, runner.IsStatic, runner.BaseClass,
            runner.ImplementedInterfaces, runner.TypeParameters, runner.Fields, runner.Properties, runner.Constructors,
            [opaqueRun, runner.Methods[1]], runner.Events, runner.OperatorOverloads, runner.Attributes,
            runner.CSharpAttributes, visibility: runner.Visibility, identifierSpan: runner.IdentifierSpan,
            items: [opaqueRun, runner.Methods[1]]));
        ast = ast.With(module =>
        {
            module.Classes = [opaqueRunner];
            module.Items = ast.Items.Select(item => ReferenceEquals(item, runner) ? opaqueRunner : item).ToArray();
        });
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.True(call.HasUnresolvedArguments);
        Assert.Equal(rawCode, Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound)
            .OfType<BoundInteropExpression>()).SourceText);
        const string original = """
            namespace Probe;
            public class Runner
            {
                public static int Run()
                {
                    var validator = (new Witness { Value = 7 });
                    return Pick(validator);
                }
                private static int Pick(Witness validator) => validator.Value;
            }
            """;
        var emitted = new CSharpEmitter().Emit(ast);
        Assert.Contains(rawCode, emitted, StringComparison.Ordinal);
        foreach (var code in new[] { original, emitted })
        {
            var compilation = CSharpCompilation.Create("Opaque_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code),
                    CSharpSyntaxTree.ParseText("namespace Probe; public class Witness { public int Value { get; set; } }")],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emission = compilation.Emit(stream);
            Assert.True(emission.Success, string.Join("\n", emission.Diagnostics) + "\n" + code);
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            Assert.Equal(7, assembly.GetType("Probe.Runner", throwOnError: true)!
                .GetMethod("Run")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData(false, false, "object[]")]
    [InlineData(false, true, "object[]")]
    [InlineData(true, false, "object[]")]
    [InlineData(true, true, "object[]")]
    [InlineData(true, false, "IEnumerable<object>")]
    [InlineData(true, false, "List<object>")]
    public void ForeachElementInference_DistinguishesUnknownFromActualObject(
        bool known, bool explicitType, string collectionType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public ({{collectionType}}:items, External:External) → void
                !
                let source = {{(known ? "items" : "External.GetTypes()")}}
                each {{(explicitType ? "object:" : "")}}value in source
                  value.ToString()
                  Pick(value)
              func Pick public (object:item) → void
                !
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var receiverCall = MemberCall(use, "value", "ToString");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        if (known || explicitType)
        {
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
            Assert.IsNotType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span);
        }
    }

    [Fact]
    public void NestedUnknownForeachElements_RetainEveryReceiverWarningAndRejectAuthority()
    {
        var ast = Parse("""
            module Test
              func Use public (System.Reflection.Assembly:assembly) → void
                !
                each type in assembly.GetTypes()
                  type.GetInterfaces()
                  type.GetGenericArguments()
                  each @interface in type.GetInterfaces()
                    @interface.GetGenericArguments()
                    Pick(@interface)
              func Pick public (object:item) → void
                !
            """);
        var function = ast.Functions[0];
        var loop = Assert.IsType<ForeachStatementNode>(Assert.Single(function.Body));
        var nativeProducer = Assert.IsType<ExpressionCallNode>(loop.Collection);
        var member = Assert.IsType<FieldAccessNode>(nativeProducer.TargetExpression);
        // The named-call SDK boundary deliberately has no Type[] result authority.
        var namedProducer = new CallExpressionNode(nativeProducer.Span, "assembly.GetTypes", [], null, null, null,
            calleeSpan: member.FieldNameSpan, receiverSpan: member.Target.Span);
        var sdkLoop = loop.CopyMetadataTo(new ForeachStatementNode(loop.Span, loop.Id, loop.VariableName,
            loop.VariableType, namedProducer, loop.Body, loop.Attributes, loop.IndexVariableName,
            loop.VariableSpan, loop.IndexVariableSpan, loop.VariableTypeSpan));
        ast = WithBody(ast, function, [sdkLoop]);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().ToArray();
        var receivers = calls.Select(call => call.Receiver)
            .Where(receiver => receiver?.Type is Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType)
            .ToArray();
        Assert.Equal(4, receivers.Length);
        Assert.Equal(receivers.Select(receiver => receiver!.Span.Start).Order(),
            diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved)
                .Select(diagnostic => diagnostic.Span.Start).Order());
        var pick = calls.Single(call => call.Target == "Pick");
        Assert.Null(pick.ResolvedSymbol);
        Assert.Empty(pick.SelectedOverloadMatches);
        Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
        var graph = CallGraphAnalysis.Build(ast);
        Assert.Empty(graph.ResolveCallSites(ast.Functions.Single(function => function.Name == "Use").Id,
            "Pick", pick.Span));
    }

    [Fact]
    public void NativeReflectionForeach_RetainsActualMetadataArrayIdentity()
    {
        var (ast, bound, diagnostics) = Bind("""
            module Test
              func Use public (System.Reflection.Assembly:assembly) → void
                !
                each type in assembly.GetTypes()
                  type.GetInterfaces()
                  type.GetGenericArguments()
                  each @interface in type.GetInterfaces()
                    @interface.GetGenericArguments()
                    Pick(@interface)
              func Pick public (object:item) → void
                !
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var producer = MemberCall(use, "assembly", "GetTypes");
        var method = Assert.IsAssignableFrom<IMethodSymbol>(producer.ResolvedMetadataMethod);
        Assert.Equal("System.Reflection.Assembly", method.ContainingType.ToDisplayString());
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(method.ReturnType);
        Assert.Equal("System.Type", array.ElementType.ToDisplayString());
        Assert.Same(array, producer.MetadataArrayResultType);
        foreach (var loop in BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundForeachStatement>())
        {
            Assert.False(loop.LoopVariable.HasUnknownInferredType);
            Assert.Equal("System.Type", loop.LoopVariable.InferredReferenceType!.RoslynSymbol!.ToDisplayString());
        }
        var pick = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>(),
            call => call.Target == "Pick");
        Assert.NotNull(pick.ResolvedSymbol);
        Assert.Single(pick.SelectedOverloadMatches);
        Assert.False(pick.HasUnresolvedArguments);
        Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId],
            site => site.Callee == pick.ResolvedSymbolId);
        Assert.Contains(ast.Functions[1].Id,
            CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeUnknownReceiver_RetainsPerSiteDiagnosticsWithoutMetadataAuthority(bool explicitType, bool synthesized)
    {
        var name = synthesized ? "_generated001" : "value";
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use (External:External) → void
                !
                let {{(explicitType ? "object:" : "")}}{{name}} = External.Make()
                {{name}}.ToString()
                {{name}}.ToString()
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Where(call => call.ResolvedMethodName == "ToString").ToArray();
        Assert.Equal(2, calls.Length);
        var warnings = diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved).ToArray();
        Assert.Equal(explicitType || synthesized ? 0 : 2, warnings.Length);
        if (!explicitType && !synthesized)
            Assert.Equal(calls.Select(call => call.ReceiverSpan!.Value).OrderBy(span => span.Start),
                warnings.Select(diagnostic => diagnostic.Span).OrderBy(span => span.Start));
        foreach (var call in calls)
        {
            if (explicitType)
            {
                Assert.IsNotType<BoundTypes.UnresolvedBoundType>(call.Receiver!.Type);
                Assert.Equal(SpecialType.System_Object, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
            }
            else
            {
                Assert.IsType<BoundTypes.UnresolvedBoundType>(call.Receiver!.Type);
                Assert.Null(call.ResolvedMetadataMethod);
            }
        }
        var loader = new ManifestLoader(loadUserLevelManifests: false);
        Assert.NotNull(loader.LoadFromJson("""{"mappings":[{"type":"System.Object","methods":{"ToString":[]}}]}"""));
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true,
            resolver: new EffectResolver(loader)).Enforce(ast);
        foreach (var call in calls)
            Assert.Equal(!explicitType, diagnostics.Errors.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == call.Span));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeEscapedStringTypeParameter_DoesNotBorrowIndexerMetadataOnEitherDeclaration(bool classParameter)
    {
        var (ast, bound, diagnostics) = Bind(classParameter ? """
            module Test
              class Holder<@String>
                func Use (@String:text) → string
                  !
                  return text[0].ToString()
            """ : """
            module Test
              func Use<@String> (@String:text) → string
                !
                return text[0].ToString()
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var access = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundArrayAccess>());
        Assert.True(access.HasUnknownInferredType);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Null(call.ResolvedSymbol);
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == call.Span);
    }

    [Theory]
    [InlineData("object", false)]
    [InlineData("object?", true)]
    [InlineData("string", false)]
    [InlineData("string?", true)]
    [InlineData("Widget", false)]
    [InlineData("Widget?", true)]
    public void NativeMatchCapture_PreservesEstablishedReferenceAnnotation(string type, bool nullable)
    {
        foreach (var pattern in new[] { "captured", "var captured" })
        {
            var (_, bound, diagnostics) = Bind($$"""
                module Test
                  func Use ({{type}}:source) → int
                    !
                    match source
                      case {{pattern}}
                        return Pick(captured)
                  func Pick ({{type.TrimEnd('?')}}:item) → int
                    !
                    return 2
                  class Widget
                """);
            var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
            Assert.NotNull(call.ResolvedSymbol);
            var argument = Assert.IsType<BoundVariableExpression>(Assert.Single(call.Arguments));
            Assert.False(argument.HasUnknownInferredType);
            Assert.Equal(nullable ? BoundTypes.NullableAnnotation.Annotated : BoundTypes.NullableAnnotation.NotAnnotated,
                Assert.IsType<BoundTypes.NominalBoundType>(argument.Type).NullableAnnotation);
            Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
            Assert.Equal(nullable ? 1 : 0, diagnostics.Errors.Count());
        }
    }

    [Theory]
    [InlineData("System.Object")]
    [InlineData("global::System.Object")]
    public void NativeQualifiedObject_IsKnownForOverloadMismatch(string type)
    {
        foreach (var parameter in new[] { "object", "int" })
        {
            var (_, bound, diagnostics) = Bind($$"""
                module Test
                  func Use ({{type}}:value) → int
                    !
                    return Pick(value)
                  func Pick ({{parameter}}:value) → int
                    !
                    return 2
                """);
            var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
            Assert.False(call.HasUnresolvedArguments);
            Assert.False(Assert.Single(call.Arguments).HasUnknownInferredType);
            Assert.Equal(parameter == "object", call.ResolvedSymbol is not null);
            Assert.Equal(parameter == "int", diagnostics.Errors.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.NoMatchingOverload));
            Assert.Equal(parameter == "int" ? 1 : 0, diagnostics.Errors.Count());
        }
    }

    [Theory]
    [InlineData("var captured", false)]
    [InlineData("captured", false)]
    [InlineData("{ Item: var captured }", false)]
    [InlineData("object captured", true)]
    public void MatchCapture_UnknownRecoveryDoesNotAcquireObjectAuthority(string pattern, bool explicitType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (External:External) → int
                !
                let source = External.Make()
                match source
                  case {{pattern}}
                    captured.ToString()
                    return Pick(captured)
              func Pick public (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var receiverCall = MemberCall(use, "captured", "ToString");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        if (explicitType)
        {
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span);
        }
    }

    [Theory]
    [InlineData("var captured", "object")]
    [InlineData("captured", "object")]
    [InlineData("var captured", "int")]
    [InlineData("captured", "int")]
    public void MatchCapture_KnownMatchedTypeRetainsItsAuthority(string pattern, string type)
    {
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public ({{type}}:source) → int
                !
                match source
                  case {{pattern}}
                    captured.ToString()
                    return Pick(captured)
              func Pick public ({{type}}:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.SignatureUnresolved);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.NotNull(pick.ResolvedSymbol);
        Assert.Single(pick.SelectedOverloadMatches);
        Assert.False(Assert.Single(pick.Arguments).HasUnknownInferredType);
        Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId],
            call => call.Callee == pick.ResolvedSymbolId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchExpressionCapture_RetainsMatchedValueAuthority(bool known)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (object:input, External:External) → int
                !
                let source = {{(known ? "input" : "External.Make()")}}
                return match source
                  case var captured → Pick(captured)
              func Pick public (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.Equal(known, pick.ResolvedSymbol is not null);
        Assert.Equal(!known, pick.HasUnresolvedArguments);
        if (!known)
        {
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MatchCaptureResult_DoesNotRecoverUnknownAuthority(bool known, bool returnCapture)
    {
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (object:input, External:External) → int
                !
                let source = {{(known ? "input" : "External.Make()")}}
                let projected = match source
                  case var captured → {{(returnCapture ? "captured" : "input")}}
                let alias = projected
                return Pick(alias)
              func Pick public (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.Equal(known || !returnCapture, pick.ResolvedSymbol is not null);
        Assert.Equal(!known && returnCapture, pick.HasUnresolvedArguments);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("IList<ObjectDetails>")]
    public void UnknownInferredArgument_DoesNotRejectOrSelectNativeOverload(string parameterType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (External:External) → int
                !
                let details = External.Make()
                let alias = details
                return Pick(alias)
              func Pick public ({{parameterType}}:value) → int
                !
                return 2
            """);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.ResolvedSymbols);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Null(call.ArgumentParameterIndices);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.Empty(graph.ForwardGraph[use.SymbolId]);
        var astUse = ast.Functions.Single(function => function.Name == "Use");
        var astGraph = CallGraphAnalysis.Build(ast);
        Assert.Empty(astGraph.ResolveCallSites(astUse.Id, "Pick", call.Span));
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == call.Span);
    }

    [Theory]
    [InlineData("let System.Object:value = External.Make()")]
    [InlineData("let object:value = External.Make()")]
    [InlineData("let value = new object()")]
    [InlineData("let value = Make()")]
    [InlineData("let value = make()")]
    [InlineData("let value = (make)()")]
    [InlineData("let value = System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(new object())")]
    public void ActualObjectArgument_RetainsNativeSelectionAndMismatch(string binding)
    {
        foreach (var parameter in new[] { "object", "int" })
        {
            var (_, bound, diagnostics) = Bind($$"""
                module Test
                  func Use public (Func<object>:make, External:External) → int
                    !
                    {{binding}}
                    let alias = value
                    return Pick(alias)
                  func Make public () → object
                    !
                    return new object()
                  func Pick public ({{parameter}}:value) → int
                    !
                    return 2
                """);
            var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
            var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
                .Single(call => call.Target == "Pick");
            if (parameter == "object")
            {
                Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.NoMatchingOverload);
                Assert.NotNull(pick.ResolvedSymbol);
                Assert.Single(pick.SelectedOverloadMatches);
                Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId],
                    site => site.Callee == pick.ResolvedSymbolId);
            }
            else
            {
                Assert.Contains(diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.NoMatchingOverload);
                Assert.Null(pick.ResolvedSymbol);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MutableInferredTypeAuthority_IsStaticAcrossReassignmentsAndJoins(bool unknown, bool branch)
    {
        var initializer = unknown ? "External.Make()" : "new object()";
        var replacement = unknown ? "new object()" : "External.Make()";
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (bool:gate, External:External) → int
                !
                var value = {{initializer}}
                {{(branch ? "if gate\n      set value " : "var value = ")}}{{replacement}}
                let alias = value
                return Pick(alias)
              func Pick public (object:value) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var pick = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.Equal(unknown, pick.HasUnresolvedArguments);
        Assert.Equal(!unknown, pick.ResolvedSymbol is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredTypeJoin_DoesNotRecoverUnknownAuthority(bool preprocessor)
    {
        var binding = preprocessor
            ? """
                pp FEATURE
                  let value = new object()
                pp else
                  let value = External.Make()
                """
            : "let value = gate ? new object() : External.Make()";
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (bool:gate, External:External) → int
                !
                {{binding.Replace("\n", "\n    ", StringComparison.Ordinal)}}
                return Pick(value)
              func Pick public (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var pick = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.True(pick.HasUnresolvedArguments);
        Assert.Null(pick.ResolvedSymbol);
        Assert.Empty(pick.SelectedOverloadMatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownArgument_BlocksNativeAndMetadataFallback(bool statement)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (External:External) → int
                !
                let value = External.Make()
                let text = System.Convert.ToString(value)
                {{(statement ? "" : "let result = ")}}Pick(value)
                return 0
              func Pick public (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var metadata = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.ResolvedTypeName == "System.Convert" && call.ResolvedMethodName == "ToString");
        Assert.True(metadata.HasUnknownInferredType);
        Assert.Null(metadata.ResolvedMetadataMethod);
        Assert.Null(metadata.ArgumentParameterIndices);
        var expression = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        if (statement)
            Assert.Same(expression, Assert.Single(use.Body.OfType<BoundExpressionStatement>()).Expression);
        else
            Assert.Same(expression, use.Body.OfType<BoundBindStatement>().Single(binding => binding.Variable.Name == "result").Initializer);
        Assert.Empty(expression.ResolvedSymbols);
        Assert.Empty(expression.SelectedOverloadMatches);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.Empty(graph.ForwardGraph[use.SymbolId]);
        var span = expression.Span;
        var astGraph = CallGraphAnalysis.Build(ast);
        Assert.Empty(astGraph.ResolveCallSites(ast.Functions[0].Id, "Pick", span));
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == span);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == metadata.Span);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownArgument_DoesNotEraseADeclaredDelegateInvocationRow(bool unknown)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (Func<object,int>:make ! cw, object:seed, External:External) → int
                !
                let value = {{(unknown ? "External.Make()" : "seed")}}
                return make(value)
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.Target == "make");
        var enforcement = new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true);
        enforcement.Enforce(ast);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == call.Span);
        var fact = enforcement.DeclarationFacts[ast.Functions.Single().Id];
        Assert.Equal(unknown, fact.InferredRow.IsUnknown);
        Assert.Equal(unknown ? ["unknown"] : new[] { "cw" }, fact.Forbidden);
        if (!unknown)
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == "Calor0410" && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));

        // Isolate this invocation's charge without teaching the binder the
        // producer's return type. Unknown remains top in the default pass above.
        var loader = new ManifestLoader(loadUserLevelManifests: false);
        Assert.NotNull(loader.LoadFromJson("""
            {"mappings":[{"type":"External","methods":{"Make":[]}}]}
            """));
        var isolatedDiagnostics = new DiagnosticBag();
        var isolated = new EffectEnforcementPass(isolatedDiagnostics,
            policy: UnknownCallPolicy.Strict, strictEffects: true, resolver: new EffectResolver(loader));
        var use = ast.Functions.Single();
        var body = use.Body.Select(statement =>
        {
            if (!unknown || statement is not BindStatementNode { Name: "value", Initializer: ExpressionCallNode producer } binding)
                return statement;
            var member = Assert.IsType<FieldAccessNode>(producer.TargetExpression);
            return binding.CopyMetadataTo(new BindStatementNode(binding.Span, binding.Name, binding.TypeName, binding.IsMutable,
                new CallExpressionNode(producer.Span, "External.Make", [], null, null, null,
                    calleeSpan: member.FieldNameSpan, receiverSpan: member.Target.Span),
                binding.Attributes, binding.IdentifierSpan, binding.TypeNameSpan));
        }).ToArray();
        var isolatedAst = WithBody(ast, use, body);
        isolated.Enforce(isolatedAst);
        var isolatedFact = isolated.DeclarationFacts[ast.Functions.Single().Id];
        Assert.False(isolatedFact.InferredRow.IsUnknown);
        Assert.Equal(["cw"], isolatedFact.Forbidden);
        var forbidden = Assert.Single(isolatedDiagnostics.Errors,
            diagnostic => diagnostic.Code == "Calor0410");
        Assert.Contains("uses effect 'cw'", forbidden.Message, StringComparison.Ordinal);
        Assert.Contains("charged by invoking 'make' (row: cw)", forbidden.Message, StringComparison.Ordinal);
        Assert.Equal(unknown, Assert.IsType<BoundVariableExpression>(Assert.Single(call.Arguments)).HasUnknownInferredType);
    }

    [Fact]
    public void NativeUnselectedComputedMember_DoesNotBorrowNamedCallManifestAuthority()
    {
        var (ast, bound, diagnostics) = Bind("""
            module Test
              func Use (External:External) → int
                !
                let value = External.Make()
                return Pick(value)
              func Pick (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var producer = MemberCall(use, "External", "Make");
        Assert.Null(producer.ResolvedMetadataMethod);
        Assert.Null(producer.ResolvedSymbol);
        Assert.True(producer.HasUnknownInferredType);
        var pick = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>(),
            call => call.Target == "Pick");
        Assert.Null(pick.ResolvedSymbol);
        Assert.Empty(pick.SelectedOverloadMatches);
        Assert.True(pick.HasUnresolvedArguments);
        var loader = new ManifestLoader(loadUserLevelManifests: false);
        Assert.NotNull(loader.LoadFromJson("""{"mappings":[{"type":"External","methods":{"Make":[]}}]}"""));
        var enforcement = new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true,
            resolver: new EffectResolver(loader));
        enforcement.Enforce(ast);
        Assert.True(enforcement.DeclarationFacts[ast.Functions[0].Id].InferredRow.IsUnknown);
        foreach (var span in new[] { producer.Span, pick.Span })
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == span);
        Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
    }

    [Fact]
    public void UnknownInferredLinqArgument_PreservesExecutableCSharpInference()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;
            namespace Probe;
            public class Orderer
            {
                public static int Run(Handler[] handlers)
                {
                    var handlerObjectsDetails = handlers.Select(static s => new ObjectDetails(s)).ToList();
                    var uniqueHandlers = RemoveOverridden(handlerObjectsDetails).ToArray();
                    return uniqueHandlers.Length;
                }
                private static IEnumerable<ObjectDetails> RemoveOverridden(IList<ObjectDetails> details)
                {
                    return details;
                }
            }
            """;
        const string externalTypes = """
            namespace Probe;
            public sealed class Handler { }
            public sealed class ObjectDetails
            {
                public ObjectDetails(Handler handler) { }
            }
            """;
        var (ast, bound, diagnostics) = Bind("""
            module Probe Probe
              using System.Linq
              using System.Collections.Generic
              class Orderer public
                func Run public static (Handler[]:handlers) → int
                  !
                  let handlerObjectsDetails = handlers.Select((Handler:s) => new ObjectDetails(s)).ToList()
                  let uniqueHandlers = RemoveOverridden(handlerObjectsDetails).ToArray()
                  return uniqueHandlers.Length
                func RemoveOverridden private static (IList<ObjectDetails>:details) → IEnumerable<ObjectDetails>
                  return details
            """);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
        var call = calls.Single(call => call.Target.EndsWith("RemoveOverridden", StringComparison.Ordinal)
            || call.ResolvedMethodName == "RemoveOverridden");
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        var emitted = new CSharpEmitter().Emit(ast);
        foreach (var code in new[] { source, emitted })
        {
            var compilation = CSharpCompilation.Create("Inferred_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code),
                    CSharpSyntaxTree.ParseText(externalTypes)],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emission = compilation.Emit(stream);
            Assert.True(emission.Success, string.Join("\n", emission.Diagnostics) + "\n" + code);
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            var handlers = Array.CreateInstance(assembly.GetType("Probe.Handler", throwOnError: true)!, 3);
            for (var index = 0; index < handlers.Length; index++)
                handlers.SetValue(Activator.CreateInstance(handlers.GetType().GetElementType()!), index);
            Assert.Equal(3, assembly.GetType("Probe.Orderer", throwOnError: true)!
                .GetMethod("Run")!.Invoke(null, [handlers]));
        }
    }

    [Fact]
    public void ComputedMembers_RetainOverloadIdentityAndOrderedReceiverChildren()
    {
        var (ast, bound, diagnostics) = Bind("""
            module Test
              func Use public (Holder:h) → int
                !
                return Get(h).Pick(3)
              func Get public (Holder:h) → Holder
                !
                return h
              class Holder public
                func Pick public (int:value) → int
                  !
                  return value
                func Pick public (string:value) → string
                  !
                  return value
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var outer = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(use.Body)).Expression);
        var inner = Assert.IsType<BoundCallExpression>(outer.EvaluatedReceiver);
        Assert.Same(inner, outer.Receiver);
        Assert.Same(inner, outer.Children[0]);
        Assert.Same(outer.Arguments[0], outer.Children[1]);
        Assert.Same(bound.Functions.Single(function =>
            function.Symbol.Name == "Holder.Pick" && function.Symbol.ReturnType == "int").Symbol, outer.ResolvedSymbol);
        Assert.Equal(2, CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId].Count);
        Assert.Contains($"Holder.{ast.Classes[0].Methods[0].Id}",
            CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "<expression-call>", outer.Span));
    }

    [Fact]
    public void ComputedMember_InvalidArgumentStillReportsNativeOverloadError()
    {
        var (_, _, diagnostics) = Bind("""
            module Test
              func Use public (Holder:h) → int
                !
                return Get(h).Pick(true)
              func Get public (Holder:h) → Holder
                !
                return h
              class Holder public
                func Pick public (int:value) → int
                  !
                  return value
            """);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void ComputedMetadataArrayCall_RetainsExactSelectedMethodAndArrayResult()
    {
        var (_, bound, diagnostics) = Bind("""
            module Test
              func Use public () → string[]
                !
                return new System.Globalization.CultureInfo("en-US").DateTimeFormat.GetAllDateTimePatterns()
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().Single();
        Assert.Equal("GetAllDateTimePatterns", call.ResolvedMetadataMethod?.Name);
        Assert.Equal("System.Globalization.DateTimeFormatInfo", call.ResolvedMetadataMethod?.ContainingType.ToDisplayString());
        Assert.Same(call.ResolvedMetadataMethod!.ReturnType, call.MetadataArrayResultType);
        Assert.Equal(SpecialType.System_String, call.MetadataArrayResultType!.ElementType.SpecialType);
    }

    [Fact]
    public void GenericComputedMetadataCall_RetainsExplicitTypesAndExactOverload()
    {
        var node = new ExpressionCallNode(default,
            new FieldAccessNode(default, new NewExpressionNode(default, "List", ["str"], []), "ToArray"), []);
        var function = new FunctionNode(default, "use", "Use", Visibility.Public, [],
            new OutputNode(default, "object"), null, [new ReturnStatementNode(default, node)], new AttributeCollection());
        var module = new ModuleNode(default, "test", "Test", [], [function], new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().Single();
        Assert.NotNull(call.ResolvedMetadataMethod);
        Assert.Equal(SpecialType.System_String, call.MetadataArrayResultType!.ElementType.SpecialType);

        var generic = new ExpressionCallNode(default,
            new FieldAccessNode(default, new ReferenceNode(default, "value"), "Echo"),
            [new IntLiteralNode(default, 5)], ["item"], [null], ["i32"]);
        Assert.Equal("value.Echo<int>(item: 5)", generic.Accept(new CSharpEmitter()));
        var simplified = generic.Accept(new Calor.Compiler.Verification.ExpressionSimplifier());
        Assert.Equal(generic.TypeArguments, Assert.IsType<ExpressionCallNode>(simplified).TypeArguments);
        Assert.Equal("value.Echo<int>(item: 5)", generic.Accept(new CalorEmitter()));
    }

    [Fact]
    public void ExplicitGenericComputedCall_UsesTheConstructedSelectedMetadataMethod()
    {
        var call = new ExpressionCallNode(default,
            new FieldAccessNode(default, new NewExpressionNode(default, "System.Collections.ArrayList", [], []), "Cast"),
            [], typeArguments: ["i32"]);
        var function = new FunctionNode(default, "use", "Use", Visibility.Public, [],
            new OutputNode(default, "object"), null, [new ReturnStatementNode(default, call)], new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(new ModuleNode(default, "test", "Test", [], [function], new AttributeCollection()));
        var selected = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().Single();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal("Cast", selected.ResolvedMetadataMethod?.Name);
        Assert.Equal(SpecialType.System_Int32, Assert.Single(selected.ResolvedMetadataMethod!.TypeArguments).SpecialType);
        Assert.Equal(call.TypeArguments, selected.TypeArguments);
    }

    [Fact]
    public void ComputedMetadataReceiver_UsesExactSelectedParametersAndIsCollected()
    {
        var (ast, bound, diagnostics) = Bind("""
            module Test
              func Use public () → bool
                !
                return new object().Equals(3)
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().Single();
        Assert.Equal("Equals", call.ResolvedMetadataMethod?.Name);
        Assert.False(call.ResolvedMetadataMethod!.IsStatic);
        Assert.Equal(["System.Object"], call.ResolvedParameterTypes);
        Assert.Contains(ExternalCallCollector.Collect(ast),
            collected => collected.TypeName == "System.Object" && collected.MethodName == "Equals");
    }

    [Fact]
    public void ComputedReceiver_TypeParameterDoesNotAcquireBclMetadata()
    {
        var (_, bound, diagnostics) = Bind("""
            module Test
              func Use<Random> public (Random:value) → int
                !
                return Get<Random>(value).Next()
              func Get<T> public (T:value) → T
                !
                return value
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.EvaluatedReceiver is not null);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Null(call.ResolvedSymbol);
    }

    [Fact]
    public void ComputedMemberArguments_AreStillTypeCheckedWhenTheMemberIsExternal()
    {
        var result = Program.Compile("""
            module Test
              func Use public () → object
                !
                return new object().Equals(1 ?? 2)
            """, "computed.calr", new CompilationOptions { EnableTypeChecking = true, EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Message.Contains("Null-coalescing requires", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("! alloc,cw", false)]
    [InlineData("! alloc", true)]
    public void ComputedInternalMethod_ChargesItsSelectedEffects(string effects, bool error)
    {
        var result = Program.Compile($$"""
            module Test
              func Use public () → void
                !
                {{effects}}
                new Holder().Loud()
              class Holder public
                ctor public ()
                  let int:unused = 0
                func Loud public () → void
                  ! cw
                  Console.WriteLine("loud")
            """, "computed.calr", new CompilationOptions { EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Equal(error, result.HasErrors);
        if (error)
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0410");
    }

    [Fact]
    public void ComputedDelegateTarget_InvalidatesCapturedMutationFacts()
    {
        foreach (var target in new[] { "later", "(gate ? later : later)" })
        {
            var (_, _, diagnostics) = Bind($$"""
                module Test
                  func Use public (string?:input, bool:gate) → int
                    !
                    var string?:value = input
                    let Action:later = () =>
                      set value null
                    if value != null
                      {{target}}()
                      let string:observed = value
                      return 1
                    return 0
                """);
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0272");
        }
    }

    [Fact]
    public void ComputedFactory_InvokedReturnValueUsesItsDeclaredRow()
    {
        foreach (var binding in new[] { false, true })
        foreach (var permitsWrite in new[] { false, true })
        {
            var result = Program.Compile($$"""
                module Test
                  func Use public () → int
                    ! alloc{{(permitsWrite ? ",cw" : "")}}
                    {{(binding ? "let handler = new Holder().Make()\n    return handler()" : "return new Holder().Make()()")}}
                  class Holder public
                    ctor public ()
                      let int:unused = 0
                    func Make public () → Func<int> ! cw
                      !
                      return () => 0
                """, "computed.calr", new CompilationOptions { EnforceEffects = true, StatusWriter = TextWriter.Null });
            Assert.True(result.HasErrors == !permitsWrite,
                $"binding={binding}, permitsWrite={permitsWrite}: {string.Join("\n", result.Diagnostics)}");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
            if (!permitsWrite)
                Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0410");
        }
    }

    [Fact]
    public void UnknownComputedMember_DoesNotBecomePure()
    {
        var result = Program.Compile("""
            module Test
              func Use public () → void
                ! alloc
                new System.Random().DoesNotExist()
            """, "computed.calr", new CompilationOptions { EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void ConditionalDelegateTarget_IsGroupedBeforeInvocation()
    {
        var call = new ExpressionCallNode(default,
            new ConditionalExpressionNode(default, new ReferenceNode(default, "gate"),
                new ReferenceNode(default, "first"), new ReferenceNode(default, "second")),
            [new IntLiteralNode(default, 7)]);
        var expression = call.Accept(new CSharpEmitter());
        Assert.Equal("((gate ? first : second))(7)", expression);
        var code = $"class Probe {{ static int Run(bool gate, System.Func<int,int> first, System.Func<int,int> second) => {expression}; }}";
        var compilation = CSharpCompilation.Create("ComputedGrouping",
            [CSharpSyntaxTree.ParseText(code)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }

    private static (ModuleNode Ast, BoundModule Bound, DiagnosticBag Diagnostics) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source);
        return (ast, new Binder(diagnostics).Bind(ast), diagnostics);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return ast;
    }

    private static BoundCallExpression MemberCall(BoundNode root, string receiver, string member)
    {
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(root).OfType<BoundCallExpression>(),
            call => call.ResolvedMethodName == member
                && call.Receiver is BoundVariableExpression variable && variable.Variable.Name == receiver);
        Assert.Equal("<expression-call>", call.Target);
        Assert.Same(call.Receiver, call.EvaluatedReceiver);
        return call;
    }

    private static ModuleNode WithBody(ModuleNode ast, FunctionNode function, IReadOnlyList<StatementNode> body)
    {
        var replacement = function.CopyMetadataTo(new FunctionNode(function.Span, function.Id, function.Name,
            function.Visibility, function.Parameters, function.Output, function.Effects, body, function.Attributes));
        return ast.With(module =>
        {
            module.Functions = ast.Functions.Select(item => ReferenceEquals(item, function) ? replacement : item).ToArray();
            module.Items = ast.Items.Select(item => ReferenceEquals(item, function) ? replacement : item).ToArray();
        });
    }
}
