using System.Diagnostics;
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

    private static string? FindString(JsonNode? node, string key) => node switch
    {
        JsonObject o => o.TryGetPropertyValue(key, out var value) && value is JsonValue s && s.TryGetValue<string>(out var text)
            ? text
            : o.Select(p => FindString(p.Value, key)).FirstOrDefault(x => x != null),
        JsonArray a => a.Select(x => FindString(x, key)).FirstOrDefault(x => x != null),
        _ => null
    };

    private static (int ExitCode, string Output) Git(string arguments)
    {
        var start = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    /// <summary>Real closure evidence: the commit is reachable from origin/main (a missing ref
    /// fails closed), the witness exists in that commit, and the commit message is read from git.</summary>
    private static readonly DispositionValidator.ClosureEvidence RepositoryEvidence = new(
        sha => Git($"merge-base --is-ancestor {sha} origin/main").ExitCode == 0,
        (sha, path) => Git($"cat-file -e {sha}:{path}").ExitCode == 0,
        sha => Git($"log -1 --format=%B {sha}") is (0, var message) ? message : null);

    /// <summary>Every check passes; each merge commit is the GitHub merge of its own repair PR.</summary>
    private static DispositionValidator.ClosureEvidence AcceptAll(JsonNode record) => new(
        _ => true, (_, _) => true,
        sha => record["repairs"]!.AsArray()
            .Where(r => r!["mergeCommit"]?.GetValue<string>() == sha)
            .Select(r => $"Merge pull request #{r!["pr"]} from juanmicrosoft/{r["branch"]}\n\nbody")
            .FirstOrDefault());

    /// <summary>Discoveries the record must keep (by id), pinned outside the record.</summary>
    private static readonly string[] PinnedDiscoveries = ["D-1493", "D-NUM-WHILE-BOUND", "D-OBL-PROOF-GETTER", "D-OBL-THROWING-PREDECESSOR"];

    /// <summary>The opened repair PRs, pinned outside the record (an accepted R-NUM PR is added here).</summary>
    private static readonly Dictionary<string, int> PinnedRepairPrs = new()
    {
        ["R-CACHE"] = 1494, ["R-IMPL"] = 1495, ["R-OBL"] = 1496, ["R-TEXT"] = 1497, ["R-QNT"] = 1498,
        ["R-NUM"] = 1502, ["R-OBL-RESIDUALS"] = 1503,
    };

    private static Dictionary<(string Baseline, string Row), int> ValidatedProofCounts()
    {
        var counts = new Dictionary<(string Baseline, string Row), int>();
        foreach (var baseline in new[] { "B1", "N1" })
        {
            var path = Path.Combine(RepoRoot(), $"bench/correctness/false-established/v024/run2/{baseline}/case-results.jsonl");
            foreach (var line in File.ReadLines(path).Where(l => l.Trim().Length > 0))
            {
                var row = JsonNode.Parse(line)!;
                var key = (baseline, row["rowId"]!.GetValue<string>());
                counts[key] = counts.GetValueOrDefault(key) + (row["class"]!.GetValue<string>() == "validated-proof" ? 1 : 0);
            }
        }
        return counts;
    }

    /// <summary>
    /// At closure, a record stating a later contract version than the committed contract.json is
    /// validated against that contract as amended (the version is the only field the closure rules
    /// read); open-state validation always uses the committed contract. Without an explicit
    /// <paramref name="closing"/>, a record is validated in its own state (S2 is closed, so the
    /// negative controls validate closed records), with evidence that accepts the record's own
    /// merge commits; <see cref="CommittedRecord_ValidatesClosed"/> checks them against real git.
    /// </summary>
    private static IReadOnlyList<DispositionValidator.Violation> Validate(
        JsonNode record, bool? closingOverride = null, DispositionValidator.ClosureEvidence? evidence = null)
    {
        var closing = closingOverride ?? record["closure"]?["status"]?.GetValue<string>() == "CLOSED";
        evidence ??= closingOverride is null && closing ? AcceptAll(record) : RepositoryEvidence;
        var contract = Load("docs/plans/evidence/evidence-contract-1407/contract.json");
        if (closing && record["contractVersion"]?.GetValue<string>() is { } stated && stated != contract["contractVersion"]!.GetValue<string>())
            contract["contractVersion"] = stated;
        return DispositionValidator.Validate(
            record,
            contract,
            Load("docs/plans/evidence/s1-1311/run2/findings.json"),
            Load("docs/plans/evidence/s1-1311/findings.json"),
            Load("docs/plans/evidence/s1-1311/combined-row-status.json"),
            FindString(Load("docs/plans/evidence/s1-1311/run2/pins.json"), "registrationCommit")!,
            closing,
            evidence ?? RepositoryEvidence,
            ValidatedProofCounts(),
            PinnedDiscoveries,
            PinnedRepairPrs);
    }

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

    /// <summary>A variant where every repair merged with full closure evidence fields.</summary>
    private static JsonNode MergedRecord()
    {
        var record = Record();
        var index = 0;
        foreach (var repair in record["repairs"]!.AsArray())
        {
            repair!["status"] = "merged";
            repair["mergeCommit"] = (index++).ToString("x").PadLeft(40, 'a');
            repair["pr"] ??= 9999;
            repair["branch"] ??= "milestone-0.24/s2-1413-fix-example";

            if (repair["regressionWitness"]!.AsArray().Count == 0)
                repair["regressionWitness"]!.AsArray().Add("tests/example.cs");
        }
        // Closing assumes amendments 1.3.0 (capacity allowance, R-OBL overrun), 1.3.1
        // (R-OBL-RESIDUALS overrun) and 1.3.2 (R-NUM overrun) have merged.
        record["contractVersion"] = "1.3.2";
        record["closure"]!["status"] = "CLOSED";
        record["closure"]!["result"] = "SUCCESS";
        return record;
    }

    // ------------------------------------------------------------------ positive

    [Fact]
    public void CommittedRecord_ValidatesClosed()
    {
        // S2 is closed: all seven repairs are merged, and their real merge commits pass the real git
        // checks (reachable from origin/main, GitHub's merge of the PR from its S2 branch, regression
        // witnesses present). The check needs the fetched main ref; without it this fails, never skips.
        Assert.True(Git("rev-parse --verify --quiet origin/main").ExitCode == 0,
            "closure validation needs a fetched origin/main (full-history checkout)");
        var record = Record();
        Assert.Equal("CLOSED", record["closure"]!["status"]!.GetValue<string>());
        Assert.Equal("SUCCESS", record["closure"]!["result"]!.GetValue<string>());
        AssertCodes(Validate(record, closingOverride: true));
    }

    [Fact]
    public void CommittedRecord_IsNotAnOpenRecord()
    {
        // A closed record cannot pass as open: open validation requires status OPEN with no result.
        var violations = Validate(Record(), closingOverride: false);
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("must say closure.status OPEN"));
    }

    [Fact]
    public void D010_UnmergedRepair_DoesNotClose()
    {
        var record = Record();
        Repair(record, "R-NUM")["status"] = "open";
        Repair(record, "R-NUM")["mergeCommit"] = null;
        var violations = Validate(record, closingOverride: true, AcceptAll(record));
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-NUM: not merged at closure"));
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
        var record = MergedRecord();
        AssertCodes(Validate(record, closingOverride: true, AcceptAll(record)));
    }

    [Fact]
    public void D010_AmendmentNotMerged_DoesNotClose()
    {
        // Against a contract without amendment 1.3.0, neither the seventh repair nor the R-OBL
        // review overrun can close.
        var record = MergedRecord();
        record["contractVersion"] = "1.2.0";
        var violations = Validate(record, closingOverride: true, AcceptAll(record));
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("capacity amendment 1.3.0 is not in the contract"));
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: review-round overrun without a merged amendment"));
    }

    [Fact]
    public void D009_AmendmentAllowanceCoversOnlyItsDiscoveries()
    {
        var record = Record();
        Finding(record, "B1", "F-B1-001")["repair"] = "R-OBL-RESIDUALS";
        var violations = Validate(record);
        Assert.Contains(violations, v => v.Code == "D009" && v.Message.Contains("covers discoveries only"));
    }

    [Fact]
    public void D009_AmendmentAllowanceMustMatchTheRepairsDiscoveries()
    {
        var record = Record();
        record["capacity"]!["amendmentAllowance"]!["discoveries"] = new JsonArray("D-OBL-PROOF-GETTER");
        var violations = Validate(record);
        AssertCodes(violations, "D009");
        Assert.Contains(violations, v => v.Message.Contains("exceed the 6-repair ceiling"));
    }

    [Fact]
    public void D014_PinnedDiscoveryDeleted()
    {
        var record = Record();
        var discoveries = record["discoveryFindings"]!.AsArray();
        discoveries.Remove(discoveries.Single(d => d!["id"]!.GetValue<string>() == "D-OBL-PROOF-GETTER"));
        AssertCodes(Validate(record), "D014");
    }

    [Fact]
    public void D010_ReviewOverrunWithoutAmendment_DoesNotClose()
    {
        var record = MergedRecord();
        Repair(record, "R-OBL").Remove("overrunAmendment");
        var violations = Validate(record, closingOverride: true, AcceptAll(record));
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: review-round overrun without a merged amendment"));
    }

    [Fact]
    public void FailedRecord_ClosesOnlyAsMilestoneFailed()
    {
        var record = MergedRecord();
        Finding(record, "B1", "F-B1-001")["disposition"] = "MILESTONE-FAILED";
        Row(record, "B1", "NUM-NARROW-ARITH")["disposition"] = "MILESTONE-FAILED";
        AssertCodes(Validate(record, closingOverride: true, AcceptAll(record)), "D016");

        record["closure"]!["result"] = "MILESTONE-FAILED";
        AssertCodes(Validate(record, closingOverride: true, AcceptAll(record)));
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
        // The rejected finding no longer counts toward its row's finding list (D015).
        AssertCodes(Validate(record), "D001", "D015");
    }

    [Fact]
    public void D002_MissingFinding()
    {
        var record = Record();
        var findings = record["baselines"]!["N1"]!["findings"]!.AsArray();
        findings.Remove(findings.Single(f => f!["findingId"]!.GetValue<string>() == "F-N1-030"));
        // IMPL-DIVISION-TOTALIZED then has no dispositioned finding (D012).
        AssertCodes(Validate(record), "D002", "D012");
    }

    [Fact]
    public void D002_DuplicateFinding()
    {
        var record = Record();
        record["baselines"]!["B1"]!["findings"]!.AsArray().Add(Finding(record, "B1", "F-B1-001").DeepClone());
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
    public void D002_TokenChanged()
    {
        var record = Record();
        Finding(record, "N1", "F-N1-014")["token"] = "Failed";
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
    public void D003_ReleaseCriticalMarkChanged()
    {
        var record = Record();
        Row(record, "B1", "NUM-ARITH-ADD")["releaseCritical"] = false;
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
        // The row no longer matches its findings' disposition either (D012).
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
        Finding(record, "N1", "F-N1-031")["disposition"] = "NOT-INVESTIGATED";
        // NOT-INVESTIGATED is also never valid for a finding (D005).
        AssertCodes(Validate(record), "D005", "D006");
    }

    [Fact]
    public void D007_UnknownRepair()
    {
        var record = Record();
        Finding(record, "B1", "F-B1-031")["repair"] = "R-NOPE";
        // The row's repairs list no longer matches its findings (D015).
        AssertCodes(Validate(record), "D007", "D015");
    }

    [Fact]
    public void D007_RepairDoesNotCoverRow()
    {
        var record = Record();
        Finding(record, "B1", "F-B1-031")["repair"] = "R-IMPL";
        AssertCodes(Validate(record), "D007", "D015");
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
    public void D008_RepairKindLessConservativeThanItsFindings()
    {
        var record = Record();
        Repair(record, "R-OBL")["kind"] = "FIX-IN-0.24";
        AssertCodes(Validate(record), "D008");
    }

    [Fact]
    public void D009_OverCapacityRepairs()
    {
        var record = Record();
        var extra = Repair(record, "R-QNT").DeepClone();
        extra["id"] = "R-EXTRA";
        record["repairs"]!.AsArray().Add(extra);
        // The extra repair is referenced by nothing (D015).
        AssertCodes(Validate(record), "D009", "D015");
    }

    [Fact]
    public void D009_OversizedRepair()
    {
        var record = Record();
        Repair(record, "R-OBL")["nonTestChangedLines"] = 601;
        AssertCodes(Validate(record), "D009");
    }

    [Fact]
    public void D009_CapacityUsedMisstated()
    {
        var record = Record();
        record["capacity"]!["used"] = 5;
        AssertCodes(Validate(record), "D009");
    }

    [Fact]
    public void D010_ClosedButNotClosing()
    {
        var record = Record();
        record["closure"]!["status"] = "CLOSED";
        AssertCodes(Validate(record, closingOverride: false), "D010");
    }

    [Fact]
    public void D010_InventedMergeEvidence_DoesNotClose()
    {
        // A syntactically valid but absent merge commit and a nonexistent witness file.
        var violations = Validate(MergedRecord(), closingOverride: true, RepositoryEvidence);
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("merge commit is not on main"));
    }

    [Fact]
    public void D010_CommitReachableOnlyFromABranch_DoesNotClose()
    {
        var record = MergedRecord();
        var violations = Validate(record, closingOverride: true,
            AcceptAll(record) with { CommitIsOnMain = _ => false });
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: merge commit is not on main"));
    }

    [Fact]
    public void D010_WitnessAbsentFromTheMergeCommit_DoesNotClose()
    {
        var record = MergedRecord();
        var violations = Validate(record, closingOverride: true,
            AcceptAll(record) with { FileExistsAtCommit = (_, path) => !path.Contains("S2ObligationStateTests") });
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: a regression witness is missing from the merge commit"));
    }

    [Fact]
    public void D010_MergeCommitOfAnotherPr_DoesNotClose()
    {
        // An unrelated main merge (round-3 witness: PR #1483, which also mentions #1426) is not the
        // merge of the repair's PR from its S2 branch.
        var record = MergedRecord();
        var violations = Validate(record, closingOverride: true, AcceptAll(record) with
        {
            CommitMessage = _ => "Merge pull request #1483 from juanmicrosoft/milestone-0.25/r0-1426-scope-baseline\n\n#1426",
        });
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: the merge commit is not the merge of PR #1496"));
    }

    [Fact]
    public void D008_RepairPrDiffersFromThePinnedPr()
    {
        // Review witness: every repair naming the disposition-record PR instead of its own.
        var record = Record();
        Repair(record, "R-OBL")["pr"] = 1499;
        var violations = Validate(record);
        AssertCodes(violations, "D008");
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: PR #1499 differs from the pinned repair PR #1496"));
    }

    [Fact]
    public void D008_RepairOnANonS2Branch()
    {
        var record = Record();
        Repair(record, "R-OBL")["branch"] = "milestone-0.25/r0-1426-scope-baseline";
        AssertCodes(Validate(record), "D008");
    }

    [Fact]
    public void RealCallbacks_FailClosedOnAnUnknownCommit()
    {
        var absent = new string('0', 40);
        Assert.False(RepositoryEvidence.CommitIsOnMain(absent));
        Assert.False(RepositoryEvidence.FileExistsAtCommit(absent, "global.json"));
        Assert.Null(RepositoryEvidence.CommitMessage(absent));
    }

    [Fact]
    public void D018_ValidatedProofCases_MustMatchTheS1CaseResults()
    {
        var record = Record();
        Row(record, "B1", "NUM-MIXED-U64-SIGNED")["validatedProofCases"] = 999;
        Row(record, "N1", "NUM-MIXED-U64-SIGNED").Remove("validatedProofCases");
        var violations = Validate(record);
        AssertCodes(violations, "D018");
        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void ReviewRecordsExistForEveryOpenedRepair()
    {
        foreach (var repair in Record()["repairs"]!.AsArray().Where(r => r!["pr"] is JsonValue))
        {
            var directory = Path.Combine(RepoRoot(), repair!["reviews"]!.GetValue<string>());
            Assert.True(Directory.Exists(directory), directory);
            Assert.NotEmpty(Directory.GetFiles(directory, "*.md"));
        }
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
    public void D014_RequiredDiscoveryRemoved()
    {
        var record = Record();
        record["discoveryFindings"] = new JsonArray();
        // R-OBL-RESIDUALS then carries no discovery and is referenced by nothing (D015).
        AssertCodes(Validate(record), "D014", "D015");
    }

    [Fact]
    public void D014_DiscoveryNotListedByItsRepair()
    {
        var record = Record();
        Repair(record, "R-TEXT")["discoveries"] = new JsonArray();
        AssertCodes(Validate(record), "D014");
    }

    [Fact]
    public void D015_RowFindingListTampered()
    {
        var record = Record();
        Row(record, "B1", "OBL-SELFREF")["findings"]!.AsArray().RemoveAt(0);
        AssertCodes(Validate(record), "D015");
    }

    [Fact]
    public void D017_ContractVersionChanged()
    {
        // An open-state rule (at closure the stated version selects the amended contract).
        var record = Record();
        record["closure"]!["status"] = "OPEN";
        record["closure"]!["result"] = null;
        record["contractVersion"] = "1.0.0";
        AssertCodes(Validate(record), "D017");
    }
}
