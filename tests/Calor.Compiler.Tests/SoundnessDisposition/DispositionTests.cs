using System.Text.Json.Nodes;
using Xunit;

namespace Calor.Compiler.Tests.SoundnessDisposition;

/// <summary>
/// #1413 (0.24 S2): the committed disposition record validates, and each negative control
/// changes one fact and asserts its specific code (plus only the co-firing codes it names).
/// </summary>
public sealed class DispositionTests
{
    private const string RecordPath = "docs/plans/evidence/s2-1413/dispositions.json";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static JsonNode Load(string relative)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), relative)))!;

    private static JsonNode Record() => Load(RecordPath);

    private static IReadOnlyList<DispositionValidator.Violation> Validate(JsonNode record, bool closing = false)
        => DispositionValidator.Validate(
            record,
            Load("docs/plans/evidence/evidence-contract-1407/contract.json"),
            Load("docs/plans/evidence/s1-1311/run2/findings.json"),
            Load("docs/plans/evidence/s1-1311/findings.json"),
            Load("docs/plans/evidence/s1-1311/combined-row-status.json"),
            closing);

    private static void AssertCodes(IReadOnlyList<DispositionValidator.Violation> violations, params string[] codes)
        => Assert.Equal(
            codes.OrderBy(c => c, StringComparer.Ordinal),
            violations.Select(v => v.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal));

    private static JsonObject Finding(JsonNode record, string baseline, string id)
        => record["baselines"]![baseline]!["findings"]!.AsArray().Single(f => f!["findingId"]!.GetValue<string>() == id)!.AsObject();

    private static JsonObject Row(JsonNode record, string baseline, string id)
        => record["baselines"]![baseline]!["rows"]!.AsArray().Single(r => r!["rowId"]!.GetValue<string>() == id)!.AsObject();

    private static JsonObject Repair(JsonNode record, string id)
        => record["repairs"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == id)!.AsObject();

    // ------------------------------------------------------------------ positive

    [Fact]
    public void CommittedRecord_ValidatesOpen()
        => AssertCodes(Validate(Record()));

    [Fact]
    public void CommittedRecord_IsNotYetClosable()
    {
        // Unmerged repairs and the undecided R-NUM keep S2 open.
        var violations = Validate(Record(), closing: true);
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-NUM: undecided at closure"));
        Assert.Contains(violations, v => v.Message.Contains("R-CACHE: not merged at closure"));
    }

    [Fact]
    public void CommittedRecord_DispositionsEveryFindingAndRowOnBothBaselines()
    {
        var record = Record();
        foreach (var baseline in new[] { "B1", "N1" })
        {
            Assert.Equal(34, record["baselines"]![baseline]!["findings"]!.AsArray().Count);
            Assert.Equal(100, record["baselines"]![baseline]!["rows"]!.AsArray().Count);
        }
    }

    [Fact]
    public void CommittedRecord_PinsTheS1PacketItDispositions()
    {
        // The record is bound to the exact S1 packet: a changed source means re-dispositioning.
        var sources = Record()["sources"]!["sha256"]!.AsObject();
        Assert.Equal(3, sources.Count);
        foreach (var (path, expected) in sources)
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), path));
            var normalized = System.Text.Encoding.UTF8.GetBytes(
                System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(normalized)).ToLowerInvariant();
            Assert.Equal(expected!.GetValue<string>(), actual);
        }
    }

    [Fact]
    public void FullyMergedRecord_Closes()
    {
        var record = Record();
        foreach (var repair in record["repairs"]!.AsArray())
        {
            repair!["status"] = "merged";
            repair["mergeCommit"] = new string('a', 40);
            repair["pr"] ??= 9999;
            repair["branch"] ??= "milestone-0.24/s2-1413-fix-example";
            if (repair["regressionWitness"]!.AsArray().Count == 0)
                repair["regressionWitness"]!.AsArray().Add("tests/example.cs");
        }
        record["closure"]!["status"] = "CLOSED";
        AssertCodes(Validate(record, closing: true));
    }

    // ------------------------------------------------------------- negative controls

    [Fact]
    public void D001_VocabularyChanged()
    {
        var record = Record();
        record["vocabulary"]!.AsArray().Add("KNOWN-INCOMPLETENESS");
        AssertCodes(Validate(record), "D001");
    }

    [Fact]
    public void D001_UnknownFindingDisposition()
    {
        var record = Record();
        Finding(record, "B1", "F-B1-033")["disposition"] = "KNOWN-INCOMPLETENESS";
        // The row's finding set loses one entry but its other findings keep it consistent.
        AssertCodes(Validate(record), "D001");
    }

    [Fact]
    public void D002_MissingFinding()
    {
        var record = Record();
        var findings = record["baselines"]!["N1"]!["findings"]!.AsArray();
        findings.Remove(findings.Single(f => f!["findingId"]!.GetValue<string>() == "F-N1-030"));
        // IMPL-DIVISION-TOTALIZED then has no dispositioned finding: D012 co-fires.
        AssertCodes(Validate(record), "D002", "D012");
    }

    [Fact]
    public void D002_DuplicateFinding()
    {
        var record = Record();
        var findings = record["baselines"]!["B1"]!["findings"]!.AsArray();
        findings.Add(Finding(record, "B1", "F-B1-001").DeepClone());
        AssertCodes(Validate(record), "D002");
    }

    [Fact]
    public void D002_ClassChanged()
    {
        var record = Record();
        Finding(record, "B1", "F-B1-014")["class"] = "spurious-refutation";
        AssertCodes(Validate(record), "D002");
    }

    [Fact]
    public void D003_MissingRow()
    {
        var record = Record();
        var rows = record["baselines"]!["N1"]!["rows"]!.AsArray();
        rows.Remove(rows.Single(r => r!["rowId"]!.GetValue<string>() == "NUM-ARITH-ADD"));
        AssertCodes(Validate(record), "D003");
    }

    [Fact]
    public void D004_CleanRowNotValidated()
    {
        var record = Record();
        Row(record, "B1", "NUM-ARITH-ADD")["disposition"] = "FIX-IN-0.24";
        AssertCodes(Validate(record), "D004");
    }

    [Fact]
    public void D004_FindingRowValidated()
    {
        var record = Record();
        Row(record, "N1", "CACHE-LITERAL-WIDTH")["disposition"] = "VALIDATED";
        // The row no longer matches its findings' disposition either.
        AssertCodes(Validate(record), "D004", "D012");
    }

    [Fact]
    public void D004_ExcludedRowValidated()
    {
        var record = Record();
        Row(record, "B1", "XCL-KINDUCTION")["disposition"] = "VALIDATED";
        AssertCodes(Validate(record), "D004");
    }

    [Fact]
    public void D005_FindingValidated()
    {
        var record = Record();
        Finding(record, "B1", "F-B1-033")["disposition"] = "VALIDATED";
        AssertCodes(Validate(record), "D005");
    }

    [Fact]
    public void D006_FalseProofNotInvestigated()
    {
        var record = Record();
        Finding(record, "N1", "F-N1-014")["disposition"] = "NOT-INVESTIGATED";
        // NOT-INVESTIGATED is also never valid for a finding (D005).
        AssertCodes(Validate(record), "D005", "D006");
    }

    [Fact]
    public void D007_UnknownRepair()
    {
        var record = Record();
        Finding(record, "B1", "F-B1-031")["repair"] = "R-NOPE";
        AssertCodes(Validate(record), "D007");
    }

    [Fact]
    public void D007_RepairDoesNotCoverRow()
    {
        var record = Record();
        Finding(record, "B1", "F-B1-031")["repair"] = "R-IMPL";
        AssertCodes(Validate(record), "D007");
    }

    [Fact]
    public void D008_OpenRepairWithoutPr()
    {
        var record = Record();
        Repair(record, "R-OBL")["pr"] = null;
        AssertCodes(Validate(record), "D008");
    }

    [Fact]
    public void D008_MergedRepairWithoutFullSha()
    {
        var record = Record();
        var repair = Repair(record, "R-CACHE");
        repair["status"] = "merged";
        repair["mergeCommit"] = "abc1234";
        AssertCodes(Validate(record), "D008");
    }

    [Fact]
    public void D008_RepairNotBlockingCandidateFreeze()
    {
        var record = Record();
        Repair(record, "R-TEXT")["blocks"] = new JsonArray();
        AssertCodes(Validate(record), "D008");
    }

    [Fact]
    public void D009_OverCapacityRepairs()
    {
        var record = Record();
        var extra = Repair(record, "R-QNT").DeepClone();
        extra["id"] = "R-EXTRA";
        record["repairs"]!.AsArray().Add(extra);
        AssertCodes(Validate(record), "D009");
    }

    [Fact]
    public void D009_OversizedRepair()
    {
        var record = Record();
        Repair(record, "R-OBL")["nonTestChangedLines"] = 601;
        AssertCodes(Validate(record), "D009");
    }

    [Fact]
    public void D010_ClosedButNotClosing()
    {
        var record = Record();
        record["closure"]!["status"] = "CLOSED";
        AssertCodes(Validate(record), "D010");
    }

    [Fact]
    public void D011_MissingReason()
    {
        var record = Record();
        Finding(record, "N1", "F-N1-009")["reason"] = "";
        AssertCodes(Validate(record), "D011");
    }

    [Fact]
    public void D012_RowLessConservativeThanItsFindings()
    {
        var record = Record();
        // OBL-MUTATION-KILL has demoted findings, so the row must be DEMOTE-IN-0.24.
        Row(record, "B1", "OBL-MUTATION-KILL")["disposition"] = "FIX-IN-0.24";
        AssertCodes(Validate(record), "D012");
    }

    [Fact]
    public void D013_PublishedPackageWithoutObligation()
    {
        var record = Record();
        record["baselines"]!["N1"]!["artifact"]!.AsObject().Remove("publishedArtifactObligation");
        AssertCodes(Validate(record), "D013");
    }

    [Fact]
    public void D013_PublishedPackageClaimedMutable()
    {
        var record = Record();
        record["baselines"]!["N1"]!["artifact"]!["immutable"] = false;
        AssertCodes(Validate(record), "D013");
    }

    [Fact]
    public void D014_DiscoveryClaimedRegistered()
    {
        var record = Record();
        record["discoveryFindings"]![0]!["registered"] = true;
        AssertCodes(Validate(record), "D014");
    }

    [Fact]
    public void D014_DiscoveryNotListedByItsRepair()
    {
        var record = Record();
        Repair(record, "R-TEXT")["discoveries"] = new JsonArray();
        AssertCodes(Validate(record), "D014");
    }
}
