using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Calor.Compiler.Analysis;
using Calor.Compiler.Init;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Telemetry;
using Calor.Compiler.Verification.Z3;

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

        var skipAnalyzeOption = new Option<bool>(
            aliases: new[] { "--skip-analyze" },
            description: "Skip the migration analysis phase");

        var skipVerifyOption = new Option<bool>(
            aliases: new[] { "--skip-verify" },
            description: "Skip the Z3 contract verification phase");

        var verificationTimeoutOption = new Option<int>(
            aliases: new[] { "--verification-timeout" },
            description: "Z3 verification timeout per contract in milliseconds",
            getDefaultValue: () => (int)VerificationOptions.DefaultTimeoutMs);

        var command = new Command("migrate", "Migrate an entire project between C# and Calor")
        {
            pathArgument,
            dryRunOption,
            benchmarkOption,
            directionOption,
            parallelOption,
            reportOption,
            verboseOption,
            skipAnalyzeOption,
            skipVerifyOption,
            verificationTimeoutOption
        };

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(pathArgument);
            var dryRun = ctx.ParseResult.GetValueForOption(dryRunOption);
            var benchmark = ctx.ParseResult.GetValueForOption(benchmarkOption);
            var direction = ctx.ParseResult.GetValueForOption(directionOption)!;
            var parallel = ctx.ParseResult.GetValueForOption(parallelOption);
            var reportPath = ctx.ParseResult.GetValueForOption(reportOption);
            var verbose = ctx.ParseResult.GetValueForOption(verboseOption);
            var skipAnalyze = ctx.ParseResult.GetValueForOption(skipAnalyzeOption);
            var skipVerify = ctx.ParseResult.GetValueForOption(skipVerifyOption);
            var verificationTimeout = ctx.ParseResult.GetValueForOption(verificationTimeoutOption);

            await ExecuteAsync(path, dryRun, benchmark, direction, parallel,
                reportPath, verbose, skipAnalyze, skipVerify, (uint)verificationTimeout);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        DirectoryInfo path,
        bool dryRun,
        bool benchmark,
        string direction,
        bool parallel,
        FileInfo? reportPath,
        bool verbose,
        bool skipAnalyze,
        bool skipVerify,
        uint verificationTimeout)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("migrate");
        if (telemetry != null)
        {
            var discovered = CalorConfigManager.Discover(path.FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();

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
            IncludeBenchmark = benchmark,
            SkipAnalyze = skipAnalyze,
            SkipVerify = skipVerify,
            VerificationTimeoutMs = verificationTimeout
        };

        var migrator = new ProjectMigrator(options);
        VerificationSummaryReport? verificationSummary = null;

        try
        {
            // ── Phase 1/4: Discovering files ──
            Console.WriteLine("Phase 1/4: Discovering files...");

            var plan = await migrator.CreatePlanAsync(path.FullName, migrationDirection);

            Console.WriteLine($"  Files to convert: {plan.ConvertibleFiles}");
            Console.WriteLine($"  Files needing review: {plan.PartialFiles}");
            Console.WriteLine($"  Files to skip: {plan.SkippedFiles}");
            Console.WriteLine($"  Estimated issues: {plan.EstimatedIssues}");
            Console.WriteLine();

            if (plan.ConvertibleFiles == 0 && plan.PartialFiles == 0)
            {
                Console.WriteLine("No files to migrate.");
                return;
            }

            // ── Phase 2/4: Analyzing migration potential ──
            AnalysisSummaryReport? analysisSummary = null;
            var shouldAnalyze = !skipAnalyze && migrationDirection == MigrationDirection.CSharpToCalor;

            if (shouldAnalyze)
            {
                Console.WriteLine("Phase 2/4: Analyzing migration potential...");

                var analysisProgress = verbose
                    ? new Progress<string>(msg => Console.WriteLine($"  {msg}"))
                    : null;

                analysisSummary = await migrator.AnalyzeAsync(plan, analysisProgress);

                Console.WriteLine($"  Average score: {analysisSummary.AverageScore:F1}/100");
                if (analysisSummary.PriorityBreakdown.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var priority in new[] { MigrationPriority.Critical, MigrationPriority.High, MigrationPriority.Medium, MigrationPriority.Low })
                    {
                        if (analysisSummary.PriorityBreakdown.TryGetValue(priority, out var count))
                        {
                            parts.Add($"{count} {FileMigrationScore.GetPriorityLabel(priority)}");
                        }
                    }
                    Console.WriteLine($"  Priority: {string.Join(", ", parts)}");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("Phase 2/4: Analyzing migration potential... skipped");
                Console.WriteLine();
            }

            if (dryRun)
            {
                Console.WriteLine("Dry run - no files will be modified.");
                Console.WriteLine();
                ShowPlanDetails(plan, verbose);
                return;
            }

            // ── Phase 3/4: Converting files ──
            Console.Write("Phase 3/4: Converting files... ");

            var progress = new Progress<MigrationProgress>(p =>
            {
                if (verbose)
                {
                    Console.WriteLine($"  [{p.ProcessedFiles}/{p.TotalFiles}] {p.CurrentFile}: {p.Status}");
                }
                else
                {
                    var percent = (int)p.PercentComplete;
                    Console.Write($"\rPhase 3/4: Converting files... [{new string('#', percent / 5)}{new string('.', 20 - percent / 5)}] {percent}%");
                }
            });

            var report = await migrator.ExecuteAsync(plan, dryRun: false, progress);

            if (telemetry != null)
            {
                var featureCounts = report.FileResults
                    .SelectMany(f => f.Issues)
                    .Where(i => i.Feature != null)
                    .GroupBy(i => i.Feature!)
                    .ToDictionary(g => g.Key, g => g.Count());
                if (featureCounts.Count > 0)
                {
                    telemetry.TrackUnsupportedFeatures(featureCounts, featureCounts.Values.Sum());
                }
            }

            if (!verbose)
            {
                Console.WriteLine();
            }

            Console.WriteLine($"  Successful: {report.Summary.SuccessfulFiles}");
            if (report.Summary.PartialFiles > 0)
                Console.WriteLine($"  Partial: {report.Summary.PartialFiles} (need review)");
            if (report.Summary.FailedFiles > 0)
                Console.WriteLine($"  Failed: {report.Summary.FailedFiles}");
            Console.WriteLine();

            // ── Phase 4/4: Verifying contracts ──
            var shouldVerify = !skipVerify && migrationDirection == MigrationDirection.CSharpToCalor;

            if (shouldVerify)
            {
                Console.Write("Phase 4/4: Verifying contracts...");

                var verifyProgress = verbose
                    ? new Progress<string>(msg => Console.WriteLine($"  {msg}"))
                    : null;

                verificationSummary = await migrator.VerifyAsync(report, verificationTimeout, verifyProgress);

                Console.WriteLine();

                if (!verificationSummary.Z3Available)
                {
                    Console.WriteLine("  Z3 solver not available - verification skipped.");
                }
                else
                {
                    Console.WriteLine($"  Contracts: {verificationSummary.TotalContracts} total");
                    if (verificationSummary.TotalContracts > 0)
                    {
                        Console.WriteLine($"  Proven: {verificationSummary.Proven}, Unproven: {verificationSummary.Unproven}, Disproven: {verificationSummary.Disproven}");
                        Console.WriteLine($"  Proven rate: {verificationSummary.ProvenRate:F1}%");
                    }
                }
                Console.WriteLine();

                // Verification telemetry is merged into the final "migrate" TrackCommand below
            }
            else
            {
                Console.WriteLine("Phase 4/4: Verifying contracts... skipped");
                Console.WriteLine();
            }

            // ── Build enriched report via builder ──
            var enrichedBuilder = new MigrationReportBuilder()
                .SetDirection(report.Direction)
                .IncludeBenchmark(benchmark);

            foreach (var fr in report.FileResults)
                enrichedBuilder.AddFileResult(fr);
            foreach (var rec in report.Recommendations)
                enrichedBuilder.AddRecommendation(rec);

            if (analysisSummary != null)
                enrichedBuilder.SetAnalysisSummary(analysisSummary);
            if (verificationSummary != null)
                enrichedBuilder.SetVerificationSummary(verificationSummary);

            var enrichedReport = enrichedBuilder.Build();

            // Show benchmark if requested
            if (benchmark && enrichedReport.Benchmark != null)
            {
                Console.WriteLine("Benchmark Summary:");
                Console.WriteLine($"  Total tokens: {enrichedReport.Benchmark.TotalOriginalTokens:N0} -> {enrichedReport.Benchmark.TotalOutputTokens:N0} ({enrichedReport.Benchmark.TokenSavingsPercent:F1}% savings)");
                Console.WriteLine($"  Total lines: {enrichedReport.Benchmark.TotalOriginalLines:N0} -> {enrichedReport.Benchmark.TotalOutputLines:N0} ({enrichedReport.Benchmark.LineSavingsPercent:F1}% savings)");
                Console.WriteLine($"  Overall Calor advantage: {enrichedReport.Benchmark.OverallAdvantage:F2}x");
                Console.WriteLine();
            }

            // Save report if requested
            if (reportPath != null)
            {
                var generator = new MigrationReportGenerator(enrichedReport);
                var format = reportPath.Extension.ToLowerInvariant() == ".json"
                    ? ReportFormat.Json
                    : ReportFormat.Markdown;

                await generator.SaveAsync(reportPath.FullName, format);
                Console.WriteLine($"Report saved: {reportPath.FullName}");
            }

            // Show errors/warnings summary
            if (enrichedReport.Summary.TotalErrors > 0 || enrichedReport.Summary.TotalWarnings > 0)
            {
                Console.WriteLine($"Issues: {enrichedReport.Summary.TotalErrors} error(s), {enrichedReport.Summary.TotalWarnings} warning(s)");

                if (verbose && enrichedReport.Summary.MostCommonIssues.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Most common issues:");
                    foreach (var issue in enrichedReport.Summary.MostCommonIssues.Take(5))
                    {
                        Console.WriteLine($"  - {issue}");
                    }
                }
            }

            // Set exit code based on results
            if (enrichedReport.Summary.FailedFiles > 0)
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
            telemetry?.TrackException(ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            sw.Stop();
            var telemetryProps = new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString()
            };
            if (verificationSummary is { Z3Available: true })
            {
                telemetryProps["verifyContracts"] = verificationSummary.TotalContracts.ToString();
                telemetryProps["verifyProven"] = verificationSummary.Proven.ToString();
                telemetryProps["verifyDisproven"] = verificationSummary.Disproven.ToString();
                telemetryProps["verifyDurationMs"] = verificationSummary.Duration.TotalMilliseconds.ToString("F0");
            }
            telemetry?.TrackCommand("migrate", Environment.ExitCode, telemetryProps);
            if (Environment.ExitCode != 0)
            {
                IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "migrate", "Migration failed");
            }
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
                Console.WriteLine($"  + {Path.GetFileName(entry.SourcePath)}");
                if (verbose && entry.AnalysisScore != null && !entry.AnalysisScore.WasSkipped)
                {
                    Console.WriteLine($"      Score: {entry.AnalysisScore.TotalScore:F1} ({FileMigrationScore.GetPriorityLabel(entry.AnalysisScore.Priority)})");
                }
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
                Console.WriteLine($"  ~ {Path.GetFileName(entry.SourcePath)}");
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
                Console.WriteLine($"  - {Path.GetFileName(entry.SourcePath)}: {reason}");
            }
            if (skip.Count > 10)
            {
                Console.WriteLine($"  ... and {skip.Count - 10} more");
            }
            Console.WriteLine();
        }
    }
}
