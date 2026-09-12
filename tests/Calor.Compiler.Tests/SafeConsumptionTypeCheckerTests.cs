using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class SafeConsumptionTypeCheckerTests
{
    private static CompilationResult Check(string source)
        => Program.Compile(source, "safe-consumption.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = false,
            DeferGeneratedOutputValidation = true,
            StatusWriter = TextWriter.Null
        });

    private static void AssertNoErrors(CompilationResult result)
        => Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));

    private static Diagnostic SingleErrorAt(CompilationResult result, int line)
    {
        var diagnostic = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.TypeMismatch, diagnostic.Code);
        Assert.Equal(line, diagnostic.Span.Line);
        return diagnostic;
    }

    [Fact]
    public void CoalesceWithNonNullFallback_HasTheReferentType()
    {
        var accepted = Program.Compile("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> str
                §E{}
                §B{value:str} (?? input "fallback")
                §R value
            """, "safe-consumption.calr");
        AssertNoErrors(accepted);
        Assert.Contains("input ?? \"fallback\"", accepted.GeneratedCode);

        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §B{value:i32} (?? input "fallback")
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void CoalesceWithThrow_HasTheReferentType()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{alloc,throw}
                §B{value:i32} (?? input §TH §NEW{ArgumentNullException})
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void CoalesceWithNullableFallback_StaysNullable()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §B{fallback:?str} null
                §B{value:Option<str>} (?? input fallback)
            """);

        var diagnostic = SingleErrorAt(rejected, 5);
        Assert.Contains("?str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void CoalesceWithNullFallback_StaysNullable()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §B{value:Option<str>} (?? input null)
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("?str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void CoalesceDoesNotUnwrapRuntimeOption()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{alloc}
                §B{opt:Option<str>} §SM "value"
                §B{value:str} (?? opt "fallback")
            """);

        var diagnostic = SingleErrorAt(rejected, 5);
        Assert.Contains("Option<str>", diagnostic.Message);
        Assert.Contains("does not unwrap", diagnostic.Message);
    }

    [Fact]
    public void ConditionalWithOneThrowingArm_HasTheOtherArmType()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (bool:flag) -> void
                §E{throw}
                §B{value:i32} (? flag §TH "missing" "fallback")
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void ConditionalWithBothThrowingArms_HasNeverType()
    {
        var accepted = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (bool:flag) -> void
                §E{throw}
                §B{value:i32} (?? (? flag §TH "left" §TH "right") "fallback")
            """);

        AssertNoErrors(accepted);
    }

    [Fact]
    public void ConditionalValidatesItsCondition()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{value:str} (? INT:1 "yes" "no")
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("condition must be bool", diagnostic.Message);
    }

    [Fact]
    public void ConditionalReportsUnrelatedArmMismatch()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (bool:flag) -> void
                §E{}
                §B{value:str} (? flag INT:1 "fallback")
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("object", diagnostic.Message);
        Assert.Contains("str", diagnostic.Message);
    }

    [Fact]
    public void ThrowExpressionRejectsKnownNonExceptionReference()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (bool:flag, str:message) -> void
                §E{throw}
                §B{value:i32} (? flag §TH message INT:1)
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("requires an exception value", diagnostic.Message);
        Assert.Contains("str", diagnostic.Message);
    }

    [Fact]
    public void IsPatternConditionBindsNonNullVariableOnlyInTrueBranch()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §IF{if1} (is input str text)
                  §B{ok:str} text
                §B{leak:str} text
            """);

        var diagnostic = Assert.Single(rejected.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UndefinedReference, diagnostic.Code);
        Assert.Equal(6, diagnostic.Span.Line);
        Assert.Contains("text", diagnostic.Message);
    }

    [Fact]
    public void IsPatternConditionalArmBindsNonNullVariableOnlyInTrueArm()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §B{value:i32} (? (is input str text) text "fallback")
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void NegatedIsPatternDoesNotBindInTrueBranch()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §IF{if1} (! (is input str text))
                  §B{value:str} text
            """);

        var diagnostic = Assert.Single(rejected.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UndefinedReference, diagnostic.Code);
        Assert.Equal(5, diagnostic.Span.Line);
        Assert.Contains("text", diagnostic.Message);
    }

    [Fact]
    public void MatchExpressionTypePatternBindingHasTheTestedNonNullType()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §B{value:i32} §W{sw1:expr} input
                  §K §PTYPE{str:text} → text
                  §K _ → "fallback"
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void NegatedMatchTypePatternDoesNotBind()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §B{value:str} §W{sw1:expr} input
                  §K (not §PTYPE{str:text}) → text
                  §K _ → "fallback"
            """);

        var diagnostic = Assert.Single(rejected.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UndefinedReference, diagnostic.Code);
        Assert.Equal(5, diagnostic.Span.Line);
        Assert.Contains("text", diagnostic.Message);
    }

    [Fact]
    public void IsPatternRejectsKnownImpossibleInputType()
    {
        var rejected = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{value:bool} (is INT:1 str text)
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("not compatible", diagnostic.Message);
        Assert.Contains("str", diagnostic.Message);
        Assert.Contains("i32", diagnostic.Message);
    }

    [Theory]
    [InlineData("(?? INT:1 §TH \"missing\")")]
    [InlineData("(?? BOOL:true false)")]
    [InlineData("(?? INT:1 §C{System.Guid.NewGuid} §/C)")]
    [InlineData("(? true §TH §C{MakeString} §/C \"fallback\")")]
    [InlineData("(? true §TH §NEW{i32} \"fallback\")")]
    public void KnownInvalidOperands_AreRejectedBeforeGeneratedValidation(string expression)
    {
        var source = $$"""
            §M{m1:SafeConsumption}
              §F{f1:MakeString:pub} () -> str
                §E{}
                §R "not an exception"
              §F{f2:Probe:pub} () -> object
                §E{alloc,throw}
                §R {{expression}}
            """;
        foreach (var result in new[] { Check(source), Program.Compile(source, "invalid-consumption.calr") })
        {
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.TypeMismatch && diagnostic.Span.Line == 7);
        }
    }
}
