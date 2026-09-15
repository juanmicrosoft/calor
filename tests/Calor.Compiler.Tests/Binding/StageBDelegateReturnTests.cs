using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
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
        var targetType = shape == "generic" ? "List<str>" : "[str]";
        var sourceType = !nullable ? targetType : shape switch
        {
            "generic" => "List<?str>",
            "array-element" => "[?str]",
            "array-container" => "?[str]",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var call = $"§C{{reader{(invoke ? ".Invoke" : "")}}} §/C";
        var value = inferred ? "produced" : call;
        var source = $$"""
            §M{m:DelegateReturns}
              §U{System.Collections.Generic}
              §DEL{readerType:Reader:pub}
                §O{ {{sourceType}} }
              §F{take:Take:pub} ({{targetType}}:value) -> void
                §E{}
              §F{probe:Probe:pub} (Reader:reader) -> {{(boundary == "return" ? targetType : "void")}}
            {{(inferred ? $"    §B{{produced}} {call}" : "")}}
                {{Sink(boundary, targetType, value)}}
            """;
        AssertBoundary(source, boundary, nullable);
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
        var expression = coalesce ? "(?? value fallback)" : "(? flag value fallback)";
        var source = $$"""
            §M{m:GuardedGenericJoin}
              §U{System.Collections.Generic}
              §F{take:Take:pub} (List<str>:value) -> void
                §E{}
              §F{probe:Probe:pub} (List<{{(nullable ? "?str" : "str")}}>:value, List<str>:fallback, bool:flag) -> {{(boundary == "return" ? "List<str>" : "void")}}
                §B{joined} {{expression}}
                §IF{guard} (!= joined null)
                  {{Sink(boundary, "List<str>", "joined")}}
            {{(boundary == "return" ? "    §R fallback" : "")}}
            """;
        AssertBoundary(source, boundary, nullable);
    }

    private static string Sink(string boundary, string type, string value) => boundary switch
    {
        "binding" => $"§B{{required:{type}}} {value}",
        "return" => $"§R {value}",
        "input" => $"§C{{Take}} §A {value} §/C",
        _ => throw new ArgumentOutOfRangeException(nameof(boundary))
    };

    private static void AssertBoundary(string source, string boundary, bool nullable)
    {
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
}
