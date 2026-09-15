using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class StageBReceivingSurfaceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, target, shape) in new[]
        {
            ("?[str]", "[str]", BindingReceivingShape.Array),
            ("[?str]", "[str]", BindingReceivingShape.Array),
            ("List<?str>", "List<str>", BindingReceivingShape.Generic),
            ("?Widget", "Widget", BindingReceivingShape.Nominal)
        })
        foreach (var boundary in new[]
        {
            BindingReceivingBoundary.Initializer,
            BindingReceivingBoundary.NativeReturn,
            BindingReceivingBoundary.MethodArgument
        })
            yield return [source, target, shape, boundary];
    }

    internal static string Source(string inputType, string targetType, BindingReceivingBoundary boundary)
    {
        var statement = boundary switch
        {
            BindingReceivingBoundary.Initializer => $"§B{{required:{targetType}}} value",
            BindingReceivingBoundary.NativeReturn => "§R value",
            _ => "§C{Take} §A value §/C"
        };
        return $$"""
            §M{m1:StageBReceiving}
              §CL{c1:Widget:pub}
                §MT{id:Id:pub} () -> i32
                  §E{}
                  §R 1
              §F{take:Take:pub} ({{targetType}}:value) -> void
                §E{}
              §F{probe:Probe:pub} ({{inputType}}:value) -> {{(boundary == BindingReceivingBoundary.NativeReturn ? targetType : "void")}}
                §E{}
                {{statement}}
            """;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ProductionModesAndSharedContextRetainCodeSeverityAndSpan(
        string sourceType, string targetType, BindingReceivingShape shape, BindingReceivingBoundary boundary)
    {
        var source = Source(sourceType, targetType, boundary);
        var safeSource = Source(targetType, targetType, boundary);
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        new Binder(diagnostics).Bind(module);
        var expected = Assert.Single(diagnostics.Where(BindingDiagnosticPolicy.IsCompilationError));
        var code = boundary switch
        {
            BindingReceivingBoundary.Initializer => DiagnosticCode.NullableToNonNullableBinding,
            BindingReceivingBoundary.NativeReturn => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        Assert.Equal(code, expected.Code);
        Assert.Equal(shape, expected.BindingContext?.Shape);
        Assert.Equal("value", source.Substring(expected.Span.Start, expected.Span.Length));

        using var context = new CompilationContext();
        for (var mask = 0; mask < 16; mask++)
        {
            var options = new CompilationOptions
            {
                Context = context,
                EnableTypeChecking = (mask & 1) != 0,
                UnsafeTranspileOnly = (mask & 2) != 0,
                EnforceEffects = (mask & 4) != 0,
                VerifyContracts = (mask & 8) != 0,
                StatusWriter = TextWriter.Null
            };
            var safe = Program.Compile(safeSource, "stage-b-surface.calr", options);
            Assert.False(safe.HasErrors, string.Join("\n", safe.Diagnostics));
            var rejected = Program.Compile(source, "stage-b-surface.calr", options);
            Assert.True(rejected.HasErrors);
            Assert.True(rejected.Diagnostics.Any(d => d.Code == code),
                $"Mode {mask}: {string.Join("\n", rejected.Diagnostics)}");
            var actual = Assert.Single(rejected.Diagnostics.Where(d => d.Code == code));
            Assert.Equal(DiagnosticSeverity.Error, actual.Severity);
            Assert.Equal(expected.Span, actual.Span);
            Assert.True(string.IsNullOrEmpty(rejected.GeneratedCode));
            Assert.False(Program.Compile(safeSource, "stage-b-surface.calr", options).HasErrors);
        }
    }

    [Theory]
    [InlineData("List<List<?str>>", "List<List<str>>")]
    [InlineData("List<i32>", "List<str>")]
    [InlineData("Dictionary<str,?str>", "Dictionary<str,str>")]
    public void UnrelatedTypeMismatchesAreNotDelegatedToNullability(string sourceType, string targetType)
    {
        var source = $$"""
            §M{m1:UnsupportedPayload}
              §F{probe:Probe:pub} ({{sourceType}}:value) -> void
                §B{required:{{targetType}}} value
            """;
        var result = Program.Compile(source, "unsupported-payload.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = false,
            StatusWriter = TextWriter.Null
        });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.TypeMismatch);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }
}
