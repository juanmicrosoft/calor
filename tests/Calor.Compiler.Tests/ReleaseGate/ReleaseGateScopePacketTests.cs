using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using static Calor.Compiler.Tests.ReleaseGate.ReleaseAdjudicationGateTests;

namespace Calor.Compiler.Tests.ReleaseGate;

/// <summary>
/// D-GATE-SCOPE-PACKET (2026-10-19, scope amendment 1.4.0): for 0.25.x the release gate checks the frozen #1426
/// R0 scope packet, the T1 register and W1 records that consume it, and the 1.3.0 re-proof preconditions (S014), and
/// reads #1430's terminal record (ACCEPTED or PARTIAL_READY; one row per family, re-proof check, release gap and
/// claim; a READY family cites a complete S007 READY record under the family's evidence directory's ready/, per
/// D-READY-EVIDENCE-DIRS). The fixture copies the real committed packet. Each negative control asserts the exact
/// set of codes, and packet and READY controls also the one message they target.
/// </summary>
public class ReleaseGateScopePacketTests
{
    private const string ScopeDir = "docs/plans/evidence/v0.25-r0-1426";
    private const string Scope = ScopeDir + "/scope.json";
    private const string Register = "docs/plans/evidence/v0.25-t1-1428/register.json";
    private const string W1 = "docs/plans/evidence/v0.25-w1-1429/records.json";
    private const string Record025 = "docs/plans/evidence/adjudication-1430/terminal-record.json";
    private const string Registry025 = "docs/plans/evidence/adjudication-1430/claim-registry.json";
    private const string Page = "website/content/guides/csharp-interoperability.mdx";

    [Fact]
    public void CommittedMapNamesTheScopePacketFormat()
    {
        var entry = Entry(GateRepo.CommittedMap(), "0.25.x");
        Assert.Equal("not-yet-adjudicated", entry["status"]!.GetValue<string>());
        Assert.Equal("calor.v0.25-scope-packet/1426", entry["contractPacket"]!["format"]!.GetValue<string>());
        Assert.Equal(Scope, entry["contractPacket"]!["contract"]!.GetValue<string>());
    }

    [Fact]
    public void AcceptedRecordUnderTheScopePacketPasses()
    {
        using var repo = Build();
        var (code, output) = repo.Run(repo.AllSurfaces());
        Assert.True(code == 0, output);
        Assert.Contains("version=0.25.0", output);
    }

    [Fact]
    public void PartialReadyWithAPendingFamilyPasses()
    {
        using var repo = Build(record: r => { r["outcome"] = "PARTIAL_READY"; Row(r, "family:F2")["outcome"] = "PENDING"; ((JsonObject)Row(r, "family:F2")).Remove("readyRecord"); });
        var (code, output) = repo.Run(repo.AllSurfaces());
        Assert.True(code == 0, output);
    }

    [Fact]
    public void NotYetAdjudicatedStillFails()
    {
        using var repo = Build(adjudicated: false);
        AssertCodes(repo.Run(repo.AllSurfaces()), "G016");
    }

    [Theory]
    [InlineData("BLOCKED outcome", "G006")]
    [InlineData("MILESTONE-SUCCEEDED outcome", "G006")]
    [InlineData("PENDING in ACCEPTED", "G008")]
    [InlineData("READY without its record", "G008")]
    [InlineData("READY record outside the family directory", "G008")]
    [InlineData("missing re-proof row", "G008")]
    [InlineData("gap not closed", "G008")]
    [InlineData("claim SUPPORTED", "G008")]
    [InlineData("unknown subject", "G008")]
    [InlineData("independence not reduced", "G007")]
    [InlineData("limitation rewritten", "G007")]
    [InlineData("contract hash", "G005")]
    [InlineData("contract version", "G005")]
    public void RecordDefectsFail(string defect, string expected)
    {
        using var repo = Build(record: r =>
        {
            switch (defect)
            {
                case "BLOCKED outcome": r["outcome"] = "BLOCKED"; break;
                case "MILESTONE-SUCCEEDED outcome": r["outcome"] = "MILESTONE-SUCCEEDED"; break;
                case "PENDING in ACCEPTED": Row(r, "family:F3")["outcome"] = "PENDING"; break;
                case "READY without its record": ((JsonObject)Row(r, "family:F1")).Remove("readyRecord"); break;
                case "READY record outside the family directory": Row(r, "family:F1")["readyRecord"] = Row(r, "family:F2")["readyRecord"]!.DeepClone(); break;
                case "missing re-proof row": Remove(r, "reproof:provenance-determinism"); break;
                case "gap not closed": Row(r, "gap:#1545")["outcome"] = "OPEN"; break;
                case "claim SUPPORTED": Row(r, GateRepo.Claims[0])["outcome"] = "SUPPORTED"; break;
                case "unknown subject": ((JsonArray)r["adjudications"]!).Add(new JsonObject { ["subject"] = "family:F7", ["outcome"] = "READY", ["independence"] = "reduced-maintainer-adjudicated" }); break;
                case "independence not reduced": r["adjudicationIndependence"] = "independent"; break;
                case "limitation rewritten": r["limitation"] = "Reviewed."; break;
                case "contract hash": r["contract"]!["files"]![Register] = new string('0', 64); break;
                case "contract version": r["contract"]!["version"] = "1.3.0"; break;
            }
        });
        AssertCodes(repo.Run(repo.AllSurfaces()), expected);
    }

    /// <summary>S007 for a READY record kept under ready/: each control breaks one field of F1's record.</summary>
    [Theory]
    [InlineData("family", "name its family")]
    [InlineData("candidate", "candidate.commit")]
    [InlineData("case missing", "case F1-REFOUT-01 needs")]
    [InlineData("case requirement", "case F1-REFOUT-02 needs")]
    [InlineData("case cell", "case X-INT-01 needs")]
    [InlineData("tests failed", "tests must")]
    [InlineData("website page missing", "websiteExamples")]
    [InlineData("review record missing", "reviews must")]
    [InlineData("approval URL", "approver needs")]
    [InlineData("independence", "independence must")]
    [InlineData("blocker unresolved", "blocker #1427")]
    [InlineData("witness run", "mapped blocker R-OBL ")]
    [InlineData("stale scope version", "is stale")]
    [InlineData("duplicate passing case row", "listed more than once")]
    [InlineData("failed first case row with a passing duplicate", "F1-REFOUT-03")]
    [InlineData("boolean test total", "tests must")]
    [InlineData("blank reviewer", "reviews must")]
    public void IncompleteReadyRecordsFail(string defect, string message)
    {
        using var repo = Build(readyEdit: (family, ready, files) =>
        {
            if (family != "F1") return;
            switch (defect)
            {
                case "family": ready["family"] = "F2"; break;
                case "candidate": ready["candidate"] = new JsonObject { ["commit"] = new string('0', 40) }; break;
                case "case missing": ready["cases"]!.AsArray().Remove(ready["cases"]!.AsArray().First(c => c!["id"]!.GetValue<string>() == "F1-REFOUT-01")); break;
                case "case requirement": Edit(files, "F1-REFOUT-02", e => e["requirementSha256"] = new string('0', 64)); break;
                case "case cell": Edit(files, "X-INT-01", e => e["cells"]!["mcp-default"] = "failed"); break;
                case "tests failed": ready["tests"]![0]!["failed"] = 1; break;
                case "website page missing": ready["websiteExamples"] = new JsonArray("website/content/no-such-page.mdx"); break;
                case "review record missing": ready["reviews"]![0]!["record"] = "docs/plans/evidence/v0.25-f1-943/reviews/no-such.md"; break;
                case "approval URL": ready["approver"]!["approvalUrl"] = "https://example.invalid/approved"; break;
                case "independence": ready["independence"] = "independent"; break;
                case "blocker unresolved": ready["blockersResolved"]!.AsArray().Remove(ready["blockersResolved"]!.AsArray().First(b => b!["issue"]!.GetValue<int>() == 1427)); break;
                case "witness run": Edit(files, "witness-1496", e => e["result"] = "failed"); break;
                case "stale scope version": ready["scopeVersion"] = "1.0.0"; break;
                case "duplicate passing case row" or "failed first case row with a passing duplicate":
                    var row = ready["cases"]!.AsArray().First(c => c!["id"]!.GetValue<string>() == "F1-REFOUT-03")!.DeepClone();
                    if (defect.StartsWith("failed", StringComparison.Ordinal)) row["result"] = "failed";
                    ready["cases"]!.AsArray().Insert(0, row);
                    break;
                case "boolean test total": ready["tests"]![0]!["total"] = true; break;
                case "blank reviewer": ready["reviews"]![0]!["reviewer"] = "  "; break;
            }
        });
        AssertOnly(repo.Run(repo.AllSurfaces()), "G008", message);
    }

    [Theory]
    [InlineData("tampered generated output", false, "evidenceSeal")]
    [InlineData("one producer dropped", false, "evidenceSeal")] // the evidenceSeal check and the pinned denominator seal both cover producers
    [InlineData("W1 records without schema", false, "W1 records must be")]
    [InlineData("hollow W1 family records", false, "W1 records for")]
    [InlineData("T1 cases differ from the scope", false, "(T003)")]
    [InlineData("decision date not a date", false, "a recorded maintainer decision")]
    [InlineData("decision time not a time", false, "a recorded maintainer decision")]
    [InlineData("case removed from the scope and T1", false, "case denominator")]
    [InlineData("W1 anchor to a heading inside fenced code", false, "W1 records for F1")]
    [InlineData("precondition requirement weakened in both copies", false, "1.3.0's recorded preconditions")]
    [InlineData("precondition copy differs", false, "equal the copy")]
    [InlineData("tampered T1 evidence", false, "approval seal")]
    [InlineData("scope directory changed after the candidate", true, "contract packet changed between")]
    [InlineData("register changed after the candidate", true, "T1 register or W1 records changed between")]
    public void PacketDefectsFail(string defect, bool afterCandidate, string message)
    {
        void Mutate(GateRepo r)
        {
            switch (defect)
            {
                case "tampered generated output": Append(r, Directory.GetFiles(Path.Combine(r.Root, ScopeDir, "generated")).Order(StringComparer.Ordinal).First()); break;
                case "one producer dropped": EditJson(r, Scope, s => s["evidenceSeal"]!["producers"]!.AsObject().Remove($"{ScopeDir}/reproduce/ProbeRunner.cs.txt")); break;
                case "W1 records without schema": EditJson(r, W1, w => w.AsObject().Remove("schema")); break;
                case "hollow W1 family records":
                    EditJson(r, W1, w =>
                    {
                        foreach (var f in w["families"]!.AsArray())
                            foreach (var k in f!.AsObject().Select(p => p.Key).ToList()) if (k is not ("family" or "issue")) f.AsObject().Remove(k);
                    });
                    break;
                case "T1 cases differ from the scope": EditJson(r, Register, g => g["families"]![0]!["cases"]!.AsArray().RemoveAt(0)); break;
                case "decision date not a date":
                    EditJson(r, Scope, s => s["amendments"]!.AsArray().First(a => a!["version"]!.GetValue<string>() == "1.3.0")!["maintainerDecisions"]![0]!["date"] = "2026-99-99garbage");
                    break;
                case "precondition requirement weakened in both copies":
                    EditJson(r, Scope, s =>
                    {
                        foreach (var copy in new[] { s["acceptancePreconditions"]!, s["amendments"]!.AsArray().First(a => a!["version"]!.GetValue<string>() == "1.3.0")!["acceptancePreconditions"]! })
                            foreach (var c in copy["reproof"]!["checks"]!.AsArray()) c!["requirement"] = "No rerun needed.";
                    });
                    break;
                case "precondition copy differs": EditJson(r, Scope, s => s["acceptancePreconditions"]!["reproof"]!["checks"]![0]!["requirement"] = "No rerun needed."); break;
                case "tampered T1 evidence": Append(r, "docs/plans/evidence/v0.25-f1-943/results.json"); break;
                case "decision time not a time":
                    EditJson(r, Scope, s => s["amendments"]!.AsArray().First(a => a!["version"]!.GetValue<string>() == "1.3.0")!["maintainerDecisions"]![0]!["date"] = "2026-10-19T99:99:99Z");
                    break;
                case "case removed from the scope and T1":
                    EditJson(r, Scope, s => s["cases"]!.AsArray().Remove(s["cases"]!.AsArray().First(c => c!["id"]!.GetValue<string>() == "F1-REFOUT-01")));
                    EditJson(r, Register, g => g["families"]![0]!["cases"]!.AsArray().Remove(g["families"]![0]!["cases"]!.AsArray().First(c => c!["id"]!.GetValue<string>() == "F1-REFOUT-01")));
                    break;
                case "W1 anchor to a heading inside fenced code": AnchorF1(r, "\n```\n## Phantom Heading\n```\n", "phantom-heading"); break;
                case "scope directory changed after the candidate": Append(r, $"{ScopeDir}/notes.md"); break;
                case "register changed after the candidate": EditJson(r, Register, g => g["statusNote"] = "edited after the freeze"); break;
            }
        }
        using var repo = afterCandidate ? Build(atAdjudication: Mutate) : Build(beforeCandidate: Mutate);
        AssertOnly(repo.Run(repo.AllSurfaces()), "G005", message);
    }

    /// <summary>Anchors follow website/src/lib/headings.ts: a repeated heading's second id is "-1".</summary>
    [Fact]
    public void W1AnchorToARepeatedHeadingPasses()
    {
        using var repo = Build(beforeCandidate: r => AnchorF1(r, "\n## Repeated Heading\n\n## Repeated Heading\n", "repeated-heading-1"));
        var (code, output) = repo.Run(repo.AllSurfaces());
        Assert.True(code == 0, output);
    }

    /// <summary>Appends text to F1's first W1 page and points that page's anchor at an id.</summary>
    private static void AnchorF1(GateRepo repo, string append, string anchor)
    {
        string page = "";
        EditJson(repo, W1, w =>
        {
            page = w["families"]![0]!["pages"]![0]!.GetValue<string>().Split('#')[0];
            w["families"]![0]!["pages"]![0] = $"{page}#{anchor}";
        });
        File.AppendAllText(Path.Combine(repo.Root, page), append);
    }

    // ---- fixture ----

    private delegate void ReadyEdit(string family, JsonObject ready, Dictionary<string, JsonObject> files);

    private static GateRepo Build(Action<JsonObject>? record = null, Action<GateRepo>? beforeCandidate = null, Action<GateRepo>? atAdjudication = null,
        ReadyEdit? readyEdit = null, bool adjudicated = true)
    {
        var map = GateRepo.CommittedMap();
        if (adjudicated) Entry(map, "0.25.x")["status"] = "adjudicated";
        var root = RepoRoot();
        var scope = JsonNode.Parse(File.ReadAllText(Path.Combine(root, Scope)))!;
        var registerNode = JsonNode.Parse(File.ReadAllText(Path.Combine(root, Register)))!;
        var cells = scope["cases"]!.AsArray().ToDictionary(c => c!["id"]!.GetValue<string>(), c => c!["cells"]?.AsArray().Select(x => x!.GetValue<string>()).ToList() ?? new List<string>());
        var mapped = scope["mappedBlockers"]!["applicable"]!.AsArray();
        var w1Node = JsonNode.Parse(File.ReadAllText(Path.Combine(root, W1)))!;
        var w1Pages = w1Node["families"]!.AsArray().SelectMany(f => f!["pages"]!.AsArray()).Concat(w1Node["sharedPages"]!.AsArray())
            .Select(p => p!.GetValue<string>().Split('#')[0]).ToList();
        var families = scope["families"]!.AsArray().Select(f => (Id: f!["id"]!.GetValue<string>(), Blockers: f["blockers"]!.AsArray().Select(b => b!.GetValue<int>()).ToList(),
            Dir: Path.GetDirectoryName(f["readiness"]!["evidence"]!.GetValue<string>())!.Replace('\\', '/'))).ToList();
        return GateRepo.Build(version: "0.25.0", recordPath: Record025, issue: 1430, registryPath: Registry025,
            website: "<p>Calor 0.25.0</p>", map: map.ToJsonString(),
            prepare: repo =>
            {
                var paths = Directory.GetFiles(Path.Combine(root, ScopeDir), "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                    .Concat(new[] { Register, W1, registerNode["results"]!["path"]!.GetValue<string>(), Page,
                        "docs/plans/evidence/adjudication-1408/terminal-record.json", "docs/plans/evidence/s2-1413/dispositions.json",
                        "docs/plans/v0.24-c1-candidate.md", "docs/plans/evidence/c2-1424/regeneration.md", "scripts/determinism_protocol.py",
                        "docs/plans/evidence/r2-1410/release-gate.md" })
                    .Concat(registerNode["families"]!.AsArray().SelectMany(f => f!["evidence"]!.AsArray().Select(e => e!["path"]!.GetValue<string>())))
                    .Concat(registerNode["controls"]!.AsArray().Select(c => c!["fixture"]!.GetValue<string>()))
                    .Concat(Directory.GetFiles(Path.Combine(root, "docs/plans/evidence/v0.25-w1-1429/transcripts")).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')))
                    .Concat(w1Pages);
                foreach (var path in paths.Distinct())
                {
                    var target = Path.Combine(repo.Root, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(Path.Combine(root, path), target, overwrite: true);
                }
                beforeCandidate?.Invoke(repo);
            },
            adjudicate: (repo, r) =>
            {
                atAdjudication?.Invoke(repo);
                var candidate = r["candidate"]!["commit"]!.GetValue<string>();
                r["outcome"] = "ACCEPTED";
                r["limitation"] = scope["independence"]!["statement"]!.DeepClone();
                r["contract"] = new JsonObject
                {
                    ["version"] = JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Root, Scope)))!["scopeVersion"]!.DeepClone(),
                    ["files"] = new JsonObject(new[] { Scope, Register, W1 }.Select(p =>
                        KeyValuePair.Create(p, (JsonNode?)Sha(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(repo.Root, p)).Replace("\r\n", "\n")))))),
                };
                var rows = new JsonArray();
                JsonObject Add(string subject, string outcome) { var row = new JsonObject { ["subject"] = subject, ["outcome"] = outcome, ["independence"] = "reduced-maintainer-adjudicated" }; rows.Add(row); return row; }
                foreach (var (id, blockers, dir) in families)
                {
                    var ready = dir + "/ready";
                    var files = new Dictionary<string, JsonObject>();
                    var caseRows = new JsonArray();
                    foreach (var c in registerNode["families"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == id)!["cases"]!.AsArray())
                    {
                        var cid = c!["id"]!.GetValue<string>();
                        files[cid] = new JsonObject { ["candidate"] = candidate, ["caseId"] = cid, ["requirementSha256"] = c["requirementSha256"]!.DeepClone(), ["result"] = "passed",
                            ["cells"] = new JsonObject(cells[cid].Select(cell => KeyValuePair.Create(cell, (JsonNode?)"passed"))) };
                        caseRows.Add(new JsonObject { ["id"] = cid, ["result"] = "passed", ["evidence"] = $"{ready}/cases/{cid}.json" });
                    }
                    var resolved = new JsonArray();
                    foreach (var b in blockers)
                    {
                        var res = new JsonObject { ["issue"] = b, ["reference"] = $"#{b}" };
                        if (mapped.FirstOrDefault(m => m!["pr"]!.GetValue<int>() == b) is { } m)
                        {
                            files[$"witness-{b}"] = new JsonObject { ["candidate"] = candidate, ["repair"] = m["repair"]!.DeepClone(), ["witnesses"] = m["regressionWitness"]!.DeepClone(), ["result"] = "passed" };
                            res["mergeCommit"] = m["mergeCommit"]!.DeepClone();
                            res["witnessRun"] = $"{ready}/cases/witness-{b}.json";
                        }
                        resolved.Add(res);
                    }
                    repo.Write($"{ready}/reviews/codex.md", "review\n");
                    var record = new JsonObject
                    {
                        ["family"] = id, ["scopeVersion"] = scope["scopeVersion"]!.DeepClone(), ["candidate"] = new JsonObject { ["commit"] = candidate },
                        ["cases"] = caseRows, ["tests"] = new JsonArray(new JsonObject { ["project"] = "tests/Calor.Compiler.Tests", ["total"] = 10, ["failed"] = 0 }),
                        ["websiteExamples"] = new JsonArray(Page + "#anchor"), ["reviews"] = new JsonArray(new JsonObject { ["reviewer"] = "Codex", ["record"] = $"{ready}/reviews/codex.md" }),
                        ["approver"] = new JsonObject { ["login"] = "juanmicrosoft", ["approvalUrl"] = "https://github.com/juanmicrosoft/calor/pull/1#pullrequestreview-2" },
                        ["independence"] = "reduced", ["blockersResolved"] = resolved,
                    };
                    readyEdit?.Invoke(id, record, files);
                    foreach (var (name, body) in files) repo.Write($"{ready}/cases/{name}.json", body.ToJsonString());
                    var text = record.ToJsonString();
                    repo.Write($"{ready}/{id}.json", text);
                    Add($"family:{id}", "READY")["readyRecord"] = new JsonObject { ["path"] = $"{ready}/{id}.json", ["sha256"] = Sha(Encoding.UTF8.GetBytes(text)) };
                }
                foreach (var check in scope["acceptancePreconditions"]!["reproof"]!["checks"]!.AsArray())
                    Add($"reproof:{check!["id"]!.GetValue<string>()}", "PASSED");
                foreach (var gap in scope["acceptancePreconditions"]!["releaseGaps"]!.AsArray())
                    Add($"gap:#{gap!["issue"]!.GetValue<int>()}", "CLOSED");
                foreach (var claim in GateRepo.Claims) Add(claim, "BOUNDED");
                r["adjudications"] = rows;
                record?.Invoke(r);
            });
    }

    private static void Edit(Dictionary<string, JsonObject> files, string name, Action<JsonObject> edit) => edit(files[name]);

    private static void EditJson(GateRepo repo, string path, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Root, path)))!;
        edit(node);
        repo.Write(path, node.ToJsonString());
    }

    private static void Append(GateRepo repo, string path)
        => File.AppendAllText(Path.IsPathRooted(path) ? path : Path.Combine(repo.Root, path), "\nchanged\n");

    private static JsonNode Row(JsonObject record, string subject)
        => record["adjudications"]!.AsArray().Single(r => r!["subject"]!.GetValue<string>() == subject)!;

    private static void Remove(JsonObject record, string subject)
        => record["adjudications"]!.AsArray().Remove(Row(record, subject));

    private static JsonObject Entry(JsonObject map, string version) =>
        map["releases"]!.AsArray().Single(e => e!["version"]!.GetValue<string>() == version)!.AsObject();

    private static void AssertCodes((int Code, string Output) result, params string[] expected)
    {
        var actual = Regex.Matches(result.Output, @"release gate \(#1410\) (G\d{3}):").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(expected.ToHashSet().SetEquals(actual),
            $"expected codes [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]\n{result.Output}");
        Assert.Equal(1, result.Code);
    }

    /// <summary>Exactly one code, and every violation line names the targeted guard (so each control isolates it).</summary>
    private static void AssertOnly((int Code, string Output) result, string code, string message)
    {
        AssertCodes(result, code);
        var lines = result.Output.Split('\n').Where(l => l.Contains($"release gate (#1410) {code}:", StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count > 0 && lines.All(l => l.Contains(message, StringComparison.Ordinal)), $"expected only '{message}':\n{result.Output}");
    }
}
