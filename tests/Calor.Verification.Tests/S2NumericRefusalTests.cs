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
    [InlineData("i32", "INT:0", "(== (- (+ x INT:1) INT:1) x)")]        // T-OVF-CHECKED-3
    public void OverflowSensitiveCheckedArithmetic_IsAssumed(string type, string lower, string claim)
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
        Assert.Equal(ProofStatus.Assumed, outcome.Status);
        Assert.Equal([Z3Verifier.CheckedArithmeticAssumption], outcome.Assumptions);
    }

    [Theory]
    [InlineData("i32", "(> (+ x LONG:1) x)")]     // promoted to 64-bit: no i32 input overflows
    [InlineData("u32", "(>= (- x INT:-1) x)")]    // u32 - int promotes to long: no overflow
    public void Control_ArithmeticThatCannotOverflow_StaysProven(string type, string claim)
    {
        var source = $$"""
            §M{m1:R1Case}
              §F{f1:Probe:pub} ({{type}}:x) -> i32
                §E{}
                §S {{claim}}
                §R INT:0
            """;
        Assert.Equal(ContractVerificationStatus.Proven, VerifySinglePostcondition(source).Status);
    }

    [Fact]
    public void OversizeLiteralBuiltWithoutTheLexer_IsRefused()
    {
        // Review round 1: an SDK-built 32-bit literal holding a 64-bit value is the same D2 form.
        Assert.True(Z3ContextFactory.IsAvailable, "this witness needs Z3");
        var span = Calor.Compiler.Parsing.TextSpan.Empty;
        var literal = new Calor.Compiler.Ast.IntLiteralNode(span, long.MaxValue) { IsLong = false };
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var result = verifier.VerifyPostcondition(
            [("x", "i64")], "i32", [],
            new Calor.Compiler.Ast.EnsuresNode(span,
                new Calor.Compiler.Ast.BinaryOperationNode(span, Calor.Compiler.Ast.BinaryOperator.LessOrEqual,
                    new Calor.Compiler.Ast.ReferenceNode(span, "x"), literal),
                null, new Calor.Compiler.Ast.AttributeCollection()));
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [Fact]
    public void InferredWidthLiteral_HasItsOwnCacheKey_AndTheFormatEvictsOlderEntries()
    {
        var span = Calor.Compiler.Parsing.TextSpan.Empty;
        var hasher = new ContractHasher();
        var inferred = hasher.GetCanonicalExpression(new Calor.Compiler.Ast.IntLiteralNode(span, 3_000_000_000) { WidthInferred = true });
        var spelled = hasher.GetCanonicalExpression(new Calor.Compiler.Ast.IntLiteralNode(span, 3_000_000_000));
        Assert.NotEqual(inferred, spelled);
        // Entries of 1.21 and earlier may hold Proven for the forms R-NUM refuses or demotes.
        Assert.True(int.Parse(VerificationCacheEntry.CurrentFormatVersion.Split('.')[1]) >= 22,
            VerificationCacheEntry.CurrentFormatVersion);
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
