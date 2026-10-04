using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (0.24 S2) regression witnesses for the obligation solver's state model, from S1
/// rows OBL-MUTATION-KILL (F-B1/N1-014..019), OBL-BRANCH-FACTS (-012, -013),
/// OBL-REFINEMENT-RETURN (-020), OBL-SUBTYPE (-021), and OBL-SELFREF (-022..026).
/// The sources are the registered S1 cases, verbatim.
///
/// - False proof: a §Q fact survived `§ASSIGN x` and discharged `§PROOF` on the new value,
///   deleting its guard (R1-OBL-MUTATION-KILL-001/-003).
/// - Spurious refutations (compile errors on correct programs, or a wrong counterexample):
///   models that ignored a reassignment, an else-branch guard, the returned value, or a
///   parameter's named refinement type.
/// The tests need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2ObligationStateTests
{
    private static (List<Obligation> Obligations, string CSharp, bool HasErrors) Solve(string source)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerifyRefinements = true,
            ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var result = Program.Compile(source, "case.calr", options);
        return (options.ObligationResults!.Obligations.ToList(), result.GeneratedCode, result.HasErrors);
    }

    private static Obligation Single(List<Obligation> obligations, ObligationKind kind)
        => Assert.Single(obligations, obligation => obligation.Kind == kind);

    // ---------------------------------------------------------------- false proof

    [Theory]
    [InlineData("(> x INT:-1)", "INT:-408872766")]       // R1-OBL-MUTATION-KILL-001
    [InlineData("(== x INT:2147483647)", "INT:0")]        // R1-OBL-MUTATION-KILL-003
    public void PreconditionFactKilledByAssignment_IsNotDischarged(string predicate, string assigned)
    {
        var source = $$"""
            §M{m1:R1Case}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §Q {{predicate}}
                §ASSIGN x {{assigned}}
                §PROOF{p1:claim} {{predicate}}
                §R x
            """;
        // Oracle witnesses (x = 0 and x = int.MaxValue satisfy §Q): after the assignment
        // the claim is false, so a Discharged verdict here is a false proof.
        Assert.False(-408872766 > -1);
        Assert.False(0 == int.MaxValue);

        var (obligations, csharp, _) = Solve(source);
        var proof = Single(obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
        Assert.Equal(ProofStatus.Unsupported, proof.Outcome!.Status);
        Assert.Contains("may change", proof.Outcome.Reason);
        // The runtime guard stays in the default (elision-enabled) emission.
        Assert.Contains("Proof obligation [p1", csharp);
    }

    [Fact]
    public void RefArgumentKillsPreconditionFact()
    {
        const string source = """
            §M{m1:M}
              §F{g1:Bump:priv} (i32:v:ref) -> void
                §E{}
                §ASSIGN v INT:-5
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §Q (> x INT:0)
                §C{Bump} §A{ref} x §/C
                §PROOF{p1:claim} (> x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void AliasedRefParameterWriteKillsPreconditionFact()
    {
        // Probe(ref a, ref a) passes one variable twice: writing y also writes x.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:x:ref, i32:y:ref) -> i32
                §E{}
                §Q (> x INT:0)
                §ASSIGN y INT:-5
                §PROOF{p1:claim} (> x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ArrayElementStoreKillsPreconditionFact()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} ([i32]:a) -> i32
                §E{mut}
                §Q (> §IDX a INT:0 INT:0)
                §SETIDX{a} INT:0 INT:-5
                §PROOF{p1:claim} (> §IDX a INT:0 INT:0)
                §R INT:0
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
    }

    // Review round 1 witnesses: each was a false Discharged (guard elided) or a lost proof.
    [Theory]
    [InlineData("""
        §M{m1:M}
          §F{f1:Probe:priv} (i32:x) -> i32
            §E{}
            §Q (> x INT:0)
            §B{sink:i32} §CS{(x = -5)}
            §PROOF{p1:claim} (> x INT:0)
            §R x
        """)]
    [InlineData("""
        §M{m1:M}
          §F{f1:Probe:priv} (i32:x:ref, i32:y:ref) -> i32
            §E{}
            §IF{if1} (< x INT:0)
              §R INT:0
            §EL
              §ASSIGN y INT:-5
              §PROOF{p1:claim} (>= x INT:0)
            §R x
        """)]
    [InlineData("""
        §M{m1:M}
          §F{f1:Probe:priv} (i32:x) -> i32
            §E{unsafe}
            §Q (> x INT:0)
            §UNSAFE{u1}
              §B{ptr:i32*} §ADDR x
              §ASSIGN §DEREF ptr INT:-5
            §PROOF{p1:claim} (> x INT:0)
            §R x
        """)]
    [InlineData("""
        §M{m1:M}
          §F{f1:Probe:priv} (i32:x) -> i32
            §E{}
            §B{bump:Func<i32>} §LAM{l1} §ASSIGN x INT:-5 §R INT:0 §/LAM{l1}
            §IF{if1} (< x INT:0)
              §R INT:0
            §EL
              §B{z:i32} §C{bump} §/C
              §PROOF{p1:claim} (>= x INT:0)
            §R x
        """)]
    // Review round 2 witnesses: a compiler-directive payload ("x = -5;") and a field the caller
    // can pass by reference as x.
    [InlineData("""
        §M{m1:M}
          §F{f1:Probe:priv} (i32:x) -> i32
            §E{}
            §Q (> x INT:0)
            §CDIR{compiler-directive:eCA9IC01Ow}
            §PROOF{p1:claim} (> x INT:0)
            §R x
        """)]
    [InlineData("""
        §M{m1:M}
          §CL{c1:Box:pub}
            §FLD{i32:Value:pub}
            §MT{mt1:Probe:pub}
              §I{i32:x:ref}
              §O{i32}
              §E{mut}
              §Q (> x INT:0)
              §ASSIGN this.Value INT:-5
              §PROOF{p1:claim} (> x INT:0)
              §R x
        """)]
    public void IndirectWrite_KillsFacts(string source)
    {
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void DroppedParameterRefinement_MakesTheStateInexact()
    {
        // Review round 2 witness: the entry guard enforces y > 0, but the refinement is dropped
        // because x is reassigned, so a model y <= 0 must not become a Failed compile error.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub}
                §I{i32:x} | (&& (> # INT:0) (> y INT:0))
                §I{i32:y}
                §O{void}
                §E{}
                §ASSIGN x INT:1
                §PROOF{p1:claim} (> y INT:0)
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ParameterNamedResult_DoesNotDischargeTheRefinedReturn()
    {
        const string source = """
            §M{m1:M}
              §RTYPE{r1:Pos:i32} (> # INT:0)
              §F{f1:Probe:pub} (Pos:result) -> Pos
                §E{}
                §R INT:-5
            """;
        var (obligations, csharp, _) = Solve(source);
        Assert.NotEqual(ObligationStatus.Discharged, Single(obligations, ObligationKind.RefinementReturn).Status);
        Assert.Contains("Return value violates refinement type 'Pos'", csharp);
    }

    [Fact]
    public void GuardAfterEarlyExit_StillUsedAsAFact()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §IF{e1} (< x INT:0)
                  §R INT:0
                §IF{p2} (> x INT:5)
                  §PROOF{p1:claim} (> x INT:0)
                §R x
            """;
        Assert.Equal(ObligationStatus.Discharged, Single(Solve(source).Obligations, ObligationKind.ProofObligation).Status);
    }

    [Fact]
    public void ObligationAfterLoop_IsNotRefutedWithANonExitingModel()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{cw}
                §WH{w1} (< x INT:0)
                  §P x
                §PROOF{p1:claim} (>= x INT:0)
                §R x
            """;
        Assert.NotEqual(ObligationStatus.Failed, Single(Solve(source).Obligations, ObligationKind.ProofObligation).Status);
    }

    [Fact]
    public void ConstructorNamedRefinementParameter_IsAnEntryFact()
    {
        const string source = """
            §M{m1:M}
              §RTYPE{r1:Pos:i32} (> # INT:0)
              §CL{c001:Box:pub}
                §CTOR{ctor1:pub} (Pos:value)
                  §PROOF{p1:claim} (> value INT:0)
            """;
        Assert.Equal(ObligationStatus.Discharged, Single(Solve(source).Obligations, ObligationKind.ProofObligation).Status);
    }

    [Fact]
    public void RawCSharpKillsPreconditionFact()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §Q (> x INT:0)
                §RAW
                x = -5;
                §/RAW
                §PROOF{p1:claim} (> x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void NamedRefinementFactKilledByAssignment_IsNotDischarged()
    {
        const string source = """
            §M{m1:M}
              §RTYPE{r1:Pos:i32} (> # INT:0)
              §F{f1:Probe:priv} (Pos:x) -> i32
                §E{}
                §ASSIGN x INT:-5
                §PROOF{p1:claim} (> x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void Control_UnassignedPreconditionFact_StillDischarges()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §Q (> x INT:0)
                §PROOF{p1:claim} (> x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.Equal(ObligationStatus.Discharged, proof.Status);
    }

    // ------------------------------------------------------- spurious refutations

    [Theory]
    [InlineData("(== x INT:2)", "INT:1457976674")]      // R1-OBL-MUTATION-KILL-004
    [InlineData("(>= x INT:2)", "INT:315807369")]       // R1-OBL-MUTATION-KILL-005
    [InlineData("(< x INT:2147483647)", "INT:-2147483648")] // R1-OBL-MUTATION-KILL-006
    [InlineData("(!= x INT:2147483647)", "INT:-936965578")] // R1-OBL-MUTATION-KILL-007
    public void ReassignedVariable_IsNotRefutedWithAnUnreachableModel(string guard, string assigned)
    {
        var source = $$"""
            §M{m1:R1Case}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §IF{if1} {{guard}}
                  §ASSIGN x {{assigned}}
                  §PROOF{p1:claim} {{guard}}
                §R x
            """;
        var (obligations, csharp, hasErrors) = Solve(source);
        var proof = Single(obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
        Assert.Equal(ProofStatus.Unsupported, proof.Outcome!.Status);
        Assert.False(hasErrors);
        Assert.Contains("Proof obligation [p1", csharp);
    }

    [Fact]
    public void ElseBranch_ViolatedClaim_IsRefutedWithAReachingModel()
    {
        // R1-OBL-BRANCH-FACTS-006: the else body runs only for x == 548596110, where the
        // claim x <= -2147483647 is false. The counterexample must be that input.
        const string source = """
            §M{m1:R1Case}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §IF{if1} (!= x INT:548596110)
                  §R INT:1
                §EL
                  §PROOF{p1:claim} (<= x INT:-2147483647)
                §R INT:0
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.Equal(ObligationStatus.Failed, proof.Status);
        var binding = Assert.Single(proof.Outcome!.Counterexample!.Bindings, b => b.Name == "x");
        Assert.Equal("548596110", binding.Value);
    }

    [Fact]
    public void ElseBranch_HoldingClaim_IsDischarged()
    {
        // R1-OBL-BRANCH-FACTS-007: the else body runs only for x == int.MaxValue.
        const string source = """
            §M{m1:R1Case}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §IF{if1} (< x INT:2147483647)
                  §R INT:1
                §EL
                  §PROOF{p1:claim} (> x INT:-2147483647)
                §R INT:0
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.Equal(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ElseIfBranch_UsesEarlierNegations()
    {
        // The elseif body runs only when x <= 0 and x >= -5: x + 10 > 0 holds there.
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §IF{if1} (> x INT:0)
                  §R INT:1
                §EI (>= x INT:-5)
                  §PROOF{p1:claim} (> (+ x INT:10) INT:0)
                §R INT:0
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.Equal(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ElseFact_KilledWhenElseBodyReassigns()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §IF{if1} (< x INT:0)
                  §R INT:1
                §EL
                  §ASSIGN x INT:-7
                  §PROOF{p1:claim} (>= x INT:0)
                §R INT:0
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ObligationAfterEarlyReturn_IsNotRefutedWithAnUnreachableModel()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:x) -> i32
                §E{}
                §IF{if1} (< x INT:0)
                  §R INT:0
                §PROOF{p1:claim} (>= x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void RefinedReturn_UnboundResult_IsNotRefuted()
    {
        // R1-OBL-REFINEMENT-RETURN-002: the solver's `result` is not bound to the returned
        // expression, so `result = 2` is no counterexample (x = 439613318 returns 439613316).
        const string source = """
            §M{m1:R1Case}
              §RTYPE{r1:Ref:i32} (!= # INT:2)
              §F{f1:Probe:pub}
                §I{i32:x}
                §O{Ref}
                §E{}
                §Q (> x INT:-1707870331)
                §R (+ x INT:-2)
            """;
        Assert.NotEqual(2, 439613318 + -2);
        var (obligations, csharp, hasErrors) = Solve(source);
        var obligation = Single(obligations, ObligationKind.RefinementReturn);
        Assert.Equal(ObligationStatus.Unsupported, obligation.Status);
        Assert.False(hasErrors);
        Assert.Contains("Return value violates refinement type 'Ref'", csharp);
    }

    [Theory]
    [InlineData("§RTYPE{r1:Ref:i32} (> (- # INT:403639039) INT:608460032)", "i32")]        // SELFREF-001
    [InlineData("§RTYPE{r1:Ref:i32} (>= (& # INT:-1200624532) INT:-2147483647)", "i32")]  // SELFREF-002
    [InlineData("§RTYPE{r1:Ref:i64} (>= (+ # INT:-2147483648) LONG:4788170680209598745)", "i64")] // SELFREF-003
    [InlineData("§RTYPE{r1:Ref:i32} (>= (& # INT:2147483647) INT:2147483646)", "i32")]    // SELFREF-004
    [InlineData("§RTYPE{r1:Ref:i64} (< (- # LONG:-9223372036854775808) INT:-1)", "i64")]  // SELFREF-005
    public void NamedRefinementParameter_IsAnEntryFact(string refinement, string _)
    {
        var source = $$"""
            §M{m1:R1Case}
              {{refinement}}
              §F{f1:Probe:pub}
                §I{Ref:x}
                §O{i32}
                §E{}
                §B{y:Ref} x
                §R INT:0
            """;
        var (obligations, _, hasErrors) = Solve(source);
        var subtype = Single(obligations, ObligationKind.Subtype);
        // y = x where x already satisfies Ref: never a refutation.
        Assert.NotEqual(ObligationStatus.Failed, subtype.Status);
        Assert.False(hasErrors);
    }

    [Fact]
    public void NamedRefinementParameter_SubtypeIsDischarged()
    {
        // R1-OBL-SUBTYPE-002: x : Src (x == long.MaxValue) implies Dst (x != -5562123229525975277).
        const string source = """
            §M{m1:R1Case}
              §RTYPE{r1:Src:i64} (== # LONG:9223372036854775807)
              §RTYPE{r2:Dst:i64} (!= # LONG:-5562123229525975277)
              §F{f1:Probe:pub}
                §I{Src:x}
                §O{i32}
                §E{}
                §B{n:Dst} x
                §R INT:0
            """;
        var subtype = Single(Solve(source).Obligations, ObligationKind.Subtype);
        Assert.Equal(ObligationStatus.Discharged, subtype.Status);
    }

    [Fact]
    public void Control_GenuineSubtypeViolation_StillRefuted()
    {
        // x : Pos does not imply Big: x = 1 is a real counterexample at a reachable binding.
        const string source = """
            §M{m1:M}
              §RTYPE{r1:Pos:i32} (> # INT:0)
              §RTYPE{r2:Big:i32} (> # INT:100)
              §F{f1:Probe:pub}
                §I{Pos:x}
                §O{i32}
                §E{}
                §B{n:Big} x
                §R INT:0
            """;
        var subtype = Single(Solve(source).Obligations, ObligationKind.Subtype);
        Assert.Equal(ObligationStatus.Failed, subtype.Status);
    }
}
