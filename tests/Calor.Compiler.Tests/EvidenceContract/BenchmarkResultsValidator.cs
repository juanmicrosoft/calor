using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// #1276 (0.24 B1) results: the dispositioned pair manifest and aggregate under
/// <c>docs/plans/evidence/b1-1276/results/</c>, checked against the registration packet and the
/// committed raw oracle and metric runs. R codes: R001 seals, R002 manifest completeness, R003
/// dispositions come from the valid reconciled oracle run, R004 inclusion, R005 the metric and key.
/// </summary>
internal static partial class EvidenceContractValidator
{
    public const string ResultsDir = "docs/plans/evidence/b1-1276/results";

    /// <summary>The main commit that merged the registration (PR #1473); the oracle ran at exactly this commit.</summary>
    public const string RegistrationMergeCommit = "f0e0eb682a8658364170ad49aff2d2c433d570f8";

    public static readonly IReadOnlyList<string> ResultsJsonFiles =
        ["oracle-run-1.json", "oracle-run-2.json", "metrics-run-1.json", "metrics-run-2.json", "pair-manifest.json", "results.json", "environment.json"];

    private const string ResultsLabel =
        "Describes this fixed, author-built corpus only. r is a static source-size ratio of two committed files, not a coding-agent outcome or a language advantage. " +
        "EQUIVALENT means no observed disagreement on the registered finite inputs; it is not a proof and not a correctness claim about either arm.";

    /// <summary>
    /// <paramref name="results"/> maps every file in the results directory to its text;
    /// <paramref name="recompute"/> re-runs the pinned metric calculator on a registered pair's committed files.
    /// </summary>
    public static IReadOnlyList<ContractViolation> ValidateBenchmarkResults(
        JsonNode contract, IReadOnlyDictionary<string, string> registrationPacket, IReadOnlyDictionary<string, string> results,
        Func<JsonNode, (string Metric, double CalorSize, double CSharpSize, double R)> recompute)
    {
        var v = new List<ContractViolation>();
        if (ResultsJsonFiles.Append("sha256.json").FirstOrDefault(f => !results.ContainsKey(f)) is { } missing)
            return [new("R001", missing, "results file is missing")];

        // R001: every results file is sealed by its exact bytes, and nothing unsealed sits beside them.
        var seals = JsonNode.Parse(results["sha256.json"])!["files"]?.AsObject();
        foreach (var name in results.Keys.Where(k => k != "sha256.json"))
        {
            if (Str(seals?[$"{ResultsDir}/{name}"]) != RawSha256(results[name]))
                v.Add(new("R001", name, "sha256.json does not record this results file's SHA-256"));
        }
        foreach (var sealedName in seals?.Select(s => s.Key) ?? [])
        {
            if (!results.ContainsKey(sealedName[(ResultsDir.Length + 1)..]))
                v.Add(new("R001", sealedName, "sealed file is missing"));
        }

        JsonNode Load(string name) => JsonNode.Parse(results[name])!;
        var (run1, run2, manifest, summary) = (Load("oracle-run-1.json"), Load("oracle-run-2.json"), Load("pair-manifest.json"), Load("results.json"));
        var registration = JsonNode.Parse(registrationPacket["registration.json"])!;
        var registered = Array(JsonNode.Parse(registrationPacket["pairs.json"])!["pairs"]).OfType<JsonNode>().ToList();
        var rows = Array(manifest["pairs"]).OfType<JsonNode>().ToList();
        var registrationSha = TextSha256(registrationPacket["pairs.json"]);

        // R002: the manifest keeps every registered pair, path, hash, and pinned field, and names the registration.
        if (Str(manifest["registrationPairManifestSha256"]) != registrationSha || Str(registration["comparability"]?["pairManifestSha256"]) != registrationSha)
            v.Add(new("R002", "pair-manifest", "the manifest does not name the registration's pairManifestSha256"));
        if (!rows.Select(r => Str(r["pairId"])).SequenceEqual(registered.Select(r => Str(r["pairId"]))) || Int(manifest["pairCount"]) != registered.Count)
            v.Add(new("R002", "pair-manifest", "the manifest does not list exactly the registered pairs in registered order"));
        string[] dispositionFields = ["disposition", "included", "equivalenceEvidence", "reviewer", "registeredDisposition"];
        foreach (var (row, pair) in rows.Zip(registered))
        {
            var id = Str(pair["pairId"]) ?? "?";
            var pinned = pair.AsObject().Select(f => f.Key).Concat(row.AsObject().Select(f => f.Key)).Distinct().Where(f => !dispositionFields.Contains(f));
            foreach (var field in pinned.Where(f => row[f]?.ToJsonString() != pair[f]?.ToJsonString()))
                v.Add(new("R002", id, $"registered field '{field}' changed after registration"));
            if (Str(row["registeredDisposition"]) != Str(pair["disposition"]))
                v.Add(new("R002", id, "registeredDisposition is not the registration's disposition"));
        }

        // R003: every disposition is the valid reconciled oracle result's, at the registration merge commit.
        if (Bool(run2["valid"]) != true || Array(run2["problems"]).Count != 0 || Str(run2["registrationCommit"]) != RegistrationMergeCommit
            || Str(run2["pairsSha256"]) != RawSha256(registrationPacket["pairs.json"]))
            v.Add(new("R003", "oracle-run-2.json", "the reconciled oracle result is not valid for this registration at the merge commit"));
        foreach (var field in new[] { "oracleId", "oracleVersion", "inputGeneratorVersion", "registrationCommit", "pairsSha256", "environment" })
        {
            if (run1[field]?.ToJsonString() != run2[field]?.ToJsonString())
                v.Add(new("R003", "oracle-run-1.json", $"the two oracle runs differ in {field}"));
        }
        var oracle = registration["oracle"];
        if (Str(run2["oracleId"]) != Str(oracle?["id"]) || Str(run2["oracleVersion"]) != Str(oracle?["version"])
            || Str(run2["inputGeneratorVersion"]) != Str(oracle?["inputs"]?["generator"]))
            v.Add(new("R003", "oracle-identity", "the oracle runs do not name the registered oracle and input generator"));
        // Re-derive the reconciliation from both raw verdict arrays: unchanged verdicts carry over, changed ones are UNCLASSIFIED.
        var raw1 = Array(run1["runResults"]).OfType<JsonNode>().ToDictionary(r => Str(r["PairId"]) ?? "", r => r.ToJsonString(), StringComparer.Ordinal);
        var raw2 = Array(run2["runResults"]).OfType<JsonNode>().ToList();
        var reconciled = Array(run2["results"]).OfType<JsonNode>().ToList();
        if (!raw1.Keys.Order(StringComparer.Ordinal).SequenceEqual(raw2.Select(r => Str(r["PairId"]) ?? "").Order(StringComparer.Ordinal))
            || !raw2.Select(r => Str(r["PairId"])).SequenceEqual(reconciled.Select(r => Str(r["PairId"]))))
            v.Add(new("R003", "reconciliation", "the two runs and the reconciled result do not cover the same pairs"));
        foreach (var (raw, result) in raw2.Zip(reconciled))
        {
            var changed = !raw1.TryGetValue(Str(raw["PairId"]) ?? "", out var before) || before != raw.ToJsonString();
            if (changed ? Str(result["Reason"]) != "NONDETERMINISTIC_ACROSS_RUNS" || Str(result["Disposition"]) != "UNCLASSIFIED" : result.ToJsonString() != raw.ToJsonString())
                v.Add(new("R003", "reconciliation", $"{Str(raw["PairId"])}: the reconciled verdict does not follow from the two raw runs"));
        }
        // What the registered oracle can emit per reason: only an executed comparison has a surface, inputs, and a
        // transcript hash; AGREE has no witnesses and OBSERVATION_MISMATCH has 1 to 5.
        foreach (var verdict in reconciled.Concat(raw2).Concat(Array(run1["runResults"]).OfType<JsonNode>()))
        {
            var reason = Str(verdict["Reason"]);
            var executed = reason is "AGREE" or "OBSERVATION_MISMATCH";
            var witnesses = Array(verdict["Witnesses"]).Count;
            if (executed != IsSha256(Str(verdict["ObservationsSha256"])) || (executed && (Array(verdict["Surface"]).Count == 0 || Int(verdict["InputCount"]) is not > 0))
                || (reason == "AGREE" && (witnesses != 0 || Str(verdict["Detail"]) != "")) || (reason == "OBSERVATION_MISMATCH" && witnesses is < 1 or > 5)
                || (reason == "PRE_REGISTERED_EXCLUSION" && (Array(verdict["Surface"]).Count != 0 || Int(verdict["InputCount"]) != 0)))
                v.Add(new("R003", "verdict-invariants", $"{Str(verdict["PairId"])}: a {reason} verdict the registered oracle cannot produce"));
        }
        if (Str(manifest["oracleResultSha256"]) != RawSha256(results["oracle-run-2.json"]) || Str(manifest["firstOracleResultSha256"]) != RawSha256(results["oracle-run-1.json"]))
            v.Add(new("R003", "pair-manifest", "the manifest does not name the committed oracle runs"));
        var verdicts = Array(run2["results"]).OfType<JsonNode>().ToDictionary(r => Str(r["PairId"]) ?? "", StringComparer.Ordinal);
        var byReason = registration["oracle"]?["dispositionByReason"];
        foreach (var row in rows)
        {
            var id = Str(row["pairId"]) ?? "?";
            var reason = Str(row["equivalenceEvidence"]?["reason"]);
            if (!verdicts.TryGetValue(id, out var verdict) || Str(verdict["Disposition"]) != Str(row["disposition"]) || Str(verdict["Reason"]) != reason
                || Str(byReason?[reason ?? ""]) != Str(row["disposition"]))
                v.Add(new("R003", id, "disposition is not the reconciled oracle verdict under the registered reason map"));
            if ((Str(row["registeredDisposition"]) == "EXCLUDED-PRE-REGISTERED") != (Str(row["disposition"]) == "EXCLUDED-PRE-REGISTERED"))
                v.Add(new("R003", id, "only pre-registered exclusions may be EXCLUDED-PRE-REGISTERED"));
            // The pair's evidence is exactly the reconciled verdict's, with the oracle and result it came from.
            var evidence = verdict is null ? null : new JsonObject
            {
                ["status"] = Str(row["disposition"]) == "EXCLUDED-PRE-REGISTERED" ? "not-run" : "oracle-executed",
                ["oracle"] = $"{Str(oracle?["id"])}@{Str(oracle?["version"])}",
                ["oracleResultSha256"] = RawSha256(results["oracle-run-2.json"]),
                ["reason"] = verdict["Reason"]?.DeepClone(),
                ["inputCount"] = verdict["InputCount"]?.DeepClone(),
                ["observationsSha256"] = verdict["ObservationsSha256"]?.DeepClone(),
                ["surface"] = verdict["Surface"]?.DeepClone(),
                ["witnesses"] = verdict["Witnesses"]?.DeepClone(),
                ["detail"] = verdict["Detail"]?.DeepClone(),
            };
            if (evidence is null || row["equivalenceEvidence"]?.ToJsonString() != evidence.ToJsonString())
                v.Add(new("R003", id + " evidence", "equivalenceEvidence is not the reconciled oracle verdict with its oracle and result hash"));
        }
        // The execution record: both oracle runs at the merge commit with their registered exit codes, and two
        // bit-identical metric runs at one recorded commit.
        var environment = Load("environment.json");
        if (Str(environment["oracleRuns"]?["checkout"])?.Contains(RegistrationMergeCommit, StringComparison.Ordinal) != true
            || Int(environment["oracleRuns"]?["run1"]?["exitCode"]) != 4 || Int(environment["oracleRuns"]?["run2"]?["exitCode"]) != 0
            || !Regex.IsMatch(Str(environment["metricRuns"]?["commit"]) ?? "", "^[0-9a-f]{40}$") || Bool(environment["metricRuns"]?["bitIdentical"]) != true
            || Array(environment["metricRuns"]?["exitCodes"]).Any(c => Int(c) != 0) || Array(environment["metricRuns"]?["exitCodes"]).Count != 2
            || Str(environment["machine"]?["oracleEnvironmentString"]) != Str(run2["environment"])
            || new[] { "run1", "run2" }.Any(r => Str(environment["oracleRuns"]?[r]?["command"])?.Contains($"pair-oracle --registration {RegistrationDir} --registration-commit {RegistrationMergeCommit}", StringComparison.Ordinal) != true)
            || Str(environment["oracleRuns"]?["run2"]?["command"])?.Contains("--compare-with", StringComparison.Ordinal) != true
            || Array(environment["metricRuns"]?["commands"]).Select(Str).FirstOrDefault()?.Contains("pair-metrics", StringComparison.Ordinal) != true
            || Str(environment["oracleRuns"]?["run1"]?["output"]) != "oracle-run-1.json" || Str(environment["oracleRuns"]?["run2"]?["output"]) != "oracle-run-2.json"
            || !Array(environment["metricRuns"]?["outputs"]).Select(Str).SequenceEqual(["metrics-run-1.json", "metrics-run-2.json"]))
            v.Add(new("R003", "environment", "the environment record does not show the registered oracle and metric executions"));
        if (Str(verdicts.GetValueOrDefault("DomainProblems/CsvParser")?["Disposition"]) != "NOT-EQUIVALENT")
            v.Add(new("R003", "known-witness", "DomainProblems/CsvParser is not NOT-EQUIVALENT; the oracle result is invalid"));

        // R004 and the shared E008-E010 pair rules: only EQUIVALENT manifest.benchmarks pairs are included.
        foreach (var row in rows)
        {
            var expected = Str(row["disposition"]) == "EQUIVALENT" && Str(row["source"]) == "manifest.benchmarks";
            if (Bool(row["included"]) != expected)
                v.Add(new("R004", Str(row["pairId"]) ?? "?", "included must be true exactly for EQUIVALENT manifest.benchmarks pairs"));
        }
        var bench = contract["benchmarkEquivalence"]!;
        var shared = new JsonObject
        {
            ["benchmark"] = new JsonObject
            {
                ["samplingUnit"] = summary["metric"]?["samplingUnit"]?.DeepClone(),
                ["comparability"] = summary["comparability"]?.DeepClone(),
                ["pairs"] = new JsonArray(rows.Select(p => (JsonNode?)p.DeepClone()).ToArray()),
            },
        };
        v.AddRange(ValidateBenchmark(shared, "results",
            Array(bench["comparabilityKey"]).Select(Str).OfType<string>().ToList(),
            Array(bench["pairRowRequiredFields"]).Select(Str).OfType<string>().ToList(),
            Array(bench["pairRowStringFields"]).Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal),
            Array(bench["pairDispositions"]).Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal),
            Str(bench["samplingUnit"])));

        // R005: the comparability key, the determinism check, and the metric recomputed from the committed runs.
        var key = summary["comparability"]?.AsObject();
        foreach (var field in registration["comparability"]!.AsObject().Select(f => f.Key).Where(f => f != "pairManifestSha256"))
        {
            if (key?[field]?.ToJsonString() != registration["comparability"]![field]!.ToJsonString())
                v.Add(new("R005", field, "comparability field differs from the registration"));
        }
        if (Str(key?["pairManifestSha256"]) != TextSha256(results["pair-manifest.json"]))
            v.Add(new("R005", "pairManifestSha256", "comparability pairManifestSha256 is not the SHA-256 of pair-manifest.json"));
        var metricRuns = new[] { Load("metrics-run-1.json"), Load("metrics-run-2.json") };
        if (metricRuns[0]["pairs"]?.ToJsonString() != metricRuns[1]["pairs"]?.ToJsonString())
            v.Add(new("R005", "metrics", "the two metric runs are not bit-identical; no result may be produced"));
        foreach (var run in metricRuns)
        {
            if (Str(run["generatorVersion"]) != Str(registration["comparability"]?["generatorVersion"])
                || Str(run["metricImplementationVersion"]) != Str(registration["comparability"]?["metricImplementationVersion"])
                || Str(run["oracleResultSha256"]) != RawSha256(results["oracle-run-2.json"]))
                v.Add(new("R005", "metric-provenance", "a metric run does not name the registered generator, metric implementation, and committed oracle result"));
        }
        var values = Array(metricRuns[0]["pairs"]).OfType<JsonNode>().ToList();
        var includedIds = rows.Where(r => Bool(r["included"]) == true).Select(r => Str(r["pairId"])).Order(StringComparer.Ordinal).ToList();
        if (!values.Select(x => Str(x["pairId"])).SequenceEqual(includedIds))
            v.Add(new("R005", "included-set", "the metric runs do not cover exactly the included (EQUIVALENT) pairs"));
        // Every per-pair value is the pinned calculator's output on the registered files, in the registered category.
        var registeredById = registered.ToDictionary(p => Str(p["pairId"]) ?? "", StringComparer.Ordinal);
        foreach (var value in values)
        {
            var id = Str(value["pairId"]) ?? "";
            if (!registeredById.TryGetValue(id, out var pair))
                continue;
            var category = (Str(pair["taskStatement"]) ?? "").Split('\n').FirstOrDefault(l => l.StartsWith("category: ", StringComparison.Ordinal))?["category: ".Length..];
            var (name, calorSize, csharpSize, r) = recompute(pair);
            if (Str(value["category"]) != category || Str(value["metric"]) != name || Str(value["r"]) != F(r) || Str(value["calorSize"]) != F(calorSize)
                || Str(value["csharpSize"]) != F(csharpSize) || Str(value["rBits"]) != BitConverter.DoubleToInt64Bits(r).ToString("x16", CultureInfo.InvariantCulture))
                v.Add(new("R005", "metric-values", $"{id}: the committed value is not the pinned calculator's output on the registered files"));
        }
        if (summary["byDisposition"]?.ToJsonString() != Tally(rows.Select(r => Str(r["disposition"]) ?? "")).ToJsonString()
            || Int(summary["denominator"]) != registered.Count)
            v.Add(new("R005", "results.json", "disposition counts do not match the manifest"));
        // Every other published summary fact is derived from the manifest, the runs, and the registration.
        var witnessRow = rows.FirstOrDefault(r => Str(r["pairId"]) == "DomainProblems/CsvParser");
        var expectedSummary = new JsonObject
        {
            ["generatorVersion"] = registration["comparability"]?["generatorVersion"]?.DeepClone(),
            ["registrationPairManifestSha256"] = registrationSha,
            ["byDispositionAndReason"] = Tally(rows.Select(r => $"{Str(r["disposition"])} {Str(r["equivalenceEvidence"]?["reason"])}")),
            ["knownWitness"] = new JsonObject
            {
                ["pairId"] = "DomainProblems/CsvParser",
                ["disposition"] = Str(witnessRow?["disposition"]),
                ["reason"] = Str(witnessRow?["equivalenceEvidence"]?["reason"]),
            },
            ["determinism"] = new JsonObject
            {
                ["oracleRuns"] = 2,
                ["oracleResultValid"] = Bool(run2["valid"]),
                ["pairsChangedAcrossOracleRuns"] = rows.Count(r => Str(r["equivalenceEvidence"]?["reason"]) == "NONDETERMINISTIC_ACROSS_RUNS"),
                ["metricRuns"] = 2,
                ["metricRunsBitIdentical"] = metricRuns[0]["pairs"]?.ToJsonString() == metricRuns[1]["pairs"]?.ToJsonString(),
            },
            ["includedPerCategory"] = Tally(values.Select(x => Str(x["category"]) ?? "")),
        };
        foreach (var (field, expected) in expectedSummary)
        {
            if (summary[field]?.ToJsonString() != expected?.ToJsonString())
                v.Add(new("R005", "summary", $"results.json {field} is not derived from the manifest and runs"));
        }
        var metric = summary["metric"];
        // The declared population, sampling unit, and interval interpretation are the registered ones, word for word.
        var labels = new (JsonNode? Node, string Expected)[]
        {
            (metric?["metric"], "TokenEconomics/CompositeTokenEconomics"),
            (metric?["perPairValue"], "r = csharpSize / calorSize (r > 1: the C# file is larger; r < 1: the Calor file is larger)"),
            (metric?["population"], "registered manifest.benchmarks pairs dispositioned EQUIVALENT by the reconciled oracle result"),
            (metric?["samplingUnit"], Str(registration["statistics"]?["samplingUnit"]) ?? "?"),
            (metric?["intervalLabel"], Str(registration["statistics"]?["interval"]?["label"]) ?? "?"),
            (summary["label"], ResultsLabel),
        };
        if (labels.Any(l => Str(l.Node) != l.Expected))
            v.Add(new("R005", "labels", "the metric's population, sampling unit, or interval or claim label is not the registered wording"));
        if (metric?["overall"]?.ToJsonString() != Expected(values).ToJsonString())
            v.Add(new("R005", "overall", "the overall estimate or interval is not the registered computation over the included pairs"));
        foreach (var group in values.GroupBy(x => Str(x["category"]) ?? ""))
        {
            if (metric?["perCategory"]?[group.Key]?.ToJsonString() != Expected(group.ToList()).ToJsonString())
                v.Add(new("R005", group.Key, "the category estimate or interval is not the registered computation"));
        }
        if (metric?["perCategory"]?.AsObject().Count != values.Select(x => Str(x["category"])).Distinct().Count())
            v.Add(new("R005", "perCategory", "a category without included pairs is reported"));
        return v;
    }

    /// <summary>An independent implementation of registration.json statistics (estimate, interval, also-reported).</summary>
    private static JsonObject Expected(IReadOnlyList<JsonNode> group)
    {
        var r = group.OrderBy(x => Str(x["pairId"]), StringComparer.Ordinal).Select(x => double.Parse(Str(x["r"])!, CultureInfo.InvariantCulture)).ToList();
        static double Gm(IEnumerable<double> xs) { var (s, n) = (0.0, 0); foreach (var x in xs) (s, n) = (s + Math.Log(x), n + 1); return Math.Exp(s / n); }
        JsonArray? interval = null;
        if (r.Count >= 2)
        {
            var state = 1276UL;
            ulong Next() { var z = state += 0x9E3779B97F4A7C15UL; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
            var boot = Enumerable.Range(0, 10_000).Select(b => Gm(Enumerable.Range(0, r.Count).Select(i => r[(int)(Next() % (ulong)r.Count)]).ToList())).Order().ToList();
            interval = [F(boot[249]), F(boot[9749])];
        }
        var sorted = r.Order().ToList();
        return new JsonObject
        {
            ["pairs"] = r.Count,
            ["geometricMeanR"] = F(Gm(r)),
            ["interval95"] = interval,
            ["intervalNote"] = interval is null ? "no interval: fewer than 2 included pairs" : null,
            ["medianR"] = F(r.Count % 2 == 1 ? sorted[r.Count / 2] : (sorted[r.Count / 2 - 1] + sorted[r.Count / 2]) / 2),
            ["pairsRAbove1"] = r.Count(x => x > 1),
            ["pairsRBelow1"] = r.Count(x => x < 1),
            ["pairsREqual1"] = r.Count(x => x == 1),
        };
    }

    private static JsonObject Tally(IEnumerable<string> keys)
    {
        var counts = new JsonObject();
        foreach (var g in keys.GroupBy(k => k).OrderBy(g => g.Key, StringComparer.Ordinal))
            counts[g.Key] = g.Count();
        return counts;
    }

    private static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static string RawSha256(string text) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));
}
