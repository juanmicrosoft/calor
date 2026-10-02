using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Calor.Evaluation.Equivalence;

/// <summary>
/// <c>pair-oracle</c>: assigns #1276 dispositions. Each UNCLASSIFIED registered pair runs in its own
/// child process, so a crash or hang in one arm cannot affect another pair; EXCLUDED-PRE-REGISTERED
/// pairs are recorded without running. Output has no timestamps, so two runs compare byte for byte.
/// </summary>
public static class PairOracleCommand
{
    public const int PairProcessTimeoutMs = 300_000;

    public static Command Create()
    {
        var command = new Command("pair-oracle", "Run the #1276 registered pair differential oracle");
        var registration = new Option<string>("--registration", "Registration directory (contains pairs.json)") { IsRequired = true };
        var commit = new Option<string>("--registration-commit", "Full SHA of the main commit that merged the registration") { IsRequired = true };
        var output = new Option<string>("--output", () => "pair-oracle-results.json", "Results file");
        var pair = new Option<string?>("--pair", "Evaluate one pair in-process and print its verdict (child mode)");
        foreach (var option in new Option[] { registration, commit, output, pair })
            command.AddOption(option);
        command.SetHandler(async (reg, sha, outPath, pairId) =>
        {
            Environment.ExitCode = pairId is null
                ? await RunAllAsync(reg, sha, outPath)
                : RunOne(reg, pairId);
        }, registration, commit, output, pair);
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

    private static async Task<int> RunAllAsync(string registrationDir, string registrationCommit, string outputPath)
    {
        if (!Regex.IsMatch(registrationCommit, "^[0-9a-f]{40}$"))
        {
            Console.Error.WriteLine("--registration-commit must be the full 40-hex SHA of the merged registration");
            return 2;
        }

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

        var document = new JsonObject
        {
            ["oracleId"] = PairDifferentialOracle.OracleId,
            ["oracleVersion"] = PairDifferentialOracle.OracleVersion,
            ["inputGeneratorVersion"] = PairDifferentialOracle.InputGeneratorVersion,
            ["registrationCommit"] = registrationCommit,
            ["pairsSha256"] = PairDifferentialOracle.Sha256Hex(File.ReadAllBytes(Path.Combine(registrationDir, "pairs.json"))),
            ["results"] = results,
        };
        await File.WriteAllTextAsync(outputPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return 0;
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
        using var cts = new CancellationTokenSource(PairProcessTimeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
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
