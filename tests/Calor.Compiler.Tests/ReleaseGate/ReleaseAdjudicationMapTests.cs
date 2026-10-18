using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using static Calor.Compiler.Tests.ReleaseGate.ReleaseAdjudicationGateTests;

namespace Calor.Compiler.Tests.ReleaseGate;

/// <summary>
/// #1567: the release gate reads which A1 issue, terminal record, contract packet, and claim
/// registry apply to a release from <c>eng/release-adjudication-map.json</c>, keyed by version,
/// instead of 0.24 constants. 0.24.0 still resolves to its MILESTONE-FAILED #1408 record and its
/// maintainer override; 0.25.x points at #1430's future paths and fails until a reviewed change
/// marks it adjudicated. Each negative control asserts the exact set of violation codes.
/// </summary>
public class ReleaseAdjudicationMapTests
{
    private const string MapFile = "eng/release-adjudication-map.json";
    private const string OverridePath = "docs/plans/evidence/adjudication-1408/maintainer-override.json";
    private const string Record025 = "docs/plans/evidence/adjudication-1430/terminal-record.json";
    private const string Registry025 = "docs/plans/evidence/adjudication-1430/claim-registry.json";

    // ------------------------------------------------------------------
    // 0.24.0 keeps resolving
    // ------------------------------------------------------------------

    [Fact]
    public void Release024ResolvesToItsFailedRecordAndTheMaintainerOverride()
    {
        var root = RepoRoot();
        var entry = Entry(GateRepo.CommittedMap(), "0.24.0");
        Assert.Equal("adjudicated", entry["status"]!.GetValue<string>());
        Assert.Equal(1408, entry["a1Issue"]!.GetValue<int>());
        Assert.Equal(RecordPath, entry["terminalRecord"]!.GetValue<string>());
        Assert.Equal(OverridePath, entry["maintainerOverride"]!.GetValue<string>());
        Assert.Equal(PacketDir, entry["contractPacket"]!["dir"]!.GetValue<string>());
        Assert.Equal($"{PacketDir}/sha256.json", entry["contractPacket"]!["seal"]!.GetValue<string>());

        // The committed record is the one the override names, it failed, and it names the registry
        // the map binds.
        var recordBytes = File.ReadAllBytes(Path.Combine(root, RecordPath));
        var record = JsonNode.Parse(recordBytes)!;
        var over = JsonNode.Parse(File.ReadAllText(Path.Combine(root, OverridePath)))!;
        Assert.Equal(over["terminalRecord"]!["sha256"]!.GetValue<string>(), Sha(recordBytes));
        Assert.Equal("MILESTONE-FAILED", record["outcome"]!.GetValue<string>());
        Assert.Equal(1408, record["issue"]!.GetValue<int>());
        Assert.Equal(entry["claimRegistry"]!.GetValue<string>(), record["claimRegistry"]!["path"]!.GetValue<string>());
        foreach (var key in new[] { "contract", "inventory", "seal" })
            Assert.True(File.Exists(Path.Combine(root, entry["contractPacket"]![key]!.GetValue<string>())), key);
    }

    [Fact]
    public void FailedRecordForRelease024StillFailsThroughTheMap()
    {
        using var repo = GateRepo.Build(record: r => r["outcome"] = "MILESTONE-FAILED");
        AssertCodes(repo.Run(repo.AllSurfaces()), "G006");
    }

    // ------------------------------------------------------------------
    // Versions and entries the map does not support
    // ------------------------------------------------------------------

    [Fact]
    public void RecordForAVersionMissingFromTheMapFails()
    {
        // A version problem, so G009, like any other candidate-version failure.
        using var repo = GateRepo.Build(version: "0.26.0");
        var result = repo.Run(Array.Empty<string>());
        AssertCodes(result, "G009");
        Assert.Contains("candidate version 0.26.0 is not in release-adjudication-map.json", result.Output);
        AssertCodes(repo.Run(repo.AllSurfaces()), "G009");
    }

    [Fact]
    public void RequestedVersionMissingFromTheMapFails()
    {
        using var repo = GateRepo.Build();
        var result = repo.Run(new[] { "--version", "0.24.1" });
        AssertCodes(result, "G009");
        Assert.Contains("requested version 0.24.1 is not in release-adjudication-map.json", result.Output);
    }

    [Fact]
    public void MapEntryPointingAtAMissingRecordFails()
    {
        using var repo = GateRepo.Build();
        var map = GateRepo.CommittedMap();
        Entry(map, "0.24.0")["terminalRecord"] = "docs/plans/evidence/adjudication-1408/terminal-record-v2.json";
        AssertCodes(repo.RunWithMap(map), "G004");
    }

    [Fact]
    public void RecordAtAnotherReleasesPathFails()
    {
        // A 0.25.0 record committed at 0.24.0's record path.
        using var repo = GateRepo.Build(version: "0.25.0");
        AssertCodes(repo.Run(Array.Empty<string>()), "G016");
    }

    [Fact]
    public void RecordNamingAnotherClaimRegistryThanTheMapFails()
    {
        using var repo = GateRepo.Build(registryPath: "docs/plans/evidence/regeneration-1424/claim-registry.json");
        AssertCodes(repo.Run(Array.Empty<string>()), "G008");
    }

    // ------------------------------------------------------------------
    // 0.25.x fails until it is adjudicated
    // ------------------------------------------------------------------

    [Fact]
    public void Release025IsMappedToTheFutureA1PathsAndIsNotYetAdjudicated()
    {
        var entry = Entry(GateRepo.CommittedMap(), "0.25.x");
        Assert.Equal("not-yet-adjudicated", entry["status"]!.GetValue<string>());
        Assert.Equal(1430, entry["a1Issue"]!.GetValue<int>());
        Assert.Equal(Record025, entry["terminalRecord"]!.GetValue<string>());
        Assert.Equal(Registry025, entry["claimRegistry"]!.GetValue<string>());
        Assert.False(entry.ContainsKey("maintainerOverride"));
        Assert.False(File.Exists(Path.Combine(RepoRoot(), Record025)), "a #1430 record landed; update the map and this test");
    }

    [Fact]
    public void Release025FailsUntilItIsAdjudicated()
    {
        using var repo = Build025();
        // The committed map: not yet adjudicated, whatever the record says.
        AssertCodes(repo.Run(repo.AllSurfaces()), "G016");
        // Marking it adjudicated is not enough while its contract packet is a format the gate cannot read.
        var map = GateRepo.CommittedMap();
        Entry(map, "0.25.x")["status"] = "adjudicated";
        AssertCodes(repo.RunWithMap(map, repo.AllSurfaces()), "G016");
        // With no record at the mapped path, the identity names nothing.
        var identity = $"calor-adjudication:v1:{repo.Candidate}:{repo.RecordSha}";
        AssertCodes(repo.RunWithMap(map, "--identity", identity), "G004");
    }

    [Fact]
    public void Release025PassesOnceAReviewedMapMarksItAdjudicated()
    {
        // Positive control for the generalization: the same gate, with a map entry for 0.25.x
        // whose packet is a format it checks, passes a 0.25.0 record at #1430's path.
        using var repo = Build025();
        var map = GateRepo.CommittedMap();
        var entry = Entry(map, "0.25.x");
        entry["status"] = "adjudicated";
        entry["contractPacket"] = Entry(map, "0.24.0")["contractPacket"]!.DeepClone();
        var (code, output) = repo.RunWithMap(map, repo.AllSurfaces());
        Assert.True(code == 0, output);
        Assert.Contains("version=0.25.0", output);
    }

    // ------------------------------------------------------------------
    // The map itself fails closed
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("schema")]
    [InlineData("duplicate version")]
    [InlineData("overlapping keys")]
    [InlineData("override on another release")]
    [InlineData("unknown field")]
    [InlineData("parent path")]
    [InlineData("unknown status")]
    [InlineData("shared record path")]
    public void MalformedMapFails(string defect)
    {
        using var repo = GateRepo.Build();
        var map = GateRepo.CommittedMap();
        var e024 = Entry(map, "0.24.0");
        var e025 = Entry(map, "0.25.x");
        switch (defect)
        {
            case "schema": map["schema"] = "calor.release-adjudication-map/0"; break;
            case "duplicate version": e025["version"] = "0.24.0"; break;
            case "overlapping keys": e025["version"] = "0.24.x"; break;
            case "override on another release": e025["maintainerOverride"] = OverridePath; break;
            case "unknown field": e024["bypass"] = true; break;
            case "parent path": e024["terminalRecord"] = "docs/../docs/plans/evidence/adjudication-1408/terminal-record.json"; break;
            case "unknown status": e024["status"] = "assumed"; break;
            case "shared record path": e025["terminalRecord"] = RecordPath; break;
        }
        AssertCodes(repo.RunWithMap(map, repo.AllSurfaces()), "G016");
    }

    private static GateRepo Build025() => GateRepo.Build(
        version: "0.25.0", recordPath: Record025, issue: 1430, registryPath: Registry025,
        website: "<p>Calor 0.25.0</p>");

    private static JsonObject Entry(JsonObject map, string version) =>
        map["releases"]!.AsArray().Single(e => e!["version"]!.GetValue<string>() == version)!.AsObject();

    private static void AssertCodes((int Code, string Output) result, params string[] expected)
    {
        var actual = Regex.Matches(result.Output, @"release gate \(#1410\) (G\d{3}):")
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(expected.ToHashSet().SetEquals(actual),
            $"expected codes [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]\n{result.Output}");
        Assert.Equal(1, result.Code);
    }
}
