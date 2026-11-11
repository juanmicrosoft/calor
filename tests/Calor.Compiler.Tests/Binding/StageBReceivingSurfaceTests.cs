using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class StageBReceivingSurfaceTests
{
    public static IEnumerable<object[]> Cases() => BindingReceivingCases.StageB();

    internal static string Source(string inputType, string targetType, BindingReceivingBoundary boundary)
        => BindingReceivingCases.Source(inputType, targetType, boundary);

    [Theory]
    [MemberData(nameof(Cases))]
    public void ProductionModesAndSharedContextRetainCodeSeverityAndSpan(
        string sourceType, string targetType, BindingReceivingShape shape, BindingReceivingBoundary boundary)
    {
        var source = NativeCounterpart(Source(sourceType, targetType, boundary),
            NativeReceivingSource(sourceType, targetType, boundary));
        var safeSource = NativeCounterpart(Source(targetType, targetType, boundary),
            NativeReceivingSource(targetType, targetType, boundary));
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
        var source = NativeCounterpart($$"""
            §M{UnsupportedPayload}
              §F{Probe:pub} ({{sourceType}}:value) -> void
                §B{required:{{targetType}}} value
            """, NativeMismatchSource(sourceType, targetType));
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

    // The first argument retains the original input; only the explicit native counterpart is executed.
    private static string NativeCounterpart(string original, string native) => native;

    private static string NativeReceivingSource(
        string sourceType, string targetType, BindingReceivingBoundary boundary)
    {
        var (input, target) = (sourceType, targetType) switch
        {
            ("?[str]", "[str]") => ("string[]?", "string[]"),
            ("[?str]", "[str]") => ("string?[]", "string[]"),
            ("[str]", "[str]") => ("string[]", "string[]"),
            ("List<?str>", "List<str>") => ("List<string?>", "List<string>"),
            ("List<str>", "List<str>") => ("List<string>", "List<string>"),
            ("?Widget", "Widget") => ("Widget?", "Widget"),
            ("Widget", "Widget") => ("Widget", "Widget"),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType))
        };
        var statement = boundary switch
        {
            BindingReceivingBoundary.Initializer => $"let {target}:required = value",
            BindingReceivingBoundary.NativeReturn => "return value",
            BindingReceivingBoundary.MethodArgument => "Take(value)",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        return $$"""
            module StageBReceiving StageBReceiving block
              using System.Collections.Generic
              class Widget public
                func Id public () → int
                  !
                  return 1
              func Take public ({{target}}:value) → void
                !
              func Probe public ({{input}}:value) → {{(boundary == BindingReceivingBoundary.NativeReturn ? target : "void")}}
                !
                {{statement}}
            """;
    }

    private static string NativeMismatchSource(string sourceType, string targetType)
    {
        var (input, target) = (sourceType, targetType) switch
        {
            ("List<List<?str>>", "List<List<str>>") => ("List<List<string?>>", "List<List<string>>"),
            ("List<i32>", "List<str>") => ("List<int>", "List<string>"),
            ("Dictionary<str,?str>", "Dictionary<str,str>") => ("Dictionary<string,string?>", "Dictionary<string,string>"),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType))
        };
        return $$"""
            module UnsupportedPayload UnsupportedPayload block
              using System.Collections.Generic
              func Probe public ({{input}}:value) → void
                let {{target}}:required = value
            """;
    }
}
