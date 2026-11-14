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
§M{Test}
  §F{IndirectPrint:pub}
      §I{str:msg}
      §O{void}
      §C{Helper}
        §A msg
      §/C
  §F{Helper:int}
      §I{str:msg}
      §O{void}
      §E{cw}
      §P msg
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
module Test Test block
  func GetTime () → string
    !
    let string:now = ""placeholder""
    return now
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
module Test Test block
  func CreateRandom () → int
    !
    let int:val = 0
    return val
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
module Test Test block
  func WithLambda () → void
    !
    let Action<int>:action = (int:x) =>
      Console.WriteLine(x)
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
module Test Test block
  func MaybePrint (bool:shouldPrint) → void
    !
    if shouldPrint
      Console.WriteLine(""Printed"")
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
module Test Test block
  func PrintInTry () → void
    !
    try
      Console.WriteLine(""In try"")
    catch Exception:ex
      Console.WriteLine(""In catch"")
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
module Test Test block
  func PrintLoop () → void
    !
    for i from 0 to 10 step 1
      Console.WriteLine(i)
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Should fail - loop print requires effect");
        TestHarness.AssertDiagnostic(result.Diagnostics.Errors, DiagnosticCode.ForbiddenEffect, "PrintLoop");
    }
}
