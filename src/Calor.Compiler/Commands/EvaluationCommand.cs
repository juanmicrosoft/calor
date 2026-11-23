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

        command.Subcommands.Add(CreateRegistryCommand());
        command.Subcommands.Add(CreateRegistryValidateCommand());
        command.Subcommands.Add(CreateAbCommand());

        return command;
    }

    // ========================================================================
    // ab subcommand — §5.0c paired baseline-vs-candidate decision runner
    // ========================================================================

    private static Command CreateAbCommand()
    {
        var baselineOption = new Option<FileInfo>("--baseline-file")
        {
            Description = "JSON file with baseline-run per-program metric values (AbRun schema).",
            Required = true
        };

        var candidateOption = new Option<FileInfo>("--candidate-file")
        {
            Description = "JSON file with candidate-run per-program metric values (AbRun schema).",
            Required = true
        };

        var primaryMetricOption = new Option<string>("--primary-metric")
        {
            Description = "Name of the primary metric to gate on.",
            Required = true
        };

        var thresholdOption = new Option<double>("--threshold")
        {
            Description = "Minimum primary-metric relative effect (e.g., 0.15 = +15%).",
            Required = true
        };

        var directionOption = new Option<string>("--direction")
        {
            Description = "Direction of effect: up (candidate > baseline) or down (candidate < baseline).",
            DefaultValueFactory = _ => "up"
        };

        var guardOption = new Option<string[]>("--guard")
        {
            Description = "No-regression guard: 'MetricName=Tolerance', e.g., 'Comprehension=0.03'. Repeatable.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var resamplesOption = new Option<int>("--bootstrap-resamples")
        {
            Description = "Bootstrap resample count (default: 2000).",
            DefaultValueFactory = _ => 2000
        };

        var seedOption = new Option<int?>("--seed")
        {
            Description = "Random seed for reproducible bootstrap output."
        };

        var command = new Command("ab",
            "Paired baseline-vs-candidate evaluation. Emits a decision memo (PASS/FAIL/INCONCLUSIVE) with PROMOTE/HOLD/DROP recommendation.")
        {
            baselineOption, candidateOption, primaryMetricOption, thresholdOption,
            directionOption, guardOption, resamplesOption, seedOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            await Task.Yield();
            var baselineFile = parseResult.GetValue(baselineOption)!;
            var candidateFile = parseResult.GetValue(candidateOption)!;
            var primaryName = parseResult.GetValue(primaryMetricOption)!;
            var threshold = parseResult.GetValue(thresholdOption);
            var directionRaw = parseResult.GetValue(directionOption) ?? "up";
            var guardArgs = parseResult.GetValue(guardOption) ?? Array.Empty<string>();
            var resamples = parseResult.GetValue(resamplesOption);
            var seed = parseResult.GetValue(seedOption);

            return ExecuteAb(
                baselineFile, candidateFile, primaryName, threshold, directionRaw,
                guardArgs, resamples, seed);
        });

        return command;
    }

    private static int ExecuteAb(
        FileInfo baselineFile,
        FileInfo candidateFile,
        string primaryName,
        double threshold,
        string directionRaw,
        string[] guardArgs,
        int resamples,
        int? seed)
    {
        if (!Enum.TryParse<EffectDirection>(directionRaw, ignoreCase: true, out var direction))
        {
            Console.Error.WriteLine($"--direction must be 'up' or 'down', got '{directionRaw}'");
            return 2;
        }

        AbRun? baseline, candidate;
        try
        {
            baseline = JsonSerializer.Deserialize<AbRun>(
                File.ReadAllText(baselineFile.FullName),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            candidate = JsonSerializer.Deserialize<AbRun>(
                File.ReadAllText(candidateFile.FullName),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read input JSON: {ex.Message}");
            return 2;
        }

        if (baseline is null || candidate is null)
        {
            Console.Error.WriteLine("Baseline and candidate JSON files must parse to AbRun objects.");
            return 2;
        }

        var guards = new List<GuardSpec>();
        foreach (var arg in guardArgs)
        {
            var parts = arg.Split('=', 2);
            if (parts.Length != 2 || !double.TryParse(parts[1],
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var tol))
            {
                Console.Error.WriteLine($"--guard must be 'Name=Tolerance', got '{arg}'");
                return 2;
            }
            guards.Add(new GuardSpec(parts[0], tol));
        }

        var spec = new PrimaryMetricSpec(primaryName, direction, threshold);
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, guards, resamples, seed);

        // Always emit the decision memo as JSON on stdout — the agent consumes it.
        var opts = new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        Console.WriteLine(JsonSerializer.Serialize(memo, opts));

        // Non-zero exit on FAIL so scripts can branch on the CLI's outcome directly.
        return memo.Decision == "PASS" ? 0 : 1;
    }

    // ========================================================================
    // registry-validate subcommand — §5.0f tamper-evidence check
    // ========================================================================

    private static Command CreateRegistryValidateCommand()
    {
        var baseFileOption = new Option<FileInfo>("--base-file")
        {
            Description = "Path to the base-branch version of registry.json (e.g., from `git show <base>:docs/experiments/registry.json`).",
            Required = true
        };

        var headFileOption = new Option<FileInfo>("--head-file")
        {
            Description = "Path to the head (PR) version of registry.json.",
            Required = true
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit a JSON report on stdout in addition to human-readable output.",
            DefaultValueFactory = _ => false
        };

        var command = new Command("registry-validate",
            "Validate registry.json append-only invariant: no existing entry's fields may change. Exits 0 on pass, non-zero on violation.")
        {
            baseFileOption,
            headFileOption,
            jsonOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            await Task.Yield();
            var baseFile = parseResult.GetValue(baseFileOption)!;
            var headFile = parseResult.GetValue(headFileOption)!;
            var json = parseResult.GetValue(jsonOption);
            return ExecuteRegistryValidate(baseFile, headFile, json);
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
        var fileOption = new Option<FileInfo?>("--file", "-f")
        {
            Description = "Path to the registry JSON file. Defaults to ./docs/experiments/registry.json."
        };

        var queryOption = new Option<string?>("--query", "-q")
        {
            Description = "Query mode: current-state | two-kill-risk | held-owned-by | stale-holds | audit-trail",
            Required = true
        };

        var hypothesisOption = new Option<string?>("--hypothesis")
        {
            Description = "Hypothesis ID (required for current-state, audit-trail)."
        };

        var tupleOption = new Option<string?>("--tuple")
        {
            Description = "Identity tuple for two-kill-risk: '<tag>/<code-class>/<direction>'."
        };

        var userOption = new Option<string?>("--user")
        {
            Description = "GitHub username (required for held-owned-by)."
        };

        var prettyOption = new Option<bool>("--pretty")
        {
            Description = "Pretty-print JSON output (default: false — compact for machine consumption).",
            DefaultValueFactory = _ => false
        };

        var command = new Command("registry", "Query the experimental hypothesis registry.")
        {
            fileOption,
            queryOption,
            hypothesisOption,
            tupleOption,
            userOption,
            prettyOption
        };

        command.SetAction((parseResult, cancellationToken) => ExecuteRegistry(
            parseResult.GetValue(fileOption),
            parseResult.GetValue(queryOption),
            parseResult.GetValue(hypothesisOption),
            parseResult.GetValue(tupleOption),
            parseResult.GetValue(userOption),
            parseResult.GetValue(prettyOption)));

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
