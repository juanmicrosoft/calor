using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using NullableAnnotation = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;

namespace Calor.Compiler.Tests;

public class MethodInputArrayNullabilityTests(Xunit.Abstractions.ITestOutputHelper output)
{
    public static IEnumerable<object[]> NestedMetadataArrayDeclarations()
    {
        foreach (var type in new[]
        {
            "System.Collections.Generic.List<string[]>[]",
            "System.Collections.Generic.List<string?[]>[]",
            "System.Collections.Generic.List<string[]?>[]",
            "System.Collections.Generic.List<string[]>?[]",
            "System.Collections.Generic.List<string[]>[]?",
            "System.Collections.Generic.List<string?[]?>?[]?",
            "System.Collections.Generic.List<System.String?[,]?[]?>[,][]?",
            "System.Collections.Generic.List<System.Collections.Generic.List<string?[]>?[]>[]",
            "System.Collections.Generic.List<Text?[]>[]",
            "System.Collections.Generic.List<string?>[]"
        })
        foreach (var oblivious in new[] { false, true })
            yield return [type, oblivious];
    }

    [Theory]
    [MemberData(nameof(NestedMetadataArrayDeclarations))]
    public void MetadataArrayDeclaration_PreservesNestedAnnotationsAndSelectedSymbols(string type, bool oblivious)
    {
        var source = $$"""
            module NestedMetadataArray{{(oblivious ? " nullable oblivious" : "")}}
              using Text = System.String
              func Echo public ({{type}}:value) → {{type}}
                !
                return value
            """;
        var (bound, diagnostics) = Bind(source);
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var parameter = Assert.Single(Assert.Single(bound.Functions).Symbol.Parameters);
        var actual = Assert.IsType<ArrayBoundType>(parameter.MethodInputArrayType);

        using var context = MetadataContext.Create();
        var tree = CSharpSyntaxTree.ParseText($$"""
            #nullable {{(oblivious ? "disable" : "enable")}}
            using Text = System.String;
            public static class Oracle {
                public static {{type}} Echo({{type}} value) => value;
            }
            """);
        var oracle = context.HostCompilationForBinder.AddSyntaxTrees(tree);
        Assert.DoesNotContain(oracle.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var method = Assert.IsAssignableFrom<IMethodSymbol>(
            Assert.Single(oracle.GetTypeByMetadataName("Oracle")!.GetMembers("Echo")));
        var expected = Assert.IsAssignableFrom<IArrayTypeSymbol>(Assert.Single(method.Parameters).Type);
        AssertDeclarationGraph(expected, actual, "value");

        var compiled = Program.Compile(source, "nested-metadata-array.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var emitted = AssertEmittedArrayParameter(compiled.GeneratedCode, "Echo", expected.Rank);
        Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(expected, emitted),
            $"Expected {expected.ToDisplayString()}, emitted {emitted.ToDisplayString()}");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void NestedMetadataArrayRead_KeepsDeclaredLeafAndDiagnosticSite(
        bool oblivious, bool nullableElement, bool local)
    {
        var expression = $"{(local ? "copy" : "value")}[0][0][0]";
        var source = $$"""
            module NestedMetadataRead{{(oblivious ? " nullable oblivious" : "")}}
              func Probe public (System.Collections.Generic.List<System.String{{(nullableElement ? "?" : "")}}[]>[]:value) → int
                !
                {{(local ? "let copy = value" : "")}}
                return System.Int32.Parse({{expression}})
            """;
        var (bound, diagnostics) = Bind(source);
        var function = Assert.Single(bound.Functions);
        var outer = Assert.IsType<ArrayBoundType>(Assert.Single(function.Symbol.Parameters).MethodInputArrayType);
        var generic = Assert.IsType<GenericInstantiationBoundType>(outer.ElementType);
        var inner = Assert.IsType<ArrayBoundType>(Assert.Single(generic.TypeArguments));
        var leaf = Assert.IsType<NominalBoundType>(inner.ElementType);
        Assert.Equal(nullableElement ? NullableAnnotation.Annotated
            : oblivious ? NullableAnnotation.Oblivious : NullableAnnotation.NotAnnotated, leaf.NullableAnnotation);
        Assert.Equal(SpecialType.System_String, leaf.RoslynSymbol!.SpecialType);
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(function.Body[^1]).Expression);
        var argument = Assert.IsType<BoundArrayAccess>(Assert.Single(call.Arguments));
        var innerRead = Assert.IsType<BoundArrayAccess>(argument.Array);
        var genericRead = Assert.IsType<BoundArrayAccess>(innerRead.Array);
        Assert.Same(generic, genericRead.Type);
        Assert.Same(inner, innerRead.Type);
        Assert.Same(leaf, argument.Type);

        // 2026-11-14: an oblivious leaf in a `nullable oblivious` module is accepted at a
        // non-null boundary, as in C#'s disabled context; an explicit `?` leaf is not.
        var rejected = nullableElement;
        var findings = diagnostics.Where(diagnostic => BindingDiagnosticPolicy.IsCompilationError(diagnostic)).ToArray();
        Assert.Equal(rejected ? 1 : 0, findings.Length);
        if (rejected)
        {
            var finding = Assert.Single(findings);
            Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, finding.Code);
            Assert.Equal(argument.Span, finding.Span);
            Assert.Equal(expression, source.Substring(finding.Span.Start, finding.Span.Length));
            Assert.Contains(leaf.NullableAnnotation.ToString(), finding.Message);
        }
        var compiled = Program.Compile(source, "nested-metadata-read.calr");
        Assert.Equal(rejected, compiled.HasErrors);
        if (rejected)
        {
            var finding = Assert.Single(compiled.Diagnostics.Errors);
            Assert.Equal(Assert.Single(findings).Code, finding.Code);
            Assert.Equal(argument.Span, finding.Span);
            Assert.Equal(Assert.Single(findings).Message, finding.Message);
            Assert.Empty(compiled.GeneratedCode);
        }
        else
        {
            using var context = MetadataContext.Create();
            var tree = CSharpSyntaxTree.ParseText(compiled.GeneratedCode);
            using var image = new MemoryStream();
            var emitted = context.HostCompilationForBinder.AddSyntaxTrees(tree).Emit(image);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            var probe = System.Reflection.Assembly.Load(image.ToArray()).GetTypes()
                .SelectMany(type => type.GetMethods()).Single(method => method.Name == "Probe");
            Assert.Equal(123, probe.Invoke(null, [new List<string[]>[] { new() { new[] { "123" } } }]));
        }
    }

    private void AssertDeclarationGraph(ITypeSymbol expected, BoundType actual, string path)
    {
        output.WriteLine($"{path}: Roslyn {expected.Kind} {expected.ToDisplayString()} {expected.NullableAnnotation}; "
            + $"bound {actual.GetType().Name} {actual.DisplayString} {NullabilityChecker.GetAnnotation(actual)}");
        Assert.Equal(MetadataBinderResult.MapAnnotation(expected.NullableAnnotation),
            NullabilityChecker.GetAnnotation(actual));
        switch (expected)
        {
            case IArrayTypeSymbol array:
                var boundArray = Assert.IsType<ArrayBoundType>(actual);
                Assert.Equal(array.Rank, boundArray.Rank);
                AssertDeclarationGraph(array.ElementType, boundArray.ElementType, path + ".element");
                if (boundArray.RoslynSymbol is { } selectedArray)
                    Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(array, selectedArray));
                break;
            case INamedTypeSymbol { IsGenericType: true } generic:
                var boundGeneric = Assert.IsType<GenericInstantiationBoundType>(actual);
                var selected = Assert.IsAssignableFrom<INamedTypeSymbol>(boundGeneric.RoslynSymbol);
                Assert.Null(boundGeneric.Definition.Declaration);
                Assert.Equal(generic.ContainingAssembly.Identity, selected.ContainingAssembly.Identity);
                Assert.True(SymbolEqualityComparer.Default.Equals(generic.OriginalDefinition, selected.OriginalDefinition));
                Assert.True(SymbolEqualityComparer.Default.Equals(boundGeneric.Definition.RoslynSymbol, selected.ConstructedFrom));
                Assert.Equal(generic.TypeArguments.Length, boundGeneric.TypeArguments.Length);
                for (var index = 0; index < generic.TypeArguments.Length; index++)
                    AssertDeclarationGraph(generic.TypeArguments[index], boundGeneric.TypeArguments[index],
                        $"{path}.argument[{index}]");
                Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(generic, selected));
                break;
            case INamedTypeSymbol nominal:
                var boundNominal = Assert.IsType<NominalBoundType>(actual);
                Assert.Null(boundNominal.Declaration);
                Assert.Equal(nominal.SpecialType, boundNominal.RoslynSymbol!.SpecialType);
                Assert.Equal(nominal.ContainingAssembly.Identity, boundNominal.RoslynSymbol.ContainingAssembly.Identity);
                Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(nominal, boundNominal.RoslynSymbol));
                break;
            default:
                Assert.Fail($"Unexpected oracle type {expected}");
                break;
        }
    }

    public static IEnumerable<object[]> NativeMatrix()
    {
        for (var sourceShape = 0; sourceShape < 4; sourceShape++)
        for (var targetShape = 0; targetShape < 4; targetShape++)
        foreach (var expression in new[] { false, true })
        foreach (var locals in new[] { false, true })
            yield return [sourceShape, targetShape, expression, locals];
    }

    [Theory]
    [MemberData(nameof(NativeMatrix))]
    public void A4_NativeNamedArrays_ContainerAndElementAreIndependent(
        int sourceShape, int targetShape, bool expression, bool locals)
    {
        var sourceType = ArraySpelling(sourceShape);
        var targetType = ArraySpelling(targetShape);
        var supplied = locals ? "second" : "values";
        var source = $$"""
            module ArrayInputs
              func Take public ({{targetType}}:items) → int
                !
                return 1
              func Caller public ({{sourceType}}:values) → int
                !
                {{(locals ? "let first = values\n    let second = first" : "")}}
                {{(expression ? "return " : "")}}Take(items: {{supplied}})
                {{(expression ? "" : "return 0")}}
            """;
        var (module, diagnostics) = Bind(source);
        var caller = module.Functions.Single(f => f.Symbol.Name == "Caller");
        var call = expression
            ? Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(caller.Body[^1]).Expression)
            : Assert.IsType<BoundCallExpression>(
                Assert.Single(caller.Body.OfType<BoundExpressionStatement>()).Expression);
        var argument = Assert.Single(call.Arguments);
        var array = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(argument));
        Assert.Equal(1, array.Rank);
        Assert.Equal((sourceShape & 1) != 0 ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            array.NullableAnnotation);
        Assert.Equal((sourceShape & 2) != 0 ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(array.ElementType).NullableAnnotation);
        var match = Assert.Single(call.SelectedOverloadMatches);
        Assert.Equal("Take", match.Function.Name);
        var mapping = Assert.Single(match.Arguments);
        Assert.Equal(0, mapping.ArgumentIndex);
        Assert.Equal(0, mapping.ParameterIndex);
        Assert.False(mapping.IsExpandedParams);
        Assert.Equal(supplied, source.Substring(argument.Span.Start, argument.Span.Length));

        var containerMismatch = (sourceShape & 1) != 0 && (targetShape & 1) == 0;
        var elementMismatch = (sourceShape & 2) != 0 && (targetShape & 2) == 0;
        var findings = diagnostics.Where(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter).ToArray();
        Assert.Equal(containerMismatch || elementMismatch, findings.Length != 0);
        if (findings.Length != 0)
        {
            var finding = Assert.Single(findings);
            Assert.Equal(argument.Span, finding.Span);
            Assert.Contains("'items'", finding.Message);
            Assert.Equal(BindingReceivingBoundary.MethodArgument, finding.BindingContext?.Boundary);
            Assert.Equal(BindingReceivingShape.Array, finding.BindingContext?.Shape);
            Assert.Equal(SemanticsVersion.NullabilitySeverityFor(), finding.Severity);
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(finding));
            if (containerMismatch)
                Assert.Contains("container", finding.Message, StringComparison.OrdinalIgnoreCase);
            if (elementMismatch)
                Assert.Contains("element", finding.Message, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(diagnostics, d => d.Code is DiagnosticCode.NullableToNonNullableBinding
            or DiagnosticCode.NullableReturnFromNonNullable);
        var compiled = Program.Compile(source, "a4-native-array.calr");
        Assert.Equal(containerMismatch || elementMismatch, compiled.HasErrors);
        if (!compiled.HasErrors)
        {
            var emitted = AssertEmittedArrayParameter(compiled.GeneratedCode, "Take", 1);
            Assert.Equal((targetShape & 1) != 0 ? Microsoft.CodeAnalysis.NullableAnnotation.Annotated
                : Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated, emitted.NullableAnnotation);
            Assert.Equal((targetShape & 2) != 0 ? Microsoft.CodeAnalysis.NullableAnnotation.Annotated
                : Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated, emitted.ElementType.NullableAnnotation);
        }
    }

    [Theory]
    [InlineData("string[]?", false)]
    [InlineData("string?[]?", true)]
    public void StageB_NullGuardNarrowsOnlyArrayContainer(string sourceType, bool elementMismatch)
    {
        var source = $$"""
            module GuardedArray
              func Take public (string[]:items) → int
                !
                return 1
              func Probe public ({{sourceType}}:items) → int
                !
                if items != null
                  return Take(items)
                return 0
            """;
        var (_, diagnostics) = Bind(source);
        Assert.Equal(elementMismatch, diagnostics.Any(d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && BindingDiagnosticPolicy.IsCompilationError(d)));
        var result = Program.Compile(source, "guarded-array.calr");
        Assert.Equal(elementMismatch, result.HasErrors);
    }

    [Theory]
    [InlineData("text.ToCharArray()", true)]
    [InlineData("new char[2]", false)]
    public void ObliviousArrayInitializerEstablishesOnlyContainerFlow(string initializer, bool metadataCall)
    {
        var source = $$"""
            module InitializedArray nullable oblivious
              func Probe public (string:text) → void
                ! alloc, mut
                let char[]:values = {{initializer}}
                System.Array.Reverse(values)
            """;
        var (bound, diagnostics) = Bind(source);
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var probe = Assert.Single(bound.Functions);
        var declaration = Assert.IsType<BoundBindStatement>(probe.Body[0]);
        var declared = Assert.IsType<ArrayBoundType>(declaration.Variable.MethodInputArrayType);
        Assert.Equal(NullableAnnotation.Oblivious, declared.NullableAnnotation);
        if (metadataCall)
        {
            var call = Assert.IsType<BoundCallExpression>(declaration.Initializer);
            Assert.NotNull(call.ResolvedMetadataMethod);
            Assert.Equal("ToCharArray", call.ResolvedMetadataMethod.Name);
            Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated,
                Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod.ReturnType).NullableAnnotation);
        }
        var receiving = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(probe.Body[1]).Expression);
        var argument = Assert.IsType<BoundVariableExpression>(Assert.Single(receiving.Arguments));
        var flow = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(argument));
        Assert.Equal(NullableAnnotation.NotAnnotated, flow.NullableAnnotation);
        Assert.Same(declared.ElementType, flow.ElementType);
        Assert.Same(declaration.Variable, argument.Variable.StorageIdentity);

        var compiled = Program.Compile(source, "initialized-array.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        using var context = MetadataContext.Create();
        var tree = CSharpSyntaxTree.ParseText(compiled.GeneratedCode);
        var generated = context.HostCompilationForBinder.AddSyntaxTrees(tree);
        var local = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(node => node.Identifier.ValueText == "values");
        var symbol = Assert.IsAssignableFrom<ILocalSymbol>(generated.GetSemanticModel(tree).GetDeclaredSymbol(local));
        Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.None, symbol.NullableAnnotation);
        using var image = new MemoryStream();
        Assert.True(generated.Emit(image).Success);
    }

    // 2026-11-14: inside a `nullable oblivious` module an oblivious producer is accepted at a
    // non-null boundary; a `?T` producer still taints the local and is rejected.
    [Theory]
    [InlineData("nullable", false, true)]
    [InlineData("legacy", false, false)]
    [InlineData("NullableProducer(nullable)", false, true)]
    [InlineData("LegacyProducer()", false, false)]
    [InlineData("unknown.ToCharArray()", true, false)]
    public void ObliviousArrayInitializerRequiresActualNonNullProducer(string initializer, bool unselected, bool rejected)
    {
        var source = $$"""
            module UnprovenArray nullable oblivious
              func NullableProducer public (char[]?:value) → char[]?
                !
                return value
              func LegacyProducer public () → char[]
                ! alloc
                return new char[1]
              func Probe public (char[]?:nullable, char[]:legacy, object:unknown) → void
                ! alloc, mut
                let char[]:values = {{initializer}}
                System.Array.Reverse(values)
            """;
        var (bound, diagnostics) = Bind(source);
        var probe = bound.Functions.Single(function => function.Symbol.Name == "Probe");
        var declaration = Assert.IsType<BoundBindStatement>(probe.Body[0]);
        if (unselected)
        {
            var call = Assert.IsType<BoundCallExpression>(declaration.Initializer);
            Assert.Null(call.ResolvedMetadataMethod);
            Assert.Empty(call.SelectedOverloadMatches);
        }
        Assert.Equal(rejected, diagnostics.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && diagnostic.Message.Contains("container", StringComparison.Ordinal)));
        Assert.Equal(rejected, Program.Compile(source, "unproven-array.calr").Diagnostics.Errors.Any(
            diagnostic => diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
    }

    [Theory]
    [InlineData("string", NullableAnnotation.Oblivious)]
    [InlineData("string?", NullableAnnotation.Annotated)]
    public void InitializedArrayContainerDoesNotStrengthenDeclaredElements(
        string elementType, NullableAnnotation annotation)
    {
        var source = $$"""
            module ArrayElements nullable oblivious
              func Probe public () → void
                ! alloc, fw
                let {{elementType}}[]:values = new {{elementType}}[1]
                System.IO.File.WriteAllLines("unused", values)
            """;
        var (bound, diagnostics) = Bind(source);
        var probe = Assert.Single(bound.Functions);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(probe.Body[1]).Expression);
        var array = Assert.IsType<ArrayBoundType>(
            NullabilityChecker.GetMethodInputArrayType(call.Arguments[1]));
        Assert.Equal(NullableAnnotation.NotAnnotated, array.NullableAnnotation);
        Assert.Equal(annotation, Assert.IsType<NominalBoundType>(array.ElementType).NullableAnnotation);
        if (annotation == NullableAnnotation.Oblivious)
        {
            // 2026-11-14: oblivious elements are accepted inside a `nullable oblivious` module.
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
            Assert.DoesNotContain(Program.Compile(source, "array-elements.calr").Diagnostics.Errors,
                diagnostic => diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
            return;
        }
        var mismatch = Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Contains("element", mismatch.Message);
        Assert.DoesNotContain("requires non-null array container", mismatch.Message);
        Assert.True(Program.Compile(source, "array-elements.calr").HasErrors);
    }

    [Theory]
    [InlineData("set values null", true)]
    // A rebind from an oblivious parameter writes no null the module introduced (2026-11-14).
    [InlineData("var char[]:values = other", false)]
    [InlineData("Change(ref values)", true)]
    [InlineData("Replace(out values)", true)]
    [InlineData("if flag\n  set values null", true)]
    [InlineData("if flag\n  System.Array.Reverse(values)", false)]
    [InlineData("while flag\n  System.Array.Reverse(values)\n  set values null", true)]
    [InlineData("while flag\n  System.Array.Reverse(values)\n  Change(ref values)", true)]
    [InlineData("for i from 0 to 1 step 1\n  System.Array.Reverse(values)\n  set values null", true)]
    [InlineData("let System.Action:later ! mut = () ! mut => Change(ref values)\nlater()", true)]
    [InlineData("let System.Action:later ! mut = () ! mut => Change(ref values)", false)]
    public void InitializedArrayContainerFlowUsesStorageInvalidation(string operation, bool rejected)
    {
        var source = """
            module ArrayWrites nullable oblivious
              func Change public (ref char[]:value) → void
                ! mut
                set value null
              func Replace public (out char[]:value) → void
                ! mut
                set value null
              func Probe public (bool:flag, char[]:other) → void
                ! alloc, mut
                var char[]:values = new char[1]
            """ + "\n" + string.Join("\n", operation.Split('\n').Select(line => "    " + line))
            + "\n    System.Array.Reverse(values)";
        var compiled = Program.Compile(source, "array-writes.calr");
        var findings = compiled.Diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter).ToArray();
        Assert.Equal(rejected, findings.Length != 0);
        Assert.Equal(rejected, compiled.HasErrors);
        if (operation.StartsWith("while", StringComparison.Ordinal) || operation.StartsWith("for", StringComparison.Ordinal))
            Assert.Equal(2, findings.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializedArrayContainerProofCannotOutliveMutableCapture(bool mutable)
    {
        var source = $$"""
            module CapturedArray nullable oblivious
              func Probe public () → void
                ! alloc, mut
                {{(mutable ? "var" : "let")}} char[]:values = new char[1]
                let System.Action:later ! mut = () ! mut => System.Array.Reverse(values)
                {{(mutable ? "set values null" : "")}}
                later()
            """;
        var compiled = Program.Compile(source, "captured-array.calr");
        Assert.Equal(mutable, compiled.Diagnostics.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(mutable, compiled.HasErrors);
    }

    [Theory]
    [InlineData("ref", false)]
    [InlineData("out", false)]
    [InlineData("ref", true)]
    [InlineData("out", true)]
    public void StageB_MutatingConjunctionDoesNotRestoreInvalidatedArrayGuard(
        string modifier, bool nested)
    {
        var condition = nested
            ? $"(value != null && other != null) && Mutate({modifier} value)"
            : $"value != null && Mutate({modifier} value)";
        var source = $$"""
            module InvalidatedArrayGuard
              func Mutate public ({{modifier}} string[]?:value) → bool
                ! mut
                set value null
                return true
              func Take public (string[]:value) → void
                !
              func Probe public (string[]?:value, string[]?:other) → void
                ! mut
                if {{condition}}
                  {{(nested ? "Take(other)\n      " : "")}}Take(value)
            """;
        var result = Program.Compile(source, "invalidated-array-guard.calr");
        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics.Where(d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && BindingDiagnosticPolicy.IsCompilationError(d)));
        Assert.Equal("value", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
    }

    [Theory]
    [InlineData("ref", false)]
    [InlineData("out", false)]
    [InlineData("ref", true)]
    [InlineData("out", true)]
    public void StageB_OpaqueMutatingConjunctionInvalidatesEveryExplicitByRefArgument(
        string modifier, bool wrapped)
    {
        var mutation = $"MutateBoth({modifier} first, {modifier} second)";
        var right = wrapped ? $"({mutation} == true)" : mutation;
        var source = ConvertOpaqueMutator(modifier, twoArguments: true) + "\n" + $$"""
                func Take public (string[]:value) → void
                  !
                func Probe public (string[]?:first, string[]?:second) → void
                  ! mut
                  if (first != null && second != null) && {{right}}
                    Take(first)
                    Take(second)
            """;
        var bindingDiagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, bindingDiagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, bindingDiagnostics).Parse();
        var bound = new Binder(bindingDiagnostics).Bind(module);
        var probe = Assert.Single(bound.Functions.Where(function =>
            function.Symbol.Name.EndsWith(".Probe", StringComparison.Ordinal)));
        var condition = Assert.IsType<BoundBinaryExpression>(
            Assert.IsType<BoundIfStatement>(Assert.Single(probe.Body)).Condition);
        var call = wrapped
            ? Assert.IsType<BoundCallExpression>(
                Assert.IsType<BoundBinaryExpression>(condition.Right).Left)
            : Assert.IsType<BoundCallExpression>(condition.Right);
        Assert.Equal(new[] { modifier, modifier }, call.ArgumentModifiers);
        Assert.Equal(new[] { "first", "second" },
            condition.InvalidatedLeftNarrowingNames.Order(StringComparer.Ordinal).ToArray());
        var result = Program.Compile(source, "opaque-invalidated-array-guard.calr");
        var diagnostics = result.Diagnostics
            .Where(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
                && BindingDiagnosticPolicy.IsCompilationError(d))
            .ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.Equal(new[] { "first", "second" }, diagnostics
            .Select(d => source.Substring(d.Span.Start, d.Span.Length))
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("out")]
    public void StageB_OpaqueMutationInsideGuardedBodyInvalidatesNarrowing(string modifier)
    {
        var source = ConvertOpaqueMutator(modifier, twoArguments: false) + "\n" + $$"""
                func Take public (string[]:value) → void
                  !
                func Probe public (string[]?:value) → void
                  ! mut
                  if value != null
                    Mutate({{modifier}} value)
                    Take(value)
            """;
        var result = Program.Compile(source, "guarded-opaque-invalidation.calr");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    [Theory]
    [InlineData("ref", false)]
    [InlineData("out", false)]
    [InlineData("ref", true)]
    [InlineData("out", true)]
    public void StageB_BindingCleanOpaqueMutationStillRejectsUnknownEffects(
        string modifier, bool twoArguments)
    {
        var parameters = twoArguments ? "string[]?:first, string[]?:second" : "string[]?:value";
        var mutation = twoArguments
            ? $"MutateBoth({modifier} first, {modifier} second)"
            : $"Mutate({modifier} value)";
        var source = ConvertOpaqueMutator(modifier, twoArguments) + "\n" + $$"""
                func Probe public ({{parameters}}) → void
                  ! mut
                  {{mutation}}
            """;

        var result = Program.Compile(source, "opaque-mutation-effects.calr");

        Assert.True(result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("out")]
    public void StageB_NestedConjunctionMutationInvalidatesEnclosingArrayGuard(string modifier)
    {
        var source = $$"""
            module EnclosingGuardInvalidation
              func Mutate public ({{modifier}} string[]?:value) → bool
                ! mut
                set value null
                return true
              func Take public (string[]:value) → void
                !
              func Probe public (string[]?:value) → void
                ! mut,cw
                if value != null
                  if value != null && Mutate({{modifier}} value)
                    System.Console.WriteLine("mutated")
                  Take(value)
            """;

        var result = Program.Compile(source, "enclosing-array-guard-invalidation.calr");

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    [Theory]
    [InlineData("ref", "string[]?", "string[]")]
    [InlineData("out", "string[]?", "string[]")]
    [InlineData("ref", "string?", "string")]
    [InlineData("out", "string?", "string")]
    public void StageB_UninvokedLambdaMutationDoesNotInvalidateOuterGuard(
        string modifier, string nullableType, string nonNullableType)
    {
        var source = $$"""
            module DeferredMutation
              func Mutate public ({{modifier}} {{nullableType}}:value) → void
                ! mut
                set value null
              func Take public ({{nonNullableType}}:value) → void
                !
              func Probe public ({{nullableType}}:value) → void
                ! mut
                if value != null
                  let System.Action:later ! mut = () ! mut => Mutate({{modifier}} value)
                  Take(value)
            """;

        var result = Program.Compile(source, "deferred-mutation.calr");

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    [Theory]
    [InlineData("ref", "string[]?", "string[]")]
    [InlineData("out", "string[]?", "string[]")]
    [InlineData("ref", "string?", "string")]
    [InlineData("out", "string?", "string")]
    public void StageB_DeferredLambdaInConjunctionDoesNotInvalidateGuard(
        string modifier, string nullableType, string nonNullableType)
    {
        var source = $$"""
            module DeferredConjunction
              delegate DeferredCallback () → void
                ! mut
              func Mutate public ({{modifier}} {{nullableType}}:value) → bool
                ! mut
                set value null
                return true
              func Ignore public (DeferredCallback:action ! mut) → bool
                !
                return true
              func Take public ({{nonNullableType}}:value) → void
                !
              func Probe public ({{nullableType}}:value) → void
                ! mut
                if value != null && Ignore(() ! mut =>
                  Mutate({{modifier}} value)
                )
                  Take(value)
            """;

        var result = Program.Compile(source, "deferred-conjunction.calr");

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && BindingDiagnosticPolicy.IsCompilationError(diagnostic));

        var (bound, diagnostics) = Bind(source);
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var probe = Assert.Single(bound.Functions.Where(function => function.Symbol.Name == "Probe"));
        var condition = Assert.IsType<BoundBinaryExpression>(
            Assert.IsType<BoundIfStatement>(Assert.Single(probe.Body)).Condition);
        var ignored = Assert.IsType<BoundCallExpression>(condition.Right);
        Assert.Equal("Ignore", ignored.Target);
        var callback = Assert.IsType<BoundLambdaExpression>(Assert.Single(ignored.Arguments));
        Assert.Equal("VOID", callback.ReturnTypeName);
        var mutation = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(Assert.Single(callback.StatementBody!)).Expression);
        Assert.Equal(new[] { modifier }, mutation.ArgumentModifiers);
        using var context = MetadataContext.Create();
        var generated = context.HostCompilationForBinder.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(result.GeneratedCode));
        Assert.DoesNotContain(generated.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("ref", "string[]?", "string[]")]
    [InlineData("out", "string[]?", "string[]")]
    [InlineData("ref", "string?", "string")]
    [InlineData("out", "string?", "string")]
    public void StageB_InvokedCapturedLambdaInvalidatesGuard(
        string modifier, string nullableType, string nonNullableType)
    {
        var source = $$"""
            module InvokedDeferredMutation
              func Mutate public ({{modifier}} {{nullableType}}:value) → void
                ! mut
                set value null
              func Take public ({{nonNullableType}}:value) → void
                !
              func Probe public ({{nullableType}}:value) → void
                ! mut
                if value != null
                  let System.Action:later ! mut = () ! mut => Mutate({{modifier}} value)
                  later()
                  Take(value)
            """;

        var result = Program.Compile(source, "invoked-deferred-mutation.calr");

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void A4_RealBclNamedArrayInput_PreservesContainerAndElements(
        bool nullableContainer, bool nullableElements, bool locals)
    {
        var sourceType = ArraySpelling((nullableContainer ? 1 : 0) | (nullableElements ? 2 : 0));
        var supplied = locals ? "second" : "values";
        var source = $$"""
            module ArrayBcl
              func Caller public ({{sourceType}}:values) → void
                {{(locals ? "let first = values\n    let second = first" : "")}}
                System.IO.File.WriteAllLines(contents: {{supplied}}, path: "not-executed.txt")
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.Single(Assert.Single(module.Functions).Body.OfType<BoundExpressionStatement>()).Expression);
        Assert.Equal([1, 0], call.ArgumentParameterIndices);
        var array = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(call.Arguments[0]));
        Assert.Equal(nullableContainer ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            array.NullableAnnotation);
        Assert.Equal(nullableElements ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(array.ElementType).NullableAnnotation);
        var findings = diagnostics.Where(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter).ToArray();
        Assert.Equal(nullableContainer || nullableElements, findings.Length != 0);
        if (findings.Length > 0)
        {
            var finding = Assert.Single(findings);
            Assert.Equal(call.Arguments[0].Span, finding.Span);
            Assert.Contains("'contents'", finding.Message);
            Assert.Equal(BindingReceivingShape.Array, finding.BindingContext?.Shape);
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(finding));
        }
        Assert.Equal(nullableContainer || nullableElements,
            diagnostics.Any(BindingDiagnosticPolicy.IsCompilationError));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    public void A4_ConvertedRectangularArrays_RetainRankAndIndependentAnnotations(int rank, int shape)
    {
        var brackets = $"[{new string(',', rank - 1)}]";
        var type = $"string{((shape & 2) != 0 ? "?" : "")}{brackets}{((shape & 1) != 0 ? "?" : "")}";
        // These cases run their own binding and strict compilation of native output.
        var converted = new CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert($$"""
            #nullable enable
            public static class RectangularArrays {
                public static int Take(string{{brackets}} items) { return 1; }
                public static int Caller({{type}} values) {
                    var first = values;
                    var second = first;
                    return Take(items: second);
                }
            }
            """, "a4-rectangular.cs");
        Assert.True(converted.Success);
        Assert.NotNull(converted.CalorSource);
        var (module, diagnostics) = Bind(converted.CalorSource!);
        var caller = module.Functions.Single(f => f.Symbol.Name.EndsWith(".Caller", StringComparison.Ordinal));
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(caller.Body[^1]).Expression);
        Assert.Single(call.SelectedOverloadMatches);
        var array = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(Assert.Single(call.Arguments)));
        Assert.Equal(rank, array.Rank);
        Assert.Equal((shape & 1) != 0 ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            array.NullableAnnotation);
        Assert.Equal((shape & 2) != 0 ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(array.ElementType).NullableAnnotation);
        Assert.Equal(shape != 0, diagnostics.Any(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(shape != 0, diagnostics.Any(BindingDiagnosticPolicy.IsCompilationError));
        var compiled = Program.Compile(converted.CalorSource!, "a4-rectangular.calr");
        Assert.Equal(shape != 0, compiled.HasErrors);
        if (!compiled.HasErrors)
            AssertEmittedArrayParameter(compiled.GeneratedCode, "Take", rank);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void A4_ConvertedArrayCreation_SeesNullableLaterElement(
        bool nullable, bool named, bool expression)
    {
        var converted = new CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert($$"""
            #nullable enable
            public static class CreatedArrays {
                public static int Take(params string[] items) { return 1; }
                public static int Caller(string{{(nullable ? "?" : "")}} later) {
                    var first = new[] { "first", later };
                    var second = first;
                    {{(expression ? "return " : "")}}Take({{(named ? "items: " : "")}}second);
                    {{(expression ? "" : "return 0;")}}
                }
            }
            """, "a4-created.cs");
        output.WriteLine(converted.CalorSource);
        Assert.True(converted.Success);
        Assert.NotNull(converted.CalorSource);
        var (module, diagnostics) = Bind(converted.CalorSource!);
        var caller = module.Functions.Single(f => f.Symbol.Name.EndsWith(".Caller", StringComparison.Ordinal));
        var call = expression
            ? Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(caller.Body[^1]).Expression)
            : Assert.IsType<BoundCallExpression>(
                Assert.Single(caller.Body.OfType<BoundExpressionStatement>()).Expression);
        var argument = Assert.Single(call.Arguments);
        var mapping = Assert.Single(Assert.Single(call.SelectedOverloadMatches).Arguments);
        Assert.False(mapping.IsExpandedParams);
        var array = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(argument));
        Assert.Equal(NullableAnnotation.NotAnnotated, array.NullableAnnotation);
        Assert.Equal(nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(array.ElementType).NullableAnnotation);
        var findings = diagnostics.Where(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter).ToArray();
        Assert.Equal(nullable, findings.Length != 0);
        if (nullable)
        {
            var finding = Assert.Single(findings);
            Assert.Equal(argument.Span, finding.Span);
            Assert.Equal(BindingReceivingShape.Array, finding.BindingContext?.Shape);
            Assert.Contains("elements", finding.Message);
        }
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableToNonNullableBinding);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableReturnFromNonNullable);
        Assert.Equal(nullable, diagnostics.Any(BindingDiagnosticPolicy.IsCompilationError));
        var compiled = Program.Compile(converted.CalorSource!, "a4-created.calr");
        Assert.Equal(nullable, compiled.HasErrors);
        if (!compiled.HasErrors)
            AssertEmittedArrayParameter(compiled.GeneratedCode, "Take", 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A4_ConvertedArrayCreation_ExplicitBindingRetainsElementRejection(bool nullable)
    {
        var converted = new CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert($$"""
            #nullable enable
            public static class ExplicitArrayLocal {
                public static void Probe(string{{(nullable ? "?" : "")}} later) {
                    string[] values = new[] { "first", later };
                }
            }
            """, "a4-explicit-array-local.cs");
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.NotNull(converted.CalorSource);
        var (_, diagnostics) = Bind(converted.CalorSource!);
        Assert.Equal(nullable, diagnostics.Any(d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && BindingDiagnosticPolicy.IsCompilationError(d)));
        Assert.DoesNotContain(diagnostics, d => d.Code is DiagnosticCode.NullableArgumentToNonNullableParameter
            or DiagnosticCode.NullableReturnFromNonNullable);
        var compiled = Program.Compile(converted.CalorSource!, "a4-explicit-array-local.calr");
        Assert.Equal(nullable, compiled.HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A4_RealBclExpandedLaterScalar_IsNotAnArrayReceivingShape(bool nullable)
    {
        var source = $$"""
            module ExpandedArrays
              func Caller public ({{(nullable ? "string?" : "string")}}:later) → string
                return System.IO.Path.Combine("a", "b", "c", "d", later)
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(module.Functions).Body.Single()).Expression);
        Assert.Equal([0, 0, 0, 0, 0], call.ArgumentParameterIndices);
        var findings = diagnostics.Where(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter).ToArray();
        Assert.Equal(nullable, findings.Length != 0);
        if (nullable)
        {
            var finding = Assert.Single(findings);
            Assert.Equal(call.Arguments[^1].Span, finding.Span);
            Assert.Equal(BindingReceivingShape.ScalarString, finding.BindingContext?.Shape);
            Assert.Contains("Expanded", finding.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A4_RealBclGenericArrayReturn_PreservesElementsAcrossActiveBoundaries(bool nullable)
    {
        var source = $$"""
            module GenericArrayProducer
              func Take public (string[]:items) → int
                return 1
              func Caller public ({{ArraySpelling(nullable ? 2 : 0)}}:values) → int
                let string[]:legacy = System.Linq.Enumerable.ToArray(values)
                let first = System.Linq.Enumerable.ToArray(values)
                let second = first
                return Take(second)
              func LegacyReturn public ({{ArraySpelling(nullable ? 2 : 0)}}:values) → string[]
                return System.Linq.Enumerable.ToArray(values)
            """;
        var (module, diagnostics) = Bind(source);
        var caller = module.Functions.Single(f => f.Symbol.Name == "Caller");
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(caller.Body[^1]).Expression);
        Assert.Single(call.SelectedOverloadMatches);
        var array = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(Assert.Single(call.Arguments)));
        Assert.NotNull(array.RoslynSymbol);
        Assert.Equal(NullableAnnotation.NotAnnotated, array.NullableAnnotation);
        Assert.Equal(nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(array.ElementType).NullableAnnotation);
        Assert.Equal(nullable, diagnostics.Any(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(nullable, diagnostics.Any(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.Equal(nullable, diagnostics.Any(d => d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.Equal(nullable, diagnostics.Any(BindingDiagnosticPolicy.IsCompilationError));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A4_ValueArrayContainer_IsAReferenceButItsElementsStayValues(bool nullable, bool bcl)
    {
        var source = $$"""
            module ValueArrays
              func Take public (int[]:items) → void
                !
              func Caller public (int[]{{(nullable ? "?" : "")}}:values) → void
                {{(bcl ? "! mut" : "!")}}
                {{(bcl ? "System.Array.Sort" : "Take")}}(values)
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallExpression>(Assert.Single(
            module.Functions.Single(f => f.Symbol.Name == "Caller").Body.OfType<BoundExpressionStatement>()).Expression);
        if (bcl) Assert.Equal([0], call.ArgumentParameterIndices);
        else Assert.Single(call.SelectedOverloadMatches);
        var finding = diagnostics.Where(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter).ToArray();
        Assert.Equal(nullable, finding.Length != 0);
        if (nullable)
        {
            Assert.Equal(BindingReceivingShape.Array, Assert.Single(finding).BindingContext?.Shape);
            Assert.Contains("container", finding[0].Message);
            Assert.DoesNotContain("STRING", finding[0].Message);
        }
        Assert.Equal(nullable, diagnostics.Any(BindingDiagnosticPolicy.IsCompilationError));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A4_KnownNativeNominalArray_UsesActualElementIdentityWithoutWideningPayloadPolicy(
        bool nullableContainer, bool nullableElements)
    {
        var converted = new CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert($$"""
            #nullable enable
            public class Foo { }
            public static class NominalArrays {
                public static int Take(Foo[] items) { return 1; }
                public static int Caller(Foo{{(nullableElements ? "?" : "")}}[]{{(nullableContainer ? "?" : "")}} values) {
                    var first = values;
                    var second = first;
                    return Take(items: second);
                }
            }
            """, "a4-nominal-array.cs");
        Assert.True(converted.Success);
        Assert.NotNull(converted.CalorSource);
        var (module, diagnostics) = Bind(converted.CalorSource!);
        var caller = module.Functions.Single(f => f.Symbol.Name.EndsWith(".Caller", StringComparison.Ordinal));
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(caller.Body[^1]).Expression);
        Assert.Single(call.SelectedOverloadMatches);
        var array = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(Assert.Single(call.Arguments)));
        Assert.True(Assert.IsType<NominalBoundType>(array.ElementType).IsKnownReferenceType);
        Assert.Equal(nullableContainer, diagnostics.Any(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(nullableContainer, diagnostics.Any(BindingDiagnosticPolicy.IsCompilationError));
    }

    [Theory]
    [InlineData("Foo[,]", "Foo[]")]
    [InlineData("Foo[]", "Foo")]
    [InlineData("Foo", "Foo[]")]
    public void A4_ArrayAndRankIdentity_AreNotErasedByNativeClassConversion(string sourceType, string targetType)
    {
        var source = $$"""
            module ArrayRanks
              class Foo public
                field int:Value public
              func Take public ({{targetType}}:items) → int
                return 1
              func Caller public ({{sourceType}}:value) → int
                return Take(value)
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(
            module.Functions.Single(f => f.Symbol.Name == "Caller").Body[^1]).Expression);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload
            && BindingDiagnosticPolicy.IsCompilationError(d));
    }

    [Theory]
    [InlineData("Option<string[]>", "string[]")]
    [InlineData("string[]", "Option<string[]>")]
    [InlineData("int?[]", "int[]")]
    [InlineData("int[]", "string[]")]
    [InlineData("string[,]", "string[]")]
    [InlineData("List<string>", "string[]")]
    [InlineData("Missing[]", "string[]")]
    public void A4_IncompatibleOrUnsupportedInputs_DoNotAcquireAnArrayMatch(string sourceType, string targetType)
    {
        var source = $$"""
            module ArrayBoundaries
              func Take public ({{targetType}}:items) → int
                return 1
              func Caller public ({{sourceType}}:value) → int
                return Take(value)
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(
            module.Functions.Single(f => f.Symbol.Name == "Caller").Body[^1]).Expression);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
        output.WriteLine("No selected map: incompatible/unsupported, NOT a null-safety acceptance.");
        foreach (var diagnostic in diagnostics) output.WriteLine(diagnostic.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A4_RealRoslynObliviousArray_GenericProjectionDoesNotInventNonNullElements(bool refArgument)
    {
        using var context = MetadataContext.Create();
        var tree = CSharpSyntaxTree.ParseText("""
            #nullable disable
            public class ArraySignatureFixture { public string[] Values; }
            """);
        var fixture = context.HostCompilationForBinder.AddSyntaxTrees(tree);
        var field = Assert.IsAssignableFrom<IFieldSymbol>(
            fixture.GetTypeByMetadataName("ArraySignatureFixture")!.GetMembers("Values").Single());
        var sourceArray = Assert.IsAssignableFrom<IArrayTypeSymbol>(field.Type);
        Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.None, sourceArray.ElementType.NullableAnnotation);
        var metadata = new MetadataBinder(context);
        var argument = new MetadataArgument(sourceArray,
            refArgument ? RefKind.Ref : RefKind.None);
        var result = refArgument
            ? metadata.ResolveCall(context.TryResolveType("System.Array")!, "Resize",
                [argument, new MetadataArgument(context.TryResolveType("System.Int32")!)], preserveArrayAnnotations: true)
            : metadata.ResolveCall(context.TryResolveType("System.Linq.Enumerable")!, "ToArray",
                [argument], preserveArrayAnnotations: true);
        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.None, Assert.Single(result.Symbol!.TypeArguments).NullableAnnotation);
        if (!refArgument)
        {
            var array = Assert.IsType<ArrayBoundType>(result.GetReturnBoundTypeEx());
            Assert.Equal(NullableAnnotation.Oblivious, Assert.IsType<NominalBoundType>(array.ElementType).NullableAnnotation);
        }
        var projection = MetadataBinder.BuildArgumentLocals([argument], preserveArrayAnnotations: true)
            + string.Join(", ", MetadataBinder.BuildArgumentExpressions([argument], preserveArrayAnnotations: true));
        output.WriteLine("Controlled Roslyn declared-type fixture + actual BCL inference; not a default Calor API capture.");
        output.WriteLine(projection);
    }

    public static IEnumerable<object[]> RoslynMatrix()
    {
        for (var sourceShape = 0; sourceShape < 5; sourceShape++)
        for (var targetShape = 0; targetShape < 5; targetShape++)
        foreach (var rank in new[] { 1, 2 })
            yield return [sourceShape, targetShape, rank];
    }

    [Theory]
    [MemberData(nameof(RoslynMatrix))]
    public void A4_RealRoslynArraySignatures_KeepObliviousAndComponentPredicates(
        int sourceShape, int targetShape, int rank)
    {
        var brackets = $"[{new string(',', rank - 1)}]";
        static string Type(int shape, string suffix) =>
            $"string{(shape != 4 && (shape & 2) != 0 ? "?" : "")}{suffix}{(shape != 4 && (shape & 1) != 0 ? "?" : "")}";
        var source = $$"""
            #nullable {{(sourceShape == 4 ? "disable" : "enable")}}
            public static class ArraySignatureFixture {
                public static {{Type(sourceShape, brackets)}} Produce() => throw new System.Exception();
            #nullable {{(targetShape == 4 ? "disable" : "enable")}}
                public static void Receive({{Type(targetShape, brackets)}} input) { }
            #nullable enable
                public static void Probe() { Receive(Produce()); }
            }
            """;
        var (supplied, receiving) = BindRoslynArrayFixture(source);
        var mismatch = NullabilityChecker.IsPossiblyNullAssignedTo(
            new ArrayFixtureExpression(supplied), receiving, BindingReceivingBoundary.MethodArgument, out var components);
        var container = targetShape != 4 && (targetShape & 1) == 0
            && (sourceShape == 4 || (sourceShape & 1) != 0);
        var elements = targetShape != 4 && (targetShape & 2) == 0
            && (sourceShape == 4 || (sourceShape & 2) != 0);
        Assert.Equal(container || elements, mismatch);
        Assert.Equal(container, components.HasFlag(NullabilityChecker.ArrayMismatch.Container));
        Assert.Equal(elements, components.HasFlag(NullabilityChecker.ArrayMismatch.Elements));
        Assert.Equal(rank, supplied.Rank);
        Assert.Equal(rank, receiving.Rank);
        Assert.NotNull(supplied.RoslynSymbol);
        Assert.NotNull(receiving.RoslynSymbol);
        if (sourceShape == 4)
        {
            Assert.Equal(NullableAnnotation.Oblivious, supplied.NullableAnnotation);
            Assert.Equal(NullableAnnotation.Oblivious, Assert.IsType<NominalBoundType>(supplied.ElementType).NullableAnnotation);
        }
        if (targetShape == 4)
            output.WriteLine("Oblivious receiving constraints remain unsupported, not a safe acceptance.");
    }

    [Theory]
    [InlineData("string?[][]", "string[][]", false, true)]
    [InlineData("string[]?[]", "string[][]", true, false)]
    [InlineData("string?[]?[]?", "string[][]", true, true)]
    [InlineData("string[][]", "string?[]?[]?", false, false)]
    public void A4_JaggedArrays_RecursivelyCompareContainerAndStringElementAnnotations(
        string sourceType, string targetType, bool container, bool elements)
    {
        var source = $$"""
            #nullable enable
            public static class ArraySignatureFixture {
                public static {{sourceType}} Produce() => throw new System.Exception();
                public static void Receive({{targetType}} input) { }
                public static void Probe() { Receive(Produce()); }
            }
            """;
        var (supplied, receiving) = BindRoslynArrayFixture(source);
        var mismatch = NullabilityChecker.IsPossiblyNullAssignedTo(
            new ArrayFixtureExpression(supplied), receiving, BindingReceivingBoundary.MethodArgument, out var components);

        Assert.Equal(container || elements, mismatch);
        Assert.Equal(container, components.HasFlag(NullabilityChecker.ArrayMismatch.Container));
        Assert.Equal(elements, components.HasFlag(NullabilityChecker.ArrayMismatch.Elements));
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<string?>", "System.Collections.Generic.List<string>", false, false)]
    [InlineData("System.Collections.Generic.List<string?>", "System.Collections.Generic.List<string>", true, false)]
    [InlineData("System.Collections.Generic.List<string?>", "System.Collections.Generic.List<string>", false, true)]
    [InlineData("System.Collections.Generic.List<string?>", "System.Collections.Generic.List<string>", true, true)]
    [InlineData("int?", "int?", false, false)]
    [InlineData("int?", "int?", true, false)]
    [InlineData("int?", "int?", false, true)]
    [InlineData("int?", "int?", true, true)]
    public void A4_RealRoslynGenericAndNullableValueElements_DoNotBecomeStringPayloadChecks(
        string sourceElement, string targetElement, bool nullableSource, bool nullableTarget)
    {
        var source = $$"""
            #nullable enable
            public static class ArraySignatureFixture {
                public static {{sourceElement}}[]{{(nullableSource ? "?" : "")}} Produce() => throw new System.Exception();
                public static void Receive({{targetElement}}[]{{(nullableTarget ? "?" : "")}} input) { }
                public static void Probe() { Receive(Produce()); }
            }
            """;
        var (supplied, receiving) = BindRoslynArrayFixture(source);
        Assert.IsType<GenericInstantiationBoundType>(supplied.ElementType);
        Assert.IsType<GenericInstantiationBoundType>(receiving.ElementType);
        var mismatch = NullabilityChecker.IsPossiblyNullAssignedTo(
            new ArrayFixtureExpression(supplied), receiving, BindingReceivingBoundary.MethodArgument, out var components);
        Assert.Equal(nullableSource && !nullableTarget, mismatch);
        Assert.False(components.HasFlag(NullabilityChecker.ArrayMismatch.Elements));
    }

    private (ArrayBoundType Supplied, ArrayBoundType Receiving) BindRoslynArrayFixture(string source)
    {
        using var context = MetadataContext.Create();
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = context.HostCompilationForBinder.AddSyntaxTrees(tree);
        Assert.DoesNotContain(compilation.GetDiagnostics(),
            d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var type = compilation.GetTypeByMetadataName("ArraySignatureFixture")!;
        var producer = Assert.IsAssignableFrom<IMethodSymbol>(Assert.Single(type.GetMembers("Produce")));
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.Expression.ToString() == "Receive");
        var consumer = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol);
        Assert.True(SymbolEqualityComparer.Default.Equals(Assert.Single(type.GetMembers("Receive")), consumer));
        output.WriteLine("Controlled Roslyn source/signature fixture with actual resolution; not a default Calor API or BCL capture.");
        return (Assert.IsType<ArrayBoundType>(MetadataBinderResult.ToBoundType(producer.ReturnType)),
            Assert.IsType<ArrayBoundType>(MetadataBinderResult.ToBoundType(Assert.Single(consumer.Parameters).Type)));
    }

    private sealed class ArrayFixtureExpression(ArrayBoundType type) : BoundExpression(default)
    {
        public override BoundType Type => type;
    }

    private static string ConvertOpaqueMutator(string modifier, bool twoArguments)
    {
        var signature = twoArguments
            ? $"bool MutateBoth({modifier} string[]? first, {modifier} string[]? second)"
            : $"void Mutate({modifier} string[]? value)";
        var mutation = twoArguments ? "first = null; second = null; return true;" : "value = null;";
        var source = $$"""
            #nullable enable
            public class Holder {
                public {{signature}} {
                    static void KeepOpaque() { }
                    {{mutation}}
                }
            }
            """;
        var converted = new CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert(source, "opaque-array-mutator.cs");
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        var holder = Assert.Single(Assert.IsType<Ast.ModuleNode>(converted.Ast).Classes);
        var preserved = Assert.Single(holder.InteropBlocks);
        Assert.Equal(Ast.InteropMemberKind.Method, preserved.MemberKind);
        Assert.Equal("method", preserved.FeatureName);
        var originalMethod = Assert.Single(CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>());
        Assert.Equal(originalMethod.ToFullString(), preserved.CSharpCode);
        return Assert.IsType<string>(converted.CalorSource);
    }

    private static IArrayTypeSymbol AssertEmittedArrayParameter(string generated, string method, int rank)
    {
        var tree = CSharpSyntaxTree.ParseText(generated);
        var compilation = MetadataContext.Create().HostCompilationForBinder.AddSyntaxTrees(tree);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.True(!errors.Any(), generated + "\n" + string.Join("\n", errors));
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == method);
        var symbol = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration));
        var parameter = Assert.IsAssignableFrom<IArrayTypeSymbol>(Assert.Single(symbol.Parameters).Type);
        Assert.Equal(rank, parameter.Rank);
        return parameter;
    }

    private static string ArraySpelling(int shape) =>
        $"string{((shape & 2) != 0 ? "?" : "")}[]{((shape & 1) != 0 ? "?" : "")}";

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(string source)
    {
        var parsing = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, parsing).TokenizeAllForParser(), parsing).Parse();
        Assert.False(parsing.HasErrors, string.Join("\n", parsing));
        var diagnostics = new DiagnosticBag();
        return (new Binder(diagnostics).Bind(ast), diagnostics);
    }
}
