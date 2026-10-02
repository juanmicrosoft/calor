using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Calor.Evaluation.Equivalence;

/// <summary>
/// <c>pair-oracle</c>: assigns #1276 dispositions. Each UNCLASSIFIED registered pair runs in its own
/// child process; EXCLUDED-PRE-REGISTERED pairs are recorded without running. Both modes refuse
/// (exit 2) unless the checkout is the clean registration commit with matching seals and pins. The
/// first run exits 4 (not a result); the second (<c>--compare-with</c> the first) reconciles the
/// runs and the known witness and exits 0 only when valid, otherwise 3.
/// </summary>
public static class PairOracleCommand
{
    public const int PairProcessTimeoutMs = 300_000;
    public const string KnownWitness = "DomainProblems/CsvParser";
    private static readonly string[] ProvenanceFields = ["oracleId", "oracleVersion", "inputGeneratorVersion", "registrationCommit", "pairsSha256", "environment"];

    public static Command Create()
    {
        var command = new Command("pair-oracle", "Run the #1276 registered pair differential oracle");
        var registration = new Option<string>("--registration", "Registration directory (contains pairs.json)") { IsRequired = true };
        var commit = new Option<string>("--registration-commit", "Full SHA of the main commit that merged the registration") { IsRequired = true };
        var output = new Option<string>("--output", "Results file, outside the repository") { IsRequired = true };
        var pair = new Option<string?>("--pair", "Evaluate one pair in-process and print its verdict (child mode)");
        var compareWith = new Option<string?>("--compare-with", "First run's results; makes this the reconciling second run");
        foreach (var option in new Option[] { registration, commit, output, pair, compareWith })
            command.AddOption(option);
        command.SetHandler(async context =>
        {
            var (reg, sha, pairId) = (context.ParseResult.GetValueForOption(registration)!, context.ParseResult.GetValueForOption(commit)!, context.ParseResult.GetValueForOption(pair));
            var problem = Preflight(reg, sha);
            if (problem is not null)
                Console.Error.WriteLine("pair-oracle refused: " + problem);
            context.ExitCode = problem is not null ? 2
                : pairId is not null ? RunOne(reg, pairId)
                : await RunAllAsync(reg, sha, context.ParseResult.GetValueForOption(output)!, context.ParseResult.GetValueForOption(compareWith));
        });
        return command;
    }

    private static int RunOne(string registrationDir, string pairId)
    {
        var row = Pairs(registrationDir).Single(p => p?["pairId"]?.GetValue<string>() == pairId)!;
        string Field(string name) => row[name]!.GetValue<string>();
        byte[] Read(string field) => File.ReadAllBytes(Path.Combine(RepoRoot(registrationDir), Field(field)));
        var verdict = PairDifferentialOracle.Evaluate(new OraclePairInput(pairId,
            Field("calorPath"), Read("calorPath"), Field("calorSha256"), Field("csharpPath"), Read("csharpPath"), Field("csharpSha256")));
        Console.Out.Flush();
        Console.WriteLine("@@VERDICT " + JsonSerializer.Serialize(verdict));
        return 0;
    }

    /// <summary>Null when the checkout is the clean registration commit and every seal and pin matches.</summary>
    internal static string? Preflight(string registrationDir, string registrationCommit)
    {
        if (!Regex.IsMatch(registrationCommit, "^[0-9a-f]{40}$"))
            return "--registration-commit must be a full 40-hex SHA";
        var root = RepoRoot(registrationDir);
        if (Git(root, "rev-parse", "HEAD").Trim() != registrationCommit)
            return "HEAD is not the registration commit";
        if (Git(root, "status", "--porcelain", "--untracked-files=all").Length > 0)
            return "the checkout is not clean: an edited or untracked file could change the compiler, oracle, or packet (write outputs outside the repository)";
        var seals = JsonNode.Parse(File.ReadAllText(Path.Combine(registrationDir, "sha256.json")))!["files"]!.AsObject();
        var registration = JsonNode.Parse(File.ReadAllText(Path.Combine(registrationDir, "registration.json")))!;
        var pins = seals.Select(f => (f.Key, f.Value!.GetValue<string>()))
            .Concat(registration["oracle"]!["implementation"]!.AsArray().Select(f => (f!["path"]!.GetValue<string>(), f["sha256"]!.GetValue<string>())));
        foreach (var (path, sha) in pins)
        {
            var full = Path.Combine(root, path);
            if (!File.Exists(full) || PairDifferentialOracle.Sha256Hex(Encoding.UTF8.GetBytes(File.ReadAllText(full).Replace("\r\n", "\n"))) != sha)
                return $"{path} does not match its registered hash";
        }
        return null;
    }

    /// <summary>
    /// Second-run reconciliation. The first run must have the same provenance and exactly the same
    /// pair ids; a pair whose verdict differs is UNCLASSIFIED (NONDETERMINISTIC_ACROSS_RUNS); a
    /// known witness that is not NOT-EQUIVALENT makes the result invalid.
    /// </summary>
    internal static (JsonArray Results, List<string> Problems) Reconcile(JsonObject header, JsonArray current, JsonNode? first)
    {
        var problems = new List<string>();
        var prior = first?["runResults"]?.AsArray().ToDictionary(r => r!["PairId"]!.GetValue<string>(), r => r!.ToJsonString());
        if (first is null || prior is null)
            problems.Add("single run: dispositions need a second run with --compare-with");
        foreach (var field in ProvenanceFields.Where(f => first is not null && first[f]?.ToJsonString() != header[f]?.ToJsonString()))
            problems.Add($"the first run's {field} differs; the runs are not of the same registration, oracle, and environment");
        var ids = current.Select(r => r!["PairId"]!.GetValue<string>()).ToList();
        if (prior is not null && !prior.Keys.Order(StringComparer.Ordinal).SequenceEqual(ids.Order(StringComparer.Ordinal)))
            problems.Add("the two runs cover different pairs");
        var results = new JsonArray();
        foreach (var row in current)
        {
            var id = row!["PairId"]!.GetValue<string>();
            results.Add(prior is not null && (!prior.TryGetValue(id, out var before) || before != row.ToJsonString())
                ? Node(new OracleVerdict(id, "UNCLASSIFIED", "NONDETERMINISTIC_ACROSS_RUNS", [], 0, null, [], "verdicts differ between the two runs"))
                : row.DeepClone());
        }
        if (results.FirstOrDefault(r => r!["PairId"]!.GetValue<string>() == KnownWitness)?["Disposition"]?.GetValue<string>() != "NOT-EQUIVALENT")
            problems.Add($"known witness {KnownWitness} is not NOT-EQUIVALENT; the oracle run is invalid");
        return (results, problems);
    }

    private static async Task<int> RunAllAsync(string registrationDir, string registrationCommit, string outputPath, string? firstRunPath)
    {
        var results = new JsonArray();
        foreach (var row in Pairs(registrationDir))
        {
            var pairId = row!["pairId"]!.GetValue<string>();
            var registered = row["disposition"]!.GetValue<string>();
            JsonNode verdict = registered switch
            {
                "EXCLUDED-PRE-REGISTERED" => Node(new OracleVerdict(pairId, registered, "PRE_REGISTERED_EXCLUSION", [], 0, null, [], row["exclusionId"]?.GetValue<string>() ?? "")),
                "UNCLASSIFIED" => await RunChildAsync(registrationDir, registrationCommit, pairId),
                _ => throw new InvalidOperationException($"pair {pairId}: registration disposition '{registered}' is not runnable"),
            };
            Console.WriteLine($"{pairId}: {verdict["Disposition"]} ({verdict["Reason"]})");
            results.Add(verdict);
        }

        var document = new JsonObject
        {
            ["oracleId"] = PairDifferentialOracle.OracleId,
            ["oracleVersion"] = PairDifferentialOracle.OracleVersion,
            ["inputGeneratorVersion"] = PairDifferentialOracle.InputGeneratorVersion,
            ["registrationCommit"] = registrationCommit,
            ["pairsSha256"] = PairDifferentialOracle.Sha256Hex(File.ReadAllBytes(Path.Combine(registrationDir, "pairs.json"))),
            ["environment"] = $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; Roslyn {typeof(Microsoft.CodeAnalysis.Compilation).Assembly.GetName().Version}",
        };
        var first = firstRunPath is null ? null : JsonNode.Parse(await File.ReadAllTextAsync(firstRunPath));
        var (reconciled, problems) = Reconcile(document, results, first);
        document["valid"] = problems.Count == 0;
        document["problems"] = new JsonArray(problems.Select(p => (JsonNode?)p).ToArray());
        document["runResults"] = results;
        document["results"] = reconciled;
        await File.WriteAllTextAsync(outputPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return first is null ? 4 : problems.Count == 0 ? 0 : 3;
    }

    private static async Task<JsonNode> RunChildAsync(string registrationDir, string registrationCommit, string pairId)
    {
        var host = Environment.ProcessPath!;
        var start = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(host) == "dotnet")
            start.ArgumentList.Add(typeof(PairOracleCommand).Assembly.Location);
        foreach (var arg in new[] { "pair-oracle", "--registration", registrationDir, "--registration-commit", registrationCommit, "--pair", pairId })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(PairProcessTimeoutMs))
        {
            process.Kill(entireProcessTree: true);
            return Node(new OracleVerdict(pairId, "UNCLASSIFIED", "TIMEOUT", [], 0, null, [], $"pair process exceeded {PairProcessTimeoutMs} ms"));
        }
        var line = (await stdout).Split('\n').LastOrDefault(l => l.StartsWith("@@VERDICT ", StringComparison.Ordinal));
        return process.ExitCode == 0 && line is not null
            ? JsonNode.Parse(line["@@VERDICT ".Length..])!
            : Node(new OracleVerdict(pairId, "UNCLASSIFIED", "ORACLE_CRASH", [], 0, null, [], $"pair process exit code {process.ExitCode}"));
    }

    internal static string Git(string root, params string[] args)
    {
        using var process = Process.Start(new ProcessStartInfo("git", ["-C", root, .. args]) { RedirectStandardOutput = true })!;
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? text : "<git failed>";
    }

    private static JsonNode Node(OracleVerdict verdict) => JsonSerializer.SerializeToNode(verdict)!;

    private static JsonArray Pairs(string registrationDir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(registrationDir, "pairs.json")))!["pairs"]!.AsArray();

    internal static string RepoRoot(string start)
    {
        var dir = Path.GetFullPath(start);
        while (!Directory.Exists(Path.Combine(dir, ".git")) && !File.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException("repository root not found above " + start);
        return dir;
    }
}
