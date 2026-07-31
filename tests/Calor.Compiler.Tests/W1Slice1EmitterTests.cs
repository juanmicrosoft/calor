using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// W1 Slice 1 T2 pins (#764 stopgap, wedge-w1-prereqs.md §1.1): the
/// postcondition lowering never silently skips or double-runs a runtime check.
/// Unlowerable body shapes (early/nested returns) emit the body untransformed
/// and report Calor1001; the `result` substitution is word-bounded so
/// identifiers containing "result" survive.
/// </summary>
public class W1Slice1EmitterTests
{
    private static (string Code, DiagnosticBag Diagnostics) EmitWithDiagnostics(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, null, diagnostics);
        var code = emitter.Emit(module);
        return (code, diagnostics);
    }

    [Fact]
    public void NestedReturn_ChecksOmitted_Calor1001Reported()
    {
        // A return inside §IF would BYPASS the postcondition check under the old
        // lowering (it stayed a real return while checks were emitted after the
        // body). The stopgap refuses lowering: real returns preserved, checks
        // omitted, Calor1001 reported.
        var source = @"
§M{m001:T}
  §F{f001:Pick:pub} (i32:x) -> i32
    §S (>= result 0)
    §IF{if1} (> x 10)
      §R x
    §R 0
";
        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.DoesNotContain("__result__", code);
        Assert.Contains("return x;", code);
    }

    [Fact]
    public void EarlyTopLevelReturn_ChecksOmitted_RealReturnPreserved()
    {
        // An early top-level return became `__result__ = ...` and execution FELL
        // THROUGH to the rest of the body under the old lowering — changed
        // execution order, double-computed effects. The stopgap keeps the real
        // early return and reports Calor1001.
        var source = @"
§M{m001:T}
  §F{f001:First:pub} (i32:x) -> i32
    §S (>= result 0)
    §R x
    §R 0
";
        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.DoesNotContain("__result__", code);
    }

    [Fact]
    public void SingleFinalReturn_LowersNormally_NoDiagnostic()
    {
        var source = @"
§M{m001:T}
  §F{f001:Id:pub} (i32:x) -> i32
    §S (== result x)
    §R x
";
        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.Contains("__result__", code);
        Assert.Contains("return __result__;", code);
    }

    [Fact]
    public void OperatorOverload_NestedReturn_ChecksOmitted_Calor1001()
    {
        // Review #833 M1: the operator-overload lowering was the fifth,
        // ungated site — a nested return silently bypassed the §S check and
        // the check used the raw `result` identifier with no substitution.
        var source = @"
§M{m001:T}
  §CL{c1:MyType}
      §OP{op001:+:pub}
        §I{i32:left}
        §I{i32:right}
        §O{i32}
        §S (>= result 0)
        §IF{if1} (> left 0)
          §R left
        §R right
      §/OP{op001}
";
        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.DoesNotContain("__result__", code);
    }

    [Fact]
    public void OperatorOverload_FinalReturn_SubstitutesResult()
    {
        var source = @"
§M{m001:T}
  §CL{c1:MyType}
      §OP{op001:+:pub}
        §I{i32:left}
        §I{i32:right}
        §O{i32}
        §S (>= result 0)
        §R left
      §/OP{op001}
";
        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.Contains("__result__", code);
        // The check must reference the captured local, not the raw identifier.
        Assert.DoesNotContain("!(result >= 0)", code);
    }

    [Fact]
    public void ResultSubstring_IdentifierNotCorrupted()
    {
        // The old textual Replace("result", "__result__") corrupted every
        // identifier CONTAINING "result": `resultCode` became `__result__Code`.
        // The word-bounded substitution leaves it intact.
        var source = @"
§M{m001:T}
  §F{f001:Echo:pub} (i32:resultCode) -> i32
    §S (== result resultCode)
    §R resultCode
";
        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.DoesNotContain("__result__Code", code);
        Assert.Contains("resultCode", code);
    }
}
