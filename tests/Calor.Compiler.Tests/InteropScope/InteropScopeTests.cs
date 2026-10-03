using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.EvidenceContract;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1426 (0.25 R0) — the committed scope and baseline packet is valid, and the validator fails
/// closed. Each negative control changes one fact of the committed packet and asserts that only
/// the expected violation code fires, so a validator that rejected everything would fail the
/// positive controls and one that accepted everything would fail the negative ones.
/// </summary>
public class InteropScopeTests
{
    /// <summary>
    /// SHA-256 over the frozen 1.0.0 denominator and the evidence seal. Both are frozen at 1.0.0;
    /// later changes are amendments, never edits to them. A packet that drops a case from both the
    /// cases and the frozen list, or regenerates and reseals the evidence, passes the validator but
    /// fails this pin.
    /// </summary>
    private const string FrozenSeal = "dfd029745a39da716e4b750677ca4f6601496e4e803f0a6b44e9e0572faffc70";

    private const string Dir = InteropScopeValidator.PacketDir;
    private const string Results = Dir + "/baseline-results.json";

    [Fact]
    public void CommittedPacketIsValid()
    {
        var violations = Run(Scope());
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

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
    public void CommittedPacketIsNotMetWhileAnyGateConditionIsOpen()
    {
        // Lifecycle-aware: PROPOSED until the acceptance write-back, then FROZEN; MET only once every
        // gate condition is SATISFIED (S009 checks the records behind that).
        var scope = Scope();
        var open = scope["gateConditions"]!.AsArray().Any(g => g!["state"]!.GetValue<string>() != "SATISFIED");
        Assert.True(!open || scope["gateStatus"]!.GetValue<string>() == "NOT-MET");
        Assert.Equal(6, scope["families"]!.AsArray().Count);
    }

    [Fact]
    public void FrozenNotMetPacketPasses()
    {
        var scope = Scope();
        Freeze(scope);
        scope["gateStatus"] = "NOT-MET";
        var violations = Run(scope);
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    [Fact]
    public void FullyEvidencedMetPacketPasses()
    {
        var scope = Scope();
        MetWithAmendment(scope);
        var violations = Run(scope, GateReader("CLOSED"));
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    [Fact]
    public void WellFormedReadyRecordPasses()
    {
        var scope = Scope();
        Ready(scope);
        var violations = Run(scope);
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    [Fact]
    public void AmendmentsThatRetainRemoveRegisterAndChangeCasesPass()
    {
        var scope = Scope();
        Remove(scope["cases"]!, "F5-ARRAY-02");
        var added = Case(scope, "F6-REPORT-10").DeepClone();
        added["id"] = "F6-REPORT-99";
        scope["cases"]!.AsArray().Add(added.DeepClone());
        Case(scope, "F4-ITER-01")["expected"] = "rejected";
        var a = Amendment(scope, removed: "F5-ARRAY-02");
        added["requirementSha256"] = Requirement(added);
        a["addedCases"] = new JsonArray(added);
        a["changes"] = new JsonArray(Change("F4-ITER-01", "expected", "preserved", "rejected"));
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
        var scope = Scope();
        var (code, apply) = Controls[mutation];
        var violations = Run(scope, apply(scope) ?? ReadRepo);
        Assert.True(violations.Count > 0 && violations.All(x => x.Code == code), $"{mutation}: {EvidenceContractTests.Describe(violations)}");
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
        ["scope version ahead of amendments"] = ("S005", scope => { scope["scopeVersion"] = "1.1.0"; return null; }),
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
        ["MET while 1413 is open"] = ("S009", scope => { scope["gateStatus"] = "MET"; return null; }),
        ["MET without the gate amendment"] = ("S009", scope => { MetWithoutAmendment(scope); return null; }),
        ["FROZEN without acceptance"] = ("S009", scope => { scope["status"] = "FROZEN"; scope["capacity"]!["status"] = "ACCEPTED"; return null; }),
        ["ceiling accepted while proposed"] = ("S009", scope => { scope["capacity"]!["ceilings"]![0]!["status"] = "ACCEPTED"; return null; }),
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
        ["READY with README as example"] = ("S007", scope => { Ready(scope)["websiteExamples"] = new JsonArray("README.md"); return null; }),
        ["READY with README as review"] = ("S007", scope => { Ready(scope)["reviews"]![0]!["record"] = "README.md"; return null; }),
        ["READY with a bare approval URL"] = ("S007", scope => { Ready(scope)["approver"]!["approvalUrl"] = "https://github.com/"; return null; }),
        ["READY case without retained result"] = ("S007", scope => { Ready(scope)["cases"]![0]!.AsObject().Remove("evidence"); return null; }),
        ["MET with the planning doc as evidence"] = ("S009", scope => { MetWithAmendment(scope); scope["gateConditions"]![0]!["evidence"] = "docs/plans/v0.25-interop-scope-and-baseline.md"; return GateReader("CLOSED"); }),
        ["MET while the 1413 record is open"] = ("S009", scope => { MetWithAmendment(scope); return GateReader("OPEN"); }),
    };

    // ------------------------------------------------------------------

    private static IReadOnlyList<ContractViolation> Run(JsonNode scope, Func<string, byte[]?>? read = null,
        string? extraFixture = null, string? withoutFixture = null)
    {
        read ??= ReadRepo;
        var fixtures = Files("fixtures").Where(f => Path.GetFileName(f) != withoutFixture)
            .Concat(extraFixture is null ? [] : [extraFixture]).ToList();
        var results = JsonNode.Parse(Encoding.UTF8.GetString(read(Results)!))!;
        return InteropScopeValidator.Validate(scope, results, read, fixtures, Files("generated"));
    }

    private static List<string> Files(string sub) =>
        Directory.EnumerateFiles(Path.Combine(EvidenceContractTests.RepoRoot(), Dir, sub))
            .Where(f => Path.GetFileName(f) != ".DS_Store")
            .Select(f => Path.GetRelativePath(EvidenceContractTests.RepoRoot(), f).Replace('\\', '/')).ToList();

    private static byte[]? ReadRepo(string path)
    {
        var full = Path.Combine(EvidenceContractTests.RepoRoot(), path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    private static JsonNode Scope() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(), Dir, "scope.json")))!;

    private static string Seal(JsonNode scope) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        scope["denominatorV1"]!.ToJsonString() + "\n" + scope["evidenceSeal"]!["baselineResults"]!.GetValue<string>()
        + "\n" + scope["evidenceSeal"]!["generated"]!.GetValue<string>() + "\n" + scope["evidenceSeal"]!["producers"]!.ToJsonString())));

    private static JsonNode Family(JsonNode scope, string id) => scope["families"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Case(JsonNode scope, string id) => scope["cases"]!.AsArray().First(c => c!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Ceiling(JsonNode scope, string id) => scope["capacity"]!["ceilings"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;

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

    private static void Freeze(JsonNode scope)
    {
        scope["status"] = "FROZEN";
        scope["acceptance"] = new JsonObject { ["mergeCommit"] = new string('1', 40), ["mergedAtUtc"] = "2026-10-10T00:00:00Z", ["pr"] = 1483 };
        scope["capacity"]!["status"] = "ACCEPTED";
        foreach (var c in scope["capacity"]!["ceilings"]!.AsArray()) c!["status"] = "ACCEPTED";
    }

    /// <summary>Serves gate/&lt;id&gt;.json records; issueState is what the #1413 record says.</summary>
    private static Func<string, byte[]?> GateReader(string issueState) => p =>
        p.StartsWith(Dir + "/gate/", StringComparison.Ordinal)
            ? Encoding.UTF8.GetBytes(new JsonObject
            {
                ["condition"] = Path.GetFileNameWithoutExtension(p), ["state"] = "SATISFIED",
                ["issueState"] = issueState, ["closedAtUtc"] = "2026-10-09T00:00:00Z",
            }.ToJsonString())
            : ReadRepo(p);

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
            scope["cases"]!.AsArray().Add(row.DeepClone());
            row["requirementSha256"] = Requirement(row);
            scope["denominatorV1"]!.AsArray().Add(row);
        }
    }

    private static JsonNode Ready(JsonNode scope)
    {
        var record = new JsonObject
        {
            ["status"] = "READY",
            ["scopeVersion"] = "1.0.0",
            ["candidate"] = new JsonObject { ["commit"] = new string('1', 40) },
            ["cases"] = new JsonArray(scope["cases"]!.AsArray().Where(c => c!["family"]!.GetValue<string>() == "F4")
                .Select(c => (JsonNode)new JsonObject { ["id"] = c!["id"]!.GetValue<string>(), ["result"] = "passed", ["evidence"] = Results }).ToArray()),
            ["tests"] = new JsonArray(new JsonObject { ["project"] = "tests/Calor.Conversion.Tests", ["total"] = 12, ["failed"] = 0 }),
            ["websiteExamples"] = new JsonArray("website/content/cli/convert.mdx#what-changed-in-0-16"),
            ["reviews"] = new JsonArray(new JsonObject { ["reviewer"] = "codex", ["record"] = Dir + "/reviews/round-1.md" }),
            ["approver"] = new JsonObject { ["login"] = "juanmicrosoft", ["approvalUrl"] = "https://github.com/juanmicrosoft/calor/pull/1#pullrequestreview-1" },
            ["independence"] = "reduced",
            ["blockersResolved"] = new JsonArray(
                new JsonObject { ["issue"] = 1426, ["reference"] = "closed by PR #1" },
                new JsonObject { ["issue"] = 1423, ["reference"] = "candidate manifest commit" }),
        };
        Family(scope, "F4")["readiness"] = record;
        return record;
    }
}
