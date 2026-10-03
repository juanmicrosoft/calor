using System.CommandLine;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Evaluation.Benchmarks;
using Calor.Evaluation.Metrics;

namespace Calor.Evaluation.Equivalence;

/// <summary>
/// #1276 results generator <c>b1-1276-results-generator-v1</c>. <c>pair-metrics</c> runs the pinned
/// TokenEconomicsCalculator once over the pairs a valid reconciled oracle result marks EQUIVALENT (and
/// only those); it is run twice as a determinism check. <c>pair-results</c> builds the dispositioned,
/// content-addressed pair manifest and the aggregate exactly as <c>registration.json</c>
/// <c>statistics</c> specifies. Neither command reads any metric for a non-EQUIVALENT pair.
/// </summary>
public static class PairResultsCommand
{
    public const string GeneratorVersion = "b1-1276-results-generator-v1";
    public const ulong BootstrapSeed = 1276;
    public const int Resamples = 10_000;
    public const int LowerRank = 250, UpperRank = 9_750, MinimumPairs = 2;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static IEnumerable<Command> Create()
    {
        var registration = new Option<string>("--registration", "Registration directory") { IsRequired = true };
        var oracle = new Option<string>("--oracle-result", "The reconciled (second) pair-oracle output") { IsRequired = true };
        var output = new Option<string>("--output", "Output file") { IsRequired = true };
        var metrics = new Command("pair-metrics", "Compute CompositeTokenEconomics for EQUIVALENT pairs (#1276)");
        foreach (var option in new Option[] { registration, oracle, output })
            metrics.AddOption(option);
        metrics.SetHandler(async context => context.ExitCode = await RunMetricsAsync(
            context.ParseResult.GetValueForOption(registration)!, context.ParseResult.GetValueForOption(oracle)!, context.ParseResult.GetValueForOption(output)!));

        var firstRun = new Option<string>("--first-oracle-result", "The first pair-oracle output") { IsRequired = true };
        var metricRuns = new Option<string[]>("--metrics", "The two pair-metrics outputs") { IsRequired = true, AllowMultipleArgumentsPerToken = true };
        var results = new Command("pair-results", "Build the #1276 dispositioned pair manifest and aggregate");
        foreach (var option in new Option[] { registration, firstRun, oracle, metricRuns, output })
            results.AddOption(option);
        results.SetHandler(context =>
        {
            var p = context.ParseResult;
            var problem = BuildResults(p.GetValueForOption(registration)!, p.GetValueForOption(firstRun)!, p.GetValueForOption(oracle)!,
                p.GetValueForOption(metricRuns)!, p.GetValueForOption(output)!);
            if (problem is not null)
                Console.Error.WriteLine("pair-results refused: " + problem);
            context.ExitCode = problem is null ? 0 : 2;
        });
        return [metrics, results];
    }

    /// <summary>The pairs whose CompositeTokenEconomics value enters the comparative metric.</summary>
    internal static List<JsonNode> IncludedPairs(JsonArray registeredPairs, JsonNode oracleResult)
    {
        if (oracleResult["valid"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("the oracle result is not a valid reconciled result");
        var dispositions = oracleResult["results"]!.AsArray().ToDictionary(r => r!["PairId"]!.GetValue<string>(), r => r!["Disposition"]!.GetValue<string>());
        return registeredPairs.OfType<JsonNode>()
            .Where(p => Str(p, "source") == "manifest.benchmarks" && dispositions[Str(p, "pairId")] == "EQUIVALENT")
            .OrderBy(p => Str(p, "pairId"), StringComparer.Ordinal).ToList();
    }

    private static async Task<int> RunMetricsAsync(string registrationDir, string oraclePath, string outputPath)
    {
        var root = PairOracleCommand.RepoRoot(registrationDir);
        var metricSet = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(registrationDir, "metric-set.json")))!;
        foreach (var file in metricSet["implementationFiles"]!.AsArray())
        {
            if (Sha256Hex(await File.ReadAllBytesAsync(Path.Combine(root, Str(file!, "path")))) != Str(file!, "sha256"))
            {
                Console.Error.WriteLine($"pair-metrics refused: {Str(file!, "path")} differs from its registered hash");
                return 2;
            }
        }
        var rows = new JsonArray();
        foreach (var pair in IncludedPairs(Pairs(registrationDir), JsonNode.Parse(await File.ReadAllTextAsync(oraclePath))!))
        {
            var (calor, csharp) = (Path.Combine(root, Str(pair, "calorPath")), Path.Combine(root, Str(pair, "csharpPath")));
            if (Sha256Hex(await File.ReadAllBytesAsync(calor)) != Str(pair, "calorSha256") || Sha256Hex(await File.ReadAllBytesAsync(csharp)) != Str(pair, "csharpSha256"))
            {
                Console.Error.WriteLine($"pair-metrics refused: {Str(pair, "pairId")} differs from its registered bytes");
                return 2;
            }
            var result = await new TokenEconomicsCalculator().CalculateAsync(await TestDataAdapter.CreateContextFromFilesAsync(calor, csharp));
            var r = result.AdvantageRatio;
            rows.Add(new JsonObject
            {
                ["pairId"] = Str(pair, "pairId"),
                ["category"] = Category(pair),
                ["metric"] = result.MetricName,
                ["calorSize"] = R(result.CalorScore),
                ["csharpSize"] = R(result.CSharpScore),
                ["r"] = R(r),
                ["rBits"] = BitConverter.DoubleToInt64Bits(r).ToString("x16", CultureInfo.InvariantCulture),
            });
        }
        var document = new JsonObject
        {
            ["generatorVersion"] = GeneratorVersion,
            ["metricImplementationVersion"] = Str(metricSet, "implementationVersion"),
            ["oracleResultSha256"] = Sha256Hex(await File.ReadAllBytesAsync(oraclePath)),
            ["pairs"] = rows,
        };
        await File.WriteAllTextAsync(outputPath, document.ToJsonString(Indented) + "\n");
        Console.WriteLine($"pair-metrics: {rows.Count} EQUIVALENT benchmarks pairs");
        return 0;
    }

    /// <summary>Null on success; otherwise why no result is produced.</summary>
    internal static string? BuildResults(string registrationDir, string firstOraclePath, string oraclePath, string[] metricPaths, string outputDir)
    {
        var oracleResult = JsonNode.Parse(File.ReadAllText(oraclePath))!;
        if (oracleResult["valid"]?.GetValue<bool>() != true)
            return "the reconciled oracle result is invalid: " + oracleResult["problems"]?.ToJsonString();
        if (metricPaths.Length != 2)
            return "exactly two metric runs are registered";
        var runs = metricPaths.Select(p => JsonNode.Parse(File.ReadAllText(p))!["pairs"]!.AsArray()).ToList();
        if (runs[0].ToJsonString() != runs[1].ToJsonString())
            return "the two metric runs are not bit-identical; no result is produced";

        var registered = Pairs(registrationDir);
        var registrationPairsSha = TextSha256(File.ReadAllText(Path.Combine(registrationDir, "pairs.json")));
        var registration = JsonNode.Parse(File.ReadAllText(Path.Combine(registrationDir, "registration.json")))!;
        var byReason = registration["oracle"]!["dispositionByReason"]!.AsObject();
        var verdicts = oracleResult["results"]!.AsArray().ToDictionary(r => r!["PairId"]!.GetValue<string>(), r => r!);
        var included = IncludedPairs(registered, oracleResult).Select(p => Str(p, "pairId")).ToHashSet(StringComparer.Ordinal);
        var values = runs[0].OfType<JsonNode>().ToList();
        if (!values.Select(v => Str(v, "pairId")).SequenceEqual(included.Order(StringComparer.Ordinal)))
            return "the metric runs do not cover exactly the included pairs";

        var oracleSha = Sha256Hex(File.ReadAllBytes(oraclePath));
        var manifestPairs = new JsonArray();
        foreach (var pair in registered.OfType<JsonNode>())
        {
            var row = pair.DeepClone().AsObject();
            var verdict = verdicts[Str(pair, "pairId")];
            var (disposition, reason) = (verdict["Disposition"]!.GetValue<string>(), verdict["Reason"]!.GetValue<string>());
            if (byReason[reason]?.GetValue<string>() != disposition)
                return $"{Str(pair, "pairId")}: reason {reason} does not map to {disposition}";
            row["registeredDisposition"] = Str(pair, "disposition");
            row["disposition"] = disposition;
            row["included"] = included.Contains(Str(pair, "pairId"));
            row["equivalenceEvidence"] = new JsonObject
            {
                ["status"] = disposition == "EXCLUDED-PRE-REGISTERED" ? "not-run" : "oracle-executed",
                ["oracle"] = "b1-1276-pair-oracle@1",
                ["oracleResultSha256"] = oracleSha,
                ["reason"] = reason,
                ["inputCount"] = verdict["InputCount"]!.DeepClone(),
                ["observationsSha256"] = verdict["ObservationsSha256"]?.DeepClone(),
                ["surface"] = verdict["Surface"]!.DeepClone(),
                ["witnesses"] = verdict["Witnesses"]!.DeepClone(),
                ["detail"] = verdict["Detail"]!.DeepClone(),
            };
            row["reviewer"] = "b1-1276-pair-oracle@1 (automated disposition, reconciled over 2 runs); human review: the #1276 results pull request";
            manifestPairs.Add(row);
        }
        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["generatorVersion"] = GeneratorVersion,
            ["registrationPairManifestSha256"] = registrationPairsSha,
            ["registrationCommit"] = oracleResult["registrationCommit"]!.DeepClone(),
            ["firstOracleResultSha256"] = Sha256Hex(File.ReadAllBytes(firstOraclePath)),
            ["oracleResultSha256"] = oracleSha,
            ["pairCount"] = manifestPairs.Count,
            ["pairs"] = manifestPairs,
        };
        var manifestText = manifest.ToJsonString(Indented) + "\n";

        var comparability = registration["comparability"]!.DeepClone().AsObject();
        comparability["pairManifestSha256"] = TextSha256(manifestText);
        var dispositions = manifestPairs.OfType<JsonNode>().ToList();
        var summary = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["generatorVersion"] = GeneratorVersion,
            ["registrationPairManifestSha256"] = registrationPairsSha,
            ["comparability"] = comparability,
            ["denominator"] = dispositions.Count,
            ["byDisposition"] = Counts(dispositions, p => Str(p, "disposition")),
            ["byDispositionAndReason"] = Counts(dispositions, p => $"{Str(p, "disposition")} {p["equivalenceEvidence"]!["reason"]}"),
            ["knownWitness"] = new JsonObject
            {
                ["pairId"] = PairOracleCommand.KnownWitness,
                ["disposition"] = verdicts[PairOracleCommand.KnownWitness]["Disposition"]!.DeepClone(),
                ["reason"] = verdicts[PairOracleCommand.KnownWitness]["Reason"]!.DeepClone(),
            },
            ["determinism"] = new JsonObject
            {
                ["oracleRuns"] = 2,
                ["oracleResultValid"] = true,
                ["pairsChangedAcrossOracleRuns"] = dispositions.Count(p => p["equivalenceEvidence"]!["reason"]!.GetValue<string>() == "NONDETERMINISTIC_ACROSS_RUNS"),
                ["metricRuns"] = 2,
                ["metricRunsBitIdentical"] = true,
            },
            ["includedPerCategory"] = Counts(values, v => Str(v, "category")),
            ["metric"] = Aggregate(values),
            ["descriptiveOnlyMetrics"] = "not reported: metric-set.json makes reporting them optional, and this result reports none",
            ["label"] = "Describes this fixed, author-built corpus only. r is a static source-size ratio of two committed files, not a coding-agent outcome or a language advantage. EQUIVALENT means no observed disagreement on the registered finite inputs; it is not a proof and not a correctness claim about either arm.",
        };
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "pair-manifest.json"), manifestText);
        File.WriteAllText(Path.Combine(outputDir, "results.json"), summary.ToJsonString(Indented) + "\n");
        return null;
    }

    /// <summary>Point estimate, interval, and the registered also-reported values, overall and per category.</summary>
    internal static JsonObject Aggregate(IReadOnlyList<JsonNode> values)
    {
        JsonObject Summarize(IReadOnlyList<JsonNode> group)
        {
            var r = group.OrderBy(v => Str(v, "pairId"), StringComparer.Ordinal).Select(v => double.Parse(Str(v, "r"), CultureInfo.InvariantCulture)).ToList();
            var interval = Bootstrap(r);
            var sorted = r.Order().ToList();
            return new JsonObject
            {
                ["pairs"] = r.Count,
                ["geometricMeanR"] = r.Count == 0 ? null : R(GeometricMean(r, Enumerable.Range(0, r.Count))),
                ["interval95"] = interval is null ? null : new JsonArray(R(interval.Value.Low), R(interval.Value.High)),
                ["intervalNote"] = interval is null ? $"no interval: fewer than {MinimumPairs} included pairs" : null,
                ["medianR"] = r.Count == 0 ? null : R(r.Count % 2 == 1 ? sorted[r.Count / 2] : (sorted[r.Count / 2 - 1] + sorted[r.Count / 2]) / 2),
                ["pairsRAbove1"] = r.Count(x => x > 1),
                ["pairsRBelow1"] = r.Count(x => x < 1),
                ["pairsREqual1"] = r.Count(x => x == 1),
            };
        }
        var categories = new JsonObject();
        foreach (var group in values.GroupBy(v => Str(v, "category")).OrderBy(g => g.Key, StringComparer.Ordinal))
            categories[group.Key] = Summarize(group.ToList());
        return new JsonObject
        {
            ["metric"] = "TokenEconomics/CompositeTokenEconomics",
            ["perPairValue"] = "r = csharpSize / calorSize (r > 1: the C# file is larger; r < 1: the Calor file is larger)",
            ["population"] = "registered manifest.benchmarks pairs dispositioned EQUIVALENT by the reconciled oracle result",
            ["samplingUnit"] = "program-pair",
            ["intervalLabel"] = "corpus-resampling interval for this fixed corpus; not a confidence interval for any population",
            ["overall"] = Summarize(values),
            ["perCategory"] = categories,
        };
    }

    /// <summary>registration.json statistics.interval.algorithm, verbatim: SplitMix64(1276), 10,000 resamples, ranks 250 and 9,750.</summary>
    internal static (double Low, double High)? Bootstrap(IReadOnlyList<double> rInPairIdOrder)
    {
        var n = rInPairIdOrder.Count;
        if (n < MinimumPairs)
            return null;
        var rng = new InputGenerator.SplitMix64(BootstrapSeed);
        var estimates = new double[Resamples];
        for (var b = 0; b < Resamples; b++)
            estimates[b] = GeometricMean(rInPairIdOrder, Enumerable.Range(0, n).Select(_ => (int)(rng.NextUInt64() % (ulong)n)).ToList());
        Array.Sort(estimates);
        return (estimates[LowerRank - 1], estimates[UpperRank - 1]);
    }

    private static double GeometricMean(IReadOnlyList<double> r, IEnumerable<int> indices)
    {
        var (sum, count) = (0.0, 0);
        foreach (var i in indices)
            (sum, count) = (sum + Math.Log(r[i]), count + 1);
        return Math.Exp(sum / count);
    }

    private static JsonObject Counts(IEnumerable<JsonNode> rows, Func<JsonNode, string> key)
    {
        var counts = new JsonObject();
        foreach (var group in rows.GroupBy(key).OrderBy(g => g.Key, StringComparer.Ordinal))
            counts[group.Key] = group.Count();
        return counts;
    }

    private static string Category(JsonNode pair) =>
        Str(pair, "taskStatement").Split('\n').Single(l => l.StartsWith("category: ", StringComparison.Ordinal))["category: ".Length..];

    private static JsonArray Pairs(string registrationDir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(registrationDir, "pairs.json")))!["pairs"]!.AsArray();

    private static string Str(JsonNode node, string field) => node[field]?.GetValue<string>() ?? "";

    private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    internal static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string TextSha256(string text) => Sha256Hex(new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n")));
}
