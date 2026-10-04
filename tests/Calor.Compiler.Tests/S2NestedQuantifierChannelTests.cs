using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Calor.Compiler.Verification.Z3.KInduction;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (0.24 S2), S1 row QNT-NESTED, review round 1: the nested-quantifier refusal must hold
/// on the remaining channels too — obligation assumptions and k-induction invariants. These
/// need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2NestedQuantifierChannelTests
{
    [Fact]
    public void NestedQuantifierPrecondition_DoesNotDischargeAnObligation()
    {
        // The nested precondition entails n > 0 over non-empty bounds; it must not be used.
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:n) -> i32
                §E{}
                §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i INT:1)) (forall ((j i32)) (-> (&& (>= j INT:0) (< j INT:1)) (> n INT:0)))))
                §PROOF{p1:claim} (> n INT:0)
                §R n
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        Program.Compile(source, "case.calr", options);
        var proof = Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void NestedFactOutOfScope_DoesNotBlockAnUnrelatedObligation()
    {
        // Review round 2 witness: the nested if-condition is a fact only inside its then-body,
        // so it must not make the earlier obligation Unsupported.
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:n) -> i32
                §E{}
                §PROOF{p1:claim} (>= (* n INT:0) INT:0)
                §IF{if1} (forall ((i i32)) (-> (&& (>= i INT:0) (< i INT:1)) (cast bool (forall ((j i32)) (-> (&& (>= j INT:0) (< j INT:1)) (< j INT:2))))))
                  §R INT:1
                §R INT:0
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        Program.Compile(source, "case.calr", options);
        var proof = Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Unsupported, proof.Status);
    }

    [Fact]
    public void KInduction_UnparsableConjunct_IsNotDropped()
    {
        // Proving only "i >= 0" must not report the whole invariant, nested part included.
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        const string source = """
            §M{m001:Test}
              §F{f001:Loop:pub}
                  §O{i32}
                  §L{l1:i:0:3:1}
                      §P i
                  §R INT:0
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var function = new Binder(diagnostics).Bind(module).Functions.First();
        var loop = function.Body.OfType<BoundForStatement>().Single();

        using var prover = new KInductionProver(new KInductionOptions());
        var result = prover.ProveInvariant(loop, "i >= 0 && (forall ((j i32)) (forall ((k i32)) false))", function);
        Assert.NotEqual(KInductionStatus.Proven, result.Status);
    }
}
