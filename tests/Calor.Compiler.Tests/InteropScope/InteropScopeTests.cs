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
    private const string FrozenSeal = "774f5c360cf5dd62d7796aba63f4c5a2e2adc4618e367432d2266c829a920d64";

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
    public void CommittedPacketIsProposedAndNotMetWhile1413IsOpen()
    {
        var scope = Scope();
        Assert.Equal("PROPOSED", scope["status"]!.GetValue<string>());
        Assert.Equal("NOT-MET", scope["gateStatus"]!.GetValue<string>());
        var blocker = scope["gateConditions"]!.AsArray().Single(g => g!["issue"]?.GetValue<int>() == 1413)!;
        Assert.Equal("OPEN", blocker["state"]!.GetValue<string>());
        Assert.Equal(6, scope["families"]!.AsArray().Count);
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
        var a = Amendment("1.0.1", removed: "F5-ARRAY-02");
        a["addedCases"] = new JsonArray(added);
        a["changes"] = new JsonArray(Change("F4-ITER-01", "expected", "preserved", "rejected"));
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = "1.0.1";
        var violations = Run(scope);
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    [Theory]
    [InlineData("family missing", "S001")]
    [InlineData("family tracks another issue", "S001")]
    [InlineData("unknown family", "S001")]
    [InlineData("duplicate case id", "S002")]
    [InlineData("role outside vocabulary", "S002")]
    [InlineData("vocabulary extended", "S002")]
    [InlineData("measured status without baseline", "S002")]
    [InlineData("fixture hash drift", "S003")]
    [InlineData("unregistered fixture file", "S003")]
    [InlineData("case removed silently", "S004")]
    [InlineData("case reclassified silently", "S004")]
    [InlineData("baseline status relabeled silently", "S004")]
    [InlineData("case added silently", "S004")]
    [InlineData("amendment change from a wrong value", "S004")]
    [InlineData("amendment without PR", "S005")]
    [InlineData("scope version ahead of amendments", "S005")]
    [InlineData("unknown readiness status", "S006")]
    [InlineData("readiness vocabulary extended", "S006")]
    [InlineData("READY without commit", "S007")]
    [InlineData("READY without tests", "S007")]
    [InlineData("READY with failing tests", "S007")]
    [InlineData("READY with failed count missing", "S007")]
    [InlineData("READY with blank project", "S007")]
    [InlineData("READY without website examples", "S007")]
    [InlineData("READY with a missing example page", "S007")]
    [InlineData("READY without reviews", "S007")]
    [InlineData("READY with a missing review record", "S007")]
    [InlineData("READY without approver", "S007")]
    [InlineData("READY claiming full independence", "S007")]
    [InlineData("READY with unresolved 1423", "S007")]
    [InlineData("READY with a failed case", "S007")]
    [InlineData("READY with a missing case", "S007")]
    [InlineData("READY made stale by a later amendment", "S007")]
    [InlineData("BLOCKED without reason", "S008")]
    [InlineData("IN_PROGRESS without PR", "S008")]
    [InlineData("MET while 1413 is open", "S009")]
    [InlineData("MET without the gate amendment", "S009")]
    [InlineData("FROZEN without acceptance", "S009")]
    [InlineData("ceiling accepted while proposed", "S009")]
    [InlineData("1413 condition removed", "S009")]
    [InlineData("capacity condition removed", "S009")]
    [InlineData("ceiling without unit", "S010")]
    [InlineData("paid spend above zero", "S010")]
    [InlineData("deviation recorded as full", "S011")]
    [InlineData("deviation lifted without amendment", "S011")]
    [InlineData("family without 1423 blocker", "S012")]
    [InlineData("F1 without 1427 blocker", "S012")]
    [InlineData("family without 1426 blocker", "S012")]
    [InlineData("baseline overstated", "S013")]
    [InlineData("observation not in results", "S013")]
    [InlineData("results edited", "S013")]
    [InlineData("generated output edited", "S013")]
    [InlineData("surface dropped from baseline", "S013")]
    public void NegativeControlFailsWithItsCode(string mutation, string code)
    {
        var scope = Scope();
        Func<string, byte[]?> read = ReadRepo;
        string? extraFixture = null;
        switch (mutation)
        {
            case "family missing": scope["families"]!.AsArray().Remove(Family(scope, "F3")); RemoveFamilyCases(scope, "F3"); break;
            case "family tracks another issue": Family(scope, "F2")["issue"] = 9999; break;
            case "unknown family":
                var extra = Family(scope, "F6").DeepClone(); extra["id"] = "F7"; scope["families"]!.AsArray().Add(extra);
                var c7 = Case(scope, "F6-REPORT-04").DeepClone(); c7["id"] = "F7-X"; c7["family"] = "F7";
                scope["cases"]!.AsArray().Add(c7); Register(scope, c7); break;
            case "duplicate case id": scope["cases"]!.AsArray().Add(Case(scope, "F5-ARRAY-03").DeepClone()); break;
            // These two also edit the frozen entry, so the control isolates S002 from S004.
            case "role outside vocabulary": SetBoth(scope, "F5-ARRAY-03", "role", "nice-to-have"); break;
            case "vocabulary extended": scope["vocabularies"]!["role"]!.AsArray().Add("bonus"); break;
            case "measured status without baseline": SetBoth(scope, "F6-REPORT-04", "baselineStatus", "reproduces"); break;
            case "fixture hash drift": read = p => p.EndsWith("F5-ARRAY-03.cs.txt") ? Encoding.UTF8.GetBytes("// edited") : ReadRepo(p); break;
            case "unregistered fixture file": extraFixture = Dir + "/fixtures/F9-NEW-01.cs.txt"; break;
            case "case removed silently": Remove(scope["cases"]!, "F2-INTERP-03"); break;
            case "case reclassified silently": Case(scope, "F4-ITER-01")["expected"] = "rejected"; break;
            case "baseline status relabeled silently": Case(scope, "F1-REFOUT-08")["baselineStatus"] = "control-passes"; break;
            case "case added silently": var n = Case(scope, "F6-REPORT-10").DeepClone(); n["id"] = "F6-REPORT-99"; scope["cases"]!.AsArray().Add(n); break;
            case "amendment change from a wrong value":
                Case(scope, "F4-ITER-01")["expected"] = "rejected";
                var w = Amendment("1.0.1", removed: null); w["changes"] = new JsonArray(Change("F4-ITER-01", "expected", "native", "rejected"));
                scope["amendments"]!.AsArray().Add(w); scope["scopeVersion"] = "1.0.1"; break;
            case "amendment without PR": var a = Amendment("1.0.1", removed: null); a["pr"] = 0; scope["amendments"]!.AsArray().Add(a); scope["scopeVersion"] = "1.0.1"; break;
            case "scope version ahead of amendments": scope["scopeVersion"] = "1.1.0"; break;
            case "unknown readiness status": Family(scope, "F2")["readiness"]!["status"] = "DONE"; break;
            case "readiness vocabulary extended": scope["vocabularies"]!["readiness"]!.AsArray().Add("REVIEWED"); break;
            case "READY without commit": Ready(scope)["candidate"]!["commit"] = "abc1234"; break;
            case "READY without tests": Ready(scope)["tests"] = new JsonArray(); break;
            case "READY with failing tests": Ready(scope)["tests"]![0]!["failed"] = 1; break;
            case "READY with failed count missing": Ready(scope)["tests"]![0]!.AsObject().Remove("failed"); break;
            case "READY with blank project": Ready(scope)["tests"]![0]!["project"] = " "; break;
            case "READY without website examples": Ready(scope)["websiteExamples"] = new JsonArray(); break;
            case "READY with a missing example page": Ready(scope)["websiteExamples"] = new JsonArray("website/content/guides/no-such-page.mdx"); break;
            case "READY without reviews": Ready(scope)["reviews"] = new JsonArray(); break;
            case "READY with a missing review record": Ready(scope)["reviews"]![0]!["record"] = Dir + "/reviews/no-such-round.md"; break;
            case "READY without approver": Ready(scope).AsObject().Remove("approver"); break;
            case "READY claiming full independence": Ready(scope)["independence"] = "independent"; break;
            case "READY with unresolved 1423": Ready(scope)["blockersResolved"]!.AsArray().RemoveAt(1); break;
            case "READY with a failed case": Ready(scope)["cases"]![0]!["result"] = "failed"; break;
            case "READY with a missing case": Ready(scope)["cases"] = new JsonArray(); break;
            case "READY made stale by a later amendment":
                Ready(scope); Case(scope, "F4-ITER-01")["expected"] = "rejected";
                var s = Amendment("1.0.1", removed: null); s["changes"] = new JsonArray(Change("F4-ITER-01", "expected", "preserved", "rejected"));
                scope["amendments"]!.AsArray().Add(s); scope["scopeVersion"] = "1.0.1"; break;
            case "BLOCKED without reason": Family(scope, "F2")["readiness"] = new JsonObject { ["status"] = "BLOCKED" }; break;
            case "IN_PROGRESS without PR": Family(scope, "F2")["readiness"] = new JsonObject { ["status"] = "IN_PROGRESS" }; break;
            case "MET while 1413 is open": scope["gateStatus"] = "MET"; break;
            case "MET without the gate amendment": MetWithoutAmendment(scope); break;
            case "FROZEN without acceptance": scope["status"] = "FROZEN"; scope["capacity"]!["status"] = "ACCEPTED"; break;
            case "ceiling accepted while proposed": scope["capacity"]!["ceilings"]![0]!["status"] = "ACCEPTED"; break;
            case "1413 condition removed": scope["gateConditions"]!.AsArray().RemoveAt(0); break;
            case "capacity condition removed": scope["gateConditions"]!.AsArray().RemoveAt(1); break;
            case "ceiling without unit": scope["capacity"]!["ceilings"]![0]!["unit"] = ""; break;
            case "paid spend above zero": Ceiling(scope, "paid-spend")["value"] = 50; break;
            case "deviation recorded as full": scope["independence"]!["recordValue"] = "full"; break;
            case "deviation lifted without amendment": scope["independence"]!["deviation"] = false; break;
            case "family without 1423 blocker": Family(scope, "F5")["blockers"] = new JsonArray(1426); break;
            case "F1 without 1427 blocker": Family(scope, "F1")["blockers"] = new JsonArray(1423); break;
            case "family without 1426 blocker": Family(scope, "F3")["blockers"] = new JsonArray(1423); break;
            case "baseline overstated": Case(scope, "F5-ARRAY-01")["baseline"]!["mcp-default"] = "native-match"; break;
            case "observation not in results": Case(scope, "F6-REPORT-01")["observations"]![0]!["equals"] = "(2 via the rescue path)"; break;
            case "results edited": read = p => p == Results ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ReadRepo(p)!) + " ") : ReadRepo(p); break;
            case "generated output edited": read = p => p.EndsWith("F5-ARRAY-01.mcp-default.calr.txt") ? Encoding.UTF8.GetBytes("edited") : ReadRepo(p); break;
            case "surface dropped from baseline": Case(scope, "F4-ITER-01")["baseline"]!.AsObject().Remove("mcp-default"); break;
            default: throw new ArgumentException(mutation);
        }
        var violations = Run(scope, read, extraFixture);
        Assert.True(violations.Count > 0 && violations.All(x => x.Code == code), $"{mutation}: {EvidenceContractTests.Describe(violations)}");
    }

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
        + "\n" + scope["evidenceSeal"]!["generated"]!.GetValue<string>())));

    private static JsonNode Family(JsonNode scope, string id) => scope["families"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Case(JsonNode scope, string id) => scope["cases"]!.AsArray().First(c => c!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Ceiling(JsonNode scope, string id) => scope["capacity"]!["ceilings"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;

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
        var a = Amendment("1.0.1", removed: null);
        a["removedCases"] = new JsonArray(ids.Select(i => (JsonNode)new JsonObject { ["id"] = i, ["lastStatus"] = "reproduces" }).ToArray());
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = "1.0.1";
    }

    private static void Register(JsonNode scope, JsonNode added)
    {
        var a = Amendment("1.0.1", removed: null);
        a["addedCases"] = new JsonArray(added.DeepClone());
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = "1.0.1";
    }

    private static JsonObject Amendment(string version, string? removed) => new()
    {
        ["version"] = version, ["dateUtc"] = "2026-10-10T00:00:00Z", ["pr"] = 1500, ["afterInspection"] = false,
        ["justification"] = "control",
        ["removedCases"] = removed is null ? new JsonArray() : new JsonArray(new JsonObject { ["id"] = removed, ["lastStatus"] = "reproduces" }),
    };

    private static JsonObject Change(string id, string field, string from, string to) =>
        new() { ["id"] = id, ["field"] = field, ["from"] = from, ["to"] = to };

    /// <summary>Everything a MET gate needs except the gate amendment.</summary>
    private static void MetWithoutAmendment(JsonNode scope)
    {
        scope["status"] = "FROZEN";
        scope["gateStatus"] = "MET";
        scope["acceptance"] = new JsonObject { ["mergeCommit"] = new string('1', 40), ["mergedAtUtc"] = "2026-10-10T00:00:00Z", ["pr"] = 1483 };
        scope["capacity"]!["status"] = "ACCEPTED";
        foreach (var c in scope["capacity"]!["ceilings"]!.AsArray()) c!["status"] = "ACCEPTED";
        foreach (var g in scope["gateConditions"]!.AsArray())
        {
            g!["state"] = "SATISFIED";
            g["evidence"] = "docs/plans/v0.25-interop-scope-and-baseline.md";
            g["closedAtUtc"] = "2026-10-09T00:00:00Z";
        }
        foreach (var role in new[] { "interaction", "doc-example" })
        {
            var row = Case(scope, "F6-REPORT-10").DeepClone();
            row["id"] = "X-" + role;
            row["role"] = role;
            scope["cases"]!.AsArray().Add(row.DeepClone());
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
                .Select(c => (JsonNode)new JsonObject { ["id"] = c!["id"]!.GetValue<string>(), ["result"] = "passed" }).ToArray()),
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
