using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// #1407 (milestone 0.24 R0) — the committed evidence contract and artifact inventory are valid, and
/// the validator fails closed on unsupported, stale, retrospective, vacuous, and incomparable
/// evidence. Each negative control mutates one fact of a known-good input and asserts the specific
/// violation code, so a validator that rejected everything would fail the positive controls and a
/// validator that accepted everything would fail the negative ones.
/// </summary>
public class EvidenceContractTests
{
    private const string PacketDir = "docs/plans/evidence/evidence-contract-1407";
    private const string ContractPath = PacketDir + "/contract.json";
    private const string Candidate = "1111111111111111111111111111111111111111";
    private const string SemanticsVersion = "z3-executable-semantics-v2";

    // ------------------------------------------------------------------
    // Positive controls: the committed packet
    // ------------------------------------------------------------------

    [Fact]
    public void CommittedContractIsValid()
    {
        var violations = EvidenceContractValidator.ValidateContract(Contract());
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void CommittedInventoryIsValid()
    {
        var violations = EvidenceContractValidator.ValidateInventory(Contract(), Inventory());
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void CommittedContractLifecycleIsConsistent()
    {
        // Either PROPOSED (R0 PR and its merge commit: NOT-MET, capacity PROPOSED) or FROZEN after
        // the acceptance write-back (MET, capacity ACCEPTED, acceptance record); C008 and C012
        // reject every mixed state, and CommittedContractIsValid runs them on the committed packet.
        var contract = Contract();
        var status = contract["status"]!.GetValue<string>();
        Assert.Contains(status, new[] { "PROPOSED", "FROZEN" });
        Assert.Equal(status == "FROZEN" ? "MET" : "NOT-MET", contract["gateStatus"]!.GetValue<string>());
        Assert.Equal(status == "FROZEN" ? "ACCEPTED" : "PROPOSED",
            contract["authorityCapacity"]!["capacity"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void CommittedVocabularyMatchesTheVerifierStatusEnums()
    {
        // Every status the verifier can emit must be named by at least one frozen token, so no
        // emitted status is left unclassified. The legacy enums are lossy (for example
        // ContractVerificationStatus.Unproven receives both Assumed and TimeoutOrUnavailable), so a
        // legacy value may appear under more than one token; consumers read ProofOutcome instead.
        var codeStatuses = Contract()["outcomeVocabulary"]!["outcomes"]!.AsArray()
            .SelectMany(o => o!["codeStatuses"]!.AsArray().Select(s => s!.GetValue<string>()))
            .ToList();

        foreach (var name in Enum.GetNames<Calor.Compiler.Verification.ProofStatus>())
            Assert.Contains(codeStatuses, s => s.StartsWith($"ProofStatus.{name}", StringComparison.Ordinal));
        foreach (var name in Enum.GetNames<Calor.Compiler.Verification.Z3.ContractVerificationStatus>())
            Assert.Contains(codeStatuses, s => s.Contains($"ContractVerificationStatus.{name}", StringComparison.Ordinal));
        foreach (var name in Enum.GetNames<Calor.Compiler.Verification.Obligations.ObligationStatus>())
            Assert.Contains(codeStatuses, s => s.Contains($"ObligationStatus.{name}", StringComparison.Ordinal));
        foreach (var name in Enum.GetNames<Calor.Compiler.Verification.Z3.ImplicationStatus>())
            Assert.Contains(codeStatuses, s => s.Contains($"ImplicationStatus.{name}", StringComparison.Ordinal));
        foreach (var name in Enum.GetNames<Calor.Compiler.Verification.Z3.KInduction.KInductionStatus>())
            Assert.Contains(codeStatuses, s => s.Contains($"KInductionStatus.{name}", StringComparison.Ordinal));
    }

    [Fact]
    public void PacketHashesMatchSha256Manifest()
    {
        var root = RepoRoot();
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, PacketDir, "sha256.json")))!;
        var files = manifest["files"]!.AsObject();
        var coverage = EvidenceContractValidator.ValidatePacketManifest(Contract(), manifest, ContractPath);
        Assert.True(coverage.Count == 0, Describe(coverage));

        var mismatches = new List<string>();
        foreach (var (path, expected) in files)
        {
            var actual = Sha256Lf(Path.Combine(root, path));
            if (actual != expected!.GetValue<string>())
                mismatches.Add($"\"{path}\": \"{actual}\"");
        }
        Assert.True(mismatches.Count == 0,
            "Contract packet changed without updating sha256.json (and, after merge, the amendment log). "
            + "Expected values:" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void WellFormedEstablishedRowPasses()
    {
        var violations = Rows(Row());
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void WellFormedComparableBenchmarkRowPasses()
    {
        var violations = Rows(BenchmarkRow());
        Assert.True(violations.Count == 0, Describe(violations));
    }

    // ------------------------------------------------------------------
    // Outcome vocabulary: missingness and unsupported outcomes fail closed
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Assumed")]
    [InlineData("TimeoutOrUnavailable")]
    [InlineData("Unsupported")]
    [InlineData("Failed")]
    [InlineData("Boundary")]
    [InlineData("ProvenVacuous")]
    [InlineData("skipped")]
    [InlineData("flaky")]
    [InlineData("crashed")]
    [InlineData("invalid")]
    [InlineData("not-investigated")]
    [InlineData("unclassified")]
    [InlineData("incomparable")]
    [InlineData("historical")]
    [InlineData("stale")]
    public void NonEstablishingOutcomeCountedAsEstablishedFails(string outcome)
    {
        var row = Row();
        row["outcome"] = outcome;
        AssertViolation(Rows(row), "E002");
    }

    [Theory]
    [InlineData("Assumed")]
    [InlineData("TimeoutOrUnavailable")]
    [InlineData("skipped")]
    public void NonEstablishingOutcomeRecordedAsNotEstablishedPasses(string outcome)
    {
        var row = Row();
        row["outcome"] = outcome;
        row["counted"] = "not-established";
        var violations = Rows(row);
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Theory]
    [InlineData("Refuted")]
    [InlineData("Timeout")]
    [InlineData("Unknown")]
    [InlineData("Unavailable")]
    [InlineData("proven")]
    [InlineData("")]
    public void UnfrozenOutcomeTokenFailsClosed(string outcome)
    {
        // Raw code names and wire names must be mapped to a frozen token by the producer; the
        // validator never guesses the mapping.
        var row = Row();
        row["outcome"] = outcome;
        row["counted"] = "not-established";
        AssertViolation(Rows(row), "E001");
    }

    [Fact]
    public void VacuousProvenCountedAsEstablishedFails()
    {
        var row = Row();
        row["vacuous"] = true;
        AssertViolation(Rows(row), "E003");
    }

    [Fact]
    public void ProvenWithoutExplicitVacuityFails()
    {
        var row = Row();
        row.Remove("vacuous");
        AssertViolation(Rows(row), "E003");
    }

    [Fact]
    public void DischargedWithoutRegisteredNonVacuityCheckFails()
    {
        var row = Row();
        row["outcome"] = "Discharged";
        AssertViolation(Rows(row), "E003");

        // A bare boolean is not a registered check.
        row["nonVacuityChecked"] = true;
        AssertViolation(Rows(row), "E003");

        row["nonVacuityCheck"] = new JsonObject { ["id"] = "nv-1", ["result"] = "failed" };
        AssertViolation(Rows(row), "E003");

        row["nonVacuityCheck"] = new JsonObject { ["id"] = "nv-1", ["result"] = "passed" };
        var violations = Rows(row);
        Assert.True(violations.Count == 0, Describe(violations));
    }

    // ------------------------------------------------------------------
    // §4 establishment conditions the row itself must evidence
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("registrationId")]
    [InlineData("producer")]
    [InlineData("oracle")]
    [InlineData("translatorSemanticsVersion")]
    [InlineData("unresolvedFalseProof")]
    public void EstablishedRowMissingPrerequisiteFails(string field)
    {
        var row = Row();
        row.Remove(field);
        AssertViolation(Rows(row), "E013");
    }

    [Fact]
    public void DisagreeingOracleFails()
    {
        var row = Row();
        row["oracle"]!["agrees"] = false;
        AssertViolation(Rows(row), "E013");
    }

    [Fact]
    public void OtherTranslatorSemanticsVersionFails()
    {
        var row = Row();
        row["translatorSemanticsVersion"] = "z3-executable-semantics-v1";
        AssertViolation(Rows(row), "E013");
    }

    [Fact]
    public void UnknownCandidateSemanticsVersionFails()
    {
        AssertViolation(EvidenceContractValidator.ValidateEvidenceRows(
            Contract(), Inventory(), [Row()], Candidate, candidateSemanticsVersion: null), "E013");
    }

    [Fact]
    public void UnresolvedFalseProofFails()
    {
        var row = Row();
        row["unresolvedFalseProof"] = true;
        AssertViolation(Rows(row), "E013");
    }

    [Fact]
    public void ArtifactWithOpenDefectNeedsResolution()
    {
        // verifier-runtime-differential carries the #1135 defect in the committed inventory.
        var row = Row();
        row.Remove("openDefectsResolvedBy");
        AssertViolation(Rows(row), "E014");
    }

    // ------------------------------------------------------------------
    // Stale, historical, retrospective, and off-candidate evidence
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("benchmark-results")]          // stale in the committed inventory
    [InlineData("tier2-corpus-verification")]  // stale in the committed inventory
    [InlineData("llm-and-agent-results")]      // historical-only in the committed inventory
    [InlineData("roundtrip-reports")]          // derived in the committed inventory
    public void NonAuthoritativeArtifactUsedAsAuthoritativeFails(string artifact)
    {
        var row = Row();
        row["artifact"] = artifact;
        // Some of these also carry open defects; E014 may accompany E005.
        AssertViolation(Rows(row), "E005", "E014");
    }

    [Fact]
    public void UnknownArtifactFails()
    {
        var row = Row();
        row["artifact"] = "not-in-inventory";
        AssertViolation(Rows(row), "E004");
    }

    [Fact]
    public void EstablishedRowWithoutFrozenCandidateFails()
    {
        AssertViolation(EvidenceContractValidator.ValidateEvidenceRows(
            Contract(), Inventory(), [Row()], candidateCommit: null, SemanticsVersion), "E006");
    }

    [Fact]
    public void RowFromAnotherCommitFails()
    {
        var row = Row();
        row["sourceCommit"] = "2222222222222222222222222222222222222222";
        AssertViolation(Rows(row), "E006");
    }

    [Fact]
    public void ShortShaSourceCommitFails()
    {
        var row = Row();
        row["sourceCommit"] = "c2a8816d";
        // A short SHA is also not the frozen candidate, so E006 accompanies E012.
        AssertViolation(Rows(row), "E012", "E006");
    }

    [Theory]
    [InlineData("2026-09-15T17:11:53Z")] // v0.22.0 release: before the cutoff
    [InlineData("2026-10-01T17:36:19Z")] // exactly the cutoff
    public void RetrospectiveRowCannotBeConfirmatory(string recordedAt)
    {
        var row = Row();
        row["recordedAtUtc"] = recordedAt;
        AssertViolation(Rows(row), "E007");
    }

    [Fact]
    public void SupportedAdjudicationUnderReducedIndependenceFails()
    {
        var row = Row();
        row["adjudication"] = "SUPPORTED";
        AssertViolation(Rows(row), "E011");
    }

    [Fact]
    public void UnknownAdjudicationOutcomeFails()
    {
        var row = Row();
        row["adjudication"] = "PASS";
        AssertViolation(Rows(row), "E011");
    }

    // ------------------------------------------------------------------
    // Benchmark equivalence and comparability
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("pairManifestSha256")]
    [InlineData("metricSetSha256")]
    [InlineData("metricImplementationVersion")]
    [InlineData("aggregationMethod")]
    [InlineData("samplingUnit")]
    [InlineData("runCount")]
    [InlineData("generatorVersion")]
    [InlineData("exclusionsSha256")]
    public void ChangedComparabilityFieldIsIncomparable(string field)
    {
        var row = BenchmarkRow();
        // Hash fields change to a different valid hash, so only the equality rule can reject them.
        row["benchmark"]!["comparability"]![field] = field switch
        {
            "runCount" => 1,
            _ when field.EndsWith("Sha256", StringComparison.Ordinal) => Hash('d'),
            _ => "changed",
        };
        // A changed sampling unit is also not the frozen one, so E010 may accompany E008.
        AssertViolation(Rows(row), "E008", "E010");
    }

    [Fact]
    public void MissingComparabilityFieldIsIncomparable()
    {
        var row = BenchmarkRow();
        row["benchmark"]!["comparability"]!.AsObject().Remove("pairManifestSha256");
        AssertViolation(Rows(row), "E008");
    }

    [Theory]
    [InlineData("UNCLASSIFIED")]
    [InlineData("NOT-EQUIVALENT")]
    [InlineData("EXCLUDED-PRE-REGISTERED")]
    public void IncludedNonEquivalentPairBlocksHeadline(string disposition)
    {
        var row = BenchmarkRow();
        row["benchmark"]!["pairs"]![0]!["disposition"] = disposition;
        AssertViolation(Rows(row), "E009");
    }

    [Fact]
    public void UnknownPairDispositionFails()
    {
        var row = BenchmarkRow();
        row["benchmark"]!["pairs"]![0]!["disposition"] = "LOOKS-SIMILAR";
        AssertViolation(Rows(row), "E009");
    }

    [Fact]
    public void RepeatedDeterministicRunsAreNotASamplingUnit()
    {
        var row = BenchmarkRow();
        row["benchmark"]!["samplingUnit"] = "run";
        AssertViolation(Rows(row), "E010");
    }

    [Theory]
    [InlineData("pairId")]
    [InlineData("calorPath")]
    [InlineData("calorSha256")]
    [InlineData("csharpPath")]
    [InlineData("csharpSha256")]
    [InlineData("taskStatementSha256")]
    [InlineData("inputSet")]
    [InlineData("expectedOutputs")]
    [InlineData("failureBehavior")]
    [InlineData("disposition")]
    [InlineData("equivalenceEvidence")]
    [InlineData("reviewer")]
    public void PairMissingRequiredFieldFails(string field)
    {
        var row = BenchmarkRow();
        row["benchmark"]!["pairs"]![0]!.AsObject().Remove(field);
        AssertViolation(Rows(row), "E009");
    }

    [Fact]
    public void UnclassifiedPairWithoutInclusionFlagFails()
    {
        var row = BenchmarkRow();
        var pair = row["benchmark"]!["pairs"]![0]!.AsObject();
        pair["disposition"] = "UNCLASSIFIED";
        pair.Remove("included");
        AssertViolation(Rows(row), "E009");
    }

    [Fact]
    public void EmptyPairDenominatorFails()
    {
        var row = BenchmarkRow();
        row["benchmark"]!["pairs"] = new JsonArray();
        AssertViolation(Rows(row), "E009");
    }

    [Fact]
    public void MissingComparabilityFieldWithoutComparisonFails()
    {
        var row = BenchmarkRow();
        row["benchmark"]!.AsObject().Remove("comparedTo");
        row["benchmark"]!["comparability"]!.AsObject().Remove("metricSetSha256");
        AssertViolation(Rows(row), "E008");
    }

    [Theory]
    [InlineData("calorSha256")]
    [InlineData("taskStatementSha256")]
    public void PairWithShortHashFails(string field)
    {
        var row = BenchmarkRow();
        row["benchmark"]!["pairs"]![0]![field] = "11";
        AssertViolation(Rows(row), "E009");
    }

    [Fact]
    public void PairWithWrongTypeIdentityFails()
    {
        var row = BenchmarkRow();
        row["benchmark"]!["pairs"]![0]!["csharpSha256"] = new JsonObject();
        AssertViolation(Rows(row), "E009");
    }

    [Theory]
    [InlineData("calorPath", "bool")]
    [InlineData("pairId", "object")]
    [InlineData("reviewer", "bool")]
    [InlineData("inputSet", "number")]
    public void PairFieldWithWrongJsonTypeFails(string field, string kind)
    {
        var row = BenchmarkRow();
        row["benchmark"]!["pairs"]![0]![field] = kind switch
        {
            "bool" => JsonValue.Create(false),
            "number" => JsonValue.Create(7),
            _ => new JsonObject { ["x"] = 1 },
        };
        AssertViolation(Rows(row), "E009");
    }

    [Fact]
    public void ComparabilityKeysAgreeingOnWrongSamplingUnitFail()
    {
        // Both keys agree with each other, but on the wrong unit; the outer field still says program-pair.
        var row = BenchmarkRow();
        row["benchmark"]!["comparability"]!["samplingUnit"] = "run";
        row["benchmark"]!["comparedTo"]!["samplingUnit"] = "run";
        AssertViolation(Rows(row), "E010");
    }

    [Fact]
    public void ComparabilityKeyWithShortHashFails()
    {
        var row = BenchmarkRow();
        row["benchmark"]!.AsObject().Remove("comparedTo");
        row["benchmark"]!["comparability"]!["pairManifestSha256"] = "aa";
        AssertViolation(Rows(row), "E008");
    }

    // A benchmark row that is not counted as an established proof still feeds headlines and
    // adjudication, so it meets the artifact, candidate, and freshness rules.

    [Fact]
    public void DecisionBearingBenchmarkOnStaleArtifactFails()
    {
        var row = BenchmarkRow();
        row["artifact"] = "benchmark-results";
        AssertViolation(Rows(row), "E005", "E014"); // benchmark-results also has two open defects
    }

    [Fact]
    public void DecisionBearingBenchmarkFromAnotherCommitFails()
    {
        var row = BenchmarkRow();
        row["sourceCommit"] = "2222222222222222222222222222222222222222";
        AssertViolation(Rows(row), "E006");
    }

    [Fact]
    public void DecisionBearingBenchmarkBeforeCutoffFails()
    {
        var row = BenchmarkRow();
        row["recordedAtUtc"] = "2026-09-10T00:00:00Z";
        AssertViolation(Rows(row), "E007");
    }

    [Fact]
    public void HistoricalBenchmarkRowIsNotHeldToCandidateRules()
    {
        // The pre-cut benchmark-results.json, recorded as historical, is retained without being
        // decision-bearing.
        var row = BenchmarkRow();
        row["outcome"] = "historical";
        row["artifact"] = "benchmark-results";
        row["sourceCommit"] = "2222222222222222222222222222222222222222";
        row["recordedAtUtc"] = "2026-09-10T00:00:00Z";
        var violations = Rows(row);
        Assert.True(violations.Count == 0, Describe(violations));
    }

    // ------------------------------------------------------------------
    // Terminal record (#1408): the machine-checkable success conditions
    // ------------------------------------------------------------------

    [Fact]
    public void WellFormedSuccessRecordPasses()
    {
        var violations = Terminal(SuccessRecord());
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void UnregisteredClaimFails()
    {
        var record = SuccessRecord();
        record["adjudications"]!.AsArray().Add(Adjudication("claim:new-advantage", "BOUNDED"));
        AssertViolation(Terminal(record), "T003");
    }

    [Fact]
    public void OmittedRegisteredClaimFails()
    {
        // Dropping a registered claim (for example, one that would have been BLOCKED) fails success.
        var record = SuccessRecord();
        record["adjudications"]!.AsArray().Remove(Subject(record, RegisteredClaim));
        AssertViolation(Terminal(record), "T001");
    }

    [Fact]
    public void BlockedRegisteredClaimFails()
    {
        var record = SuccessRecord();
        Subject(record, RegisteredClaim)["outcome"] = "BLOCKED";
        AssertViolation(Terminal(record), "T001");
    }

    [Fact]
    public void StaleArtifactAdjudicatedBoundedFails()
    {
        // Restore benchmark-results' committed stale classification in the repaired inventory: a
        // BOUNDED adjudication of a still-stale artifact is rejected; it can only be BLOCKED.
        var inventory = RepairedInventory();
        inventory["artifacts"]!.AsArray().First(a => a!["id"]!.GetValue<string>() == "benchmark-results")!
            ["classification"] = "stale";
        AssertViolation(Terminal(SuccessRecord(), inventory), "T003");
    }

    [Fact]
    public void SuccessWithNonSweepBlockerFails()
    {
        // A blocker outside the #1419 sweep (the release quality reports) still fails success.
        var record = SuccessRecord();
        Subject(record, "release-quality-reports")["outcome"] = "BLOCKED";
        AssertViolation(Terminal(record), "T001");
    }

    [Fact]
    public void SuccessWithBlockerMarkedNonCriticalStillFails()
    {
        // Criticality is fixed by the contract; a record-supplied flag cannot launder a blocker.
        var record = SuccessRecord();
        var row = Subject(record, "release-quality-reports");
        row["outcome"] = "BLOCKED";
        row["releaseCritical"] = false;
        AssertViolation(Terminal(record), "T001");
    }

    [Theory]
    [InlineData("release-quality-reports")]
    [InlineData("gate:#1311")]
    public void SuccessMissingRequiredSubjectFails(string subject)
    {
        var record = SuccessRecord();
        record["adjudications"]!.AsArray().Remove(Subject(record, subject));
        AssertViolation(Terminal(record), "T001");
    }

    [Fact]
    public void ImproperHistoricalOnlyDemotionFails()
    {
        // release-quality-reports is derived, not historical-only; missing evidence must be BLOCKED.
        var record = SuccessRecord();
        Subject(record, "release-quality-reports")["outcome"] = "HISTORICAL-ONLY";
        AssertViolation(Terminal(record), "T003");
    }

    [Fact]
    public void DuplicateSubjectFails()
    {
        var record = SuccessRecord();
        record["adjudications"]!.AsArray().Add(Adjudication("test-manifest", "BOUNDED"));
        AssertViolation(Terminal(record), "T003");
    }

    [Fact]
    public void UnknownSubjectFails()
    {
        var record = SuccessRecord();
        record["adjudications"]!.AsArray().Add(Adjudication("some-artifact", "BOUNDED"));
        AssertViolation(Terminal(record), "T003");
    }

    [Fact]
    public void SuccessWithoutReducedIndependenceFails()
    {
        var record = SuccessRecord();
        record["adjudicationIndependence"] = "independent";
        AssertViolation(Terminal(record), "T002");
    }

    [Fact]
    public void SuccessWithoutPublishedLimitationFails()
    {
        var record = SuccessRecord();
        record.Remove("limitation");
        AssertViolation(Terminal(record), "T002");
    }

    [Fact]
    public void SuccessClaimingEpicIndependenceFails()
    {
        var record = SuccessRecord();
        record["epicIndependentAdjudicationMet"] = true;
        AssertViolation(Terminal(record), "T002");
    }

    [Fact]
    public void SuccessWithSupportedRowFails()
    {
        var record = SuccessRecord();
        Subject(record, "verifier-runtime-differential")["outcome"] = "SUPPORTED";
        AssertViolation(Terminal(record), "T002");
    }

    [Fact]
    public void FailureRecordWithBlockerPasses()
    {
        var record = SuccessRecord();
        record["outcome"] = "MILESTONE-FAILED";
        Subject(record, "release-quality-reports")["outcome"] = "BLOCKED";
        var violations = Terminal(record);
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void UnknownTerminalOutcomeFails()
    {
        var record = SuccessRecord();
        record["outcome"] = "MILESTONE-MOSTLY-SUCCEEDED";
        AssertViolation(Terminal(record), "T003");
    }

    // ------------------------------------------------------------------
    // Inventory negative controls
    // ------------------------------------------------------------------

    [Fact]
    public void AuthoritativeArtifactWithoutRegenerationCommandFails()
    {
        var inventory = Inventory();
        Artifact(inventory, "verifier-runtime-differential")["regeneration"] = new JsonObject();
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I004");
    }

    [Fact]
    public void DerivedArtifactWithoutRegenerationCommandFails()
    {
        var inventory = Inventory();
        Artifact(inventory, "roundtrip-reports")["regeneration"] = new JsonObject { ["note"] = "manual" };
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I004");
    }

    [Fact]
    public void StaleArtifactWithoutCommandOrNoteFails()
    {
        var inventory = Inventory();
        Artifact(inventory, "benchmark-results")["regeneration"] = new JsonObject();
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I005");
    }

    [Fact]
    public void ArtifactWithoutProvenanceFails()
    {
        var inventory = Inventory();
        Artifact(inventory, "test-manifest").AsObject().Remove("provenance");
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I006");
    }

    [Theory]
    [InlineData("short-sha")]
    [InlineData("branch-ref")]
    [InlineData("actions-artifact")]
    [InlineData("none")]
    public void AuthoritativeArtifactWithNonDurableIdentityFails(string identity)
    {
        var inventory = Inventory();
        Artifact(inventory, "verifier-runtime-differential")["provenance"]!["identity"] = identity;
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I007");
    }

    [Fact]
    public void AuthoritativeArtifactStoredOnlyInActionsFails()
    {
        var inventory = Inventory();
        Artifact(inventory, "verifier-runtime-differential")["storage"]!["kind"] = "actions-artifact";
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I007");
    }

    [Fact]
    public void StaleArtifactPromotedToAuthoritativeFails()
    {
        // benchmark-results carries a short-SHA identity; reclassifying it without repairing that
        // identity is refused.
        var inventory = Inventory();
        Artifact(inventory, "benchmark-results")["classification"] = "authoritative";
        // The stale entry also has no valid regeneration command, so I004 may accompany I007.
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I007", "I004");
    }

    [Fact]
    public void ArtifactDerivedFromStaleInputMustBeStale()
    {
        var inventory = Inventory();
        Artifact(inventory, "benchmark-results-md")["classification"] = "derived";
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I010");
    }

    [Theory]
    [InlineData("current")]
    [InlineData("Authoritative")]
    [InlineData("")]
    public void UnknownClassificationFails(string classification)
    {
        var inventory = Inventory();
        Artifact(inventory, "test-manifest")["classification"] = classification;
        // An empty classification is also a missing required field (I001).
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I002", "I001");
    }

    [Fact]
    public void DuplicateArtifactIdFails()
    {
        var inventory = Inventory();
        var copy = Artifact(inventory, "test-manifest").DeepClone();
        inventory["artifacts"]!.AsArray().Add(copy);
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I003");
    }

    [Fact]
    public void DerivedFromUnknownArtifactFails()
    {
        var inventory = Inventory();
        Artifact(inventory, "roundtrip-baselines")["derivedFrom"] = new JsonArray("does-not-exist");
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I009");
    }

    [Fact]
    public void InventoryFromAnotherCutoffFails()
    {
        var inventory = Inventory();
        inventory["cutoffCommit"] = "74e55ce4861f9548199629ab6e18623ef1759f21";
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I012");
    }

    // ------------------------------------------------------------------
    // Contract negative controls
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Assumed")]
    [InlineData("TimeoutOrUnavailable")]
    [InlineData("ProvenVacuous")]
    public void NonEstablishingOutcomeMarkedEstablishingFails(string token)
    {
        var contract = Contract();
        Outcome(contract, token)["mayEstablishClaim"] = true;
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C004");
    }

    [Fact]
    public void AssumedMarkedGuardRemovingFails()
    {
        var contract = Contract();
        Outcome(contract, "Assumed")["mayRemoveGuard"] = true;
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C004");
    }

    [Fact]
    public void OutcomeRemovedFromVocabularyFails()
    {
        var contract = Contract();
        var outcomes = contract["outcomeVocabulary"]!["outcomes"]!.AsArray();
        outcomes.Remove(outcomes.First(o => o!["token"]!.GetValue<string>() == "Boundary"));
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C003");
    }

    [Fact]
    public void GateMarkedMetWhileCapacityProposedFails()
    {
        var contract = ProposedContract();
        contract["gateStatus"] = "MET";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C008");
    }

    [Fact]
    public void ChildWithoutClosureEvidenceFails()
    {
        var contract = Contract();
        Child(contract, 1311)["closureEvidence"] = "";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C005");
    }

    [Fact]
    public void ChildConsumingUnknownSectionFails()
    {
        var contract = Contract();
        Child(contract, 1276)["consumes"] = new JsonArray("benchmark-vibes");
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C005");
    }

    [Fact]
    public void MissingChildFails()
    {
        var contract = Contract();
        var children = contract["children"]!.AsArray();
        children.Remove(Child(contract, 1422));
        // #1423 still depends on the removed child, so C006 accompanies C007.
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C007", "C006");
    }

    [Fact]
    public void DependencyCycleFails()
    {
        var contract = Contract();
        Child(contract, 1419)["dependsOn"] = new JsonArray(1407, 1311);
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C006");
    }

    [Fact]
    public void IndependenceDeviationWithoutBoundedCapFails()
    {
        var contract = Contract();
        contract["authorityCapacity"]!["independence"]!["maxAdjudicationUnderDeviation"] = "SUPPORTED";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C009");
    }

    [Fact]
    public void ShortShaBaselineFails()
    {
        var contract = Contract();
        contract["baselines"]![0]!["commit"] = "72a0a855";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C002");
    }

    [Fact]
    public void WellFormedAmendmentPasses()
    {
        var violations = EvidenceContractValidator.ValidateContract(AmendedContract(Amendment()));
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void AmendmentWithoutJustificationFails()
    {
        var amendment = Amendment();
        amendment["justification"] = "";
        AssertViolation(EvidenceContractValidator.ValidateContract(AmendedContract(amendment)), "C010");
    }

    [Fact]
    public void AmendmentRemovingRowWithoutLastStatusFails()
    {
        var amendment = Amendment();
        amendment["removedRows"] = new JsonArray(new JsonObject { ["row"] = "S1-042" });
        AssertViolation(EvidenceContractValidator.ValidateContract(AmendedContract(amendment)), "C010");
    }

    [Fact]
    public void AmendmentWithoutContractVersionBumpFails()
    {
        var contract = AmendedContract(Amendment());
        contract["contractVersion"] = "1.0.0";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C010");
    }

    [Theory]
    [InlineData("1.0.0")]   // equal to the initial version: not a bump
    [InlineData("0.9.0")]   // a decrease
    [InlineData("1.1")]     // not MAJOR.MINOR.PATCH
    public void AmendmentVersionNotStrictlyIncreasingFails(string version)
    {
        var amendment = Amendment();
        amendment["version"] = version;
        var contract = AmendedContract(amendment);
        contract["contractVersion"] = version;
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C010");
    }

    [Fact]
    public void RepeatedAmendmentVersionFails()
    {
        var contract = AmendedContract(Amendment());
        contract["amendmentLog"]!.AsArray().Add(Amendment());
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C010");
    }

    [Fact]
    public void AmendmentInspectionFlagMustBeBoolean()
    {
        var amendment = Amendment();
        amendment["afterDecisionBearingInspection"] = "no";
        AssertViolation(EvidenceContractValidator.ValidateContract(AmendedContract(amendment)), "C010");
    }

    // ------------------------------------------------------------------
    // Lifecycle: PROPOSED until merge, FROZEN only with a complete acceptance record
    // ------------------------------------------------------------------

    [Fact]
    public void WellFormedAcceptanceWriteBackPasses()
    {
        // The whole write-back packet: contract and inventory move to 1.0.1 together.
        var contract = FrozenContract();
        var inventory = ProposedInventory();
        inventory["contractVersion"] = "1.0.1";
        var violations = EvidenceContractValidator.ValidateContract(contract)
            .Concat(EvidenceContractValidator.ValidateInventory(contract, inventory)).ToList();
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void AcceptanceWriteBackWithoutInventoryVersionFails()
    {
        AssertViolation(EvidenceContractValidator.ValidateInventory(FrozenContract(), ProposedInventory()), "I012");
    }

    [Fact]
    public void ProposedPacketIsValid()
    {
        // The proposed state stays valid after the write-back, so the proposed-state controls below
        // keep their meaning whichever lifecycle state is committed.
        var contract = ProposedContract();
        var violations = EvidenceContractValidator.ValidateContract(contract)
            .Concat(EvidenceContractValidator.ValidateInventory(contract, ProposedInventory())).ToList();
        Assert.True(violations.Count == 0, Describe(violations));
        // The proposed fixture carries nothing a later amendment added.
        var text = contract.ToJsonString();
        foreach (var added in new[] { "1.1.0", "1.2.0", "1.2.1", "ExecutionCeiling", "taskStatementRule", "determinismRows", "platformDeterminism", "evidenceDataRule", "chargeRules" })
            Assert.DoesNotContain(added, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProposedContractWithAcceptedCeilingFails()
    {
        var contract = ProposedContract();
        contract["authorityCapacity"]!["capacity"]!["ceilings"]![0]!["status"] = "ACCEPTED";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C012");
    }

    [Theory]
    [InlineData("mergeCommit")]
    [InlineData("mergedAtUtc")]
    [InlineData("pr")]
    public void FrozenContractWithIncompleteAcceptanceFails(string field)
    {
        var contract = FrozenContract();
        contract["acceptance"]!.AsObject().Remove(field);
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C012");
    }

    [Fact]
    public void FrozenContractWithProposedCeilingFails()
    {
        var contract = FrozenContract();
        contract["authorityCapacity"]!["capacity"]!["ceilings"]![0]!["status"] = "PROPOSED";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C008", "C012");
    }

    [Fact]
    public void FrozenContractNotMetFails()
    {
        var contract = FrozenContract();
        contract["gateStatus"] = "NOT-MET";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C012");
    }

    [Fact]
    public void ProposedContractWithAcceptanceRecordFails()
    {
        var contract = ProposedContract();
        contract["acceptance"] = FrozenContract()["acceptance"]!.DeepClone();
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C012");
    }

    [Fact]
    public void UnknownContractStatusFails()
    {
        var contract = Contract();
        contract["status"] = "ACCEPTED-ISH";
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C012");
    }

    // ------------------------------------------------------------------
    // Amendment 1.1.0: per-PR ceiling exception (C011)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("other-value")]
    [InlineData("other-pr")]
    [InlineData("other-ceiling")]
    [InlineData("unlogged-amendment")]
    [InlineData("no-justification")]
    [InlineData("added-exception")]
    [InlineData("duplicate-other-issue")]
    public void CeilingExceptionOtherThanTheRegisteredOneFails(string mutation)
    {
        // Decision 6 raises one ceiling for one PR to one value; nothing else passes without a new
        // amendment and a validator change.
        var contract = Contract();
        var exceptions = contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray();
        var exception = exceptions.First(e => e?["pr"]?.GetValue<int>() == 1473)!;
        switch (mutation)
        {
            case "other-value": exception["value"] = 2000.0; break;
            case "other-pr": exception["pr"] = 1474; break;
            case "other-ceiling": exception["ceiling"] = "s2-repair-size"; break;
            case "unlogged-amendment": exception["amendment"] = "1.0.9"; break;
            case "no-justification": exception["justification"] = " "; break;
            case "added-exception":
                var added = exception.DeepClone();
                added["pr"] = 9004;
                exceptions.Add(added);
                break;
            case "duplicate-other-issue":
                // Per-PR identity is ceiling and PR; a different issue does not make it distinct.
                var copy = exception.DeepClone();
                copy["issue"] = 1311;
                exceptions.Add(copy);
                break;
        }
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C011");
    }

    // ------------------------------------------------------------------
    // Amendment 1.2.0: per-gate ceiling exception for #1311 and charge rules (C011)
    // ------------------------------------------------------------------

    [Fact]
    public void CommittedContractRegistersTheS1ReRunAndTheC2Charge()
    {
        // Decision A raises one ceiling for one gate to one value; decision B moves one charge.
        // Neither changes a ceiling's own value.
        var contract = Contract();
        var capacity = contract["authorityCapacity"]!["capacity"]!;
        var s1 = S1Exception(contract);
        Assert.Equal("s1-generated-cases", s1["ceiling"]!.GetValue<string>());
        Assert.Equal(3008, s1["value"]!.GetValue<int>());
        Assert.Equal(1508, s1["addedExecutions"]!.GetValue<int>());
        Assert.Null(s1["pr"]);
        var ceilings = capacity["ceilings"]!.AsArray().ToDictionary(c => c!["id"]!.GetValue<string>(), c => c!["value"]!.GetValue<int>());
        Assert.Equal(1500, ceilings["s1-generated-cases"]);
        Assert.Equal(2000, ceilings["determinism-compute"]);
        Assert.Equal(1500, ceilings["regeneration-compute"]);
        Assert.Equal(2, ceilings["regenerations"]);
        Assert.Equal(10, ceilings["s1-timebox"]);
        Assert.Equal(20, ceilings["s1-agent-hours"]);
        var rule = Assert.Single(capacity["chargeRules"]!.AsArray())!;
        Assert.Equal("regeneration-compute", rule["chargedTo"]!.GetValue<string>());
        Assert.Equal("determinism-compute", rule["notChargedTo"]!.GetValue<string>());
        var amendment = contract["amendmentLog"]!.AsArray().Single(a => a!["version"]!.GetValue<string>() == "1.2.0")!;
        Assert.True(amendment["afterDecisionBearingInspection"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("other-value")]
    [InlineData("other-issue")]
    [InlineData("other-ceiling")]
    [InlineData("adds-pr")]
    [InlineData("unlogged-amendment")]
    [InlineData("no-justification")]
    [InlineData("no-scope")]
    [InlineData("no-conditions")]
    [InlineData("blank-condition")]
    [InlineData("duplicated")]
    [InlineData("weakened-condition")]
    [InlineData("dropped-condition")]
    [InlineData("added-condition")]
    [InlineData("reordered-conditions")]
    [InlineData("replaced-scope")]
    [InlineData("other-justification")]
    [InlineData("conditions-moved-into-justification")]
    [InlineData("other-added-executions")]
    [InlineData("no-added-executions")]
    public void GateCeilingExceptionOtherThanTheRegisteredOneFails(string mutation)
    {
        var contract = Contract();
        var exceptions = contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray();
        var exception = S1Exception(contract);
        switch (mutation)
        {
            case "other-value": exception["value"] = 3009; break;
            case "other-issue": exception["issue"] = 1413; break;
            case "other-ceiling": exception["ceiling"] = "s1-compute"; break;
            case "adds-pr": exception["pr"] = 1480; break;
            case "unlogged-amendment": exception["amendment"] = "1.1.9"; break;
            case "no-justification": exception["justification"] = " "; break;
            case "no-scope": exception.AsObject().Remove("scope"); break;
            case "no-conditions": exception["conditions"] = new JsonArray(); break;
            case "blank-condition": exception["conditions"]!.AsArray().Add(" "); break;
            case "duplicated": exceptions.Add(exception.DeepClone()); break;
            case "weakened-condition": exception["conditions"]![5] = "Choose the better run."; break;
            case "dropped-condition": exception["conditions"]!.AsArray().RemoveAt(6); break;
            case "added-condition": exception["conditions"]!.AsArray().Add("Repeated selective sweeps are permitted."); break;
            case "reordered-conditions":
                var conditions = exception["conditions"]!.AsArray();
                var first = conditions[0]!.DeepClone();
                conditions.RemoveAt(0);
                conditions.Add(first);
                break;
            case "replaced-scope": exception["scope"] = "Repeated selective sweeps until every row is clean."; break;
            case "other-justification": exception["justification"] = "Fixture justification."; break;
            case "conditions-moved-into-justification":
                // The same text, but five conditions are no longer conditions (review round 2).
                var all = exception["conditions"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
                exception["conditions"] = new JsonArray(all.Take(5).Select(c => (JsonNode)JsonValue.Create(c)!).ToArray());
                exception["justification"] = string.Join("\n", all.Skip(5).Append(exception["justification"]!.GetValue<string>()));
                break;
            case "other-added-executions": exception["addedExecutions"] = 3008; break;
            case "no-added-executions": exception.AsObject().Remove("addedExecutions"); break;
        }
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C011");
    }

    [Fact]
    public void RegisteredTextHashesMatchTheCommittedEntries()
    {
        // The committed packet is the registered text: its exception and charge rule pass C011 as committed.
        var capacity = Contract()["authorityCapacity"]!["capacity"]!;
        var violations = EvidenceContractValidator.ValidateContract(Contract());
        Assert.DoesNotContain(violations, v => v.Code == "C011");
        Assert.Single(capacity["chargeRules"]!.AsArray());
    }

    [Fact]
    public void TextHashNormalizesLineEndingsAndFramesParts()
    {
        // CRLF and LF inputs hash alike; text cannot move across a part boundary unnoticed.
        Assert.Equal(EvidenceContractValidator.TextSha256(["a\nb", "c"]), EvidenceContractValidator.TextSha256(["a\r\nb", "c"]));
        Assert.NotEqual(EvidenceContractValidator.TextSha256(["a\nb", "c"]), EvidenceContractValidator.TextSha256(["a", "b\nc"]));
        Assert.NotEqual(EvidenceContractValidator.TextSha256(["a", "b"]), EvidenceContractValidator.TextSha256(["a\nb"]));
        Assert.NotEqual(EvidenceContractValidator.TextSha256(["a", ""]), EvidenceContractValidator.TextSha256(["a"]));
    }

    [Fact]
    public void GateCeilingExceptionWithoutItsAmendmentInTheLogFails()
    {
        // Removing amendments 1.2.1 and 1.2.0 from the log leaves the exception and charge rule unregistered.
        var contract = Contract();
        var log = contract["amendmentLog"]!.AsArray();
        foreach (var entry in log.Where(a => a!["version"]!.GetValue<string>() is "1.2.1" or "1.2.0").ToList())
            log.Remove(entry);
        contract["contractVersion"] = log.Last()!["version"]!.DeepClone();
        var violations = EvidenceContractValidator.ValidateContract(contract);
        AssertViolation(violations, "C011");
        Assert.Contains(violations, v => v.Subject == "exception s1-generated-cases issue #1311");
        Assert.Contains(violations, v => v.Subject == "charge rule c2-candidate-determinism-protocol");
    }

    [Theory]
    [InlineData("other-id")]
    [InlineData("reversed")]
    [InlineData("unknown-ceiling")]
    [InlineData("unlogged-amendment")]
    [InlineData("no-work")]
    [InlineData("no-rule")]
    [InlineData("no-justification")]
    [InlineData("added-rule")]
    [InlineData("duplicated")]
    [InlineData("broadened-work")]
    [InlineData("dropped-candidate-binding")]
    [InlineData("other-justification")]
    public void ChargeRuleOtherThanTheRegisteredOneFails(string mutation)
    {
        var contract = Contract();
        var rules = contract["authorityCapacity"]!["capacity"]!["chargeRules"]!.AsArray();
        var rule = rules[0]!;
        switch (mutation)
        {
            case "other-id": rule["id"] = "c2-all-determinism-runs"; break;
            case "reversed":
                rule["chargedTo"] = "determinism-compute";
                rule["notChargedTo"] = "regeneration-compute";
                break;
            case "unknown-ceiling": rule["chargedTo"] = "free-compute"; break;
            case "unlogged-amendment": rule["amendment"] = "1.1.9"; break;
            case "no-work": rule["work"] = ""; break;
            case "no-rule": rule.AsObject().Remove("rule"); break;
            case "no-justification": rule["justification"] = " "; break;
            case "added-rule":
                var added = rule.DeepClone();
                added["id"] = "s1-to-ordinary-ci";
                rules.Add(added);
                break;
            case "duplicated": rules.Add(rule.DeepClone()); break;
            case "broadened-work": rule["work"] = "Every execution of the #1421 determinism protocol."; break;
            case "dropped-candidate-binding":
                rule["rule"] = rule["rule"]!.GetValue<string>().Replace("the #1423 manifest names", "any commit names", StringComparison.Ordinal);
                break;
            case "other-justification": rule["justification"] = "Fixture justification."; break;
        }
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C011");
    }

    // ------------------------------------------------------------------
    // Amendment 1.2.1: condition 4 of the #1311 exception (run-2 harness ceiling constant)
    // ------------------------------------------------------------------

    private const string Amended4 = "changed in exactly two ways (amendment 1.2.1): (a) it adds the loaded-image capture, and (b) it changes the single constant ExecutionCeiling in the sweep tool's Program.cs (tools/Calor.Soundness.Sweep/Program.cs at that commit, or the same file after a path-only move) from 1500 to 1508, the run-2 budget this exception approves.";

    [Fact]
    public void CommittedContractRecordsAmendment121AsTheOnlyChangeToCondition4()
    {
        // 1.2.1 changes condition 4 only; the ceiling value, addedExecutions, and the other nine
        // conditions keep their 1.2.0 text (bound by the registered hash).
        var contract = Contract();
        var s1 = S1Exception(contract);
        Assert.Equal("1.2.0", s1["amendment"]!.GetValue<string>());
        Assert.Equal(3008, s1["value"]!.GetValue<int>());
        Assert.Equal(1508, s1["addedExecutions"]!.GetValue<int>());
        var conditions = s1["conditions"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
        Assert.Equal(10, conditions.Count);
        Assert.StartsWith("Same harness.", conditions[3], StringComparison.Ordinal);
        Assert.Contains(Amended4, conditions[3], StringComparison.Ordinal);
        Assert.Single(conditions, c => c.Contains("ExecutionCeiling", StringComparison.Ordinal));
        var last = contract["amendmentLog"]!.AsArray().Single(a => a!["version"]!.GetValue<string>() == "1.2.1")!;
        Assert.True(last["afterDecisionBearingInspection"]!.GetValue<bool>());
        Assert.True(last["reviewedInPr"]!.GetValue<int>() > 1484);
        Assert.Single(last["weakens"]!.AsArray());
        Assert.Empty(last["removedRows"]!.AsArray());
    }

    [Theory]
    [InlineData("1.2.0-text")]
    [InlineData("other-constant-value")]
    [InlineData("other-constant")]
    [InlineData("no-capture")]
    [InlineData("further-change")]
    public void Condition4OtherThanTheAmendedTextFails(string mutation)
    {
        var contract = Contract();
        var conditions = S1Exception(contract)["conditions"]!.AsArray();
        var text = conditions[3]!.GetValue<string>();
        Assert.Contains(Amended4, text, StringComparison.Ordinal);
        conditions[3] = mutation switch
        {
            "1.2.0-text" => text.Replace(Amended4, "changed only to add the loaded-image capture.", StringComparison.Ordinal),
            "other-constant-value" => text.Replace("from 1500 to 1508", "from 1500 to 1600", StringComparison.Ordinal),
            "other-constant" => text.Replace("constant ExecutionCeiling", "constant Reserve", StringComparison.Ordinal),
            "no-capture" => text.Replace("(a) it adds the loaded-image capture, and (b) it", "it", StringComparison.Ordinal),
            "further-change" => text.Replace("the run-2 budget this exception approves.", "the run-2 budget this exception approves, and any other constant as needed.", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        Assert.NotEqual(text, conditions[3]!.GetValue<string>());
        var violations = EvidenceContractValidator.ValidateContract(contract);
        AssertViolation(violations, "C011");
        Assert.Contains(violations, v => v.Subject == "exception s1-generated-cases issue #1311");
    }

    [Fact]
    public void AmendedConditionTextWithoutAmendment121InTheLogFails()
    {
        // The 1.2.1 text is registered by 1.2.1: dropping 1.2.1 from the log fails the exception,
        // while the 1.2.0 charge rule stays registered.
        var contract = Contract();
        var log = contract["amendmentLog"]!.AsArray();
        log.Remove(log.Single(a => a!["version"]!.GetValue<string>() == "1.2.1"));
        var violations = EvidenceContractValidator.ValidateContract(contract);
        Assert.Contains(violations, v => v.Code == "C011" && v.Subject == "exception s1-generated-cases issue #1311");
        Assert.DoesNotContain(violations, v => v.Subject == "charge rule c2-candidate-determinism-protocol");
    }

    private static JsonNode S1Exception(JsonNode contract)
        => contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray()
            .First(e => e?["pr"] is null && e?["issue"]?.GetValue<int>() == 1311)!;

    // ------------------------------------------------------------------
    // Amendment 1.3.0: S2 discovery demotion slot (A) and #1496 review overrun (B) (C011)
    // ------------------------------------------------------------------

    private const string S2Subject = "exception s2-repairs issue #1413";
    private const string ReviewSubject = "exception review-rounds-per-pr #1496";
    private const string LastRoblFix = "674e3bdff202486c6d3815bc02c5b7b267ee9146";

    [Fact]
    public void CommittedContractRegistersAmendment130Exceptions()
    {
        // A raises s2-repairs to 7 for #1413 only, scoped to the two discovery findings; B raises
        // review-rounds-per-pr to 5 for PR #1496 only, conditioned on a final verification pass.
        // Neither changes a ceiling's own value.
        var contract = Contract();
        var capacity = contract["authorityCapacity"]!["capacity"]!;
        var ceilings = capacity["ceilings"]!.AsArray().ToDictionary(c => c!["id"]!.GetValue<string>(), c => c!["value"]!.GetValue<int>());
        Assert.Equal(6, ceilings["s2-repairs"]);
        Assert.Equal(600, ceilings["s2-repair-size"]);
        Assert.Equal(3, ceilings["review-rounds-per-pr"]);

        var a = S2Exception(contract);
        Assert.Equal(7, a["value"]!.GetValue<int>());
        Assert.Equal(1, a["addedPrs"]!.GetValue<int>());
        Assert.Equal("1.3.0", a["amendment"]!.GetValue<string>());
        Assert.Equal(["D-OBL-PROOF-GETTER", "D-OBL-THROWING-PREDECESSOR"], a["findings"]!.AsArray().Select(f => f!.GetValue<string>()));
        Assert.Contains("D-OBL-PROOF-GETTER and D-OBL-THROWING-PREDECESSOR", a["scope"]!.GetValue<string>(), StringComparison.Ordinal);

        var b = ReviewException(contract);
        Assert.Equal(5, b["value"]!.GetValue<int>());
        Assert.Equal(1413, b["issue"]!.GetValue<int>());
        Assert.Equal("1.3.0", b["amendment"]!.GetValue<string>());
        Assert.Null(b["addedPrs"]);
        Assert.Null(b["findings"]);
        var conditions = b["conditions"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
        Assert.StartsWith("Final verification pass required.", conditions[0], StringComparison.Ordinal);
        Assert.Contains(LastRoblFix, conditions[0], StringComparison.Ordinal);
        Assert.StartsWith("Clean or not accepted.", conditions[1], StringComparison.Ordinal);
        Assert.StartsWith("Repair frozen at the last fix.", conditions[2], StringComparison.Ordinal);
        Assert.Contains("before and after the pass", conditions[2], StringComparison.Ordinal);

        // Exactly one review-round exception comes from 1.3.0, and it is #1496's (1.3.1 adds #1502 and #1503).
        var reviewExceptions = capacity["exceptions"]!.AsArray()
            .Where(e => e!["ceiling"]!.GetValue<string>() == "review-rounds-per-pr" && e["amendment"]!.GetValue<string>() == "1.3.0").ToList();
        Assert.Equal(1496, Assert.Single(reviewExceptions)!["pr"]!.GetValue<int>());

        var entry = contract["amendmentLog"]!.AsArray().Single(a => a!["version"]!.GetValue<string>() == "1.3.0")!;
        Assert.True(entry["afterDecisionBearingInspection"]!.GetValue<bool>());
        Assert.True(entry["reviewedInPr"]!.GetValue<int>() > 1499);
        Assert.Equal(2, entry["weakens"]!.AsArray().Count);
        Assert.Empty(entry["removedRows"]!.AsArray());
    }

    [Theory]
    [InlineData("other-discovery-id")]
    [InlineData("extra-finding")]
    [InlineData("dropped-finding")]
    [InlineData("reordered-findings")]
    [InlineData("no-findings")]
    [InlineData("scope-names-other-discovery")]
    [InlineData("value-8")]
    [InlineData("other-issue")]
    [InlineData("adds-pr")]
    [InlineData("other-added-prs")]
    [InlineData("no-added-prs")]
    [InlineData("added-executions-field")]
    [InlineData("unlogged-amendment")]
    [InlineData("no-conditions")]
    [InlineData("dropped-condition")]
    [InlineData("weakened-condition")]
    [InlineData("no-justification")]
    [InlineData("duplicated")]
    public void S2RepairExceptionOtherThanTheRegisteredOneFails(string mutation)
    {
        var contract = Contract();
        var exceptions = contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray();
        var exception = S2Exception(contract);
        var findings = exception["findings"]!.AsArray();
        var conditions = exception["conditions"]!.AsArray();
        switch (mutation)
        {
            case "other-discovery-id": findings[1] = "D-OBL-HEAP-ALIAS"; break;
            case "extra-finding": findings.Add("NUM-NARROW-ARITH-001"); break;
            case "dropped-finding": findings.RemoveAt(1); break;
            case "reordered-findings":
                var first = findings[0]!.DeepClone();
                findings.RemoveAt(0);
                findings.Add(first);
                break;
            case "no-findings": exception.AsObject().Remove("findings"); break;
            case "scope-names-other-discovery":
                exception["scope"] = exception["scope"]!.GetValue<string>().Replace("D-OBL-THROWING-PREDECESSOR", "D-OBL-HEAP-ALIAS", StringComparison.Ordinal);
                break;
            case "value-8": exception["value"] = 8; break;
            case "other-issue": exception["issue"] = 1311; break;
            case "adds-pr": exception["pr"] = 1496; break;
            case "other-added-prs": exception["addedPrs"] = 2; break;
            case "no-added-prs": exception.AsObject().Remove("addedPrs"); break;
            case "added-executions-field": exception["addedExecutions"] = 1; break;
            case "unlogged-amendment": exception["amendment"] = "1.2.9"; break;
            case "no-conditions": exception["conditions"] = new JsonArray(); break;
            case "dropped-condition": conditions.RemoveAt(1); break;
            case "weakened-condition":
                conditions[1] = conditions[1]!.GetValue<string>().Replace(", and no runtime guard is removed", "", StringComparison.Ordinal);
                break;
            case "no-justification": exception["justification"] = " "; break;
            case "duplicated": exceptions.Add(exception.DeepClone()); break;
        }
        var violations = EvidenceContractValidator.ValidateContract(contract);
        AssertViolation(violations, "C011");
        if (mutation is not ("other-issue" or "adds-pr" or "duplicated"))
            Assert.Contains(violations, v => v.Code == "C011" && v.Subject == S2Subject);
    }

    [Theory]
    [InlineData("other-pr")]
    [InlineData("added-for-another-pr")]
    [InlineData("value-6")]
    [InlineData("other-ceiling")]
    [InlineData("other-issue")]
    [InlineData("as-per-gate")]
    [InlineData("missing-final-pass-condition")]
    [InlineData("missing-clean-condition")]
    [InlineData("no-conditions")]
    [InlineData("no-scope")]
    [InlineData("weakened-final-pass")]
    [InlineData("other-last-fix")]
    [InlineData("repair-not-frozen-before-pass")]
    [InlineData("names-findings")]
    [InlineData("added-prs-field")]
    [InlineData("unlogged-amendment")]
    [InlineData("no-justification")]
    [InlineData("duplicated")]
    public void ReviewOverrunExceptionOtherThanTheRegisteredOneFails(string mutation)
    {
        var contract = Contract();
        var exceptions = contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray();
        var exception = ReviewException(contract);
        var conditions = exception["conditions"]!.AsArray();
        switch (mutation)
        {
            case "other-pr": exception["pr"] = 1495; break;
            case "added-for-another-pr":
                // The overrun allowance is #1496's alone; the same text for another PR is unregistered.
                var copy = exception.DeepClone();
                copy["pr"] = 1497;
                exceptions.Add(copy);
                break;
            case "value-6": exception["value"] = 6; break;
            case "other-ceiling": exception["ceiling"] = "maintainer-review-per-pr"; break;
            case "other-issue": exception["issue"] = 1311; break;
            case "as-per-gate": exception.AsObject().Remove("pr"); break;
            case "missing-final-pass-condition": conditions.RemoveAt(0); break;
            case "missing-clean-condition": conditions.RemoveAt(1); break;
            case "no-conditions": exception.AsObject().Remove("conditions"); break;
            case "no-scope": exception["scope"] = ""; break;
            case "weakened-final-pass":
                conditions[1] = conditions[1]!.GetValue<string>().Replace("must request no change", "should request little change", StringComparison.Ordinal);
                break;
            case "other-last-fix":
                conditions[0] = conditions[0]!.GetValue<string>().Replace(LastRoblFix, "05df19c0aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", StringComparison.Ordinal);
                break;
            case "repair-not-frozen-before-pass":
                // Review round 1: freezing only after the pass would let another fix land before it.
                conditions[2] = "No change after the pass. After the pass, the PR's changes under src/ and tests/ do not change before merge. Only a merge from main whose conflicts are confined to CHANGELOG.md or eng/test-manifest.json may follow it.";
                break;
            case "names-findings": exception["findings"] = new JsonArray("D-OBL-PROOF-GETTER"); break;
            case "added-prs-field": exception["addedPrs"] = 1; break;
            case "unlogged-amendment": exception["amendment"] = "1.2.9"; break;
            case "no-justification": exception.AsObject().Remove("justification"); break;
            case "duplicated": exceptions.Add(exception.DeepClone()); break;
        }
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C011");
    }

    [Fact]
    public void ExceptionWithoutRegisteredTextCannotCarryConditions()
    {
        // The 1.1.0 per-PR exception has no registered text; adding a scope or conditions is unregistered.
        var contract = Contract();
        var exception = contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray().First(e => e?["pr"]?.GetValue<int>() == 1473)!;
        exception["conditions"] = new JsonArray("Any later overrun is also allowed.");
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C011");
    }

    [Fact]
    public void Amendment130ExceptionsWithoutAmendment130InTheLogFail()
    {
        // Dropping 1.3.0 from the log leaves both of its exceptions unregistered; the earlier ones stay valid.
        var contract = Contract();
        var log = contract["amendmentLog"]!.AsArray();
        log.Remove(log.Single(a => a!["version"]!.GetValue<string>() == "1.3.0"));
        contract["contractVersion"] = log.Last()!["version"]!.DeepClone();
        var violations = EvidenceContractValidator.ValidateContract(contract);
        AssertViolation(violations, "C011");
        Assert.Contains(violations, v => v.Subject == S2Subject);
        Assert.Contains(violations, v => v.Subject == ReviewSubject);
        Assert.DoesNotContain(violations, v => v.Subject == "exception s1-generated-cases issue #1311");
        Assert.DoesNotContain(violations, v => v.Subject == "exception pr-size #1473");
    }

    // ------------------------------------------------------------------
    // Amendment 1.3.1: one registered change each for #1502 (three parts) and #1503 (revert-only) (C011)
    // ------------------------------------------------------------------

    private const string Base1502 = "bdb430db1c1dfdbcd578c5b1b74110841be95a2d"; // 1.3.2 re-registration (1.3.1: 3f3016d2)
    private const string Base1503 = "9b54c9c3d8d7fb178c5594ddc757d871fbb34ab4";
    private const string WhileBoundFinding = "D-NUM-WHILE-BOUND";

    [Theory]
    [InlineData(1502, Base1502, "One change only.", "Z3Verifier.CanFailForSomeInput", false, 6)]
    [InlineData(1503, Base1503, "Revert only.", "FactCollector.CollectFromIf", true, 5)]
    public void CommittedContractRegistersAmendment131Exceptions(int pr, string baseCommit, string scopeCondition, string hunk, bool revertOnly, int value)
    {
        // Each PR gets exactly one registered change on its base, then one verification pass that must
        // APPROVE: 3 rounds + 2 passes = 5 (#1502: + the 1.3.1 pass and the 1.3.2 pass = 6, amendment
        // 1.3.2). No ceiling value changes.
        var contract = Contract();
        var capacity = contract["authorityCapacity"]!["capacity"]!;
        var ceilings = capacity["ceilings"]!.AsArray().ToDictionary(c => c!["id"]!.GetValue<string>(), c => c!["value"]!.GetValue<int>());
        Assert.Equal(3, ceilings["review-rounds-per-pr"]);
        Assert.Equal(6, ceilings["s2-repairs"]);
        Assert.Equal(600, ceilings["s2-repair-size"]);

        var exception = ChangeException(contract, pr);
        Assert.Equal(value, exception["value"]!.GetValue<int>());
        Assert.Equal(1413, exception["issue"]!.GetValue<int>());
        Assert.Equal("1.3.1", exception["amendment"]!.GetValue<string>());
        Assert.Equal(revertOnly, exception["revertOnly"]!.GetValue<bool>());
        Assert.Equal(baseCommit, exception["baseCommit"]!.GetValue<string>());
        Assert.Null(exception["addedPrs"]);
        Assert.Contains(baseCommit, exception["scope"]!.GetValue<string>(), StringComparison.Ordinal);
        var conditions = exception["conditions"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
        Assert.StartsWith(scopeCondition, conditions[0], StringComparison.Ordinal);
        Assert.Contains(baseCommit, conditions[0], StringComparison.Ordinal);
        Assert.Contains(hunk, conditions[0], StringComparison.Ordinal);
        var approve = Assert.Single(conditions, c => c.StartsWith("Verification pass must APPROVE.", StringComparison.Ordinal));
        Assert.Contains("no further fix or pass is allowed", approve, StringComparison.Ordinal);
        Assert.Contains(conditions, c => c.StartsWith("Frozen at the", StringComparison.Ordinal)
            && c.Contains("before and after the pass", StringComparison.Ordinal));

        var entry = contract["amendmentLog"]!.AsArray().Single(a => a!["version"]!.GetValue<string>() == "1.3.1")!;
        Assert.True(entry["afterDecisionBearingInspection"]!.GetValue<bool>());
        Assert.True(entry["reviewedInPr"]!.GetValue<int>() > 1503);
        Assert.Equal(2, entry["weakens"]!.AsArray().Count);
        Assert.Empty(entry["removedRows"]!.AsArray());

        // Review-round overruns exist for exactly #1496, #1502, and #1503.
        Assert.Equal([1496, 1502, 1503], capacity["exceptions"]!.AsArray()
            .Where(e => e!["ceiling"]!.GetValue<string>() == "review-rounds-per-pr")
            .Select(e => e!["pr"]!.GetValue<int>()).Order());
    }

    // ------------------------------------------------------------------
    // Amendment 1.3.2: #1502's part (b) becomes a rule with no solver (C011)
    // ------------------------------------------------------------------

    [Fact]
    public void Pr1502ChangeIsTheNoSolverRule()
    {
        // Maintainer decision 2026-10-06 ("Amend: no solver, rule-based"): the 1.3.1 text of part (b)
        // consulted the solver and contradicted its own determinism condition.
        var contract = Contract();
        var exception = ChangeException(contract, 1502);
        Assert.Equal("1.3.1", exception["amendment"]!.GetValue<string>());
        Assert.Equal(6, exception["value"]!.GetValue<int>());
        Assert.Equal([WhileBoundFinding], exception["findings"]!.AsArray().Select(f => f!.GetValue<string>()));
        var conditions = exception["conditions"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
        var scope = conditions[0];
        Assert.StartsWith("One change only. Exactly one commit on top of commit " + Base1502, scope, StringComparison.Ordinal);
        Assert.Contains("makes no solver call: no satisfiability check and no timeout", scope, StringComparison.Ordinal);
        Assert.Contains("at most Assumed (checked-arithmetic)", scope, StringComparison.Ordinal);
        Assert.Contains("Z3Verifier.OverflowProbeStatusForTesting) are removed", scope, StringComparison.Ordinal);
        Assert.Contains("Changes (a) and (c), and every other change in the PR, stay exactly as at commit " + Base1502, scope, StringComparison.Ordinal);
        Assert.DoesNotContain("consult the solver", scope, StringComparison.Ordinal);
        Assert.Contains(conditions, c => c.StartsWith("Tests and text only as the rule requires.", StringComparison.Ordinal)
            && c.Contains("UndecidedOverflowProbe_IsUnsupported", StringComparison.Ordinal)
            && c.Contains("each only by adding the checked-arithmetic assumption", StringComparison.Ordinal)
            && c.Contains("never stronger, and no others", StringComparison.Ordinal)
            && c.Contains("the 6 of scalar-type:i64 and scalar-type:u64 change from Proven to Assumed (checked-arithmetic)", StringComparison.Ordinal)
            && c.Contains("were already Assumed (reference-model) and now carry both assumptions", StringComparison.Ordinal)
            && c.Contains("aa1f86f9ae5e6e9c6eedea1970b4080f8e347271c0ba99434a1afa3203b9c9e9", StringComparison.Ordinal)
            && c.Contains("24901f2176fb3157dedcb79f40564f2bfe9bcd10db94018f5c8942c56c20b2d5", StringComparison.Ordinal)
            && c.Contains("ProductionOverflowRuntimeTests.GuardedArithmetic_ProvesWithoutEvaluatingUnselectedOverflow", StringComparison.Ordinal)
            && c.Contains("exactly the 12 provable postcondition cells of exactly the 4 forms scalar-type:i64, scalar-type:u64, array-element-type:i64, and array-element-type:u64", StringComparison.Ordinal)
            && c.Contains("regenerated by the oracle generator and not hand-edited", StringComparison.Ordinal)
            && c.Contains("from 435 Proven and 150 Assumed to 429 Proven and 156 Assumed (585 Refuted unchanged)", StringComparison.Ordinal));
        Assert.Contains(conditions, c => c.StartsWith("No false proof, and deterministic.", StringComparison.Ordinal)
            && c.Contains("never on a solver answer, solver time, timeouts, or platform", StringComparison.Ordinal)
            && c.Contains("at most 600 changed non-test lines", StringComparison.Ordinal));
        Assert.Contains(conditions, c => c.StartsWith("Verification pass must APPROVE.", StringComparison.Ordinal)
            && c.Contains(WhileBoundFinding + " is MILESTONE-FAILED", StringComparison.Ordinal));
        Assert.Contains("REQUEST-CHANGES with one MAJOR", exception["scope"]!.GetValue<string>(), StringComparison.Ordinal);

        var entry = contract["amendmentLog"]!.AsArray().Last()!;
        Assert.Equal("1.3.2", entry["version"]!.GetValue<string>());
        Assert.Equal("1.3.2", contract["contractVersion"]!.GetValue<string>());
        Assert.Equal("1.3.2", Inventory()["contractVersion"]!.GetValue<string>());
        Assert.True(entry["afterDecisionBearingInspection"]!.GetValue<bool>());
        Assert.True(entry["reviewedInPr"]!.GetValue<int>() > 1504);
        Assert.Empty(entry["removedRows"]!.AsArray());
        var weakens = entry["weakens"]!.AsArray().Select(w => w!.GetValue<string>()).ToList();
        Assert.Equal(3, weakens.Count);
        Assert.Contains(weakens, w => w.Contains("from 5 (amendment 1.3.1) to 6", StringComparison.Ordinal));
        // Option A: G3 (#1135) artifacts change after G3's deterministic execution; demotion only; C2 re-runs.
        Assert.Contains(weakens, w => w.Contains("G3 (#1135) artifacts after G3's DETERMINISTIC execution on d42d031352f2d8d5c1df56fffc19d901186bb94d", StringComparison.Ordinal)
            && w.Contains("This is a demotion only", StringComparison.Ordinal)
            && w.Contains("C2 (#1424) re-runs the determinism protocol on the candidate", StringComparison.Ordinal));
        Assert.Contains(weakens, w => w.Contains("self-contradictory", StringComparison.Ordinal));
        // #1503's 1.3.1 exception is untouched.
        Assert.Equal(5, ChangeException(contract, 1503)["value"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("value-5")]
    [InlineData("1.3.1-base")]
    [InlineData("solver-consulted")]
    [InlineData("unknown-unsupported")]
    [InlineData("seam-kept")]
    [InlineData("stronger-test-change")]
    [InlineData("thirteenth-cell")]
    [InlineData("fifth-form")]
    [InlineData("proven-ward-test-change")]
    [InlineData("hand-edited-reports")]
    [InlineData("old-report-totals")]
    [InlineData("other-report-hash")]
    [InlineData("three-changes-scope-condition")]
    public void Pr1502ExceptionOtherThanThe132TextFails(string mutation)
    {
        var contract = Contract();
        var exception = ChangeException(contract, 1502);
        var conditions = exception["conditions"]!.AsArray();
        string Edit(int index, string from, string to)
        {
            var text = conditions[index]!.GetValue<string>();
            Assert.Contains(from, text, StringComparison.Ordinal);
            return text.Replace(from, to, StringComparison.Ordinal);
        }
        switch (mutation)
        {
            case "value-5": exception["value"] = 5; break;
            case "1.3.1-base": exception["baseCommit"] = "3f3016d2297491fca1734e931946747833def32d"; break;
            case "solver-consulted":
                conditions[0] = Edit(0, "makes no solver call: no satisfiability check and no timeout", "consults the solver when the width rule cannot decide");
                break;
            case "unknown-unsupported":
                conditions[0] = Edit(0, "and the postcondition is at most Assumed (checked-arithmetic)", "and a solver unknown makes the postcondition Unsupported");
                break;
            case "seam-kept":
                conditions[0] = Edit(0, "are removed", "are kept");
                break;
            case "stronger-test-change":
                conditions[1] = Edit(1, "never stronger, and no others", "in either direction");
                break;
            case "thirteenth-cell":
                conditions[1] = Edit(1, "exactly the 12 provable postcondition cells", "exactly the 13 provable postcondition cells");
                break;
            case "fifth-form":
                conditions[1] = Edit(1, "exactly the 4 forms scalar-type:i64, scalar-type:u64,", "exactly the 5 forms scalar-type:i32, scalar-type:i64, scalar-type:u64,");
                break;
            case "proven-ward-test-change":
                conditions[1] = Edit(1, "(i) the 3 rows of ProductionOverflowRuntimeTests", "(i) a change from Assumed to Proven where a solver proves it, and the 3 rows of ProductionOverflowRuntimeTests");
                break;
            case "old-report-totals":
                conditions[1] = Edit(1, "to 429 Proven and 156 Assumed", "to 423 Proven and 162 Assumed");
                break;
            case "other-report-hash":
                conditions[1] = Edit(1, "aa1f86f9ae5e6e9c6eedea1970b4080f8e347271c0ba99434a1afa3203b9c9e9", "0000000000000000000000000000000000000000000000000000000000000000");
                break;
            case "hand-edited-reports":
                conditions[1] = Edit(1, "regenerated by the oracle generator and not hand-edited", "edited by hand");
                break;
            case "three-changes-scope-condition":
                conditions[0] = "Three changes only." + conditions[0]!.GetValue<string>()["One change only.".Length..];
                break;
        }
        Assert.Empty(EvidenceContractValidator.ValidateContract(Contract()));
        var violations = EvidenceContractValidator.ValidateContract(contract);
        AssertViolation(violations, "C011");
        Assert.Contains(violations, v => v.Code == "C011" && v.Subject == "exception review-rounds-per-pr #1502");
    }

    [Fact]
    public void Amendment132TextWithoutAmendment132InTheLogFails()
    {
        // #1502's re-registered text was set by 1.3.2; dropping 1.3.2 leaves it unregistered, #1503's stays valid.
        var contract = Contract();
        var log = contract["amendmentLog"]!.AsArray();
        log.Remove(log.Single(a => a!["version"]!.GetValue<string>() == "1.3.2"));
        contract["contractVersion"] = "1.3.1";
        var violations = EvidenceContractValidator.ValidateContract(contract);
        AssertViolation(violations, "C011");
        Assert.Contains(violations, v => v.Subject == "exception review-rounds-per-pr #1502"
            && v.Message.Contains("amendment 1.3.2", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.Subject == "exception review-rounds-per-pr #1503");
    }

    [Theory]
    [InlineData(1502, "wrong-pr")]
    [InlineData(1503, "wrong-pr")]
    [InlineData(1502, "swapped-prs")]
    [InlineData(1502, "value-plus-one")]
    [InlineData(1503, "value-plus-one")]
    [InlineData(1502, "missing-approve-condition")]
    [InlineData(1503, "missing-approve-condition")]
    [InlineData(1502, "weakened-approve-condition")]
    [InlineData(1502, "broadened-scope")]
    [InlineData(1503, "broadened-scope")]
    [InlineData(1503, "non-revert-condition")]
    [InlineData(1502, "fourth-change")]
    [InlineData(1502, "solver-allowed")]
    [InlineData(1502, "dropped-determinism")]
    [InlineData(1502, "dropped-finding")]
    [InlineData(1502, "other-finding")]
    [InlineData(1502, "dropped-discovery-condition")]
    [InlineData(1502, "revert-only-flipped")]
    [InlineData(1503, "revert-only-flipped")]
    [InlineData(1503, "no-revert-only")]
    [InlineData(1502, "other-base-commit")]
    [InlineData(1503, "other-base-commit")]
    [InlineData(1503, "no-base-commit")]
    [InlineData(1502, "reordered-conditions")]
    [InlineData(1503, "other-issue")]
    [InlineData(1502, "unlogged-amendment")]
    [InlineData(1503, "names-findings")]
    [InlineData(1502, "duplicated")]
    [InlineData(1503, "added-for-another-pr")]
    public void Amendment131ExceptionOtherThanTheRegisteredOneFails(int pr, string mutation)
    {
        var contract = Contract();
        var exceptions = contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray();
        var exception = ChangeException(contract, pr);
        var conditions = exception["conditions"]!.AsArray();
        int IndexOf(string prefix) => conditions.Select((c, i) => (Text: c!.GetValue<string>(), Index: i))
            .Single(x => x.Text.StartsWith(prefix, StringComparison.Ordinal)).Index;
        var approveIndex = IndexOf("Verification pass must APPROVE.");
        switch (mutation)
        {
            case "wrong-pr": exception["pr"] = 1497; break;
            case "swapped-prs":
                var other = ChangeException(contract, 1503);
                exception["pr"] = 1503;
                other["pr"] = 1502;
                break;
            case "value-plus-one": exception["value"] = exception["value"]!.GetValue<int>() + 1; break;
            case "missing-approve-condition": conditions.RemoveAt(approveIndex); break;
            case "weakened-approve-condition":
                conditions[approveIndex] = conditions[approveIndex]!.GetValue<string>()
                    .Replace("Its verdict must be APPROVE, with no BLOCKING or MAJOR finding.", "Its verdict should have no BLOCKING finding.", StringComparison.Ordinal);
                break;
            case "broadened-scope":
                exception["scope"] = exception["scope"]!.GetValue<string>()
                    .Replace(pr == 1502 ? "allows exactly one further commit and then" : "exactly one revert-only change",
                        "exactly one further fix", StringComparison.Ordinal);
                break;
            case "non-revert-condition":
                conditions[0] = conditions[0]!.GetValue<string>()
                    .Replace("It adds no logic", "It may add the logic the pass needs", StringComparison.Ordinal);
                break;
            case "fourth-change":
                conditions[0] = conditions[0]!.GetValue<string>()
                    .Replace("Changes (a) and (c), and every other change", "It may also fix anything else the pass reports. Changes (a) and (c), and every other change", StringComparison.Ordinal);
                break;
            case "solver-allowed":
                conditions[0] = conditions[0]!.GetValue<string>()
                    .Replace("makes no solver call", "may make a solver call", StringComparison.Ordinal);
                break;
            case "dropped-determinism": conditions.RemoveAt(IndexOf("No false proof, and deterministic.")); break;
            case "dropped-finding": exception.AsObject().Remove("findings"); break;
            case "other-finding": exception["findings"] = new JsonArray("D-NUM-OTHER"); break;
            case "dropped-discovery-condition": conditions.RemoveAt(IndexOf("Discovery finding on record.")); break;
            case "revert-only-flipped": exception["revertOnly"] = !exception["revertOnly"]!.GetValue<bool>(); break;
            case "no-revert-only": exception.AsObject().Remove("revertOnly"); break;
            case "other-base-commit": exception["baseCommit"] = "0123456789abcdef0123456789abcdef01234567"; break;
            case "no-base-commit": exception.AsObject().Remove("baseCommit"); break;
            case "reordered-conditions":
                var first = conditions[0]!.DeepClone();
                conditions.RemoveAt(0);
                conditions.Add(first);
                break;
            case "other-issue": exception["issue"] = 1311; break;
            case "unlogged-amendment": exception["amendment"] = "1.3.9"; break;
            case "names-findings": exception["findings"] = new JsonArray("D-OBL-PROOF-GETTER"); break;
            case "duplicated": exceptions.Add(exception.DeepClone()); break;
            case "added-for-another-pr":
                // The allowance is #1502's and #1503's alone; the same text for another PR is unregistered.
                var copy = exception.DeepClone();
                copy["pr"] = 1498;
                exceptions.Add(copy);
                break;
        }
        // Review round 1: the committed contract is clean, and the mutation fails on its own subject,
        // so a control cannot pass on an unrelated C011.
        Assert.Empty(EvidenceContractValidator.ValidateContract(Contract()));
        var violations = EvidenceContractValidator.ValidateContract(contract);
        AssertViolation(violations, "C011");
        var subject = mutation switch
        {
            "wrong-pr" => "exception review-rounds-per-pr #1497",
            "added-for-another-pr" => "exception review-rounds-per-pr #1498",
            "duplicated" => "exception review-rounds-per-pr",
            _ => $"exception review-rounds-per-pr #{pr}",
        };
        Assert.Contains(violations, v => v.Code == "C011" && v.Subject == subject);
    }

    [Theory]
    [InlineData(1502, "missing-approve-condition", "this exception needs the condition that the verification pass must APPROVE")]
    [InlineData(1502, "revert-only-flipped", "this exception must record revertOnly false")]
    [InlineData(1503, "revert-only-flipped", "this exception must record revertOnly true")]
    [InlineData(1502, "other-base-commit", "this exception must record baseCommit " + Base1502)]
    [InlineData(1502, "reordered-conditions", "the first condition must be the 'One change only.' condition naming the base commit")]
    [InlineData(1503, "reordered-conditions", "the first condition must be the 'Revert only.' condition naming the base commit")]
    [InlineData(1502, "dropped-finding", "exception must name exactly the findings " + WhileBoundFinding)]
    public void Amendment131StructuralChecksReportTheirOwnViolation(int pr, string mutation, string message)
    {
        // Beyond the text hash, each structural rule of a base-bound exception has its own violation.
        var contract = Contract();
        var exception = ChangeException(contract, pr);
        var conditions = exception["conditions"]!.AsArray();
        switch (mutation)
        {
            case "missing-approve-condition":
                conditions.Remove(conditions.Single(c => c!.GetValue<string>().StartsWith("Verification pass must APPROVE.", StringComparison.Ordinal)));
                break;
            case "revert-only-flipped": exception["revertOnly"] = !exception["revertOnly"]!.GetValue<bool>(); break;
            case "other-base-commit": exception["baseCommit"] = "0123456789abcdef0123456789abcdef01234567"; break;
            case "reordered-conditions":
                var first = conditions[0]!.DeepClone();
                conditions.RemoveAt(0);
                conditions.Add(first);
                break;
            case "dropped-finding": exception.AsObject().Remove("findings"); break;
        }
        Assert.Contains(EvidenceContractValidator.ValidateContract(contract),
            v => v.Code == "C011" && v.Subject == $"exception review-rounds-per-pr #{pr}" && v.Message == message);
    }

    [Theory]
    [InlineData("revertOnly")]
    [InlineData("baseCommit")]
    public void ExceptionNotRegisteredWithABaseCommitCannotCarryItsFields(string field)
    {
        // #1496's 1.3.0 exception has no registered base commit; adding either field is unregistered.
        var contract = Contract();
        var exception = ReviewException(contract);
        exception[field] = field == "revertOnly" ? JsonValue.Create(true) : JsonValue.Create(LastRoblFix);
        Assert.Contains(EvidenceContractValidator.ValidateContract(contract),
            v => v.Code == "C011" && v.Subject == ReviewSubject && v.Message == "this exception is not registered with a base commit");
    }

    [Fact]
    public void Amendment131ExceptionsWithoutAmendment131InTheLogFail()
    {
        // Dropping 1.3.1 from the log leaves both of its exceptions unregistered; 1.3.0's stay valid.
        var contract = Contract();
        var log = contract["amendmentLog"]!.AsArray();
        log.Remove(log.Single(a => a!["version"]!.GetValue<string>() == "1.3.1"));
        contract["contractVersion"] = log.Last()!["version"]!.DeepClone();
        var violations = EvidenceContractValidator.ValidateContract(contract);
        AssertViolation(violations, "C011");
        Assert.Contains(violations, v => v.Subject == "exception review-rounds-per-pr #1502");
        Assert.Contains(violations, v => v.Subject == "exception review-rounds-per-pr #1503");
        Assert.DoesNotContain(violations, v => v.Subject == ReviewSubject);
        Assert.DoesNotContain(violations, v => v.Subject == S2Subject);
    }

    private static JsonNode ChangeException(JsonNode contract, int pr)
        => contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray()
            .Single(e => e?["ceiling"]?.GetValue<string>() == "review-rounds-per-pr" && e?["pr"]?.GetValue<int>() == pr)!;

    private static JsonNode S2Exception(JsonNode contract)
        => contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray()
            .First(e => e?["pr"] is null && e?["issue"]?.GetValue<int>() == 1413)!;

    private static JsonNode ReviewException(JsonNode contract)
        => contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray()
            .First(e => e?["pr"]?.GetValue<int>() == 1496)!;

    // ------------------------------------------------------------------
    // Amendment 1.1.0: pending inventory updates (I013)
    // ------------------------------------------------------------------

    [Fact]
    public void PendingUpdateChangesNoArtifact()
    {
        // A pending update is recorded, not applied: the artifacts the validator reads are the ones
        // committed, and evidence rows are judged against them. The fixture would reclassify a stale
        // artifact and resolve its defect; neither may take effect.
        var inventory = Inventory();
        var update = PendingUpdate();
        update["artifact"] = "benchmark-provenance";
        update["classificationAfter"] = "authoritative";
        update["changes"] = new JsonObject { ["reason"] = "fixture: repaired" };
        update["defectResolutions"]![0]!["defect"] = Artifact(inventory, "benchmark-provenance")["openDefects"]![0]!.DeepClone();
        inventory["pendingUpdates"] = new JsonArray(update);
        var before = Artifact(Inventory(), "benchmark-provenance").ToJsonString();

        var violations = EvidenceContractValidator.ValidateInventory(Contract(), inventory);
        Assert.True(violations.Count == 0, Describe(violations));
        Assert.Equal(before, Artifact(inventory, "benchmark-provenance").ToJsonString());

        var row = Row();
        row["artifact"] = "benchmark-provenance";
        row["openDefectsResolvedBy"] = new JsonArray(9003);
        var rows = EvidenceContractValidator.ValidateEvidenceRows(
            Contract(), inventory, new JsonArray(row), Candidate, SemanticsVersion);
        AssertViolation(rows, "E005"); // still stale: the pending classification is not read
    }

    [Theory]
    [InlineData("unknown-artifact")]
    [InlineData("no-pr")]
    [InlineData("unknown-amendment")]
    [InlineData("applied-status")]
    [InlineData("unknown-classification")]
    [InlineData("no-changes")]
    [InlineData("paraphrased-defect")]
    [InlineData("resolution-without-pr")]
    [InlineData("duplicate-id")]
    [InlineData("resolutions-not-array")]
    [InlineData("updates-not-array")]
    public void MalformedPendingUpdateFails(string mutation)
    {
        var update = PendingUpdate();
        var inventory = Inventory();
        var updates = new JsonArray(update);
        inventory["pendingUpdates"] = updates;
        switch (mutation)
        {
            case "unknown-artifact": update["artifact"] = "ledger-index"; break;
            case "no-pr": update.Remove("repairingPr"); break;
            case "unknown-amendment": update["recordedInAmendment"] = "1.0.9"; break;
            case "applied-status": update["status"] = "applied"; break;
            case "unknown-classification": update["classificationAfter"] = "repaired"; break;
            case "no-changes": update.Remove("changes"); break;
            case "paraphrased-defect": update["defectResolutions"]![0]!["defect"] = "HEAD reachability."; break;
            case "resolution-without-pr": update["defectResolutions"]![0]!.AsObject().Remove("resolvedByPr"); break;
            case "duplicate-id": updates.Add(PendingUpdate()); break;
            case "resolutions-not-array": update["defectResolutions"] = "resolved by #9003"; break;
            case "updates-not-array": inventory["pendingUpdates"] = new JsonObject { ["fixture"] = "x" }; break;
        }
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I013");
    }

    // ------------------------------------------------------------------
    // Packet hash manifest coverage
    // ------------------------------------------------------------------

    [Fact]
    public void HashManifestMissingRequiredPacketFileFails()
    {
        var manifest = Load("sha256.json");
        var files = manifest["files"]!.AsObject();
        // Substitute an unrelated, correctly hashed file for the inventory: coverage must still fail.
        files.Remove(PacketDir + "/artifact-inventory.json");
        files["global.json"] = Sha256Lf(Path.Combine(RepoRoot(), "global.json"));
        AssertViolation(EvidenceContractValidator.ValidatePacketManifest(Contract(), manifest, ContractPath), "H001");
    }

    // ------------------------------------------------------------------
    // Fixtures and helpers
    // ------------------------------------------------------------------

    private static JsonObject Row() => new()
    {
        ["id"] = "row-1",
        ["artifact"] = "verifier-runtime-differential",
        ["outcome"] = "Proven",
        ["vacuous"] = false,
        ["counted"] = "established",
        ["sourceCommit"] = Candidate,
        ["recordedAtUtc"] = "2026-11-01T00:00:00Z",
        ["adjudication"] = "BOUNDED",
        ["registrationId"] = "R1-0001",
        ["producer"] = "tests/Calor.Verification.Tests/VerifierRuntimeDifferential",
        ["oracle"] = new JsonObject { ["id"] = "runtime-differential", ["agrees"] = true },
        ["translatorSemanticsVersion"] = SemanticsVersion,
        ["unresolvedFalseProof"] = false,
        ["openDefectsResolvedBy"] = new JsonArray(9001), // the #1135 defect on this artifact
    };

    private static JsonObject Pair(string pairId, string disposition, bool included) => new()
    {
        ["pairId"] = pairId,
        ["calorPath"] = $"{pairId}/program.calr",
        ["calorSha256"] = Hash('1'),
        ["csharpPath"] = $"{pairId}/Program.cs",
        ["csharpSha256"] = Hash('2'),
        ["taskStatementSha256"] = Hash('3'),
        ["inputSet"] = "inputs/v1",
        ["expectedOutputs"] = "outputs/v1",
        ["failureBehavior"] = "same exception type on malformed input",
        ["disposition"] = disposition,
        ["equivalenceEvidence"] = "differential run 1",
        ["reviewer"] = "@juanmicrosoft",
        ["included"] = included,
    };

    /// <summary>A well-formed amendment one MINOR version above the committed contract.</summary>
    private static JsonObject Amendment() => new()
    {
        ["version"] = NextMinorVersion(),
        ["timestampUtc"] = "2026-10-20T00:00:00Z",
        ["reviewedInPr"] = 9999,
        ["afterDecisionBearingInspection"] = false,
        ["justification"] = "Control fixture for the amendment rule.",
        ["removedRows"] = new JsonArray(),
    };

    private static string NextMinorVersion()
    {
        var parts = Contract()["contractVersion"]!.GetValue<string>().Split('.').Select(int.Parse).ToArray();
        return $"{parts[0]}.{parts[1] + 1}.0";
    }

    private static JsonNode AmendedContract(JsonObject amendment, JsonNode? baseContract = null)
    {
        var contract = baseContract ?? Contract();
        contract["contractVersion"] = amendment["version"]?.DeepClone();
        contract["amendmentLog"]!.AsArray().Add(amendment);
        return contract;
    }

    /// <summary>
    /// The committed contract in the PROPOSED lifecycle state, whatever state is committed. After the
    /// acceptance write-back merges, this undoes it in memory so the proposed-state controls keep
    /// testing the proposed state instead of silently changing meaning. The proposed state precedes
    /// every amendment, so the log is emptied (later amendments, such as 1.1.0, follow the freeze).
    /// </summary>
    private static JsonNode ProposedContract()
    {
        var contract = Contract();
        // Sentences that amendment 1.1.0 appended to frozen text are marked "(amendment 1.1.0)" or
        // "amendment 1.1.0)"; strip them so the fixture is the original text, not a hybrid.
        RevertAmendedSentences(contract, "amendment 1.1.0");
        RevertAmendedSentences(contract, "amendment 1.2.0");
        contract.AsObject().Remove("acceptance");
        contract["amendmentLog"]!.AsArray().Clear();
        contract["contractVersion"] = "1.0.0";
        // Rules added by amendments do not exist in the proposed state.
        contract["benchmarkEquivalence"]!.AsObject().Remove("taskStatementRule");
        contract["authorityCapacity"]!["capacity"]!.AsObject().Remove("exceptions");
        contract["authorityCapacity"]!["capacity"]!.AsObject().Remove("chargeRules");
        contract["authorityCapacity"]!["capacity"]!.AsObject().Remove("evidenceDataRule");
        contract["rules"]!.AsObject().Remove("platformDeterminism");
        contract.AsObject().Remove("determinismRows");
        contract["status"] = "PROPOSED";
        contract["gateStatus"] = "NOT-MET";
        contract["authorityCapacity"]!["capacity"]!["status"] = "PROPOSED";
        foreach (var ceiling in contract["authorityCapacity"]!["capacity"]!["ceilings"]!.AsArray())
            ceiling!["status"] = "PROPOSED";
        return contract;
    }

    private static void RevertAmendedSentences(JsonNode node, string marker)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Contains(marker, StringComparison.Ordinal))
                        obj[key] = string.Join(" ", System.Text.RegularExpressions.Regex.Split(text, @"(?<=\.)\s+")
                            .Where(sentence => !sentence.Contains(marker, StringComparison.Ordinal)));
                    else if (obj[key] is { } child)
                        RevertAmendedSentences(child, marker);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text) && text.Contains(marker, StringComparison.Ordinal))
                        array[i] = string.Join(" ", System.Text.RegularExpressions.Regex.Split(text, @"(?<=\.)\s+")
                            .Where(sentence => !sentence.Contains(marker, StringComparison.Ordinal)));
                    else if (array[i] is { } child)
                        RevertAmendedSentences(child, marker);
                }
                break;
        }
    }

    /// <summary>The inventory matching <see cref="ProposedContract"/>.</summary>
    private static JsonNode ProposedInventory()
    {
        var inventory = Inventory();
        inventory.AsObject().Remove("pendingUpdates"); // recorded by amendments after the freeze
        inventory["contractVersion"] = ProposedContract()["contractVersion"]!.DeepClone();
        return inventory;
    }

    /// <summary>The proposed contract after a well-formed acceptance write-back (lifecycle FROZEN).</summary>
    private static JsonNode FrozenContract()
    {
        var acceptance = Amendment();
        acceptance["version"] = "1.0.1";
        acceptance["justification"] = "acceptance write-back";
        var contract = AmendedContract(acceptance, ProposedContract());
        contract["status"] = "FROZEN";
        contract["gateStatus"] = "MET";
        contract["authorityCapacity"]!["capacity"]!["status"] = "ACCEPTED";
        foreach (var ceiling in contract["authorityCapacity"]!["capacity"]!["ceilings"]!.AsArray())
            ceiling!["status"] = "ACCEPTED";
        contract["acceptance"] = new JsonObject
        {
            ["mergeCommit"] = "3333333333333333333333333333333333333333",
            ["mergedAtUtc"] = "2026-10-02T00:00:00Z",
            ["pr"] = 1466,
        };
        return contract;
    }

    /// <summary>A well-formed pending inventory update, independent of what is committed.</summary>
    private static JsonObject PendingUpdate() => new()
    {
        ["id"] = "fixture-ledger-provenance-index",
        ["artifact"] = "ledger-provenance-index",
        ["repairingPr"] = 9003,
        ["recordedInAmendment"] = Contract()["contractVersion"]!.DeepClone(),
        ["status"] = "pending-merge",
        ["classificationAfter"] = "authoritative",
        ["changes"] = new JsonObject(),
        ["defectResolutions"] = new JsonArray(new JsonObject
        {
            ["defect"] = Artifact(Inventory(), "ledger-provenance-index")["openDefects"]![0]!.DeepClone(),
            ["resolvedByPr"] = 9003,
            ["scope"] = "fixture",
        }),
    };

    private static JsonObject ComparabilityKey() => new()
    {
        ["pairManifestSha256"] = Hash('a'),
        ["metricSetSha256"] = Hash('b'),
        ["metricImplementationVersion"] = "1",
        ["aggregationMethod"] = "geometric-mean-over-equivalent-pairs",
        ["samplingUnit"] = "program-pair",
        ["runCount"] = 30,
        ["generatorVersion"] = "1.0",
        ["exclusionsSha256"] = Hash('c'),
    };

    private static string Hash(char c) => new(c, 64);

    private static JsonObject Adjudication(string subject, string outcome) => new()
    {
        ["subject"] = subject,
        ["outcome"] = outcome,
        ["independence"] = "reduced-maintainer-adjudicated",
    };

    /// <summary>
    /// A complete success record: every inventory artifact (historical-only ones as HISTORICAL-ONLY,
    /// the rest BOUNDED) and every child gate except #1408, with the reduced-independence statement.
    /// </summary>
    private static JsonObject SuccessRecord()
    {
        var rows = new JsonArray(Adjudication(RegisteredClaim, "BOUNDED"));
        foreach (var artifact in RepairedInventory()["artifacts"]!.AsArray())
        {
            var historical = artifact!["classification"]!.GetValue<string>() == "historical-only";
            rows.Add(Adjudication(artifact["id"]!.GetValue<string>(), historical ? "HISTORICAL-ONLY" : "BOUNDED"));
        }
        foreach (var child in Contract()["children"]!.AsArray())
        {
            var issue = child!["issue"]!.GetValue<int>();
            if (issue != 1408)
                rows.Add(Adjudication($"gate:#{issue}", "BOUNDED"));
        }
        return new JsonObject
        {
            ["outcome"] = "MILESTONE-SUCCEEDED",
            ["adjudicationIndependence"] = "reduced",
            ["limitation"] = Contract()["authorityCapacity"]!["independence"]!["publishedLimitation"]!.GetValue<string>(),
            ["epicIndependentAdjudicationMet"] = false,
            ["adjudications"] = rows,
        };
    }

    private static JsonNode Subject(JsonNode record, string subject)
        => record["adjudications"]!.AsArray().First(r => r!["subject"]!.GetValue<string>() == subject)!;

    private const string RegisteredClaim = "claim:verifier-coverage";

    /// <summary>
    /// The committed inventory after a synthetic repair amendment that reclassifies every stale
    /// artifact as authoritative; success is impossible while any artifact is still stale.
    /// </summary>
    private static JsonNode RepairedInventory()
    {
        var inventory = Inventory();
        foreach (var artifact in inventory["artifacts"]!.AsArray())
        {
            if (artifact!["classification"]!.GetValue<string>() == "stale")
                artifact["classification"] = "authoritative";
        }
        return inventory;
    }

    private static IReadOnlyList<ContractViolation> Terminal(JsonNode record, JsonNode? inventory = null)
        => EvidenceContractValidator.ValidateTerminalRecord(
            Contract(), inventory ?? RepairedInventory(), [RegisteredClaim], record);

    /// <summary>A decision-bearing benchmark row on the candidate, citing the authoritative corpus.</summary>
    private static JsonObject BenchmarkRow()
    {
        var row = Row();
        row["counted"] = "not-established";
        row["artifact"] = "benchmark-corpus";
        row["openDefectsResolvedBy"] = new JsonArray(9002); // the #1276 defect on the corpus
        row["benchmark"] = new JsonObject
        {
            ["samplingUnit"] = "program-pair",
            ["comparability"] = ComparabilityKey(),
            ["comparedTo"] = ComparabilityKey(),
            ["pairs"] = new JsonArray(
                Pair("CsvParser", "EQUIVALENT", included: true),
                Pair("Other", "NOT-EQUIVALENT", included: false)),
        };
        return row;
    }

    private static IReadOnlyList<ContractViolation> Rows(params JsonObject[] rows)
    {
        var array = new JsonArray();
        foreach (var row in rows) array.Add(row.DeepClone());
        return EvidenceContractValidator.ValidateEvidenceRows(Contract(), Inventory(), array, Candidate, SemanticsVersion);
    }

    /// <summary>
    /// The mutation must produce <paramref name="code"/>, and nothing outside
    /// <paramref name="code"/> and the explicitly <paramref name="alsoAllowed"/> codes, so a control
    /// cannot pass because some unrelated rule fired.
    /// </summary>
    internal static void AssertViolation(IReadOnlyList<ContractViolation> violations, string code, params string[] alsoAllowed)
    {
        Assert.True(violations.Any(v => v.Code == code),
            $"expected a {code} violation; got:{Environment.NewLine}{Describe(violations)}");
        var unexpected = violations.Where(v => v.Code != code && !alsoAllowed.Contains(v.Code)).ToList();
        Assert.True(unexpected.Count == 0,
            $"expected only {code}; also got:{Environment.NewLine}{Describe(unexpected)}");
    }

    internal static string Describe(IReadOnlyList<ContractViolation> violations)
        => violations.Count == 0 ? "(none)" : string.Join(Environment.NewLine, violations);

    private static JsonNode Artifact(JsonNode inventory, string id)
        => inventory["artifacts"]!.AsArray().First(a => a!["id"]!.GetValue<string>() == id)!;

    private static JsonNode Outcome(JsonNode contract, string token)
        => contract["outcomeVocabulary"]!["outcomes"]!.AsArray().First(o => o!["token"]!.GetValue<string>() == token)!;

    private static JsonNode Child(JsonNode contract, int issue)
        => contract["children"]!.AsArray().First(c => c!["issue"]!.GetValue<int>() == issue)!;

    private static JsonNode Contract() => Load("contract.json");

    private static JsonNode Inventory() => Load("artifact-inventory.json");

    private static JsonNode Load(string file)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), PacketDir, file)))!;

    /// <summary>SHA-256 over LF-normalized bytes, so a CRLF checkout hashes the same as main.</summary>
    private static string Sha256Lf(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        return Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));
    }

    internal static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null
            && !Directory.Exists(Path.Combine(dir, ".git"))
            && !File.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
