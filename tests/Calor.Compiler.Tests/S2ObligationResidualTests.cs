using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (0.24 S2, amendment 1.3.0): visible demotions for the two review-found discoveries
/// D-OBL-PROOF-GETTER and D-OBL-THROWING-PREDECESSOR. Both were spurious refutations (a Failed
/// compile error on a correct program); the counterexample is now withheld (Unsupported, guard
/// kept). The tests need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2ObligationResidualTests
{
    private static List<Obligation> Solve(string source)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerifyRefinements = true,
            ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        Program.Compile(source, "case.calr", options);
        return options.ObligationResults!.Obligations.ToList();
    }

    private static Obligation Proof(List<Obligation> obligations, string id)
        => Assert.Single(obligations, o => o.Kind == ObligationKind.ProofObligation && o.SourceProofId == id);

    [Fact]
    public void ProofReadingAPropertyThatHidesAField_IsNotRefuted()
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
        var proof = Proof(Solve(source), "p1");
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ProofAfterAStatementThatThrowsOnTheModel_IsNotRefuted()
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
        var proof = Proof(Solve(source), "p1");
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void ProofAfterAnEarlierProofGuard_IsNotRefuted()
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
        var proof = Proof(Solve(source), "p2");
        Assert.NotEqual(ObligationStatus.Failed, proof.Status);
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
        Assert.Equal(ObligationStatus.Failed, Proof(Solve(source), "p1").Status);
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
        Assert.Equal(ObligationStatus.Failed, Proof(Solve(source), "p1").Status);
    }
}
