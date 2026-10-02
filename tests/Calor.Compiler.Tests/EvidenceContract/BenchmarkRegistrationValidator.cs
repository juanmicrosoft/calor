using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// #1276 (0.24 B1) benchmark registration: the contract's <c>benchmarkEquivalence.registrationRule</c>
/// checked against the committed packet under <c>docs/plans/evidence/b1-1276/registration/</c>.
/// Pair rows go through the same E008-E010 rules as any benchmark row; the B codes add what a
/// registration must pin before any disposition exists.
/// </summary>
internal static partial class EvidenceContractValidator
{
    public const string RegistrationDir = "docs/plans/evidence/b1-1276/registration";

    public static readonly IReadOnlyList<string> RegistrationPacketFiles =
        ["registration.json", "pairs.json", "corpus-inventory.json", "exclusions.json", "metric-set.json"];

    /// <summary>The metric categories BenchmarkRunner computes in its static run at the cutoff.</summary>
    public static readonly IReadOnlyList<string> StaticMetricCategories =
        ["TokenEconomics", "GenerationAccuracy", "Comprehension", "EditPrecision", "ErrorDetection",
         "InformationDensity", "RefactoringStability", "Correctness"];

    /// <summary>
    /// <paramref name="packet"/> maps packet file names to text; <paramref name="readRepoFile"/> returns null for an absent file;
    /// a null <paramref name="pinnedSeal"/> skips B008 (for single-rule controls).
    /// </summary>
    public static IReadOnlyList<ContractViolation> ValidateBenchmarkRegistration(
        JsonNode contract, IReadOnlyDictionary<string, string> packet,
        Func<string, byte[]?> readRepoFile, IReadOnlyCollection<string> corpusFiles, string? pinnedSeal)
    {
        var v = new List<ContractViolation>();
        // B008: a regenerated, internally consistent packet is still a different registration.
        if (pinnedSeal is not null && TextSha256(packet["sha256.json"]) != pinnedSeal)
            v.Add(new("B008", "sha256.json", "the packet is not the registered packet; changing it is a #1407 amendment"));
        JsonNode Load(string name) => JsonNode.Parse(packet[name])!;
        var registration = Load("registration.json");
        var pairs = Array(Load("pairs.json")["pairs"]).OfType<JsonNode>().ToList();
        var inventory = Array(Load("corpus-inventory.json")["files"]).OfType<JsonNode>().ToList();
        var exclusions = Array(Load("exclusions.json")["pairExclusions"]).OfType<JsonNode>().ToList();
        var metricSet = Load("metric-set.json");
        var bench = contract["benchmarkEquivalence"]!;
        var samplingUnit = Str(bench["samplingUnit"]);

        // B001: content addressing of the packet itself.
        var manifest = Load("sha256.json")["files"];
        var document = Str(registration["document"]);
        foreach (var name in RegistrationPacketFiles)
        {
            var key = $"{RegistrationDir}/{name}";
            if (Str(manifest?[key]) != TextSha256(packet[name]))
                v.Add(new("B001", key, "sha256.json does not record this packet file's LF-normalized SHA-256"));
        }
        var docBytes = document is null ? null : readRepoFile(document);
        if (docBytes is null || Str(manifest?[document!]) != TextSha256(Encoding.UTF8.GetString(docBytes)))
            v.Add(new("B001", document ?? "document", "sha256.json does not record the registration document's SHA-256"));

        // B002: the denominator is every pair in the cutoff corpus, and the corpus has not drifted.
        if (Str(registration["cutoff"]?["commit"]) != Str(contract["cutoff"]?["commit"]))
            v.Add(new("B002", "cutoff", "registration cutoff is not the contract cutoff"));
        var inventoryPaths = inventory.Select(f => Str(f["path"]) ?? "").ToHashSet(StringComparer.Ordinal);
        foreach (var path in corpusFiles.Where(p => !inventoryPaths.Contains(p)))
            v.Add(new("B002", path, "corpus file is not in corpus-inventory.json; the denominator is incomplete"));
        foreach (var file in inventory)
        {
            var path = Str(file["path"]) ?? "?";
            var bytes = readRepoFile(path);
            if (bytes is null || Sha256(bytes) != Str(file["sha256"]))
                v.Add(new("B002", path, "corpus file is missing or differs from its registered SHA-256; a change to a pinned pair is an amendment"));
            var role = Str(file["role"]);
            if (role == "non-pair" && (string.IsNullOrWhiteSpace(Str(file["reason"])) || IsArmPath(path)))
                v.Add(new("B002", path, "a non-pair file needs a reason and cannot be a .calr or hand-written .cs"));
            else if (role is not ("non-pair" or "calor-arm" or "csharp-arm"))
                v.Add(new("B002", path, $"unknown corpus file role '{role}'"));
        }
        var arms = pairs.SelectMany(p => new[] { Str(p["calorPath"]), Str(p["csharpPath"]) }).ToList();
        foreach (var file in inventory.Where(f => Str(f["role"]) is "calor-arm" or "csharp-arm"))
        {
            if (arms.Count(a => a == Str(file["path"])) != 1)
                v.Add(new("B002", Str(file["path"]) ?? "?", "an arm file must belong to exactly one registered pair"));
        }
        var denominator = registration["denominator"];
        var excludedCount = pairs.Count(p => Str(p["disposition"]) == "EXCLUDED-PRE-REGISTERED");
        if (Int(denominator?["pairs"]) != pairs.Count || Int(denominator?["excludedPreRegistered"]) != excludedCount
            || Int(denominator?["toOracle"]) != pairs.Count - excludedCount)
            v.Add(new("B002", "denominator", "denominator counts do not match pairs.json"));

        // E008-E010 via the shared benchmark-row rules, then the registration-specific pair rules.
        var row = new JsonObject
        {
            ["benchmark"] = new JsonObject
            {
                ["samplingUnit"] = registration["statistics"]?["samplingUnit"]?.DeepClone(),
                ["comparability"] = registration["comparability"]?.DeepClone(),
                ["pairs"] = new JsonArray(pairs.Select(p => (JsonNode?)p.DeepClone()).ToArray()),
            },
        };
        v.AddRange(ValidateBenchmark(row, "registration",
            Array(bench["comparabilityKey"]).Select(Str).OfType<string>().ToList(),
            Array(bench["pairRowRequiredFields"]).Select(Str).OfType<string>().ToList(),
            Array(bench["pairRowStringFields"]).Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal),
            Array(bench["pairDispositions"]).Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal),
            samplingUnit));

        var inventoryHashes = inventory.ToDictionary(f => Str(f["path"]) ?? "", f => Str(f["sha256"]), StringComparer.Ordinal);
        var exclusionIds = exclusions.ToDictionary(e => Str(e["exclusionId"]) ?? "", e => e, StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var id = Str(pair["pairId"]) ?? "?";
            if (pairs.Count(p => Str(p["pairId"]) == id) != 1)
                v.Add(new("B003", id, "duplicate pairId"));
            foreach (var (pathField, hashField) in new[] { ("calorPath", "calorSha256"), ("csharpPath", "csharpSha256") })
            {
                if (!inventoryHashes.TryGetValue(Str(pair[pathField]) ?? "", out var registered) || registered != Str(pair[hashField]))
                    v.Add(new("B003", id, $"{hashField} does not match the corpus inventory"));
            }
            if (Str(pair["taskStatementSha256"]) != TextSha256Raw(Str(pair["taskStatement"])))
                v.Add(new("B003", id, "taskStatementSha256 is not the SHA-256 of the registered task statement"));

            // B004: registration assigns no disposition; exclusions are pre-registered with a reason.
            var disposition = Str(pair["disposition"]);
            if (Bool(pair["included"]) != false)
                v.Add(new("B004", id, "no pair may be included before the oracle assigns dispositions"));
            if (disposition == "UNCLASSIFIED")
            {
                if (Str(pair["equivalenceEvidence"]?["status"]) != "pending-oracle")
                    v.Add(new("B004", id, "an unclassified pair must await the registered oracle"));
            }
            else if (disposition == "EXCLUDED-PRE-REGISTERED")
            {
                var exclusionId = Str(pair["exclusionId"]) ?? "";
                if (!exclusionIds.TryGetValue(exclusionId, out var exclusion)
                    || !Array(exclusion["pairs"]).Select(Str).Contains(id))
                    v.Add(new("B004", id, $"exclusion '{exclusionId}' is not pre-registered for this pair"));
            }
            else
                v.Add(new("B004", id, $"disposition '{disposition}' is assigned before the registered oracle ran"));
        }
        if (Bool(registration["dispositionsAssigned"]) != false)
            v.Add(new("B004", "registration", "dispositionsAssigned must be false in the registration"));
        foreach (var exclusion in exclusions)
        {
            var exclusionId = Str(exclusion["exclusionId"]) ?? "?";
            if (string.IsNullOrWhiteSpace(Str(exclusion["reason"])))
                v.Add(new("B004", exclusionId, "pre-registered exclusion has no reason"));
            foreach (var listed in Array(exclusion["pairs"]).Select(Str))
            {
                if (!pairs.Any(p => Str(p["pairId"]) == listed && Str(p["exclusionId"]) == exclusionId
                    && Str(p["disposition"]) == "EXCLUDED-PRE-REGISTERED"))
                    v.Add(new("B004", exclusionId, $"listed pair '{listed}' is not registered as excluded by it"));
            }
        }

        // B005: comparability key and statistical identity.
        var comparability = registration["comparability"];
        foreach (var (field, file) in new[] { ("pairManifestSha256", "pairs.json"), ("metricSetSha256", "metric-set.json"), ("exclusionsSha256", "exclusions.json") })
        {
            if (Str(comparability?[field]) != TextSha256(packet[file]))
                v.Add(new("B005", field, $"comparability {field} is not the SHA-256 of {file}"));
        }
        var statistics = registration["statistics"];
        if (Bool(statistics?["repeatedDeterministicRunsAreSamples"]) != false)
            v.Add(new("B005", "statistics", "repeated deterministic runs must not be samples"));
        if (Int(statistics?["interval"]?["minimumPairs"]) is not >= 2)
            v.Add(new("B005", "statistics", "an interval needs at least 2 independent pairs"));
        if (Int(comparability?["runCount"]) is not >= 2 || Int(statistics?["runCount"]) != Int(comparability?["runCount"]))
            v.Add(new("B005", "statistics", "runCount must be the registered determinism-check count (at least 2) everywhere"));
        if (string.IsNullOrWhiteSpace(Str(statistics?["population"])) || string.IsNullOrWhiteSpace(Str(statistics?["interval"]?["label"])))
            v.Add(new("B005", "statistics", "population and interval label must be declared"));

        // B006: the oracle and the metric implementation are pinned by content.
        var oracle = registration["oracle"];
        var command = Str(oracle?["command"]) ?? "";
        if (string.IsNullOrWhiteSpace(Str(oracle?["id"])) || string.IsNullOrWhiteSpace(Str(oracle?["version"]))
            || !command.Contains("pair-oracle", StringComparison.Ordinal) || !command.Contains("--registration-commit", StringComparison.Ordinal)
            || Int(oracle?["runs"]) is not >= 2 || IsBlank(oracle?["failureBehavior"]) || IsBlank(oracle?["expectedOutputs"]) || IsBlank(oracle?["inputs"]))
            v.Add(new("B006", "oracle", "oracle id, version, command, run count, inputs, expected outputs, and failure behavior must be pinned"));
        var oracleFiles = Array(oracle?["implementation"]).OfType<JsonNode>().ToList();
        if (oracleFiles.Count == 0)
            v.Add(new("B006", "oracle", "oracle implementation files are not pinned"));
        if (Str(metricSet["implementationVersion"]) != Str(comparability?["metricImplementationVersion"]))
            v.Add(new("B006", "metric-set", "metric implementation version differs from the comparability key"));
        // Oracle sources are pinned over LF-normalized text; metric sources over the cutoff blob bytes.
        var pinned = oracleFiles.Select(f => (f, (Func<byte[], string>)(b => TextSha256(Encoding.UTF8.GetString(b)))))
            .Concat(Array(metricSet["implementationFiles"]).OfType<JsonNode>().Select(f => (f, (Func<byte[], string>)Sha256)));
        foreach (var (file, hash) in pinned)
        {
            var bytes = readRepoFile(Str(file["path"]) ?? "");
            if (bytes is null || hash(bytes) != Str(file["sha256"]))
                v.Add(new("B006", Str(file["path"]) ?? "?", "implementation differs from its registered hash; changing the oracle or a metric is an amendment"));
        }

        // B007: every static metric category is classified exactly once, and at least one is comparative.
        var comparative = Array(metricSet["comparative"]).Select(m => Str(m?["category"])).ToList();
        var descriptive = Array(metricSet["descriptiveOnly"]).Select(m => Str(m?["category"])).ToList();
        if (comparative.Count == 0 || Array(metricSet["descriptiveOnly"]).Any(m => string.IsNullOrWhiteSpace(Str(m?["reason"]))))
            v.Add(new("B007", "metric-set", "no comparative metric, or a descriptive-only metric without a reason"));
        foreach (var category in StaticMetricCategories)
        {
            if (comparative.Concat(descriptive).Count(c => c == category) != 1)
                v.Add(new("B007", category, "static metric category must be classified exactly once"));
        }
        foreach (var unknown in comparative.Concat(descriptive).Where(c => c is null || !StaticMetricCategories.Contains(c)))
            v.Add(new("B007", unknown ?? "?", "unknown metric category"));

        return v;
    }

    private static bool IsArmPath(string path) =>
        path.EndsWith(".calr", StringComparison.Ordinal)
        || (path.EndsWith(".cs", StringComparison.Ordinal) && !path.EndsWith(".g.cs", StringComparison.Ordinal));

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>SHA-256 over LF-normalized UTF-8 text (packet files and the oracle source).</summary>
    internal static string TextSha256(string text) => Sha256(new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n")));

    private static string? TextSha256Raw(string? text) => text is null ? null : Sha256(new UTF8Encoding(false).GetBytes(text));
}
