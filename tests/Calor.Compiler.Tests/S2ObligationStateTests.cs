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
}
