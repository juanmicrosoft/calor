using Calor.Compiler.Diagnostics;
using Calor.Compiler.TypeChecking;
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

    [Fact]
    public void CallExpressions_RecognizeFunctionArguments()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Double:pub} (i32:value) -> i32
                §E{}
                §R (* value 2)
              §F{f2:Map:pub} (Func<i32,i32>:transform §E{}) -> i32
                §E{}
                §R §C{transform} §A 1 §/C
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R §C{Map} §A Double §/C
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void ExpressionCalls_ValidateNestedArguments()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Double:pub} (i32:value) -> i32
                §E{}
                §R (* value 2)
              §F{f2:GetTransform:pub} () -> Func<i32,i32>
                §E{}
                §R Double
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R §C §C{GetTransform} §/C §A (?? INT:1 INT:2) §/C
            """);

        var diagnostic = SingleErrorAt(result, 10);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void ResolvedOverloadReturnType_IsCheckedByOuterConsumer()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f2:Pick:pub} (str:value) -> str
                §E{}
                §R value
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Pick} §A 1 §/C 2)
            """);

        var diagnostic = SingleErrorAt(result, 10);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void NullableValueBindings_AcceptValuesAndConditionalNull()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (bool:flag) -> void
                §E{}
                §B{direct:?i32} INT:1
                §B{conditional:?i32} (? flag null INT:1)
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void MatchExpressions_UnifyThrowingAndNullArms()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (i32:value) -> void
                §E{throw}
                §B{text:str} §W{m1:expr} value
                  §K 0 → "ok"
                  §K _ → §TH "bad"
                §B{nullable:?str} §W{m2:expr} value
                  §K 0 → "ok"
                  §K _ → null
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void FunctionReferences_AreMatchedAgainstDelegateTargetsAndOverloads()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (str:value) -> str
                §E{}
                §R value
              §F{f2:Pick:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f3:Probe:pub} () -> void
                §E{}
                §B{picker:Func<i32,i32>} Pick
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void NamedArguments_SelectTheMappedOverloadReturnType()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (str:value, i32:number) -> str
                §E{}
                §R value
              §F{f2:Pick:pub} (i32:value, str:number) -> i32
                §E{}
                §R value
              §F{f3:Probe:pub} () -> str
                §E{}
                §R (?? §C{Pick} §A[number] 7 §A[value] "ok" §/C "fallback")
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void GenericCallReturnType_IsCheckedByOuterConsumer()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Identity:pub}<T> (T:value) -> T
                §E{}
                §R value
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Identity<i32>} §A 1 §/C 2)
            """);

        var diagnostic = SingleErrorAt(result, 7);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void DelegateCalls_ValidateArgumentsAndExposeReturnType()
    {
        var wrongArgument = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (Func<i32,i32>:transform) -> i32
                §E{}
                §R §C{transform} §A "wrong" §/C
            """);
        Assert.True(wrongArgument.HasErrors);

        var invalidConsumer = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (Func<i32,i32>:transform) -> i32
                §E{}
                §R (?? §C{transform} §A 1 §/C 2)
            """);
        var diagnostic = SingleErrorAt(invalidConsumer, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void GenericAndVariantMethodGroups_ConvertToDelegateTargets()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Identity:pub}<T> (T:value) -> T
                §E{}
                §R value
              §F{f2:GetText:pub} () -> str
                §E{}
                §R "text"
              §F{f3:Probe:pub} () -> void
                §E{}
                §B{identity:Func<i32,i32>} Identity
                §B{textFactory:Func<object>} GetText
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void OverloadedMethodGroups_UseCallArgumentDelegateContext()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (str:value) -> str
                §E{}
                §R value
              §F{f2:Pick:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f3:Map:pub} (Func<i32,i32>:transform) -> i32
                §E{}
                §R §C{transform} §A 1 §/C
              §F{f4:Probe:pub} () -> i32
                §E{}
                §R §C{Map} §A Pick §/C
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void UserDeclaredDelegate_ProvidesMethodGroupContext()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §DEL{d1:Picker:pub}
                §I{i32:value}
                §O{i32}
              §F{f1:Pick:pub} (str:value) -> str
                §E{}
                §R value
              §F{f2:Pick:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f3:Probe:pub} () -> void
                §E{}
                §B{picker:Picker} Pick
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void NullableValueCommonType_IsIndependentOfArmOrder()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (bool:flag, ?i32:maybe) -> void
                §E{}
                §B{value:?f64} (? flag maybe FLOAT:1.5)
                §B{reverse:?f64} (? flag FLOAT:1.5 maybe)
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void MatchExpression_RejectsUnrelatedInferredArmTypes()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (i32:value) -> void
                §E{}
                §B{mixed} §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
            """);

        var diagnostic = SingleErrorAt(result, 4);
        Assert.Contains("incompatible types", diagnostic.Message);
    }

    [Fact]
    public void UnmodeledStructuralWrappers_StillValidateNestedExpressions()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> str
                §E{}
                §R §INTERP "value=" §EXP (?? 1 2) §/INTERP
            """);

        var diagnostic = SingleErrorAt(result, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void InlineGenericFunctionNames_AreCallableMethodGroups()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Identity<T>:pub} (T:value) -> T
                §E{}
                §R value
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{identity:Func<i32,i32>} Identity
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void GenericInference_MergesAllArgumentBounds()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Choose:pub}<T> (T:first, T:second) -> T
                §E{}
                §R second
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Choose} §A (cast char INT:65) §A 1 §/C 2)
            """);

        var diagnostic = SingleErrorAt(result, 7);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void NamedUserDelegateCalls_MapArgumentsByName()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §DEL{d1:Picker:pub}
                §I{i32:number}
                §I{str:text}
                §O{str}
              §F{f1:Probe:pub} (Picker:pick) -> str
                §E{}
                §R §C{pick} §A[text] "x" §A[number] 1 §/C
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void ExpandedParamsCalls_ExposeTheirReturnType()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:First:pub} (i32[]:values:params) -> i32
                §E{}
                §R §IDX values 0
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R (?? §C{First} §A 1 §A 2 §/C 3)
            """);

        var diagnostic = SingleErrorAt(result, 7);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void DuplicateNamedParameters_DoNotCrashTypeChecking()
    {
        var exception = Record.Exception(() => Check("""
            §M{m1:SafeConsumption}
              §F{f1:Take:pub} (i32:value, i32:value) -> i32
                §E{}
                §R value
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R §C{Take} §A[value] 1 §/C
            """));

        Assert.Null(exception);
    }

    [Fact]
    public void NullableCoalesce_UsesTheWiderUnderlyingType()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (?i32:left, ?f64:right) -> ?f64
                §E{}
                §R (?? left right)
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void GenericExpandedParams_InferFromEveryElement()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:First:pub}<T> (T[]:values:params) -> T
                §E{}
                §R §IDX values 0
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R (?? §C{First} §A 1 §A 2 §/C 3)
            """);

        var diagnostic = SingleErrorAt(result, 7);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void GenericCalls_InferFromMethodGroupArguments()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:GetInt:pub} () -> i32
                §E{}
                §R 1
              §F{f2:Make:pub}<T> (Func<T>:factory) -> T
                §E{}
                §R §C{factory} §/C
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Make} §A GetInt §/C 2)
            """);

        var diagnostic = SingleErrorAt(result, 10);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void AnonymousObjectHolders_ExposeNestedExpressions()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> object
                §E{}
                §R §ANON Age = (?? 1 2) §/ANON
            """);

        var diagnostic = SingleErrorAt(result, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void MatchExpression_UsesExplicitObjectTarget()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (i32:value) -> void
                §E{}
                §B{mixed:object} §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void PublicFunctionTypeAndEnvironmentApis_RetainOriginalOverloads()
    {
        Assert.NotNull(typeof(FunctionType).GetConstructor(
            [typeof(IReadOnlyList<CalorType>), typeof(CalorType)]));
        Assert.NotNull(typeof(TypeEnvironment).GetMethod(
            nameof(TypeEnvironment.DefineFunction),
            [typeof(string), typeof(FunctionType)]));
    }

    [Fact]
    public void MatchExpression_UsesReturnAndCallParameterTargets()
    {
        var returnResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (i32:value) -> object
                §E{}
                §R §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
            """);
        AssertNoErrors(returnResult);

        var argumentResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Take:pub} (object:value) -> object
                §E{}
                §R value
              §F{f2:Probe:pub} (i32:value) -> object
                §E{}
                §R §C{Take} §A §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
                §/C
            """);
        AssertNoErrors(argumentResult);
    }

    [Fact]
    public void OverloadInference_PrefersIdentityConversion()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f2:Pick:pub} (f64:value) -> str
                §E{}
                §R "wide"
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Pick} §A 1 §/C 2)
            """);

        var diagnostic = SingleErrorAt(result, 10);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);

        var categoryResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (object:value) -> str
                §E{}
                §R "boxed"
              §F{f2:Pick:pub} (f64:value) -> i32
                §E{}
                §R 1
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Pick} §A 1 §/C 2)
            """);
        var categoryDiagnostic = SingleErrorAt(categoryResult, 10);
        Assert.Contains("Null-coalescing requires", categoryDiagnostic.Message);

        var namedResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (i32:x, object:y) -> i32
                §E{}
                §R x
              §F{f2:Pick:pub} (object:y, f64:x) -> str
                §E{}
                §R "wide"
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Pick} §A[x] 1 §A[y] "s" §/C 2)
            """);
        var namedDiagnostic = SingleErrorAt(namedResult, 10);
        Assert.Contains("Null-coalescing requires", namedDiagnostic.Message);

        var paramsResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f2:Pick:pub} (i32[]:values:params) -> str
                §E{}
                §R "expanded"
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Pick} §A 1 §/C 2)
            """);
        var paramsDiagnostic = SingleErrorAt(paramsResult, 10);
        Assert.Contains("Null-coalescing requires", paramsDiagnostic.Message);

        var charResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f2:Pick:pub} (f64:value) -> str
                §E{}
                §R "wide"
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Pick} §A (cast char INT:65) §/C 2)
            """);
        var charDiagnostic = SingleErrorAt(charResult, 10);
        Assert.Contains("Null-coalescing requires", charDiagnostic.Message);

        var optionalResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f2:Pick:pub} (i32:value, i32:other = 0) -> str
                §E{}
                §R "optional"
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Pick} §A 1 §/C 2)
            """);
        var optionalDiagnostic = SingleErrorAt(optionalResult, 10);
        Assert.Contains("Null-coalescing requires", optionalDiagnostic.Message);
    }

    [Fact]
    public void MethodGroupConversion_RejectsBoxingReturnsAndModifierMismatches()
    {
        var boxingResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:GetInt:pub} () -> i32
                §E{}
                §R 1
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{factory:Func<object>} GetInt
            """);
        Assert.Contains(boxingResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);

        var modifierResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Bump:pub} (i32:value:ref) -> i32
                §E{}
                §R value
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{f:Func<i32,i32>} Bump
            """);
        Assert.Contains(modifierResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void DelegateCalls_ReportShapeErrorsAndPreserveNamedResults()
    {
        var arityResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (Func<i32,i32>:transform) -> i32
                §E{}
                §R §C{transform} §A 1 §A 2 §/C
            """);
        Assert.Contains(arityResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);

        var namedResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (Func<i32,i32>:transform) -> i32
                §E{}
                §R (?? §C{transform} §A[arg] 1 §/C 2)
            """);
        var diagnostic = SingleErrorAt(namedResult, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void GenericMethodGroupInference_IsIndependentOfDeclarationOrder()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Get:pub} (object:value) -> str
                §E{}
                §R "wide"
              §F{f2:Get:pub} (str:value) -> i32
                §E{}
                §R 1
              §F{f3:Choose:pub}<T> (Func<str,T>:factory, T:fallback) -> T
                §E{}
                §R fallback
              §F{f4:Probe:pub} () -> i32
                §E{}
                §R (?? §C{Choose} §A Get §A 1 §/C 2)
            """);

        var diagnostic = SingleErrorAt(result, 13);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void StatementLambda_UsesItsDelegateReturnTarget()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §B{factory:Func<object>} §LAM{l1}
                  §R §W{m:expr} value
                    §K 0 → 1
                    §K _ → "text"
                §/LAM{l1}
                §R 0
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void DelegateCalls_ContextuallyTypeMatchArguments()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (Func<object,object>:transform, i32:value) -> object
                §E{}
                §R §C{transform} §A §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
                §/C
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void GenericLambdaContext_UsesOrdinaryArgumentInferenceFirst()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Choose:pub}<T> (Func<T>:factory, T:fallback) -> T
                §E{}
                §R fallback
              §F{f2:Probe:pub} (i32:value, object:fallback) -> object
                §E{}
                §R §C{Choose} §A §LAM{l1}
                  §R §W{m:expr} value
                    §K 0 → 1
                    §K _ → "text"
                §/LAM{l1} §A fallback §/C
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void InapplicableOverload_DoesNotEraseCallArgumentTarget()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Take:pub} (object:value) -> object
                §E{}
                §R value
              §F{f2:Take:pub} (i32:value, i32:other) -> object
                §E{}
                §R value
              §F{f3:Probe:pub} (i32:value) -> object
                §E{}
                §R §C{Take} §A §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
                §/C
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void NonContextualArguments_NarrowTheContextualCandidateSet()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Take:pub} (object:value, i32:tag) -> object
                §E{}
                §R value
              §F{f2:Take:pub} (str:value, str:tag) -> object
                §E{}
                §R value
              §F{f3:Probe:pub} (i32:value) -> object
                §E{}
                §R §C{Take} §A §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
                §A 1 §/C
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void ContextualLambda_ValidatesItsDeclaredSignatureAndReturnType()
    {
        var returnResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{factory:Func<i32>} §LAM{l1} "wrong" §/LAM{l1}
            """);
        Assert.Contains(returnResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);

        var parameterResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{map:Func<i32,i32>} §LAM{l1:value:str} 1 §/LAM{l1}
            """);
        Assert.Contains(parameterResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void CallStatements_UseSignatureAwareDelegateValidation()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (Action<i32>:consume) -> void
                §E{}
                §C{consume} §/C
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void DelegateInvocationAndMethodGroups_RespectByRefAndEnumValueSemantics()
    {
        var modifierResult = Check("""
            §M{m1:SafeConsumption}
              §DEL{d1:Mutator:pub}
                §I{i32:value:ref}
                §O{i32}
              §F{f1:Probe:pub} (Mutator:mutate, i32:value) -> i32
                §E{}
                §R §C{mutate} §A value §/C
            """);
        Assert.Contains(modifierResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);

        var enumResult = Check("""
            §M{m1:SafeConsumption}
              §EN{e1:Color}
              Red
              §/EN{e1}
              §F{f1:GetColor:pub} () -> Color
                §E{}
                §R Color.Red
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{factory:Func<object>} GetColor
            """);
        Assert.Contains(enumResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);

        var identityResult = Check("""
            §M{m1:SafeConsumption}
              §DEL{d1:Mutator:pub}
                §I{f64:value:ref}
                §O{void}
              §F{f1:Probe:pub} (Mutator:mutate, i32:value:ref) -> void
                §E{}
                §C{mutate} §A{ref} value §/C
            """);
        Assert.Contains(identityResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void UncontextualizedLambda_StillTraversesNestedConsumers()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §C{System.Threading.Tasks.Task.Run} §A §LAM{l1} (?? 1 2) §/LAM{l1} §/C
            """);

        var diagnostic = SingleErrorAt(result, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void ContextualInference_HandlesParamsAndVariableGenericBounds()
    {
        var paramsResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Take:pub} (object[]:values:params) -> object
                §E{}
                §R values
              §F{f2:Probe:pub} (i32:value) -> object
                §E{}
                §R §C{Take} §A §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
                §/C
            """);
        AssertNoErrors(paramsResult);

        var variableResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Choose:pub}<T> (T:first, T:second) -> T
                §E{}
                §R first
              §F{f2:Probe:pub} (i32:value, object:fallback) -> object
                §E{}
                §R §C{Choose} §A fallback §A §W{m:expr} value
                  §K 0 → 1
                  §K _ → "text"
                §/C
            """);
        AssertNoErrors(variableResult);
    }

    [Fact]
    public void OverloadedMethodGroupInference_UsesTheBestMember()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Value:pub} (object:value) -> i32
                §E{}
                §R 1
              §F{f2:Value:pub} (str:value) -> str
                §E{}
                §R value
              §F{f3:Use:pub}<T> (Func<str,T>:factory) -> T
                §E{}
                §R §C{factory} §A "x" §/C
              §F{f4:Probe:pub} () -> i32
                §E{}
                §R §C{Use} §A Value §/C
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void LosingLambdaOverloads_DoNotLeakDiagnostics()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (Func<i32,i32>:map) -> i32
                §E{}
                §R §C{map} §A 1 §/C
              §F{f2:Pick:pub} (Func<str,str>:map) -> str
                §E{}
                §R §C{map} §A "x" §/C
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R §C{Pick} §A §LAM{l1:x:i32} x §/LAM{l1} §/C
            """);
        AssertNoErrors(result);

        var actionResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Identity:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{action:Action<i32>} §LAM{l1:x:i32}
                  §C{Identity} §A x §/C
                §/LAM{l1}
            """);
        AssertNoErrors(actionResult);
    }

    [Fact]
    public void NestedMatchExpressions_InheritTheOuterTarget()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (i32:value) -> object
                §E{}
                §R §W{outer:expr} value
                  §K 0 → §W{inner:expr} value
                    §K 0 → 1
                    §K _ → "text"
                  §K _ → "fallback"
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void UncontextualizedMatch_StillTraversesNestedConsumers()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (i32:key) -> void
                §E{}
                §C{System.Console.WriteLine} §A §W{m:expr} key
                  §K 0 → (?? 1 2)
                  §K _ → "text"
                §/C
            """);

        var diagnostic = SingleErrorAt(result, 5);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void ContextualCandidateProbes_DiscardNestedDiagnostics()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Take:pub} (object:value) -> object
                §E{}
                §R value
              §F{f2:Probe:pub} (i32:key) -> object
                §E{}
                §R §C{Take} §A §W{m:expr} key
                  §K 0 → (?? 1 2)
                  §K _ → "text"
                §/C
            """);

        var diagnostic = SingleErrorAt(result, 8);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);

        var divergentResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (Func<i32,i32>:map) -> i32
                §E{}
                §R §C{map} §A 1 §/C
              §F{f2:Pick:pub} (Func<str,str>:map) -> str
                §E{}
                §R §C{map} §A "x" §/C
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R §C{Pick} §A §LAM{l1:x:i32} (?? x 2) §/LAM{l1} §/C
            """);
        Assert.Single(divergentResult.Diagnostics.Errors.Where(
            error => error.Message.Contains("Null-coalescing requires", StringComparison.Ordinal)));
    }

    [Fact]
    public void GenericMethodGroupCosts_UseSubstitutedSignatures()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Map:pub}<T> (T:value) -> T
                §E{}
                §R value
              §F{f2:Map:pub} (object:value) -> object
                §E{}
                §R value
              §F{f3:Use:pub} (Func<str,str>:map) -> i32
                §E{}
                §R 1
              §F{f4:Use:pub} (Func<object,object>:map) -> str
                §E{}
                §R "wide"
              §F{f5:Probe:pub} () -> str
                §E{}
                §R (?? §C{Use} §A Map §/C "fallback")
            """);

        var diagnostic = SingleErrorAt(result, 16);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void DirectCalls_RejectByRefWideningAndInvalidNamedOrdering()
    {
        var refResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Mutate:pub} (f64:value:ref) -> void
                §E{}
              §F{f2:Probe:pub} (i32:value:ref) -> void
                §E{}
                §C{Mutate} §A{ref} value §/C
            """);
        Assert.Contains(refResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);

        var namedResult = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Take:pub} (i32:left, i32:right) -> i32
                §E{}
                §R left
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R §C{Take} §A[right] 2 §A 1 §/C
            """);
        Assert.Contains(namedResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void FailedContextualOverloads_ReportNoMatchingOverload()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (Func<i32,i32>:map) -> i32
                §E{}
                §R §C{map} §A 1 §/C
              §F{f2:Pick:pub} (Func<str,i32>:map) -> i32
                §E{}
                §R 1
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R §C{Pick} §A §LAM{l1:x:i32} "wrong" §/LAM{l1} §/C
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void GenericCalls_InferTypeArgumentsFromLambdaResults()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Make:pub}<T> (Func<T>:factory) -> T
                §E{}
                §R §C{factory} §/C
              §F{f2:Probe:pub} () -> str
                §E{}
                §R §C{Make} §A §LAM{l1} 1 §/LAM{l1} §/C
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_ForFuncRequiresAReturnValue()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Identity:pub} (i32:value) -> i32
                §E{}
                §R value
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{factory:Func<i32>} §LAM{l1}
                  §C{Identity} §A 1 §/C
                §/LAM{l1}
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_ForFuncRequiresAllPathsToReturn()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{factory:Func<bool,i32>} §LAM{l1:flag:bool}
                  §IF{if1} flag
                    §R 1
                §/LAM{l1}
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void GenericCalls_InferLambdaResultsInsideParameterScope()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Apply:pub}<T,U> (T:value, Func<T,U>:map) -> U
                §E{}
                §R §C{map} §A value §/C
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R §C{Apply} §A 1 §A §LAM{l1:x} x §/LAM{l1} §/C
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void GenericCalls_InferLambdaResultsFromLocalBindings()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Make:pub}<T> (Func<T>:factory) -> T
                §E{}
                §R §C{factory} §/C
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R §C{Make} §A §LAM{l1}
                  §B{x:i32} 1
                  §R x
                §/LAM{l1} §/C
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void GenericCalls_ExcludeNestedLambdaReturnsFromOuterInference()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Make:pub}<T> (Func<T>:factory) -> T
                §E{}
                §R §C{factory} §/C
              §F{f2:Probe:pub} () -> i32
                §E{}
                §R §C{Make} §A §LAM{l1}
                  §B{nested:Func<str>} §LAM{l2}
                    §R "nested"
                  §/LAM{l2}
                  §R 1
                §/LAM{l1} §/C
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ExhaustiveMatchCanDefinitelyReturn()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{factory:Func<i32,i32>} §LAM{l1:value:i32}
                  §W{m1} value
                    §K 0
                      §R 1
                    §K _
                      §R 2
                §/LAM{l1}
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ReturningTryWithFinallyCanDefinitelyReturn()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{cw}
                §B{factory:Func<i32>} §LAM{l1}
                  §TR{t1}
                    §R 1
                  §FI
                    §P "cleanup"
                §/LAM{l1}
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_UnconditionalLoopCanDefinitelyNotReturn()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{cw}
                §B{factory:Func<i32>} §LAM{l1}
                  §WH{w1} true
                    §P "loop"
                §/LAM{l1}
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_UnconditionalDoWhileCanDefinitelyNotReturn()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{cw}
                §B{factory:Func<i32>} §LAM{l1}
                  §DO{d1}
                    §P "loop"
                  §/DO{d1} true
                §/LAM{l1}
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_UnconditionalLoopWithNestedBreakMustReturn()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{factory:Func<i32>} §LAM{l1}
                  §WH{w1} true
                    §UNSAFE{u1}
                      §BK
                §/LAM{l1}
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_UnconditionalLoopWithGotoMustReturn()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{cw}
                §B{factory:Func<i32>} §LAM{l1}
                  §WH{w1} true
                    §GOTO{done}
                  §LABEL{done}
                  §P "done"
                §/LAM{l1}
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_UnconditionalLoopWithInternalGotoDoesNotReturn()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{factory:Func<i32>} §LAM{l1}
                  §WH{w1} true
                    §LABEL{again}
                    §GOTO{again}
                §/LAM{l1}
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void MethodGroupParameters_SupportReferenceDelegateVariance()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Use:pub} (Func<object>:factory) -> void
                §E{}
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{action:Action<Func<str>>} Use
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void MethodGroupParameters_SupportNestedDelegateReturnVariance()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Use:pub} (Func<Func<object>>:factory) -> void
                §E{}
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{action:Action<Func<Func<str>>>} Use
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void MethodGroupParameters_RejectWideningAndBoxing()
    {
        var widening = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Widen:pub} (f64:value) -> void
                §E{}
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{action:Action<i32>} Widen
            """);
        Assert.Contains(widening.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);

        var boxing = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Box:pub} (object:value) -> void
                §E{}
              §F{f2:Probe:pub} () -> void
                §E{}
                §B{action:Action<i32>} Box
            """);
        Assert.Contains(boxing.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void MethodGroupRanking_RejectsParetoIncomparableCandidates()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (str:left, object:right) -> i32
                §E{}
                §R left.Length
              §F{f2:Pick:pub} (object:left, str:right) -> i32
                §E{}
                §R right.Length
              §F{f3:Take:pub} (Func<str,str,i32>:picker) -> void
                §E{}
              §F{f4:Probe:pub} () -> void
                §E{}
                §C{Take} §A Pick §/C
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void DelegateCalls_RejectPositionalArgumentsAfterOutOfPositionNamedArguments()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Probe:pub} (Func<i32,i32,i32>:combine) -> i32
                §E{}
                §R §C{combine} §A[arg2] 2 §A 1 §/C
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void MethodGroupRanking_DoesNotUseReturnTypeIdentity()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Value:pub} (f64:value) -> object
                §E{}
                §R value
              §F{f2:Value:pub} (decimal:value) -> str
                §E{}
                §R "value"
              §F{f3:Probe:pub} () -> void
                §E{}
                §B{factory:Func<char,object>} Value
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void ImplicitLambda_UsesAnonymousFunctionBetterConversion()
    {
        var result = Check("""
            §M{m1:SafeConsumption}
              §F{f1:Pick:pub} (Func<object,i32>:map) -> str
                §E{}
                §R "object"
              §F{f2:Pick:pub} (Func<str,i32>:map) -> i32
                §E{}
                §R 1
              §F{f3:Probe:pub} () -> i32
                §E{}
                §R §C{Pick} §A §LAM{l1:x} 1 §/LAM{l1} §/C
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
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
