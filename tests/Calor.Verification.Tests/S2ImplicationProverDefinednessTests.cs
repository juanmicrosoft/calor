using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// #1413 (0.24 S2): prover-level witnesses for the implication definedness demotion
/// (S1 rows IMPL-ASSUMPTION-FORMS, IMPL-DIVISION-TOTALIZED). These need Z3 and fail,
/// not skip, without it.
/// </summary>
public sealed class S2ImplicationProverDefinednessTests
{
    private static IReadOnlyList<EnsuresNode> Postconditions(string postconditions)
    {
        var source = $$"""
            §M{m1:M}
              §F{f1:P:pub} (i32:x, i32:y) -> i32
                §E{}
            {{postconditions}}
                §R INT:1
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        return Assert.Single(module.Functions).Postconditions;
    }

    [Fact]
    public void PostconditionDirection_ThrowingInterfaceGuarantee_IsRefutedAtTheThrowingInput()
    {
        // The implementer guarantees result >= 0; the interface guarantees
        // (100 / result) >= -1, which throws at result = 0 (the solver totalizes 100/0 to -1).
        var posts = Postconditions("    §S (>= result INT:0)\n    §S (>= (/ INT:100 result) INT:-1)");
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var result = prover.CheckPostconditionStrengthening(
            [("x", "i32"), ("y", "i32")], "i32", posts[1].Condition, posts[0].Condition);
        // The implementer allows result = 0, where the interface's guarantee throws.
        Assert.Equal(ImplicationStatus.Disproven, result.Status);
        Assert.Contains(result.Outcome!.Counterexample!.Bindings, b => b.Name == "result" && b.Value == "0");
    }

    [Fact]
    public void PostconditionDirection_EntailedDivisor_StaysProven()
    {
        // result >= 1 rules out the zero divisor: (100 / result) >= 0 is proven.
        var posts = Postconditions("    §S (>= result INT:1)\n    §S (>= (/ INT:100 result) INT:0)");
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var result = prover.CheckPostconditionStrengthening(
            [("x", "i32"), ("y", "i32")], "i32", posts[1].Condition, posts[0].Condition);
        Assert.Equal(ImplicationStatus.Proven, result.Status);
    }

    [Fact]
    public void ConditionallyEvaluatedDivisor_IsAssumed()
    {
        // A divisor on the right of || is evaluated only on some inputs; no side condition is
        // modeled for it, so the (UNSAT) implication is only Assumed.
        var posts = Postconditions("    §S (>= x INT:1)\n    §S (|| (> x INT:0) (> (/ INT:1 y) INT:0))");
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var result = prover.ProveImplication(
            [("x", "i32"), ("y", "i32")], posts[0].Condition, posts[1].Condition);
        Assert.Equal(ImplicationStatus.Unknown, result.Status);
        Assert.Equal(ProofStatus.Assumed, result.Outcome!.Status);
        Assert.Contains(Z3Verifier.ContractExpressionDivisionAssumption, result.Outcome.Assumptions);
    }

    [Fact]
    public void ModelImpossibleNullOnlyAntecedent_IsAssumed()
    {
        // isempty(s) && s != "" holds only for s = null, which the solver cannot represent:
        // the solver sees an unsatisfiable antecedent and would "prove" anything.
        string? s = null;
        Assert.True(string.IsNullOrEmpty(s) && s != "");

        var source = """
            §M{m1:M}
              §F{f1:P:pub} (str:s, i32:x) -> i32
                §E{}
                §Q (&& (isempty s) (!= s STR:""))
                §Q (> x INT:0)
                §R INT:1
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        var pres = Assert.Single(module.Functions).Preconditions;
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var result = prover.ProveImplication(
            [("s", "str"), ("x", "i32")], pres[0].Condition, pres[1].Condition);
        Assert.NotEqual(ImplicationStatus.Proven, result.Status);
        Assert.Equal(ProofStatus.Assumed, result.Outcome!.Status);
        Assert.Contains(Z3Verifier.StringModelAssumption, result.Outcome.Assumptions);
    }

    [Theory]
    [InlineData("(== §IDX arr INT:-1 INT:0)")]                      // index out of range throws
    [InlineData("(== (substr s INT:-1 INT:1) STR:\"\")")]      // negative substring start throws
    public void ThrowingReferenceAntecedent_IsNotRefuted(string antecedent)
    {
        // Review round 3 witnesses: no input completes these antecedents, so A -> false holds
        // at runtime; the solver's total select/extract must not yield a counterexample.
        var source = $$"""
            §M{m1:M}
              §F{f1:P:pub} (i32[]:arr, str:s) -> i32
                §E{}
                §Q {{antecedent}}
                §R INT:1
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var pre = Assert.Single(Assert.Single(module.Functions).Preconditions);
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var result = prover.ProveImplication(
            [("arr", "i32[]"), ("s", "str")], pre.Condition, new BoolLiteralNode(pre.Span, false));
        Assert.NotEqual(ImplicationStatus.Disproven, result.Status);
    }
}
