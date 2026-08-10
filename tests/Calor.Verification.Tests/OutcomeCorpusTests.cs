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
            ElideProvenGuards = true,
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

        var contractDiagnostics = ContractDiagnostics(result).ToList();
        Assert.NotEmpty(contractDiagnostics);

        // This used to assert NotEmpty(timedOut) — i.e. it required the solver to actually MISS a
        // 1 ms budget. That is a wall-clock race: on a fast or lightly-loaded machine Z3 answers
        // inside the millisecond, the list is empty, and the test fails for reasons that have
        // nothing to do with the property. Observed failing in exactly that way, intermittently,
        // and it is the only wall-clock-dependent assertion in this assembly.
        //
        // The property worth pinning is the one the test is named for: a timeout is DISTINGUISHED
        // from unknown — never silently collapsed into it — and every outcome carries its payload.
        // That holds whether or not the budget is actually exceeded on this run.
        Assert.All(contractDiagnostics, d =>
        {
            Assert.NotNull(d.Verification);

            if (d.Code == DiagnosticCode.ContractVerificationTimeout)
            {
                Assert.Equal(ProofStatus.Timeout, d.Verification!.Status);
                Assert.Equal("timeout", d.Verification.StatusName);
            }
            else
            {
                // The distinction under test: nothing that did not time out may claim to have,
                // and a decided outcome must name a status other than `timeout`.
                Assert.NotEqual(ProofStatus.Timeout, d.Verification!.Status);
                Assert.NotEqual("timeout", d.Verification.StatusName);
            }
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
            DiagnosticCode.VacuousPrecondition,
            DiagnosticCode.ContractVerificationAssumed
        ];
        string[] fixtures =
        [
            "proven.calr", "refuted-with-model.calr", "unsupported.calr", "timeout.calr",
            "proven-with-result.calr", "refuted-overflow.calr", "unsupported-body.calr",
            "vacuous-precondition.calr", "assumed-division.calr",
            "proven-with-binding.calr", "refuted-with-binding.calr"
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
    public void ProvenWithBindingFixture_W5BShapesProve()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // D-G3.1: the three W5-B probe contract shapes — immutable §B chains with
        // guard-clause branching — prove via SSA substitution. This is the depth
        // surface PP-G3's threshold structurally depends on.
        var result = CompileFixture("proven-with-binding.calr", verbose: true);

        Assert.Empty(ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.PostconditionMayBeViolated));
        Assert.Empty(ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.ContractVerificationUnsupported));

        var proven = ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.PostconditionProven)
            .ToList();
        Assert.Equal(3, proven.Count);
        Assert.All(proven, d => Assert.Equal(ProofStatus.Proven, d.Verification!.Status));
    }

    [SkippableFact]
    public void RefutedWithBindingFixture_DefectiveW5BShapeRefutesWithModel()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // #825 review m1: A-1.3's feasibility-by-determinism claim for the
        // Guarantees probe's surfacing half rests on the DEFECTIVE W5-B shapes
        // refuting deterministically with a model — this fixture makes that a
        // CI fact instead of an assertion.
        var result = CompileFixture("refuted-with-binding.calr");

        var refuted = ContractDiagnostics(result)
            .Single(d => d.Code == DiagnosticCode.PostconditionMayBeViolated);
        Assert.Equal(ProofStatus.Refuted, refuted.Verification!.Status);
        var model = refuted.Verification.Counterexample;
        Assert.NotNull(model);
        Assert.Contains(model.Bindings, b => b.Name == "result");
    }

    [SkippableFact]
    public void AssumedDivisionFixture_AssumedWithNamedAssumption()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // D-G2.5: Assumed's first producer — a division-carrying proof reports
        // `assumed` with the canonical exceptional-paths assumption, warns via
        // Calor0720, and never elides the runtime check.
        var result = CompileFixture("assumed-division.calr");

        var assumed = ContractDiagnostics(result)
            .Single(d => d.Code == DiagnosticCode.ContractVerificationAssumed);
        Assert.Equal(ProofStatus.Assumed, assumed.Verification!.Status);
        Assert.Contains(Z3Verifier.ExceptionalPathDivisionAssumption, assumed.Verification.Assumptions);

        Assert.Empty(ContractDiagnostics(result)
            .Where(d => d.Code == DiagnosticCode.PostconditionMayBeViolated));

        // Assumed never elides: the ensures guard must be in the generated C#.
        Assert.Contains("ContractKind.Ensures", result.GeneratedCode);
        Assert.DoesNotContain("// PROVEN: Postcondition", result.GeneratedCode);
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
    public void SolverUnavailableEvidence_AssignsUnavailable()
    {
        // D-G2.2: "no solver" is its own status, split from "solver gave up".
        var outcome = ProofOutcome.Assign(
            ProofEvidence.SolverUnavailable("Z3 native library not found"));

        Assert.Equal(ProofStatus.Unavailable, outcome.Status);
        Assert.Equal("unavailable", outcome.StatusName);
        Assert.Null(outcome.Counterexample);
        Assert.Contains("not found", outcome.Reason);
    }

    [Fact]
    public void AssumedProofEvidence_AssignsAssumedWithSortedAssumptions()
    {
        // D-G2.1: assumed carries its named assumption set, canonically sorted;
        // it is not Proven and maps to no legacy Proven-equivalent.
        var outcome = ProofOutcome.Assign(ProofEvidence.AssumedProof(
            "proof conditional on undischarged assumptions",
            ["zeta-assumption", "alpha-assumption"]));

        Assert.Equal(ProofStatus.Assumed, outcome.Status);
        Assert.Equal("assumed", outcome.StatusName);
        Assert.Equal(["alpha-assumption", "zeta-assumption"], outcome.Assumptions);
        Assert.Equal(Calor.Compiler.Verification.Z3.ContractVerificationStatus.Unproven, outcome.ToContractStatus());
    }

    [Fact]
    public void RehydratedAssumed_RestoresAssumptions()
    {
        var outcome = ProofOutcome.Rehydrate("assumed", null, "conditional", assumptions: ["a1"]);

        Assert.Equal(ProofStatus.Assumed, outcome.Status);
        Assert.Equal(["a1"], outcome.Assumptions);
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

    [Fact]
    public void AssumedProofEvidence_RejectsEmptyAssumptions()
    {
        // Schema 2.0 guarantees `assumptions` is non-empty on assumed (G2 review m3).
        Assert.Throws<ArgumentException>(() => ProofEvidence.AssumedProof("reason", []));
    }

    [Fact]
    public void RehydratedAssumedWithoutAssumptions_DegradesToUnknown()
    {
        // A stale/hand-edited persistence entry must not mint an Assumed outcome
        // that violates the non-empty envelope guarantee (G2 review m3).
        var outcome = ProofOutcome.Rehydrate("assumed", null, "conditional", assumptions: null);

        Assert.Equal(ProofStatus.Unknown, outcome.Status);
        Assert.Empty(outcome.Assumptions);
    }

    [Fact]
    public void JsonEnvelope_CarriesVacuousAndAssumptions()
    {
        // G2 review C1: the primary JSON envelope (not just SARIF) must carry the
        // schema-2.0 payload additions.
        var vacuous = Calor.Compiler.Diagnostics.DiagnosticEnvelope.BuildVerification(
            ProofOutcome.Rehydrate("proven", null, "vacuous set", isVacuous: true));
        Assert.NotNull(vacuous);
        Assert.True(vacuous.Vacuous);
        Assert.Null(vacuous.Assumptions);

        var assumed = Calor.Compiler.Diagnostics.DiagnosticEnvelope.BuildVerification(
            ProofOutcome.Rehydrate("assumed", null, "conditional", assumptions: ["b-assumption", "a-assumption"]));
        Assert.NotNull(assumed);
        Assert.Null(assumed.Vacuous);
        Assert.Equal(["a-assumption", "b-assumption"], assumed.Assumptions);

        var plain = Calor.Compiler.Diagnostics.DiagnosticEnvelope.BuildVerification(
            ProofOutcome.Rehydrate("proven", null, null));
        Assert.NotNull(plain);
        Assert.Null(plain.Vacuous);
        Assert.Null(plain.Assumptions);

        // Wire form: absent-when-null, present otherwise
        var json = System.Text.Json.JsonSerializer.Serialize(assumed);
        Assert.Contains("\"Assumptions\"", json);
        var plainJson = System.Text.Json.JsonSerializer.Serialize(plain);
        Assert.DoesNotContain("Vacuous", plainJson);
        Assert.DoesNotContain("Assumptions", plainJson);
    }
}
