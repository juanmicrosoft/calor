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
        var tokens = lexer.TokenizeAll();
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
            §/F{f001}
            §/M{m001}
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
    public void Generate_InlineRefinement_CreatesRefinementEntryObligation()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:age} | (>= # INT:0)
              §O{void}
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, tracker);
        var csharp = emitter.Emit(module);

        Assert.Contains("// PROVEN:", csharp);
        Assert.DoesNotContain("// TODO:", csharp);
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
            §/F{f001}
            §/M{m001}
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
    public void CSharpEmit_NoTracker_EmitsTodoComment()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x}
              §O{void}
              §PROOF{p1:check} (>= x INT:0)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // No tracker — should emit TODO comment (M0 behavior)
        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("// TODO: proof obligation", csharp);
    }
}
