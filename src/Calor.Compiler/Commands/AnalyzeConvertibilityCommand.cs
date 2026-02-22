using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Init;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for analyzing C# code convertibility to Calor.
/// </summary>
public static class AnalyzeConvertibilityCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "File or directory to analyze")
        {
            Arity = ArgumentArity.ExactlyOne
        };

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "text",
            description: "Output format: text or json");

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Output file (stdout if not specified)");

        var quickOption = new Option<bool>(
            aliases: ["--quick", "-q"],
            description: "Stage 1 only (no conversion attempt)");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show per-file breakdown for directories");

        var command = new Command("analyze-convertibility",
            "Analyze how likely C# code is to successfully convert to Calor")
        {
            pathArgument,
            formatOption,
            outputOption,
            quickOption,
            verboseOption
        };

        command.SetHandler(ExecuteAsync, pathArgument, formatOption, outputOption, quickOption, verboseOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string path,
        string format,
        FileInfo? output,
        bool quick,
        bool verbose)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("analyze-convertibility");
        var sw = Stopwatch.StartNew();
        var exitCode = 0;

        try
        {
            var analyzer = new ConvertibilityAnalyzer();
            string formatted;

            if (File.Exists(path))
            {
                // Single file mode
                var source = await File.ReadAllTextAsync(path);
                var result = quick
                    ? analyzer.AnalyzeQuick(source, path)
                    : analyzer.Analyze(source, path);

                formatted = format.ToLowerInvariant() switch
                {
                    "json" => FormatFileJson(result),
                    _ => FormatFileText(result)
                };
            }
            else if (Directory.Exists(path))
            {
                // Directory mode
                if (!quick && verbose)
                {
                    Console.Error.WriteLine($"Analyzing {path}...");
                }

                var result = await analyzer.AnalyzeDirectoryAsync(path, quick);

                formatted = format.ToLowerInvariant() switch
                {
                    "json" => FormatDirectoryJson(result),
                    _ => FormatDirectoryText(result, verbose)
                };
            }
            else
            {
                Console.Error.WriteLine($"Error: Path not found: {path}");
                return 2;
            }

            // Write output
            if (output != null)
            {
                await File.WriteAllTextAsync(output.FullName, formatted);
                Console.Error.WriteLine($"Analysis written to: {output.FullName}");
            }
            else
            {
                Console.WriteLine(formatted);
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            telemetry?.TrackException(ex);
            exitCode = 2;
            return exitCode;
        }
        finally
        {
            sw.Stop();
            telemetry?.TrackCommand("analyze-convertibility", exitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString()
            });
        }
    }

    private static string FormatFileText(ConvertibilityResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== Calor Convertibility Analysis ===");
        sb.AppendLine($"File: {result.FilePath}");
        sb.AppendLine($"Score: {result.Score}/100");
        sb.AppendLine($"Status: {GetStatusLabel(result.Score)}");
        sb.AppendLine();

        if (result.Blockers.Count > 0)
        {
            sb.AppendLine("Blockers:");
            foreach (var blocker in result.Blockers)
            {
                sb.AppendLine($"  {blocker.Name} ({blocker.Count} instance{(blocker.Count != 1 ? "s" : "")}): {blocker.Description}");
            }
            sb.AppendLine();
        }

        if (result.ConversionAttempted)
        {
            var convStatus = result.ConversionSucceeded ? "Success" : "Failed";
            var convDetail = result.ConversionSucceeded
                ? $" ({result.ConversionRate}% nodes converted)"
                : "";
            sb.AppendLine($"Conversion attempt: {convStatus}{convDetail}");

            if (result.ConversionSucceeded)
            {
                var compStatus = result.CompilationSucceeded ? "Success" : "Failed";
                sb.AppendLine($"Compilation: {compStatus}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("Conversion attempt: Skipped (quick mode)");
            sb.AppendLine();
        }

        sb.AppendLine($"Summary: {result.Summary}");

        return sb.ToString();
    }

    private static string FormatDirectoryText(DirectoryConvertibilityResult result, bool verbose)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== Calor Convertibility Analysis ===");
        sb.AppendLine($"Directory: {result.DirectoryPath}");
        sb.AppendLine($"Files analyzed: {result.TotalFiles}");
        sb.AppendLine();

        sb.AppendLine("Score Distribution:");
        sb.AppendLine($"  90-100 (High):    {result.HighCount} file{(result.HighCount != 1 ? "s" : "")}");
        sb.AppendLine($"  70-89 (Medium):   {result.MediumCount} file{(result.MediumCount != 1 ? "s" : "")}");
        sb.AppendLine($"  40-69 (Low):      {result.LowCount} file{(result.LowCount != 1 ? "s" : "")}");
        sb.AppendLine($"  0-39 (Blocked):   {result.BlockedCount} file{(result.BlockedCount != 1 ? "s" : "")}");
        sb.AppendLine();

        var aggregatedBlockers = result.GetAggregatedBlockers();
        if (aggregatedBlockers.Count > 0)
        {
            sb.AppendLine("Top Blockers:");
            foreach (var (name, totalInstances, fileCount) in aggregatedBlockers.Take(10))
            {
                sb.AppendLine($"  {name}: {totalInstances} instance{(totalInstances != 1 ? "s" : "")} across {fileCount} file{(fileCount != 1 ? "s" : "")}");
            }
            sb.AppendLine();
        }

        if (verbose || result.TotalFiles <= 20)
        {
            sb.AppendLine("Per-File Results:");
            foreach (var file in result.FileResults)
            {
                sb.AppendLine($"  {file.Score,3}/100  {file.FilePath}");
            }
        }
        else
        {
            sb.AppendLine($"Per-File Results (top 10 of {result.TotalFiles}):");
            foreach (var file in result.FileResults.Take(10))
            {
                sb.AppendLine($"  {file.Score,3}/100  {file.FilePath}");
            }
            if (result.TotalFiles > 10)
            {
                sb.AppendLine($"  ... ({result.TotalFiles - 10} more files)");
            }
        }

        return sb.ToString();
    }

    private static string FormatFileJson(ConvertibilityResult result)
    {
        var output = new JsonFileOutput
        {
            Score = result.Score,
            Summary = result.Summary,
            FilePath = result.FilePath,
            ConversionAttempted = result.ConversionAttempted,
            ConversionSucceeded = result.ConversionSucceeded,
            CompilationSucceeded = result.CompilationSucceeded,
            ConversionRate = result.ConversionRate,
            Blockers = result.Blockers.Select(b => new JsonBlocker
            {
                Name = b.Name,
                Description = b.Description,
                Count = b.Count
            }).ToList(),
            TotalBlockerInstances = result.TotalBlockerInstances,
            DurationMs = (int)result.Duration.TotalMilliseconds
        };

        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string FormatDirectoryJson(DirectoryConvertibilityResult result)
    {
        var output = new JsonDirectoryOutput
        {
            DirectoryPath = result.DirectoryPath,
            TotalFiles = result.TotalFiles,
            AverageScore = Math.Round(result.AverageScore, 1),
            Distribution = new JsonDistribution
            {
                High = result.HighCount,
                Medium = result.MediumCount,
                Low = result.LowCount,
                Blocked = result.BlockedCount
            },
            Files = result.FileResults.Select(r => new JsonFileOutput
            {
                Score = r.Score,
                Summary = r.Summary,
                FilePath = r.FilePath,
                ConversionAttempted = r.ConversionAttempted,
                ConversionSucceeded = r.ConversionSucceeded,
                CompilationSucceeded = r.CompilationSucceeded,
                ConversionRate = r.ConversionRate,
                Blockers = r.Blockers.Select(b => new JsonBlocker
                {
                    Name = b.Name,
                    Description = b.Description,
                    Count = b.Count
                }).ToList(),
                TotalBlockerInstances = r.TotalBlockerInstances,
                DurationMs = (int)r.Duration.TotalMilliseconds
            }).ToList(),
            DurationMs = (int)result.Duration.TotalMilliseconds
        };

        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string GetStatusLabel(int score) => score switch
    {
        >= 90 => "Highly convertible",
        >= 70 => "Likely convertible",
        >= 40 => "Partially convertible",
        _ => "Blocked"
    };

    // JSON output DTOs
    private sealed class JsonFileOutput
    {
        public int Score { get; init; }
        public required string Summary { get; init; }
        public required string FilePath { get; init; }
        public bool ConversionAttempted { get; init; }
        public bool ConversionSucceeded { get; init; }
        public bool CompilationSucceeded { get; init; }
        public double ConversionRate { get; init; }
        public required List<JsonBlocker> Blockers { get; init; }
        public int TotalBlockerInstances { get; init; }
        public int DurationMs { get; init; }
    }

    private sealed class JsonBlocker
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public int Count { get; init; }
    }

    private sealed class JsonDirectoryOutput
    {
        public required string DirectoryPath { get; init; }
        public int TotalFiles { get; init; }
        public double AverageScore { get; init; }
        public required JsonDistribution Distribution { get; init; }
        public required List<JsonFileOutput> Files { get; init; }
        public int DurationMs { get; init; }
    }

    private sealed class JsonDistribution
    {
        public int High { get; init; }
        public int Medium { get; init; }
        public int Low { get; init; }
        public int Blocked { get; init; }
    }
}
