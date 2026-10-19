using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using static Calor.Compiler.Tests.ReleaseGate.ReleaseAdjudicationGateTests;

namespace Calor.Compiler.Tests.ReleaseGate;

/// <summary>
/// D-GATE-SCOPE-PACKET (2026-10-19, scope amendment 1.4.0): for 0.25.x the release gate checks the frozen #1426
/// R0 scope packet, the T1 register and W1 records that consume it, and the 1.3.0 re-proof preconditions, and
/// reads #1430's terminal record (ACCEPTED or PARTIAL_READY; one row per family, re-proof check, release gap and
/// claim; a READY family cites its READY record under the family's evidence directory, per D-READY-EVIDENCE-DIRS).
/// The fixture copies the real committed packet. Each negative control asserts the exact set of violation codes.
/// </summary>
public class ReleaseGateScopePacketTests
{
    private const string ScopeDir = "docs/plans/evidence/v0.25-r0-1426";
    private const string Register = "docs/plans/evidence/v0.25-t1-1428/register.json";
    private const string W1 = "docs/plans/evidence/v0.25-w1-1429/records.json";
    private const string Record025 = "docs/plans/evidence/adjudication-1430/terminal-record.json";
    private const string Registry025 = "docs/plans/evidence/adjudication-1430/claim-registry.json";

    [Fact]
    public void CommittedMapNamesTheScopePacketFormat()
    {
        var entry = Entry(GateRepo.CommittedMap(), "0.25.x");
        Assert.Equal("not-yet-adjudicated", entry["status"]!.GetValue<string>());
        Assert.Equal("calor.v0.25-scope-packet/1426", entry["contractPacket"]!["format"]!.GetValue<string>());
        Assert.Equal($"{ScopeDir}/scope.json", entry["contractPacket"]!["contract"]!.GetValue<string>());
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
    [InlineData("READY record naming another candidate", "G008")]
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
                case "READY record outside the family directory": Row(r, "family:F1")["readyRecord"]!["path"] = "docs/plans/evidence/v0.25-f2-906/ready/F1.json"; break;
                case "READY record naming another candidate": r["mutateReady"] = "F4"; break;
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

    [Theory]
    [InlineData("scope changed after the candidate")]
    [InlineData("register changed after the candidate")]
    [InlineData("tampered generated output")]
    [InlineData("tampered T1 evidence")]
    public void PacketDefectsFail(string defect)
    {
        using var repo = Build(atAdjudication: r =>
        {
            switch (defect)
            {
                case "scope changed after the candidate": Append(r, $"{ScopeDir}/notes.md"); break;
                case "register changed after the candidate": Append(r, Register); break;
                case "tampered generated output": Append(r, Directory.GetFiles(Path.Combine(r.Root, ScopeDir, "generated")).Order(StringComparer.Ordinal).First()); break;
                case "tampered T1 evidence": Append(r, "docs/plans/evidence/v0.25-f1-943/results.json"); break;
            }
        });
        AssertCodes(repo.Run(repo.AllSurfaces()), "G005");
    }

    // ---- fixture ----

    private static GateRepo Build(Action<JsonObject>? record = null, Action<GateRepo>? atAdjudication = null, bool adjudicated = true)
    {
        var map = GateRepo.CommittedMap();
        if (adjudicated) Entry(map, "0.25.x")["status"] = "adjudicated";
        var root = RepoRoot();
        var scope = JsonNode.Parse(File.ReadAllText(Path.Combine(root, ScopeDir, "scope.json")))!;
        var registerNode = JsonNode.Parse(File.ReadAllText(Path.Combine(root, Register)))!;
        var families = scope["families"]!.AsArray().Select(f => (Id: f!["id"]!.GetValue<string>(),
            Dir: Path.GetDirectoryName(f["readiness"]!["evidence"]!.GetValue<string>())!.Replace('\\', '/'))).ToList();
        return GateRepo.Build(version: "0.25.0", recordPath: Record025, issue: 1430, registryPath: Registry025,
            website: "<p>Calor 0.25.0</p>", map: map.ToJsonString(),
            prepare: repo =>
            {
                var paths = Directory.GetFiles(Path.Combine(root, ScopeDir), "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                    .Concat(new[] { Register, W1, registerNode["results"]!["path"]!.GetValue<string>() })
                    .Concat(registerNode["families"]!.AsArray().SelectMany(f => f!["evidence"]!.AsArray().Select(e => e!["path"]!.GetValue<string>())))
                    .Concat(registerNode["controls"]!.AsArray().Select(c => c!["fixture"]!.GetValue<string>()));
                foreach (var path in paths.Distinct())
                {
                    var target = Path.Combine(repo.Root, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(Path.Combine(root, path), target, overwrite: true);
                }
            },
            adjudicate: (repo, r) =>
            {
                atAdjudication?.Invoke(repo);
                var candidate = r["candidate"]!["commit"]!.GetValue<string>();
                r["outcome"] = "ACCEPTED";
                r["limitation"] = scope["independence"]!["statement"]!.DeepClone();
                r["contract"] = new JsonObject
                {
                    ["version"] = scope["scopeVersion"]!.DeepClone(),
                    ["files"] = new JsonObject(new[] { $"{ScopeDir}/scope.json", Register, W1 }.Select(p =>
                        KeyValuePair.Create(p, (JsonNode?)Sha(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root, p)).Replace("\r\n", "\n")))))),
                };
                var rows = new JsonArray();
                JsonObject Add(string subject, string outcome) { var row = new JsonObject { ["subject"] = subject, ["outcome"] = outcome, ["independence"] = "reduced-maintainer-adjudicated" }; rows.Add(row); return row; }
                foreach (var (id, dir) in families)
                {
                    var text = $"{{\"candidate\":{{\"commit\":\"{candidate}\"}},\"family\":\"{id}\"}}\n";
                    var path = $"{dir}/ready/{id}.json";
                    repo.Write(path, text);
                    Add($"family:{id}", "READY")["readyRecord"] = new JsonObject { ["path"] = path, ["sha256"] = Sha(Encoding.UTF8.GetBytes(text)) };
                }
                foreach (var check in scope["acceptancePreconditions"]!["reproof"]!["checks"]!.AsArray())
                    Add($"reproof:{check!["id"]!.GetValue<string>()}", "PASSED");
                foreach (var gap in scope["acceptancePreconditions"]!["releaseGaps"]!.AsArray())
                    Add($"gap:#{gap!["issue"]!.GetValue<int>()}", "CLOSED");
                foreach (var claim in GateRepo.Claims) Add(claim, "BOUNDED");
                r["adjudications"] = rows;
                record?.Invoke(r);
                if (r["mutateReady"] is { } family)
                {
                    r.Remove("mutateReady");
                    var dir = families.Single(f => f.Id == family.GetValue<string>()).Dir;
                    var text = "{\"candidate\":{\"commit\":\"" + new string('0', 40) + "\"}}\n";
                    repo.Write($"{dir}/ready/other.json", text);
                    Row(r, $"family:{family.GetValue<string>()}")["readyRecord"] = new JsonObject { ["path"] = $"{dir}/ready/other.json", ["sha256"] = Sha(Encoding.UTF8.GetBytes(text)) };
                }
            });
    }

    private static void Append(GateRepo repo, string path)
        => File.AppendAllText(Path.IsPathRooted(path) ? path : Path.Combine(repo.Root, path), "\nchanged after the candidate\n");

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
}
