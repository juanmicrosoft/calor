using System.CommandLine;
using System.Diagnostics;
using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Init;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Parsing;
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
        var pathArgument = new Argument<DirectoryInfo>("path")
        {
            Description = "The project directory or .csproj file to migrate"
        };

        var dryRunOption = new Option<bool>("--dry-run", "-n")
        {
            Description = "Preview changes without writing files"
        };

        var benchmarkOption = new Option<bool>("--benchmark", "-b")
        {
            Description = "Include before/after metrics"
        };

        var directionOption = new Option<string>("--direction", "-d")
        {
            Description = "Migration direction (cs-to-calor or calor-to-cs)",
            DefaultValueFactory = _ => "cs-to-calor"
        };

        var parallelOption = new Option<bool>("--parallel", "-p")
        {
            Description = "Run conversions in parallel",
            DefaultValueFactory = _ => true
        };

        var reportOption = new Option<FileInfo?>("--report", "-r")
        {
            Description = "Save migration report to file (supports .md or .json)"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        var skipAnalyzeOption = new Option<bool>("--skip-analyze")
        {
            Description = "Skip the migration analysis phase"
        };

        var skipVerifyOption = new Option<bool>("--skip-verify")
        {
            Description = "Skip the Z3 contract verification phase"
        };

        var verificationTimeoutOption = new Option<int>("--verification-timeout")
        {
            Description = "Z3 verification timeout per contract in milliseconds",
            DefaultValueFactory = _ => (int)VerificationOptions.DefaultTimeoutMs
        };

        var noImplicitSystemOption = new Option<bool>("--no-implicit-system")
        {
            Description = "Validate without the implicit System project import"
        };
        var splitNamespacesOption = new Option<bool>("--split-namespaces")
        {
            Description = "Opt in to separate native files for split-safe top-level namespaces"
        };
        var defineOption = new Option<string[]>("--define")
        {
            Description = "Additional conditional-compilation symbol. Repeat for multiple symbols.",
            AllowMultipleArgumentsPerToken = true
        };
        var configurationOption = new Option<string>("--configuration")
        {
            Description = "MSBuild configuration used to resolve project parse options.",
            DefaultValueFactory = _ => "Debug"
        };
        var targetFrameworkOption = new Option<string?>("--framework")
        {
            Description = "Target framework to select for configuration-sensitive conversion."
        };
        var languageVersionOption = new Option<string?>("--language-version")
        {
            Description = "Override the project C# language version."
        };
        var documentationModeOption = new Option<string>("--documentation-mode")
        {
            Description = "Roslyn documentation mode: none, parse, or diagnose.",
            DefaultValueFactory = _ => "parse"
        };
        documentationModeOption.AcceptOnlyFromAmong("none", "parse", "diagnose");
        var featureOption = new Option<string[]>("--feature")
        {
            Description = "Roslyn parse feature key=value. Repeat for multiple features.",
            AllowMultipleArgumentsPerToken = true
        };

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
            verificationTimeoutOption,
            noImplicitSystemOption,
            splitNamespacesOption,
            defineOption,
            configurationOption,
            targetFrameworkOption,
            languageVersionOption,
            documentationModeOption,
            featureOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(pathArgument)!;
            var dryRun = parseResult.GetValue(dryRunOption);
            var benchmark = parseResult.GetValue(benchmarkOption);
            var direction = parseResult.GetValue(directionOption)!;
            var parallel = parseResult.GetValue(parallelOption);
            var reportPath = parseResult.GetValue(reportOption);
            var verbose = parseResult.GetValue(verboseOption);
            var skipAnalyze = parseResult.GetValue(skipAnalyzeOption);
            var skipVerify = parseResult.GetValue(skipVerifyOption);
            var verificationTimeout = parseResult.GetValue(verificationTimeoutOption);
            var definedSymbols = parseResult.GetValue(defineOption) ?? [];
            var configuration = parseResult.GetValue(configurationOption) ?? "Debug";
            var targetFramework = parseResult.GetValue(targetFrameworkOption);
            var languageVersion = parseResult.GetValue(languageVersionOption);
            var documentationMode = parseResult.GetValue(documentationModeOption) ?? "parse";
            var parseFeatures = parseResult.GetValue(featureOption) ?? [];

            // Exit code returned from the action: a code parked only on
            // Environment.ExitCode is overwritten by Main's InvokeAsync return.
            return await ExecuteAsync(path, dryRun, benchmark, direction, parallel,
                reportPath, verbose, skipAnalyze, skipVerify, (uint)verificationTimeout,
                definedSymbols,
                configuration, targetFramework, languageVersion,
                documentationMode, parseFeatures,
                !parseResult.GetValue(noImplicitSystemOption),
                parseResult.GetValue(splitNamespacesOption));
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        DirectoryInfo path,
        bool dryRun,
        bool benchmark,
        string direction,
        bool parallel,
        FileInfo? reportPath,
        bool verbose,
        bool skipAnalyze,
        bool skipVerify,
        uint verificationTimeout,
        IReadOnlyCollection<string> definedSymbols,
        string configuration,
        string? targetFramework,
        string? languageVersion,
        string documentationMode,
        IReadOnlyCollection<string> parseFeatures,
        bool includeImplicitSystemUsing,
        bool splitNamespaces)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("migrate");
        if (telemetry != null)
        {
            var discovered = CalorConfigManager.Discover(path.FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();
        var exitCode = 0;

        if (!path.Exists && !File.Exists(path.FullName))
        {
            Console.Error.WriteLine($"Error: Path not found: {path.FullName}");
            return 1;
        }

        var migrationDirection = direction.ToLowerInvariant() switch
        {
            "cs-to-calor" or "csharp-to-calor" or "c#-to-calor" => MigrationDirection.CSharpToCalor,
            "calor-to-cs" or "calor-to-csharp" or "calor-to-c#" => MigrationDirection.CalorToCSharp,
            _ => MigrationDirection.CSharpToCalor
        };

        var options = new MigrationPlanOptions
        {
            IncludeImplicitSystemUsing = includeImplicitSystemUsing,
            SplitNamespaces = splitNamespaces,
            Parallel = parallel,
            IncludeBenchmark = benchmark,
            SkipAnalyze = skipAnalyze,
            SkipVerify = skipVerify,
            VerificationTimeoutMs = verificationTimeout,
            Fidelity = ConversionFidelity.Lossless,
            PreprocessorMode = PreprocessorConversionMode.PreserveAllBranches,
            Configuration = configuration,
            TargetFramework = targetFramework,
            DefinedSymbols = definedSymbols.ToList(),
            LanguageVersion = ParseLanguageVersion(languageVersion),
            DocumentationMode = Enum.Parse<
                Microsoft.CodeAnalysis.DocumentationMode>(
                    documentationMode,
                    ignoreCase: true),
            ParseFeatures = ParseFeatures(parseFeatures)
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
                return exitCode;
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
                return exitCode;
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
                // Track aggregate conversion metrics (Phase 4)
                try
                {
                    // Use benchmark metrics when available, otherwise count lines from source files
                    var totalInputLines = report.FileResults
                        .Where(f => f.Metrics != null)
                        .Sum(f => f.Metrics!.OriginalLines);
                    if (totalInputLines == 0)
                    {
                        foreach (var fileResult in report.FileResults.DistinctBy(result => result.SourcePath))
                        {
                            try
                            {
                                if (File.Exists(fileResult.SourcePath))
                                    totalInputLines += File.ReadAllLines(fileResult.SourcePath).Length;
                            }
                            catch
                            {
                                // Best effort — skip files that can't be read
                            }
                        }
                    }
                    var totalIssues = report.FileResults.Sum(f => f.Issues.Count);
                    var totalUnsupported = report.FileResults.Sum(f =>
                        f.Issues.Count(i => i.Feature != null));
                    telemetry.TrackConversionAttempted(
                        totalInputLines,
                        report.Summary.FailedFiles == 0,
                        sw.ElapsedMilliseconds,
                        totalIssues,
                        totalUnsupported);

                    // Track individual conversion gaps
                    foreach (var issue in report.FileResults
                        .SelectMany(f => f.Issues)
                        .Where(i => i.Feature != null))
                    {
                        telemetry.TrackConversionGap(issue.Feature!, issue.Line);
                    }
                }
                catch
                {
                    // Never crash the CLI
                }

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
            // #1144: which path preserved each member, read from the loss ledger.
            if (ConversionPathSummary.Describe(report.FileResults.SelectMany(f => f.Losses), "PassthroughOnError") is { } pathLine)
                Console.WriteLine($"  {pathLine}");
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

            // Save report if requested. The .json report is wrapped in the
            // envelope schema v1.1 document ({version, command, diagnostics,
            // summary, data}); the report shape itself is unchanged under
            // `data`. The .md report is unchanged.
            if (reportPath != null)
            {
                var generator = new MigrationReportGenerator(enrichedReport);
                if (reportPath.Extension.ToLowerInvariant() == ".json")
                {
                    var envelope = EnvelopeWriter.SerializeRaw(
                        "migrate",
                        generator.GenerateJson(),
                        BuildReportDiagnostics(enrichedReport));
                    await File.WriteAllTextAsync(reportPath.FullName, envelope);
                }
                else
                {
                    await generator.SaveAsync(reportPath.FullName, ReportFormat.Markdown);
                }
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

            // Exit code based on results (returned from the action)
            if (enrichedReport.Summary.FailedFiles > 0)
            {
                exitCode = 1;
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
            exitCode = 1;
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
            telemetry?.TrackCommand("migrate", exitCode, telemetryProps);
            if (exitCode != 0)
            {
                IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "migrate", "Migration failed");
            }
        }

        return exitCode;
    }

    private static Microsoft.CodeAnalysis.CSharp.LanguageVersion?
        ParseLanguageVersion(string? languageVersion)
    {
        if (string.IsNullOrWhiteSpace(languageVersion))
            return null;
        if (!Microsoft.CodeAnalysis.CSharp.LanguageVersionFacts.TryParse(
                languageVersion,
                out var parsed))
        {
            throw new ArgumentException(
                $"Unknown C# language version '{languageVersion}'.",
                nameof(languageVersion));
        }
        return parsed;
    }

    private static Dictionary<string, string> ParseFeatures(
        IReadOnlyCollection<string> features)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            var separator = feature.IndexOf('=');
            if (separator <= 0)
            {
                throw new ArgumentException(
                    $"Parse feature '{feature}' must use key=value syntax.",
                    nameof(features));
            }
            result[feature[..separator]] = feature[(separator + 1)..];
        }
        return result;
    }

    /// <summary>
    /// Per-file conversion issues as envelope diagnostics (Calor1343, feature
    /// name prefixed — the same mapping `calor convert --format json` uses),
    /// so findings never hide inside the report payload.
    /// </summary>
    private static List<Diagnostic> BuildReportDiagnostics(MigrationReport report)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var fileResult in report.FileResults)
        {
            foreach (var issue in fileResult.Issues)
            {
                var severity = issue.Severity switch
                {
                    ConversionIssueSeverity.Error => DiagnosticSeverity.Error,
                    ConversionIssueSeverity.Warning => DiagnosticSeverity.Warning,
                    _ => DiagnosticSeverity.Info
                };
                var message = issue.Feature != null
                    ? $"[{issue.Feature}] {issue.Message}"
                    : issue.Message;
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.ConversionIssue,
                    message,
                    new TextSpan(0, 0, issue.Line ?? 1, issue.Column ?? 1),
                    severity,
                    fileResult.SourcePath));
            }
        }
        return diagnostics;
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
