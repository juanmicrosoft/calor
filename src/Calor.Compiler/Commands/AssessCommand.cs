using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Init;
using Calor.Compiler.Parsing;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for assessing C# codebases for Calor migration potential.
/// </summary>
public static class AssessCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<DirectoryInfo>(
            name: "path",
            description: "Directory to assess recursively")
        {
            Arity = ArgumentArity.ExactlyOne
        };

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "text",
            description: "Output format: text, json, or sarif");

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Output file (stdout if not specified)");

        var thresholdOption = new Option<int>(
            aliases: ["--threshold", "-t"],
            getDefaultValue: () => 0,
            description: "Minimum score to include (0-100)");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed per-file breakdown");

        var topOption = new Option<int>(
            aliases: ["--top", "-n"],
            getDefaultValue: () => 20,
            description: "Number of top files to show");

        var command = new Command("assess", "Assess C# files for Calor migration potential")
        {
            pathArgument,
            formatOption,
            outputOption,
            thresholdOption,
            verboseOption,
            topOption
        };

        // Add 'analyze' as alias for backwards compatibility
        command.AddAlias("analyze");

        command.SetHandler(ExecuteAsync, pathArgument, formatOption, outputOption, thresholdOption, verboseOption, topOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        DirectoryInfo path,
        string format,
        FileInfo? output,
        int threshold,
        bool verbose,
        int top)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("assess");
        if (telemetry != null)
        {
            var discovered = CalorConfigManager.Discover(path.FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();
        var exitCode = 0;

        // Envelope mode: stdout carries exactly one document, always —
        // including early exits (envelope schema v1.1 contract).
        var json = format.Equals("json", StringComparison.OrdinalIgnoreCase);

        if (!path.Exists)
        {
            if (json)
            {
                Console.WriteLine(EnvelopeWriter.Serialize("assess", null,
                    [new Diagnostic(
                        DiagnosticCode.CliInputNotFound,
                        DiagnosticSeverity.Error,
                        $"Directory not found: {path.FullName}",
                        path.FullName,
                        line: 1,
                        column: 1)]));
            }
            Console.Error.WriteLine($"Error: Directory not found: {path.FullName}");
            return 2;
        }

        if (threshold < 0 || threshold > 100)
        {
            if (json)
            {
                Console.WriteLine(EnvelopeWriter.Serialize("assess", null,
                    [new Diagnostic(
                        DiagnosticCode.CliUsageError,
                        DiagnosticSeverity.Error,
                        "Threshold must be between 0 and 100",
                        filePath: null,
                        line: 1,
                        column: 1)]));
            }
            Console.Error.WriteLine("Error: Threshold must be between 0 and 100");
            return 2;
        }

        try
        {
            var progress = new Progress<string>(msg =>
            {
                if (verbose)
                {
                    Console.Error.WriteLine(msg);
                }
            });

            var options = new MigrationAnalysisOptions
            {
                Verbose = verbose,
                Thresholds = new AnalysisThresholds
                {
                    MinimumScore = threshold,
                    TopFilesCount = top
                },
                Progress = progress
            };

            var analyzer = new MigrationAnalyzer(options);

            if (!verbose)
            {
                Console.Error.WriteLine($"Analyzing {path.FullName}...");
            }

            var result = await analyzer.AnalyzeDirectoryAsync(path.FullName);

            // Format output
            var formatted = FormatResult(result, format, threshold, top, verbose);

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

            // Return exit code based on findings
            exitCode = result.HasHighPriorityFiles ? 1 : 0;
            return exitCode;
        }
        catch (Exception ex)
        {
            if (json)
            {
                Console.WriteLine(EnvelopeWriter.Serialize("assess", null,
                    [new Diagnostic(
                        DiagnosticCode.CliInternalError,
                        DiagnosticSeverity.Error,
                        $"Assessment failed: {ex.Message}",
                        path.FullName,
                        line: 1,
                        column: 1)]));
            }
            Console.Error.WriteLine($"Error: {ex.Message}");
            telemetry?.TrackException(ex);
            exitCode = 2;
            return exitCode;
        }
        finally
        {
            sw.Stop();
            telemetry?.TrackCommand("assess", exitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString()
            });
            if (exitCode == 2)
            {
                IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "assess", "Assessment failed");
            }
        }
    }

    private static string FormatResult(ProjectAnalysisResult result, string format, int threshold, int top, bool verbose)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => FormatJson(result, threshold),
            "sarif" => FormatSarif(result, threshold),
            "text" or _ => FormatText(result, threshold, top, verbose)
        };
    }

    private static string FormatText(ProjectAnalysisResult result, int threshold, int top, bool verbose)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== Calor Migration Assessment ===");
        sb.AppendLine();

        // Summary
        sb.AppendLine($"Analyzed: {result.TotalFilesAnalyzed} files");
        sb.AppendLine($"Skipped: {result.TotalFilesSkipped} files (generated/errors)");
        sb.AppendLine($"Average Score: {result.AverageScore:F1}/100");
        sb.AppendLine();

        // Priority breakdown
        sb.AppendLine("Priority Breakdown:");
        var breakdown = result.PriorityBreakdown;
        sb.AppendLine($"  Critical (76-100): {breakdown.GetValueOrDefault(MigrationPriority.Critical, 0)} files");
        sb.AppendLine($"  High (51-75):      {breakdown.GetValueOrDefault(MigrationPriority.High, 0)} files");
        sb.AppendLine($"  Medium (26-50):    {breakdown.GetValueOrDefault(MigrationPriority.Medium, 0)} files");
        sb.AppendLine($"  Low (0-25):        {breakdown.GetValueOrDefault(MigrationPriority.Low, 0)} files");
        sb.AppendLine();

        // Average scores by dimension
        sb.AppendLine("Average Scores by Dimension:");
        var dimScores = result.AverageScoresByDimension
            .OrderByDescending(kv => kv.Value)
            .ToList();

        var maxDimNameLength = dimScores.Max(kv => kv.Key.ToString().Length);
        foreach (var (dimension, score) in dimScores)
        {
            var dimName = dimension.ToString().PadRight(maxDimNameLength);
            var bar = new string('#', (int)(score / 5)); // Scale to ~20 chars max
            sb.AppendLine($"  {dimName} {score,5:F1} |{bar}");
        }
        sb.AppendLine();

        // Top files
        var topFiles = result.GetFilesAboveThreshold(threshold).Take(top).ToList();
        sb.AppendLine($"Top {Math.Min(top, topFiles.Count)} Files for Migration:");
        sb.AppendLine(new string('-', 80));

        foreach (var file in topFiles)
        {
            var priorityLabel = $"[{FileMigrationScore.GetPriorityLabel(file.Priority)}]";
            sb.AppendLine($"{file.TotalScore,3:F0}/100 {priorityLabel,-10} {file.RelativePath}");

            if (verbose)
            {
                foreach (var (dimension, dimScore) in file.Dimensions.OrderByDescending(kv => kv.Value.WeightedScore))
                {
                    if (dimScore.PatternCount > 0)
                    {
                        sb.AppendLine($"         {dimension}: {dimScore.RawScore:F0} ({dimScore.PatternCount} patterns)");
                    }
                }
                sb.AppendLine();
            }
        }

        if (topFiles.Count == 0)
        {
            sb.AppendLine("  No files above threshold.");
        }

        return sb.ToString();
    }

    private static string FormatJson(ProjectAnalysisResult result, int threshold)
    {
        var output = new JsonOutput
        {
            AnalyzedAt = result.AnalyzedAt,
            RootPath = result.RootPath,
            DurationMs = (int)result.Duration.TotalMilliseconds,
            Summary = new JsonSummary
            {
                TotalFiles = result.TotalFilesAnalyzed,
                SkippedFiles = result.TotalFilesSkipped,
                AverageScore = Math.Round(result.AverageScore, 1),
                PriorityBreakdown = new JsonPriorityBreakdown
                {
                    Critical = result.PriorityBreakdown.GetValueOrDefault(MigrationPriority.Critical, 0),
                    High = result.PriorityBreakdown.GetValueOrDefault(MigrationPriority.High, 0),
                    Medium = result.PriorityBreakdown.GetValueOrDefault(MigrationPriority.Medium, 0),
                    Low = result.PriorityBreakdown.GetValueOrDefault(MigrationPriority.Low, 0)
                },
                AveragesByDimension = result.AverageScoresByDimension
                    .ToDictionary(kv => kv.Key.ToString(), kv => Math.Round(kv.Value, 1))
            },
            Files = result.GetFilesAboveThreshold(threshold)
                .Select(f => new JsonFileScore
                {
                    Path = f.RelativePath,
                    Score = Math.Round(f.TotalScore, 1),
                    Priority = FileMigrationScore.GetPriorityLabel(f.Priority).ToLower(),
                    LineCount = f.LineCount,
                    MethodCount = f.MethodCount,
                    TypeCount = f.TypeCount,
                    Dimensions = f.Dimensions.ToDictionary(
                        kv => kv.Key.ToString(),
                        kv => new JsonDimensionScore
                        {
                            Score = Math.Round(kv.Value.RawScore, 1),
                            Weight = kv.Value.Weight,
                            PatternCount = kv.Value.PatternCount,
                            Examples = kv.Value.Examples
                        })
                })
                .ToList()
        };

        // Envelope schema v1.1 (loop plan D1.3): assess emits no source-anchored
        // diagnostics, so the document carries an empty diagnostics array and the
        // assessment payload under data.
        var envelope = new EnvelopeDocument
        {
            Version = JsonDiagnosticFormatter.SchemaVersion,
            Command = "assess",
            Diagnostics = [],
            Summary = new EnvelopeSummary(),
            Data = output
        };

        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private sealed class EnvelopeDocument
    {
        public required string Version { get; init; }
        public required string Command { get; init; }
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }
        public required EnvelopeSummary Summary { get; init; }
        public required JsonOutput Data { get; init; }
    }

    /// <summary>
    /// SARIF output is produced by the shared <see cref="SarifDiagnosticFormatter"/>
    /// (single SARIF implementation for the whole CLI); assess results are mapped
    /// to <see cref="Diagnostic"/> instances with per-dimension rule IDs, and rule
    /// metadata (descriptions, migration-doc help URIs) is supplied via the
    /// formatter's provider hooks.
    /// </summary>
    private static string FormatSarif(ProjectAnalysisResult result, int threshold)
    {
        var diagnostics = result.GetFilesAboveThreshold(threshold)
            .SelectMany(f => f.Dimensions
                .Where(kv => kv.Value.PatternCount > 0)
                .Select(kv => new Diagnostic(
                    $"Calor-{kv.Key}",
                    $"Score: {kv.Value.RawScore:F0}/100. {kv.Value.PatternCount} patterns detected. " +
                    $"Examples: {string.Join(", ", kv.Value.Examples.Take(3))}",
                    new TextSpan(0, 0, 1, 1),
                    f.Priority switch
                    {
                        MigrationPriority.Critical => DiagnosticSeverity.Error,
                        MigrationPriority.High => DiagnosticSeverity.Warning,
                        _ => DiagnosticSeverity.Info
                    },
                    f.RelativePath)))
            .ToList();

        var formatter = new SarifDiagnosticFormatter(
            toolName: "calor-assess",
            ruleDescriptionProvider: ruleId =>
                Enum.TryParse<ScoreDimension>(ruleId.Replace("Calor-", ""), out var dimension)
                    ? GetDimensionDescription(dimension)
                    : "Calor migration opportunity",
            ruleHelpUriProvider: ruleId =>
                $"https://calor-lang.org/docs/migration/{ruleId.Replace("Calor-", "").ToLowerInvariant()}");

        return formatter.Format(diagnostics);
    }

    private static string GetDimensionDescription(ScoreDimension dimension) => dimension switch
    {
        ScoreDimension.ContractPotential => "Code would benefit from Calor contracts (preconditions/postconditions)",
        ScoreDimension.EffectPotential => "Code has side effects that Calor can track",
        ScoreDimension.NullSafetyPotential => "Code has null handling that Calor Option<T> can improve",
        ScoreDimension.ErrorHandlingPotential => "Code has error handling that Calor Result<T,E> can improve",
        ScoreDimension.PatternMatchPotential => "Code has pattern matching that benefits from Calor exhaustiveness",
        ScoreDimension.ApiComplexityPotential => "Public APIs lack documentation that Calor metadata requires",
        ScoreDimension.AsyncPotential => "Code uses async/await patterns that Calor handles differently",
        ScoreDimension.LinqPotential => "Code uses LINQ patterns that map to Calor collection operations",
        _ => "Calor migration opportunity"
    };

    // JSON output classes
    private sealed class JsonOutput
    {
        public DateTime AnalyzedAt { get; init; }
        public required string RootPath { get; init; }
        public int DurationMs { get; init; }
        public required JsonSummary Summary { get; init; }
        public required List<JsonFileScore> Files { get; init; }
    }

    private sealed class JsonSummary
    {
        public int TotalFiles { get; init; }
        public int SkippedFiles { get; init; }
        public double AverageScore { get; init; }
        public required JsonPriorityBreakdown PriorityBreakdown { get; init; }
        public required Dictionary<string, double> AveragesByDimension { get; init; }
    }

    private sealed class JsonPriorityBreakdown
    {
        public int Critical { get; init; }
        public int High { get; init; }
        public int Medium { get; init; }
        public int Low { get; init; }
    }

    private sealed class JsonFileScore
    {
        public required string Path { get; init; }
        public double Score { get; init; }
        public required string Priority { get; init; }
        public int LineCount { get; init; }
        public int MethodCount { get; init; }
        public int TypeCount { get; init; }
        public required Dictionary<string, JsonDimensionScore> Dimensions { get; init; }
    }

    private sealed class JsonDimensionScore
    {
        public double Score { get; init; }
        public double Weight { get; init; }
        public int PatternCount { get; init; }
        public required List<string> Examples { get; init; }
    }
}
