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
    [InlineData("§IF{if1} (is input Widget text)\n      §R true")]
    [InlineData("§IF{if1} false\n      §R true\n    §EI (is input Widget text)\n      §R true")]
    [InlineData("§R (? (is input Widget text) true false)")]
    [InlineData("§IF{if1} (&& (is input Widget text) true)\n      §R true")]
    [InlineData("§WH{wh1} (is input Widget text)\n      §R true")]
    [InlineData("§W{w1} input\n      §K _ §WHEN (is input Widget text)\n        §R true")]
    [InlineData("§R §W{w1:expr} input\n      §K _ §WHEN (is input Widget text) → true\n      §K _ → false")]
    public void UnknownPatternType_IsDiagnosedOnceWhenItsBindingIsTransferred(string body)
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?object:input) -> bool
                §E{}
            """ + "\n    " + body + "\n    §R false");

        AssertNoErrors(result);
        var warning = Assert.Single(result.Diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.UndefinedReference));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("Widget", warning.Message);
    }

    [Fact]
    public void UnknownPatternType_DiagnosesEachDistinctSourceOccurrence()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?object:input) -> bool
                §E{}
                §IF{if1} (is input Widget first)
                  §R true
                §EI (is input Widget second)
                  §R true
                §R false
            """);

        AssertNoErrors(result);
        var warnings = result.Diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.UndefinedReference).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, warning => Assert.Equal(DiagnosticSeverity.Warning, warning.Severity));
        Assert.Equal(new[] { 4, 6 }, warnings.Select(warning => warning.Span.Line));
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

    [Theory]
    [InlineData("Take", "(?? 1 2)")]
    [InlineData("Take", "(? 1 2 3)")]
    [InlineData("Take", "(? true §TH §C{MakeString} §/C 1)")]
    [InlineData("Overloaded", "(?? 1 2)")]
    [InlineData("Generic", "(?? 1 2)")]
    [InlineData("Missing", "(?? 1 2)")]
    [InlineData("Shadowed", "(?? 1 2)")]
    [InlineData("Take", "§C{Missing} §A (?? 1 2) §/C")]
    public void CallExpressions_ValidateChildrenWithoutAssumingASelectedReturnType(string target, string argument)
    {
        var source = $$"""
            §M{m1:SafeConsumption}
              §F{f1:Take:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f2:MakeString:pub} () -> str
                §E{}
                §R "not an exception"
              §F{f3:Overloaded:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f4:Overloaded:pub} (str:value) -> str
                §E{}
                §R value
              §F{f5:Generic:pub}<T> (T:value) -> T
                §E{}
                §R value
              §F{f6:Probe:pub} {{(target == "Shadowed" ? "(i32:Shadowed)" : "()")}} -> i32
                §E{throw}
                §R §C{{{target}}} §A {{argument}} §/C
            """;
        var result = Check(source);
        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics.Errors.Where(d =>
            d.Code == DiagnosticCode.TypeMismatch));
        Assert.Equal(19, diagnostic.Span.Line);
    }

    [Theory]
    [InlineData("§R §C{Take} §A (?? 1 2) §/C")]
    [InlineData("§C{Take} §A (?? 1 2) §/C\n    §R 0")]
    [InlineData("§B{result:i32} (?? 1 2)\n    §R result")]
    public void InvalidCoalesce_HasTheSameOwnedDiagnosticAcrossConsumers(string body)
    {
        var source = $$"""
            §M{m:M}
              §F{take:Take:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{probe:Probe:pub} () -> i32
                §E{}
                {{body}}
            """;
        foreach (var result in new[] { Check(source), Program.Compile(source, "invalid-consumer.calr") })
        {
            var error = SingleErrorAt(result, 7);
            Assert.Contains("Null-coalescing requires", error.Message);
        }
    }

    [Fact]
    public void CallArgumentPattern_DoesNotLeakIntoTheEnclosingScope()
    {
        var result = Check("""
            §M{m:M}
              §F{take:Take:pub} (bool:value) -> bool
                §E{}
                §R value
              §F{probe:Probe:pub} (?str:input) -> str
                §E{}
                §B{result} §C{Take} §A (is input str text) §/C
                §R text
            """);
        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UndefinedReference, error.Code);
        Assert.Equal(8, error.Span.Line);
        Assert.Contains("text", error.Message);
    }
}
