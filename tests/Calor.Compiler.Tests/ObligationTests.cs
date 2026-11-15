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
            module Test Test block
              func Main (int:x) → void
                !
                proof{"positive"} (x >= 0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Single(tracker.Obligations);
        var obl = tracker.Obligations[0];
        Assert.Equal(ObligationKind.ProofObligation, obl.Kind);
        Assert.Equal(Assert.Single(module.Functions).Id, obl.FunctionId);
        Assert.Contains("positive", obl.Description);
        Assert.Equal(ObligationStatus.Pending, obl.Status);
    }

    [Fact]
    public void Generate_NestedProofObligation_CreatesObligation()
    {
        var source = """
            module Test Test block
              func Main (int:x) → void
                !
                if (x > 0)
                  proof{"positive"} (x > 0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);

        var obligation = Assert.Single(tracker.Obligations);
        Assert.Equal(ObligationKind.ProofObligation, obligation.Kind);
        var proof = Assert.Single(
            Calor.Compiler.Analysis.RecursiveAstWalker.DescendantsAndSelf(module).OfType<ProofObligationNode>());
        Assert.Equal(proof.Id, obligation.SourceProofId);
        Assert.Equal(proof.Span, obligation.Span);
    }

    [Fact]
    public void Generate_InlineRefinement_CreatesRefinementEntryObligation()
    {
        var source = """
            module Test Test block
              func Main (int:age | (# >= 0)) → void
                !
                return
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Single(tracker.Obligations);
        var obl = tracker.Obligations[0];
        Assert.Equal(ObligationKind.RefinementEntry, obl.Kind);
        Assert.Equal(Assert.Single(module.Functions).Id, obl.FunctionId);
        // Public function -> Boundary status
        Assert.Equal(ObligationStatus.Boundary, obl.Status);
    }

    [Fact]
    public void Generate_PrivateFunctionInlineRefinement_IsBoundary()
    {
        var source = """
            module Test Test block
              func Helper private (int:age | (# >= 0)) → void
                return
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        Assert.Single(tracker.Obligations);
        var obl = tracker.Obligations[0];
        Assert.Equal(ObligationKind.RefinementEntry, obl.Kind);
        Assert.Equal(ObligationStatus.Boundary, obl.Status);
    }

    [Fact]
    public void Generate_NoRefinements_NoObligations()
    {
        var source = """
            module Test Test block
              func Main (int:x) → void
                !
                return
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
            module Test Test block
              func Add private (int:x) → void
                requires (x >= 0)
                proof{"non-neg"} (x >= 0)
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

        // (== (len "\u00e9") 1) holds in .NET; since #1413 literals reach Z3 per UTF-16 code unit,
        // so the solver proves it too. The proof is still carried by the string theory (null
        // strings are unmodeled), so it must stay Assumed, never Discharged.
        var source = """
            module Test Test block
              func Check private (int:x) → void
                proof{"bytelen"} (("é").Length == 1)
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
            module Test Test block
              func Check private (int:x) → void
                proof{"numeric"} ((1 + 1) == 2)
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
            module Test Test block
              func Check private (int:x) → void
                proof{"always-positive"} (x > 0)
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
    public void Solve_InlineRefinementWithPrecondition_KeepsEntryBoundary()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Private function with precondition that guarantees the inline refinement.
        // The # in (>= # INT:0) should resolve to 'age' via PushSelfVariable.
        var source = """
            module Test Test block
              func Validate private (int:age | (# >= 0)) → void
                requires (age >= 0)
                return
            """;

        var options = new CompilationOptions { VerifyRefinements = true };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        var refObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.RefinementEntry);
        Assert.NotNull(refObl);
        Assert.Equal("age", refObl.ParameterName);
        Assert.Equal(ObligationStatus.Boundary, refObl.Status);
        Assert.Null(refObl.Outcome);
    }

    [SkippableFact]
    public void Solve_InlineRefinementWithoutPrecondition_IsNotACallerRefutation()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // A private declaration does not universally claim that every input is valid.
        var source = """
            module Test Test block
              func Validate private (int:age | (# >= 0)) → void
                return
            """;

        var options = new CompilationOptions { VerifyRefinements = true };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        var refObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.RefinementEntry);
        Assert.NotNull(refObl);
        Assert.Equal(ObligationStatus.Boundary, refObl.Status);
        Assert.Null(refObl.CounterexampleDescription);
        Assert.False(result.HasErrors);
    }

    // ───── Obligation Metadata ─────

    [Fact]
    public void Generate_SetsParameterNameOnRefinementEntry()
    {
        var source = """
            module Test Test block
              func Main (int:myParam | (# >= 0)) → void
                !
                return
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
            module Test Test block
              func Main (int:x) → void
                !
                proof{"check"} (x >= 0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        var obl = Assert.Single(tracker.Obligations);
        var proof = Assert.Single(
            Calor.Compiler.Analysis.RecursiveAstWalker.DescendantsAndSelf(module).OfType<ProofObligationNode>());
        Assert.Equal(proof.Id, obl.SourceProofId);
        Assert.Equal(proof.Span, obl.Span);
    }

    [Fact]
    public void CompilePipeline_WithoutVerifyRefinements_NoObligations()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
              §F{Main:pub}
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
            module Test Test block
              func Main (int:x) → void
                !
                proof{"check"} (x >= 0)
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
            module Test Test block
              func Main (int:x) → void
                !
                proof{"check"} (x >= 0)
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
            module Test Test block
              func Bad () → void
                !
                var int:x = 1
                if (x > 0)
                  set x -1
                  proof (x > 0)
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
        var sourceProof = Assert.Single(
            Calor.Compiler.Analysis.RecursiveAstWalker.DescendantsAndSelf(result.Ast!).OfType<ProofObligationNode>());
        Assert.Equal(sourceProof.Id, proof.SourceProofId);
        Assert.Equal(sourceProof.Span, proof.Span);
        Assert.Contains($"Proof obligation [{sourceProof.Id}] violated", result.GeneratedCode);
        Assert.DoesNotContain("// PROVEN:", result.GeneratedCode);
    }

    private const string DischargedObligationSource = """
        module Test Test block
          func Main (int:x) → void
            !
            proof{"check"} (x >= 0)
        """;

    private static ObligationTracker DischargedTracker(ModuleNode module)
    {
        var tracker = new ObligationTracker();
        new ObligationGenerator(tracker).Generate(module);
        foreach (var obl in tracker.Obligations)
        {
            if (obl.Kind == ObligationKind.ProofObligation)
                obl.Status = ObligationStatus.Discharged;
        }

        return tracker;
    }

    [Fact]
    public void CSharpEmit_DischargedObligation_DefaultElidesGuard()
    {
        // v0.15 default (roadmap §4.5): a Discharged obligation drops its runtime
        // check without any opt-in — the emitter's ElideProvenGuards defaults to true.
        var module = Parse(DischargedObligationSource, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        var tracker = DischargedTracker(module);

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, tracker);
        var csharp = emitter.Emit(module);

        Assert.DoesNotContain("throw new InvalidOperationException", csharp);
        var proof = Assert.Single(
            Calor.Compiler.Analysis.RecursiveAstWalker.DescendantsAndSelf(module).OfType<ProofObligationNode>());
        Assert.Contains($"// PROVEN: proof obligation [{proof.Id}: check]", csharp);
    }

    [Fact]
    public void CSharpEmit_DischargedObligation_OptOutKeepsGuard()
    {
        // Opt-out (--keep-proven-guards / ElideProvenGuards = false): the verdict is
        // diagnostic and the Discharged obligation keeps its runtime check.
        var module = Parse(DischargedObligationSource, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        var tracker = DischargedTracker(module);

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, tracker)
        {
            ElideProvenGuards = false
        };
        var csharp = emitter.Emit(module);

        Assert.Contains("throw new InvalidOperationException", csharp);
        Assert.DoesNotContain("// PROVEN:", csharp);
    }

    [Fact]
    public void CSharpEmit_FailedProofObligation_EmitsRuntimeGuard()
    {
        var source = """
            module Test Test block
              func Main (int:x) → void
                !
                proof{"check"} (x >= 0)
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
            module Test Test block
              func Main (int:x) → void
                !
                proof{"check"} (x >= 0)
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
            module Test Test block
              func Main (int:x) → void
                !
                proof{"check"} (x >= 0)
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
            module Test Test block
              func Main (int:x) → void
                !
                proof{"check"} (x >= 0)
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
            module Test Test block
              func Use (int:age | (# >= 0)) → void
                !
                return
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var csharp = new CSharpEmitter().Emit(module);

        Assert.Contains("if (!(age >= 0))", csharp);
        Assert.Contains("ArgumentOutOfRangeException", csharp);
    }

    [Fact]
    public void CSharpEmit_RefinedParameterGuard_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            module Test Test block
              func RequirePositive (int:value | (# > 0)) → int
                !
                return value
            """);

        var exception = InvokeGenerated(csharp, "RequirePositive", -1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
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
            new ThrowExpressionNode(span, new SelfRefNode(span)),
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
    public void CSharpEmit_InlineRefinedParameterAssignment_RejectsInvalidRuntimeValue()
    {
        var csharp = Emit("""
            module Test Test block
              func BadParameterWrite (int:value | (# > 0)) → int
                !
                set value -1
                return value
            """);

        var exception = InvokeGenerated(csharp, "BadParameterWrite", 1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_RefinedOutParameter_GuardsAfterAssignment()
    {
        var csharp = Emit("""
            §M{Test}
              §F{SetBad:pub}
                  §I{i32:value:out} | (> # INT:0)
                  §O{void}
                  §ASSIGN value INT:-1
            """);

        var exception = InvokeGenerated(csharp, "SetBad", (object?)null);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [SkippableFact]
    public void MethodNestedProof_UsesBranchFact()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = """
            module Test Test block
              class Checker
                func Check (int:x) → void
                  !
                  if (x > 0)
                    proof (x > 0)
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
    public void CSharpEmit_RefinedRefParameterMutation_CompilesAndThrows()
    {
        var csharp = Emit("""
            module Test Test block
              func Mutate (ref int:value | (# > 0)) → void
                !
                set value (value - 1)
            """);

        var exception = InvokeGenerated(csharp, "Mutate", 1);

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CSharpEmit_FailedRefinedWrites_DoNotCommitInvalidState()
    {
        var assignment = Emit("""
            module Test Test block
              func Mutate (ref int:value | (# > 0)) → void
                !
                set value -1
            """);
        object?[] assignmentArguments = [1];

        var assignmentException = InvokeGenerated(
            assignment,
            "Mutate",
            assignmentArguments);

        Assert.IsType<ArgumentOutOfRangeException>(assignmentException);
        Assert.Equal(1, assignmentArguments[0]);

        var decrement = Emit("""
            module Test Test block
              func Mutate (ref int:value | (# > 0)) → void
                !
                set value (value - 1)
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
        var references = TrustedPlatformReferences.All;
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
