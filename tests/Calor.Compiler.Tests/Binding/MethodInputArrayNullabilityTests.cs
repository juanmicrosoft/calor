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
            §M{m1:ArrayInputs}
              §F{take:Take:pub} ({{targetType}}:items) -> i32
                §E{}
                §R 1
              §F{caller:Caller:pub} ({{sourceType}}:values) -> i32
                §E{}
                {{(locals ? "§B{first} values\n    §B{second} first" : "")}}
                {{(expression ? "§R " : "")}}§C{Take} §A[items] {{supplied}} §/C
                {{(expression ? "" : "§R 0")}}
            """;
        var (module, diagnostics) = Bind(source);
        Assert.True(!diagnostics.Any(BindingDiagnosticPolicy.IsCompilationError),
            string.Join("\n", diagnostics));
        var caller = module.Functions.Single(f => f.Symbol.Name == "Caller");
        var callExpression = expression
            ? Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(caller.Body[^1]).Expression)
            : null;
        var callStatement = expression ? null : Assert.Single(caller.Body.OfType<BoundCallStatement>());
        var argument = Assert.Single(callExpression?.Arguments ?? callStatement!.Arguments);
        var array = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(argument));
        Assert.Equal(1, array.Rank);
        Assert.Equal((sourceShape & 1) != 0 ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            array.NullableAnnotation);
        Assert.Equal((sourceShape & 2) != 0 ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(array.ElementType).NullableAnnotation);
        var match = Assert.Single(callExpression?.SelectedOverloadMatches ?? callStatement!.SelectedOverloadMatches);
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
            Assert.False(BindingDiagnosticPolicy.IsCompilationError(finding));
            if (containerMismatch)
                Assert.Contains("container", finding.Message, StringComparison.OrdinalIgnoreCase);
            if (elementMismatch)
                Assert.Contains("element", finding.Message, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(diagnostics, d => d.Code is DiagnosticCode.NullableToNonNullableBinding
            or DiagnosticCode.NullableReturnFromNonNullable);
        var compiled = Program.Compile(source, "a4-native-array.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var emitted = AssertEmittedArrayParameter(compiled.GeneratedCode, "Take", 1);
        Assert.Equal((targetShape & 1) != 0 ? Microsoft.CodeAnalysis.NullableAnnotation.Annotated
            : Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated, emitted.NullableAnnotation);
        Assert.Equal((targetShape & 2) != 0 ? Microsoft.CodeAnalysis.NullableAnnotation.Annotated
            : Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated, emitted.ElementType.NullableAnnotation);
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
            §M{m1:ArrayBcl}
              §F{caller:Caller:pub} ({{sourceType}}:values) -> void
                {{(locals ? "§B{first} values\n    §B{second} first" : "")}}
                §C{System.IO.File.WriteAllLines} §A[contents] {{supplied}} §A[path] STR:"not-executed.txt" §/C
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.Single(Assert.Single(module.Functions).Body.OfType<BoundCallStatement>());
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
            Assert.False(BindingDiagnosticPolicy.IsCompilationError(finding));
        }
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
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
        var converted = new CSharpToCalorConverter().Convert($$"""
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
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
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
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var compiled = Program.Compile(converted.CalorSource!, "a4-rectangular.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
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
        var converted = new CSharpToCalorConverter().Convert($$"""
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
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        var (module, diagnostics) = Bind(converted.CalorSource!);
        var caller = module.Functions.Single(f => f.Symbol.Name.EndsWith(".Caller", StringComparison.Ordinal));
        var call = expression
            ? (BoundNode)Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(caller.Body[^1]).Expression)
            : Assert.Single(caller.Body.OfType<BoundCallStatement>());
        var argument = call is BoundCallExpression expr ? Assert.Single(expr.Arguments)
            : Assert.Single(((BoundCallStatement)call).Arguments);
        var matches = call is BoundCallExpression expr2 ? expr2.SelectedOverloadMatches
            : ((BoundCallStatement)call).SelectedOverloadMatches;
        var mapping = Assert.Single(Assert.Single(matches).Arguments);
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
        Assert.DoesNotContain(diagnostics, d => d.Code is DiagnosticCode.NullableToNonNullableBinding
            or DiagnosticCode.NullableReturnFromNonNullable);
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var compiled = Program.Compile(converted.CalorSource!, "a4-created.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        AssertEmittedArrayParameter(compiled.GeneratedCode, "Take", 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A4_RealBclExpandedLaterScalar_IsNotAnArrayReceivingShape(bool nullable)
    {
        var source = $$"""
            §M{m1:ExpandedArrays}
              §F{caller:Caller:pub} ({{(nullable ? "?str" : "str")}}:later) -> str
                §R §C{System.IO.Path.Combine} §A "a" §A "b" §A "c" §A "d" §A later §/C
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
    public void A4_RealBclGenericArrayReturn_PreservesElementsOnlyForMethodInputs(bool nullable)
    {
        var source = $$"""
            §M{m1:GenericArrayProducer}
              §F{take:Take:pub} ([str]:items) -> i32
                §R 1
              §F{caller:Caller:pub} ({{ArraySpelling(nullable ? 2 : 0)}}:values) -> i32
                §B{legacy:[str]} §C{System.Linq.Enumerable.ToArray} §A values §/C
                §B{first} §C{System.Linq.Enumerable.ToArray} §A values §/C
                §B{second} first
                §R §C{Take} §A second §/C
              §F{return:LegacyReturn:pub} ({{ArraySpelling(nullable ? 2 : 0)}}:values) -> [str]
                §R §C{System.Linq.Enumerable.ToArray} §A values §/C
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
        Assert.DoesNotContain(diagnostics, d => d.Code is DiagnosticCode.NullableToNonNullableBinding
            or DiagnosticCode.NullableReturnFromNonNullable);
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A4_ValueArrayContainer_IsAReferenceButItsElementsStayValues(bool nullable, bool bcl)
    {
        var source = $$"""
            §M{m1:ValueArrays}
              §F{take:Take:pub} (i32[]:items) -> void
                §E{}
              §F{caller:Caller:pub} (i32[]{{(nullable ? "?" : "")}}:values) -> void
                §C{{{(bcl ? "System.Array.Sort" : "Take")}}} §A values §/C
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.Single(module.Functions.Single(f => f.Symbol.Name == "Caller").Body.OfType<BoundCallStatement>());
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
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
    }

    [Theory]
    [InlineData("Option<str[]>", "str[]")]
    [InlineData("str[]", "Option<str[]>")]
    [InlineData("i32?[]", "i32[]")]
    [InlineData("i32[]", "str[]")]
    [InlineData("str[,]", "str[]")]
    [InlineData("List<str>", "str[]")]
    [InlineData("Missing[]", "str[]")]
    public void A4_IncompatibleOrUnsupportedInputs_DoNotAcquireAnArrayMatch(string sourceType, string targetType)
    {
        var source = $$"""
            §M{m1:ArrayBoundaries}
              §F{take:Take:pub} ({{targetType}}:items) -> i32
                §R 1
              §F{caller:Caller:pub} ({{sourceType}}:value) -> i32
                §R §C{Take} §A value §/C
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
        $"{((shape & 1) != 0 ? "?" : "")}[{((shape & 2) != 0 ? "?" : "")}str]";

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(string source)
    {
        var parsing = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, parsing).TokenizeAllForParser(), parsing).Parse();
        Assert.False(parsing.HasErrors, string.Join("\n", parsing));
        var diagnostics = new DiagnosticBag();
        return (new Binder(diagnostics).Bind(ast), diagnostics);
    }
}
