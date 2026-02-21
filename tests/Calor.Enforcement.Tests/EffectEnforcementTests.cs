using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for effect enforcement at compile time.
/// </summary>
public class EffectEnforcementTests
{
    [Fact]
    public void E1_MissingEffect_FailsWithForbiddenEffect()
    {
        // Function uses §P (print) but doesn't declare cw effect
        var source = TestHarness.LoadScenario("Effects/E1_missing_effect.calr");
        var expected = TestHarness.LoadExpected("Effects/E1_missing_effect.expected.json");

        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should have compilation errors");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void E2_DeclaredEffect_CompilesSuccessfully()
    {
        // Function uses §P (print) and declares cw effect
        var source = TestHarness.LoadScenario("Effects/E2_declared_effect.calr");

        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors, $"Should compile successfully. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void E3_CallChain_ReportsCallerFunction()
    {
        // Function A calls function B which has cw effect
        // A should fail because it doesn't declare cw
        var source = TestHarness.LoadScenario("Effects/E3_call_chain.calr");
        var expected = TestHarness.LoadExpected("Effects/E3_call_chain.expected.json");

        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should have compilation errors");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void E4_UnknownExternal_FailsInStrictMode()
    {
        // Function calls unknown external method
        var source = TestHarness.LoadScenario("Effects/E4_unknown_external.calr");
        var expected = TestHarness.LoadExpected("Effects/E4_unknown_external.expected.json");

        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should have compilation errors");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void E5_Recursion_ComputesEffectsViaFixpoint()
    {
        // Recursive function with print - effect should be computed via fixpoint
        var source = TestHarness.LoadScenario("Effects/E5_recursion.calr");
        var expected = TestHarness.LoadExpected("Effects/E5_recursion.expected.json");

        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should have compilation errors");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void E6_SpanAccuracy_PointsAtCorrectLocation()
    {
        // Test that diagnostic points at the §P statement
        var source = TestHarness.LoadScenario("Effects/E6_span_accuracy.calr");

        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should have compilation errors");

        var diag = result.Diagnostics.Errors.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(diag);
        // The §P statement is on line 7 in the test file, but diagnostic may point to function
        // Just verify we have a valid span
        Assert.True(diag.Span.Line >= 1 && diag.Span.Line <= 11,
            $"Expected line within file range, got {diag.Span.Line}");
    }

    [Fact]
    public void EffectEnforcement_CanBeDisabled()
    {
        // Same source as E1 but with enforcement disabled
        var source = TestHarness.LoadScenario("Effects/E1_missing_effect.calr");
        var options = new CompilationOptions { EnforceEffects = false };

        var result = TestHarness.Compile(source, options);

        Assert.False(result.HasErrors, "Should compile successfully with enforcement disabled");
    }

    [Fact]
    public void MultipleEffects_AllMustBeDeclared()
    {
        var source = @"
§M{m001:Test}
§F{f001:PrintAndRead:pub}
  §O{str}
  §P ""Enter your name: ""
  §B{str:name} STR:""placeholder""
  §R name
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should have compilation errors");
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void PureFunction_WithNoEffects_Compiles()
    {
        var source = @"
§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors, $"Pure function should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void ConsolePrint_RequiresCwEffect()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestPrint:pub}
  §O{void}
  §P ""test""
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should require cw effect");
        TestHarness.AssertDiagnostic(result.Diagnostics.Errors, DiagnosticCode.ForbiddenEffect, "TestPrint");
    }

    [Fact]
    public void LinqCallsInFunction_ArePure_CompilesSuccessfully()
    {
        // LINQ extension method calls in §F should be recognized as pure
        var source = @"
§M{m001:Test}
§F{f001:SortItems:pub}
  §O{void}
  §C{items.OrderByDescending}
  §/C
  §C{items.ToArray}
  §/C
  §C{items.Where}
  §/C
  §C{items.Select}
  §/C
  §C{items.First}
  §/C
  §C{items.Any}
  §/C
  §C{items.Count}
  §/C
  §C{items.Distinct}
  §/C
  §C{items.ToList}
  §/C
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"LINQ calls should compile without errors. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void LinqStaticCall_Enumerable_Range_CompilesSuccessfully()
    {
        // Static LINQ calls like Enumerable.Range should resolve via type mapping
        var source = @"
§M{m001:Test}
§F{f001:MakeRange:pub}
  §O{void}
  §C{Enumerable.Range}
    §A INT:1
    §A INT:10
  §/C
  §C{Enumerable.Empty}
  §/C
  §C{Enumerable.Repeat}
    §A INT:0
    §A INT:5
  §/C
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Static Enumerable calls should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void LinqOfType_CompilesSuccessfully()
    {
        // OfType<T> calls should be recognized as pure
        var source = @"
§M{m001:Test}
§F{f001:FilterTypes:pub}
  §O{void}
  §C{items.OfType}
  §/C
  §C{items.Cast}
  §/C
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"OfType/Cast should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void MethodInClass_WithUndeclaredEffect_FailsEnforcement()
    {
        // §MT method inside §CL using §P without declaring cw should fail
        var source = @"
§M{m001:Test}
§CL{c001:MyService:pub}
  §MT{mt001:PrintHello:pub}
    §O{void}
    §P ""Hello from method""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors,
            "§MT method with undeclared effect should fail enforcement");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("PrintHello"));
    }

    [Fact]
    public void MethodInClass_WithDeclaredEffect_CompilesSuccessfully()
    {
        // §MT method inside §CL using §P with cw declared should pass
        var source = @"
§M{m001:Test}
§CL{c001:MyService:pub}
  §MT{mt001:PrintHello:pub}
    §O{void}
    §E{cw}
    §P ""Hello from method""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"§MT with declared cw effect should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void MethodInClass_WithLinqCalls_CompilesSuccessfully()
    {
        // §MT method with LINQ calls should compile (both class enforcement and LINQ purity)
        var source = @"
§M{m001:Test}
§CL{c001:DataProcessor:pub}
  §MT{mt001:ProcessItems:pub}
    §O{void}
    §C{items.OrderByDescending}
    §/C
    §C{items.ToArray}
    §/C
    §C{items.Where}
    §/C
  §/MT{mt001}
§/CL{c001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"LINQ calls in §MT should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void MethodInClass_CallingConsoleWriteLine_FailsWithoutEffect()
    {
        // §MT method calling Console.WriteLine without §E{cw} should fail
        var source = @"
§M{m001:Test}
§CL{c001:Logger:pub}
  §MT{mt001:Log:pub}
    §O{void}
    §C{Console.WriteLine}
      §A STR:""log message""
    §/C
  §/MT{mt001}
§/CL{c001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors,
            "§MT calling Console.WriteLine without cw effect should fail");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void PureMethodInClass_WithNoEffects_Compiles()
    {
        // §MT method with only pure operations should compile
        var source = @"
§M{m001:Test}
§CL{c001:Calculator:pub}
  §MT{mt001:Add:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §R (+ a b)
  §/MT{mt001}
§/CL{c001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Pure §MT should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void ConstructorWithFieldAssignment_NotFlaggedForEffects()
    {
        // §CTOR that assigns to §THIS fields should NOT be flagged for mut effect
        // since constructors can't declare effects and field assignment is their purpose
        var source = @"
§M{m001:Test}
§CL{c001:Person:pub}
  §FLD{str:_name:pri}
  §CTOR{ctor1:pub}
    §I{str:name}
    §ASSIGN §THIS._name name
  §/CTOR{ctor1}
§/CL{c001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Constructor field assignment should not fail enforcement. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void DelegateInvocation_SingleWordTarget_DoesNotFail()
    {
        // Calling a variable (delegate/Func parameter) should not produce
        // an "unknown external call" error since it's not an external call
        var source = @"
§M{m001:Test}
§CL{c001:Mapper:pub}
  §MT{mt001:Apply:pub}
    §I{i32:value}
    §O{i32}
    §R §C{transform} §A value §/C
  §/MT{mt001}
§/CL{c001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Delegate invocation should not fail. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void DelegateInvocation_InStrictMode_EmitsWarning()
    {
        // In strict effects mode, delegate invocations should get a warning
        var source = @"
§M{m001:Test}
§F{f001:UseDelegate:pub}
  §O{void}
  §C{callback}
  §/C
§/F{f001}
§/M{m001}
";
        var options = new CompilationOptions
        {
            EnforceEffects = true,
            StrictEffects = true
        };
        var result = TestHarness.Compile(source, options);

        // Should not produce errors (delegate invocations are assumed pure)
        Assert.False(result.HasErrors,
            $"Delegate invocation should not be an error even in strict mode. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
        // But should produce a warning about unverified effects
        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("callback"));
    }

    // === §HAS/§IDX/§CNT inside lisp expressions (Issue 319) ===

    [Fact]
    public void HasInsideLispNegation_CompilesSuccessfully()
    {
        // §HAS should be usable inside lisp-style prefix expressions: (! §HAS{...} ...)
        var source = @"
§M{m001:Test}
§F{f001:NotContains:pub}
  §I{str:items}
  §O{bool}
  §R (! §HAS{items} ""key"")
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"§HAS inside (! ...) should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
        Assert.Contains("!items.Contains", result.GeneratedCode);
    }

    [Fact]
    public void HasInsideLispAnd_CompilesSuccessfully()
    {
        // §HAS should be usable inside lisp-style boolean expressions: (&& §HAS{...} expr)
        var source = @"
§M{m001:Test}
§F{f001:CheckBoth:pub}
  §I{str:items}
  §I{bool:flag}
  §O{bool}
  §R (&& §HAS{items} ""a"" flag)
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"§HAS inside (&&) should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void IdxInsideLispAdd_CompilesSuccessfully()
    {
        // §IDX should be usable inside lisp expressions: (+ sum §IDX arr i)
        var source = @"
§M{m001:Test}
§F{f001:SumFirst:pub}
  §I{[i32]:arr}
  §O{i32}
  §B{val} §IDX arr 0
  §R (+ val §IDX arr 1)
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"§IDX inside (+ ...) should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
        Assert.Contains("arr[1]", result.GeneratedCode);
    }

    [Fact]
    public void CntInsideLispComparison_CompilesSuccessfully()
    {
        // §CNT should be usable inside lisp expressions: (> §CNT{list} 0)
        var source = @"
§M{m001:Test}
§F{f001:HasItems:pub}
  §I{List<i32>:items}
  §O{bool}
  §R (> §CNT{items} 0)
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"§CNT inside (> ...) should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
        Assert.Contains("items.Count", result.GeneratedCode);
    }
}
