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

    /// <summary>Every check passes; the message names every PR the merged record uses.</summary>
    private static readonly DispositionValidator.ClosureEvidence AcceptAll = new(
        _ => true, (_, _) => true, _ => "Merge pull request #1494 #1495 #1496 #1497 #1498 #9999");

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

    private static IReadOnlyList<DispositionValidator.Violation> Validate(
        JsonNode record, bool closing = false, DispositionValidator.ClosureEvidence? evidence = null)
        => DispositionValidator.Validate(
            record,
            Load("docs/plans/evidence/evidence-contract-1407/contract.json"),
            Load("docs/plans/evidence/s1-1311/run2/findings.json"),
            Load("docs/plans/evidence/s1-1311/findings.json"),
            Load("docs/plans/evidence/s1-1311/combined-row-status.json"),
            FindString(Load("docs/plans/evidence/s1-1311/run2/pins.json"), "registrationCommit")!,
            closing,
            evidence ?? RepositoryEvidence,
            ValidatedProofCounts());

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
        foreach (var repair in record["repairs"]!.AsArray())
        {
            repair!["status"] = "merged";
            repair["mergeCommit"] = new string('a', 40);
            repair["pr"] ??= 9999;
            repair["branch"] ??= "milestone-0.24/s2-1413-fix-example";
            if (repair["regressionWitness"]!.AsArray().Count == 0)
                repair["regressionWitness"]!.AsArray().Add("tests/example.cs");
        }
        // Discoveries that are not required (the review-found residuals) are dropped, so the
        // positive control describes a record that has resolved them.
        var discoveries = record["discoveryFindings"]!.AsArray();
        foreach (var d in discoveries.Where(d => d!["issue"]!.GetValue<int>() != 1493).ToList())
            discoveries.Remove(d);
        record["closure"]!["status"] = "CLOSED";
        record["closure"]!["result"] = "SUCCESS";
        return record;
    }

    // ------------------------------------------------------------------ positive

    [Fact]
    public void CommittedRecord_ValidatesOpen()
        => AssertCodes(Validate(Record()));

    [Fact]
    public void CommittedRecord_IsNotYetClosable()
    {
        // Unmerged repairs and the undecided R-NUM keep S2 open; there is no closure result yet.
        var violations = Validate(Record(), closing: true);
        AssertCodes(violations, "D010", "D016");
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
        => AssertCodes(Validate(MergedRecord(), closing: true, AcceptAll));

    [Fact]
    public void FailedRecord_ClosesOnlyAsMilestoneFailed()
    {
        var record = MergedRecord();
        Finding(record, "B1", "F-B1-001")["disposition"] = "MILESTONE-FAILED";
        Row(record, "B1", "NUM-NARROW-ARITH")["disposition"] = "MILESTONE-FAILED";
        AssertCodes(Validate(record, closing: true, AcceptAll), "D016");

        record["closure"]!["result"] = "MILESTONE-FAILED";
        AssertCodes(Validate(record, closing: true, AcceptAll));
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
        AssertCodes(Validate(record), "D010");
    }

    [Fact]
    public void D010_InventedMergeEvidence_DoesNotClose()
    {
        // A syntactically valid but absent merge commit and a nonexistent witness file.
        var violations = Validate(MergedRecord(), closing: true, RepositoryEvidence);
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("merge commit is not on main"));
    }

    [Fact]
    public void D010_CommitReachableOnlyFromABranch_DoesNotClose()
    {
        var violations = Validate(MergedRecord(), closing: true,
            AcceptAll with { CommitIsOnMain = _ => false });
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: merge commit is not on main"));
    }

    [Fact]
    public void D010_WitnessAbsentFromTheMergeCommit_DoesNotClose()
    {
        var violations = Validate(MergedRecord(), closing: true,
            AcceptAll with { FileExistsAtCommit = (_, path) => !path.Contains("S2ObligationStateTests") });
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: a regression witness is missing from the merge commit"));
    }

    [Fact]
    public void D010_MergeCommitOfAnotherPr_DoesNotClose()
    {
        // An unrelated existing commit (or an invented PR number) does not name the repair's PR.
        var violations = Validate(MergedRecord(), closing: true,
            AcceptAll with { CommitMessage = _ => "Merge pull request #1483 from juanmicrosoft/unrelated" });
        AssertCodes(violations, "D010");
        Assert.Contains(violations, v => v.Message.Contains("R-OBL: the merge commit does not name PR #1496"));
    }

    [SkippableFact]
    public void D010_RealHeadOnlyCommit_IsNotOnMain()
    {
        // The real callback: HEAD of an unmerged branch is not reachable from origin/main.
        var head = Git("rev-parse HEAD").Output.Trim();
        var onMain = Git($"merge-base --is-ancestor {head} origin/main").ExitCode == 0;
        Skip.If(onMain, "HEAD is already on origin/main");
        Assert.False(RepositoryEvidence.CommitIsOnMain(head));
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
        AssertCodes(Validate(record), "D014");
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
        var record = Record();
        record["contractVersion"] = "1.0.0";
        AssertCodes(Validate(record), "D017");
    }
}
