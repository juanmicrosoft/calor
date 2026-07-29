using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// Drives the committed verification-outcome fixture corpus
/// (tests/TestData/Verification/Outcomes/, loop plan D1.5) through the full
/// compile+verify pipeline and asserts that each fixture produces its expected
/// choke-point status with the envelope guarantees: refuted carries a concrete
/// model, timeout/unsupported are surfaced as diagnostics (no silent cliffs),
/// and every contract diagnostic carries a verification payload.
/// </summary>
public class OutcomeCorpusTests
{
    private static string CorpusDir()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(projectRoot, "tests", "TestData", "Verification", "Outcomes");
    }

    private static CompilationResult CompileFixture(string fileName, uint timeoutMs = 5000, bool verbose = false)
    {
        var path = Path.Combine(CorpusDir(), fileName);
        var source = File.ReadAllText(path);
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            Verbose = verbose,
            StatusWriter = TextWriter.Null,
            VerificationTimeoutMs = timeoutMs,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
        };
        return Program.Compile(source, path, options);
    }

    private static IEnumerable<Diagnostic> ContractDiagnostics(CompilationResult result)
        => result.Diagnostics.Where(d => d.Verification != null);

    [SkippableFact]
    public void ProvenFixture_ReportsProvenWithPayload()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = CompileFixture("proven.calr", verbose: true);

        var proven = ContractDiagnostics(result)
            .Single(d => d.Code == DiagnosticCode.PostconditionProven);
        Assert.Equal(ProofStatus.Proven, proven.Verification!.Status);
        Assert.Equal("proven", proven.Verification.StatusName);
        Assert.Null(proven.Verification.Counterexample);
    }

    [SkippableFact]
    public void RefutedFixture_CarriesConcreteModel()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = CompileFixture("refuted-with-model.calr");

        var refuted = ContractDiagnostics(result)
            .Single(d => d.Code == DiagnosticCode.PostconditionMayBeViolated);
        Assert.Equal(ProofStatus.Refuted, refuted.Verification!.Status);
        Assert.Equal("refuted", refuted.Verification.StatusName);

        // The envelope guarantee (M-E2): refuted carries the concrete Z3 model
        var model = refuted.Verification.Counterexample;
        Assert.NotNull(model);
        Assert.NotEmpty(model.Bindings);
        Assert.Contains(model.Bindings, b => b.Name == "result");
        Assert.StartsWith("Counterexample:", model.Render());
    }

    [SkippableFact]
    public void UnsupportedFixture_SurfacesDiagnosis()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = CompileFixture("unsupported.calr");

        var unsupported = ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.ContractVerificationUnsupported)
            .ToList();
        Assert.NotEmpty(unsupported);
        Assert.All(unsupported, d =>
        {
            Assert.Equal(ProofStatus.Unsupported, d.Verification!.Status);
            Assert.Contains("not supported", d.Verification.Reason);
        });
    }

    [SkippableFact]
    public void TimeoutFixture_DistinguishedFromUnknown()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = CompileFixture("timeout.calr", timeoutMs: 1);

        var timedOut = ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.ContractVerificationTimeout)
            .ToList();
        Assert.NotEmpty(timedOut);
        Assert.All(timedOut, d =>
        {
            Assert.Equal(ProofStatus.Timeout, d.Verification!.Status);
            Assert.Equal("timeout", d.Verification.StatusName);
        });
    }

    [SkippableFact]
    public void EveryContractDiagnostic_CarriesVerificationPayload()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // M-E3 over the corpus: every contract-band diagnostic reports one of
        // the five statuses — no silent cliffs.
        string[] contractCodes =
        [
            DiagnosticCode.PreconditionMayBeViolated,
            DiagnosticCode.PostconditionMayBeViolated,
            DiagnosticCode.PostconditionProven,
            DiagnosticCode.ContractVerificationInconclusive,
            DiagnosticCode.ContractVerificationTimeout,
            DiagnosticCode.ContractVerificationUnsupported,
            DiagnosticCode.VacuousPrecondition
        ];
        string[] fixtures =
        [
            "proven.calr", "refuted-with-model.calr", "unsupported.calr", "timeout.calr",
            "proven-with-result.calr", "refuted-overflow.calr", "unsupported-body.calr",
            "vacuous-precondition.calr"
        ];

        foreach (var fixture in fixtures)
        {
            var timeout = fixture == "timeout.calr" ? 1u : 5000u;
            var result = CompileFixture(fixture, timeoutMs: timeout, verbose: true);
            var contractDiags = result.Diagnostics.Where(d => contractCodes.Contains(d.Code)).ToList();
            Assert.NotEmpty(contractDiags);
            Assert.All(contractDiags, d => Assert.NotNull(d.Verification));
        }
    }

    // ------------------------------------------------------------------
    // Guarantees plan WS-G1 fixtures (D-G1.4): result binding, honest
    // refutation, never-refute-free-result, and vacuity.
    // ------------------------------------------------------------------

    [SkippableFact]
    public void ProvenWithResultFixture_ResultBoundPostconditionsProve()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // The #807 regression pin, proving direction: result-referencing
        // postconditions over encodable bodies (single-return, if/else,
        // elseif chains) are Proven — not refuted against a free result.
        var result = CompileFixture("proven-with-result.calr", verbose: true);

        Assert.Empty(ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.PostconditionMayBeViolated));

        var proven = ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.PostconditionProven)
            .ToList();
        Assert.Equal(4, proven.Count);
        Assert.All(proven, d => Assert.Equal(ProofStatus.Proven, d.Verification!.Status));
        Assert.All(proven, d => Assert.False(d.Verification!.IsVacuous));
    }

    [SkippableFact]
    public void RefutedOverflowFixture_CarriesGenuineModels()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // Honest refutations: with result bound to the body, the only remaining
        // counterexamples are genuine two's-complement overflows, and each must
        // carry a concrete model (M-E2).
        var result = CompileFixture("refuted-overflow.calr");

        var refuted = ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.PostconditionMayBeViolated)
            .ToList();
        Assert.Equal(2, refuted.Count);
        Assert.All(refuted, d =>
        {
            Assert.Equal(ProofStatus.Refuted, d.Verification!.Status);
            var model = d.Verification.Counterexample;
            Assert.NotNull(model);
            Assert.Contains(model.Bindings, b => b.Name == "result");
        });
    }

    [SkippableFact]
    public void UnsupportedBodyFixture_NeverRefutesAgainstFreeResult()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // The #807 regression pin, refuting direction: a result-referencing
        // postcondition over a body outside the encodable surface must be
        // Unsupported — never Refuted with a fabricated model.
        var result = CompileFixture("unsupported-body.calr");

        Assert.Empty(ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.PostconditionMayBeViolated));

        var unsupported = ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.ContractVerificationUnsupported)
            .ToList();
        Assert.NotEmpty(unsupported);
        Assert.All(unsupported, d =>
        {
            Assert.Equal(ProofStatus.Unsupported, d.Verification!.Status);
            Assert.Contains("result", d.Verification.Reason);
        });
    }

    [SkippableFact]
    public void VacuousPreconditionFixture_ProvenVacuousAndLoud()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // D-G1.3: a jointly-unsatisfiable precondition set makes the
        // postcondition Proven(vacuous) — flagged, warned, never elidable.
        var result = CompileFixture("vacuous-precondition.calr");

        var vacuous = ContractDiagnostics(result)
            .Single(d => d.Code == DiagnosticCode.VacuousPrecondition);
        Assert.Equal(ProofStatus.Proven, vacuous.Verification!.Status);
        Assert.True(vacuous.Verification.IsVacuous);
        Assert.Equal(DiagnosticSeverity.Warning, vacuous.Severity);
    }

    // ------------------------------------------------------------------
    // The `unknown` status is not source-fixturable (see the corpus README):
    // covered at evidence level against the choke point directly.
    // ------------------------------------------------------------------

    [Fact]
    public void SolverUnavailableEvidence_AssignsUnknown()
    {
        var outcome = ProofOutcome.Assign(
            ProofEvidence.SolverUnavailable("Z3 native library not found"));

        Assert.Equal(ProofStatus.Unknown, outcome.Status);
        Assert.Equal("unknown", outcome.StatusName);
        Assert.Null(outcome.Counterexample);
        Assert.Contains("not found", outcome.Reason);
    }

    [Fact]
    public void RehydratedUnknown_RoundTripsWireName()
    {
        var outcome = ProofOutcome.Rehydrate("unknown", null, "smt tactic gave up");

        Assert.Equal(ProofStatus.Unknown, outcome.Status);
        Assert.Equal("unknown", outcome.StatusName);
        Assert.Equal("smt tactic gave up", outcome.Reason);
    }

    [Fact]
    public void RehydratedRefuted_RestoresStructuredModel()
    {
        var outcome = ProofOutcome.Rehydrate(
            "refuted",
            [new CounterexampleBinding("x", "1")],
            null);

        Assert.Equal(ProofStatus.Refuted, outcome.Status);
        Assert.NotNull(outcome.Counterexample);
        Assert.Equal("Counterexample: x=1", outcome.Counterexample.Render());
    }

    [Fact]
    public void RehydratedVacuousProven_KeepsVacuousFlag()
    {
        // D-G1.3: the vacuous flag must survive persistence — a rehydrated
        // vacuous proof must still never elide runtime checks.
        var outcome = ProofOutcome.Rehydrate("proven", null, "vacuous set", isVacuous: true);

        Assert.Equal(ProofStatus.Proven, outcome.Status);
        Assert.True(outcome.IsVacuous);
    }

    [Fact]
    public void VacuousProofEvidence_AssignsProvenVacuous()
    {
        var outcome = ProofOutcome.Assign(ProofEvidence.VacuousProof("unsat preconditions"));

        Assert.Equal(ProofStatus.Proven, outcome.Status);
        Assert.True(outcome.IsVacuous);
        Assert.Null(outcome.Counterexample);
        Assert.Contains("unsat", outcome.Reason);
    }
}
