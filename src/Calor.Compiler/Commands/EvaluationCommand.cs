using System.CommandLine;
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

        return command;
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
