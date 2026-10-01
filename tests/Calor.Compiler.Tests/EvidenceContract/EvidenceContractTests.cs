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
    private const string Candidate = "1111111111111111111111111111111111111111";

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
    public void CommittedContractIsNotMarkedMetWhileCapacityIsProposed()
    {
        var contract = Contract();
        Assert.Equal("NOT-MET", contract["gateStatus"]!.GetValue<string>());
        Assert.Equal("PROPOSED", contract["authorityCapacity"]!["capacity"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void CommittedVocabularyMatchesTheVerifierStatusEnums()
    {
        // Every status the verifier can emit must be named by exactly one frozen token, so no
        // emitted status is left unclassified.
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
        Assert.True(files.Count >= 3, "sha256.json must cover the document, contract, and inventory");

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

        row["nonVacuityChecked"] = true;
        var violations = Rows(row);
        Assert.True(violations.Count == 0, Describe(violations));
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
        AssertViolation(Rows(row), "E005");
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
            Contract(), Inventory(), [Row()], candidateCommit: null), "E006");
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
        AssertViolation(Rows(row), "E012");
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
        row["benchmark"]!["comparability"]![field] = field == "runCount" ? 1 : "changed";
        AssertViolation(Rows(row), "E008");
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
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I007");
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
        AssertViolation(EvidenceContractValidator.ValidateInventory(Contract(), inventory), "I002");
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
        var contract = Contract();
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
        AssertViolation(EvidenceContractValidator.ValidateContract(contract), "C007");
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
    public void AmendmentWithoutJustificationOrVersionBumpFails()
    {
        var contract = Contract();
        contract["amendmentLog"]!.AsArray().Add(new JsonObject
        {
            ["version"] = "1.1.0",
            ["timestampUtc"] = "2026-10-20T00:00:00Z",
            ["reviewedInPr"] = 9999,
            ["afterDecisionBearingInspection"] = true,
            ["justification"] = "",
            ["removedRows"] = new JsonArray(new JsonObject { ["row"] = "S1-042" }),
        });
        var violations = EvidenceContractValidator.ValidateContract(contract);
        // No justification, a removed row without its last status, and contractVersion still 1.0.0.
        Assert.Equal(3, violations.Count(x => x.Code == "C010"));
    }

    [Fact]
    public void WellFormedAmendmentPasses()
    {
        var contract = Contract();
        contract["contractVersion"] = "1.1.0";
        contract["amendmentLog"]!.AsArray().Add(new JsonObject
        {
            ["version"] = "1.1.0",
            ["timestampUtc"] = "2026-10-20T00:00:00Z",
            ["reviewedInPr"] = 9999,
            ["afterDecisionBearingInspection"] = false,
            ["justification"] = "Positive control for the amendment rule.",
            ["removedRows"] = new JsonArray(),
        });
        var violations = EvidenceContractValidator.ValidateContract(contract);
        Assert.True(violations.Count == 0, Describe(violations));
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
    };

    private static JsonObject ComparabilityKey() => new()
    {
        ["pairManifestSha256"] = "aa",
        ["metricSetSha256"] = "bb",
        ["metricImplementationVersion"] = "1",
        ["aggregationMethod"] = "geometric-mean-over-equivalent-pairs",
        ["samplingUnit"] = "program-pair",
        ["runCount"] = 30,
        ["generatorVersion"] = "1.0",
        ["exclusionsSha256"] = "cc",
    };

    private static JsonObject BenchmarkRow()
    {
        var row = Row();
        row["counted"] = "not-established";
        row["benchmark"] = new JsonObject
        {
            ["samplingUnit"] = "program-pair",
            ["comparability"] = ComparabilityKey(),
            ["comparedTo"] = ComparabilityKey(),
            ["pairs"] = new JsonArray(
                new JsonObject { ["pairId"] = "CsvParser", ["disposition"] = "EQUIVALENT", ["included"] = true },
                new JsonObject { ["pairId"] = "Other", ["disposition"] = "NOT-EQUIVALENT", ["included"] = false }),
        };
        return row;
    }

    private static IReadOnlyList<ContractViolation> Rows(params JsonObject[] rows)
    {
        var array = new JsonArray();
        foreach (var row in rows) array.Add(row.DeepClone());
        return EvidenceContractValidator.ValidateEvidenceRows(Contract(), Inventory(), array, Candidate);
    }

    private static void AssertViolation(IReadOnlyList<ContractViolation> violations, string code)
        => Assert.True(violations.Any(v => v.Code == code),
            $"expected a {code} violation; got:{Environment.NewLine}{Describe(violations)}");

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
