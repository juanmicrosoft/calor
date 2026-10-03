using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// #1276 (0.24 B1) results — the committed pair manifest carries every registered pair with the
/// disposition the valid reconciled oracle run assigned, and the comparative metric is the
/// registered computation over EQUIVALENT pairs only. Each negative control changes one fact,
/// re-seals the results so only the targeted rule can fire, and asserts its code.
/// </summary>
public class BenchmarkResultsTests
{
    private static readonly Dictionary<string, (string File, Action<JsonNode> Change, string Code, string[] Also)> Mutations = new()
    {
        ["dropped pair"] = ("pair-manifest.json", d => d["pairs"]!.AsArray().RemoveAt(0), "R002", ["R005"]),
        ["changed registered hash"] = ("pair-manifest.json", d => d["pairs"]![0]!["csharpSha256"] = new string('0', 64), "R002", []),
        ["disposition not from the oracle"] = ("pair-manifest.json", d => Row(d, "DomainProblems/CsvParser")["disposition"] = "UNCLASSIFIED", "R003", ["R005"]),
        ["late exclusion"] = ("pair-manifest.json", d => Row(d, "DomainProblems/CsvParser")["disposition"] = "EXCLUDED-PRE-REGISTERED", "R003", ["R005"]),
        ["invalid oracle run"] = ("oracle-run-2.json", d => d["valid"] = false, "R003", []),
        ["witness not NOT-EQUIVALENT"] = ("oracle-run-2.json", d => Verdict(d, "DomainProblems/CsvParser")["Disposition"] = "EQUIVALENT", "R003", []),
        ["NOT-EQUIVALENT pair included"] = ("pair-manifest.json", d => Row(d, "DomainProblems/CsvParser")["included"] = true, "R004", ["E009", "R005"]),
        ["EQUIVALENT pair left out"] = ("pair-manifest.json", d => d["pairs"]!.AsArray().First(p => p!["included"]!.GetValue<bool>())!["included"] = false, "R004", ["R005"]),
        ["metric over a NOT-EQUIVALENT pair"] = ("metrics-run-1.json", d => d["pairs"]!.AsArray().Add(new JsonObject
        {
            ["pairId"] = "DomainProblems/CsvParser", ["category"] = "DomainProblems", ["r"] = "1.5",
        }), "R005", []),
        ["runs not bit-identical"] = ("metrics-run-2.json", d => d["pairs"]![0]!["r"] = "1.0000000000000002", "R005", []),
        ["narrowed interval"] = ("results.json", d => d["metric"]!["overall"]!["interval95"]![0] = d["metric"]!["overall"]!["geometricMeanR"]!.GetValue<string>(), "R005", []),
        ["interval for one pair"] = ("results.json", d => d["metric"]!["perCategory"]!.AsObject().First(c => c.Value!["pairs"]!.GetValue<int>() == 1).Value!["interval95"] = new JsonArray("1", "2"), "R005", []),
        ["changed method key"] = ("results.json", d => d["comparability"]!["aggregationMethod"] = "mean of category means", "R005", []),
    };

    [Fact]
    public void CommittedResultsAreValid()
    {
        var violations = Validate(Results());
        Assert.True(violations.Count == 0, EvidenceContractTests.Describe(violations));
    }

    [Fact]
    public void EveryRegisteredPairHasAnOracleDisposition()
    {
        var rows = JsonNode.Parse(Results()["pair-manifest.json"])!["pairs"]!.AsArray().OfType<JsonNode>().ToList();
        Assert.Equal(226, rows.Count);
        Assert.Equal(9, rows.Count(r => S(r, "disposition") == "EXCLUDED-PRE-REGISTERED"));
        Assert.All(rows, r => Assert.NotEqual("pending-oracle", S(r["equivalenceEvidence"]!, "status")));
        Assert.Equal("NOT-EQUIVALENT", S(rows.Single(r => S(r, "pairId") == "DomainProblems/CsvParser"), "disposition"));
        Assert.All(rows.Where(r => r["included"]!.GetValue<bool>()), r => Assert.Equal("EQUIVALENT", S(r, "disposition")));
    }

    [Theory]
    [InlineData("dropped pair")]
    [InlineData("changed registered hash")]
    [InlineData("disposition not from the oracle")]
    [InlineData("late exclusion")]
    [InlineData("invalid oracle run")]
    [InlineData("witness not NOT-EQUIVALENT")]
    [InlineData("NOT-EQUIVALENT pair included")]
    [InlineData("EQUIVALENT pair left out")]
    [InlineData("metric over a NOT-EQUIVALENT pair")]
    [InlineData("runs not bit-identical")]
    [InlineData("narrowed interval")]
    [InlineData("interval for one pair")]
    [InlineData("changed method key")]
    public void MutationIsRejected(string name)
    {
        var (file, change, code, also) = Mutations[name];
        var results = Results();
        var document = JsonNode.Parse(results[file])!;
        change(document);
        results[file] = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        EvidenceContractTests.AssertViolation(Validate(Reseal(results)), code, also);
    }

    [Fact]
    public void TamperedResultsFileBreaksTheSeal()
    {
        var results = Results();
        results["results.json"] += " ";
        EvidenceContractTests.AssertViolation(Validate(results), "R001");
    }

    // ------------------------------------------------------------------

    private static IReadOnlyList<ContractViolation> Validate(Dictionary<string, string> results)
    {
        var root = EvidenceContractTests.RepoRoot();
        var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/plans/evidence/evidence-contract-1407/contract.json")))!;
        var packet = EvidenceContractValidator.RegistrationPacketFiles.ToDictionary(
            name => name, name => File.ReadAllText(Path.Combine(root, EvidenceContractValidator.RegistrationDir, name)), StringComparer.Ordinal);
        return EvidenceContractValidator.ValidateBenchmarkResults(contract, packet, results);
    }

    private static Dictionary<string, string> Results()
    {
        var dir = Path.Combine(EvidenceContractTests.RepoRoot(), EvidenceContractValidator.ResultsDir);
        return Directory.EnumerateFiles(dir).Where(f => Path.GetFileName(f) != ".DS_Store")
            .ToDictionary(f => Path.GetFileName(f), f => Encoding.UTF8.GetString(File.ReadAllBytes(f)), StringComparer.Ordinal);
    }

    /// <summary>Recomputes the manifest's oracle hashes, the key's manifest hash, and sha256.json over the in-memory results.</summary>
    private static Dictionary<string, string> Reseal(Dictionary<string, string> results)
    {
        static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));
        var manifest = JsonNode.Parse(results["pair-manifest.json"])!;
        (manifest["oracleResultSha256"], manifest["firstOracleResultSha256"]) = (Sha(results["oracle-run-2.json"]), Sha(results["oracle-run-1.json"]));
        results["pair-manifest.json"] = manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var summary = JsonNode.Parse(results["results.json"])!;
        summary["comparability"]!["pairManifestSha256"] = Sha(results["pair-manifest.json"]);
        results["results.json"] = summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var seals = new JsonObject();
        foreach (var name in results.Keys.Where(k => k != "sha256.json").Order(StringComparer.Ordinal))
            seals[$"{EvidenceContractValidator.ResultsDir}/{name}"] = Sha(results[name]);
        results["sha256.json"] = new JsonObject { ["algorithm"] = "sha256", ["files"] = seals }.ToJsonString();
        return results;
    }

    private static JsonNode Row(JsonNode manifest, string pairId) => manifest["pairs"]!.AsArray().First(p => S(p!, "pairId") == pairId)!;

    private static JsonNode Verdict(JsonNode run, string pairId) => run["results"]!.AsArray().First(p => S(p!, "PairId") == pairId)!;

    private static string S(JsonNode node, string field) => node[field]?.GetValue<string>() ?? "";
}
