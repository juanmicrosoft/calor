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
            module R1Case
              func Probe public (bool:c) → string
                !
                => result.Length <= 1
                return c ? "\u00e9" : ""
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
            module M
              func Probe public (bool:c) → int
                !
                => result == 4
                return (c ? "\ud83d\ude00ab" : "abcd").Length
            """;
        Assert.Equal(ProofStatus.Assumed, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Fact]
    public void BackslashInLiteral_IsNotReadAsAZ3Escape()
    {
        // The Calor literal is the six characters \u{41}; Z3 must not read it as "A".
        Assert.Equal(6, "\\u{41}".Length);
        const string source = """
            module M
              func Probe public () → int
                !
                => result == 6
                return "\\u{41}".Length
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
            module R1Case R1Case block
              func Probe (string:s) → int
                !
                => (result == 4)
                return ((s).Substring(1, 1)).Length
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
            module M M block
              func Probe (string:s) → int
                !
                => (result == 5)
                return ((s).Substring(2)).Length
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Refuted, outcome.Status);
        // The model's s (Z3 rendering, \u{h} escapes decoded) has at least two characters.
        var s = Assert.Single(outcome.Counterexample!.Bindings, binding => binding.Name == "s");
        var decoded = System.Text.RegularExpressions.Regex.Replace(
            s.Value.Trim('"'), @"\\u\{([0-9a-fA-F]+)\}", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        Assert.True(decoded.Length >= 2, s.Value);
    }

    [Fact]
    public void SubstringOverALocal_WithoutPrecondition_IsNotRefuted()
    {
        // Review round 2 witness: every normal return produces 1; s = "" throws in .NET.
        const string source = """
            module M M block
              func Probe (string:s) → int
                !
                => (result == 1)
                let int:i = 1
                return ((s).Substring(i, 1)).Length
            """;
        Assert.NotEqual(ProofStatus.Refuted, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Fact]
    public void NullSatisfiablePrecondition_IsNotReportedVacuous()
    {
        // Review round 2 witness: s = null satisfies the precondition at runtime; the solver's
        // non-null strings make it unsatisfiable.
        string? s = null;
        Assert.True(string.IsNullOrEmpty(s) && s != "");
        const string source = """
            module M
              func Probe public (string:s | ("\u00e9".Length != 1 || (string.IsNullOrEmpty(#) && # != ""))) → int
                !
                => result == 1
                return 0
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.False(outcome.Status == ProofStatus.Proven && outcome.IsVacuous);
        Assert.Equal(ProofStatus.Unsupported, outcome.Status);
    }

    [Fact]
    public void DollarQuantifierVariables_AreRefused()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);
        Assert.True(translator.DeclareVariable("a", "i32[]"));
        var diagnostics = new DiagnosticBag();
        var module = new Calor.Compiler.Parsing.Parser(new Calor.Compiler.Parsing.Lexer("""
            module M
              func P public () → int
                !
                => forall int:q where true : q >= 0
                return 0
            """, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var forall = (Calor.Compiler.Ast.ForallExpressionNode)module.Functions[0].Postconditions[0].Condition;
        // Control: the quantifier as written translates.
        Assert.NotNull(translator.TranslateBoolExpr(forall));
        // Witness: binder and body both renamed to the array's synthetic length symbol.
        var body = Assert.IsType<Calor.Compiler.Ast.BinaryOperationNode>(
            Assert.IsType<Calor.Compiler.Ast.ImplicationExpressionNode>(forall.Body).Consequent);
        var renamed = new Calor.Compiler.Ast.ForallExpressionNode(
            forall.Span,
            [new Calor.Compiler.Ast.QuantifierVariableNode(forall.Span, "a$length", "u32")],
            new Calor.Compiler.Ast.BinaryOperationNode(body.Span, body.Operator,
                new Calor.Compiler.Ast.ReferenceNode(body.Span, "a$length"), body.Right));
        Assert.Null(translator.TranslateBoolExpr(renamed));
    }

    [Fact]
    public void IndexOfWithStart_IsUnsupported()
    {
        // The emitted call ignores the start index, so the solver must not model one.
        const string source = """
            module M M block
              func Probe () → int
                !
                => (result == 4)
                return ("abcabc").IndexOf("b", 3, StringComparison.Ordinal)
            """;
        Assert.Equal(ProofStatus.Unsupported, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Fact]
    public void SubstringOverALocal_IsUnsupported()
    {
        const string source = """
            module M M block
              func Probe (string:s | (# == "ab")) → int
                !
                => (result == 1)
                let int:i = 1
                return ((s).Substring(i, 1)).Length
            """;
        // Review round 3: a range over a body local is not modeled (no substitution).
        // 0.30: pin the reason, so a Substring that is never lowered cannot pass as this case.
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Unsupported, outcome.Status);
        Assert.Contains("index range could not be modeled", outcome.Reason);
    }

    [Fact]
    public void MixedWidthLocalInASubstringStart_IsNotRefuted()
    {
        // Review round 3 witness: x + i is 4294967296L at runtime (start 1), and "".Substring(1, 0)
        // throws; substituting the literal 1 for i would wrap to start 0.
        Assert.Throws<ArgumentOutOfRangeException>(() => "".Substring(1, 0));
        const string source = """
            module M M block
              func Probe (uint:x | (# == 4294967295U), string:s | (# == "")) → int
                !
                => (result == 1)
                let int:i = 1
                return ((s).Substring((((x + i) > 4294967295L) ? 1 : 0), 0)).Length
            """;
        Assert.NotEqual(ProofStatus.Refuted, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Fact]
    public void EagerSubstringInitializerReadInBothArms_StaysAssumed()
    {
        // Review round 3 witness: the substring runs once, at the binding, not in the arms.
        const string source = """
            module M M block
              func Probe (string:s | (# == "ab"), bool:c) → int
                !
                => (result == 1)
                let string:t = (s).Substring(1, 1)
                return (c ? t.Length : t.Length)
            """;
        Assert.Equal(ProofStatus.Assumed, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }

    [Fact]
    public void LiteralOnlyStringsUnderABooleanSymbol_StayVacuous()
    {
        // Review round 3 witness: every string operand is a non-null literal, so the
        // precondition is unsatisfiable at runtime too.
        const string source = """
            module M
              func Probe public (bool:c | ((# ? "a" : "b") == "c")) → int
                !
                => result == 1
                return 0
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.NotEqual(ProofStatus.Unsupported, outcome.Status);
        Assert.True(outcome.IsVacuous);
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
            module M M block
              func Probe (string:s, bool:c) → int
                !
                => (result == 4)
                return (c ? ((s).Substring(1, 1)).Length : 4)
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Unsupported, outcome.Status);
        Assert.Contains("conditionally-evaluated position", outcome.Reason);
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
            module M
              func Probe public (int:ж | (# == 0), int:щ) → int
                !
                => щ == 0
                return 0
            """;
        Assert.Equal(ProofStatus.Refuted, VerifySinglePostcondition(source).EffectiveOutcome.Status);
    }
}
