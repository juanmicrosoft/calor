using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Migration;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for checking C# feature support in Calor conversion.
/// Designed for AI coding agents to query feature compatibility.
/// </summary>
public static class FeatureCheckCommand
{
    public static Command Create()
    {
        var featureArgument = new Argument<string?>(
            name: "feature",
            description: "The C# feature to check (e.g., 'yield-return', 'async-await', 'linq-method')")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var listOption = new Option<bool>(
            aliases: ["--list", "-l"],
            description: "List all known features and their support levels");

        var levelOption = new Option<string?>(
            aliases: ["--level"],
            description: "Filter by support level: full, partial, notsupported, manualrequired");

        var command = new Command("feature-check", "Check if a C# feature is supported in Calor conversion")
        {
            featureArgument,
            listOption,
            levelOption
        };

        command.SetHandler(Execute, featureArgument, listOption, levelOption);

        return command;
    }

    private static void Execute(string? feature, bool list, string? level)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("feature-check");
        var sw = Stopwatch.StartNew();
        var exitCode = 0;

        try
        {
            if (list || string.IsNullOrEmpty(feature))
            {
                ListFeatures(level);
                return;
            }

            var info = FeatureSupport.GetFeatureInfo(feature);

            if (info == null)
            {
                // Feature not in registry - output as JSON for easy parsing
                var unknownResult = new FeatureCheckResult
                {
                    Feature = feature,
                    Found = false,
                    Supported = null,
                    SupportLevel = null,
                    Description = null,
                    Alternative = "Feature not in registry. It may be supported (basic C# features work), or check documentation."
                };
                Console.WriteLine(JsonSerializer.Serialize(unknownResult, JsonOptions));
                return;
            }

            var result = new FeatureCheckResult
            {
                Feature = info.Name,
                Found = true,
                Supported = info.Support == SupportLevel.Full || info.Support == SupportLevel.Partial,
                SupportLevel = info.Support.ToString().ToLowerInvariant(),
                Description = info.Description,
                Alternative = info.Workaround
            };

            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        catch (Exception ex)
        {
            telemetry?.TrackException(ex);
            exitCode = 1;
            throw;
        }
        finally
        {
            sw.Stop();
            telemetry?.TrackCommand("feature-check", exitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString(),
                ["feature"] = feature ?? "(list)",
                ["list"] = list.ToString()
            });
        }
    }

    private static void ListFeatures(string? levelFilter)
    {
        var features = FeatureSupport.GetAllFeatures();

        if (!string.IsNullOrEmpty(levelFilter))
        {
            if (Enum.TryParse<SupportLevel>(levelFilter, ignoreCase: true, out var parsedLevel))
            {
                features = features.Where(f => f.Support == parsedLevel);
            }
            else
            {
                Console.Error.WriteLine($"Unknown support level: {levelFilter}");
                Console.Error.WriteLine("Valid levels: full, partial, notsupported, manualrequired");
                Environment.ExitCode = 1;
                return;
            }
        }

        var grouped = features
            .GroupBy(f => f.Support)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key.ToString().ToLowerInvariant(),
                g => g.Select(f => new FeatureListItem
                {
                    Name = f.Name,
                    Description = f.Description,
                    Workaround = f.Workaround
                }).ToList()
            );

        var output = new FeatureListOutput
        {
            TotalFeatures = features.Count(),
            Features = grouped
        };

        Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class FeatureCheckResult
    {
        public required string Feature { get; init; }
        public bool Found { get; init; }
        public bool? Supported { get; init; }
        public string? SupportLevel { get; init; }
        public string? Description { get; init; }
        public string? Alternative { get; init; }
    }

    private sealed class FeatureListOutput
    {
        public int TotalFeatures { get; init; }
        public required Dictionary<string, List<FeatureListItem>> Features { get; init; }
    }

    private sealed class FeatureListItem
    {
        public required string Name { get; init; }
        public string? Description { get; init; }
        public string? Workaround { get; init; }
    }
}
