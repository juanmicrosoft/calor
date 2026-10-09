using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1516: a §PROOF may read an immutable §B local whose value at the proof is its definition.
/// Soundness first: a local whose value at the proof is not known (mutable, reassigned, bound on
/// only some paths, defined from changing state) must never let the proof be Discharged, which
/// deletes the runtime guard.
/// </summary>
public sealed class ProofLocalObligationTests
{
    private static Obligation SolveProof(string body, string parameters = "§I{i32:x}", string? proofId = "p1")
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = $$"""
            §M{m001:Test}
              §F{f001:Check:priv}
                {{parameters}}
                §O{void}
            {{Indent(body)}}
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.ToString())));

        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        using (var solver = new ObligationSolver(Z3ContextFactory.Create()))
            solver.SolveAll(tracker, module);
        return Assert.Single(tracker.Obligations,
            o => o.Kind == ObligationKind.ProofObligation && o.SourceProofId == proofId);
    }

    private static string Indent(string body)
        => string.Join("\n", body.Trim('\n').Split('\n').Select(line => "    " + line));

    private static void AssertNotDischarged(Obligation obligation, string? reason = null)
    {
        Assert.NotEqual(ObligationStatus.Discharged, obligation.Status);
        if (reason != null)
        {
            Assert.Equal(ObligationStatus.Unsupported, obligation.Status);
            Assert.Contains(reason, obligation.Outcome?.Reason ?? obligation.CounterexampleDescription ?? "");
        }
    }

    // ───── Positive: an immutable local now proves ─────

    [SkippableFact]
    public void WebsiteTransferExample_Discharges()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = """
            §M{m001:Banking}
              §F{f001:Transfer:pub}
                §I{i32:balance}
                §I{i32:amount} | (> # INT:0)
                §O{i32}
                §Q (>= balance amount)
                §B{newBalance:i32} (- balance amount)
                §PROOF{p1:non-negative} (>= newBalance INT:0)
                §R newBalance
            """;
        var options = new CompilationOptions { VerifyRefinements = true };
        Program.Compile(source, "test.calr", options);

        var proof = Assert.Single(options.ObligationResults!.Obligations,
            o => o.Kind == ObligationKind.ProofObligation);
        Assert.Equal(ObligationStatus.Discharged, proof.Status);
    }

    [SkippableFact]
    public void LiteralLocal_Discharges()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            §B{k:i32} INT:5
            §PROOF{p1} (== k INT:5)
            """).Status);

    [SkippableFact]
    public void ChainedLocals_Discharge()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            §Q (>= x INT:0)
            §Q (< x INT:1000)
            §B{a:i32} (+ x INT:1)
            §B{b:i32} (* a INT:2)
            §PROOF{p1} (> b x)
            """).Status);

    [SkippableFact]
    public void UntypedBoolLocal_Discharges()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            §Q (> x INT:0)
            §B{positive} (> x INT:0)
            §PROOF{p1} positive
            """).Status);

    [SkippableFact]
    public void LocalBoundBeforeBranch_ProvesInsideIt()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            §B{k:i32} (- INT:0 INT:3)
            §IF{i1} (> x k)
              §PROOF{p1} (> x (- k INT:1))
            """).Status);

    [SkippableFact]
    public void LocalBoundInsideGuardedBody_ProvesInSameBody()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            §IF{i1} (> x INT:0)
              §B{y:i32} x
              §PROOF{p1} (> y INT:0)
            """).Status);

    // ───── Soundness negatives: never Discharged ─────

    [SkippableFact]
    public void MutableLocal_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §B{~m:i32} INT:5
            §PROOF{p1} (== m INT:5)
            """), "local 'm' is mutable");

    [SkippableFact]
    public void ReassignedMutableLocal_IsNotDischarged()
        => AssertNotDischarged(SolveProof("""
            §B{~m:i32} INT:5
            §ASSIGN m INT:-1
            §PROOF{p1} (>= m INT:0)
            """), "local 'm'");

    [SkippableFact]
    public void LocalDefinedFromReassignedParameter_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §B{y:i32} x
            §ASSIGN x (+ x INT:1)
            §PROOF{p1} (== y x)
            """), "is defined from 'x'");

    [SkippableFact]
    public void LocalReboundByRefArgument_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §B{y:i32} INT:1
            §C{Mutate} §A{ref} y §/C
            §PROOF{p1} (== y INT:1)
            """), "local 'y' is reassigned");

    [SkippableFact]
    public void LocalBoundInBranchNotEnclosingProof_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §IF{i1} (> x INT:0)
              §B{y:i32} INT:1
            §EL
              §PROOF{p1} (== y INT:1)
            """), "is not bound on every path to the proof");

    [SkippableFact]
    public void LocalBoundAfterProof_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §PROOF{p1} (== y INT:1)
            §B{y:i32} INT:1
            """), "is not bound on every path to the proof");

    [SkippableFact]
    public void LocalBoundInEarlierLoopIteration_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §WH{w1} (> x INT:0)
              §PROOF{p1} (== y INT:1)
              §B{y:i32} INT:1
            """), "is not bound on every path to the proof");

    [SkippableFact]
    public void LocalShadowingParameter_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §Q (> x INT:0)
            §B{x:i32} INT:-1
            §PROOF{p1} (> x INT:0)
            """), "shares its name with a parameter");

    // Review round 1: the emitter sanitizes `a-b` to `ab`, so the mutable `ab` rebinds it.
    [SkippableTheory]
    [InlineData("INT:1")]
    [InlineData("INT:2")]
    public void LocalSharingItsCSharpName_IsRefused(string claimed)
        => AssertNotDischarged(SolveProof($"""
            §B{"{"}a-b:i32{"}"} INT:1
            §B{"{"}~ab:i32{"}"} INT:2
            §PROOF{"{"}p1{"}"} (== `a-b` {claimed})
            """), "shares its C# name with another variable");

    [SkippableFact]
    public void LocalBoundTwice_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §IF{i1} (> x INT:0)
              §B{y:i32} INT:1
              §PROOF{p1} (== y INT:1)
            §EL
              §B{y:i32} INT:-1
            """), "is bound more than once");

    [SkippableFact]
    public void LocalDefinedFromLoopVariable_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §L{l1:i:0:10:1}
              §B{d:i32} i
              §PROOF{p1} (>= d INT:0)
            """), "is defined from 'i'");

    [SkippableFact]
    public void FalseClaimAboutLocal_Fails()
    {
        var proof = SolveProof("""
            §B{positive:bool} (> x INT:0)
            §PROOF{p1} positive
            """);
        Assert.Equal(ObligationStatus.Failed, proof.Status);
    }

    [SkippableFact]
    public void FalseClaimAboutArithmeticLocal_IsNotDischarged()
        => AssertNotDischarged(SolveProof("""
            §Q (>= x INT:0)
            §Q (< x INT:100)
            §B{k:i32} (+ x INT:1)
            §PROOF{p1} (< k x)
            """));

    [SkippableFact]
    public void LocalDefinitionThatMayOverflow_IsNotDischarged()
    {
        // Without bounds on x, (+ x 1) overflows at int.MaxValue; the proof needs the checked add
        // to complete, so the guard stays.
        var proof = SolveProof("""
            §B{k:i32} (+ x INT:1)
            §PROOF{p1} (> k x)
            """);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [SkippableFact]
    public void WideningLocal_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §B{w:i64} x
            §PROOF{p1} (== w x)
            """), "the conversion is not modeled");

    [SkippableFact]
    public void LocalDefinedByCall_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §B{y:i32} §C{Next} §A x §/C
            §PROOF{p1} (> y x)
            """), "has a defining expression the verifier does not model");

    [SkippableFact]
    public void LocalDefinedFromField_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §B{y:i32} count
            §PROOF{p1} (== y count)
            """), "is defined from 'count'");

    [SkippableFact]
    public void LocalInBodyWithRawCSharp_IsRefused()
        => AssertNotDischarged(SolveProof("""
            §B{y:i32} INT:1
            §B{sink:i32} §CS{(y = -5)}
            §PROOF{p1} (== y INT:1)
            """), "raw C#");

    // ───── Genuinely untranslatable proofs still report Unsupported ─────

    [SkippableFact]
    public void ProofWithCall_StillUnsupported()
    {
        var proof = SolveProof("""
            §PROOF{p1} (> §C{Next} §A x §/C x)
            """);
        Assert.Equal(ObligationStatus.Unsupported, proof.Status);
    }

    [SkippableFact]
    public void ProofOverUnboundName_StillReportsUnknownVariable()
        => AssertNotDischarged(SolveProof("""
            §PROOF{p1} (> missing INT:0)
            """), "Unknown variable 'missing'");
}
