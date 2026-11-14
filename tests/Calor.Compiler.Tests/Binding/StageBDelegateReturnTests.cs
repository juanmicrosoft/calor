using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class StageBDelegateReturnTests
{
    public static IEnumerable<object[]> DelegateCases()
    {
        foreach (var shape in new[] { "generic", "array-element", "array-container" })
        foreach (var invoke in new[] { false, true })
        foreach (var inferred in new[] { false, true })
        foreach (var nullable in new[] { false, true })
        foreach (var boundary in new[] { "binding", "return", "input" })
            yield return [shape, invoke, inferred, nullable, boundary];
    }

    [Theory]
    [MemberData(nameof(DelegateCases))]
    public void DeclaredDelegateReturn_PreservesReceivingShape(
        string shape, bool invoke, bool inferred, bool nullable, string boundary)
    {
        var targetType = shape == "generic" ? "List<string>" : "string[]";
        var sourceType = !nullable ? targetType : shape switch
        {
            "generic" => "List<string?>",
            "array-element" => "string?[]",
            "array-container" => "string[]?",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var call = $"reader{(invoke ? ".Invoke" : "")}()";
        var value = inferred ? "produced" : call;
        var source = $$"""
            module DelegateReturns
              using System.Collections.Generic
              delegate Reader () → {{sourceType}}
                ! unknown
              func Take public ({{targetType}}:value) → void
                !
              func Probe public (Reader:reader ! unknown) → {{(boundary == "return" ? targetType : "void")}}
                !
            {{(inferred ? $"    let produced = {call}" : "")}}
                {{Sink(boundary, targetType, value)}}
            """;
        AssertBoundary(source, boundary, nullable);
    }

    [Fact]
    public void ResolvedDelegateReturnIdentitiesSurviveNativeRoundTrip()
    {
        const string source = """
            module DelegateIdentities
              using System.Collections.Generic
              class Lookalikes
                class String
              delegate Reader () → List<string?>
                ! unknown
              delegate NestedReader () → List<List<string?>>
                ! unknown
              delegate ElementReader () → string?[]
                ! unknown
              delegate ContainerReader () → string[]?
                ! unknown
              delegate SafeReader () → List<string>
                ! unknown
              delegate NominalReader () → List<Lookalikes.String?>
                ! unknown
              func Probe public (Reader:reader ! unknown, NestedReader:nested ! unknown, ElementReader:elements ! unknown, ContainerReader:container ! unknown, SafeReader:safe ! unknown, NominalReader:lookalike ! unknown, Lookalikes.String:ordinary) → void
                !
                let List<string>:required = reader()
                let nestedValue = nested()
                let elementValue = elements()
                let containerValue = container()
                let safeValue = safe()
                let nominalValue = lookalike()
            """;

        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(candidate));
            var probe = Assert.Single(bound.Functions);
            var parameters = probe.Symbol.Parameters;
            var calls = BoundNodeHelpers.DescendantsAndSelf(probe).OfType<BoundCallExpression>().ToArray();
            Assert.Equal(6, calls.Length);
            for (var index = 0; index < calls.Length; index++)
            {
                var callable = parameters[index].FunctionType!;
                Assert.True(callable.Row.IsUnknown);
                Assert.Same(callable.ReturnType, calls[index].Type);
            }
            Assert.Null(parameters[^1].FunctionType);

            var generic = Assert.IsType<GenericInstantiationBoundType>(calls[0].Type);
            var payload = Assert.IsType<NominalBoundType>(Assert.Single(generic.TypeArguments));
            Assert.Equal(Microsoft.CodeAnalysis.SpecialType.System_String, payload.RoslynSymbol!.SpecialType);
            Assert.Equal(NullableAnnotation.Annotated, payload.NullableAnnotation);

            var nested = Assert.IsType<GenericInstantiationBoundType>(calls[1].Type);
            var inner = Assert.IsType<GenericInstantiationBoundType>(Assert.Single(nested.TypeArguments));
            Assert.Equal(NullableAnnotation.Annotated,
                Assert.IsType<NominalBoundType>(Assert.Single(inner.TypeArguments)).NullableAnnotation);

            var elements = Assert.IsType<ArrayBoundType>(calls[2].Type);
            Assert.Equal(NullableAnnotation.NotAnnotated, elements.NullableAnnotation);
            Assert.Equal(NullableAnnotation.Annotated,
                Assert.IsType<NominalBoundType>(elements.ElementType).NullableAnnotation);
            var container = Assert.IsType<ArrayBoundType>(calls[3].Type);
            Assert.Equal(NullableAnnotation.Annotated, container.NullableAnnotation);
            Assert.Equal(NullableAnnotation.NotAnnotated,
                Assert.IsType<NominalBoundType>(container.ElementType).NullableAnnotation);

            var safe = Assert.IsType<GenericInstantiationBoundType>(calls[4].Type);
            Assert.Equal(NullableAnnotation.NotAnnotated,
                Assert.IsType<NominalBoundType>(Assert.Single(safe.TypeArguments)).NullableAnnotation);
            var nominal = Assert.IsType<GenericInstantiationBoundType>(calls[5].Type);
            var lookalike = Assert.IsType<NominalBoundType>(Assert.Single(nominal.TypeArguments));
            Assert.Equal(Microsoft.CodeAnalysis.SpecialType.None, lookalike.RoslynSymbol!.SpecialType);
            Assert.Equal("String", lookalike.RoslynSymbol.Name);
            Assert.Equal("Lookalikes", lookalike.RoslynSymbol.ContainingType?.Name);
            Assert.Equal(NullableAnnotation.Annotated, lookalike.NullableAnnotation);

            var mismatch = Assert.Single(diagnostics.Where(BindingDiagnosticPolicy.IsCompilationError));
            Assert.Equal(DiagnosticCode.NullableToNonNullableBinding, mismatch.Code);
            Assert.Equal("reader()", candidate.Substring(mismatch.Span.Start, mismatch.Span.Length));
            AssertBoundary(candidate, "binding", nullable: true);
        }
    }

    public static IEnumerable<object[]> GuardedJoinCases()
    {
        foreach (var coalesce in new[] { false, true })
        foreach (var nullable in new[] { false, true })
        foreach (var boundary in new[] { "binding", "return", "input" })
            yield return [coalesce, nullable, boundary];
    }

    [Theory]
    [MemberData(nameof(GuardedJoinCases))]
    public void GuardedInferredGenericJoin_PreservesPayload(
        bool coalesce, bool nullable, string boundary)
    {
        var expression = coalesce ? "value ?? fallback" : "flag ? value : fallback";
        var source = $$"""
            module GuardedGenericJoin
              using System.Collections.Generic
              func Take public (List<string>:value) → void
                !
              func Probe public (List<{{(nullable ? "string?" : "string")}}>:value, List<string>:fallback, bool:flag) → {{(boundary == "return" ? "List<string>" : "void")}}
                !
                let joined = {{expression}}
                if joined != null
                  {{Sink(boundary, "List<string>", "joined")}}
            {{(boundary == "return" ? "    return fallback" : "")}}
            """;
        AssertBoundary(source, boundary, nullable);
    }

    private static string Sink(string boundary, string type, string value) => boundary switch
    {
        "binding" => $"let {type}:required = {value}",
        "return" => $"return {value}",
        "input" => $"Take({value})",
        _ => throw new ArgumentOutOfRangeException(nameof(boundary))
    };

    private static void AssertBoundary(string source, string boundary, bool nullable)
    {
        Parse(source);

        var expectedCode = boundary switch
        {
            "binding" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            "input" => DiagnosticCode.NullableArgumentToNonNullableParameter,
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        foreach (var typeChecking in new[] { false, true })
        {
            var result = Program.Compile(source, "delegate-return-shape.calr", new CompilationOptions
            {
                EnableTypeChecking = typeChecking,
                EnforceEffects = false,
                StatusWriter = TextWriter.Null
            });
            Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic => diagnostic.Code != expectedCode);
            Assert.True(nullable == result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == expectedCode && BindingDiagnosticPolicy.IsCompilationError(diagnostic)),
                string.Join("\n", result.Diagnostics));
            Assert.Equal(nullable, result.HasErrors);
            Assert.Equal(nullable, string.IsNullOrEmpty(result.GeneratedCode));
        }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, $"{source}{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}");
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, $"{source}{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}");
        return module;
    }
}
