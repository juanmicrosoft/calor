using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Calor.Compiler.Experiments;

namespace Calor.Compiler.Commands;

/// <summary>
/// Top-level <c>calor evaluation</c> command — home for the research-plan evaluation
/// harness. Currently provides <c>registry</c> for querying
/// <c>docs/experiments/registry.json</c>. <c>ab</c> (paired baseline-vs-candidate
/// benchmark runner) will land in a later sub-phase of the type-system research plan.
/// </summary>
public static class EvaluationCommand
{
    public static Command Create()
    {
        var command = new Command("evaluation", "Evaluation harness for the Calor-native type-system research plan (Phase 0+).");

        command.AddCommand(CreateRegistryCommand());
        command.AddCommand(CreateRegistryValidateCommand());

        return command;
    }

    // ========================================================================
    // registry-validate subcommand — §5.0f tamper-evidence check
    // ========================================================================

    private static Command CreateRegistryValidateCommand()
    {
        var baseFileOption = new Option<FileInfo>(
            aliases: ["--base-file"],
            description: "Path to the base-branch version of registry.json (e.g., from `git show <base>:docs/experiments/registry.json`).")
        { IsRequired = true };

        var headFileOption = new Option<FileInfo>(
            aliases: ["--head-file"],
            description: "Path to the head (PR) version of registry.json.")
        { IsRequired = true };

        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Emit a JSON report on stdout in addition to human-readable output.",
            getDefaultValue: () => false);

        var command = new Command("registry-validate",
            "Validate registry.json append-only invariant: no existing entry's fields may change. Exits 0 on pass, non-zero on violation.")
        {
            baseFileOption,
            headFileOption,
            jsonOption
        };

        command.SetHandler(async (InvocationContext ctx) =>
        {
            await Task.Yield();
            var baseFile = ctx.ParseResult.GetValueForOption(baseFileOption)!;
            var headFile = ctx.ParseResult.GetValueForOption(headFileOption)!;
            var json = ctx.ParseResult.GetValueForOption(jsonOption);
            ctx.ExitCode = ExecuteRegistryValidate(baseFile, headFile, json);
        });
        return command;
    }

    private static int ExecuteRegistryValidate(FileInfo baseFile, FileInfo headFile, bool json)
    {
        var baseDoc = LoadRegistryDoc(baseFile.FullName);
        var headDoc = LoadRegistryDoc(headFile.FullName);

        var result = RegistryValidator.Validate(baseDoc, headDoc);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (result.IsValid)
            {
                Console.WriteLine($"OK: registry.json append-only invariant holds. {result.EntriesAdded} new entr{(result.EntriesAdded == 1 ? "y" : "ies")} added, no existing entries modified.");
            }
            else
            {
                Console.Error.WriteLine($"FAIL: registry.json append-only invariant violated: {result.Violations.Count} violation(s) detected.");
                foreach (var v in result.Violations)
                {
                    Console.Error.WriteLine($"  [{v.Kind}] {v.EntryId}: {v.Message}");
                }
                Console.Error.WriteLine();
                Console.Error.WriteLine("The registry is append-only. To correct a prior entry, add a new entry with supersedes=<predecessor-id>.");
            }
        }

        return result.IsValid ? 0 : 1;
    }

    private static RegistryDocument LoadRegistryDoc(string path)
    {
        if (!File.Exists(path))
            return new RegistryDocument();
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            return new RegistryDocument();
        return JsonSerializer.Deserialize<RegistryDocument>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
            ?? new RegistryDocument();
    }

    // ========================================================================
    // registry subcommand
    // ========================================================================

    private static Command CreateRegistryCommand()
    {
        var fileOption = new Option<FileInfo?>(
            aliases: ["--file", "-f"],
            description: "Path to the registry JSON file. Defaults to ./docs/experiments/registry.json.");

        var queryOption = new Option<string?>(
            aliases: ["--query", "-q"],
            description: "Query mode: current-state | two-kill-risk | held-owned-by | stale-holds | audit-trail")
        { IsRequired = true };

        var hypothesisOption = new Option<string?>(
            aliases: ["--hypothesis"],
            description: "Hypothesis ID (required for current-state, audit-trail).");

        var tupleOption = new Option<string?>(
            aliases: ["--tuple"],
            description: "Identity tuple for two-kill-risk: '<tag>/<code-class>/<direction>'.");

        var userOption = new Option<string?>(
            aliases: ["--user"],
            description: "GitHub username (required for held-owned-by).");

        var prettyOption = new Option<bool>(
            aliases: ["--pretty"],
            description: "Pretty-print JSON output (default: false — compact for machine consumption).",
            getDefaultValue: () => false);

        var command = new Command("registry", "Query the experimental hypothesis registry.")
        {
            fileOption,
            queryOption,
            hypothesisOption,
            tupleOption,
            userOption,
            prettyOption
        };

        command.SetHandler(ExecuteRegistry,
            fileOption, queryOption, hypothesisOption, tupleOption, userOption, prettyOption);

        return command;
    }

    private static async Task ExecuteRegistry(
        FileInfo? file,
        string? query,
        string? hypothesis,
        string? tuple,
        string? user,
        bool pretty)
    {
        await Task.Yield(); // keep signature async-compatible for future I/O

        var path = file?.FullName ?? Path.Combine(Directory.GetCurrentDirectory(), "docs", "experiments", "registry.json");
        var registry = Registry.Load(path);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = pretty };

        switch (query)
        {
            case "current-state":
                if (string.IsNullOrWhiteSpace(hypothesis))
                {
                    Console.Error.WriteLine("--hypothesis is required for current-state");
                    Environment.ExitCode = 2;
                    return;
                }
                Console.WriteLine(JsonSerializer.Serialize(registry.CurrentState(hypothesis), jsonOptions));
                break;

            case "two-kill-risk":
                if (string.IsNullOrWhiteSpace(tuple))
                {
                    Console.Error.WriteLine("--tuple is required for two-kill-risk (format: tag/code-class/direction)");
                    Environment.ExitCode = 2;
                    return;
                }
                var parts = tuple.Split('/', 3);
                if (parts.Length != 3)
                {
                    Console.Error.WriteLine("--tuple must have three slash-separated parts: tag/code-class/direction");
                    Environment.ExitCode = 2;
                    return;
                }
                var matches = registry.TwoKillRisk(parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
                Console.WriteLine(JsonSerializer.Serialize(new { matches }, jsonOptions));
                break;

            case "held-owned-by":
                if (string.IsNullOrWhiteSpace(user))
                {
                    Console.Error.WriteLine("--user is required for held-owned-by");
                    Environment.ExitCode = 2;
                    return;
                }
                var owned = registry.HeldOwnedBy(user);
                Console.WriteLine(JsonSerializer.Serialize(new { held = owned }, jsonOptions));
                break;

            case "stale-holds":
                var stale = registry.StaleHolds();
                Console.WriteLine(JsonSerializer.Serialize(new { stale }, jsonOptions));
                break;

            case "audit-trail":
                if (string.IsNullOrWhiteSpace(hypothesis))
                {
                    Console.Error.WriteLine("--hypothesis is required for audit-trail");
                    Environment.ExitCode = 2;
                    return;
                }
                var trail = registry.AuditTrail(hypothesis);
                Console.WriteLine(JsonSerializer.Serialize(new { trail }, jsonOptions));
                break;

            default:
                Console.Error.WriteLine($"Unknown --query value '{query}'. Valid: current-state | two-kill-risk | held-owned-by | stale-holds | audit-trail");
                Environment.ExitCode = 2;
                return;
        }
    }
}
