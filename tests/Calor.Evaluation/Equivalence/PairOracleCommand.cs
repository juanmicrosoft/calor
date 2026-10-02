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
/// child process, so a crash or hang in one arm cannot affect another pair; EXCLUDED-PRE-REGISTERED
/// pairs are recorded without running. Before anything runs, both modes check that HEAD is the
/// registration commit, the packet matches its seals, and the oracle sources match their pins. The
/// second run (<c>--compare-with</c> the first) reconciles the runs and the known-witness control
/// and exits nonzero unless the result is valid.
/// </summary>
public static class PairOracleCommand
{
    public const int PairProcessTimeoutMs = 300_000;
    public const string KnownWitness = "DomainProblems/CsvParser";

    public static Command Create()
    {
        var command = new Command("pair-oracle", "Run the #1276 registered pair differential oracle");
        var registration = new Option<string>("--registration", "Registration directory (contains pairs.json)") { IsRequired = true };
        var commit = new Option<string>("--registration-commit", "Full SHA of the main commit that merged the registration") { IsRequired = true };
        var output = new Option<string>("--output", () => "pair-oracle-results.json", "Results file");
        var pair = new Option<string?>("--pair", "Evaluate one pair in-process and print its verdict (child mode)");
        var compareWith = new Option<string?>("--compare-with", "First run's results; makes this the reconciling second run");
        foreach (var option in new Option[] { registration, commit, output, pair, compareWith })
            command.AddOption(option);
        command.SetHandler(async (reg, sha, outPath, pairId, first) =>
        {
            var problem = Preflight(reg, sha);
            if (problem is not null)
                Console.Error.WriteLine("pair-oracle refused: " + problem);
            Environment.ExitCode = problem is not null ? 2
                : pairId is null ? await RunAllAsync(reg, sha, outPath, first)
                : RunOne(reg, pairId);
        }, registration, commit, output, pair, compareWith);
        return command;
    }

    private static int RunOne(string registrationDir, string pairId)
    {
        var row = Pairs(registrationDir).Single(p => p?["pairId"]?.GetValue<string>() == pairId)!;
        var root = RepoRoot(registrationDir);
        string Rel(string field) => Path.Combine(root, row[field]!.GetValue<string>());
        var verdict = PairDifferentialOracle.Evaluate(new OraclePairInput(
            pairId,
            row["calorPath"]!.GetValue<string>(), File.ReadAllBytes(Rel("calorPath")), row["calorSha256"]!.GetValue<string>(),
            row["csharpPath"]!.GetValue<string>(), File.ReadAllBytes(Rel("csharpPath")), row["csharpSha256"]!.GetValue<string>()));
        Console.Out.Flush();
        Console.WriteLine("@@VERDICT " + JsonSerializer.Serialize(verdict));
        return 0;
    }

    /// <summary>Null when the executing tree is the sealed, pinned registration at its merge commit.</summary>
    internal static string? Preflight(string registrationDir, string registrationCommit)
    {
        if (!Regex.IsMatch(registrationCommit, "^[0-9a-f]{40}$"))
            return "--registration-commit must be a full 40-hex SHA";
        var root = RepoRoot(registrationDir);
        var head = Process.Start(new ProcessStartInfo("git", ["-C", root, "rev-parse", "HEAD"]) { RedirectStandardOutput = true })!;
        if (head.StandardOutput.ReadToEnd().Trim() != registrationCommit)
            return "HEAD is not the registration commit";
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
    /// Second-run reconciliation: a pair whose verdict differs between the runs is UNCLASSIFIED
    /// (NONDETERMINISTIC_ACROSS_RUNS); a missing second run or a known witness that is not
    /// NOT-EQUIVALENT makes the whole result invalid.
    /// </summary>
    internal static (JsonArray Results, List<string> Problems) Reconcile(JsonArray current, JsonArray? first)
    {
        var problems = new List<string>();
        var prior = first?.ToDictionary(r => r!["PairId"]!.GetValue<string>(), r => r!.ToJsonString());
        if (prior is null)
            problems.Add("single run: dispositions need a second run with --compare-with");
        else if (prior.Count != current.Count)
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
                _ => throw new InvalidOperationException($"pair {pairId}: registration disposition '{registered}' is not runnable; dispositions are assigned only by this command"),
            };
            Console.WriteLine($"{pairId}: {verdict["Disposition"]} ({verdict["Reason"]})");
            results.Add(verdict);
        }

        var first = firstRunPath is null ? null : JsonNode.Parse(await File.ReadAllTextAsync(firstRunPath))!["runResults"]!.AsArray();
        var (reconciled, problems) = Reconcile(results, first);
        var document = new JsonObject
        {
            ["oracleId"] = PairDifferentialOracle.OracleId,
            ["oracleVersion"] = PairDifferentialOracle.OracleVersion,
            ["inputGeneratorVersion"] = PairDifferentialOracle.InputGeneratorVersion,
            ["registrationCommit"] = registrationCommit,
            ["pairsSha256"] = PairDifferentialOracle.Sha256Hex(File.ReadAllBytes(Path.Combine(registrationDir, "pairs.json"))),
            ["environment"] = $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; Roslyn {typeof(Microsoft.CodeAnalysis.Compilation).Assembly.GetName().Version}",
            ["valid"] = problems.Count == 0,
            ["problems"] = new JsonArray(problems.Select(p => (JsonNode?)p).ToArray()),
            ["runResults"] = results,
            ["results"] = reconciled,
        };
        await File.WriteAllTextAsync(outputPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return first is null || problems.Count == 0 ? 0 : 3;
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

    private static JsonNode Node(OracleVerdict verdict) => JsonSerializer.SerializeToNode(verdict)!;

    private static JsonArray Pairs(string registrationDir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(registrationDir, "pairs.json")))!["pairs"]!.AsArray();

    private static string RepoRoot(string start)
    {
        var dir = Path.GetFullPath(start);
        while (!Directory.Exists(Path.Combine(dir, ".git")) && !File.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException("repository root not found above " + start);
        return dir;
    }
}
