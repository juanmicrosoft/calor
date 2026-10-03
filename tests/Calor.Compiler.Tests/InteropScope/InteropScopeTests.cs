using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.EvidenceContract;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1426 (0.25 R0) — the committed scope and baseline packet is valid, and the validator fails
/// closed. Each negative control changes one fact of the committed packet and asserts exactly the
/// expected violation code, so a validator that rejected everything would fail the positive
/// controls and one that accepted everything would fail the negative ones.
/// </summary>
public class InteropScopeTests
{
    /// <summary>
    /// SHA-256 of the canonical 1.0.0 denominator (denominatorV1). The denominator is frozen at
    /// 1.0.0; later scope changes are amendments, never edits to this list. A packet that drops a
    /// case from both the cases and the frozen list passes the validator but fails this pin.
    /// </summary>
    private const string DenominatorV1Seal = "4bc2bd22ab0125fdafd133d554ee615a4a4269377c76b7721a7822246773f80a";

    [Fact]
    public void CommittedPacketIsValid()
    {
        var violations = Run(Scope());
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    [Fact]
    public void DenominatorV1MatchesThePinnedSeal() => Assert.Equal(DenominatorV1Seal, Seal(Scope()));

    [Fact]
    public void ResealedPacketWithADroppedCaseFailsOnlyThePin()
    {
        var scope = Scope();
        Remove(scope["cases"]!, "F5-ARRAY-02");
        Remove(scope["denominatorV1"]!, "F5-ARRAY-02");
        Assert.Empty(Run(scope, withoutFixture: "F5-ARRAY-02.cs"));
        Assert.NotEqual(DenominatorV1Seal, Seal(scope));
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
        Family(scope, "F4")["readiness"] = ReadyRecord();
        var violations = Run(scope);
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    [Fact]
    public void AmendmentThatRetainsARemovedCasePasses()
    {
        var scope = Scope();
        Remove(scope["cases"]!, "F5-ARRAY-02");
        scope["amendments"]!.AsArray().Add(Amendment("1.0.1", removed: "F5-ARRAY-02"));
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
    [InlineData("measured status without baseline", "S002")]
    [InlineData("fixture hash drift", "S003")]
    [InlineData("case removed silently", "S004")]
    [InlineData("case reclassified silently", "S004")]
    [InlineData("amendment without PR", "S005")]
    [InlineData("scope version ahead of amendments", "S005")]
    [InlineData("unknown readiness status", "S006")]
    [InlineData("READY without commit", "S007")]
    [InlineData("READY without tests", "S007")]
    [InlineData("READY with failing tests", "S007")]
    [InlineData("READY without website examples", "S007")]
    [InlineData("READY without reviews", "S007")]
    [InlineData("READY without approver", "S007")]
    [InlineData("READY claiming full independence", "S007")]
    [InlineData("READY with unresolved 1423", "S007")]
    [InlineData("READY with a failed case", "S007")]
    [InlineData("READY with a missing case", "S007")]
    [InlineData("BLOCKED without reason", "S008")]
    [InlineData("IN_PROGRESS without PR", "S008")]
    [InlineData("MET while 1413 is open", "S009")]
    [InlineData("FROZEN without acceptance", "S009")]
    [InlineData("ceiling accepted while proposed", "S009")]
    [InlineData("1413 condition removed", "S009")]
    [InlineData("ceiling without unit", "S010")]
    [InlineData("paid spend above zero", "S010")]
    [InlineData("deviation recorded as full", "S011")]
    [InlineData("family without 1423 blocker", "S012")]
    [InlineData("F1 without 1427 blocker", "S012")]
    [InlineData("baseline overstated", "S013")]
    [InlineData("observation not in results", "S013")]
    public void NegativeControlFailsWithItsCode(string mutation, string code)
    {
        var scope = Scope();
        Func<string, byte[]?>? read = null;
        switch (mutation)
        {
            case "family missing": scope["families"]!.AsArray().Remove(Family(scope, "F3")); Remove(scope["cases"]!, "F3-LOCAL-01", "F3-LOCAL-02"); Keep(scope, "F3-LOCAL-01", "F3-LOCAL-02"); break;
            case "family tracks another issue": Family(scope, "F2")["issue"] = 9999; break;
            case "unknown family": var extra = Family(scope, "F6").DeepClone(); extra["id"] = "F7"; scope["families"]!.AsArray().Add(extra); var c7 = Case(scope, "F6-REPORT-04").DeepClone(); c7["id"] = "F7-X"; c7["family"] = "F7"; scope["cases"]!.AsArray().Add(c7); break;
            case "duplicate case id": scope["cases"]!.AsArray().Add(Case(scope, "F5-ARRAY-03").DeepClone()); break;
            case "role outside vocabulary": Case(scope, "F5-ARRAY-03")["role"] = "nice-to-have"; break;
            case "measured status without baseline": Case(scope, "F6-REPORT-04")["baselineStatus"] = "reproduces"; break;
            case "fixture hash drift": read = p => p.EndsWith("F5-ARRAY-03.cs") ? Encoding.UTF8.GetBytes("// edited") : ReadRepo(p); break;
            case "case removed silently": Remove(scope["cases"]!, "F2-INTERP-03"); break;
            case "case reclassified silently": Case(scope, "F4-ITER-01")["expected"] = "rejected"; break;
            case "amendment without PR": var a = Amendment("1.0.1", removed: null); a["pr"] = 0; scope["amendments"]!.AsArray().Add(a); scope["scopeVersion"] = "1.0.1"; break;
            case "scope version ahead of amendments": scope["scopeVersion"] = "1.1.0"; break;
            case "unknown readiness status": Family(scope, "F2")["readiness"]!["status"] = "DONE"; break;
            case "READY without commit": Ready(scope)["candidate"]!["commit"] = "abc1234"; break;
            case "READY without tests": Ready(scope)["tests"] = new JsonArray(); break;
            case "READY with failing tests": Ready(scope)["tests"]![0]!["failed"] = 1; break;
            case "READY without website examples": Ready(scope)["websiteExamples"] = new JsonArray(); break;
            case "READY without reviews": Ready(scope)["reviews"] = new JsonArray(); break;
            case "READY without approver": Ready(scope).AsObject().Remove("approver"); break;
            case "READY claiming full independence": Ready(scope)["independence"] = "independent"; break;
            case "READY with unresolved 1423": Ready(scope)["blockersResolved"]!.AsArray().RemoveAt(1); break;
            case "READY with a failed case": Ready(scope)["cases"]![0]!["result"] = "failed"; break;
            case "READY with a missing case": Ready(scope)["cases"] = new JsonArray(); break;
            case "BLOCKED without reason": Family(scope, "F2")["readiness"] = new JsonObject { ["status"] = "BLOCKED" }; break;
            case "IN_PROGRESS without PR": Family(scope, "F2")["readiness"] = new JsonObject { ["status"] = "IN_PROGRESS" }; break;
            case "MET while 1413 is open": scope["gateStatus"] = "MET"; break;
            case "FROZEN without acceptance": scope["status"] = "FROZEN"; Accept(scope); break;
            case "ceiling accepted while proposed": scope["capacity"]!["ceilings"]![0]!["status"] = "ACCEPTED"; break;
            case "1413 condition removed": scope["gateConditions"]!.AsArray().RemoveAt(0); break;
            case "ceiling without unit": scope["capacity"]!["ceilings"]![0]!["unit"] = ""; break;
            case "paid spend above zero": Ceiling(scope, "paid-spend")["value"] = 50; break;
            case "deviation recorded as full": scope["independence"]!["recordValue"] = "full"; break;
            case "family without 1423 blocker": Family(scope, "F5")["blockers"] = new JsonArray(1426); break;
            case "F1 without 1427 blocker": Family(scope, "F1")["blockers"] = new JsonArray(1423); break;
            case "baseline overstated": Case(scope, "F5-ARRAY-01")["baseline"]!["mcp-default"] = "native-match"; break;
            case "observation not in results": Case(scope, "F6-REPORT-01")["observations"]![0]!["equals"] = "(2 via the rescue path)"; break;
            default: throw new ArgumentException(mutation);
        }
        var violations = Run(scope, read);
        Assert.True(violations.Count > 0 && violations.All(x => x.Code == code), $"{mutation}: {EvidenceContractTests.Describe(violations)}");
    }

    [Fact]
    public void UnregisteredFixtureFileFails()
    {
        var violations = Run(Scope(), extraFixture: InteropScopeValidator.PacketDir + "/fixtures/F9-NEW-01.cs");
        Assert.Single(violations);
        Assert.Equal("S003", violations[0].Code);
    }

    // ------------------------------------------------------------------

    private static IReadOnlyList<ContractViolation> Run(JsonNode scope, Func<string, byte[]?>? read = null,
        string? extraFixture = null, string? withoutFixture = null)
    {
        var fixtures = Directory.EnumerateFiles(Path.Combine(EvidenceContractTests.RepoRoot(), InteropScopeValidator.PacketDir, "fixtures"))
            .Where(f => Path.GetFileName(f) != ".DS_Store" && Path.GetFileName(f) != withoutFixture)
            .Select(f => Path.GetRelativePath(EvidenceContractTests.RepoRoot(), f).Replace('\\', '/'))
            .Concat(extraFixture is null ? [] : [extraFixture]).ToList();
        return InteropScopeValidator.Validate(scope, Load("baseline-results.json"), read ?? ReadRepo, fixtures);
    }

    private static byte[]? ReadRepo(string path)
    {
        var full = Path.Combine(EvidenceContractTests.RepoRoot(), path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    private static JsonNode Load(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(), InteropScopeValidator.PacketDir, name)))!;

    private static JsonNode Scope() => Load("scope.json");

    private static string Seal(JsonNode scope) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(scope["denominatorV1"]!.ToJsonString())));

    private static JsonNode Family(JsonNode scope, string id) => scope["families"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Case(JsonNode scope, string id) => scope["cases"]!.AsArray().First(c => c!["id"]!.GetValue<string>() == id)!;
    private static JsonNode Ceiling(JsonNode scope, string id) => scope["capacity"]!["ceilings"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;

    private static void Remove(JsonNode list, params string[] ids)
    {
        foreach (var node in list.AsArray().Where(c => ids.Contains(c!["id"]!.GetValue<string>())).ToList())
            list.AsArray().Remove(node);
    }

    /// <summary>Retains removed cases through an amendment, so a family-removal control isolates S001.</summary>
    private static void Keep(JsonNode scope, params string[] ids)
    {
        var a = Amendment("1.0.1", removed: null);
        a["removedCases"] = new JsonArray(ids.Select(i => (JsonNode)new JsonObject { ["id"] = i, ["lastStatus"] = "reproduces" }).ToArray());
        scope["amendments"]!.AsArray().Add(a);
        scope["scopeVersion"] = "1.0.1";
    }

    private static JsonObject Amendment(string version, string? removed) => new()
    {
        ["version"] = version, ["dateUtc"] = "2026-10-10T00:00:00Z", ["pr"] = 1500, ["afterInspection"] = false,
        ["justification"] = "control",
        ["removedCases"] = removed is null ? new JsonArray() : new JsonArray(new JsonObject { ["id"] = removed, ["lastStatus"] = "reproduces" }),
    };

    private static void Accept(JsonNode scope) => scope["capacity"]!["status"] = "ACCEPTED";

    private static JsonNode Ready(JsonNode scope)
    {
        var record = ReadyRecord();
        Family(scope, "F4")["readiness"] = record;
        return record;
    }

    private static JsonObject ReadyRecord() => new()
    {
        ["status"] = "READY",
        ["candidate"] = new JsonObject { ["commit"] = "1111111111111111111111111111111111111111" },
        ["cases"] = new JsonArray(new JsonObject { ["id"] = "F4-ITER-01", ["result"] = "passed" }),
        ["tests"] = new JsonArray(new JsonObject { ["project"] = "tests/Calor.Conversion.Tests", ["total"] = 12, ["failed"] = 0 }),
        ["websiteExamples"] = new JsonArray("website/content/guides/interop.mdx#iterator-accessors"),
        ["reviews"] = new JsonArray(new JsonObject { ["reviewer"] = "codex", ["record"] = "docs/plans/evidence/x/reviews/round-1.md" }),
        ["approver"] = new JsonObject { ["login"] = "juanmicrosoft", ["approvalUrl"] = "https://github.com/juanmicrosoft/calor/pull/1#pullrequestreview-1" },
        ["independence"] = "reduced",
        ["blockersResolved"] = new JsonArray(
            new JsonObject { ["issue"] = 1426, ["reference"] = "closed by PR #1" },
            new JsonObject { ["issue"] = 1423, ["reference"] = "candidate manifest commit" }),
    };
}
