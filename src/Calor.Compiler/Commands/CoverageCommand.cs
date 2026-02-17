using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for analyzing C# file coverage for Calor conversion.
/// Designed for AI coding agents to understand conversion feasibility.
/// </summary>
public static class CoverageCommand
{
    public static Command Create()
    {
        var fileArgument = new Argument<FileInfo>(
            name: "file",
            description: "The C# file to analyze for Calor conversion coverage")
        {
            Arity = ArgumentArity.ExactlyOne
        };

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Include detailed dimension scores and examples");

        var command = new Command("coverage", "Analyze a C# file for Calor conversion coverage and blockers")
        {
            fileArgument,
            verboseOption
        };

        command.SetHandler(ExecuteAsync, fileArgument, verboseOption);

        return command;
    }

    private static async Task ExecuteAsync(FileInfo file, bool verbose)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("coverage");
        var sw = Stopwatch.StartNew();
        var exitCode = 0;
        var blockerCount = 0;

        if (!file.Exists)
        {
            var errorResult = new CoverageResult
            {
                File = file.FullName,
                Success = false,
                Error = "File not found"
            };
            Console.WriteLine(JsonSerializer.Serialize(errorResult, JsonOptions));
            Environment.ExitCode = 1;
            exitCode = 1;
            telemetry?.TrackCommand("coverage", exitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString(),
                ["error"] = "file_not_found"
            });
            return;
        }

        try
        {
            var analyzer = new MigrationAnalyzer(new MigrationAnalysisOptions { Verbose = verbose });
            var score = await analyzer.AnalyzeFileAsync(file.FullName);

            if (score.WasSkipped)
            {
                var skippedResult = new CoverageResult
                {
                    File = file.FullName,
                    Success = false,
                    Error = score.SkipReason ?? "File was skipped"
                };
                Console.WriteLine(JsonSerializer.Serialize(skippedResult, JsonOptions));
                return;
            }

            // Calculate coverage percentage based on unsupported constructs
            // If no unsupported constructs, coverage is 100%
            // Each unsupported construct type reduces coverage
            var totalUnsupported = score.UnsupportedConstructs.Sum(c => c.Count);
            var coveragePercent = totalUnsupported == 0
                ? 100.0
                : Math.Max(0, 100 - (score.UnsupportedConstructs.Count * 15) - (totalUnsupported * 2));

            blockerCount = score.UnsupportedConstructs.Count;

            var result = new CoverageResult
            {
                File = file.FullName,
                Success = true,
                CoveragePercent = Math.Round(coveragePercent, 1),
                MigrationScore = Math.Round(score.TotalScore, 1),
                Priority = FileMigrationScore.GetPriorityLabel(score.Priority).ToLowerInvariant(),
                IsConvertible = score.UnsupportedConstructs.Count == 0,
                LineCount = score.LineCount,
                MethodCount = score.MethodCount,
                TypeCount = score.TypeCount,
                Blockers = score.UnsupportedConstructs.Select(c => new BlockerInfo
                {
                    Name = c.Name,
                    Description = c.Description,
                    Count = c.Count,
                    Examples = c.Examples.Take(3).ToList()
                }).ToList(),
                Dimensions = verbose ? score.Dimensions.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => new DimensionInfo
                    {
                        Score = Math.Round(kv.Value.RawScore, 1),
                        Weight = kv.Value.Weight,
                        PatternCount = kv.Value.PatternCount,
                        Examples = kv.Value.Examples
                    }) : null
            };

            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        catch (Exception ex)
        {
            var errorResult = new CoverageResult
            {
                File = file.FullName,
                Success = false,
                Error = ex.Message
            };
            Console.WriteLine(JsonSerializer.Serialize(errorResult, JsonOptions));
            Environment.ExitCode = 1;
            exitCode = 1;
            telemetry?.TrackException(ex);
        }
        finally
        {
            sw.Stop();
            telemetry?.TrackCommand("coverage", exitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString(),
                ["blockerCount"] = blockerCount.ToString(),
                ["verbose"] = verbose.ToString()
            });
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class CoverageResult
    {
        public required string File { get; init; }
        public bool Success { get; init; }
        public string? Error { get; init; }
        public double? CoveragePercent { get; init; }
        public double? MigrationScore { get; init; }
        public string? Priority { get; init; }
        public bool? IsConvertible { get; init; }
        public int? LineCount { get; init; }
        public int? MethodCount { get; init; }
        public int? TypeCount { get; init; }
        public List<BlockerInfo>? Blockers { get; init; }
        public Dictionary<string, DimensionInfo>? Dimensions { get; init; }
    }

    private sealed class BlockerInfo
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public int Count { get; init; }
        public required List<string> Examples { get; init; }
    }

    private sealed class DimensionInfo
    {
        public double Score { get; init; }
        public double Weight { get; init; }
        public int PatternCount { get; init; }
        public required List<string> Examples { get; init; }
    }
}
