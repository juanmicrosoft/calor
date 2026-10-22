using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.EvidenceContract;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1426 (0.25 R0): positive controls pass; each negative control changes one fact of the committed
/// packet and asserts that only its expected violation code fires.
/// </summary>
public class InteropScopeTests
{
    /// <summary>
    /// SHA-256 over the frozen 1.0.0 denominator and evidence seal. Later changes are amendments, so a
    /// packet that drops a case from both lists, or reseals regenerated evidence, fails only this pin.
    /// </summary>
    private const string FrozenSeal = "65307b91a3c57b54c6484219f67fc6bdb285b23858cbe15a7d7293b422ed7ff8";

    private const string Dir = InteropScopeValidator.PacketDir;
    private const string Results = Dir + "/baseline-results.json";

    public static IEnumerable<object[]> Positives => PositiveSetups.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Positives))]
    public void PositiveControlPasses(string name)
    {
        var scope = name == "committed packet" ? Scope() : Unready(Scope());
        var violations = Run(scope, PositiveSetups[name](scope));
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    private static readonly Dictionary<string, Func<JsonNode, Func<string, byte[]?>?>> PositiveSetups = new()
    {
        ["committed packet"] = _ => null,
        ["frozen, not met"] = s => { Freeze(s); s["gateStatus"] = "NOT-MET"; return null; },
        ["fully evidenced MET"] = s => { MetWithAmendment(s); return GateReader("CLOSED"); },
        ["well-formed READY record"] = s => { Ready(s); return null; },
        ["well-formed READY record for a family with mapped 0.24 blockers"] = s => { Ready(s, "F2"); return null; },
    };

    [Fact]
    public void FrozenDenominatorAndEvidenceMatchThePinnedSeal() => Assert.Equal(FrozenSeal, Seal(Scope()));

    [Fact]
    public void ResealedPacketWithADroppedCaseFailsOnlyThePin()
    {
        var scope = Scope();
        Remove(scope["cases"]!, "F5-ARRAY-02");
        Remove(scope["denominatorV1"]!, "F5-ARRAY-02");
        Assert.Empty(Run(scope, withoutFixture: "F5-ARRAY-02.cs.txt"));
        Assert.NotEqual(FrozenSeal, Seal(scope));
    }

    [Fact]
    public void CommittedPacketHasSixFamiliesAndIsNotMetWhileAConditionIsOpen()
    {
        var scope = Scope();
        Assert.Equal(6, scope["families"]!.AsArray().Count);
        Assert.True(scope["gateConditions"]!.AsArray().All(g => g!["state"]!.GetValue<string>() == "SATISFIED")
            || scope["gateStatus"]!.GetValue<string>() == "NOT-MET");
    }

    [Fact]
    public void AmendmentsThatRetainRemoveRegisterAndChangeCasesPass()
    {
        var scope = Unready(Scope());
        Remove(scope["cases"]!, "F5-ARRAY-02");
        var added = Case(scope, "F6-REPORT-10").DeepClone();
        added["id"] = "F6-REPORT-99";
        scope["cases"]!.AsArray().Add(added.DeepClone());
        var from = Case(scope, "F4-ITER-01")["expected"]!.GetValue<string>();
        var to = from == "rejected" ? "native" : "rejected";
        Case(scope, "F4-ITER-01")["expected"] = to;
        var a = Amendment(scope, removed: "F5-ARRAY-02");
        added["requirementSha256"] = Requirement(added);
        a["addedCases"] = new JsonArray(added);
        a["changes"] = new JsonArray(Change("F4-ITER-01", "expected", from, to));
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = Last(scope);
        var violations = Run(scope);
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    public static IEnumerable<object[]> Mutations => Controls.Keys.Where(k => k != "unregistered fixture file").Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Mutations))]
    public void NegativeControlFailsWithItsCode(string mutation)
    {
        var scope = Unready(Scope());
        var (code, apply) = Controls[mutation];
        var violations = Run(scope, apply(scope) ?? ReadRepo);
        Assert.True(violations.Count > 0 && violations.All(x => x.Code == code), $"{mutation}: {EvidenceContractTests.Describe(violations)}");
    }

    /// <summary>The written-back READY records (1.6.0) on the committed packet: a later amendment that changes one F4 case
    /// makes exactly F4's record stale (S007), and nothing else fires.</summary>
    [Fact]
    public void WrittenBackReadyGoesStaleOnALaterCaseChange()
    {
        var scope = Scope();
        Assert.All(scope["families"]!.AsArray(), f => Assert.Equal("READY", f!["readiness"]!["status"]!.GetValue<string>()));
        var from = Case(scope, "F4-ITER-01")["expected"]!.GetValue<string>();
        var to = from == "rejected" ? "native" : "rejected";
        Case(scope, "F4-ITER-01")["expected"] = to;
        var a = Amendment(scope, removed: null);
        a["changes"] = new JsonArray(Change("F4-ITER-01", "expected", from, to));
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = Last(scope);
        var violations = Run(scope);
        Assert.True(violations.Count > 0 && violations.All(x => x.Code == "S007" && x.Subject == "F4"), EvidenceContractTests.Describe(violations));
        Assert.Contains(violations, x => x.Message.Contains("stale", StringComparison.Ordinal));
    }

    [Fact]
    public void UnregisteredFixtureFileFails()
    {
        var violations = Run(Scope(), extraFixture: Dir + "/fixtures/F9-NEW-01.cs.txt");
        Assert.True(violations.Count == 1 && violations[0].Code == "S003", EvidenceContractTests.Describe(violations));
    }

    /// <summary>One-fact mutations of the committed packet: name -> (expected code, mutation; returns a file reader or null).</summary>
    private static readonly Dictionary<string, (string Code, Func<JsonNode, Func<string, byte[]?>?> Apply)> Controls = new()
    {
        ["family missing"] = ("S001", scope => { scope["families"]!.AsArray().Remove(Family(scope, "F3")); RemoveFamilyCases(scope, "F3"); return null; }),
        ["family tracks another issue"] = ("S001", scope => { Family(scope, "F2")["issue"] = 9999; return null; }),
        ["unknown family"] = ("S001", scope => {
            var extra = Family(scope, "F6").DeepClone(); extra["id"] = "F7"; scope["families"]!.AsArray().Add(extra);
            var c7 = Case(scope, "F6-REPORT-04").DeepClone(); c7["id"] = "F7-X"; c7["family"] = "F7";
            scope["cases"]!.AsArray().Add(c7); Register(scope, c7); return null; }),
        ["duplicate case id"] = ("S002", scope => { scope["cases"]!.AsArray().Add(Case(scope, "F5-ARRAY-03").DeepClone()); return null; }),
        // SetBoth also edits the frozen entry, so the control isolates S002 from S004.
        ["role outside vocabulary"] = ("S002", scope => { SetBoth(scope, "F5-ARRAY-03", "role", "nice-to-have"); return null; }),
        ["vocabulary extended"] = ("S002", scope => { scope["vocabularies"]!["role"]!.AsArray().Add("bonus"); return null; }),
        ["measured status without baseline"] = ("S002", scope => { SetBoth(scope, "F6-REPORT-04", "baselineStatus", "reproduces"); return null; }),
        ["fixture hash drift"] = ("S003", scope => { return p => p.EndsWith("F5-ARRAY-03.cs.txt") ? Encoding.UTF8.GetBytes("// edited") : ReadRepo(p); }),
        ["unregistered fixture file"] = ("S003", scope => null),
        ["case removed silently"] = ("S004", scope => { Remove(scope["cases"]!, "F2-INTERP-03"); return null; }),
        ["case reclassified silently"] = ("S004", scope => { Case(scope, "F4-ITER-01")["expected"] = "rejected"; return null; }),
        ["baseline status relabeled silently"] = ("S004", scope => { Case(scope, "F1-REFOUT-08")["baselineStatus"] = "control-passes"; return null; }),
        ["case added silently"] = ("S004", scope => { var n = Case(scope, "F6-REPORT-10").DeepClone(); n["id"] = "F6-REPORT-99"; scope["cases"]!.AsArray().Add(n); return null; }),
        ["amendment change from a wrong value"] = ("S004", scope => {
            Case(scope, "F4-ITER-01")["expected"] = "rejected";
            var w = Amendment(scope, removed: null); w["changes"] = new JsonArray(Change("F4-ITER-01", "expected", "native", "rejected"));
            scope["amendments"]!.AsArray().Add(w); scope["scopeVersion"] = Last(scope); return null; }),
        ["amendment without PR"] = ("S005", scope => { var a = Amendment(scope, removed: null); a["pr"] = 0; scope["amendments"]!.AsArray().Add(a); scope["scopeVersion"] = Last(scope); return null; }),
        ["scope version ahead of amendments"] = ("S005", scope => { scope["scopeVersion"] = Bump(Last(scope)); return null; }),
        ["unknown readiness status"] = ("S006", scope => { Family(scope, "F2")["readiness"]!["status"] = "DONE"; return null; }),
        ["readiness vocabulary extended"] = ("S006", scope => { scope["vocabularies"]!["readiness"]!.AsArray().Add("REVIEWED"); return null; }),
        ["READY without commit"] = ("S007", scope => { Ready(scope)["candidate"]!["commit"] = "abc1234"; return null; }),
        ["READY without tests"] = ("S007", scope => { Ready(scope)["tests"] = new JsonArray(); return null; }),
        ["READY with failing tests"] = ("S007", scope => { Ready(scope)["tests"]![0]!["failed"] = 1; return null; }),
        ["READY with failed count missing"] = ("S007", scope => { Ready(scope)["tests"]![0]!.AsObject().Remove("failed"); return null; }),
        ["READY with blank project"] = ("S007", scope => { Ready(scope)["tests"]![0]!["project"] = " "; return null; }),
        ["READY without website examples"] = ("S007", scope => { Ready(scope)["websiteExamples"] = new JsonArray(); return null; }),
        ["READY with a missing example page"] = ("S007", scope => { Ready(scope)["websiteExamples"] = new JsonArray("website/content/guides/no-such-page.mdx"); return null; }),
        ["READY without reviews"] = ("S007", scope => { Ready(scope)["reviews"] = new JsonArray(); return null; }),
        ["READY with a missing review record"] = ("S007", scope => { Ready(scope)["reviews"]![0]!["record"] = Dir + "/reviews/no-such-round.md"; return null; }),
        ["READY without approver"] = ("S007", scope => { Ready(scope).AsObject().Remove("approver"); return null; }),
        ["READY claiming full independence"] = ("S007", scope => { Ready(scope)["independence"] = "independent"; return null; }),
        ["READY with unresolved 1423"] = ("S007", scope => { Ready(scope)["blockersResolved"]!.AsArray().RemoveAt(1); return null; }),
        ["READY with a failed case"] = ("S007", scope => { Ready(scope)["cases"]![0]!["result"] = "failed"; return null; }),
        ["READY with a missing case"] = ("S007", scope => { Ready(scope)["cases"] = new JsonArray(); return null; }),
        ["READY made stale by a later amendment"] = ("S007", scope => {
            Ready(scope); Case(scope, "F4-ITER-01")["expected"] = "rejected";
            var s = Amendment(scope, removed: null); s["changes"] = new JsonArray(Change("F4-ITER-01", "expected", "preserved", "rejected"));
            scope["amendments"]!.AsArray().Add(s); scope["scopeVersion"] = Last(scope); return null; }),
        ["BLOCKED without reason"] = ("S008", scope => { Family(scope, "F2")["readiness"] = new JsonObject { ["status"] = "BLOCKED" }; return null; }),
        ["IN_PROGRESS without PR"] = ("S008", scope => { Family(scope, "F2")["readiness"] = new JsonObject { ["status"] = "IN_PROGRESS" }; return null; }),
        ["MET while 1413 is open"] = ("S009", scope => { scope["gateStatus"] = "MET"; scope["gateConditions"]![0]!["state"] = "OPEN"; return null; }),
        ["MET without the gate amendment"] = ("S009", scope => { MetWithoutAmendment(scope);
            foreach (var a in scope["amendments"]!.AsArray()) a!.AsObject().Remove("gateDecision"); return GateReader("CLOSED"); }),
        ["MET with a gate record that names another condition"] = ("S009", scope => {
            return p => p.EndsWith("/gate/capacity-accepted.json") ? Encoding.UTF8.GetBytes("{\"condition\":\"blocker-1413\",\"state\":\"SATISFIED\"}") : ReadRepo(p); }),
        ["mapped blocker missing from a family"] = ("S012", scope => { Family(scope, "F2")["blockers"]!.AsArray().RemoveAt(2); return null; }),
        ["capacity exception for an unknown ceiling"] = ("S010", scope => { scope["capacity"]!["exceptions"]![0]!["ceiling"] = "no-such-ceiling"; return null; }),
        ["mapped blocker naming an unknown family"] = ("S012", scope => { scope["mappedBlockers"]!["applicable"]![0]!["families"] = new JsonArray("F7"); return null; }),
        ["capacity exception widened after registration"] = ("S010", scope => { Exception(scope, "EXC-1520-EXTRA-VERIFICATION")["allowedExtraPasses"] = 99; return null; }),
        ["capacity exception retargeted after registration"] = ("S010", scope => { Exception(scope, "EXC-1538-EXTRA-VERIFICATION")["pr"] = 9999; return null; }),
        ["unconfirmed exception based on an unrelated decision"] = ("S010", scope => {
            Exception(scope, "EXC-1520-EXTRA-VERIFICATION")["basis"] = "D-CAPACITY"; Reregister(scope, "EXC-1520-EXTRA-VERIFICATION"); return null; }),
        ["unconfirmed exception wider than its basis"] = ("S010", scope => {
            Exception(scope, "EXC-1520-EXTRA-VERIFICATION")["allowedExtraPasses"] = 2; Reregister(scope, "EXC-1520-EXTRA-VERIFICATION"); return null; }),
        ["mapping cites a cropped copy of the S2 record"] = ("S012", scope => {
            const string cropped = "docs/plans/evidence/v0.25-r0-1426/cropped-dispositions.json";
            scope["mappedBlockers"]!["source"] = cropped; scope["mappedBlockers"]!["notApplicable"] = new JsonArray();
            return p => {
                if (p != cropped) return ReadRepo(p);
                var s2 = JsonNode.Parse(Encoding.UTF8.GetString(ReadRepo(InteropScopeValidator.S2Record)!))!;
                var keep = new[] { "R-OBL", "R-OBL-RESIDUALS", "R-TEXT" };
                s2["repairs"] = new JsonArray(s2["repairs"]!.AsArray().Where(r => keep.Contains(r!["id"]!.GetValue<string>())).Select(r => r!.DeepClone()).ToArray());
                return Encoding.UTF8.GetBytes(s2.ToJsonString()); }; }),
        ["gate record without its mapping"] = ("S012", scope => {
            return p => {
                if (!p.EndsWith("/gate/blockers-mapped-after-1413.json", StringComparison.Ordinal)) return ReadRepo(p);
                var g = JsonNode.Parse(Encoding.UTF8.GetString(ReadRepo(p)!))!.AsObject(); g.Remove("applicable");
                return Encoding.UTF8.GetBytes(g.ToJsonString()); }; }),
        ["mapping removed while MET"] = ("S012", scope => { scope.AsObject().Remove("mappedBlockers"); return null; }),
        ["mapping emptied while MET"] = ("S012", scope => {
            scope["mappedBlockers"]!["applicable"] = new JsonArray(); scope["mappedBlockers"]!["notApplicable"] = new JsonArray(); return null; }),
        ["S2 repair dropped from the mapping"] = ("S012", scope => { scope["mappedBlockers"]!["notApplicable"]!.AsArray().RemoveAt(0); return null; }),
        ["mapped repair with another merge commit"] = ("S012", scope => {
            var m = scope["mappedBlockers"]!["applicable"]![0]!; m["mergeCommit"] = new string('3', 40); return null; }),
        ["amendment mapping differs from the packet"] = ("S012", scope => {
            scope["amendments"]!.AsArray().Last(a => a!["mappedBlockers"] is JsonObject)!["mappedBlockers"]!["R-TEXT"] = new JsonArray("F2", "F4"); return null; }),
        ["READY without a mapped blocker's witness run"] = ("S007", scope => { MappedResolution(Ready(scope, "F2"), 1496).AsObject().Remove("witnessRun"); return null; }),
        ["READY with a witness run from another candidate"] = ("S007", scope => {
            MappedResolution(Ready(scope, "F2"), 1496)["witnessRun"] = $"{Dir}/ready/witness-R-OBL.{new string('2', 40)}.json"; return null; }),
        ["READY with a failed witness run"] = ("S007", scope => {
            MappedResolution(Ready(scope, "F2"), 1503)["witnessRun"] = $"{Dir}/ready/witness-R-OBL-RESIDUALS.{new string('1', 40)}.failed.json"; return null; }),
        ["READY citing another merge for a mapped blocker"] = ("S007", scope => { MappedResolution(Ready(scope, "F2"), 1497)["mergeCommit"] = new string('4', 40); return null; }),
        ["unconfirmed capacity exception without a basis"] = ("S010", scope => {
            var e = scope["capacity"]!["exceptions"]!.AsArray().First(x => !x!["confirmed"]!.GetValue<bool>())!; e.AsObject().Remove("basis"); return null; }),
        ["FROZEN without acceptance"] = ("S009", scope => { Freeze(scope); scope["acceptance"] = null; return null; }),
        ["ceiling accepted while proposed"] = ("S009", scope => { Unfreeze(scope); scope["capacity"]!["ceilings"]![0]!["status"] = "ACCEPTED"; return null; }),
        ["ceiling still proposed while frozen"] = ("S009", scope => { Freeze(scope); scope["capacity"]!["ceilings"]![0]!["status"] = "PROPOSED"; return null; }),
        ["acceptance without a full merge commit"] = ("S009", scope => { Freeze(scope); scope["acceptance"]!["mergeCommit"] = "16c1c5f8"; return null; }),
        ["1413 condition removed"] = ("S009", scope => { scope["gateConditions"]!.AsArray().RemoveAt(0); return null; }),
        ["capacity condition removed"] = ("S009", scope => { scope["gateConditions"]!.AsArray().RemoveAt(1); return null; }),
        ["ceiling without unit"] = ("S010", scope => { scope["capacity"]!["ceilings"]![0]!["unit"] = ""; return null; }),
        ["paid spend above zero"] = ("S010", scope => { Ceiling(scope, "paid-spend")["value"] = 50; return null; }),
        ["deviation recorded as full"] = ("S011", scope => { scope["independence"]!["recordValue"] = "full"; return null; }),
        ["deviation lifted without amendment"] = ("S011", scope => { scope["independence"]!["deviation"] = false; return null; }),
        ["family without 1423 blocker"] = ("S012", scope => { Family(scope, "F5")["blockers"] = new JsonArray(1426); return null; }),
        ["F1 without 1427 blocker"] = ("S012", scope => { Family(scope, "F1")["blockers"] = new JsonArray(1423); return null; }),
        ["family without 1426 blocker"] = ("S012", scope => { Family(scope, "F3")["blockers"] = new JsonArray(1423); return null; }),
        ["baseline overstated"] = ("S013", scope => { Case(scope, "F5-ARRAY-01")["baseline"]!["mcp-default"] = "native-match"; return null; }),
        ["observation not in results"] = ("S013", scope => { // the frozen digest follows, to isolate S013 from S004
            Case(scope, "F6-REPORT-01")["observations"]![0]!["equals"] = "(2 via the rescue path)";
            scope["denominatorV1"]!.AsArray().Single(d => d!["id"]!.GetValue<string>() == "F6-REPORT-01")!["requirementSha256"] =
            Requirement(Case(scope, "F6-REPORT-01")); return null; }),
        ["results edited"] = ("S013", scope => { return p => p == Results ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ReadRepo(p)!) + " ") : ReadRepo(p); }),
        ["generated output edited"] = ("S013", scope => { return p => p.EndsWith("F5-ARRAY-01.mcp-default.calr.txt") ? Encoding.UTF8.GetBytes("edited") : ReadRepo(p); }),
        ["surface dropped from baseline"] = ("S013", scope => { Case(scope, "F4-ITER-01")["baseline"]!.AsObject().Remove("mcp-default"); return null; }),
        ["producer edited"] = ("S013", scope => { return p => p.EndsWith("reproduce/reproduce.py") ? Encoding.UTF8.GetBytes("# edited") : ReadRepo(p); }),
        ["acceptance observation dropped"] = ("S004", scope => { Case(scope, "F6-REPORT-03")["observations"]!.AsArray().RemoveAt(3); return null; }),
        ["removal reused after restoration"] = ("S004", scope => {
            scope["amendments"]!.AsArray().Add(Amendment(scope, removed: "F6-REPORT-10"));
            var r2 = Amendment(scope, removed: null); r2["addedCases"] = new JsonArray(Frozen(scope, "F6-REPORT-10"));
            scope["amendments"]!.AsArray().Add(r2); scope["scopeVersion"] = Last(scope); Remove(scope["cases"]!, "F6-REPORT-10"); return null; }),
        ["READY stale after a family move"] = ("S007", scope => {
            Ready(scope); Case(scope, "F4-ITER-04")["family"] = "F5";
            var mv = Amendment(scope, removed: null); mv["changes"] = new JsonArray(Change("F4-ITER-04", "family", "F4", "F5"));
            scope["amendments"]!.AsArray().Add(mv); scope["scopeVersion"] = Last(scope);
            var f4Rows = Family(scope, "F4")["readiness"]!["cases"]!.AsArray();
            f4Rows.Remove(f4Rows.Single(x => x!["id"]!.GetValue<string>() == "F4-ITER-04")); return null; }),
        // Amendment 1.5.0 (D-F4-ITER-04-NOT-MEASURED): a not-measured case is recorded as skipped, and only such a case.
        ["READY with a not-measured case recorded passed"] = ("S007", scope => { Row(Ready(scope), "F4-ITER-04")["result"] = "passed"; return null; }),
        ["READY with a measured case recorded skipped"] = ("S007", scope => { Row(Ready(scope), "F4-ITER-01")["result"] = "skipped"; return null; }),
        ["not-measured case without a reason"] = ("S004", scope => { NotMeasuredEntry(scope)["reason"] = " "; return null; }),
        ["not-measured case without a decision"] = ("S004", scope => { NotMeasuredEntry(scope).AsObject().Remove("decision"); return null; }),
        ["not-measured case covered by another family"] = ("S004", scope => { NotMeasuredEntry(scope)["coveredBy"]!.AsArray().Add("F5-ARRAY-01"); return null; }),
        ["not-measured case covered by nothing"] = ("S004", scope => { NotMeasuredEntry(scope)["coveredBy"] = new JsonArray(); return null; }),
        ["not-measured case in the wrong family"] = ("S004", scope => { NotMeasuredEntry(scope)["family"] = "F5"; return null; }),
        ["not-measured unregistered case"] = ("S004", scope => { NotMeasuredEntry(scope)["id"] = "F4-ITER-99"; return null; }),
        ["not-measured marking retargeted to another case"] = ("S004", scope => {
            NotMeasuredEntry(scope)["id"] = "F4-ITER-02"; NotMeasuredEntry(scope)["coveredBy"] = new JsonArray("F4-ITER-01"); return null; }),
        ["not-measured marking under an unapproved decision"] = ("S004", scope => { NotMeasuredEntry(scope)["decision"] = "UNAPPROVED-DECISION"; return null; }),
        ["approved decision no longer names the case"] = ("S004", scope => {
            scope["amendments"]!.AsArray().Single(a => a!["version"]!.GetValue<string>() == "1.5.0")!["maintainerDecisions"]!.AsArray()
                .Single(d => d!["id"]!.GetValue<string>() == "D-F4-ITER-04-NOT-MEASURED")!["notMeasured"] = new JsonArray("F4-ITER-02"); return null; }),
        ["READY with README as example"] = ("S007", scope => { Ready(scope)["websiteExamples"] = new JsonArray("README.md"); return null; }),
        ["READY with README as review"] = ("S007", scope => { Ready(scope)["reviews"]![0]!["record"] = "README.md"; return null; }),
        ["READY with a bare approval URL"] = ("S007", scope => { Ready(scope)["approver"]!["approvalUrl"] = "https://github.com/"; return null; }),
        ["READY case without retained result"] = ("S007", scope => { Ready(scope)["cases"]![0]!.AsObject().Remove("evidence"); return null; }),
        ["READY result from another candidate"] = ("S007", scope => { Ready(scope)["cases"]![0]!["evidence"] = $"{Dir}/ready/F4-ITER-01.{new string('2', 40)}.json"; return null; }),
        ["READY result from historical evidence"] = ("S007", scope => { Ready(scope)["cases"]![0]!["evidence"] = Results; return null; }),
        ["READY example by path traversal"] = ("S007", scope => { Ready(scope)["websiteExamples"] = new JsonArray("website/content/../../README.md"); return null; }),
        ["READY review by path traversal"] = ("S007", scope => { Ready(scope)["reviews"]![0]!["record"] = $"{Dir}/reviews/../../../../../README.md"; return null; }),
        ["READY result by path traversal"] = ("S007", scope => { Ready(scope)["cases"]![0]!["evidence"] = "docs/plans/evidence/../../../README.md"; return null; }),
        ["MET with a relabeled interaction row"] = ("S009", scope => { MetWithAmendment(scope);
            foreach (var x in scope["cases"]!.AsArray().Where(c => c!["role"]!.GetValue<string>() == "interaction").ToList())
            { x!.AsObject().Remove("families"); Rehash(scope, x); }
            return GateReader("CLOSED"); }),
        ["MET with the planning doc as evidence"] = ("S009", scope => { MetWithAmendment(scope); scope["gateConditions"]![0]!["evidence"] = "docs/plans/v0.25-interop-scope-and-baseline.md"; return GateReader("CLOSED"); }),
        ["MET while the 1413 record is open"] = ("S009", scope => { MetWithAmendment(scope); return GateReader("OPEN"); }),
        // 1.3.0: the "#1408 succeeds" precondition is replaced by a re-proof on the 0.25 candidate.
        ["A1 still waits on 1408"] = ("S014", scope => { A1Blockers(scope).Add(1408); return null; }),
        ["release gap dropped from A1"] = ("S014", scope => {
            var b = A1Blockers(scope); b.Remove(b.Single(x => x!.GetValue<int>() == 1547)); return null; }),
        ["precondition copy drifts from its amendment"] = ("S014", scope => { scope["acceptancePreconditions"]!["requires"] = "#1408 succeeds"; return null; }),
        ["precondition without its recorded decision"] = ("S014", scope => { Preconditions(scope, p => p["decision"] = "D-NONE"); return null; }),
        ["precondition misstates the 1408 outcome"] = ("S014", scope => { Preconditions(scope, p => p["replaces"]!["outcome"] = "MILESTONE-SUCCEEDED"); return null; }),
        ["re-proof on another candidate's SHA"] = ("S014", scope => { Preconditions(scope, p => p["reproof"]!["onCandidate"] = "0.24-candidate"); return null; }),
        ["re-proof without a release check"] = ("S014", scope => { Preconditions(scope, p => {
            var checks = p["reproof"]!["checks"]!.AsArray(); checks.Remove(checks.Single(c => c!["kind"]!.GetValue<string>() == "release")); }); return null; }),
        ["re-proof check citing a missing source"] = ("S014", scope => { Preconditions(scope, p => p["reproof"]!["checks"]![0]!["source"] = "docs/plans/evidence/none.json"); return null; }),
        // Codex R1-2: dropping a decided gap or check from both copies (and from A1) still fails.
        ["release gap dropped from both copies and A1"] = ("S014", scope => {
            var b = A1Blockers(scope); b.Remove(b.Single(x => x!.GetValue<int>() == 1547));
            Preconditions(scope, p => { var g = p["releaseGaps"]!.AsArray(); g.Remove(g.Single(x => x!["issue"]!.GetValue<int>() == 1547)); }); return null; }),
        ["determinism re-proof dropped from both copies"] = ("S014", scope => { Preconditions(scope, p => {
            var checks = p["reproof"]!["checks"]!.AsArray(); checks.Remove(checks.Single(c => c!["id"]!.GetValue<string>() == "provenance-determinism")); }); return null; }),
        // Codex R2-1: deleting the packet copy and the amendment copy together still fails at 1.3.0.
        ["preconditions removed from the packet and the amendment"] = ("S014", scope => {
            scope.AsObject().Remove("acceptancePreconditions");
            scope["amendments"]!.AsArray().Last(a => a!["acceptancePreconditions"] is JsonObject)!.AsObject().Remove("acceptancePreconditions");
            return null; }),
        // Codex R3-3: weakening the recorded obligations in place (both copies) fails the pinned 1.3.0 text.
        ["re-proof requirement weakened in both copies"] = ("S014", scope => {
            Preconditions(scope, p => p["reproof"]!["checks"]![0]!["requirement"] = "Reuse the 0.24 result."); return null; }),
        ["re-proof source redirected in both copies"] = ("S014", scope => {
            Preconditions(scope, p => p["reproof"]!["checks"]![4]!["source"] = "docs/plans/v0.24-c1-candidate.md"); return null; }),
        ["preconditions removed from the packet"] = ("S014", scope => { scope.AsObject().Remove("acceptancePreconditions"); return null; }),
    };

    private static JsonArray A1Blockers(JsonNode scope) =>
        scope["supportingIssues"]!.AsArray().Single(s => s!["issue"]!.GetValue<int>() == 1430)!["blockers"]!.AsArray();

    /// <summary>Applies one edit to the packet's preconditions and to the amendment copy, isolating S014's content checks from its copy check.</summary>
    private static void Preconditions(JsonNode scope, Action<JsonNode> edit)
    {
        edit(scope["acceptancePreconditions"]!);
        edit(scope["amendments"]!.AsArray().Last(a => a!["acceptancePreconditions"] is JsonObject)!["acceptancePreconditions"]!);
    }

    // ------------------------------------------------------------------

    private static IReadOnlyList<ContractViolation> Run(JsonNode scope, Func<string, byte[]?>? read = null,
        string? extraFixture = null, string? withoutFixture = null)
    {
        read = ReadyRecords(scope, read ?? ReadRepo);
        var fixtures = Files("fixtures").Where(f => Path.GetFileName(f) != withoutFixture)
            .Concat(extraFixture is null ? [] : [extraFixture]).ToList();
        var results = JsonNode.Parse(Encoding.UTF8.GetString(read(Results)!))!;
        return InteropScopeValidator.Validate(scope, results, read, fixtures, Files("generated"));
    }

    private static List<string> Files(string sub) =>
        Directory.EnumerateFiles(Path.Combine(EvidenceContractTests.RepoRoot(), Dir, sub))
            .Where(f => Path.GetFileName(f) != ".DS_Store")
            .Select(f => Path.GetRelativePath(EvidenceContractTests.RepoRoot(), f).Replace('\\', '/')).ToList();

    /// <summary>Serves READY result records at ready/&lt;case&gt;.&lt;candidate&gt;.json for the packet's current requirements.</summary>
    private static Func<string, byte[]?> ReadyRecords(JsonNode scope, Func<string, byte[]?> inner) => p =>
    {
        if (!p.StartsWith(Dir + "/ready/", StringComparison.Ordinal)) return inner(p);
        var parts = Path.GetFileNameWithoutExtension(p).Split('.');
        if (parts[0].StartsWith("witness-", StringComparison.Ordinal))
        {
            var repair = parts[0]["witness-".Length..];
            var m = scope["mappedBlockers"]?["applicable"]?.AsArray().FirstOrDefault(x => x!["repair"]!.GetValue<string>() == repair);
            return m is null ? null : Encoding.UTF8.GetBytes(new JsonObject
            {
                ["candidate"] = parts[1], ["repair"] = repair, ["witnesses"] = m["regressionWitness"]!.DeepClone(),
                ["result"] = parts.Length > 2 ? parts[2] : "passed",
            }.ToJsonString());
        }
        var c = scope["cases"]!.AsArray().FirstOrDefault(x => x!["id"]!.GetValue<string>() == parts[0]);
        return c is null ? null : Encoding.UTF8.GetBytes(new JsonObject
        {
            ["candidate"] = parts[1], ["caseId"] = parts[0], ["requirementSha256"] = Requirement(c), ["result"] = ExpectedResult(scope, parts[0]),
            ["cells"] = new JsonObject((c["cells"]?.AsArray() ?? []).Select(x => KeyValuePair.Create(x!.GetValue<string>(), (JsonNode?)"passed"))),
        }.ToJsonString());
    };

    private static byte[]? ReadRepo(string path)
    {
        var full = Path.Combine(EvidenceContractTests.RepoRoot(), path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    /// <summary>
    /// R0 scope 1.6.0 wrote every family's readiness back as READY (D-READY-EVIDENCE-DIRS). A control that adds, changes
    /// or removes a case also makes those records stale (S007), correctly. To isolate each control's own code, the controls
    /// run on the packet with readiness as it stood before the write-back (IN_PROGRESS, with a branch);
    /// <see cref="WrittenBackReadyGoesStaleOnALaterCaseChange"/> covers staleness of the written-back records.
    /// </summary>
    private static JsonNode Unready(JsonNode scope)
    {
        foreach (var f in scope["families"]!.AsArray())
            f!["readiness"] = new JsonObject
            {
                ["status"] = "IN_PROGRESS", ["branch"] = $"pre-write-back control ({f["id"]!.GetValue<string>()})",
                ["evidence"] = f["readiness"]!["evidence"]!.DeepClone(),
            };
        return scope;
    }

    private static JsonNode Scope() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(), Dir, "scope.json")))!;

    private static string Seal(JsonNode scope) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        scope["denominatorV1"]!.ToJsonString() + "\n" + scope["evidenceSeal"]!["baselineResults"]!.GetValue<string>()
        + "\n" + scope["evidenceSeal"]!["generated"]!.GetValue<string>() + "\n" + scope["evidenceSeal"]!["producers"]!.ToJsonString())));

    private static JsonNode Family(JsonNode scope, string id) => scope["families"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Case(JsonNode scope, string id) => scope["cases"]!.AsArray().First(c => c!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Ceiling(JsonNode scope, string id) => scope["capacity"]!["ceilings"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;

    /// <summary>Re-registers a case's requirement digest wherever it is registered (1.0.0 or an amendment), to isolate a control from S004.</summary>
    private static void Rehash(JsonNode scope, JsonNode c)
    {
        var id = c["id"]!.GetValue<string>();
        var entries = scope["denominatorV1"]!.AsArray()
            .Concat(scope["amendments"]!.AsArray().SelectMany(a => a!["addedCases"]?.AsArray() ?? []))
            .Where(d => d!["id"]!.GetValue<string>() == id);
        foreach (var e in entries) e!["requirementSha256"] = Requirement(c);
    }

    private static JsonNode Frozen(JsonNode scope, string id) =>
        scope["denominatorV1"]!.AsArray().Single(d => d!["id"]!.GetValue<string>() == id)!.DeepClone();

    private static void MetWithAmendment(JsonNode scope)
    {
        MetWithoutAmendment(scope);
        var a = Amendment(scope, removed: null);
        a["gateDecision"] = "R0-MET";
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = Last(scope);
    }

    private static void SetBoth(JsonNode scope, string id, string field, string value)
    {
        Case(scope, id)[field] = value;
        scope["denominatorV1"]!.AsArray().Single(d => d!["id"]!.GetValue<string>() == id)![field] = value;
    }

    private static void Remove(JsonNode list, params string[] ids)
    {
        foreach (var node in list.AsArray().Where(c => ids.Contains(c!["id"]!.GetValue<string>())).ToList())
            list.AsArray().Remove(node);
    }

    /// <summary>Drops a family's cases through a retaining amendment, so a family-removal control isolates S001.</summary>
    private static void RemoveFamilyCases(JsonNode scope, string family)
    {
        var ids = scope["cases"]!.AsArray().Where(c => c!["family"]!.GetValue<string>() == family).Select(c => c!["id"]!.GetValue<string>()).ToArray();
        Remove(scope["cases"]!, ids);
        var a = Amendment(scope, removed: null);
        a["removedCases"] = new JsonArray(ids.Select(i => (JsonNode)new JsonObject { ["id"] = i, ["lastStatus"] = "reproduces" }).ToArray());
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = Last(scope);
    }

    private static void Register(JsonNode scope, JsonNode added)
    {
        var a = Amendment(scope, removed: null);
        var entry = added.DeepClone();
        entry["requirementSha256"] = Requirement(added);
        a["addedCases"] = new JsonArray(entry);
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = Last(scope);
    }

    /// <summary>What a READY record says for a case: skipped when an amendment marked it not-measured (1.5.0).</summary>
    private static string ExpectedResult(JsonNode scope, string id) =>
        scope["amendments"]!.AsArray().SelectMany(a => a!["notMeasuredCases"]?.AsArray() ?? []).Any(m => m!["id"]!.GetValue<string>() == id) ? "skipped" : "passed";

    private static JsonNode NotMeasuredEntry(JsonNode scope) =>
        scope["amendments"]!.AsArray().Single(a => a!["version"]!.GetValue<string>() == "1.5.0")!["notMeasuredCases"]![0]!;

    private static JsonNode Row(JsonNode ready, string id) => ready["cases"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;

    private static string Last(JsonNode scope) => scope["amendments"]!.AsArray().Last()!["version"]!.GetValue<string>();

    /// <summary>The next patch version after the packet's current version, so controls stay valid after real amendments.</summary>
    private static JsonObject Amendment(JsonNode scope, string? removed) => new()
    {
        ["version"] = Bump(scope["amendments"]!.AsArray().Count == 0 ? scope["scopeVersion"]!.GetValue<string>() : Last(scope)), ["dateUtc"] = "2026-10-10T00:00:00Z", ["pr"] = 1500, ["afterInspection"] = false,
        ["justification"] = "control",
        ["removedCases"] = removed is null ? new JsonArray() : new JsonArray(new JsonObject { ["id"] = removed, ["lastStatus"] = "reproduces" }),
    };

    private static string Bump(string v) { var p = v.Split('.'); return $"{p[0]}.{p[1]}.{int.Parse(p[2]) + 1}"; }

    private static string Requirement(JsonNode c) => InteropScopeValidator.RequirementDigest(c);

    /// <summary>The PROPOSED state the packet merged in: no acceptance, NOT-MET, every ceiling PROPOSED.</summary>
    private static void Unfreeze(JsonNode scope)
    {
        scope["status"] = "PROPOSED";
        scope["gateStatus"] = "NOT-MET";
        scope["acceptance"] = null;
        scope["capacity"]!["status"] = "PROPOSED";
        foreach (var c in scope["capacity"]!["ceilings"]!.AsArray()) c!["status"] = "PROPOSED";
    }

    private static void Freeze(JsonNode scope)
    {
        scope["status"] = "FROZEN";
        scope["acceptance"] = new JsonObject { ["mergeCommit"] = new string('1', 40), ["mergedAtUtc"] = "2026-10-10T00:00:00Z", ["pr"] = 1483 };
        scope["capacity"]!["status"] = "ACCEPTED";
        foreach (var c in scope["capacity"]!["ceilings"]!.AsArray()) c!["status"] = "ACCEPTED";
    }

    /// <summary>Serves gate/&lt;id&gt;.json records; issueState is what the #1413 record says.</summary>
    /// <summary>
    /// Serves gate/&lt;id&gt;.json records; issueState is what the #1413 record says. A committed record keeps its
    /// substantive evidence (such as the blocker mapping), so a MET positive still exercises reconciliation.
    /// </summary>
    private static Func<string, byte[]?> GateReader(string issueState) => p =>
    {
        if (!p.StartsWith(Dir + "/gate/", StringComparison.Ordinal)) return ReadRepo(p);
        var record = ReadRepo(p) is { } b ? JsonNode.Parse(Encoding.UTF8.GetString(b))!.AsObject() : new JsonObject();
        record["condition"] = Path.GetFileNameWithoutExtension(p);
        record["state"] = "SATISFIED";
        record["issueState"] = issueState;
        record["closedAtUtc"] = "2026-10-09T00:00:00Z";
        return Encoding.UTF8.GetBytes(record.ToJsonString());
    };

    private static JsonObject Change(string id, string field, string from, string to) =>
        new() { ["id"] = id, ["field"] = field, ["from"] = from, ["to"] = to };

    /// <summary>Everything a MET gate needs except the gate amendment.</summary>
    private static void MetWithoutAmendment(JsonNode scope)
    {
        Freeze(scope);
        scope["gateStatus"] = "MET";
        foreach (var g in scope["gateConditions"]!.AsArray())
        {
            g!["state"] = "SATISFIED";
            g["evidence"] = $"{Dir}/gate/{g["id"]!.GetValue<string>()}.json";
        }
        foreach (var role in new[] { "interaction", "doc-example" })
        {
            var row = Case(scope, "F6-REPORT-01").DeepClone();
            row["id"] = "X-" + role;
            row["role"] = role;
            if (role == "interaction") row["families"] = new JsonArray("F1", "F2");
            else row["websiteExample"] = "website/content/cli/convert.mdx";
            scope["cases"]!.AsArray().Add(row.DeepClone());
            row["requirementSha256"] = Requirement(row);
            scope["denominatorV1"]!.AsArray().Add(row);
        }
    }

    private static JsonNode Exception(JsonNode scope, string id) =>
        scope["capacity"]!["exceptions"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == id)!;

    /// <summary>Copies an exception's current value into its amendment, to isolate a control from the copy check.</summary>
    private static void Reregister(JsonNode scope, string id)
    {
        // The amendment that registered this exception, not necessarily the latest one.
        var version = Exception(scope, id)["amendment"]!.GetValue<string>();
        var copies = scope["amendments"]!.AsArray().Single(a => a!["version"]!.GetValue<string>() == version)!["capacityExceptions"]!.AsArray();
        var i = copies.Select((c, k) => (c, k)).Single(x => x.c!["id"]!.GetValue<string>() == id).k;
        copies[i] = Exception(scope, id).DeepClone();
    }

    private static JsonNode MappedResolution(JsonNode ready, int pr) =>
        ready["blockersResolved"]!.AsArray().Single(x => x!["issue"]!.GetValue<int>() == pr)!;

    private static JsonNode Ready(JsonNode scope, string family = "F4")
    {
        var candidate = new string('1', 40);
        var mapped = scope["mappedBlockers"]?["applicable"]?.AsArray().ToList() ?? [];
        var record = new JsonObject
        {
            ["status"] = "READY",
            ["scopeVersion"] = scope["scopeVersion"]!.GetValue<string>(),
            ["candidate"] = new JsonObject { ["commit"] = new string('1', 40) },
            ["cases"] = new JsonArray(scope["cases"]!.AsArray().Where(c => c!["family"]!.GetValue<string>() == family)
                .Select(c => (JsonNode)new JsonObject { ["id"] = c!["id"]!.GetValue<string>(), ["result"] = ExpectedResult(scope, c["id"]!.GetValue<string>()),
                    ["evidence"] = $"{Dir}/ready/{c!["id"]!.GetValue<string>()}.{new string('1', 40)}.json" }).ToArray()),
            ["tests"] = new JsonArray(new JsonObject { ["project"] = "tests/Calor.Conversion.Tests", ["total"] = 12, ["failed"] = 0 }),
            ["websiteExamples"] = new JsonArray("website/content/cli/convert.mdx#what-changed-in-0-16"),
            ["reviews"] = new JsonArray(new JsonObject { ["reviewer"] = "codex", ["record"] = Dir + "/reviews/round-1.md" }),
            ["approver"] = new JsonObject { ["login"] = "juanmicrosoft", ["approvalUrl"] = "https://github.com/juanmicrosoft/calor/pull/1#pullrequestreview-1" },
            ["independence"] = "reduced",
            ["blockersResolved"] = new JsonArray(Family(scope, family)["blockers"]!.AsArray().Select(b =>
            {
                var issue = b!.GetValue<int>();
                var res = new JsonObject { ["issue"] = issue, ["reference"] = issue == 1423 ? "candidate manifest commit" : "closed by PR #1" };
                if (mapped.FirstOrDefault(m => m!["pr"]!.GetValue<int>() == issue) is { } m)
                {
                    res["mergeCommit"] = m["mergeCommit"]!.GetValue<string>();
                    res["witnessRun"] = $"{Dir}/ready/witness-{m["repair"]!.GetValue<string>()}.{candidate}.json";
                }
                return (JsonNode)res;
            }).ToArray()),
        };
        Family(scope, family)["readiness"] = record;
        return record;
    }
}
