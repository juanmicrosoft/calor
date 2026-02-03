using System.CommandLine;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for project-level migration.
/// </summary>
public static class MigrateCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<DirectoryInfo>(
            name: "path",
            description: "The project directory or .csproj file to migrate");

        var dryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run", "-n" },
            description: "Preview changes without writing files");

        var benchmarkOption = new Option<bool>(
            aliases: new[] { "--benchmark", "-b" },
            description: "Include before/after metrics");

        var directionOption = new Option<string>(
            aliases: new[] { "--direction", "-d" },
            description: "Migration direction (cs-to-calor or calor-to-cs)",
            getDefaultValue: () => "cs-to-calor");

        var parallelOption = new Option<bool>(
            aliases: new[] { "--parallel", "-p" },
            description: "Run conversions in parallel",
            getDefaultValue: () => true);

        var reportOption = new Option<FileInfo?>(
            aliases: new[] { "--report", "-r" },
            description: "Save migration report to file (supports .md or .json)");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        var command = new Command("migrate", "Migrate an entire project between C# and Calor")
        {
            pathArgument,
            dryRunOption,
            benchmarkOption,
            directionOption,
            parallelOption,
            reportOption,
            verboseOption
        };

        command.SetHandler(ExecuteAsync, pathArgument, dryRunOption, benchmarkOption, directionOption, parallelOption, reportOption, verboseOption);

        return command;
    }

    private static async Task ExecuteAsync(
        DirectoryInfo path,
        bool dryRun,
        bool benchmark,
        string direction,
        bool parallel,
        FileInfo? reportPath,
        bool verbose)
    {
        if (!path.Exists && !File.Exists(path.FullName))
        {
            Console.Error.WriteLine($"Error: Path not found: {path.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        var migrationDirection = direction.ToLowerInvariant() switch
        {
            "cs-to-calor" or "csharp-to-calor" or "c#-to-calor" => MigrationDirection.CSharpToCalor,
            "calor-to-cs" or "calor-to-csharp" or "calor-to-c#" => MigrationDirection.CalorToCSharp,
            _ => MigrationDirection.CSharpToCalor
        };

        var options = new MigrationPlanOptions
        {
            Parallel = parallel,
            IncludeBenchmark = benchmark
        };

        var migrator = new ProjectMigrator(options);

        try
        {
            // Create migration plan
            Console.WriteLine($"Analyzing project: {path.FullName}");
            Console.WriteLine();

            var plan = await migrator.CreatePlanAsync(path.FullName, migrationDirection);

            // Show plan summary
            Console.WriteLine("Migration Plan:");
            Console.WriteLine($"  Files to convert: {plan.ConvertibleFiles}");
            Console.WriteLine($"  Files needing review: {plan.PartialFiles}");
            Console.WriteLine($"  Files to skip: {plan.SkippedFiles}");
            Console.WriteLine($"  Estimated issues: {plan.EstimatedIssues}");
            Console.WriteLine();

            if (plan.ConvertibleFiles == 0)
            {
                Console.WriteLine("No files to migrate.");
                return;
            }

            if (dryRun)
            {
                Console.WriteLine("Dry run - no files will be modified.");
                Console.WriteLine();
                ShowPlanDetails(plan, verbose);
                return;
            }

            // Execute migration
            Console.Write("Migrating... ");

            var progress = new Progress<MigrationProgress>(p =>
            {
                if (verbose)
                {
                    Console.WriteLine($"  [{p.ProcessedFiles}/{p.TotalFiles}] {p.CurrentFile}: {p.Status}");
                }
                else
                {
                    // Show progress bar
                    var percent = (int)p.PercentComplete;
                    Console.Write($"\rMigrating... [{new string('█', percent / 5)}{new string('░', 20 - percent / 5)}] {percent}%");
                }
            });

            var report = await migrator.ExecuteAsync(plan, dryRun: false, progress);

            if (!verbose)
            {
                Console.WriteLine(); // New line after progress bar
            }

            // Show results
            Console.WriteLine();
            Console.WriteLine("Results:");
            Console.WriteLine($"  Successful: {report.Summary.SuccessfulFiles}");
            if (report.Summary.PartialFiles > 0)
                Console.WriteLine($"  Partial: {report.Summary.PartialFiles} (need review)");
            if (report.Summary.FailedFiles > 0)
                Console.WriteLine($"  Failed: {report.Summary.FailedFiles}");

            // Show benchmark if requested
            if (benchmark && report.Benchmark != null)
            {
                Console.WriteLine();
                Console.WriteLine("Benchmark Summary:");
                Console.WriteLine($"  Total tokens: {report.Benchmark.TotalOriginalTokens:N0} → {report.Benchmark.TotalOutputTokens:N0} ({report.Benchmark.TokenSavingsPercent:F1}% savings)");
                Console.WriteLine($"  Total lines: {report.Benchmark.TotalOriginalLines:N0} → {report.Benchmark.TotalOutputLines:N0} ({report.Benchmark.LineSavingsPercent:F1}% savings)");
                Console.WriteLine($"  Overall Calor advantage: {report.Benchmark.OverallAdvantage:F2}x");
            }

            // Save report if requested
            if (reportPath != null)
            {
                var generator = new MigrationReportGenerator(report);
                var format = reportPath.Extension.ToLowerInvariant() == ".json"
                    ? ReportFormat.Json
                    : ReportFormat.Markdown;

                await generator.SaveAsync(reportPath.FullName, format);
                Console.WriteLine();
                Console.WriteLine($"Report saved: {reportPath.FullName}");
            }

            // Show errors/warnings summary
            if (report.Summary.TotalErrors > 0 || report.Summary.TotalWarnings > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Issues: {report.Summary.TotalErrors} error(s), {report.Summary.TotalWarnings} warning(s)");

                if (verbose && report.Summary.MostCommonIssues.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Most common issues:");
                    foreach (var issue in report.Summary.MostCommonIssues.Take(5))
                    {
                        Console.WriteLine($"  • {issue}");
                    }
                }
            }

            // Set exit code based on results
            if (report.Summary.FailedFiles > 0)
            {
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
            Environment.ExitCode = 1;
        }
    }

    private static void ShowPlanDetails(MigrationPlan plan, bool verbose)
    {
        var fullConvert = plan.Entries.Where(e => e.Convertibility == FileConvertibility.Full).ToList();
        var partial = plan.Entries.Where(e => e.Convertibility == FileConvertibility.Partial).ToList();
        var skip = plan.Entries.Where(e => e.Convertibility == FileConvertibility.Skip).ToList();

        if (fullConvert.Count > 0)
        {
            Console.WriteLine("Files to convert:");
            foreach (var entry in fullConvert.Take(verbose ? 100 : 10))
            {
                Console.WriteLine($"  ✓ {Path.GetFileName(entry.SourcePath)}");
            }
            if (!verbose && fullConvert.Count > 10)
            {
                Console.WriteLine($"  ... and {fullConvert.Count - 10} more");
            }
            Console.WriteLine();
        }

        if (partial.Count > 0)
        {
            Console.WriteLine("Files needing review:");
            foreach (var entry in partial.Take(verbose ? 100 : 5))
            {
                Console.WriteLine($"  ⚠ {Path.GetFileName(entry.SourcePath)}");
                foreach (var issue in entry.PotentialIssues.Take(2))
                {
                    Console.WriteLine($"      {issue}");
                }
            }
            if (!verbose && partial.Count > 5)
            {
                Console.WriteLine($"  ... and {partial.Count - 5} more");
            }
            Console.WriteLine();
        }

        if (skip.Count > 0 && verbose)
        {
            Console.WriteLine("Files to skip:");
            foreach (var entry in skip.Take(10))
            {
                var reason = entry.SkipReason ?? "excluded by pattern";
                Console.WriteLine($"  ⊘ {Path.GetFileName(entry.SourcePath)}: {reason}");
            }
            if (skip.Count > 10)
            {
                Console.WriteLine($"  ... and {skip.Count - 10} more");
            }
            Console.WriteLine();
        }
    }
}
