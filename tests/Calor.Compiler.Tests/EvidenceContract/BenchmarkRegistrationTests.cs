using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// #1276 (0.24 B1) — the committed benchmark registration pins the complete cutoff pair
/// denominator, the oracle, the metric set, and the statistical identity before any disposition
/// exists, and the validator fails closed when any pinned fact drifts. Each negative control
/// changes one fact, re-seals the packet hashes so only the targeted rule can fire (except the
/// controls that test the seals), and asserts the specific code plus any named co-firing code.
/// </summary>
public class BenchmarkRegistrationTests
{
    private const string Corpus = "tests/TestData/Benchmarks";

    private static readonly Dictionary<string, (string File, Action<JsonNode> Change, string Code, string[] Also)> Mutations = new()
    {
        ["dropped pair"] = ("pairs.json", d => d["pairs"]!.AsArray().RemoveAt(0), "B002", []),
        ["arm filed as non-pair"] = ("corpus-inventory.json", d =>
        {
            var file = d["files"]!.AsArray().First(f => S(f!, "path").EndsWith("CsvParser.calr", StringComparison.Ordinal))!;
            (file["role"], file["reason"]) = ("non-pair", "not a pair");
        }, "B002", []),
        ["changed task statement"] = ("pairs.json", d => d["pairs"]![0]!["taskStatement"] = "name: something else\n", "B003", []),
        ["duplicate pair id"] = ("pairs.json", d => d["pairs"]![1]!["pairId"] = S(d["pairs"]![0]!, "pairId"), "B003", []),
        // B003 co-fires: the missing hash no longer matches the corpus inventory.
        ["pair without hash"] = ("pairs.json", d => d["pairs"]![0]!.AsObject().Remove("calorSha256"), "E009", ["B003"]),
        ["EQUIVALENT before the oracle"] = ("pairs.json", d => d["pairs"]![0]!["disposition"] = "EQUIVALENT", "B004", []),
        ["NOT-EQUIVALENT before the oracle"] = ("pairs.json", d => d["pairs"]![0]!["disposition"] = "NOT-EQUIVALENT", "B004", []),
        // E009 co-fires: an included UNCLASSIFIED pair also breaks the shared inclusion rule.
        ["included before the oracle"] = ("pairs.json", d => d["pairs"]![0]!["included"] = true, "B004", ["E009"]),
        // B002 co-fires: the denominator's excluded count changes too.
        ["unregistered exclusion"] = ("pairs.json", d =>
        {
            (d["pairs"]![0]!["disposition"], d["pairs"]![0]!["exclusionId"]) = ("EXCLUDED-PRE-REGISTERED", "X9-after-the-fact");
        }, "B004", ["B002"]),
        ["exclusion without reason"] = ("exclusions.json", d => d["pairExclusions"]![0]!["reason"] = "", "B004", []),
        ["dispositions assigned"] = ("registration.json", d => d["dispositionsAssigned"] = true, "B004", []),
        ["repeated runs as samples"] = ("registration.json", d => d["statistics"]!["repeatedDeterministicRunsAreSamples"] = true, "B005", []),
        ["interval below two pairs"] = ("registration.json", d => d["statistics"]!["interval"]!["minimumPairs"] = 1, "B005", []),
        ["single run"] = ("registration.json", d => d["statistics"]!["runCount"] = 1, "B005", []),
        ["undeclared population"] = ("registration.json", d => d["statistics"]!["population"] = "", "B005", []),
        ["run-level sampling unit"] = ("registration.json", d => d["statistics"]!["samplingUnit"] = "benchmark-run", "E010", []),
        ["oracle command without commit"] = ("registration.json", d => d["oracle"]!["command"] = "dotnet run -- pair-oracle", "B006", []),
        ["unpinned oracle"] = ("registration.json", d => d["oracle"]!["implementation"] = new JsonArray(), "B006", []),
        ["unclassified metric"] = ("metric-set.json", d => d["descriptiveOnly"]!.AsArray().RemoveAt(6), "B007", []),
        ["metric classified twice"] = ("metric-set.json", d => d["comparative"]!.AsArray()
            .Add(new JsonObject { ["category"] = "Comprehension", ["metric"] = "StructuralClarity" }), "B007", []),
    };

    [Fact]
    public void CommittedRegistrationIsValid()
    {
        var violations = Validate(Packet());
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void DenominatorIsEveryCutoffPairWithNoDispositionAssigned()
    {
        var pairs = JsonNode.Parse(Packet()["pairs.json"])!["pairs"]!.AsArray().OfType<JsonNode>().ToList();
        Assert.Equal(226, pairs.Count);
        Assert.Equal(217, pairs.Count(p => S(p, "disposition") == "UNCLASSIFIED" && S(p, "source") == "manifest.benchmarks"));
        Assert.Equal(9, pairs.Count(p => S(p, "disposition") == "EXCLUDED-PRE-REGISTERED"));
        Assert.All(pairs, p => Assert.False(p["included"]!.GetValue<bool>()));
        Assert.Equal(Directory.EnumerateFiles(Path.Combine(RepoRoot(), Corpus), "*.calr", SearchOption.AllDirectories).Count(), pairs.Count);
        Assert.Contains(pairs, p => S(p, "pairId") == "DomainProblems/CsvParser" && S(p, "disposition") == "UNCLASSIFIED");
    }

    [Theory]
    [InlineData("dropped pair")]
    [InlineData("arm filed as non-pair")]
    [InlineData("changed task statement")]
    [InlineData("duplicate pair id")]
    [InlineData("pair without hash")]
    [InlineData("EQUIVALENT before the oracle")]
    [InlineData("NOT-EQUIVALENT before the oracle")]
    [InlineData("included before the oracle")]
    [InlineData("unregistered exclusion")]
    [InlineData("exclusion without reason")]
    [InlineData("dispositions assigned")]
    [InlineData("repeated runs as samples")]
    [InlineData("interval below two pairs")]
    [InlineData("single run")]
    [InlineData("undeclared population")]
    [InlineData("run-level sampling unit")]
    [InlineData("oracle command without commit")]
    [InlineData("unpinned oracle")]
    [InlineData("unclassified metric")]
    [InlineData("metric classified twice")]
    public void MutationIsRejected(string name)
    {
        var (file, change, code, also) = Mutations[name];
        var packet = Packet();
        var document = JsonNode.Parse(packet[file])!;
        change(document);
        packet[file] = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        AssertViolation(Validate(Reseal(packet, rekey: true)), code, also);
    }

    [Fact]
    public void TamperedPacketFileBreaksTheSeal()
    {
        var packet = Packet();
        packet["registration.json"] = packet["registration.json"].Replace("\"issue\": 1276", "\"issue\": 1277", StringComparison.Ordinal);
        AssertViolation(Validate(packet), "B001");
    }

    [Fact]
    public void StaleComparabilityKeyIsRejected()
    {
        var packet = Packet();
        packet["pairs.json"] = packet["pairs.json"].Replace("\"reviewer\": \"unassigned", "\"reviewer\": \"Unassigned", StringComparison.Ordinal);
        AssertViolation(Validate(Reseal(packet, rekey: false)), "B005");
    }

    [Theory]
    [InlineData(Corpus + "/DomainProblems/CsvParser.cs", "B002")]
    [InlineData("tests/Calor.Evaluation/Equivalence/PairDifferentialOracle.cs", "B006")]
    [InlineData("tests/Calor.Evaluation/Metrics/TokenEconomicsCalculator.cs", "B006")]
    public void DriftedPinnedFileIsRejected(string path, string code)
        => AssertViolation(Validate(Packet(), changedFile: path), code);

    [Fact]
    public void UnregisteredCorpusFileIsAnIncompleteDenominator()
        => AssertViolation(Validate(Packet(), extraCorpusFile: $"{Corpus}/DomainProblems/NewPair.calr"), "B002");

    // ------------------------------------------------------------------

    private static IReadOnlyList<ContractViolation> Validate(
        Dictionary<string, string> packet, string? extraCorpusFile = null, string? changedFile = null)
    {
        var root = RepoRoot();
        var corpus = Directory.EnumerateFiles(Path.Combine(root, Corpus), "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != ".DS_Store")
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Concat(extraCorpusFile is null ? [] : [extraCorpusFile])
            .ToList();
        byte[]? Read(string path)
        {
            var full = Path.Combine(root, path);
            return !File.Exists(full) ? null : path == changedFile ? [.. File.ReadAllBytes(full), (byte)'\n'] : File.ReadAllBytes(full);
        }
        var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/plans/evidence/evidence-contract-1407/contract.json")))!;
        return EvidenceContractValidator.ValidateBenchmarkRegistration(contract, packet, Read, corpus);
    }

    private static Dictionary<string, string> Packet()
        => EvidenceContractValidator.RegistrationPacketFiles.Append("sha256.json").ToDictionary(
            name => name,
            name => File.ReadAllText(Path.Combine(RepoRoot(), EvidenceContractValidator.RegistrationDir, name)),
            StringComparer.Ordinal);

    /// <summary>Recomputes the comparability hashes (when <paramref name="rekey"/>) and sha256.json over the in-memory packet.</summary>
    private static Dictionary<string, string> Reseal(Dictionary<string, string> packet, bool rekey)
    {
        if (rekey)
        {
            var registration = JsonNode.Parse(packet["registration.json"])!;
            var key = registration["comparability"]!;
            key["pairManifestSha256"] = EvidenceContractValidator.TextSha256(packet["pairs.json"]);
            key["metricSetSha256"] = EvidenceContractValidator.TextSha256(packet["metric-set.json"]);
            key["exclusionsSha256"] = EvidenceContractValidator.TextSha256(packet["exclusions.json"]);
            packet["registration.json"] = registration.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        var manifest = JsonNode.Parse(packet["sha256.json"])!;
        foreach (var name in EvidenceContractValidator.RegistrationPacketFiles)
            manifest["files"]![$"{EvidenceContractValidator.RegistrationDir}/{name}"] = EvidenceContractValidator.TextSha256(packet[name]);
        packet["sha256.json"] = manifest.ToJsonString();
        return packet;
    }

    private static string S(JsonNode node, string field) => node[field]?.GetValue<string>() ?? "";

    private static void AssertViolation(IReadOnlyList<ContractViolation> violations, string code, params string[] alsoAllowed)
    {
        Assert.True(violations.Any(v => v.Code == code), $"expected a {code} violation; got:{Environment.NewLine}{Describe(violations)}");
        var unexpected = violations.Where(v => v.Code != code && !alsoAllowed.Contains(v.Code)).ToList();
        Assert.True(unexpected.Count == 0, $"expected only {code}; also got:{Environment.NewLine}{Describe(unexpected)}");
    }

    private static string Describe(IReadOnlyList<ContractViolation> violations)
        => violations.Count == 0 ? "(none)" : string.Join(Environment.NewLine, violations.Take(20));

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")) && !File.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
