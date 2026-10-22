using System.Text;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.EvidenceContract;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1428 (0.25 T1): the committed readiness register passes; each negative control changes one fact
/// and asserts that only its expected code fires.
/// </summary>
public class T1RegisterTests
{
    [Fact]
    public void CommittedRegisterPasses()
    {
        var violations = T1RegisterValidator.Validate(Register(), Scope(), ReadRepo);
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    /// <summary>The 1.0.0 approval seal. A later approval is a register amendment carrying its own seal, not an edit here.</summary>
    private const string BaselineApprovalSeal = "3069326880844b179c73a50cb1ac13448f9caa3a5e8bdb418ccb18635b49da3c";

    [Fact]
    public void BaselineApprovalsMatchThePinnedSeal()
    {
        var register = Register();
        var baseline = register["amendments"]!.AsArray().Count == 0 ? T1RegisterValidator.ApprovalSeal(register) : register["approvalSeal"]!.GetValue<string>();
        Assert.True(baseline == BaselineApprovalSeal, $"approval seal {baseline}");
        Assert.Equal(BaselineApprovalSeal, register["approvalSeal"]!.GetValue<string>());
    }

    /// <summary>Each approved amendment's seal, in order. 1.0.1: F1 PR 2 (#943, PR #1571) re-ran F1's results.json.
    /// 1.0.2: consumes R0 scope 1.4.0 (the 2026-10-19 freeze decisions); no approved hash changes.
    /// 1.0.3: consumes R0 scope 1.5.0; F2-INTERP-09's counts re-registered to the 0.25 candidate (D-F2-INTERP-09-RECANDIDATE).
    /// 1.0.4: consumes R0 scope 1.6.0 and mirrors the post-release READY write-back (D-READY-EVIDENCE-DIRS); no approved hash changes.</summary>
    private static readonly (string Version, string Seal)[] AmendmentSeals =
    [
        ("1.0.1", "12a33773e2424210a04b7750c2bc3fdeabb74c0281bf6d2dd65e8485a66b27b8"),
        ("1.0.2", "12a33773e2424210a04b7750c2bc3fdeabb74c0281bf6d2dd65e8485a66b27b8"),
        ("1.0.3", "65330c86baba0be8ebc1fb9bff97c6e6c1e77a2abd24502314faf723d5393f15"),
        ("1.0.4", "65330c86baba0be8ebc1fb9bff97c6e6c1e77a2abd24502314faf723d5393f15"),
    ];

    [Fact]
    public void AmendmentApprovalsMatchThePinnedSeals()
    {
        var register = Register();
        var amendments = register["amendments"]!.AsArray();
        Assert.Equal(AmendmentSeals, amendments.Select(a => (a!["version"]!.GetValue<string>(), a["approvalSeal"]!.GetValue<string>())).ToArray());
        Assert.Equal(AmendmentSeals[^1].Seal, T1RegisterValidator.ApprovalSeal(register));
    }

    /// <summary>
    /// D-READY-EVIDENCE-DIRS (R0 scope 1.6.0, T1 register 1.0.4): READY is written back only after A1. Before the write-back
    /// this test asserted that no family was READY. Now every family's scope readiness and register mirror must cite the
    /// READY record that #1430's ACCEPTED terminal record cites for that family, at that record's hash, and every S007 field
    /// in scope.json must equal the record's own value, so the write-back adds nothing A1 did not adjudicate.
    /// </summary>
    [Fact]
    public void ReadinessIsWrittenBackFromTheA1ReadyRecords()
    {
        var terminal = JsonNode.Parse(ReadRepo(TerminalRecord)!)!;
        Assert.Equal(1430, terminal["issue"]!.GetValue<int>());
        Assert.Equal("ACCEPTED", terminal["outcome"]!.GetValue<string>());
        var register = Register();
        foreach (var family in Scope()["families"]!.AsArray())
        {
            var id = family!["id"]!.GetValue<string>();
            var readiness = family["readiness"]!;
            var row = terminal["adjudications"]!.AsArray().Single(r => r!["subject"]!.GetValue<string>() == $"family:{id}")!;
            Assert.Equal("READY", row["outcome"]!.GetValue<string>());
            Assert.Equal("READY", readiness["status"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(row["readyRecord"], readiness["readyRecord"]), $"{id}: scope cites a READY record A1 did not");
            Assert.True(JsonNode.DeepEquals(row["readyRecord"], Family(register, id)["readyRecord"]), $"{id}: the register cites a READY record A1 did not");
            Assert.Equal("READY", Family(register, id)["readiness"]!.GetValue<string>());
            var path = readiness["readyRecord"]!["path"]!.GetValue<string>();
            var bytes = ReadRepo(path);
            Assert.True(bytes != null, $"{id}: {path} is missing");
            Assert.Equal(readiness["readyRecord"]!["sha256"]!.GetValue<string>(), InteropScopeValidator.Sha256(bytes!));
            var record = JsonNode.Parse(bytes!)!;
            foreach (var field in new[] { "scopeVersion", "candidate", "cases", "tests", "websiteExamples", "reviews", "approver", "independence", "blockersResolved" })
                Assert.True(JsonNode.DeepEquals(record[field], readiness[field]), $"{id}: {field} differs from the A1 READY record");
            Assert.Equal(TerminalRecord, readiness["terminalRecord"]!["path"]!.GetValue<string>());
            Assert.Equal(InteropScopeValidator.Sha256(ReadRepo(TerminalRecord)!), readiness["terminalRecord"]!["sha256"]!.GetValue<string>());
        }
    }

    private const string TerminalRecord = "docs/plans/evidence/adjudication-1430/terminal-record.json";

    public static IEnumerable<object[]> Mutations => Controls.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Mutations))]
    public void NegativeControlFailsWithItsCode(string mutation)
    {
        var register = Register();
        var scope = Scope();
        var (code, apply) = Controls[mutation];
        var read = apply(register, scope) ?? ReadRepo;
        // Re-approve the mutated hashes so a T004/T006/T007 control isolates its own code from T009's seal check.
        // With amendments, the latest amendment carries the approval the validator checks.
        if (code != "T009")
            (register["amendments"]!.AsArray().LastOrDefault() ?? register)["approvalSeal"] = T1RegisterValidator.ApprovalSeal(register);
        var violations = T1RegisterValidator.Validate(register, scope, read);
        Assert.True(violations.Count > 0 && violations.All(x => x.Code == code), $"{mutation}: {EvidenceContractTests.Describe(violations)}");
    }

    private static readonly Dictionary<string, (string Code, Func<JsonNode, JsonNode, Func<string, byte[]?>?> Apply)> Controls = new()
    {
        ["stale scope version"] = ("T001", (r, _) => { r["consumes"]!["scopeVersion"] = "1.2.0"; return null; }),
        ["family dropped"] = ("T002", (r, _) => { r["families"]!.AsArray().RemoveAt(2); return null; }),
        // Since the 1.0.4 write-back both say READY; the scope side is reverted so the register alone claims it.
        ["READY claimed in the register only"] = ("T002", (_, s) => {
            Family(s, "F4")["readiness"] = new JsonObject { ["status"] = "IN_PROGRESS", ["branch"] = "milestone-0.25/f4-1139-iterator-accessors" }; return null; }),
        ["register not READY while the scope is"] = ("T002", (r, _) => { Family(r, "F4")["readiness"] = "IN_PROGRESS"; Family(r, "F4")["readyNeeds"] = new JsonArray("x"); return null; }),
        ["mapped blocker dropped"] = ("T002", (r, _) => { Family(r, "F5")["blockers"]!.AsArray().RemoveAt(2); return null; }),
        ["case removed"] = ("T003", (r, _) => { Family(r, "F2")["cases"]!.AsArray().RemoveAt(8); return null; }),
        ["case reclassified"] = ("T003", (r, _) => { Case(r, "F4", "F4-ITER-01")["expected"] = "native"; return null; }),
        ["unapproved requirement digest"] = ("T003", (r, _) => { Case(r, "F1", "F1-REFOUT-08")["requirementSha256"] = new string('0', 64); return null; }),
        ["case moved to another family"] = ("T003", (r, _) => {
            var c = Case(r, "F4", "F4-ITER-04"); Family(r, "F4")["cases"]!.AsArray().Remove(c); Family(r, "F5")["cases"]!.AsArray().Add(c); return null; }),
        ["evidence swapped for another family's file"] = ("T004", (r, _) => {
            Family(r, "F1")["evidence"]![0] = Family(r, "F6")["evidence"]![0]!.DeepClone(); return null; }),
        ["results rehashed without an amendment"] = ("T009", (r, _) => {
            var edited = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ReadRepo(SharedInteropControlsTests.ResultsPath)!) + " ");
            r["results"]!["sha256"] = InteropScopeValidator.Sha256(edited);
            return p => p == SharedInteropControlsTests.ResultsPath ? edited : ReadRepo(p); }),
        ["evidence rehashed"] = ("T004", (r, _) => { Family(r, "F3")["evidence"]![0]!["sha256"] = new string('1', 64); return null; }),
        ["evidence edited"] = ("T004", (_, _) => p => p.EndsWith("v0.25-f6-1144/surface-matrix.json", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes("{}") : ReadRepo(p)),
        // Codex R2-4: the counts' source is bound to F2's approved rows.json, and the counts are sealed.
        ["corpus source redirected"] = ("T005", (r, _) => {
            r["corpusCounts"]!["source"] = "docs/plans/evidence/v0.25-t1-1428/register.json";
            return p => p == "docs/plans/evidence/v0.25-t1-1428/register.json"
                ? Encoding.UTF8.GetBytes(new JsonObject { ["corpusCounts"] = r["corpusCounts"]!["counts"]!.DeepClone() }.ToJsonString()) : ReadRepo(p); }),
        ["corpus count changed"] = ("T005", (r, _) => { r["corpusCounts"]!["counts"]!["opaqueBoundaries"]!["after"] = 129; return null; }),
        ["corpus before count dropped"] = ("T005", (r, _) => { r["corpusCounts"]!["counts"]!["conversionVisits"]!.AsObject().Remove("before"); return null; }),
        // Amendment 1.0.3: the re-registered counts are bound to the candidate's measurement and the per-PR deltas.
        ["re-registered after differs from the measurement"] = ("T005", (r, _) => {
            r["corpusCounts"]!["counts"]!["opaqueExpressions"]!["after"] = 7747; ReRegistration(r)["perPr"]![0]!["delta"]!["opaqueExpressions"] = 16; return null; }),
        ["per-PR deltas do not add up"] = ("T005", (r, _) => { ReRegistration(r)["perPr"]![0]!["delta"]!["conversionVisits"] = -8; return null; }),
        ["per-PR attribution dropped"] = ("T005", (r, _) => { ReRegistration(r)["perPr"] = new JsonArray(); return null; }),
        ["measurement rehashed"] = ("T005", (r, _) => { ReRegistration(r)["measurement"]!["sha256"] = new string('3', 64); return null; }),
        ["measurement outside F2's ready records"] = ("T005", (r, _) => { ReRegistration(r)["measurement"]!["path"] = "docs/plans/evidence/v0.25-f2-906/rows.json"; return null; }),
        ["previous after rewritten"] = ("T005", (r, _) => { ReRegistration(r)["previousAfter"]!["opaqueBoundaries"] = 148; return null; }),
        ["re-registration without its amendment"] = ("T005", (r, _) => { ReRegistration(r)["amendment"] = "9.9.9"; return null; }),
        // Codex Step A round 3: a non-integer count never matches, even when the measurement says the same thing.
        ["re-registered count not an integer"] = ("T005", (r, _) => {
            var path = ReRegistration(r)["measurement"]!["path"]!.GetValue<string>();
            var file = JsonNode.Parse(ReadRepo(path)!)!;
            file["measurements"]![0]!["candidate"]!["incompleteDiagnostics"] = "x";
            var edited = Encoding.UTF8.GetBytes(file.ToJsonString());
            ReRegistration(r)["measurement"]!["sha256"] = InteropScopeValidator.Sha256(edited);
            r["corpusCounts"]!["counts"]!["incompleteDiagnostics"]!["after"] = "x";
            return p => p == path ? edited : ReadRepo(p); }),
        ["interaction row without a control"] = ("T006", (r, _) => { Remove(r["controls"]!, "X-INT-07"); return null; }),
        ["interaction control weakened"] = ("T006", (r, _) => { Control(r, "X-INT-07")["expected"] = "native-or-preserved"; return null; }),
        ["mandatory control dropped"] = ("T006", (r, _) => { Remove(r["controls"]!, "T1-INTERP-HANDLER"); return null; }),
        ["control fixture edited"] = ("T006", (_, _) => p => p.EndsWith("T1-ARRAY-CONDITIONAL.cs.txt", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes("// edited") : ReadRepo(p)),
        ["unapproved results hash"] = ("T007", (r, _) => { r["results"]!["sha256"] = new string('2', 64); return null; }),
        ["control narrowed below its observations"] = ("T007", (r, _) => { Control(r, "T1-INTERP-STRING")["expected"] = "preserved"; return null; }),
        ["measured surface without defaults"] = ("T008", (r, _) => { Surface(r, "mcp-batch").AsObject().Remove("defaults"); return null; }),
        ["LSP surface not recorded"] = ("T008", (r, _) => { Remove(r["surfaces"]!, "lsp"); return null; }),
        ["unmeasured surface without reason"] = ("T008", (r, _) => { Surface(r, "msbuild")["reason"] = ""; return null; }),
        ["version without amendment"] = ("T009", (r, _) => { r["registerVersion"] = "1.1.0"; return null; }),
        ["independence overstated"] = ("T009", (r, _) => { r["independence"] = "independent"; return null; }),
    };

    private static JsonNode ReRegistration(JsonNode r) => r["corpusCounts"]!["reRegistration"]!;

    private static JsonNode Family(JsonNode r, string id) => r["families"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Case(JsonNode r, string family, string id) => Family(r, family)["cases"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Control(JsonNode r, string id) => r["controls"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Surface(JsonNode r, string id) => r["surfaces"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;

    private static void Remove(JsonNode list, string id) =>
        list.AsArray().Remove(list.AsArray().Single(c => c!["id"]!.GetValue<string>() == id));

    private static JsonNode Register() => JsonNode.Parse(File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(), T1RegisterValidator.RegisterPath)))!;
    private static JsonNode Scope() => JsonNode.Parse(File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(), T1RegisterValidator.ScopePath)))!;

    private static byte[]? ReadRepo(string path)
    {
        var full = Path.Combine(EvidenceContractTests.RepoRoot(), path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }
}
