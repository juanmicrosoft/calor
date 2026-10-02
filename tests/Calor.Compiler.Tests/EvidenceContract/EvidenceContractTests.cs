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
        foreach (var added in new[] { "1.1.0", "documentationOnlyDeploy", "taskStatementRule", "determinismRows", "platformDeterminism", "evidenceDataRule" })
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
    // Amendment 1.1.0: documentation-only website deploy (C013, D001-D006)
    // ------------------------------------------------------------------

    [Fact]
    public void WellFormedDocsDeployAuditPasses()
    {
        var violations = EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), DocsDeployAudit());
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void DocsDeployAuditOnAnAuditedBasePasses()
    {
        var record = DocsDeployAudit();
        record["baseKind"] = "audited-docs-deploy";
        record["baseCommit"] = "4444444444444444444444444444444444444444";
        record["baseRunId"] = 37000000001L;
        var violations = EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record);
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void CommittedDocsDeployRuleNeverAllowsAWebsiteInventoryPath()
    {
        // Every inventoried website path (benchmark pages, data files, changelog) is refused, so a
        // documentation-only deploy can never change a stale or historical publication surface.
        var websitePaths = Inventory()["artifacts"]!.AsArray()
            .SelectMany(a => a!["paths"]!.AsArray().Select(p => p!.GetValue<string>()))
            .Where(p => p.StartsWith("website/", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(websitePaths);
        foreach (var path in websitePaths)
        {
            var record = DocsDeployAudit();
            record["changedPaths"] = new JsonArray(path);
            AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D003");
        }
    }

    [Theory]
    [InlineData("website/public/data/benchmark-results.json")]
    [InlineData("website/public/data/new-claims.json")]
    [InlineData("website/content/benchmarking/methodology.mdx")]
    [InlineData("website/content/philosophy/static-verification.mdx")]
    [InlineData("website/content/changelog.mdx")]
    [InlineData("website/content/index.mdx")]
    [InlineData("website/content/guides/verification-guarantees.mdx")]
    [InlineData("website/content/contributing/adding-benchmarks.mdx")]
    [InlineData("website/content/cli/benchmark.mdx")]
    [InlineData("website/src/components/landing/BenchmarkChart.tsx")]
    [InlineData("website/package.json")]
    [InlineData("website/next.config.js")]
    [InlineData("website/public/images/diagram.png")]
    [InlineData("website/content/cli/compile.tsx")]
    [InlineData("website/content/cli/../benchmarking/results.mdx")]
    [InlineData("Website/content/cli/compile.mdx")]
    [InlineData("/website/content/cli/compile.mdx")]
    [InlineData("src/Calor.Compiler/Program.cs")]
    public void NonDocumentationPathFails(string path)
    {
        var record = DocsDeployAudit();
        record["changedPaths"]!.AsArray().Add(path);
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D003");
    }

    [Fact]
    public void DocsDeployAuditWithoutChangedPathsFails()
    {
        var record = DocsDeployAudit();
        record.Remove("changedPaths");
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D003");
    }

    [Theory]
    [InlineData("package")]
    [InlineData("githubRelease")]
    [InlineData("tag")]
    [InlineData("benchmarkData")]
    [InlineData("releaseNotes")]
    public void DocsDeployThatPublishesAnythingElseFails(string flag)
    {
        var record = DocsDeployAudit();
        record["publishes"]![flag] = true;
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D005");

        record["publishes"]!.AsObject().Remove(flag);
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D005");
    }

    [Fact]
    public void DocsDeployAfterTerminalSuccessFails()
    {
        var record = DocsDeployAudit();
        record["terminalSuccessRecordPresent"] = true;
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D006");

        record.Remove("terminalSuccessRecordPresent");
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D006");
    }

    [Theory]
    [InlineData("other-base-commit")]
    [InlineData("other-base-run")]
    [InlineData("unknown-base-kind")]
    [InlineData("short-deployed-commit")]
    [InlineData("own-base")]
    public void DocsDeployWithAnInvalidBaseFails(string mutation)
    {
        var record = DocsDeployAudit();
        switch (mutation)
        {
            case "other-base-commit": record["baseCommit"] = "5555555555555555555555555555555555555555"; break;
            case "other-base-run": record["baseRunId"] = 34999741476L; break;
            case "unknown-base-kind": record["baseKind"] = "last-successful-deploy"; break;
            case "short-deployed-commit": record["deployedCommit"] = "72a0a855"; break;
            case "own-base":
                record["baseKind"] = "audited-docs-deploy";
                record["baseRunId"] = record["runId"]!.DeepClone();
                break;
        }
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D002");
    }

    [Theory]
    [InlineData("allowlist-failed")]
    [InlineData("allowlist-disallowed")]
    [InlineData("wording-failed")]
    [InlineData("file-mode-failed")]
    [InlineData("mdx-missing")]
    [InlineData("attestation-reworded")]
    [InlineData("attestation-unsigned")]
    public void DocsDeployWithAFailedOrMissingCheckFails(string mutation)
    {
        var record = DocsDeployAudit();
        switch (mutation)
        {
            case "allowlist-failed": record["allowlistCheck"]!["result"] = "skipped"; break;
            case "allowlist-disallowed": record["allowlistCheck"]!["disallowedPaths"] = new JsonArray("website/package.json"); break;
            case "wording-failed": record["wordingCheck"]!["result"] = "failed"; break;
            case "file-mode-failed": record["fileModeCheck"]!["result"] = "failed"; break;
            case "mdx-missing": record.Remove("mdxCheck"); break;
            case "attestation-reworded": record["attestation"]!["statement"] = "No benchmark numbers changed."; break;
            case "attestation-unsigned": record["attestation"]!.AsObject().Remove("by"); break;
        }
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D004");
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("contract-before-amendment")]
    [InlineData("workflow")]
    [InlineData("run-id")]
    [InlineData("no-run-id")]
    [InlineData("unlogged-future-version")]
    public void DocsDeployAuditWithWrongIdentityFails(string mutation)
    {
        var record = DocsDeployAudit();
        switch (mutation)
        {
            case "schema": record["schema"] = "calor.docs-only-deploy-audit/0"; break;
            case "contract-before-amendment": record["contractVersion"] = "1.0.1"; break;
            case "workflow": record["workflow"] = ".github/workflows/benchmark.yml"; break;
            case "run-id": record["runId"] = "37000000002"; break;
            case "no-run-id": record.Remove("runId"); break;
            case "unlogged-future-version": record["contractVersion"] = "9.9.9"; break;
        }
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D001");
    }

    [Fact]
    public void ContractWithoutTheDocsDeployRuleRefusesEveryAudit()
    {
        var contract = Contract();
        contract["releasePath"]!.AsObject().Remove("documentationOnlyDeploy");
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(contract, Inventory(), DocsDeployAudit()), "D001");
    }

    [Theory]
    [InlineData("website/public/data/**")]
    [InlineData("website/src/**/*.mdx")]
    [InlineData("website/content/**/*.mdx")]
    [InlineData("website/content/benchmarking/**/*.mdx")]
    [InlineData("website/content/../public/data/*.mdx")]
    public void DocsDeployAllowlistWidenedBeyondTheRegisteredOneFails(string pattern)
    {
        var contract = Contract();
        contract["releasePath"]!["documentationOnlyDeploy"]!["allowedPaths"]!.AsArray().Add(pattern);
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C013");
    }

    [Theory]
    [InlineData("website/public/data/**")]
    [InlineData("website/content/philosophy/**")]
    [InlineData("website/content/guides/verification-guarantees.mdx")]
    public void DocsDeployDenylistLosingARegisteredDenialFails(string pattern)
    {
        var contract = Contract();
        var denied = contract["releasePath"]!["documentationOnlyDeploy"]!["deniedPaths"]!.AsArray();
        denied.Remove(denied.First(p => p!.GetValue<string>() == pattern));
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C013");
    }

    [Theory]
    [InlineData("attestation")]
    [InlineData("pre-contract-base")]
    [InlineData("workflow-not-machinery")]
    [InlineData("website-not-diffed")]
    public void DocsDeployRuleLosingARegisteredConstraintFails(string mutation)
    {
        var contract = Contract();
        var rule = contract["releasePath"]!["documentationOnlyDeploy"]!;
        switch (mutation)
        {
            case "attestation": rule["auditRecord"]!["attestationStatement"] = "Docs only."; break;
            case "pre-contract-base": rule["base"]!["preContractDeploy"]!["commit"] = "b1d23d7d74aa17a460d6a7d429836ca8396bb182"; break;
            case "workflow-not-machinery": rule["machinery"]!["paths"] = new JsonArray("scripts/verify_release_adjudication.py"); break;
            case "website-not-diffed": rule["buildInputs"] = new JsonArray("website/content/"); break;
        }
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C013");
    }

    [Fact]
    public void InventoriedPathIsRefusedEvenWhenTheAllowlistMatches()
    {
        // Isolates the inventory-path exclusion: the path is allowed and not denied, so only its
        // membership in the inventory can refuse it.
        var inventory = Inventory();
        Artifact(inventory, "changelog")["paths"]!.AsArray().Add("website/content/cli/compile.mdx");
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), inventory, DocsDeployAudit()), "D003");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("website-path")]
    [InlineData("no-pr")]
    public void MalformedMachineryChangeFails(string mutation)
    {
        var record = DocsDeployAudit();
        switch (mutation)
        {
            case "missing": record.Remove("machineryChanges"); break;
            case "website-path": record["machineryChanges"]![0]!["path"] = "website/package.json"; break;
            case "no-pr": record["machineryChanges"]![0]!.AsObject().Remove("pr"); break;
        }
        AssertViolation(EvidenceContractValidator.ValidateDocsDeployAudit(Contract(), Inventory(), record), "D003");
    }

    // ------------------------------------------------------------------
    // Amendment 1.1.0: per-PR ceiling exception (C011)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("website/content/cli/**/*.mdx", "website/content/cli/compile.mdx", true)]
    [InlineData("website/content/cli/**/*.mdx", "website/content/cli/nested/page.mdx", true)]
    [InlineData("website/content/cli/**/*.mdx", "website/content/cli.mdx", false)]
    [InlineData("website/content/cli/**/*.mdx", "website/content/client/page.mdx", false)]
    [InlineData("website/content/cli/**/*.mdx", "website/content/cli/page.mdx.bak", false)]
    [InlineData("website/public/data/**", "website/public/data/a/b.json", true)]
    [InlineData("website/content/changelog.mdx", "website/content/changelogXmdx", false)]
    public void DocsDeployGlobsMatchWholePaths(string glob, string path, bool matches)
        => Assert.Equal(matches, EvidenceContractValidator.GlobRegex(glob).IsMatch(path));

    [Theory]
    [InlineData("other-value")]
    [InlineData("other-pr")]
    [InlineData("other-ceiling")]
    [InlineData("unlogged-amendment")]
    [InlineData("no-justification")]
    [InlineData("added-exception")]
    public void CeilingExceptionOtherThanTheRegisteredOneFails(string mutation)
    {
        // Decision 6 raises one ceiling for one PR to one value; nothing else passes without a new
        // amendment and a validator change.
        var contract = Contract();
        var exceptions = contract["authorityCapacity"]!["capacity"]!["exceptions"]!.AsArray();
        var exception = exceptions.First(e => e!["pr"]!.GetValue<int>() == 1473)!;
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
        }
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C011");
    }

    // ------------------------------------------------------------------
    // Amendment 1.1.0: pending inventory updates (I013)
    // ------------------------------------------------------------------

    [Fact]
    public void PendingUpdateChangesNoArtifact()
    {
        // A pending update is recorded, not applied: the artifacts the validator reads are the ones
        // committed, and evidence rows are judged against them.
        var inventory = Inventory();
        inventory["pendingUpdates"] = new JsonArray(PendingUpdate());
        var before = Artifact(Inventory(), "ledger-provenance-index").ToJsonString();
        Assert.Equal(before, Artifact(inventory, "ledger-provenance-index").ToJsonString());
        var violations = EvidenceContractValidator.ValidateInventory(Contract(), inventory);
        Assert.True(violations.Count == 0, Describe(violations));
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
        contract.AsObject().Remove("acceptance");
        contract["amendmentLog"]!.AsArray().Clear();
        contract["contractVersion"] = "1.0.0";
        // Rules added by amendments do not exist in the proposed state.
        contract["releasePath"]!.AsObject().Remove("documentationOnlyDeploy");
        contract["benchmarkEquivalence"]!.AsObject().Remove("taskStatementRule");
        contract["authorityCapacity"]!["capacity"]!.AsObject().Remove("exceptions");
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

    /// <summary>A well-formed audit record of a documentation-only deploy on the pre-contract base.</summary>
    private static JsonObject DocsDeployAudit()
    {
        var rule = Contract()["releasePath"]!["documentationOnlyDeploy"]!;
        return new JsonObject
        {
            ["schema"] = "calor.docs-only-deploy-audit/1",
            ["contractVersion"] = Contract()["contractVersion"]!.DeepClone(),
            ["workflow"] = ".github/workflows/nextjs-gh-pages.yml",
            ["runId"] = 37000000002L,
            ["deployedCommit"] = "6666666666666666666666666666666666666666",
            ["baseKind"] = "pre-contract-deploy",
            ["baseCommit"] = rule["base"]!["preContractDeploy"]!["commit"]!.DeepClone(),
            ["baseRunId"] = rule["base"]!["preContractDeploy"]!["runId"]!.DeepClone(),
            ["changedPaths"] = new JsonArray("website/content/cli/compile.mdx", "website/content/guides/telemetry.mdx"),
            ["machineryChanges"] = new JsonArray(new JsonObject
            {
                ["path"] = ".github/workflows/nextjs-gh-pages.yml",
                ["pr"] = 1475,
            }),
            ["allowlistCheck"] = new JsonObject { ["result"] = "passed", ["disallowedPaths"] = new JsonArray() },
            ["fileModeCheck"] = new JsonObject { ["result"] = "passed" },
            ["mdxCheck"] = new JsonObject { ["result"] = "passed" },
            ["wordingCheck"] = new JsonObject { ["result"] = "passed" },
            ["publishes"] = new JsonObject
            {
                ["package"] = false, ["githubRelease"] = false, ["tag"] = false,
                ["benchmarkData"] = false, ["releaseNotes"] = false,
            },
            ["terminalSuccessRecordPresent"] = false,
            ["attestation"] = new JsonObject
            {
                ["by"] = "@juanmicrosoft",
                ["statement"] = rule["auditRecord"]!["attestationStatement"]!.DeepClone(),
            },
            ["recordedAtUtc"] = "2026-10-10T00:00:00Z",
        };
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
    private static void AssertViolation(IReadOnlyList<ContractViolation> violations, string code, params string[] alsoAllowed)
    {
        Assert.True(violations.Any(v => v.Code == code),
            $"expected a {code} violation; got:{Environment.NewLine}{Describe(violations)}");
        var unexpected = violations.Where(v => v.Code != code && !alsoAllowed.Contains(v.Code)).ToList();
        Assert.True(unexpected.Count == 0,
            $"expected only {code}; also got:{Environment.NewLine}{Describe(unexpected)}");
    }

    private static string Describe(IReadOnlyList<ContractViolation> violations)
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

    private static string RepoRoot()
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
