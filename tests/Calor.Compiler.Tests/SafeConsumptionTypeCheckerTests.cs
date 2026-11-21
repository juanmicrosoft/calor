using Calor.Compiler.Diagnostics;
using Calor.Compiler.TypeChecking;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class SafeConsumptionTypeCheckerTests
{
    // Isolate opt-in typing; tests that need default-pipeline acceptance compile separately.
    private static CompilationResult Check(string source)
    {
        var result = Program.Compile(source, "safe-consumption.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = false,
            DeferGeneratedOutputValidation = true,
            StatusWriter = TextWriter.Null
        });
        Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code.StartsWith("Calor00", StringComparison.Ordinal)
            || diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        return result;
    }

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
            module SafeConsumption
              func Probe public (string?:input) → string
                !
                let string:value = input ?? "fallback"
                return value
            """, "safe-consumption.calr");
        AssertNoErrors(accepted);
        Assert.Contains("input ?? \"fallback\"", accepted.GeneratedCode);

        var rejected = Check("""
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                let int:value = input ?? "fallback"
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void CoalesceWithThrow_HasTheReferentType()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (string?:input) → void
                ! alloc, throw
                let int:value = input ?? throw new ArgumentNullException()
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void CoalesceWithNullableFallback_StaysNullable()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                let string?:fallback = null
                let Option<string>:value = input ?? fallback
            """);

        var diagnostic = SingleErrorAt(rejected, 5);
        Assert.Contains("string?", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void CoalesceWithNullFallback_StaysNullable()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                let Option<string>:value = input ?? null
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("string?", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void UnknownGenericCoalesce_DoesNotIntroduceOptionSemantics()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (Option<string>:opt) → void
                ! alloc
                let string:value = opt ?? "fallback"
            """);

        Assert.DoesNotContain(rejected.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.ReferenceOptionMismatch);
        Assert.DoesNotContain("Unwrap", rejected.GeneratedCode);
        Assert.DoesNotContain("Calor.Runtime.Option", rejected.GeneratedCode);
        Assert.Contains("Option<string>", rejected.GeneratedCode);
    }

    [Fact]
    public void ConditionalWithOneThrowingArm_HasTheOtherArmType()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (bool:flag) → void
                ! throw
                let int:value = flag ? throw "missing" : "fallback"
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void ConditionalWithBothThrowingArms_HasNeverType()
    {
        var accepted = Check("""
            module SafeConsumption
              func Probe public (bool:flag) → void
                ! throw
                let int:value = (flag ? throw "left" : throw "right") ?? "fallback"
            """);

        AssertNoErrors(accepted);
    }

    [Fact]
    public void ConditionalValidatesItsCondition()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let string:value = 1 ? "yes" : "no"
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("condition must be bool", diagnostic.Message);
    }

    [Fact]
    public void ConditionalReportsUnrelatedArmMismatch()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (bool:flag) → void
                !
                let string:value = flag ? 1 : "fallback"
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("object", diagnostic.Message);
        Assert.Contains("str", diagnostic.Message);
    }

    [Fact]
    public void ThrowExpressionRejectsKnownNonExceptionReference()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (bool:flag, string:message) → void
                ! throw
                let int:value = flag ? throw message : 1
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("requires an exception value", diagnostic.Message);
        Assert.Contains("str", diagnostic.Message);
    }

    [Fact]
    public void IsPatternConditionBindsNonNullVariableOnlyInTrueBranch()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                if input is string text
                  let string:ok = text
                let string:leak = text
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
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                let int:value = input is string text ? text : "fallback"
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void NegatedIsPatternDoesNotBindInTrueBranch()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                if !(input is string text)
                  let string:value = text
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
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                let int:value = match input
                  case string text → text
                  case _ → "fallback"
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("str", diagnostic.Message);
        Assert.DoesNotContain("<error>", diagnostic.Message);
    }

    [Fact]
    public void NegatedMatchTypePatternDoesNotBind()
    {
        var rejected = Check("""
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                let string:value = match input
                  case (not string text) → text
                  case _ → "fallback"
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
            module SafeConsumption
              func Probe public () → void
                !
                let bool:value = 1 is string text
            """);

        var diagnostic = SingleErrorAt(rejected, 4);
        Assert.Contains("not compatible", diagnostic.Message);
        Assert.Contains("string", diagnostic.Message);
        Assert.Contains("int", diagnostic.Message);
    }

    [Theory]
    [InlineData("§IF (is input Widget text)\n      §R true")]
    [InlineData("§IF false\n      §R true\n    §EI (is input Widget text)\n      §R true")]
    [InlineData("§R (? (is input Widget text) true false)")]
    [InlineData("§IF (&& (is input Widget text) true)\n      §R true")]
    [InlineData("§WH (is input Widget text)\n      §R true")]
    [InlineData("§W input\n      §K _ §WHEN (is input Widget text)\n        §R true")]
    [InlineData("§R §W input\n      §K _ §WHEN (is input Widget text) → true\n      §K _ → false")]
    public void UnknownPatternType_IsDiagnosedOnceWhenItsBindingIsTransferred(string body)
    {
        var nativeBody = body switch
        {
            "§IF (is input Widget text)\n      §R true" =>
                "if input is Widget text\n      return true",
            "§IF false\n      §R true\n    §EI (is input Widget text)\n      §R true" =>
                "if false\n      return true\n    else if input is Widget text\n      return true",
            "§R (? (is input Widget text) true false)" =>
                "return input is Widget text ? true : false",
            "§IF (&& (is input Widget text) true)\n      §R true" =>
                "if input is Widget text && true\n      return true",
            "§WH (is input Widget text)\n      §R true" =>
                "while input is Widget text\n      return true",
            "§W input\n      §K _ §WHEN (is input Widget text)\n        §R true" =>
                "match input\n      case _ when input is Widget text\n        return true",
            "§R §W input\n      §K _ §WHEN (is input Widget text) → true\n      §K _ → false" =>
                "return match input\n      case _ when input is Widget text → true\n      case _ → false",
            _ => throw new ArgumentOutOfRangeException(nameof(body), body, "Unmapped original subject")
        };
        var result = Check("""
            module SafeConsumption
              func Probe public (object?:input) → bool
                !
            """ + "\n    " + nativeBody + "\n    return false");

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
            module SafeConsumption
              func Probe public (object?:input) → bool
                !
                if input is Widget first
                  return true
                else if input is Widget second
                  return true
                return false
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
        var nativeExpression = expression switch
        {
            "(?? INT:1 §TH \"missing\")" => "1 ?? throw \"missing\"",
            "(?? BOOL:true false)" => "true ?? false",
            "(?? INT:1 §C{System.Guid.NewGuid} §/C)" => "1 ?? System.Guid.NewGuid()",
            "(? true §TH §C{MakeString} §/C \"fallback\")" => "true ? throw MakeString() : \"fallback\"",
            "(? true §TH §NEW{i32} \"fallback\")" => "true ? throw new int() : \"fallback\"",
            _ => throw new ArgumentOutOfRangeException(nameof(expression), expression, "Unmapped original subject")
        };
        var source = $$"""
            module SafeConsumption
              func MakeString public () → string
                !
                return "not an exception"
              func Probe public () → object
                ! alloc, throw
                return {{nativeExpression}}
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
        var nativeArgument = argument switch
        {
            "(?? 1 2)" => "1 ?? 2",
            "(? 1 2 3)" => "1 ? 2 : 3",
            "(? true §TH §C{MakeString} §/C 1)" => "true ? throw MakeString() : 1",
            "§C{Missing} §A (?? 1 2) §/C" => "Missing(1 ?? 2)",
            _ => throw new ArgumentOutOfRangeException(nameof(argument), argument, "Unmapped original subject")
        };
        var source = $$"""
            module SafeConsumption
              func Take public (int:value) → int
                !
                return value
              func MakeString public () → string
                !
                return "not an exception"
              func Overloaded public (int:value) → int
                !
                return value
              func Overloaded public (string:value) → string
                !
                return value
              func Generic<T> public (T:value) → T
                !
                return value
              func Probe public {{(target == "Shadowed" ? "(int:Shadowed)" : "()")}} → int
                ! throw
                return {{target}}({{nativeArgument}})
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
            module SafeConsumption
              func Double public (int:value) → int
                !
                return value * 2
              func Map public (Func<int,int>:transform !) → int
                !
                return transform(1)
              func Probe public () → int
                !
                return Map(Double)
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void ExpressionCalls_ValidateNestedArguments()
    {
        var result = Check("""
            module SafeConsumption
              func Double public (int:value) → int
                !
                return value * 2
              func GetTransform public () → Func<int,int> ! unknown
                !
                return Double
              func Probe public () → int
                !
                return GetTransform()(1 ?? 2)
            """);

        var diagnostic = SingleErrorAt(result, 10);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void ResolvedOverloadReturnType_IsCheckedByOuterConsumer()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (int:value) → int
                !
                return value
              func Pick public (string:value) → string
                !
                return value
              func Probe public () → int
                !
                return Pick(1) ?? 2
            """);

        var diagnostic = SingleErrorAt(result, 10);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void NullableValueBindings_AcceptValuesAndConditionalNull()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (bool:flag) → void
                !
                let int?:direct = 1
                let int?:conditional = flag ? null : 1
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void MatchExpressions_UnifyThrowingAndNullArms()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (int:value) → void
                ! throw
                let string:text = match value
                  case 0 → "ok"
                  case _ → throw "bad"
                let string?:nullable = match value
                  case 0 → "ok"
                  case _ → null
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void FunctionReferences_AreMatchedAgainstDelegateTargetsAndOverloads()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (string:value) → string
                !
                return value
              func Pick public (int:value) → int
                !
                return value
              func Probe public () → void
                !
                let Func<int,int>:picker = Pick
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void NamedArguments_SelectTheMappedOverloadReturnType()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (string:value, int:number) → string
                !
                return value
              func Pick public (int:value, string:number) → int
                !
                return value
              func Probe public () → string
                !
                return Pick(number: 7, value: "ok") ?? "fallback"
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void GenericCallReturnType_IsCheckedByOuterConsumer()
    {
        var result = Check("""
            module SafeConsumption
              func Identity<T> public (T:value) → T
                !
                return value
              func Probe public () → int
                !
                return Identity<int>(1) ?? 2
            """);

        var diagnostic = SingleErrorAt(result, 7);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void DelegateCalls_ValidateArgumentsAndExposeReturnType()
    {
        var wrongArgument = Check("""
            module SafeConsumption
              func Probe public (Func<int,int>:transform ! unknown) → int
                !
                return transform("wrong")
            """);
        Assert.True(wrongArgument.HasErrors);

        var invalidConsumer = Check("""
            module SafeConsumption
              func Probe public (Func<int,int>:transform ! unknown) → int
                !
                return transform(1) ?? 2
            """);
        var diagnostic = SingleErrorAt(invalidConsumer, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void GenericAndVariantMethodGroups_ConvertToDelegateTargets()
    {
        var result = Check("""
            module SafeConsumption
              func Identity<T> public (T:value) → T
                !
                return value
              func GetText public () → string
                !
                return "text"
              func Probe public () → void
                !
                let Func<int,int>:identity = Identity
                let Func<object>:textFactory = GetText
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void OverloadedMethodGroups_UseCallArgumentDelegateContext()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (string:value) → string
                !
                return value
              func Pick public (int:value) → int
                !
                return value
              func Map public (Func<int,int>:transform ! unknown) → int
                !
                return transform(1)
              func Probe public () → int
                !
                return Map(Pick)
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void UserDeclaredDelegate_ProvidesMethodGroupContext()
    {
        var result = Check("""
            module SafeConsumption
              delegate Picker (int:value) → int
                ! unknown


              func Pick public (string:value) → string
                !
                return value
              func Pick public (int:value) → int
                !
                return value
              func Probe public () → void
                !
                let Picker:picker = Pick
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void NullableValueCommonType_IsIndependentOfArmOrder()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (bool:flag, int?:maybe) → void
                !
                let double?:value = flag ? maybe : 1.5
                let double?:reverse = flag ? 1.5 : maybe
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void MatchExpression_RejectsUnrelatedInferredArmTypes()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (int:value) → void
                !
                let mixed = match value
                  case 0 → 1
                  case _ → "text"
            """);

        var diagnostic = SingleErrorAt(result, 4);
        Assert.Contains("incompatible types", diagnostic.Message);
    }

    [Fact]
    public void UnmodeledStructuralWrappers_StillValidateNestedExpressions()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → string
                !
                return "value=${1 ?? 2}"
            """);

        var diagnostic = SingleErrorAt(result, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void InlineGenericFunctionNames_AreCallableMethodGroups()
    {
        var result = Check("""
            module SafeConsumption
              func Identity<T> public (T:value) → T
                !
                return value
              func Probe public () → void
                !
                let Func<int,int>:identity = Identity
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void GenericInference_MergesAllArgumentBounds()
    {
        var result = Check("""
            module SafeConsumption
              func Choose<T> public (T:first, T:second) → T
                !
                return second
              func Probe public () → int
                !
                return Choose((char)65, 1) ?? 2
            """);

        var diagnostic = SingleErrorAt(result, 7);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void NamedUserDelegateCalls_MapArgumentsByName()
    {
        var result = Check("""
            module SafeConsumption
              delegate Picker (int:number, string:text) → string
                ! unknown



              func Probe public (Picker:pick ! unknown) → string
                !
                return pick(text: "x", number: 1)
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void ExpandedParamsCalls_ExposeTheirReturnType()
    {
        var result = Check("""
            module SafeConsumption
              func First public (params int[]:values) → int
                !
                return values[0]
              func Probe public () → int
                !
                return First(1, 2) ?? 3
            """);

        var diagnostic = SingleErrorAt(result, 7);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void DuplicateNamedParameters_DoNotCrashTypeChecking()
    {
        var exception = Record.Exception(() => Check("""
            module SafeConsumption
              func Take public (int:value, int:value) → int
                !
                return value
              func Probe public () → int
                !
                return Take(value: 1)
            """));

        Assert.Null(exception);
    }

    [Fact]
    public void NullableCoalesce_UsesTheWiderUnderlyingType()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (int?:left, double?:right) → double?
                !
                return left ?? right
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void GenericExpandedParams_InferFromEveryElement()
    {
        var result = Check("""
            module SafeConsumption
              func First<T> public (params T[]:values) → T
                !
                return values[0]
              func Probe public () → int
                !
                return First(1, 2) ?? 3
            """);

        var diagnostic = SingleErrorAt(result, 7);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void GenericCalls_InferFromMethodGroupArguments()
    {
        var result = Check("""
            module SafeConsumption
              func GetInt public () → int
                !
                return 1
              func Make<T> public (Func<T>:factory ! unknown) → T
                !
                return factory()
              func Probe public () → int
                !
                return Make(GetInt) ?? 2
            """);

        var diagnostic = SingleErrorAt(result, 10);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void AnonymousObjectHolders_ExposeNestedExpressions()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → object
                !
                return new { Age = 1 ?? 2 }
            """);

        var diagnostic = SingleErrorAt(result, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void MatchExpression_UsesExplicitObjectTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (int:value) → void
                !
                let object:mixed = match value
                  case 0 → 1
                  case _ → "text"
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
            module SafeConsumption
              func Probe public (int:value) → object
                !
                return match value
                  case 0 → 1
                  case _ → "text"
            """);
        AssertNoErrors(returnResult);

        var argumentResult = Check("""
            module SafeConsumption
              func Take public (object:value) → object
                !
                return value
              func Probe public (int:value) → object
                !
                return Take(match value
                  case 0 → 1
                  case _ → "text"
                )
            """);
        AssertNoErrors(argumentResult);
    }

    [Fact]
    public void OverloadInference_PrefersIdentityConversion()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (int:value) → int
                !
                return value
              func Pick public (double:value) → string
                !
                return "wide"
              func Probe public () → int
                !
                return Pick(1) ?? 2
            """);

        var diagnostic = SingleErrorAt(result, 10);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);

        var categoryResult = Check("""
            module SafeConsumption
              func Pick public (object:value) → string
                !
                return "boxed"
              func Pick public (double:value) → int
                !
                return 1
              func Probe public () → int
                !
                return Pick(1) ?? 2
            """);
        var categoryDiagnostic = SingleErrorAt(categoryResult, 10);
        Assert.Contains("Null-coalescing requires", categoryDiagnostic.Message);

        var namedResult = Check("""
            module SafeConsumption
              func Pick public (int:x, object:y) → int
                !
                return x
              func Pick public (object:y, double:x) → string
                !
                return "wide"
              func Probe public () → int
                !
                return Pick(x: 1, y: "s") ?? 2
            """);
        var namedDiagnostic = SingleErrorAt(namedResult, 10);
        Assert.Contains("Null-coalescing requires", namedDiagnostic.Message);

        var paramsResult = Check("""
            module SafeConsumption
              func Pick public (int:value) → int
                !
                return value
              func Pick public (params int[]:values) → string
                !
                return "expanded"
              func Probe public () → int
                !
                return Pick(1) ?? 2
            """);
        var paramsDiagnostic = SingleErrorAt(paramsResult, 10);
        Assert.Contains("Null-coalescing requires", paramsDiagnostic.Message);

        var charResult = Check("""
            module SafeConsumption
              func Pick public (int:value) → int
                !
                return value
              func Pick public (double:value) → string
                !
                return "wide"
              func Probe public () → int
                !
                return Pick((char)65) ?? 2
            """);
        var charDiagnostic = SingleErrorAt(charResult, 10);
        Assert.Contains("Null-coalescing requires", charDiagnostic.Message);

        var optionalResult = Check("""
            module SafeConsumption
              func Pick public (int:value) → int
                !
                return value
              func Pick public (int:value, int:other = 0) → string
                !
                return "optional"
              func Probe public () → int
                !
                return Pick(1) ?? 2
            """);
        var optionalDiagnostic = SingleErrorAt(optionalResult, 10);
        Assert.Contains("Null-coalescing requires", optionalDiagnostic.Message);
    }

    [Fact]
    public void MethodGroupConversion_RejectsBoxingReturnsAndModifierMismatches()
    {
        var boxingResult = Check("""
            module SafeConsumption
              func GetInt public () → int
                !
                return 1
              func Probe public () → void
                !
                let Func<object>:factory = GetInt
            """);
        Assert.Contains(boxingResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);

        var modifierResult = Check("""
            module SafeConsumption
              func Bump public (ref int:value) → int
                !
                return value
              func Probe public () → void
                !
                let Func<int,int>:f = Bump
            """);
        Assert.Contains(modifierResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void DelegateCalls_ReportShapeErrorsAndPreserveNamedResults()
    {
        var arityResult = Check("""
            module SafeConsumption
              func Probe public (Func<int,int>:transform ! unknown) → int
                !
                return transform(1, 2)
            """);
        Assert.Contains(arityResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);

        var namedResult = Check("""
            module SafeConsumption
              func Probe public (Func<int,int>:transform ! unknown) → int
                !
                return transform(arg: 1) ?? 2
            """);
        var diagnostic = SingleErrorAt(namedResult, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void GenericMethodGroupInference_IsIndependentOfDeclarationOrder()
    {
        var result = Check("""
            module SafeConsumption
              func Get public (object:value) → string
                !
                return "wide"
              func Get public (string:value) → int
                !
                return 1
              func Choose<T> public (Func<string,T>:factory ! unknown, T:fallback) → T
                !
                return fallback
              func Probe public () → int
                !
                return Choose(Get, 1) ?? 2
            """);

        var diagnostic = SingleErrorAt(result, 13);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void StatementLambda_UsesItsDelegateReturnTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (int:value) → int
                !
                let Func<object>:factory = () =>
                  return match value
                    case 0 → 1
                    case _ → "text"

                return 0
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void DelegateCalls_ContextuallyTypeMatchArguments()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (Func<object,object>:transform ! unknown, int:value) → object
                !
                return transform(match value
                  case 0 → 1
                  case _ → "text"
                )
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void GenericLambdaContext_UsesOrdinaryArgumentInferenceFirst()
    {
        var result = Check("""
            module SafeConsumption
              func Choose<T> public (Func<T>:factory ! unknown, T:fallback) → T
                !
                return fallback
              func Probe public (int:value, object:fallback) → object
                !
                return Choose(() =>
                  return match value
                    case 0 → 1
                    case _ → "text"
                , fallback)
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void InapplicableOverload_DoesNotEraseCallArgumentTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Take public (object:value) → object
                !
                return value
              func Take public (int:value, int:other) → object
                !
                return value
              func Probe public (int:value) → object
                !
                return Take(match value
                  case 0 → 1
                  case _ → "text"
                )
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void NonContextualArguments_NarrowTheContextualCandidateSet()
    {
        var result = Check("""
            module SafeConsumption
              func Take public (object:value, int:tag) → object
                !
                return value
              func Take public (string:value, string:tag) → object
                !
                return value
              func Probe public (int:value) → object
                !
                return Take(match value
                  case 0 → 1
                  case _ → "text"
                , 1)
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void ContextualLambda_ValidatesItsDeclaredSignatureAndReturnType()
    {
        var returnResult = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () => "wrong"
            """);
        Assert.Contains(returnResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);

        var parameterResult = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int,int>:map = (string:value) => 1
            """);
        Assert.Contains(parameterResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void CallStatements_UseSignatureAwareDelegateValidation()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (Action<int>:consume ! unknown) → void
                !
                consume()
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void DelegateInvocationAndMethodGroups_RespectByRefAndEnumValueSemantics()
    {
        var modifierResult = Check("""
            module SafeConsumption
              delegate Mutator (ref int:value) → int
                ! unknown


              func Probe public (Mutator:mutate ! unknown, int:value) → int
                !
                return mutate(value)
            """);
        Assert.Contains(modifierResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);

        var enumResult = Check("""
            module SafeConsumption
              enum Color
                Red

              func GetColor public () → Color
                !
                return Color.Red
              func Probe public () → void
                !
                let Func<object>:factory = GetColor
            """);
        Assert.Contains(enumResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);

        var identityResult = Check("""
            module SafeConsumption
              delegate Mutator (ref double:value) → void
                ! unknown


              func Probe public (Mutator:mutate ! unknown, ref int:value) → void
                !
                mutate(ref value)
            """);
        Assert.Contains(identityResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void UncontextualizedLambda_StillTraversesNestedConsumers()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                System.Threading.Tasks.Task.Run(() => 1 ?? 2)
            """);

        var diagnostic = SingleErrorAt(result, 4);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void ContextualInference_HandlesParamsAndVariableGenericBounds()
    {
        var paramsResult = Check("""
            module SafeConsumption
              func Take public (params object[]:values) → object
                !
                return values
              func Probe public (int:value) → object
                !
                return Take(match value
                  case 0 → 1
                  case _ → "text"
                )
            """);
        AssertNoErrors(paramsResult);

        var variableResult = Check("""
            module SafeConsumption
              func Choose<T> public (T:first, T:second) → T
                !
                return first
              func Probe public (int:value, object:fallback) → object
                !
                return Choose(fallback, match value
                  case 0 → 1
                  case _ → "text"
                )
            """);
        AssertNoErrors(variableResult);
    }

    [Fact]
    public void OverloadedMethodGroupInference_UsesTheBestMember()
    {
        var result = Check("""
            module SafeConsumption
              func Value public (object:value) → int
                !
                return 1
              func Value public (string:value) → string
                !
                return value
              func Use<T> public (Func<string,T>:factory ! unknown) → T
                !
                return factory("x")
              func Probe public () → int
                !
                return Use(Value)
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void LosingLambdaOverloads_DoNotLeakDiagnostics()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (Func<int,int>:map ! unknown) → int
                !
                return map(1)
              func Pick public (Func<string,string>:map ! unknown) → string
                !
                return map("x")
              func Probe public () → int
                !
                return Pick((int:x) => x)
            """);
        AssertNoErrors(result);

        var actionResult = Check("""
            module SafeConsumption
              func Identity public (int:value) → int
                !
                return value
              func Probe public () → void
                !
                let Action<int>:action = (int:x) =>
                  Identity(x)

            """);
        AssertNoErrors(actionResult);
    }

    [Fact]
    public void NestedMatchExpressions_InheritTheOuterTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (int:value) → object
                !
                return match value
                  case 0 → match value
                    case 0 → 1
                    case _ → "text"
                  case _ → "fallback"
            """);

        AssertNoErrors(result);
    }

    [Fact]
    public void UncontextualizedMatch_StillTraversesNestedConsumers()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (int:key) → void
                !
                System.Console.WriteLine(match key
                  case 0 → 1 ?? 2
                  case _ → "text"
                )
            """);

        var diagnostic = SingleErrorAt(result, 5);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void ContextualCandidateProbes_DiscardNestedDiagnostics()
    {
        var result = Check("""
            module SafeConsumption
              func Take public (object:value) → object
                !
                return value
              func Probe public (int:key) → object
                !
                return Take(match key
                  case 0 → 1 ?? 2
                  case _ → "text"
                )
            """);

        var diagnostic = SingleErrorAt(result, 8);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);

        var divergentResult = Check("""
            module SafeConsumption
              func Pick public (Func<int,int>:map ! unknown) → int
                !
                return map(1)
              func Pick public (Func<string,string>:map ! unknown) → string
                !
                return map("x")
              func Probe public () → int
                !
                return Pick((int:x) => x ?? 2)
            """);
        Assert.Single(divergentResult.Diagnostics.Errors.Where(
            error => error.Message.Contains("Null-coalescing requires", StringComparison.Ordinal)));
    }

    [Fact]
    public void GenericMethodGroupCosts_UseSubstitutedSignatures()
    {
        var result = Check("""
            module SafeConsumption
              func Map<T> public (T:value) → T
                !
                return value
              func Map public (object:value) → object
                !
                return value
              func Use public (Func<string,string>:map ! unknown) → int
                !
                return 1
              func Use public (Func<object,object>:map ! unknown) → string
                !
                return "wide"
              func Probe public () → string
                !
                return Use(Map) ?? "fallback"
            """);

        var diagnostic = SingleErrorAt(result, 16);
        Assert.Contains("Null-coalescing requires", diagnostic.Message);
    }

    [Fact]
    public void DirectCalls_RejectByRefWideningAndInvalidNamedOrdering()
    {
        var refResult = Check("""
            module SafeConsumption
              func Mutate public (ref double:value) → void
                !
              func Probe public (ref int:value) → void
                !
                Mutate(ref value)
            """);
        Assert.Contains(refResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);

        var namedResult = Check("""
            module SafeConsumption
              func Take public (int:left, int:right) → int
                !
                return left
              func Probe public () → int
                !
                return Take(right: 2, 1)
            """);
        Assert.Contains(namedResult.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void FailedContextualOverloads_ReportNoMatchingOverload()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (Func<int,int>:map ! unknown) → int
                !
                return map(1)
              func Pick public (Func<string,int>:map ! unknown) → int
                !
                return 1
              func Probe public () → int
                !
                return Pick((int:x) => "wrong")
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void GenericCalls_InferTypeArgumentsFromLambdaResults()
    {
        var result = Check("""
            module SafeConsumption
              func Make<T> public (Func<T>:factory ! unknown) → T
                !
                return factory()
              func Probe public () → string
                !
                return Make(() => 1)
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_ForFuncRequiresAReturnValue()
    {
        var result = Check("""
            module SafeConsumption
              func Identity public (int:value) → int
                !
                return value
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  Identity(1)

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_ForFuncRequiresAllPathsToReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<bool,int>:factory = (bool:flag) =>
                  if flag
                    return 1

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void GenericCalls_InferLambdaResultsInsideParameterScope()
    {
        var result = Check("""
            module SafeConsumption
              func Apply<T,U> public (T:value, Func<T,U>:map ! unknown) → U
                !
                return map(value)
              func Probe public () → int
                !
                return Apply(1, (x) => x)
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void GenericCalls_InferLambdaResultsFromLocalBindings()
    {
        var result = Check("""
            module SafeConsumption
              func Make<T> public (Func<T>:factory ! unknown) → T
                !
                return factory()
              func Probe public () → int
                !
                return Make(() =>
                  let int:x = 1
                  return x
                )
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void GenericCalls_ExcludeNestedLambdaReturnsFromOuterInference()
    {
        var result = Check("""
            module SafeConsumption
              func Make<T> public (Func<T>:factory ! unknown) → T
                !
                return factory()
              func Probe public () → int
                !
                return Make(() =>
                  let Func<string>:nested = () =>
                    return "nested"

                  return 1
                )
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ExhaustiveMatchCanDefinitelyReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int,int>:factory = (int:value) =>
                  match value
                    case 0
                      return 1
                    case _
                      return 2

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ReturningTryWithFinallyCanDefinitelyReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  try
                    return 1
                  finally
                    System.Console.WriteLine("cleanup")

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_UnconditionalLoopCanDefinitelyNotReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  while true
                    System.Console.WriteLine("loop")

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_UnconditionalDoWhileCanDefinitelyNotReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  do
                    System.Console.WriteLine("loop")
                  while true

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_DoWhileWithReturningBodyDefinitelyReturns()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  do
                    return 1
                  while false

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_UnconditionalLoopWithNestedBreakMustReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  while true
                    unsafe
                      break

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_UnreachableLoopExitAfterReturnDoesNotFallThrough()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  while true
                    return 1
                    break

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ReturnAfterReachableLoopExitIsUnreachable()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  while true
                    break
                    return 1

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_UnconditionalLoopWithGotoMustReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  while true
                    goto done
                  done:
                  System.Console.WriteLine("done")

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_GotoAcrossDeeplyNestedLoopsMustReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  while true
                    while true
                      while true
                        while true
                          while true
                            while true
                              while true
                                goto done
                  done:
                  System.Console.WriteLine("done")

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_UnconditionalLoopWithInternalGotoDoesNotReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  while true
                    again:
                    goto again

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ForwardGotoWithinLoopRemainsInsideLoop()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  while true
                    goto again
                    again:
                    System.Console.WriteLine("loop")

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ForwardGotoFollowsLocalReturnTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  goto done
                  done:
                  return 1

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ForwardGotoDoesNotResumeAtSkippedReturn()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  goto done
                  return 1
                  done:
                  System.Console.WriteLine("done")

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_NestedGotoFollowsEnclosingReturnTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<bool,int>:factory = (bool:flag) =>
                  if flag
                    goto done
                  return 1
                  done:
                  return 2

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoCaseInsideLoopMatchDoesNotExitLoop()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  while true
                    match 1
                      case 1
                        goto case 2
                      case 2
                        System.Console.WriteLine("loop")

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoCaseFollowsReturningTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  match 1
                    case 1
                      goto case 2
                    case 2
                      return 2
                    case _
                      return 0

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoCharacterCaseFollowsReturningTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  match 'a'
                    case 'a'
                      goto case 'b'
                    case 'b'
                      return 2
                    case _
                      return 0

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoNullCaseFollowsReturningTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (string?:input) → void
                !
                let Func<int>:factory = () =>
                  match input
                    case "x"
                      goto case null
                    case null
                      return 2
                    case _
                      return 0

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoBooleanCaseFollowsReturningTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (bool:input) → void
                !
                let Func<int>:factory = () =>
                  match input
                    case false
                      goto case true
                    case true
                      return 2
                    case _
                      return 0

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoCaseUsesConvertedIntegralValue()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (uint:input) → void
                !
                let Func<int>:factory = () =>
                  match input
                    case 1u
                      goto case 2
                    case 2u
                      return 2
                    case _
                      return 0

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoCaseConvertsIntegralValueToCharacter()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  match 'a'
                    case 'a'
                      goto case 98
                    case 'b'
                      return 2
                    case _
                      return 0

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoCaseConvertsIntegralValueToNullableCharacter()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (char?:input) → void
                !
                let Func<int>:factory = () =>
                  match input
                    case 'a'
                      goto case 98
                    case 'b'
                      return 2
                    case _
                      return 0

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoCaseConvertsCharacterLabelToIntegralValue()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (int:input) → void
                !
                let Func<int>:factory = () =>
                  match input
                    case 1
                      goto case 98
                    case 'b'
                      return 2
                    case _
                      return 0

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_NestedMatchUsesCachedLocalTargetType()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<bool,int>:factory = (bool:flag) =>
                  if flag
                    let char:c = 'a'
                    match c
                      case 'a'
                        goto case 98
                      case 'b'
                        return 1
                      case _
                        return 0
                  else
                    return 2

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_TryMatchUsesCachedTargetType()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  try
                    match 'a'
                      case 'a'
                        goto case 98
                      case 'b'
                        return 2
                      case _
                        return 0
                  finally
                    System.Console.WriteLine("finally")

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_GotoCaseCannotTargetGuardedArm()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  match 1
                    case 1
                      goto case 2
                    case 2 when false
                      return 2
                    case _
                      return 0

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void VoidStatementLambda_ReportsGuardedGotoCaseTarget()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Action<int>:action = (int:x) =>
                  match x
                    case 1
                      goto case 2
                    case 2 when false
                      System.Console.WriteLine("two")
                    case _
                      System.Console.WriteLine("other")

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_BreakInsideLoopMatchDoesNotExitLoop()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  while true
                    match 1
                      case 1
                        break

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ReturnAfterMatchBreakIsUnreachable()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  match 1
                    case _
                      break
                      return 1

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_ContinueOutsideLoopDoesNotCompleteLambda()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  continue

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void VoidStatementLambda_ContinueOutsideLoopIsInvalid()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Action<int>:action = (int:x) =>
                  continue

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void VoidStatementLambda_BreakOutsideLoopIsInvalid()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Action<int>:action = (int:x) =>
                  break

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_RethrowOutsideCatchIsInvalid()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  throw

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void VoidStatementLambda_RethrowInsideForIsInvalid()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Action<int>:action = (int:x) =>
                  for i from 0 to 1 step 1
                    throw

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void UncontextualizedLambda_RethrowOutsideCatchIsInvalid()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                System.Threading.Tasks.Task.Run(() =>
                  throw
                )
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void UncontextualizedLambda_ContinueOutsideLoopIsInvalid()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                System.Threading.Tasks.Task.Run(() =>
                  continue
                )
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void NestedLambdaInsideTry_PreservesControlFlowDiagnostic()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Action<int>:outer = (int:x) =>
                  try
                    let Action<int>:inner = (int:y) =>
                      throw

                  finally
                    System.Console.WriteLine("finally")

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch
                && diagnostic.Message.Contains(
                    "rethrow must be used within a catch block",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ContextualLambdaReturnInsideTry_IsValidated()
    {
        var result = Check("""
            module SafeConsumption
              func Make<T> public (Func<T>:factory ! unknown) → T
                !
                return factory()
              func Probe public () → int
                ! cw
                return Make(() =>
                  try
                    return "bad"
                  finally
                    System.Console.WriteLine("done")
                )
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void VoidStatementLambda_CatchFilterMustBeBoolean()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Action<int>:action = (int:x) =>
                  try
                    System.Console.WriteLine("try")
                  catch Exception ex when 1
                    System.Console.WriteLine("catch")

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_GotoCannotLeaveFinally()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  try
                    System.Console.WriteLine("try")
                  finally
                    goto done
                  done:
                  return 1

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_ReturnCannotLeaveFinally()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  try
                    System.Console.WriteLine("try")
                  finally
                    return 1

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_ThrowCanTerminateFinally()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                !
                let Func<int>:factory = () =>
                  try
                    System.Console.WriteLine("try")
                  finally
                    throw "boom"

            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void StatementLambda_ContinueCannotLeaveFinally()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  while true
                    try
                      System.Console.WriteLine("try")
                    finally
                      continue

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void StatementLambda_BreakCannotLeaveFinally()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public () → void
                ! cw
                let Func<int>:factory = () =>
                  while true
                    try
                      System.Console.WriteLine("try")
                    finally
                      break

            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void MethodGroupParameters_SupportReferenceDelegateVariance()
    {
        var result = Check("""
            module SafeConsumption
              func Use public (Func<object>:factory ! unknown) → void
                !
              func Probe public () → void
                !
                let Action<Func<string>>:action = Use
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void MethodGroupParameters_SupportNestedDelegateReturnVariance()
    {
        var result = Check("""
            module SafeConsumption
              func Use public (Func<Func<object>>:factory ! unknown) → void
                !
              func Probe public () → void
                !
                let Action<Func<Func<string>>>:action = Use
            """);

        Assert.Empty(result.Diagnostics.Errors);
    }

    [Fact]
    public void MethodGroupParameters_RejectDistinctNamedDelegates()
    {
        var result = Check("""
            module SafeConsumption
              delegate First (string:value) → void
                ! unknown


              delegate Second (string:value) → void
                ! unknown


              func Use public (First:callback ! unknown) → void
                !
              func Probe public () → void
                !
                let Action<Second>:action = Use
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void MethodGroupParameters_RejectWideningAndBoxing()
    {
        var widening = Check("""
            module SafeConsumption
              func Widen public (double:value) → void
                !
              func Probe public () → void
                !
                let Action<int>:action = Widen
            """);
        Assert.Contains(widening.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);

        var boxing = Check("""
            module SafeConsumption
              func Box public (object:value) → void
                !
              func Probe public () → void
                !
                let Action<int>:action = Box
            """);
        Assert.Contains(boxing.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void MethodGroupRanking_RejectsParetoIncomparableCandidates()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (string:left, object:right) → int
                !
                return left.Length
              func Pick public (object:left, string:right) → int
                !
                return right.Length
              func Take public (Func<string,string,int>:picker ! unknown) → void
                !
              func Probe public () → void
                !
                Take(Pick)
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void DelegateCalls_RejectPositionalArgumentsAfterOutOfPositionNamedArguments()
    {
        var result = Check("""
            module SafeConsumption
              func Probe public (Func<int,int,int>:combine ! unknown) → int
                !
                return combine(arg2: 2, 1)
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void MethodGroupRanking_DoesNotUseReturnTypeIdentity()
    {
        var result = Check("""
            module SafeConsumption
              func Value public (double:value) → object
                !
                return value
              func Value public (decimal:value) → string
                !
                return "value"
              func Probe public () → void
                !
                let Func<char,object>:factory = Value
            """);

        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void ImplicitLambda_UsesAnonymousFunctionBetterConversion()
    {
        var result = Check("""
            module SafeConsumption
              func Pick public (Func<object,int>:map ! unknown) → string
                !
                return "object"
              func Pick public (Func<string,int>:map ! unknown) → int
                !
                return 1
              func Probe public () → int
                !
                return Pick((x) => 1)
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
        var nativeBody = body switch
        {
            "§R §C{Take} §A (?? 1 2) §/C" => "return Take(1 ?? 2)",
            "§C{Take} §A (?? 1 2) §/C\n    §R 0" => "Take(1 ?? 2)\n    return 0",
            "§B{result:i32} (?? 1 2)\n    §R result" => "let int:result = 1 ?? 2\n    return result",
            _ => throw new ArgumentOutOfRangeException(nameof(body), body, "Unmapped original subject")
        };
        var source = $$"""
            module M
              func Take public (int:value) → int
                !
                return value
              func Probe public () → int
                !
                {{nativeBody}}
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
            module M
              func Take public (bool:value) → bool
                !
                return value
              func Probe public (string?:input) → string
                !
                let result = Take(input is string text)
                return text
            """);
        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UndefinedReference, error.Code);
        Assert.Equal(8, error.Span.Line);
        Assert.Contains("text", error.Message);
    }

    [Fact]
    public void NativeInvocationStatements_AcceptValidDelegateAndFunctionArguments()
    {
        var result = Check("""
            module SafeConsumption
              func Take public (int:value) → void
                !
              func Probe public (Action<int>:consume ! unknown) → void
                !
                consume(1)
                Take(1)
                System.Console.WriteLine(1)
            """);

        AssertNoErrors(result);
    }

    [Theory]
    [InlineData("catch", false)]
    [InlineData("outside", true)]
    [InlineData("nested", true)]
    public void NativeBareThrow_UsesOnlyTheCurrentLambdaCatchScope(string scope, bool invalid)
    {
        var body = scope switch
        {
            "catch" => """
                try
                  throw new Exception()
                catch Exception ex
                  throw
                """,
            "outside" => "throw",
            "nested" => """
                try
                  throw new Exception()
                catch Exception ex
                  let Action:inner = () =>
                    throw
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown catch scope")
        };
        var source = """
            module SafeConsumption
              func Probe public () → void
                ! alloc, throw
                let Action:action = () =>
            """ + "\n" + string.Join("\n", body.Split('\n').Select(line => "      " + line));
        var result = Check(source);

        if (invalid)
        {
            var error = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.TypeMismatch, error.Code);
            Assert.Equal("rethrow must be used within a catch block", error.Message);
        }
        else
        {
            AssertNoErrors(result);
        }
    }

    [Theory]
    [InlineData("string", "object", false)]
    [InlineData("object", "string", true)]
    [InlineData("int", "object", true)]
    public void NestedStandardDelegateVariance_PermitsOnlyReferenceConversions(
        string sourceType, string targetType, bool invalid)
    {
        var result = Check($$"""
            module SafeConsumption
              func Use public (Func<Func<{{targetType}}>>:factory ! unknown) → void
                !
              func Probe public () → void
                !
                let Action<Func<Func<{{sourceType}}>>>:action = Use
            """);

        if (invalid)
        {
            var error = SingleErrorAt(result, 6);
            Assert.Contains("Method group 'Use' is not compatible", error.Message);
        }
        else
        {
            AssertNoErrors(result);
        }
    }

    [Theory]
    [InlineData("First", false)]
    [InlineData("Second", true)]
    public void NestedNamedDelegates_RetainNominalIdentity(string delegateName, bool invalid)
    {
        var result = Check($$"""
            module SafeConsumption
              delegate First (string:value) → void
                ! unknown
              delegate Second (string:value) → void
                ! unknown
              func Use public (Func<First>:factory ! unknown) → void
                !
              func Probe public () → void
                !
                let Action<Func<{{delegateName}}>>:action = Use
            """);

        if (invalid)
        {
            var error = SingleErrorAt(result, 10);
            Assert.Contains("Method group 'Use' is not compatible", error.Message);
        }
        else
        {
            AssertNoErrors(result);
        }
    }
}
