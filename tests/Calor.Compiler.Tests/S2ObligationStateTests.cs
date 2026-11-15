using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
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
    private static (List<Obligation> Obligations, string CSharp, bool HasErrors) Solve(
        string source, bool enableTypeChecking = true)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        var options = new CompilationOptions
        {
            EnableTypeChecking = enableTypeChecking,
            VerifyContracts = true,
            VerifyRefinements = true,
            ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var result = Program.Compile(source, "case.calr", options);
        if (options.ObligationResults != null)
        {
            var proofs = RecursiveAstWalker.DescendantsAndSelf(result.Ast!).OfType<ProofObligationNode>().ToArray();
            foreach (var obligation in options.ObligationResults.Obligations.Where(o => o.Kind == ObligationKind.ProofObligation))
                Assert.Equal(Assert.Single(proofs, proof => proof.Span == obligation.Span).Id, obligation.SourceProofId);
        }
        // A type error stops compilation before obligations are generated.
        return (options.ObligationResults?.Obligations.ToList() ?? [], result.GeneratedCode, result.HasErrors);
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
            §M{R1Case}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §Q {{predicate}}
                §ASSIGN x {{assigned}}
                §PROOF{claim} {{predicate}}
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
        Assert.Contains($"Proof obligation [{proof.SourceProofId}", csharp);
    }

    [Fact]
    public void RefArgumentKillsPreconditionFact()
    {
        const string source = """
            §M{M}
              §F{Bump:priv} (i32:v:ref) -> void
                §E{}
                §ASSIGN v INT:-5
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §Q (> x INT:0)
                §C{Bump} §A{ref} x §/C
                §PROOF{claim} (> x INT:0)
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
            §M{M}
              §F{Probe:priv} (i32:x:ref, i32:y:ref) -> i32
                §E{}
                §Q (> x INT:0)
                §ASSIGN y INT:-5
                §PROOF{claim} (> x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ArrayElementStoreKillsPreconditionFact()
    {
        const string source = """
            §M{M}
              §F{Probe:priv} ([i32]:a) -> i32
                §E{mut}
                §Q (> §IDX a INT:0 INT:0)
                §SETIDX{a} INT:0 INT:-5
                §PROOF{claim} (> §IDX a INT:0 INT:0)
                §R INT:0
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
    }

    // Review round 1 witnesses: each was a false Discharged (guard elided) or a lost proof.
    [Theory]
    [InlineData("""
        §M{M}
          §F{Probe:priv} (i32:x) -> i32
            §E{}
            §Q (> x INT:0)
            §B{sink:i32} §CS{(x = -5)}
            §PROOF{claim} (> x INT:0)
            §R x
        """)]
    [InlineData("""
        §M{M}
          §F{Probe:priv} (i32:x:ref, i32:y:ref) -> i32
            §E{}
            §IF (< x INT:0)
              §R INT:0
            §EL
              §ASSIGN y INT:-5
              §PROOF{claim} (>= x INT:0)
            §R x
        """)]
    [InlineData("""
        §M{M}
          §F{Probe:priv} (i32:x) -> i32
            §E{unsafe}
            §Q (> x INT:0)
            §UNSAFE
              §B{ptr:i32*} §ADDR x
              §ASSIGN §DEREF ptr INT:-5
            §PROOF{claim} (> x INT:0)
            §R x
        """)]
    [InlineData("""
        §M{M}
          §F{Probe:priv} (i32:x) -> i32
            §E{}
            §B{bump:Func<i32>} §LAM{} §ASSIGN x INT:-5 §R INT:0 §/LAM
            §IF (< x INT:0)
              §R INT:0
            §EL
              §B{z:i32} §C{bump} §/C
              §PROOF{claim} (>= x INT:0)
            §R x
        """)]
    // Review round 2 witnesses: a compiler-directive payload ("x = -5;") and a field the caller
    // can pass by reference as x.
    [InlineData("""
        §M{M}
          §F{Probe:priv} (i32:x) -> i32
            §E{}
            §Q (> x INT:0)
            §CDIR{compiler-directive:eCA9IC01Ow}
            §PROOF{claim} (> x INT:0)
            §R x
        """)]
    [InlineData("""
        module M M block
          class Box
            field int:Value
            func Probe (ref int:x) → int
              ! mut
              requires (x > 0)
              set this.Value -5
              proof{"claim"} (x > 0)
              return x
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
            module M M block
              func Probe (int:x | ((# > 0) && (y > 0)), int:y) → void
                !
                set x 1
                proof{"claim"} (y > 0)
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void DroppedRefinement_ReachedThroughARetainedPrecondition_IsNotFailed()
    {
        // Review round 3 witness: y > 0 (dropped entry refinement) and y == z (retained
        // precondition) make the claim hold; a model y = z = 0 is unreachable.
        const string source = """
            module M M block
              func Probe (int:x | ((# > 0) && (y > 0)), int:y, int:z) → void
                !
                requires (y == z)
                set x 1
                proof{"claim"} (z > 0)
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
    }

    [Fact]
    public void PropertyGetterRead_KillsAliasedRefParameterFact()
    {
        // Review round 3 witness: box.Probe(ref box.Value) with Value = 1; reading Trigger
        // runs a getter that stores -5 through the alias.
        const string source = """
            module M M block
              class Box
                field int:Value
                property int:Trigger
                  get
                    set this.Value -5
                    return 0
                func Probe (ref int:x) → int
                  ! mut
                  requires (x > 0)
                  let int:sink = this.Trigger
                  proof{"claim"} (x > 0)
                  return x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void PropertyGetterReadInAnEarlierProof_KillsAliasedRefParameterFact()
    {
        // Verification-pass witness: p0's retained guard runs the getter, which stores -5 through
        // the alias before p1 is checked.
        const string source = """
            module M M block
              class Box
                field int:Value
                property int:Trigger
                  get
                    set this.Value -5
                    return 0
                func Probe (ref int:x) → int
                  ! mut
                  requires (x > 0)
                  proof{"trigger"} (this.Trigger == 0)
                  proof{"claim"} (x > 0)
                  return x
            """;
        var proof = Assert.Single(Solve(source).Obligations,
            obligation => obligation.Kind == ObligationKind.ProofObligation && obligation.Description == "claim");
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void GuardAfterEarlyExit_StillUsedAsAFact()
    {
        const string source = """
            §M{M}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §IF (< x INT:0)
                  §R INT:0
                §IF (> x INT:5)
                  §PROOF{claim} (> x INT:0)
                §R x
            """;
        Assert.Equal(ObligationStatus.Discharged, Single(Solve(source).Obligations, ObligationKind.ProofObligation).Status);
    }

    [Fact]
    public void ObligationAfterLoop_IsNotRefutedWithANonExitingModel()
    {
        const string source = """
            §M{M}
              §F{Probe:priv} (i32:x) -> i32
                §E{cw}
                §WH (< x INT:0)
                  §P x
                §PROOF{claim} (>= x INT:0)
                §R x
            """;
        Assert.NotEqual(ObligationStatus.Failed, Single(Solve(source).Obligations, ObligationKind.ProofObligation).Status);
    }

    [Fact]
    public void RawCSharpKillsPreconditionFact()
    {
        const string source = """
            §M{M}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §Q (> x INT:0)
                §RAW
                x = -5;
                §/RAW
                §PROOF{claim} (> x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void Control_UnassignedPreconditionFact_StillDischarges()
    {
        const string source = """
            §M{M}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §Q (> x INT:0)
                §PROOF{claim} (> x INT:0)
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
            §M{R1Case}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §IF {{guard}}
                  §ASSIGN x {{assigned}}
                  §PROOF{claim} {{guard}}
                §R x
            """;
        var (obligations, csharp, hasErrors) = Solve(source);
        var proof = Single(obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
        Assert.Equal(ProofStatus.Unsupported, proof.Outcome!.Status);
        Assert.False(hasErrors);
        Assert.Contains($"Proof obligation [{proof.SourceProofId}", csharp);
    }

    [Fact]
    public void ElseBranch_ViolatedClaim_IsRefutedWithAReachingModel()
    {
        // R1-OBL-BRANCH-FACTS-006: the else body runs only for x == 548596110, where the
        // claim x <= -2147483647 is false. The counterexample must be that input.
        const string source = """
            §M{R1Case}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §IF (!= x INT:548596110)
                  §R INT:1
                §EL
                  §PROOF{claim} (<= x INT:-2147483647)
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
            §M{R1Case}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §IF (< x INT:2147483647)
                  §R INT:1
                §EL
                  §PROOF{claim} (> x INT:-2147483647)
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
            §M{M}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §IF (> x INT:0)
                  §R INT:1
                §EI (>= x INT:-5)
                  §PROOF{claim} (> (+ x INT:10) INT:0)
                §R INT:0
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.Equal(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ElseFact_KilledWhenElseBodyReassigns()
    {
        const string source = """
            §M{M}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §IF (< x INT:0)
                  §R INT:1
                §EL
                  §ASSIGN x INT:-7
                  §PROOF{claim} (>= x INT:0)
                §R INT:0
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ObligationAfterEarlyReturn_IsNotRefutedWithAnUnreachableModel()
    {
        const string source = """
            §M{M}
              §F{Probe:priv} (i32:x) -> i32
                §E{}
                §IF (< x INT:0)
                  §R INT:0
                §PROOF{claim} (>= x INT:0)
                §R x
            """;
        var proof = Single(Solve(source).Obligations, ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }
}
