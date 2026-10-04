using Calor.Compiler;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// #1413 (0.24 S2, repair R-NUM): the frozen registration classifies NUM-NARROW-ARITH and
/// NUM-LITERAL-OVERSIZE as unsupported-refused (divergences D1 and D2) and NUM-OVERFLOW-CHECKED
/// as assumed (checked-arithmetic). S1 found Proven on all three (F-B1/N1-001..005, class
/// required-demotion-absent). The sources instantiate the registered templates T-NARROW-ARITH,
/// T-LIT-OVERSIZE, and T-OVF-CHECKED-2/-3. The tests need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2NumericRefusalTests
{
    private static Calor.Compiler.Verification.Z3.ContractVerificationResult VerifySinglePostcondition(string source)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var result = Program.Compile(source, "case.calr", options);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        var function = Assert.Single(options.VerificationResults!.Functions);
        return Assert.Single(function.PostconditionResults);
    }

    [Theory]
    [InlineData("i8", "i16", "+", ">=", "INT:-70000")]   // T-NARROW-ARITH (R1-NUM-NARROW-ARITH-005 shape)
    [InlineData("u8", "u8", "*", "<=", "INT:70000")]
    [InlineData("u16", "i8", "-", ">", "INT:-70000")]
    public void NarrowArithmetic_IsRefused(string t1, string t2, string op, string relation, string bound)
    {
        var source = $$"""
            §M{m1:R1Case}
              §F{f1:Probe:pub} ({{t1}}:x, {{t2}}:y) -> i32
                §E{}
                §S ({{relation}} ({{op}} x y) {{bound}})
                §R INT:0
            """;
        Assert.Equal(ContractVerificationStatus.Unsupported, VerifySinglePostcondition(source).Status);
    }

    [Fact]
    public void Control_NarrowComparison_StaysProven()
    {
        const string source = """
            §M{m1:R1Case}
              §F{f1:Probe:pub} (i8:x) -> i32
                §E{}
                §S (<= x INT:127)
                §R INT:0
            """;
        Assert.Equal(ContractVerificationStatus.Proven, VerifySinglePostcondition(source).Status);
    }

    [Theory]
    [InlineData("<=", "9223372036854775807")]   // T-LIT-OVERSIZE (R1-NUM-LITERAL-OVERSIZE-003 shape)
    [InlineData("!=", "-2147483649")]
    public void OversizeIntLiteral_IsRefused(string relation, string big)
    {
        Assert.True(long.Parse(big) is > int.MaxValue or < int.MinValue);
        var source = $$"""
            §M{m1:R1Case}
              §F{f1:Probe:pub} (i64:x) -> i32
                §E{}
                §S ({{relation}} x INT:{{big}})
                §R INT:0
            """;
        Assert.Equal(ContractVerificationStatus.Unsupported, VerifySinglePostcondition(source).Status);
    }

    [Fact]
    public void Control_SameValueSpelledLong_StaysProven()
    {
        const string source = """
            §M{m1:R1Case}
              §F{f1:Probe:pub} (i64:x) -> i32
                §E{}
                §S (<= x LONG:9223372036854775807)
                §R INT:0
            """;
        Assert.Equal(ContractVerificationStatus.Proven, VerifySinglePostcondition(source).Status);
    }

    [Theory]
    [InlineData("u32", "UINT:1702287129", "(< (- x INT:1) x)")]          // T-OVF-CHECKED-2 (R1-NUM-OVERFLOW-CHECKED-007 shape)
    [InlineData("i64", "LONG:1", "(< (- x INT:1) x)")]
    [InlineData("i32", "INT:0", "(== (- (+ x INT:1) INT:1) x)")]        // T-OVF-CHECKED-3 needs x < MaxValue: not entailed
    public void EntailedCheckedArithmetic_IsAssumedNotProven(string type, string lower, string claim)
    {
        var source = $$"""
            §M{m1:R1Case}
              §F{f1:Probe:pub} ({{type}}:x) -> i32
                §E{}
                §Q (>= x {{lower}})
                §S {{claim}}
                §R INT:0
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.NotEqual(ProofStatus.Proven, outcome.Status);
        if (outcome.Status == ProofStatus.Assumed)
            Assert.Contains(Z3Verifier.CheckedArithmeticAssumption, outcome.Assumptions);
    }

    [Fact]
    public void EntailedCheckedArithmetic_IsAssumedWithTheCheckedArithmeticAssumption()
    {
        const string source = """
            §M{m1:R1Case}
              §F{f1:Probe:pub} (u32:x) -> i32
                §E{}
                §Q (>= x UINT:1702287129)
                §S (< (- x INT:1) x)
                §R INT:0
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Assumed, outcome.Status);
        Assert.Equal([Z3Verifier.CheckedArithmeticAssumption], outcome.Assumptions);
    }
}
