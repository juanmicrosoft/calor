using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for obligation generation, solving, and C# emission (Milestone 1).
/// </summary>
public sealed class ObligationTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    // ───── Obligation Generation ─────

    [Fact]
    public void Generate_ProofObligation_CreatesObligation()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:positive} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Single(tracker.Obligations);
        var obl = tracker.Obligations[0];
        Assert.Equal(ObligationKind.ProofObligation, obl.Kind);
        Assert.Equal("f001", obl.FunctionId);
        Assert.Contains("positive", obl.Description);
        Assert.Equal(ObligationStatus.Pending, obl.Status);
    }

    [Fact]
    public void Generate_NestedProofObligation_CreatesObligation()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §IF{i1} (> x INT:0)
                    §PROOF{p1:positive} (> x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);

        var obligation = Assert.Single(tracker.Obligations);
        Assert.Equal(ObligationKind.ProofObligation, obligation.Kind);
        Assert.Equal("p1", obligation.SourceProofId);
    }

    [Fact]
    public void Generate_InlineRefinement_CreatesRefinementEntryObligation()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:age} | (>= # INT:0)
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Single(tracker.Obligations);
        var obl = tracker.Obligations[0];
        Assert.Equal(ObligationKind.RefinementEntry, obl.Kind);
        Assert.Equal("f001", obl.FunctionId);
        // Public function -> Boundary status
        Assert.Equal(ObligationStatus.Boundary, obl.Status);
    }

    [Fact]
    public void Generate_PrivateFunctionInlineRefinement_IsPending()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Helper:priv}
                  §I{i32:age} | (>= # INT:0)
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Single(tracker.Obligations);
        var obl = tracker.Obligations[0];
        Assert.Equal(ObligationKind.RefinementEntry, obl.Kind);
        // Private function -> stays Pending (solver will check it)
        Assert.Equal(ObligationStatus.Pending, obl.Status);
    }

    [Fact]
    public void Generate_MultipleObligations_TracksAll()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:NatInt:i32} (>= # INT:0)
              §F{f001:Main:pub}
                  §I{i32:x} | (>= # INT:0)
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        // Should have at least 2: one for inline refinement, one for proof obligation
        Assert.True(tracker.Obligations.Count >= 2);
        Assert.Contains(tracker.Obligations, o => o.Kind == ObligationKind.RefinementEntry);
        Assert.Contains(tracker.Obligations, o => o.Kind == ObligationKind.ProofObligation);
    }

    [Fact]
    public void Generate_NoRefinements_NoObligations()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Empty(tracker.Obligations);
    }

    // ───── Obligation Summary ─────

    [Fact]
    public void Summary_ReflectsObligationStatuses()
    {
        var tracker = new ObligationTracker();

        // Create a dummy expression for obligation conditions
        var dummyExpr = new IntLiteralNode(new TextSpan(0, 0, 1, 1), 0);
        var span = new TextSpan(0, 0, 1, 1);

        var obl1 = tracker.Add(ObligationKind.ProofObligation, "f1", "test1", dummyExpr, span);
        obl1.Status = ObligationStatus.Discharged;

        var obl2 = tracker.Add(ObligationKind.RefinementEntry, "f1", "test2", dummyExpr, span);
        obl2.Status = ObligationStatus.Failed;
        obl2.CounterexampleDescription = "x=-1";

        var obl3 = tracker.Add(ObligationKind.RefinementEntry, "f1", "test3", dummyExpr, span);
        obl3.Status = ObligationStatus.Boundary;

        var summary = tracker.GetSummary();
        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Discharged);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Boundary);
        Assert.Equal(0, summary.Pending);
        Assert.Equal(0, summary.Timeout);
    }

    [Fact]
    public void GetByFunction_FiltersCorrectly()
    {
        var tracker = new ObligationTracker();
        var dummyExpr = new IntLiteralNode(new TextSpan(0, 0, 1, 1), 0);
        var span = new TextSpan(0, 0, 1, 1);

        tracker.Add(ObligationKind.ProofObligation, "f1", "test1", dummyExpr, span);
        tracker.Add(ObligationKind.ProofObligation, "f2", "test2", dummyExpr, span);
        tracker.Add(ObligationKind.ProofObligation, "f1", "test3", dummyExpr, span);

        var f1Obligations = tracker.GetByFunction("f1");
        Assert.Equal(2, f1Obligations.Count);
    }

    // ───── Obligation Solving with Z3 ─────

    [SkippableFact]
    public void Solve_TrivialProofObligation_Discharged()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = """
            §M{m001:Test}
              §F{f001:Add:priv}
                  §I{i32:x}
                  §O{void}
                  §Q (>= x INT:0)
                  §PROOF{p1:non-neg} (>= x INT:0)
            """;

        var options = new CompilationOptions { VerifyRefinements = true };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        // The proof obligation says (>= x 0) and the precondition says (>= x 0)
        // So the obligation should be discharged
        var proofObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.ProofObligation);
        Assert.NotNull(proofObl);
        Assert.Equal(ObligationStatus.Discharged, proofObl.Status);
    }

    /// <summary>
    /// D3/D12, the SECOND elision channel. A discharged proof obligation makes the emitter drop
    /// its <c>if (!(cond)) throw</c>, exactly as a <c>Proven</c> postcondition elides — and
    /// <c>ObligationSolver</c> shares the contract translator's string theory, whose strings are
    /// non-null and UTF-8-byte-counted while .NET's are nullable and UTF-16-code-unit-counted.
    /// So a string-carried obligation must NOT discharge, or the guard disappears.
    ///
    /// <para>Pinned here because nothing else does: deleting the demotion in
    /// <c>ObligationSolver</c> leaves the rest of the suite fully green, and the only shipped way
    /// to reach <c>VerifyRefinements</c> is the MCP <c>refine</c> tool.</para>
    /// </summary>
    [SkippableFact]
    public void Solve_StringCarriedProofObligation_IsNotDischarged()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // (== (len "\u00e9") 2) is TRUE under Z3's byte model and FALSE in .NET, where Length is 1.
        var source = """
            §M{m001:Test}
              §F{f001:Check:priv}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:bytelen} (== (len STR:"\u00e9") INT:2)
            """;

        var options = new CompilationOptions { VerifyRefinements = true };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);
        var proofObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.ProofObligation);
        Assert.NotNull(proofObl);
        Assert.NotEqual(ObligationStatus.Discharged, proofObl!.Status);
        Assert.Equal(Verification.ProofStatus.Assumed, proofObl.Outcome!.Status);
        Assert.Contains(Verification.Z3.Z3Verifier.StringModelAssumption, proofObl.Outcome.Assumptions);
    }

    /// <summary>
    /// The control that makes the test above mean something: a numeric obligation of the same
    /// shape still discharges, so the demotion is the string trigger and not a verifier that
    /// stopped solving obligations.
    /// </summary>
    [SkippableFact]
    public void Solve_NumericProofObligation_StillDischarges()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = """
            §M{m001:Test}
              §F{f001:Check:priv}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:numeric} (== (+ INT:1 INT:1) INT:2)
            """;

        var options = new CompilationOptions { VerifyRefinements = true };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);
        var proofObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.ProofObligation);
        Assert.NotNull(proofObl);
        Assert.Equal(ObligationStatus.Discharged, proofObl!.Status);
    }

    [SkippableFact]
    public void Solve_FailingProofObligation_FailsWithCounterexample()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = """
            §M{m001:Test}
              §F{f001:Check:priv}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:always-positive} (> x INT:0)
            """;

        var options = new CompilationOptions { VerifyRefinements = true };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        // No precondition guarantees x > 0, so this should fail
        var proofObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.ProofObligation);
        Assert.NotNull(proofObl);
        Assert.Equal(ObligationStatus.Failed, proofObl.Status);
        Assert.NotNull(proofObl.CounterexampleDescription);
        Assert.Contains("Counterexample", proofObl.CounterexampleDescription);
        // Counterexample should contain a meaningful variable assignment
        Assert.Contains("x=", proofObl.CounterexampleDescription);
    }

    [SkippableFact]
    public void Solve_InlineRefinementWithSelfRef_DischargesViaZ3()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Private function with precondition that guarantees the inline refinement.
        // The # in (>= # INT:0) should resolve to 'age' via PushSelfVariable.
        var source = """
            §M{m001:Test}
              §F{f001:Validate:priv}
                  §I{i32:age} | (>= # INT:0)
                  §O{void}
                  §Q (>= age INT:0)
            """;

        var options = new CompilationOptions { VerifyRefinements = true };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        var refObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.RefinementEntry);
        Assert.NotNull(refObl);
        Assert.Equal("age", refObl.ParameterName);
        // Precondition (>= age 0) should discharge the inline refinement (>= # 0)
        Assert.Equal(ObligationStatus.Discharged, refObl.Status);
    }

    [SkippableFact]
    public void Solve_InlineRefinementWithoutPrecondition_Fails()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Private function WITHOUT precondition — inline refinement can't be verified
        var source = """
            §M{m001:Test}
              §F{f001:Validate:priv}
                  §I{i32:age} | (>= # INT:0)
                  §O{void}
            """;

        var options = new CompilationOptions { VerifyRefinements = true };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        var refObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.RefinementEntry);
        Assert.NotNull(refObl);
        Assert.Equal(ObligationStatus.Failed, refObl.Status);
        Assert.NotNull(refObl.CounterexampleDescription);
    }

    // ───── Obligation Metadata ─────

    [Fact]
    public void Generate_SetsParameterNameOnRefinementEntry()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:myParam} | (>= # INT:0)
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        var obl = Assert.Single(tracker.Obligations);
        Assert.Equal("myParam", obl.ParameterName);
    }

    [Fact]
    public void Generate_SetsSourceProofIdOnProofObligation()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        var obl = Assert.Single(tracker.Obligations);
        Assert.Equal("p1", obl.SourceProofId);
    }

    // ───── Full Pipeline Integration ─────

    [Fact]
    public void CompilePipeline_WithVerifyRefinements_PopulatesObligationResults()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:NatInt:i32} (>= # INT:0)
              §F{f001:Main:pub}
                  §I{i32:x} | (>= # INT:0)
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var options = new CompilationOptions
        {
            VerifyRefinements = true
        };

        var result = Program.Compile(source, "test.calr", options);

        // ObligationResults should be populated
        Assert.NotNull(options.ObligationResults);
        Assert.True(options.ObligationResults.Obligations.Count >= 2);

        var summary = options.ObligationResults.GetSummary();
        Assert.True(summary.Total >= 2);
    }

    [Fact]
    public void CompilePipeline_WithoutVerifyRefinements_NoObligations()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:NatInt:i32} (>= # INT:0)
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
            """;

        var options = new CompilationOptions
        {
            VerifyRefinements = false
        };

        var result = Program.Compile(source, "test.calr", options);

        // No obligation tracker when VerifyRefinements is false
        Assert.Null(options.ObligationResults);
    }

    // ───── C# Emission with Obligation Tracker ─────

    [Fact]
    public void CSharpEmit_DischargedProofObligation_EmitsProvenComment()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Create a tracker with a discharged obligation
        var tracker = new ObligationTracker();
        var genr = new ObligationGenerator(tracker);
        genr.Generate(module);

        // Manually set status to Discharged for testing
        foreach (var obl in tracker.Obligations)
        {
            if (obl.Kind == ObligationKind.ProofObligation)
                obl.Status = ObligationStatus.Discharged;
        }

        // Elision is opt-in as of v0.13 (roadmap §2.1); this test pins the machinery.
        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, tracker)
        {
            ElideProvenGuards = true
        };
        var csharp = emitter.Emit(module);

        Assert.Contains("// PROVEN:", csharp);
        Assert.DoesNotContain("// TODO:", csharp);
    }

    [Fact]
    public void CSharpEmit_PolicyAlwaysGuard_OverridesProvenGuardElision()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        Assert.Single(tracker.Obligations).Status = ObligationStatus.Discharged;

        var policy = new ObligationPolicy
        {
            Discharged = ObligationAction.AlwaysGuard
        };
        var emitter = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker,
            diagnostics: null,
            obligationPolicy: policy)
        {
            ElideProvenGuards = true
        };

        var csharp = emitter.Emit(module);

        Assert.Contains("throw new InvalidOperationException", csharp);
        Assert.DoesNotContain("// PROVEN:", csharp);
    }

    [SkippableFact]
    public void ReassignmentPreventsUnsoundProofGuardElision()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = """
            §M{m001:Test}
              §F{f001:Bad:pub}
                  §O{void}
                  §B{~x:i32} INT:1
                  §IF{i1} (> x INT:0)
                    §ASSIGN x INT:-1
                    §PROOF{p1} (> x INT:0)
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            ElideProvenGuards = true,
            ObligationPolicy = ObligationPolicy.Permissive
        };

        var result = Program.Compile(source, "test.calr", options);

        var proof = Assert.Single(
            options.ObligationResults!.Obligations,
            obligation => obligation.Kind == ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
        Assert.Contains("Proof obligation [p1] violated", result.GeneratedCode);
        Assert.DoesNotContain("// PROVEN:", result.GeneratedCode);
    }

    [SkippableFact]
    public void AssignmentSubtypeProof_UsesAssignedValue()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Assign:priv}
                  §I{Positive:value}
                  §O{i32}
                  §Q (> value INT:0)
                  §ASSIGN value INT:-1
                  §R value
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            ObligationPolicy = ObligationPolicy.Permissive
        };

        Program.Compile(source, "test.calr", options);

        var subtype = Assert.Single(
            options.ObligationResults!.Obligations,
            obligation => obligation.Kind == ObligationKind.Subtype);
        Assert.NotEqual(ObligationStatus.Discharged, subtype.Status);
    }

    [Fact]
    public void CSharpEmit_DischargedObligation_DefaultKeepsGuard()
    {
        // v0.13 default: verification is diagnostic. Without the opt-in, a Discharged
        // obligation keeps its runtime check.
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var genr = new ObligationGenerator(tracker);
        genr.Generate(module);
        foreach (var obl in tracker.Obligations)
        {
            if (obl.Kind == ObligationKind.ProofObligation)
                obl.Status = ObligationStatus.Discharged;
        }

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, tracker);
        var csharp = emitter.Emit(module);

        Assert.Contains("throw new InvalidOperationException", csharp);
        Assert.DoesNotContain("// PROVEN:", csharp);
    }

    [Fact]
    public void CSharpEmit_FailedProofObligation_EmitsRuntimeGuard()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var genr = new ObligationGenerator(tracker);
        genr.Generate(module);

        foreach (var obl in tracker.Obligations)
        {
            if (obl.Kind == ObligationKind.ProofObligation)
                obl.Status = ObligationStatus.Failed;
        }

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, tracker);
        var csharp = emitter.Emit(module);

        Assert.Contains("throw new InvalidOperationException", csharp);
        Assert.DoesNotContain("// PROVEN:", csharp);
    }

    [Fact]
    public void CSharpEmit_UnsupportedProofObligation_EmitsRuntimeGuard()
    {
        // #879 ride-along: an obligation the solver cannot model keeps its runtime
        // check. Pre-fix, Unsupported fell through to the no-guard TODO comment.
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var genr = new ObligationGenerator(tracker);
        genr.Generate(module);

        foreach (var obl in tracker.Obligations)
        {
            if (obl.Kind == ObligationKind.ProofObligation)
                obl.Status = ObligationStatus.Unsupported;
        }

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, tracker);
        var csharp = emitter.Emit(module);

        Assert.Contains("throw new InvalidOperationException", csharp);
        Assert.DoesNotContain("// TODO:", csharp);
        Assert.DoesNotContain("// PROVEN:", csharp);
    }

    [Fact]
    public void CSharpEmit_PendingProofObligation_EmitsRuntimeGuard()
    {
        // #879 ride-along: Pending-with-tracker is reachable (--verify-refinements with
        // Z3 unavailable attaches the tracker without solving) and must keep its guard.
        // The no-guard TODO now means exactly "no tracker ran".
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var genr = new ObligationGenerator(tracker);
        genr.Generate(module);
        // Generate leaves ProofObligation status at Pending — no override needed.

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, tracker);
        var csharp = emitter.Emit(module);

        Assert.Contains("throw new InvalidOperationException", csharp);
        Assert.DoesNotContain("// TODO:", csharp);
    }

    [Fact]
    public void CSharpEmit_NoTracker_EmitsRuntimeGuard()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{p1:check} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // No tracker means no proof exists, so the runtime check must remain.
        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("throw new InvalidOperationException", csharp);
        Assert.DoesNotContain("// TODO: proof obligation", csharp);
    }

    [Fact]
    public void DefaultPolicy_GuardsIncompleteOutcomes()
    {
        var policy = ObligationPolicy.Default;

        Assert.True(ObligationPolicy.RequiresGuard(
            policy.GetAction(ObligationStatus.Timeout)));
        Assert.True(ObligationPolicy.RequiresGuard(
            policy.GetAction(ObligationStatus.Unsupported)));
        Assert.True(ObligationPolicy.RequiresGuard(
            policy.GetAction(ObligationStatus.Pending)));
        Assert.True(ObligationPolicy.RequiresGuard(
            policy.GetAction(ObligationStatus.Boundary)));
    }

    [Fact]
    public void CSharpEmit_InlineRefinedParameter_EmitsEntryGuard()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Use:pub}
                  §I{i32:age} | (>= # INT:0)
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var csharp = new CSharpEmitter().Emit(module);

        Assert.Contains("if (!(age >= 0))", csharp);
        Assert.Contains("ArgumentOutOfRangeException", csharp);
    }

    [Fact]
    public void CSharpEmit_NamedRefinedParameter_ErasesTypeAndEmitsEntryGuard()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f001:Use:pub}
                  §I{Nat:value}
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var csharp = new CSharpEmitter().Emit(module);

        Assert.Contains("void Use(int value)", csharp);
        Assert.Contains("if (!(value >= 0))", csharp);
    }

    [Fact]
    public void Generate_NamedRefinedReturn_CreatesReturnObligation()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f001:Get:pub}
                  §O{Nat}
                  §R INT:1
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);

        var obligation = Assert.Single(tracker.Obligations);
        Assert.Equal(ObligationKind.RefinementReturn, obligation.Kind);
        Assert.Equal("result", obligation.ParameterName);
    }

    [Fact]
    public void CSharpEmit_NamedRefinedReturn_ErasesTypeAndEmitsGuard()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f001:Get:pub}
                  §O{Nat}
                  §R INT:1
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var csharp = new CSharpEmitter().Emit(module);

        Assert.Contains("int Get()", csharp);
        Assert.Contains("if (!(__calorPostconditionResult", csharp);
        Assert.Contains("Return value violates refinement type 'Nat'", csharp);
    }

    [Fact]
    public void Generate_RefinedBinding_CreatesSubtypeObligation()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f001:Use:pub}
                  §O{void}
                  §B{value:Nat} INT:1
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);

        var obligation = Assert.Single(tracker.Obligations);
        Assert.Equal(ObligationKind.Subtype, obligation.Kind);
        Assert.Equal("value", obligation.ParameterName);
    }

    [Fact]
    public void Generate_RefinedAssignment_CreatesSubtypeTransitionObligation()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f001:Use:pub}
                  §O{i32}
                  §B{~value:Nat} INT:1
                  §ASSIGN value INT:-1
                  §R value
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);

        var obligations = tracker.Obligations
            .Where(obligation => obligation.Kind == ObligationKind.Subtype)
            .ToArray();
        Assert.Equal(2, obligations.Length);
        Assert.Contains(
            obligations,
            obligation => obligation.Description.Contains(
                "must preserve refinement type",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CSharpEmit_RefinedBinding_ErasesTypeAndEmitsGuard()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Nat:i32} (>= # INT:0)
              §F{f001:Use:pub}
                  §O{void}
                  §B{value:Nat} INT:1
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var csharp = new CSharpEmitter().Emit(module);

        Assert.Contains("int value = 1;", csharp);
        Assert.Contains("if (!(value >= 0))", csharp);
        Assert.Contains("Value violates refinement type 'Nat'", csharp);
    }

    [Fact]
    public void CSharpEmit_RefinedParameterGuard_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §F{f001:RequirePositive:pub}
                  §I{i32:value} | (> # INT:0)
                  §O{i32}
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "RequirePositive", -1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_NamedAndInlineParameterRefinements_AreConjoined()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Use:pub}
                  §I{Positive:value} | (< # INT:10)
                  §O{i32}
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "Use", -1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Contains("value > 0 && value < 10", csharp);
    }

    [Fact]
    public void NestedNamedRefinements_EraseAndEnforceAllInheritedPredicates()
    {
        const string source = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §RTYPE{r2:Small:Positive} (< # INT:10)
              §F{f001:Use:pub}
                  §I{Small:value}
                  §O{i32}
                  §R value
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);

        var entry = Assert.Single(
            tracker.Obligations,
            obligation => obligation.Kind == ObligationKind.RefinementEntry);
        Assert.IsType<BinaryOperationNode>(entry.Condition);

        var csharp = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker).Emit(module);
        var exception = InvokeGenerated(csharp, "Use", -1);

        Assert.Contains("int Use(int value)", csharp);
        Assert.Contains("value > 0 && value < 10", csharp);
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [SkippableFact]
    public void NestedNamedRefinements_ResolveToConcreteSolverType()
    {
        Skip.IfNot(
            Verification.Z3.Z3ContextFactory.IsAvailable,
            "Z3 not available");
        const string source = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §RTYPE{r2:Small:Positive} (< # INT:10)
              §F{f001:Use:priv}
                  §I{Small:value}
                  §O{void}
                  §Q (> value INT:0)
                  §Q (< value INT:10)
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true
        };

        Program.Compile(source, "test.calr", options);

        var entry = Assert.Single(
            options.ObligationResults!.Obligations,
            obligation => obligation.Kind == ObligationKind.RefinementEntry);
        Assert.Equal(ObligationStatus.Discharged, entry.Status);
    }

    [Fact]
    public void UnboundedInheritedQuantifier_FailsClosedAtRuntime()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:NonNegative:i32} (forall ((i i32)) (>= # INT:0))
              §RTYPE{r2:Small:NonNegative} (< # INT:10)
              §F{f001:Use:pub}
                  §I{Small:value}
                  §O{i32}
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "Use", -1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.DoesNotContain("true /* STATIC ONLY:", csharp);
    }

    [Fact]
    public void SubstituteSelfRef_RecursesThroughConditionalExpressions()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var predicate = new ConditionalExpressionNode(
            span,
            new BoolLiteralNode(span, true),
            new BinaryOperationNode(
                span,
                BinaryOperator.GreaterThan,
                new SelfRefNode(span),
                new IntLiteralNode(span, 0)),
            new BoolLiteralNode(span, false));

        var substituted = FactCollector.SubstituteSelfRefStatic(
            predicate,
            "value");

        Assert.DoesNotContain(
            Calor.Compiler.Analysis.RecursiveAstWalker
                .GetAllChildren(substituted),
            node => node is SelfRefNode);
        var conditional = Assert.IsType<ConditionalExpressionNode>(substituted);
        var comparison = Assert.IsType<BinaryOperationNode>(
            conditional.WhenTrue);
        Assert.Equal(
            "value",
            Assert.IsType<ReferenceNode>(comparison.Left).Name);

        var unsupported = FactCollector.SubstituteSelfRefStatic(
            new SomeExpressionNode(span, new SelfRefNode(span)),
            "value");
        Assert.False(Assert.IsType<BoolLiteralNode>(unsupported).Value);

        var captured = FactCollector.SubstituteSelfRefStatic(
            new ExistsExpressionNode(
                span,
                [new QuantifierVariableNode(span, "value", "i32")],
                new BinaryOperationNode(
                    span,
                    BinaryOperator.Equal,
                    new SelfRefNode(span),
                    new ReferenceNode(span, "value"))),
            "value");
        Assert.False(Assert.IsType<BoolLiteralNode>(captured).Value);
    }

    [Fact]
    public void RefinementElision_RequiresAllConstraintsAndRuntimeAssumptions()
    {
        const string combinedSource = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Mutate:pub}
                  §I{Positive:value} | (< # INT:10)
                  §O{i32}
                  §ASSIGN value INT:20
                  §R value
            """;
        var combinedModule = Parse(combinedSource, out var combinedDiagnostics);
        Assert.False(combinedDiagnostics.HasErrors);
        var combinedTracker = new ObligationTracker();
        new ObligationGenerator(combinedTracker).Generate(combinedModule);

        var assignmentObligations = combinedTracker.Obligations
            .Where(obligation =>
                obligation.Kind == ObligationKind.Subtype
                && obligation.Description.StartsWith(
                    "Assignment to 'value'",
                    StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, assignmentObligations.Length);
        Assert.Contains(
            assignmentObligations,
            obligation => obligation.Description.Contains(
                "inline refinement",
                StringComparison.Ordinal));

        const string contractOffSource = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Mutate:pub}
                  §I{Positive:value}
                  §I{i32:next}
                  §O{i32}
                  §Q (> next INT:0)
                  §ASSIGN value next
                  §R value
            """;
        var contractOffModule = Parse(
            contractOffSource,
            out var contractOffDiagnostics);
        Assert.False(contractOffDiagnostics.HasErrors);
        var contractOffTracker = new ObligationTracker();
        new ObligationGenerator(contractOffTracker).Generate(contractOffModule);
        foreach (var obligation in contractOffTracker.Obligations)
            obligation.Status = ObligationStatus.Discharged;
        var contractOffCSharp = new CSharpEmitter(
            ContractMode.Off,
            null,
            null,
            contractOffTracker)
        {
            ElideProvenGuards = true
        }.Emit(contractOffModule);

        var exception = InvokeGenerated(
            contractOffCSharp,
            "Mutate",
            1,
            -1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.DoesNotContain("Precondition failed", contractOffCSharp);
    }

    [Fact]
    public void OperatorOverload_EnforcesRefinedReturnBoundary()
    {
        const string source = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §CL{c001:MyType:pub}
                §OP{op001:implicit:pub}
                  §I{MyType:value}
                  §O{Positive}
                  §R INT:-1
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        var csharp = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker).Emit(module);

        Assert.Contains(
            tracker.Obligations,
            obligation => obligation.FunctionId == "op001"
                && obligation.Kind == ObligationKind.RefinementReturn);
        var assembly = CompileGenerated(csharp);
        var type = Assert.Single(
            assembly.GetTypes(),
            candidate => candidate.GetMethod("op_Implicit") is not null);
        var instance = Activator.CreateInstance(type);
        var invocation = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => type.GetMethod("op_Implicit")!.Invoke(
                null,
                [instance]));

        Assert.IsType<InvalidOperationException>(invocation.InnerException);

        var legacyCSharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §CL{c001:LegacyType:pub}
                §MT{m001:op_Implicit:pub:stat}
                  §I{LegacyType:value}
                  §O{Positive}
                  §R INT:-1
            """);
        var legacyAssembly = CompileGenerated(legacyCSharp);
        var legacyType = Assert.Single(
            legacyAssembly.GetTypes(),
            candidate => candidate.GetMethod("op_Implicit") is not null);
        var legacyInstance = Activator.CreateInstance(legacyType);
        var legacyInvocation =
            Assert.Throws<System.Reflection.TargetInvocationException>(
                () => legacyType.GetMethod("op_Implicit")!.Invoke(
                    null,
                    [legacyInstance]));

        Assert.IsType<InvalidOperationException>(
            legacyInvocation.InnerException);
    }

    [Fact]
    public void LambdaReturn_DoesNotInheritEnclosingRefinedReturnGuard()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Test:pub}
                  §I{bool:early}
                  §O{Positive}
                  §B{f:Func<i32>} §LAM{l1} §R INT:-1 §/LAM{l1}
                  §IF{i1} early
                    §R INT:1
                  §B{x:i32} §C{f} §/C
                  §R INT:1
            """);
        var assembly = CompileGenerated(csharp);
        var type = Assert.Single(
            assembly.GetTypes(),
            candidate => candidate.GetMethod("Test") is not null);

        var result = type.GetMethod("Test")!.Invoke(null, [false]);

        Assert.Equal(1, result);
    }

    [Fact]
    public void EnumExtension_EnforcesRefinedReturnBoundary()
    {
        const string source = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §EN{e001:Color}
                Red
              §EEXT{x001:Color}
                §F{f001:Bad:pub}
                  §I{Color:self}
                  §O{Positive}
                  §R INT:-1
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        var csharp = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker).Emit(module);
        var assembly = CompileGenerated(csharp);
        var method = Assert.Single(
            assembly.GetTypes()
                .SelectMany(type => type.GetMethods())
                .Where(candidate => candidate.Name == "Bad"));
        var colorType = Assert.Single(
            assembly.GetTypes(),
            type => type.IsEnum && type.Name == "Color");
        var invocation = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method.Invoke(
                null,
                [Enum.ToObject(colorType, 0)]));

        Assert.Contains(
            tracker.Obligations,
            obligation => obligation.FunctionId == "f001"
                && obligation.Kind == ObligationKind.RefinementReturn);
        Assert.IsType<InvalidOperationException>(invocation.InnerException);
    }

    [Fact]
    public void SiblingRefinementBindings_DoNotElideLiveAssignmentGuard()
    {
        const string source = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §RTYPE{r2:Negative:i32} (< # INT:0)
              §F{f001:Bad:pub}
                  §I{bool:flag}
                  §O{i32}
                  §IF{i1} flag
                    §B{~x:Positive} INT:1
                  §IF{i2} (== flag BOOL:false)
                    §B{~x:Negative} INT:-1
                    §ASSIGN x INT:1
                    §R x
                  §R INT:0
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        foreach (var obligation in tracker.Obligations)
            obligation.Status = ObligationStatus.Discharged;
        var csharp = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker)
        {
            ElideProvenGuards = true
        }.Emit(module);

        var exception = InvokeGenerated(csharp, "Bad", false);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_RefinedReturnGuard_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:BadReturn:pub}
                  §O{Positive}
                  §R INT:-1
            """);

        var exception = InvokeGenerated(csharp, "BadReturn");

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void CSharpEmit_NestedRefinedReturnGuard_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:BadNestedReturn:pub}
                  §I{bool:takeBadPath}
                  §O{Positive}
                  §IF{i1} takeBadPath
                    §R INT:-1
                  §R INT:1
            """);

        var exception = InvokeGenerated(csharp, "BadNestedReturn", true);

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void CSharpEmit_RefinedBindingGuard_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:BadBinding:pub}
                  §O{i32}
                  §B{value:Positive} INT:-1
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "BadBinding");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_RefinedAssignmentGuard_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:BadAssignment:pub}
                  §O{i32}
                  §B{~value:Positive} INT:1
                  §ASSIGN value INT:-1
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "BadAssignment");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_UnannotatedRefinedRebind_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:BadRebind:pub}
                  §O{i32}
                  §B{~value:Positive} INT:1
                  §B{~value} INT:-1
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "BadRebind");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_InlineRefinedParameterAssignment_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §F{f001:BadParameterWrite:pub}
                  §I{i32:value} | (> # INT:0)
                  §O{i32}
                  §ASSIGN value INT:-1
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "BadParameterWrite", 1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_NestedSelfReference_CompilesAndRejectsInvalidValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:NonEmpty:str} (> (len #) INT:0)
              §F{f001:Use:pub}
                  §I{NonEmpty:value}
                  §O{void}
            """);

        var exception = InvokeGenerated(csharp, "Use", "");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.DoesNotContain("__self__", csharp);
    }

    [Fact]
    public void CSharpEmit_AsyncRefinedReturn_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §AF{f001:BadAsyncReturn:pub}
                  §O{Positive}
                  §R INT:-1
            """);

        var exception = InvokeGenerated(csharp, "BadAsyncReturn");

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void CSharpEmit_RefinedOutParameter_GuardsAfterAssignment()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §F{f001:SetBad:pub}
                  §I{i32:value:out} | (> # INT:0)
                  §O{void}
                  §ASSIGN value INT:-1
            """);

        var exception = InvokeGenerated(csharp, "SetBad", (object?)null);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_IndexedRead_EnforcesLogicalBoundAtRuntime()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:Read:pub}
                  §I{Sized:items}
                  §I{i32:n}
                  §I{i32:index}
                  §O{i32}
                  §R §IDX items index
            """);

        var exception = InvokeGenerated(
            csharp,
            "Read",
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            5,
            7);

        Assert.IsType<IndexOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_IndexedWrite_EnforcesLogicalBoundAtRuntime()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:Write:pub}
                  §I{Sized:items}
                  §I{i32:n}
                  §I{i32:index}
                  §O{void}
                  §SETIDX{items} index INT:42
            """);

        var exception = InvokeGenerated(
            csharp,
            "Write",
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            5,
            7);

        Assert.IsType<IndexOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_ArrayAccessAssignmentTarget_EnforcesLogicalBound()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:Write:pub}
                  §I{Sized:items}
                  §I{i32:n}
                  §I{i32:index}
                  §O{void}
                  §ASSIGN §IDX items index INT:42
            """);

        var exception = InvokeGenerated(
            csharp,
            "Write",
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            5,
            7);

        Assert.IsType<IndexOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_IndexedAlias_PreservesLogicalBoundAtRuntime()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:ReadAlias:pub}
                  §I{Sized:items}
                  §I{i32:n}
                  §I{i32:index}
                  §O{i32}
                  §B{alias:i32[]} items
                  §R §IDX alias index
            """);

        var exception = InvokeGenerated(
            csharp,
            "ReadAlias",
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            5,
            7);

        Assert.IsType<IndexOutOfRangeException>(exception);
    }

    [SkippableFact]
    public void NestedSelfSubtypeObligation_IsTranslatedRatherThanUnsupported()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = """
            §M{m001:Test}
              §RTYPE{r1:NonEmpty:str} (> (len #) INT:0)
              §F{f001:Assign:priv}
                  §I{NonEmpty:value}
                  §I{str:other}
                  §O{void}
                  §ASSIGN value other
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            ObligationPolicy = ObligationPolicy.Permissive
        };
        var result = Program.Compile(source, "test.calr", options);
        Assert.False(result.HasErrors);

        var subtype = Assert.Single(
            options.ObligationResults!.Obligations,
            obligation => obligation.Kind == ObligationKind.Subtype);
        Assert.NotEqual(ObligationStatus.Unsupported, subtype.Status);
    }

    [Fact]
    public void SiblingIndexedAliases_DoNotShareLogicalSizeFacts()
    {
        var source = """
            §M{m001:Test}
              §ITYPE{it1:SizedN:i32[]:n}
              §ITYPE{it2:SizedM:i32[]:m}
              §F{f001:Read:priv}
                  §I{SizedN:left}
                  §I{SizedM:right}
                  §I{i32:n}
                  §I{i32:m}
                  §I{i32:index}
                  §O{i32}
                  §IF{i1} (> n INT:0)
                    §B{alias:i32[]} left
                    §B{x:i32} §IDX alias index
                  §EL
                    §B{alias:i32[]} right
                    §B{y:i32} §IDX alias index
                  §R INT:0
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);

        var indexObligations = tracker.Obligations
            .Where(obligation => obligation.Kind == ObligationKind.IndexBounds)
            .ToArray();
        Assert.Equal(2, indexObligations.Length);
        Assert.All(
            indexObligations,
            obligation => Assert.Contains(
                "runtime collection bounds",
                obligation.Description));
    }

    [SkippableFact]
    public void MethodNestedProof_UsesBranchFact()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = """
            §M{m001:Test}
              §CL{c001:Checker:pub}
                §MT{m001:Check:pub}
                  §I{i32:x}
                  §O{void}
                  §IF{if1} (> x INT:0)
                    §PROOF{p1} (> x INT:0)
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            ObligationPolicy = ObligationPolicy.Permissive
        };
        var result = Program.Compile(source, "test.calr", options);
        Assert.False(result.HasErrors);

        var proof = Assert.Single(
            options.ObligationResults!.Obligations,
            obligation => obligation.Kind == ObligationKind.ProofObligation);
        Assert.Equal(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void CSharpEmit_MissingIndexedSizeWitness_FailsClosed()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:Read:pub}
                  §I{Sized:items}
                  §I{i32:index}
                  §O{i32}
                  §R §IDX items index
            """);

        var exception = InvokeGenerated(
            csharp,
            "Read",
            new[] { 0, 1, 2 },
            1);

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("size witness 'n' is unavailable", exception.Message);
    }

    [Fact]
    public void CSharpEmit_LocalIndexedBindingWithoutWitness_FailsClosed()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:Read:pub}
                  §I{i32:index}
                  §O{i32}
                  §B{items:Sized} §ARR{i32} INT:3
                  §R §IDX items index
            """);

        var exception = InvokeGenerated(csharp, "Read", 1);

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("size witness 'n' is unavailable", exception.Message);
    }

    [Fact]
    public void CSharpEmit_IndexedIncrement_EnforcesLogicalBound()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:Increment:pub}
                  §I{Sized:items}
                  §I{i32:n}
                  §I{i32:index}
                  §O{void}
                  (inc §IDX items index)
            """);

        var exception = InvokeGenerated(
            csharp,
            "Increment",
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            5,
            7);

        Assert.IsType<IndexOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_RefinedDecrement_RechecksInvariant()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Bad:pub}
                  §O{i32}
                  §B{~value:Positive} INT:1
                  (dec value)
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "Bad");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_UninitializedRefinedBinding_RejectsInvalidDefault()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Bad:pub}
                  §O{i32}
                  §B{value:Positive}
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "Bad");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_NestedRefinedDecrement_RechecksInvariant()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Bad:pub}
                  §O{i32}
                  §B{~value:Positive} INT:1
                  §B{old:i32} (post-dec value)
                  §R value
            """);

        var exception = InvokeGenerated(csharp, "Bad");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_RefinedIterator_GuardsYieldedValues()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Get:pub}
                  §O{Positive}
                  §YIELD INT:-1
            """);

        var exception = InvokeGenerated(csharp, "Get");

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Yielded value violates", exception.Message);
    }

    [Fact]
    public void CSharpEmit_PublicRefinedConstructor_RejectsInvalidValue()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §CL{c001:Box:pub}
                §CTOR{ctor1:pub} (Positive:value)
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        var csharp = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker).Emit(module);

        var exception = InvokeGeneratedConstructor(csharp, -1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Contains(
            tracker.Obligations,
            obligation => obligation.Kind == ObligationKind.RefinementEntry);
    }

    [Fact]
    public void CSharpEmit_RefinedRefParameterMutation_CompilesAndThrows()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §F{f001:Mutate:pub}
                  §I{i32:value:ref} | (> # INT:0)
                  §O{void}
                  (dec value)
            """);

        var exception = InvokeGenerated(csharp, "Mutate", 1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_FailedRefinedWrites_DoNotCommitInvalidState()
    {
        var assignment = Emit("""
            §M{m001:Test}
              §F{f001:Mutate:pub}
                  §I{i32:value:ref} | (> # INT:0)
                  §O{void}
                  §ASSIGN value INT:-1
            """);
        object?[] assignmentArguments = [1];

        var assignmentException = InvokeGenerated(
            assignment,
            "Mutate",
            assignmentArguments);

        Assert.IsType<ArgumentOutOfRangeException>(assignmentException);
        Assert.Equal(1, assignmentArguments[0]);

        var decrement = Emit("""
            §M{m001:Test}
              §F{f001:Mutate:pub}
                  §I{i32:value:ref} | (> # INT:0)
                  §O{void}
                  (dec value)
            """);
        object?[] decrementArguments = [1];

        var decrementException = InvokeGenerated(
            decrement,
            "Mutate",
            decrementArguments);

        Assert.IsType<ArgumentOutOfRangeException>(decrementException);
        Assert.Equal(1, decrementArguments[0]);
    }

    [Fact]
    public void CSharpEmit_FailedTypedRebind_RollsBackNestedTargetMutation()
    {
        var csharp = Emit("""
            §M{m001:Test}
              §RTYPE{r1:NonNegative:i32} (>= # INT:0)
              §RTYPE{r2:Negative:i32} (< # INT:0)
              §F{f001:Mutate:pub}
                  §I{NonNegative:value:ref}
                  §O{void}
                  §B{~value:Negative} (post-dec value)
            """);
        object?[] arguments = [1];

        var exception = InvokeGenerated(csharp, "Mutate", arguments);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(1, arguments[0]);
    }

    [Fact]
    public void CSharpEmit_TypedRebind_PreservesPriorContractAndSupportsOutInitialization()
    {
        const string caughtRebindSource = """
            §M{m001:Test}
              §RTYPE{r1:NonNegative:i32} (>= # INT:0)
              §RTYPE{r2:Negative:i32} (< # INT:0)
              §F{f001:Mutate:pub}
                  §I{NonNegative:value:ref}
                  §O{void}
                  §TR{t1}
                    §B{~value:Negative} (post-dec value)
                  §CA{Exception:ex}
                    §ASSIGN value INT:-1
            """;
        var caughtRebind = Emit(caughtRebindSource);
        object?[] caughtArguments = [1];

        var caughtException = InvokeGenerated(
            caughtRebind,
            "Mutate",
            caughtArguments);

        Assert.IsType<ArgumentOutOfRangeException>(caughtException);
        Assert.Equal(1, caughtArguments[0]);

        var caughtModule = Parse(caughtRebindSource, out var caughtDiagnostics);
        Assert.False(caughtDiagnostics.HasErrors);
        var caughtTracker = new ObligationTracker();
        new ObligationGenerator(caughtTracker).Generate(caughtModule);
        Assert.Contains(
            caughtTracker.Obligations,
            obligation => obligation.Description.Contains(
                "Assignment to 'value' must preserve refinement type 'NonNegative'",
                StringComparison.Ordinal));

        var outInitialization = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §F{f001:Set:pub}
                  §I{Positive:value:out}
                  §O{void}
                  §B{~value:Positive} INT:1
            """);
        var validation = GeneratedCSharpCompiler.Validate(outInitialization);

        Assert.True(
            validation.CompilationSuccess,
            string.Join(Environment.NewLine, validation.CompilationErrors));
        Assert.DoesNotContain("__refinementSnapshot", outInitialization);

        var initializedOut = Emit("""
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §RTYPE{r2:Negative:i32} (< # INT:0)
              §F{f001:Set:pub}
                  §I{Positive:value:out}
                  §O{void}
                  §B{~value:Positive} INT:2
                  §TR{t1}
                    §B{~value:Negative} (post-dec value)
                  §CA{Exception:ex}
                    §ASSIGN value INT:2
            """);
        object?[] initializedOutArguments = [null];

            var initializedOutAssembly = CompileGenerated(initializedOut);
            var initializedOutType = Assert.Single(
                initializedOutAssembly.GetTypes(),
                candidate => candidate.GetMethod("Set") is not null);
            initializedOutType.GetMethod("Set")!.Invoke(
                null,
                initializedOutArguments);

            Assert.Equal(2, initializedOutArguments[0]);

            var establishingRebind = Emit("""
                §M{m001:Test}
                  §RTYPE{r1:Positive:i32} (> # INT:0)
                  §F{f001:Mutate:pub}
                      §O{i32}
                      §B{~value:i32} INT:1
                      §B{~value:Positive} INT:2
                      §ASSIGN value INT:-1
                      §R value
                """);

            var establishingException = InvokeGenerated(
                establishingRebind,
                "Mutate");

            Assert.IsType<ArgumentOutOfRangeException>(establishingException);
    }

    [Fact]
    public void CSharpEmit_ConstructorInitializerChecksBeforeForwarding()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:NonEmpty:str} (> (len #) INT:0)
              §CL{c001:AppException:pub}
                §EXT{Exception}
                §CTOR{ctor1:pub}
                  §I{NonEmpty:message}
                  §BASE
                    §A message
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        var csharp = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker).Emit(module);

        var exception = InvokeGeneratedConstructor(csharp, "");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Contains(" : base((message.Length > 0 ?", csharp);
    }

    [Fact]
    public void CSharpEmit_ZeroArgumentConstructorInitializerWithRefinement_FailsClosed()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:NonEmpty:str} (> (len #) INT:0)
              §CL{c001:AppException:pub}
                §EXT{Exception}
                §CTOR{ctor1:pub}
                  §I{NonEmpty:message}
                  §BASE
            """;
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors,
            $"Errors: {string.Join(", ", parseDiagnostics.Select(d => d.Message))}");
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        var diagnostics = new DiagnosticBag();

        _ = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker,
            diagnostics).Emit(module);

        var diagnostic = Assert.Single(
            diagnostics,
            item => item.Code
                == DiagnosticCode.ConstructorRefinementInitializerNotLowered);
        Assert.True(diagnostic.IsError);

        Assert.Throws<InvalidOperationException>(
            () => new CSharpEmitter(
                ContractMode.Debug,
                null,
                null,
                tracker).Emit(module));
    }

    [Fact]
    public void CSharpEmit_LambdaParameterShadowsOuterIndexedBound()
    {
        var source = """
            §M{m001:Test}
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:Shadow:pub}
                  §I{Sized:items}
                  §I{i32:n}
                  §I{i32:index}
                  §O{void}
                  §B{read:Func<i32[], i32>} §LAM{l1:items:i32[]} §IDX items index §/LAM{l1}
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);

        var csharp = new CSharpEmitter().Emit(module);
        var validation = GeneratedCSharpCompiler.Validate(csharp);

        Assert.True(
            validation.CompilationSuccess,
            string.Join(Environment.NewLine, validation.CompilationErrors));
        Assert.DoesNotContain("Indexed-type bound violated", csharp);
        var indexObligation = Assert.Single(
            tracker.Obligations,
            obligation => obligation.Kind == ObligationKind.IndexBounds);
        Assert.Contains("runtime collection bounds", indexObligation.Description);
    }

    [Fact]
    public void CSharpEmit_StatementLambda_ContainsItsProofGuard()
    {
        var span = new TextSpan(0, 1, 1, 1);
        var condition = new BinaryOperationNode(
            span,
            BinaryOperator.GreaterThan,
            new ReferenceNode(span, "x"),
            new IntLiteralNode(span, 0));
        var lambda = new LambdaExpressionNode(
            span,
            "l1",
            [new LambdaParameterNode(span, "x", "i32")],
            effects: null,
            isAsync: false,
            expressionBody: null,
            statementBody:
            [
                new ProofObligationNode(
                    span,
                    "p1",
                    null,
                    condition,
                    new AttributeCollection())
            ],
            attributes: new AttributeCollection());

        var csharp = new CSharpEmitter().Visit(lambda);

        Assert.Contains("=>", csharp);
        Assert.Contains("Proof obligation [p1] violated", csharp);
        Assert.Contains("if (!(x > 0))", csharp);
    }

    [Fact]
    public void CSharpEmit_ElidesAllDischargedGuardKindsWhenOptedIn()
    {
        var source = """
            §M{m001:Test}
              §RTYPE{r1:Positive:i32} (> # INT:0)
              §ITYPE{it1:Sized:i32[]:n}
              §F{f001:All:priv}
                  §I{Positive:value}
                  §I{Sized:items}
                  §I{i32:n}
                  §I{i32:index}
                  §O{Positive}
                  §B{local:Positive} value
                  §R §IDX items index
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        foreach (var obligation in tracker.Obligations)
            obligation.Status = ObligationStatus.Discharged;
        var emitter = new CSharpEmitter(
            ContractMode.Debug,
            null,
            null,
            tracker)
        {
            ElideProvenGuards = true
        };

        var csharp = emitter.Emit(module);

        Assert.DoesNotContain("ArgumentOutOfRangeException", csharp);
        Assert.DoesNotContain("Return value violates", csharp);
        Assert.DoesNotContain("Indexed-type bound violated", csharp);
    }

    private static string Emit(string source)
    {
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        return new CSharpEmitter().Emit(module);
    }

    private static Exception InvokeGenerated(
        string csharp,
        string methodName,
        params object?[] arguments)
    {
        var assembly = CompileGenerated(csharp);
        var type = Assert.Single(assembly.GetTypes(), candidate =>
            candidate.GetMethod(
                methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) is not null);
        var method = type.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;

        try
        {
            var result = method.Invoke(null, arguments);
            if (result is Task task)
            {
                try
                {
                    task.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }
            else if (result is System.Collections.IEnumerable enumerable)
            {
                try
                {
                    foreach (var _ in enumerable)
                    {
                    }
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }
        }
        catch (System.Reflection.TargetInvocationException invocation)
            when (invocation.InnerException is not null)
        {
            return invocation.InnerException;
        }

        throw new Xunit.Sdk.XunitException(
            $"Generated method '{methodName}' did not throw.");
    }

    private static Exception InvokeGeneratedConstructor(
        string csharp,
        params object?[] arguments)
    {
        var assembly = CompileGenerated(csharp);
        var type = Assert.Single(
            assembly.GetTypes(),
            candidate => candidate.GetConstructors().Any(
                constructor => constructor.GetParameters().Length == arguments.Length));
        try
        {
            Activator.CreateInstance(type, arguments);
        }
        catch (System.Reflection.TargetInvocationException invocation)
            when (invocation.InnerException is not null)
        {
            return invocation.InnerException;
        }

        throw new Xunit.Sdk.XunitException(
            $"Generated constructor for '{type.FullName}' did not throw.");
    }

    private static System.Reflection.Assembly CompileGenerated(string csharp)
    {
        var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + csharp);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path));
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            $"ObligationRuntime_{Guid.NewGuid():N}",
            [syntaxTree],
            references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));

        return System.Reflection.Assembly.Load(stream.ToArray());
    }
}
