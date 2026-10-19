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

    /// <summary>Each approved amendment's seal, in order. 1.0.1: F1 PR 2 (#943, PR #1571) re-ran F1's results.json.</summary>
    private static readonly (string Version, string Seal)[] AmendmentSeals =
    [
        ("1.0.1", "12a33773e2424210a04b7750c2bc3fdeabb74c0281bf6d2dd65e8485a66b27b8"),
    ];

    [Fact]
    public void AmendmentApprovalsMatchThePinnedSeals()
    {
        var register = Register();
        var amendments = register["amendments"]!.AsArray();
        Assert.Equal(AmendmentSeals, amendments.Select(a => (a!["version"]!.GetValue<string>(), a["approvalSeal"]!.GetValue<string>())).ToArray());
        Assert.Equal(AmendmentSeals[^1].Seal, T1RegisterValidator.ApprovalSeal(register));
    }

    [Fact]
    public void CommittedRegisterHoldsNoReadyRecord()
    {
        // No 0.25 candidate is frozen yet: READY waits for W1 (#1429) and the release-gap fixes.
        Assert.All(Register()["families"]!.AsArray(), f => Assert.NotEqual("READY", f!["readiness"]!.GetValue<string>()));
        Assert.All(Scope()["families"]!.AsArray(), f => Assert.NotEqual("READY", f!["readiness"]!["status"]!.GetValue<string>()));
    }

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
        ["READY claimed in the register only"] = ("T002", (r, _) => { Family(r, "F4")["readiness"] = "READY"; return null; }),
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
