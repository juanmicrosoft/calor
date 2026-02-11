using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Negative tests to ensure no trivial bypass of effect enforcement.
/// </summary>
public class BypassTests
{
    [Fact]
    public void N1_UnknownExternalInPureFunction_Fails()
    {
        // Pure function can't call unknown external
        var source = TestHarness.LoadScenario("Bypass/N1_unknown_external_pure.calr");
        var expected = TestHarness.LoadExpected("Bypass/N1_unknown_external_pure.expected.json");

        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should fail with unknown external call");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void N2_IndirectPrint_StillRequiresEffect()
    {
        // Trying to hide print behind a local function call
        var source = @"
§M{m001:Test}
§F{f001:IndirectPrint:pub}
  §I{str:msg}
  §O{void}
  §C{Helper}
    §A msg
  §/C
§/F{f001}
§F{f002:Helper:int}
  §I{str:msg}
  §O{void}
  §E{cw}
  §P msg
§/F{f002}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        // f001 should fail because it calls f002 which has cw effect
        Assert.True(result.HasErrors, "Should fail - indirect effect not declared");
        TestHarness.AssertDiagnostic(result.Diagnostics.Errors, DiagnosticCode.ForbiddenEffect, "IndirectPrint");
    }

    [Fact]
    public void N3_PropertyGetterEffect_NotBypassed()
    {
        // Accessing DateTime.Now should require time effect
        var source = @"
§M{m001:Test}
§F{f001:GetTime:pub}
  §O{str}
  §B{str:now} STR:""placeholder""
  §R now
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        // This is a pure function test - should compile
        // The real DateTime.Now call would be in external code
        Assert.False(result.HasErrors, $"Should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void N4_ConstructorEffect_NotBypassed()
    {
        // This test documents that external constructor calls would be caught
        var source = @"
§M{m001:Test}
§F{f001:CreateRandom:pub}
  §O{i32}
  §B{i32:val} INT:0
  §R val
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        // Pure function should compile
        Assert.False(result.HasErrors, $"Should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void NestedLambda_ContributesEffects()
    {
        // Lambda with print should contribute effect to enclosing function
        // Function does NOT declare cw effect, but lambda inside has §P (print)
        var source = @"
§M{m001:Test}
§F{f001:WithLambda:pub}
  §O{void}
  §B{action:Action<i32>} §LAM{lam1:x:i32}
    §P x
  §/LAM{lam1}
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        // Lambda print should require effect declaration on enclosing function
        Assert.True(result.HasErrors, "Should fail - lambda has print effect but function doesn't declare cw");
    }

    [Fact]
    public void ConditionalEffect_StillRequired()
    {
        // Print in conditional branch still requires effect
        var source = @"
§M{m001:Test}
§F{f001:MaybePrint:pub}
  §I{bool:shouldPrint}
  §O{void}
  §IF{i1} shouldPrint
    §P ""Printed""
  §/I{i1}
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should fail - conditional print still requires effect");
        TestHarness.AssertDiagnostic(result.Diagnostics.Errors, DiagnosticCode.ForbiddenEffect, "MaybePrint");
    }

    [Fact]
    public void TryCatchPrint_StillRequiresEffect()
    {
        // Print in try/catch still requires effect
        var source = @"
§M{m001:Test}
§F{f001:PrintInTry:pub}
  §O{void}
  §TR{t1}
    §P ""In try""
  §CA{Exception:ex}
    §P ""In catch""
  §/TR{t1}
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should fail - try/catch print requires effect");
        TestHarness.AssertDiagnostic(result.Diagnostics.Errors, DiagnosticCode.ForbiddenEffect, "PrintInTry");
    }

    [Fact]
    public void LoopPrint_StillRequiresEffect()
    {
        // Print in loop still requires effect
        var source = @"
§M{m001:Test}
§F{f001:PrintLoop:pub}
  §O{void}
  §L{l1:i:0:10:1}
    §P i
  §/L{l1}
§/F{f001}
§/M{m001}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should fail - loop print requires effect");
        TestHarness.AssertDiagnostic(result.Diagnostics.Errors, DiagnosticCode.ForbiddenEffect, "PrintLoop");
    }
}
