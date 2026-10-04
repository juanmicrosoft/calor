using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (0.24 S2, amendment 1.3.0): visible demotions for the two review-found discoveries
/// D-OBL-PROOF-GETTER and D-OBL-THROWING-PREDECESSOR. Both were spurious refutations (a Failed
/// compile error on a correct program); the counterexample is now withheld: the obligation is
/// Unsupported (Calor1124) and its guard is kept. The tests need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2ObligationResidualTests
{
    private static (List<Obligation> Obligations, string CSharp, List<string> Codes) Solve(string source, bool typeCheck = true)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerifyRefinements = true,
            ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
            EnableTypeChecking = typeCheck,
            EnforceEffects = typeCheck,
        };
        var result = Program.Compile(source, "case.calr", options);
        Assert.True(options.ObligationResults != null, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        return (options.ObligationResults!.Obligations.ToList(), result.GeneratedCode,
            result.Diagnostics.Select(d => d.Code).ToList());
    }

    private static Obligation Proof(List<Obligation> obligations, string id)
        => Assert.Single(obligations, o => o.Kind == ObligationKind.ProofObligation && o.SourceProofId == id);

    private static void AssertWithheld(Obligation obligation)
    {
        Assert.Equal(ObligationStatus.Unsupported, obligation.Status);
        Assert.Null(obligation.Outcome?.Counterexample);
    }

    [Fact]
    public void ProofReadingAPropertyThatHidesAField_IsWithheld()
    {
        // D-OBL-PROOF-GETTER: the getter always returns 0, but the solver models the inherited
        // field Base.Trigger as a free value.
        const string source = """
            §M{m1:M}
              §CL{c1:Base:pub}
                §FLD{i32:Trigger:pub}
              §CL{c2:Box:pub}
                §EXT{Base}
                §PROP{pr1:Trigger:i32:pub}
                  §GET
                    §R INT:0
              §F{f1:Probe:pub} (Box:box) -> void
                §E{}
                §PROOF{p1:claim} (== box.Trigger INT:0)
            """;
        var (obligations, csharp, codes) = Solve(source);
        AssertWithheld(Proof(obligations, "p1"));
        Assert.Contains("box.Trigger == 0", csharp);   // the runtime check stays
        Assert.Contains("Calor1124", codes);           // reported as unsupported
        Assert.DoesNotContain("Calor1140", codes);     // no failed-proof error
    }

    [Fact]
    public void EntryRefinementReadingAProperty_IsNotRefuted()
    {
        // Review round 1: the property check applies to entry obligations too.
        const string source = """
            §M{m1:M}
              §CL{c1:Base:pub}
                §FLD{i32:Trigger:pub}
              §CL{c2:Box:pub}
                §EXT{Base}
                §PROP{pr1:Trigger:i32:pub}
                  §GET
                    §R INT:0
              §F{f1:Probe:priv}
                §I{Box:box} | (== box.Trigger INT:0)
                §O{void}
                §E{}
            """;
        AssertWithheld(Assert.Single(Solve(source).Obligations, o => o.Kind == ObligationKind.RefinementEntry));
    }

    [Fact]
    public void ProofAfterAStatementThatThrowsOnTheModel_IsWithheld()
    {
        // D-OBL-THROWING-PREDECESSOR: x = 0 throws at the division, so no execution reaches the
        // claim with x = 0, yet the solver's model is x = 0.
        Assert.Throws<DivideByZeroException>(() => { var x = 0; return 10 / x; });
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §B{q:i32} (/ INT:10 x)
                §PROOF{p1:claim} (!= x INT:0)
                §R q
            """;
        AssertWithheld(Proof(Solve(source).Obligations, "p1"));
    }

    [Fact]
    public void ProofAfterAnEarlierProofGuard_IsWithheld()
    {
        // A retained earlier §PROOF guard throws when false, so its negation never reaches p2.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §PROOF{p1:first} (> x INT:5)
                §PROOF{p2:second} (> x INT:0)
                §R x
            """;
        AssertWithheld(Proof(Solve(source).Obligations, "p2"));
    }

    [Fact]
    public void ProofAfterARefinedBindingGuard_IsWithheld()
    {
        // Review round 1: the compiler-inserted refinement guard on q throws for x = 0.
        const string source = """
            §M{m1:M}
              §RTYPE{r1:NZ:i32} (!= # INT:0)
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §B{q:NZ} x
                §PROOF{p1:claim} (!= x INT:0)
                §R x
            """;
        // The type checker rejects i32 -> NZ without a cast; the emitter's guard is what runs.
        AssertWithheld(Proof(Solve(source, typeCheck: false).Obligations, "p1"));
    }

    [Fact]
    public void ComparisonsAfterOperatorOverloads_CountAsPossiblyThrowing()
    {
        // Review round 1: with a user-defined operator in the module, a comparison may run it.
        const string source = """
            §M{m1:M}
              §CL{c1:Box:pub}
                §FLD{i32:Value:pub}
                §OP{op1:+:pub}
                  §I{Box:left}
                  §I{Box:right}
                  §O{Box}
                  §R left
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §B{q:bool} (< x INT:3)
                §PROOF{p1:claim} (!= x INT:0)
                §R x
            """;
        AssertWithheld(Proof(Solve(source).Obligations, "p1"));
    }

    [Fact]
    public void ThenBodyIgnoresALaterThrowingElseIfCondition()
    {
        // Review round 1 control: the elseif condition is never evaluated on the then path, so
        // x = 1 genuinely reaches and violates the claim.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §IF{if1} (> x INT:0)
                  §PROOF{p1:claim} (> x INT:1)
                §EI (> (/ INT:10 x) INT:1)
                  §R INT:2
                §R x
            """;
        Assert.Equal(ObligationStatus.Failed, Proof(Solve(source).Obligations, "p1").Status);
    }

    [Fact]
    public void ObligationAfterAThrowingOperandInTheSameStatement_IsWithheld()
    {
        // Review round 1: i = 1 throws at the division before the indexed access is evaluated.
        const string source = """
            §M{m1:M}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f1:Probe:priv}
                §I{Sized:items}
                §I{i32:n}
                §I{i32:i}
                §O{i32}
                §E{}
                §Q (== n INT:1)
                §Q (&& (>= i INT:0) (<= i INT:1))
                §R (+ (/ INT:10 (- i INT:1)) §IDX items i)
            """;
        var index = Assert.Single(Solve(source, typeCheck: false).Obligations, o => o.Kind == ObligationKind.IndexBounds);
        AssertWithheld(index);
    }

    [Fact]
    public void ProofReadingANestedClassProperty_IsWithheld()
    {
        // Review round 1: properties of nested types count as well.
        const string source = """
            §M{m1:M}
              §CL{c1:Base:pub}
                §FLD{i32:Trigger:pub}
              §CL{c0:Outer:pub}
                §CL{c2:Box:pub}
                  §EXT{Base}
                  §PROP{pr1:Trigger:i32:pub}
                    §GET
                      §R INT:0
              §F{f1:Probe:pub} (Outer.Box:box) -> void
                §E{}
                §PROOF{p1:claim} (== box.Trigger INT:0)
            """;
        AssertWithheld(Proof(Solve(source, typeCheck: false).Obligations, "p1"));
    }

    [Fact]
    public void ProofAfterAMutableRebindOfARefinedParameter_IsWithheld()
    {
        // Review round 2: the unannotated rebind of q runs q's refinement guard, which throws at x = 0.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub}
                §I{i32:q} | (!= # INT:0)
                §I{i32:x}
                §O{i32}
                §E{}
                §B{~q} x
                §PROOF{p1:claim} (!= x INT:0)
                §R q
            """;
        AssertWithheld(Proof(Solve(source, typeCheck: false).Obligations, "p1"));
    }

    [Fact]
    public void ProofAfterAConversionWithAUserDefinedConversionInTheModule_IsWithheld()
    {
        // Review round 2: with a conversion operator declared, a plain binding may run it.
        const string source = """
            §M{m1:M}
              §CL{c1:Box:pub}
                §FLD{i32:Value:pub}
                §OP{op1:implicit:pub}
                  §I{Box:value}
                  §O{i32}
                  §R (/ INT:10 value.Value)
              §F{f1:Probe:pub} (Box:box, i32:x) -> i32
                §E{}
                §B{q:i32} box
                §PROOF{p1:claim} (!= box.Value INT:0)
                §R q
            """;
        AssertWithheld(Proof(Solve(source, typeCheck: false).Obligations, "p1"));
    }

    [Fact]
    public void MigratedOperatorMethods_CountAsOperatorOverloads()
    {
        // Review round 2: C# migration declares operators as op_* methods.
        const string source = """
            §M{m1:M}
              §CL{c1:Box:pub}
                §FLD{i32:Value:pub}
                §MT{mt1:op_LessThan:pub:stat}
                  §I{Box:left}
                  §I{i32:right}
                  §O{bool}
                  §R (> (/ INT:10 right) INT:0)
                §MT{mt2:op_GreaterThan:pub:stat}
                  §I{Box:left}
                  §I{i32:right}
                  §O{bool}
                  §R true
              §F{f1:Probe:pub} (Box:box, i32:x) -> i32
                §E{}
                §B{q:bool} (< box x)
                §PROOF{p1:claim} (!= x INT:0)
                §R x
            """;
        AssertWithheld(Proof(Solve(source, typeCheck: false).Obligations, "p1"));
    }

    [Fact]
    public void ObligationInsideAFoldedMultiOperandOperation_IsWithheld()
    {
        // Review round 2: (+ a b c) folds into nested additions spanning the whole expression;
        // 2147483647 + i throws at i = 1 before the indexed access.
        const string source = """
            §M{m1:M}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f1:Probe:priv}
                §I{Sized:items}
                §I{i32:n}
                §I{i32:i}
                §O{i32}
                §E{}
                §Q (== n INT:1)
                §Q (&& (>= i INT:0) (<= i INT:1))
                §R (+ INT:2147483647 i §IDX items i)
            """;
        AssertWithheld(Assert.Single(Solve(source, typeCheck: false).Obligations, o => o.Kind == ObligationKind.IndexBounds));
    }

    [Fact]
    public void ProofAfterAPreconditionThatMayThrow_IsWithheld()
    {
        // Review round 2: x = 2147483647 satisfies the modeled precondition, but its checked
        // addition throws before the body; only x = 0 enters, and it satisfies the claim.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §Q (|| (== x INT:0) (== (+ x INT:1) INT:-2147483648))
                §PROOF{p1:claim} (< x INT:2147483647)
                §R x
            """;
        AssertWithheld(Proof(Solve(source).Obligations, "p1"));
    }

    [Fact]
    public void ThenBodyIgnoresALaterElseIfCall()
    {
        // Review round 2 control: the later call is never evaluated on the then path.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §IF{if1} (> x INT:0)
                  §PROOF{p1:claim} (> x INT:1)
                §EI (> §C{Math.Abs} §A x §/C INT:1)
                  §R INT:2
                §R x
            """;
        Assert.Equal(ObligationStatus.Failed, Proof(Solve(source, typeCheck: false).Obligations, "p1").Status);
    }

    [Fact]
    public void EntryRefinementAfterAThrowingPrecondition_IsWithheld()
    {
        // Review round 3: the precondition runs before the parameter guard; only x = 0 completes it.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv}
                §I{i32:x} | (< # INT:2147483647)
                §O{void}
                §E{}
                §Q (|| (== x INT:0) (== (+ x INT:1) INT:-2147483648))
            """;
        AssertWithheld(Assert.Single(Solve(source).Obligations, o => o.Kind == ObligationKind.RefinementEntry));
    }

    [Fact]
    public void ProofAfterALengthOfADefaultNullLocal_IsWithheld()
    {
        // Review round 3: an initializer-free array local is null, so its length throws first.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:x) -> void
                §E{}
                §B{items:i32[]}
                §B{length:i32} §LEN items
                §PROOF{p1:claim} (> x INT:0)
            """;
        AssertWithheld(Proof(Solve(source, typeCheck: false).Obligations, "p1"));
    }

    [Fact]
    public void ProofReadingALengthProperty_IsWithheld()
    {
        // Review round 3: §LEN emits `.Length`, here a getter that always returns 0.
        const string source = """
            §M{m1:M}
              §CL{c1:Box:pub}
                §PROP{pr1:Length:i32:pub}
                  §GET
                    §R INT:0
              §F{f1:Probe:pub} (Box:box) -> void
                §E{}
                §PROOF{p1:claim} (== §LEN box INT:0)
            """;
        var proof = Proof(Solve(source, typeCheck: false).Obligations, "p1");
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ProofWithRawCSharpMembersInTheModule_IsWithheld()
    {
        // Review round 3: a raw C# property hiding the inherited field is invisible to the scan.
        const string source = """
            §M{m1:M}
              §CL{c1:Base:pub}
                §FLD{i32:Trigger:pub}
              §CL{c2:Box:pub}
                §EXT{Base}
                §CSHARP{public new int Trigger => 0;}§/CSHARP
              §F{f1:Probe:pub} (Box:box) -> void
                §E{}
                §PROOF{p1:claim} (== box.Trigger INT:0)
            """;
        var proof = Proof(Solve(source, typeCheck: false).Obligations, "p1");
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ProofAfterAThrowingConstructorInitializer_IsWithheld()
    {
        // Review round 3: the base(...) argument (/ 10 x) runs before the body; x = 0 never reaches p1.
        const string source = """
            §M{m1:M}
              §CL{c1:Base:pub}
                §CTOR{ct0:pub}
                  §I{i32:v}
              §CL{c2:Derived:pub}
                §EXT{Base}
                §CTOR{ct1:pub}
                  §I{i32:x}
                  §BASE
                    §A (/ INT:10 x)
                  §/BASE
                  §PROOF{p1:claim} (!= x INT:0)
            """;
        AssertWithheld(Proof(Solve(source, typeCheck: false).Obligations, "p1"));
    }

    [Fact]
    public void Control_ThenBodyGuardStillDischargesTheSameClaim()
    {
        // Review round 3: ordering the state-change check restores the then-body guard fact, so a
        // claim it implies is (correctly) Discharged.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §IF{if1} (> x INT:0)
                  §PROOF{p1:claim} (> x INT:0)
                §EI (> §C{Math.Abs} §A x §/C INT:1)
                  §R INT:2
                §R x
            """;
        Assert.Equal(ObligationStatus.Discharged, Proof(Solve(source, typeCheck: false).Obligations, "p1").Status);
    }

    [Fact]
    public void Control_NonThrowingPredecessor_StillRefutedWithAReachingModel()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §B{q:i32} x
                §PROOF{p1:claim} (> x INT:0)
                §R q
            """;
        Assert.Equal(ObligationStatus.Failed, Proof(Solve(source).Obligations, "p1").Status);
    }

    [Fact]
    public void Control_ProofReadingAField_StillRefuted()
    {
        const string source = """
            §M{m1:M}
              §CL{c1:Box:pub}
                §FLD{i32:Value:pub}
              §F{f1:Probe:pub} (Box:box) -> void
                §E{}
                §PROOF{p1:claim} (> box.Value INT:0)
            """;
        Assert.Equal(ObligationStatus.Failed, Proof(Solve(source).Obligations, "p1").Status);
    }
}
