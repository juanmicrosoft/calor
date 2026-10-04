using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// #1413 (0.24 S2) regression witnesses for how text reaches Z3.
/// <list type="bullet">
/// <item>S1 STR-NULL-NONASCII (F-B1/N1-010, -011): string literals were sent as UTF-8 bytes, so
/// <c>"é"</c> had length 2 and <c>(&lt;= (len result) 1)</c> was refuted with a model the program
/// cannot produce.</item>
/// <item>S1 STR-OPS-COUNT-INDEX (F-B1/N1-009): <c>s.Substring(1, 1)</c> was total in the solver, so
/// the counterexample <c>s = ""</c> was one where the body throws.</item>
/// <item>#1493 (discovery, not in the registered sweep): Z3 symbol names took the binding's
/// ANSI marshaling, so on Windows two non-ASCII identifiers outside the code page became one Z3
/// constant. <see cref="DistinctNonAsciiParameters_AreDistinctSolverVariables"/> is the
/// end-to-end witness; it can only fail on Windows (the z3-consumer-matrix job), while
/// <see cref="SymbolNames_AreAsciiAndDistinct"/> pins the encoding on every platform.</item>
/// </list>
/// These need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2Z3TextEncodingTests
{
    private static Calor.Compiler.Verification.Z3.ContractVerificationResult VerifySinglePostcondition(string source)
    {
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

    [Fact]
    public void NonAsciiLiteral_HasItsDotNetLength()
    {
        // R1-STR-NULL-NONASCII-001 and -004 (the same source).
        Assert.Equal(1, "\u00e9".Length);
        const string source = """
            §M{m1:R1Case}
              §F{f1:Probe:pub} (bool:c) -> str
                §E{}
                §S (<= (len result) INT:1)
                §R (? c STR:"\u00e9" STR:"")
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Assumed, outcome.Status);
        Assert.Contains(Z3Verifier.StringModelAssumption, outcome.Assumptions);
    }

    [Fact]
    public void SurrogatePairLiteral_CountsUtf16CodeUnits()
    {
        Assert.Equal(4, "\ud83d\ude00ab".Length);
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (bool:c) -> i32
                §E{}
                §S (== result INT:4)
                §R (len (? c STR:"\ud83d\ude00ab" STR:"abcd"))
            """;
        Assert.Equal(ProofStatus.Assumed, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Fact]
    public void BackslashInLiteral_IsNotReadAsAZ3Escape()
    {
        // The Calor literal is the six characters \u{41}; Z3 must not read it as "A".
        Assert.Equal(6, "\\u{41}".Length);
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} () -> i32
                §E{}
                §S (== result INT:6)
                §R (len STR:"\\u{41}")
            """;
        Assert.Equal(ProofStatus.Assumed, VerifySinglePostcondition(source).EffectiveOutcome.Status);
        Assert.Equal(@"\u{5c}u{41}", ContractTranslator.ToZ3StringLiteral("\\u{41}"));
    }

    [Fact]
    public void SubstringCounterexample_IsOneWhereTheBodyReturns()
    {
        // R1-STR-OPS-COUNT-INDEX-003: s.Substring(1, 1) throws for s.Length < 2.
        Assert.Throws<ArgumentOutOfRangeException>(() => "".Substring(1, 1));
        const string source = """
            §M{m1:R1Case}
              §F{f1:Probe:pub} (str:s) -> i32
                §E{}
                §S (== result INT:4)
                §R (len (substr s INT:1 INT:1))
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Refuted, outcome.Status);
        var s = Assert.Single(outcome.Counterexample!.Bindings, binding => binding.Name == "s");
        Assert.NotEqual("\"\"", s.Value);
        Assert.NotEqual("\"\\u{0}\"", s.Value);
        var result = Assert.Single(outcome.Counterexample.Bindings, binding => binding.Name == "result");
        Assert.Equal("1", result.Value);
    }

    [Fact]
    public void SubstringFromCounterexample_IsOneWhereTheBodyReturns()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => "a".Substring(2));
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (str:s) -> i32
                §E{}
                §S (== result INT:5)
                §R (len (substr s INT:2))
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Refuted, outcome.Status);
        // The model's s has at least two characters (with its quotes, at least 4).
        var s = Assert.Single(outcome.Counterexample!.Bindings, binding => binding.Name == "s");
        Assert.True(s.Value.Length >= 4, s.Value);
    }

    [Fact]
    public void IndexOfWithStart_IsUnsupported()
    {
        // The emitted call ignores the start index, so the solver must not model one.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} () -> i32
                §E{}
                §S (== result INT:4)
                §R (indexof STR:"abcabc" STR:"b" INT:3 :ordinal)
            """;
        Assert.Equal(ProofStatus.Unsupported, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Fact]
    public void SubstringOverALocal_StaysAssumed()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (str:s) -> i32
                §E{}
                §Q (== s STR:"ab")
                §S (== result INT:1)
                §B{i:i32} INT:1
                §R (len (substr s i INT:1))
            """;
        Assert.Equal(ProofStatus.Assumed, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Fact]
    public void DollarNames_AreReservedForSyntheticVariables()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);
        Assert.True(translator.DeclareVariable("a", "i32[]"));
        Assert.False(translator.DeclareVariable("a$length", "u32"));
    }

    [Fact]
    public void SubstringInConditionalPosition_IsUnsupported()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (str:s, bool:c) -> i32
                §E{}
                §S (== result INT:4)
                §R (? c (len (substr s INT:1 INT:1)) INT:4)
            """;
        Assert.Equal(ProofStatus.Unsupported, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Theory]
    [InlineData("x", "x")]
    [InlineData("x$length", "x$length")]
    [InlineData("A b", "A b")]
    [InlineData("\u0436", "~0436")]
    [InlineData("a~", "a~007e")]
    [InlineData("\u00e9t\u00e9", "~00e9t~00e9")]
    public void Z3Name_KeepsSafeAsciiAndEscapesTheRest(string name, string expected)
        => Assert.Equal(expected, ContractTranslator.Z3Name(name));

    [Fact]
    public void Z3Name_IsInjectiveAcrossEscapedAndLiteralSpellings()
    {
        string[] names = ["\u0436", "\u0449", "?", "~0436", "a\u0436", "a~0436", "a~", "a~007e", "\ud83d\ude00", "\ud83d"];
        var encoded = names.Select(ContractTranslator.Z3Name).ToArray();
        Assert.Equal(names.Length, encoded.Distinct(StringComparer.Ordinal).Count());
        Assert.All(encoded, value => Assert.All(value, ch => Assert.InRange(ch, ' ', '~')));
    }

    [Fact]
    public void SymbolNames_AreAsciiAndDistinct()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);
        Assert.True(translator.DeclareVariable("\u0436", "i32"));
        Assert.True(translator.DeclareVariable("\u0449", "i32"));
        var first = translator.Variables["\u0436"].Expr.FuncDecl.Name.ToString();
        var second = translator.Variables["\u0449"].Expr.FuncDecl.Name.ToString();
        Assert.NotEqual(first, second);
        Assert.All(first + second, ch => Assert.InRange(ch, ' ', '~'));
    }

    [Fact]
    public void DistinctNonAsciiParameters_AreDistinctSolverVariables()
    {
        // #1493: if the two names collapsed into one Z3 constant (Windows, ANSI code page),
        // the precondition on ж would "prove" the postcondition on щ.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:ж, i32:щ) -> i32
                §E{}
                §Q (== ж INT:0)
                §S (== щ INT:0)
                §R INT:0
            """;
        Assert.Equal(ProofStatus.Refuted, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }
}
