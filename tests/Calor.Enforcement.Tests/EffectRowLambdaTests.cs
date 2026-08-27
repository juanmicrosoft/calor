using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Design-doc pin <b>P14</b> — §5, the fate of <c>§LAM</c>'s <c>§E</c>: it is the
/// lambda's DECLARED row, checked against ρ_body exactly as a function's <c>§E</c>
/// is checked against its body.
/// </summary>
/// <remarks>
/// <para>Before this slice the annotation was parsed, carried into the bound tree,
/// and <b>discarded</b> — executed case <b>X7</b> compiled with a <c>§E{}</c> over
/// an impure body and nothing observed it. Roadmap §4.1 forbids leaving it
/// parsed-and-ignored, so slice b computes ρ_body in the effect pass and gives it
/// two consumers: this check, and an un-annotated lambda's TYPE row at the six
/// binding sites of §4.4.</para>
///
/// <para><b>Discriminating revert</b> (P14's, verbatim): restore
/// <c>InferFromLambda</c> to ignore <c>lambda.Effects</c> — that is, drop the
/// <c>RecordLambdaBody</c> sink — and every test in this file goes silent, because
/// <c>_lambdaBodyRows</c> is then empty and ρ_body is Unknown everywhere.</para>
///
/// <para><b>Zero committed <c>.calr</c> is affected.</b> None of the corpus's nine
/// <c>§LAM</c> occurrences carries a <c>§E</c> (§5), which is why this can be an
/// error rather than a warning.</para>
/// </remarks>
public class EffectRowLambdaTests
{
    [Fact]
    public void LambdaDeclaredRow_NarrowerThanBody_IsError()
    {
        // The lambda declares §E{} and its body prints. §5: fits(ρ_body, ρ_decl)
        // is DoesNotFit, which is Calor0410 at the §E SPAN, per effect, in
        // today's shape. This is executed case X7 becoming audible.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Run:pub} () -> void
                §E{cw}
                §B{f:Func<i32,i32>} §LAM{lam1:x:i32} §E{}
                  §P x
                  §R x
                §/LAM{lam1}
                §P INT:1
            """);

        var reported = Assert.Single(result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Equal(
            "Lambda 'lam1' in 'Run' uses effect 'cw' but does not declare it",
            reported.Message);
        // The span is the §E's, not the function's: the contract the author broke
        // is the lambda's, and pointing at `Run` would name the wrong declaration.
        Assert.Equal(4, reported.Span.Line);
    }

    [Fact]
    public void LambdaDeclaredRow_WideEnough_Compiles()
    {
        // The `_Compiles` polarity: same body, honest declaration.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Run:pub} () -> void
                §E{cw}
                §B{f:Func<i32,i32>} §LAM{lam1:x:i32} §E{cw}
                  §P x
                  §R x
                §/LAM{lam1}
                §P INT:1
            """);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              || d.Code == DiagnosticCode.EffectRowUnknown
              || d.Code == DiagnosticCode.EffectRowMismatch);
    }

    [Fact]
    public void LambdaDeclaredRow_CannotTell_IsCalor0425()
    {
        // §5's second arm. The body makes a call the pass cannot resolve, so
        // ρ_body is Unknown, so `fits` is CannotTell — and CannotTell is
        // Calor0425 at every site, including this one. Never a silent pass.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Run:pub} () -> void
                §E{}
                §B{f:Func<i32,i32>} §LAM{lam1:x:i32} §E{}
                  §R §C{SomeUnresolvableThing.Compute} §A x §/C
                §/LAM{lam1}
            """);

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowUnknown
              && d.Message.Contains("Lambda 'lam1' in 'Run'")
              && d.Message.Contains("inferred body row"));
    }

    [Fact]
    public void LambdaOmittedRow_IsInferred()
    {
        // §5 — "If §E is absent the type carries ρ_body and nothing is reported."
        // Slice a made this Unknown, which fits nothing, so a §B taking a
        // row-less lambda could never be decided. It can now.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Run:pub} () -> void
                §E{cw}
                §B{f:Func<i32,i32>} §E{cw} §LAM{lam1:x:i32}
                  §P x
                  §R x
                §/LAM{lam1}
                §P INT:1
            """);

        // ρ_body is {cw}, the declared binding row is {cw}: a decided Fits, so
        // NO Calor0425. Under slice a this was CannotTell.
        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowUnknown
              || d.Code == DiagnosticCode.EffectRowMismatch);
    }

    [Fact]
    public void LambdaOmittedRow_InferredRowIsCheckedAtTheBindingSite()
    {
        // The same shape with the binding NARROWED. ρ_body is {cw}, the binding
        // declares pure, so site 1 is a decided DoesNotFit — Calor0424. This is
        // the pin that proves the inferred row is really the SOURCE row and not
        // merely an absence of Unknown.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Run:pub} () -> void
                §E{cw}
                §B{f:Func<i32,i32>} §E{} §LAM{lam1:x:i32}
                  §P x
                  §R x
                §/LAM{lam1}
                §P INT:1
            """);

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              && d.Message.Contains("Initializer of binding 'f'")
              && d.Message.Contains("Extra effect(s): cw"));
    }

    [Fact]
    public void LambdaTypeCarriesDeclaredNotInferred()
    {
        // §5's declaration boundary: when the lambda IS annotated, the type that
        // leaves it is the DECLARED row, not the body's. Here the body is pure
        // and the declaration is {cw}; the binding declares pure. If the type
        // carried ρ_body the site would be a silent Fits — it must be a
        // Calor0424, because the declaration is the contract.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Run:pub} () -> void
                §E{}
                §B{f:Func<i32,i32>} §E{} §LAM{lam1:x:i32} §E{cw}
                  §R x
                §/LAM{lam1}
            """);

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              && d.Message.Contains("Initializer of binding 'f'")
              && d.Message.Contains("Extra effect(s): cw"));
    }
}
