using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
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
    private static Obligation SolveProof(string body, string parameters = "int:x", string prelude = "")
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = $$"""
            module Test Test block
              {{prelude}}
              func Check private ({{parameters}}) → void
            {{Indent(body)}}
            """;
        return SolveModule(source);
    }

    private static Obligation SolveModule(string source)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.ToString())));
        var proofId = Assert.Single(RecursiveAstWalker.DescendantsAndSelf(module).OfType<ProofObligationNode>()).Id;

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
            module Banking Banking block
              func Transfer (int:balance, int:amount | ((# > 0) && (balance >= #))) → int
                !
                let int:newBalance = (balance - amount)
                proof{"non-negative"} (newBalance >= 0)
                return newBalance
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
            let int:k = 5
            proof (k == 5)
            """).Status);

    [SkippableFact]
    public void ChainedLocals_Discharge()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            let int:a = (x + 1)
            let int:b = (a * 2)
            proof (b > x)
            """, "int:x | ((# >= 0) && (# < 1000))").Status);

    [SkippableFact]
    public void UntypedBoolLocal_Discharges()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            let positive = (x > 0)
            proof positive
            """, "int:x | (# > 0)").Status);

    [SkippableFact]
    public void LocalBoundBeforeBranch_ProvesInsideIt()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            let int:k = (0 - 3)
            if (x > k)
              proof (x > (k - 1))
            """).Status);

    [SkippableFact]
    public void LocalBoundInsideGuardedBody_ProvesInSameBody()
        => Assert.Equal(ObligationStatus.Discharged, SolveProof("""
            if (x > 0)
              let int:y = x
              proof (y > 0)
            """).Status);

    // ───── Soundness negatives: never Discharged ─────

    [SkippableFact]
    public void MutableLocal_IsRefused()
        => AssertNotDischarged(SolveProof("""
            var int:m = 5
            proof (m == 5)
            """), "local 'm' is mutable");

    [SkippableFact]
    public void ReassignedMutableLocal_IsNotDischarged()
        => AssertNotDischarged(SolveProof("""
            var int:m = 5
            set m -1
            proof (m >= 0)
            """), "local 'm'");

    [SkippableFact]
    public void LocalDefinedFromReassignedParameter_IsRefused()
        => AssertNotDischarged(SolveProof("""
            let int:y = x
            set x (x + 1)
            proof (y == x)
            """), "is defined from 'x'");

    [SkippableFact]
    public void LocalReboundByRefArgument_IsRefused()
        => AssertNotDischarged(SolveProof("""
            let int:y = 1
            Mutate(ref y)
            proof (y == 1)
            """), "local 'y' is reassigned");

    [SkippableFact]
    public void LocalBoundInBranchNotEnclosingProof_IsRefused()
        => AssertNotDischarged(SolveProof("""
            if (x > 0)
              let int:y = 1
            else
              proof (y == 1)
            """), "is not bound on every path to the proof");

    [SkippableFact]
    public void LocalBoundAfterProof_IsRefused()
        => AssertNotDischarged(SolveProof("""
            proof (y == 1)
            let int:y = 1
            """), "is not bound on every path to the proof");

    [SkippableFact]
    public void LocalBoundInEarlierLoopIteration_IsRefused()
        => AssertNotDischarged(SolveProof("""
            while (x > 0)
              proof (y == 1)
              let int:y = 1
            """), "is not bound on every path to the proof");

    [SkippableFact]
    public void LocalShadowingParameter_IsRefused()
        => AssertNotDischarged(SolveProof("""
            let int:x = -1
            proof (x > 0)
            """, "int:x | (# > 0)"), "shares its name with a parameter");

    // Review round 3: an `in` argument exposes the local's storage to the callee.
    [SkippableFact]
    public void LocalPassedAsInArgument_IsRefused()
        => AssertNotDischarged(SolveProof("""
            let int:k = 1
            Mutate(in k)
            proof (k == 1)
            """), "local 'k' is reassigned, passed by reference");

    [SkippableFact]
    public void LocalInModuleWithOperatorOverloads_IsRefused()
        => AssertNotDischarged(SolveModule("""
            module Test Test block
              class Probe
                op == (Probe:a, Probe:b) → bool
                  !
                  return true
                op != (Probe:a, Probe:b) → bool
                  !
                  return false
              func Check private (int:x) → void
                let int:k = 1
                proof (k == 1)
            """), "operator overloads");

    [SkippableFact]
    public void LocalBoundTwice_IsRefused()
        => AssertNotDischarged(SolveProof("""
            if (x > 0)
              let int:y = 1
              proof (y == 1)
            else
              let int:y = -1
            """), "is bound more than once");

    [SkippableFact]
    public void LocalDefinedFromLoopVariable_IsRefused()
        => AssertNotDischarged(SolveProof("""
            for i from 0 to 10 step 1
              let int:d = i
              proof (d >= 0)
            """), "is defined from 'i'");

    [SkippableFact]
    public void FalseClaimAboutLocal_Fails()
    {
        var proof = SolveProof("""
            let bool:positive = (x > 0)
            proof positive
            """);
        Assert.Equal(ObligationStatus.Failed, proof.Status);
    }

    [SkippableFact]
    public void FalseClaimAboutArithmeticLocal_IsNotDischarged()
        => AssertNotDischarged(SolveProof("""
            let int:k = (x + 1)
            proof (k < x)
            """, "int:x | ((# >= 0) && (# < 100))"));

    [SkippableFact]
    public void LocalDefinitionThatMayOverflow_IsNotDischarged()
    {
        // Without bounds on x, (+ x 1) overflows at int.MaxValue; the proof needs the checked add
        // to complete, so the guard stays.
        var proof = SolveProof("""
            let int:k = (x + 1)
            proof (k > x)
            """);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [SkippableFact]
    public void WideningLocal_IsRefused()
        => AssertNotDischarged(SolveProof("""
            let long:w = x
            proof (w == x)
            """), "the conversion is not modeled");

    [SkippableFact]
    public void LocalDefinedByCall_IsRefused()
        => AssertNotDischarged(SolveProof("""
            let int:y = Next(x)
            proof (y > x)
            """), "has a defining expression the verifier does not model");

    [SkippableFact]
    public void LocalDefinedFromField_IsRefused()
        => AssertNotDischarged(SolveProof("""
            let int:y = count
            proof (y == count)
            """), "is defined from 'count'");

    [SkippableFact]
    public void LocalInBodyWithRawCSharp_IsRefused()
        => AssertNotDischarged(SolveProof("""
            let int:y = 1
            raw statement 24
            int sink = (y = -5);
            proof (y == 1)
            """), "raw C#");

    // ───── Genuinely untranslatable proofs still report Unsupported ─────

    [SkippableFact]
    public void ProofWithCall_StillUnsupported()
    {
        var proof = SolveProof("""
            proof (Next(x) > x)
            """);
        Assert.Equal(ObligationStatus.Unsupported, proof.Status);
    }

    [SkippableFact]
    public void ProofOverUnboundName_StillReportsUnknownVariable()
        => AssertNotDischarged(SolveProof("""
            proof (missing > 0)
            """), "Unknown variable 'missing'");
}
