using System.CommandLine;
using System.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for standalone benchmarking with full 7-metric evaluation.
/// </summary>
public static class BenchmarkCommand
{
    public static Command Create()
    {
        var calorOption = new Option<FileInfo?>(
            aliases: new[] { "--calor" },
            description: "The Calor file to benchmark");

        var csharpOption = new Option<FileInfo?>(
            aliases: new[] { "--csharp", "--cs" },
            description: "The C# file to benchmark");

        var projectArgument = new Argument<DirectoryInfo?>(
            name: "project",
            description: "The project directory to benchmark (optional)")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var categoryOption = new Option<string?>(
            aliases: new[] { "--category", "-c" },
            description: "Filter by category (TokenEconomics, GenerationAccuracy, Comprehension, EditPrecision, ErrorDetection, InformationDensity, TaskCompletion)");

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            description: "Output format (console, markdown, json)",
            getDefaultValue: () => "console");

        var outputOption = new Option<FileInfo?>(
            aliases: new[] { "--output", "-o" },
            description: "Save benchmark results to file");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Show detailed per-metric breakdown");

        var quickOption = new Option<bool>(
            aliases: new[] { "--quick", "-q" },
            description: "Use quick token-only benchmark (skip full 7-metric evaluation)");

        var command = new Command("benchmark", "Compare Calor vs C# across 7 evaluation categories")
        {
            projectArgument,
            calorOption,
            csharpOption,
            categoryOption,
            formatOption,
            outputOption,
            verboseOption,
            quickOption
        };

        command.SetHandler(ExecuteAsync, projectArgument, calorOption, csharpOption, categoryOption, formatOption, outputOption, verboseOption, quickOption);

        return command;
    }

    private static async Task ExecuteAsync(
        DirectoryInfo? project,
        FileInfo? calorFile,
        FileInfo? csharpFile,
        string? category,
        string format,
        FileInfo? output,
        bool verbose,
        bool quick)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("benchmark");
        var sw = Stopwatch.StartNew();

        try
        {
            // Validate category if provided
            if (category != null && !BenchmarkIntegration.AllCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Error: Invalid category '{category}'");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Valid categories:");
                foreach (var cat in BenchmarkIntegration.AllCategories)
                {
                    Console.Error.WriteLine($"  - {cat}");
                }
                Environment.ExitCode = 1;
                return;
            }

            if (calorFile != null && csharpFile != null)
            {
                if (quick)
                {
                    // Legacy quick benchmark
                    await QuickBenchmarkFilesAsync(calorFile, csharpFile, format, output);
                }
                else
                {
                    // Full 7-metric benchmark
                    await FullBenchmarkFilesAsync(calorFile, csharpFile, category, format, output, verbose);
                }
            }
            else if (project != null)
            {
                if (quick)
                {
                    // Legacy quick project benchmark
                    await QuickBenchmarkProjectAsync(project, format, output);
                }
                else
                {
                    // Full 7-metric project benchmark
                    await FullBenchmarkProjectAsync(project, category, format, output, verbose);
                }
            }
            else
            {
                Console.Error.WriteLine("Error: Provide either --calor and --csharp files, or a project directory.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Examples:");
                Console.Error.WriteLine("  calor benchmark --calor file.calr --csharp file.cs");
                Console.Error.WriteLine("  calor benchmark --calor file.calr --csharp file.cs --verbose");
                Console.Error.WriteLine("  calor benchmark --calor file.calr --csharp file.cs --category TokenEconomics");
                Console.Error.WriteLine("  calor benchmark ./MyProject");
                Console.Error.WriteLine("  calor benchmark ./MyProject --format markdown -o report.md");
                Console.Error.WriteLine("  calor benchmark --calor file.calr --csharp file.cs --quick  # Token-only");
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            telemetry?.TrackException(ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            sw.Stop();
            telemetry?.TrackCommand("benchmark", Environment.ExitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString()
            });
            if (Environment.ExitCode != 0)
            {
                IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "benchmark", "Benchmark failed");
            }
        }
    }

    private static async Task FullBenchmarkFilesAsync(
        FileInfo calorFile,
        FileInfo csharpFile,
        string? category,
        string format,
        FileInfo? output,
        bool verbose)
    {
        if (!calorFile.Exists)
        {
            Console.Error.WriteLine($"Error: Calor file not found: {calorFile.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        if (!csharpFile.Exists)
        {
            Console.Error.WriteLine($"Error: C# file not found: {csharpFile.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        var calorSource = await File.ReadAllTextAsync(calorFile.FullName);
        var csharpSource = await File.ReadAllTextAsync(csharpFile.FullName);

        if (verbose)
        {
            Console.WriteLine($"Running full benchmark: {csharpFile.Name} vs {calorFile.Name}");
            if (category != null)
            {
                Console.WriteLine($"Filtering by category: {category}");
            }
            Console.WriteLine();
        }

        var result = await BenchmarkIntegration.RunFullBenchmarkAsync(csharpSource, calorSource, category, verbose);

        var outputContent = format.ToLowerInvariant() switch
        {
            "markdown" or "md" => BenchmarkIntegration.GenerateMarkdownReport(result, calorFile.Name, csharpFile.Name),
            "json" => BenchmarkIntegration.GenerateJsonReport(result),
            _ => BenchmarkIntegration.FormatConsoleOutput(result, calorFile.Name, csharpFile.Name, verbose)
        };

        if (output != null)
        {
            await File.WriteAllTextAsync(output.FullName, outputContent);
            Console.WriteLine($"Results saved to: {output.FullName}");
        }
        else
        {
            Console.WriteLine(outputContent);
        }
    }

    private static async Task FullBenchmarkProjectAsync(
        DirectoryInfo project,
        string? category,
        string format,
        FileInfo? output,
        bool verbose)
    {
        if (!project.Exists)
        {
            Console.Error.WriteLine($"Error: Project directory not found: {project.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Scanning project: {project.FullName}");
        if (category != null)
        {
            Console.WriteLine($"Filtering by category: {category}");
        }
        Console.WriteLine();

        var result = await BenchmarkIntegration.RunProjectBenchmarkAsync(project.FullName, category, verbose);

        if (result.ProjectResults == null || result.ProjectResults.Count == 0)
        {
            Console.WriteLine("No paired .calr and .cs files found.");
            Console.WriteLine("Looking for files with the same base name (e.g., foo.calr and foo.cs)");
            return;
        }

        var outputContent = format.ToLowerInvariant() switch
        {
            "markdown" or "md" => BenchmarkIntegration.GenerateMarkdownReport(result, project.Name, project.Name),
            "json" => BenchmarkIntegration.GenerateJsonReport(result),
            _ => BenchmarkIntegration.FormatProjectConsoleOutput(result, project.Name, verbose)
        };

        if (output != null)
        {
            await File.WriteAllTextAsync(output.FullName, outputContent);
            Console.WriteLine($"Results saved to: {output.FullName}");
        }
        else
        {
            Console.WriteLine(outputContent);
        }
    }

    // ========== Legacy quick benchmark methods ==========

    private static async Task QuickBenchmarkFilesAsync(FileInfo calorFile, FileInfo csharpFile, string format, FileInfo? output)
    {
        if (!calorFile.Exists)
        {
            Console.Error.WriteLine($"Error: Calor file not found: {calorFile.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        if (!csharpFile.Exists)
        {
            Console.Error.WriteLine($"Error: C# file not found: {csharpFile.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        var calorSource = await File.ReadAllTextAsync(calorFile.FullName);
        var csharpSource = await File.ReadAllTextAsync(csharpFile.FullName);

        var result = BenchmarkIntegration.RunQuickBenchmark(csharpSource, calorSource);

        var outputContent = format.ToLowerInvariant() switch
        {
            "markdown" or "md" => FormatQuickMarkdown(calorFile.Name, csharpFile.Name, result),
            "json" => FormatQuickJson(calorFile.Name, csharpFile.Name, result),
            _ => FormatQuickConsole(calorFile.Name, csharpFile.Name, result)
        };

        if (output != null)
        {
            await File.WriteAllTextAsync(output.FullName, outputContent);
            Console.WriteLine($"Results saved to: {output.FullName}");
        }
        else
        {
            Console.WriteLine(outputContent);
        }
    }

    private static async Task QuickBenchmarkProjectAsync(DirectoryInfo project, string format, FileInfo? output)
    {
        if (!project.Exists)
        {
            Console.Error.WriteLine($"Error: Project directory not found: {project.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Scanning project (quick mode): {project.FullName}");
        Console.WriteLine();

        // Find paired files
        var calorFiles = Directory.GetFiles(project.FullName, "*.calr", SearchOption.AllDirectories);
        var pairs = new List<(string calor, string cs, FileMetrics metrics)>();

        foreach (var calorPath in calorFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(calorPath);
            var directory = Path.GetDirectoryName(calorPath) ?? ".";

            // Look for matching C# file
            var csPath = Path.Combine(directory, baseName + ".cs");
            var gcsPath = Path.Combine(directory, baseName + ".g.cs");

            string? matchingCs = null;
            if (File.Exists(csPath))
                matchingCs = csPath;
            else if (File.Exists(gcsPath))
                matchingCs = gcsPath;

            if (matchingCs != null)
            {
                var calorSource = await File.ReadAllTextAsync(calorPath);
                var csSource = await File.ReadAllTextAsync(matchingCs);
                var metrics = BenchmarkIntegration.CalculateMetrics(csSource, calorSource);

                pairs.Add((calorPath, matchingCs, metrics));
            }
        }

        if (pairs.Count == 0)
        {
            Console.WriteLine("No paired .calr and .cs files found.");
            Console.WriteLine("Looking for files with the same base name (e.g., foo.calr and foo.cs)");
            return;
        }

        // Calculate summary
        var summary = BenchmarkIntegration.CreateSummary(pairs.Select(p => p.metrics));

        // Output results
        var outputContent = format.ToLowerInvariant() switch
        {
            "markdown" or "md" => FormatQuickProjectMarkdown(project.Name, pairs, summary),
            "json" => FormatQuickProjectJson(project.Name, pairs, summary),
            _ => FormatQuickProjectConsole(project.Name, pairs, summary)
        };

        if (output != null)
        {
            await File.WriteAllTextAsync(output.FullName, outputContent);
            Console.WriteLine($"Results saved to: {output.FullName}");
        }
        else
        {
            Console.WriteLine(outputContent);
        }
    }

    private static string FormatQuickConsole(string calorName, string csName, BenchmarkResult result)
    {
        return $"""
            Benchmark (Quick Mode): {csName} vs {calorName}

            {result.Summary}
            """;
    }

    private static string FormatQuickMarkdown(string calorName, string csName, BenchmarkResult result)
    {
        var m = result.Metrics;
        return $"""
            # Benchmark Results (Quick Mode)

            **Comparison:** `{csName}` vs `{calorName}`

            | Metric | C# | Calor | Savings |
            |--------|-----|------|---------|
            | Tokens | {m.OriginalTokens:N0} | {m.OutputTokens:N0} | {m.TokenReduction:F1}% |
            | Lines | {m.OriginalLines:N0} | {m.OutputLines:N0} | {m.LineReduction:F1}% |
            | Characters | {m.OriginalCharacters:N0} | {m.OutputCharacters:N0} | {m.CharReduction:F1}% |

            **Overall Calor Advantage:** {result.AdvantageRatio:F2}x
            """;
    }

    private static string FormatQuickJson(string calorName, string csName, BenchmarkResult result)
    {
        var m = result.Metrics;
        return $$"""
            {
              "mode": "quick",
              "comparison": {
                "csharpFile": "{{csName}}",
                "calorFile": "{{calorName}}"
              },
              "metrics": {
                "tokens": { "csharp": {{m.OriginalTokens}}, "calor": {{m.OutputTokens}}, "savings": {{m.TokenReduction:F1}} },
                "lines": { "csharp": {{m.OriginalLines}}, "calor": {{m.OutputLines}}, "savings": {{m.LineReduction:F1}} },
                "characters": { "csharp": {{m.OriginalCharacters}}, "calor": {{m.OutputCharacters}}, "savings": {{m.CharReduction:F1}} }
              },
              "advantageRatio": {{result.AdvantageRatio:F2}}
            }
            """;
    }

    private static string FormatQuickProjectConsole(string projectName, List<(string calor, string cs, FileMetrics metrics)> pairs, BenchmarkSummary summary)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Project Benchmark (Quick Mode): {projectName}");
        sb.AppendLine($"Files compared: {pairs.Count}");
        sb.AppendLine();
        sb.AppendLine("Summary:");
        sb.AppendLine($"  Total tokens: {summary.TotalOriginalTokens:N0} -> {summary.TotalOutputTokens:N0} ({summary.TokenSavingsPercent:F1}% savings)");
        sb.AppendLine($"  Total lines: {summary.TotalOriginalLines:N0} -> {summary.TotalOutputLines:N0} ({summary.LineSavingsPercent:F1}% savings)");
        sb.AppendLine($"  Overall Calor advantage: {summary.OverallAdvantage:F2}x");
        sb.AppendLine();
        sb.AppendLine("By File:");

        foreach (var (calor, cs, metrics) in pairs.OrderByDescending(p => BenchmarkIntegration.CalculateAdvantageRatio(p.metrics)))
        {
            var advantage = BenchmarkIntegration.CalculateAdvantageRatio(metrics);
            var indicator = advantage > 1 ? "+" : "";
            sb.AppendLine($"  {Path.GetFileName(calor)}: {indicator}{(advantage - 1) * 100:F0}% tokens ({metrics.OriginalTokens} -> {metrics.OutputTokens})");
        }

        return sb.ToString();
    }

    private static string FormatQuickProjectMarkdown(string projectName, List<(string calor, string cs, FileMetrics metrics)> pairs, BenchmarkSummary summary)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"# Benchmark Results (Quick Mode): {projectName}");
        sb.AppendLine();
        sb.AppendLine($"**Files compared:** {pairs.Count}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | C# | Calor | Savings |");
        sb.AppendLine("|--------|-----|------|---------|");
        sb.AppendLine($"| Tokens | {summary.TotalOriginalTokens:N0} | {summary.TotalOutputTokens:N0} | {summary.TokenSavingsPercent:F1}% |");
        sb.AppendLine($"| Lines | {summary.TotalOriginalLines:N0} | {summary.TotalOutputLines:N0} | {summary.LineSavingsPercent:F1}% |");
        sb.AppendLine();
        sb.AppendLine($"**Overall Calor Advantage:** {summary.OverallAdvantage:F2}x");
        sb.AppendLine();
        sb.AppendLine("## By File");
        sb.AppendLine();
        sb.AppendLine("| File | C# Tokens | Calor Tokens | Advantage |");
        sb.AppendLine("|------|-----------|-------------|-----------|");

        foreach (var (calor, cs, metrics) in pairs.OrderByDescending(p => BenchmarkIntegration.CalculateAdvantageRatio(p.metrics)))
        {
            var advantage = BenchmarkIntegration.CalculateAdvantageRatio(metrics);
            sb.AppendLine($"| {Path.GetFileName(calor)} | {metrics.OriginalTokens} | {metrics.OutputTokens} | {advantage:F2}x |");
        }

        return sb.ToString();
    }

    private static string FormatQuickProjectJson(string projectName, List<(string calor, string cs, FileMetrics metrics)> pairs, BenchmarkSummary summary)
    {
        var filesJson = string.Join(",\n    ", pairs.Select(p =>
        {
            var advantage = BenchmarkIntegration.CalculateAdvantageRatio(p.metrics);
            return $$"""
                {
                      "calor": "{{Path.GetFileName(p.calor)}}",
                      "csharp": "{{Path.GetFileName(p.cs)}}",
                      "csharpTokens": {{p.metrics.OriginalTokens}},
                      "calorTokens": {{p.metrics.OutputTokens}},
                      "advantage": {{advantage:F2}}
                    }
                """;
        }));

        return $$"""
            {
              "mode": "quick",
              "project": "{{projectName}}",
              "fileCount": {{pairs.Count}},
              "summary": {
                "totalCSharpTokens": {{summary.TotalOriginalTokens}},
                "totalCalorTokens": {{summary.TotalOutputTokens}},
                "tokenSavings": {{summary.TokenSavingsPercent:F1}},
                "totalCSharpLines": {{summary.TotalOriginalLines}},
                "totalCalorLines": {{summary.TotalOutputLines}},
                "lineSavings": {{summary.LineSavingsPercent:F1}},
                "overallAdvantage": {{summary.OverallAdvantage:F2}}
              },
              "files": [
                {{filesJson}}
              ]
            }
            """;
    }
}
