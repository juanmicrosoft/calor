using Calor.Compiler;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// Issue #874: a keyword argument in any position other than the trailing comparison-mode
/// slot of a recognized string operation must degrade to a diagnostic naming the keyword —
/// never an unhandled NullReferenceException, and never silently invalid emitted C#.
/// The enforcement lives at a single parser choke point (Parser.FilterKeywordArgs), so these
/// tests cover the routes the original three-site fix missed: binary operands, mid-position
/// and doubled string-op keywords, and ternary operands. Parser-side behavior — deliberately
/// not gated on Z3 availability, so the pins hold on Z3-less checkouts too.
/// </summary>
public class KeywordArgContractTests
{
    [Theory]
    // The original #874 repro: capitalized C# spelling parses as a generic call.
    [InlineData(@"
§M{m001:Repro}
  §F{f001:Check:pub} (str:s) -> bool
    §Q (StartsWith s STR:""hello"" :ignore-case)
    §R true", "ignore-case")]
    // Same shape in the postcondition position. Note: this must be a MULTI-WORD C#
    // spelling — StringOpExtensions.FromString lowercases its input, so capitalized
    // single-word names like `Contains` ARE recognized string ops (their trailing
    // keyword is legal mode consumption, not an escape); only spellings absent from
    // the table (`StartsWith` vs its key `starts`) reach the generic-call path.
    [InlineData(@"
§M{m001:Repro}
  §F{f001:Check:pub} (str:s) -> bool
    §S (EndsWith s STR:""x"" :ignore-case)
    §R true", "ignore-case")]
    // Binary operand: was 'Value cannot be null (Parameter right)' pre-choke-point.
    [InlineData(@"
§M{m001:Repro}
  §F{f001:Check:pub} (i32:x) -> bool
    §Q (>= x :foo)
    §R true", "foo")]
    // Mid-position keyword on a genuine string op: only the trailing slot is legal.
    [InlineData(@"
§M{m001:Repro}
  §F{f001:Check:pub} (str:s) -> bool
    §Q (contains s :ignore-case STR:""x"")
    §R true", "ignore-case")]
    // Doubled trailing keywords: the last is the mode, the second-to-last is rejected.
    [InlineData(@"
§M{m001:Repro}
  §F{f001:Check:pub} (str:s) -> bool
    §Q (contains s STR:""x"" :ordinal :ignore-case)
    §R true", "ordinal")]
    // Ternary operand: was 'Value cannot be null (Parameter whenTrue)'.
    [InlineData(@"
§M{m001:Repro}
  §F{f001:Check:pub} (i32:x) -> bool
    §Q (== (? (> x 0) :foo false) false)
    §R true", "foo")]
    // Implication consequent: ParseImplicationExpression consumes ParseLispArgument
    // DIRECTLY, bypassing the operator-loop choke point — the side-entrance route that
    // survived the first choke-point fix ('Value cannot be null (Parameter consequent)').
    [InlineData(@"
§M{m001:Repro}
  §F{f001:Check:pub} (i32:x) -> bool
    §Q (-> (> x 0) :foo)
    §R true", "foo")]
    public void KeywordArgOutsideStringOpMode_ProducesDiagnosticNamingKeyword_NotCrash(
        string source, string keyword)
    {
        // The defect was an unhandled NullReferenceException with no location — the one
        // outcome that carries no information. Compile must complete and name the keyword.
        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.Contains(result.Diagnostics, d => d.Message.Contains(keyword));
    }

    [Fact]
    public void SilentInvalidCodegenRoute_IsClosedByDiagnostic()
    {
        // Pre-choke-point, this compiled "successfully" and emitted `bool b = x >= ;` —
        // broken C# with zero diagnostics, worse than the crash.
        const string source = @"
§M{m001:Repro}
  §F{f001:Check:pub} (i32:x) -> void
    §B{b:bool} (>= x :foo)
    §P b";

        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.Contains(result.Diagnostics, d => d.Message.Contains("foo"));
    }

    [Fact]
    public void TrailingModeOnLowercaseStringOp_StaysLegal()
    {
        // The one legal position must keep working: no keyword-argument diagnostic, and
        // the mode reaches the emitted C# as a StringComparison overload.
        const string source = @"
§M{m001:Repro}
  §F{f001:Check:pub} (str:s) -> bool
    §R (contains s STR:""x"" :ignore-case)";

        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("not valid here"));
        Assert.Contains("StringComparison.OrdinalIgnoreCase", result.GeneratedCode);
    }

    private static CompilationOptions NoCache() => new()
    {
        VerifyContracts = true,
        VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
    };
}
