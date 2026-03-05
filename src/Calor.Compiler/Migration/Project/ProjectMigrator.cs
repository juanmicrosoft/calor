using System.Diagnostics;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Verification.Z3;

namespace Calor.Compiler.Migration.Project;

/// <summary>
/// Orchestrates project-level migration.
/// </summary>
public sealed class ProjectMigrator
{
    private readonly MigrationPlanOptions _options;

    public ProjectMigrator(MigrationPlanOptions? options = null)
    {
        _options = options ?? new MigrationPlanOptions();
    }

    /// <summary>
    /// Creates a migration plan for a project.
    /// </summary>
    public async Task<MigrationPlan> CreatePlanAsync(string projectPath, MigrationDirection direction)
    {
        var discovery = new ProjectDiscovery(_options);

        return direction == MigrationDirection.CSharpToCalor
            ? await discovery.DiscoverCSharpFilesAsync(projectPath, direction)
            : await discovery.DiscoverCalorFilesAsync(projectPath);
    }

    /// <summary>
    /// Executes a migration plan.
    /// </summary>
    public async Task<MigrationReport> ExecuteAsync(MigrationPlan plan, bool dryRun = false, IProgress<MigrationProgress>? progress = null)
    {
        var reportBuilder = new MigrationReportBuilder()
            .SetDirection(plan.Direction)
            .IncludeBenchmark(_options.IncludeBenchmark);

        var entriesToProcess = plan.Entries
            .Where(e => e.Convertibility != FileConvertibility.Skip)
            .ToList();

        var processedCount = 0;
        var totalCount = entriesToProcess.Count;

        if (_options.Parallel && !dryRun)
        {
            var semaphore = new SemaphoreSlim(_options.MaxParallelism);
            var tasks = entriesToProcess.Select(async entry =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await ProcessEntryAsync(entry, plan.Direction, dryRun);
                    reportBuilder.AddFileResult(result);

                    Interlocked.Increment(ref processedCount);
                    progress?.Report(new MigrationProgress
                    {
                        CurrentFile = Path.GetFileName(entry.SourcePath),
                        ProcessedFiles = processedCount,
                        TotalFiles = totalCount,
                        Status = result.Status
                    });

                    return result;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
        else
        {
            foreach (var entry in entriesToProcess)
            {
                var result = await ProcessEntryAsync(entry, plan.Direction, dryRun);
                reportBuilder.AddFileResult(result);

                processedCount++;
                progress?.Report(new MigrationProgress
                {
                    CurrentFile = Path.GetFileName(entry.SourcePath),
                    ProcessedFiles = processedCount,
                    TotalFiles = totalCount,
                    Status = result.Status
                });
            }
        }

        // Add skipped files
        foreach (var entry in plan.Entries.Where(e => e.Convertibility == FileConvertibility.Skip))
        {
            reportBuilder.AddFileResult(new FileMigrationResult
            {
                SourcePath = entry.SourcePath,
                OutputPath = null,
                Status = FileMigrationStatus.Skipped,
                Issues = entry.SkipReason != null
                    ? new List<ConversionIssue>
                    {
                        new() { Severity = ConversionIssueSeverity.Info, Message = entry.SkipReason }
                    }
                    : new List<ConversionIssue>()
            });
        }

        // Add recommendations based on results
        AddRecommendations(reportBuilder, plan);

        var report = reportBuilder.Build();

        // Post-conversion: merge partial classes if enabled
        if (_options.MergePartialClasses && !dryRun && plan.Direction == MigrationDirection.CSharpToCalor)
        {
            await MergePartialClassesAsync(report);
        }

        return report;
    }

    /// <summary>
    /// Performs a dry run showing what would be migrated.
    /// </summary>
    public async Task<MigrationReport> DryRunAsync(MigrationPlan plan)
    {
        return await ExecuteAsync(plan, dryRun: true);
    }

    /// <summary>
    /// Merges partial class definitions from successfully converted files.
    /// Re-reads, merges, and re-writes the output Calor files.
    /// </summary>
    private async Task MergePartialClassesAsync(MigrationReport report)
    {
        var successfulFiles = report.FileResults
            .Where(f => f.Status is FileMigrationStatus.Success or FileMigrationStatus.Partial
                        && f.OutputPath != null)
            .ToList();

        if (successfulFiles.Count < 2)
            return;

        // Parse all successful output files
        var modules = new List<(FileMigrationResult File, ModuleNode Module)>();
        foreach (var file in successfulFiles)
        {
            try
            {
                var source = await File.ReadAllTextAsync(file.OutputPath!);
                var parseResult = CalorSourceHelper.Parse(source, file.OutputPath!);
                if (parseResult.IsSuccess && parseResult.Ast != null)
                {
                    // Tag partial classes with source file
                    foreach (var cls in parseResult.Ast.Classes.Where(c => c.IsPartial))
                    {
                        cls.SourceFile = file.SourcePath;
                    }
                    modules.Add((file, parseResult.Ast));
                }
            }
            catch
            {
                // Skip files that can't be parsed
            }
        }

        // Check if there are any partial classes to merge
        var hasPartials = modules
            .SelectMany(m => m.Module.Classes)
            .Any(c => c.IsPartial);

        if (!hasPartials)
            return;

        var merger = new PartialClassMerger();
        var mergedModules = merger.Merge(modules.Select(m => m.Module).ToList());

        // Re-emit only modules that changed
        var emitter = new CalorEmitter();
        for (var i = 0; i < mergedModules.Count; i++)
        {
            if (mergedModules[i] != modules[i].Module)
            {
                var newSource = emitter.Emit(mergedModules[i]);
                await File.WriteAllTextAsync(modules[i].File.OutputPath!, newSource);
            }
        }
    }

    private async Task<FileMigrationResult> ProcessEntryAsync(MigrationPlanEntry entry, MigrationDirection direction, bool dryRun)
    {
        var startTime = DateTime.UtcNow;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.PerFileTimeoutSeconds));
        try
        {
            if (direction == MigrationDirection.CSharpToCalor)
            {
                return await ProcessCSharpToCalorAsync(entry, dryRun, startTime).WaitAsync(cts.Token);
            }
            else
            {
                return await ProcessCalorToCSharpAsync(entry, dryRun, startTime).WaitAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return new FileMigrationResult
            {
                SourcePath = entry.SourcePath,
                OutputPath = null,
                Status = FileMigrationStatus.TimedOut,
                Duration = DateTime.UtcNow - startTime,
                Issues = new List<ConversionIssue>
                {
                    new() { Severity = ConversionIssueSeverity.Error, Message = $"Timed out after {_options.PerFileTimeoutSeconds}s" }
                }
            };
        }
        catch (Exception ex)
        {
            return new FileMigrationResult
            {
                SourcePath = entry.SourcePath,
                OutputPath = null,
                Status = FileMigrationStatus.Failed,
                Duration = DateTime.UtcNow - startTime,
                Issues = new List<ConversionIssue>
                {
                    new() { Severity = ConversionIssueSeverity.Error, Message = ex.Message }
                }
            };
        }
    }

    private async Task<FileMigrationResult> ProcessCSharpToCalorAsync(MigrationPlanEntry entry, bool dryRun, DateTime startTime)
    {
        var conversionOptions = new ConversionOptions
        {
            IncludeBenchmark = _options.IncludeBenchmark,
            PassthroughOnError = _options.PassthroughOnError
        };

        if (!string.IsNullOrEmpty(_options.ModuleNameOverride))
        {
            conversionOptions.ModuleName = _options.ModuleNameOverride;
        }

        var converter = new CSharpToCalorConverter(conversionOptions);

        var result = await converter.ConvertFileAsync(entry.SourcePath);

        // Tag partial classes with source file for merging
        if (result.Success && result.Ast != null && _options.MergePartialClasses)
        {
            foreach (var cls in result.Ast.Classes.Where(c => c.IsPartial))
            {
                cls.SourceFile = entry.SourcePath;
            }
        }

        FileMetrics? metrics = null;
        if (result.Success && result.CalorSource != null && _options.IncludeBenchmark)
        {
            var originalSource = await File.ReadAllTextAsync(entry.SourcePath);
            metrics = BenchmarkIntegration.CalculateMetrics(originalSource, result.CalorSource);
        }

        if (!dryRun && result.Success && result.CalorSource != null)
        {
            await File.WriteAllTextAsync(entry.OutputPath, result.CalorSource);
        }

        var status = result.Success
            ? (result.Context.HasWarnings ? FileMigrationStatus.Partial : FileMigrationStatus.Success)
            : FileMigrationStatus.Failed;

        // Validate: parse and compile the generated Calor to catch false-positive "success"
        var issues = result.Issues.ToList();
        if (_options.ValidateOutput && result.Success && result.CalorSource != null)
        {
            var parseResult = CalorSourceHelper.Parse(result.CalorSource, entry.OutputPath);
            if (!parseResult.IsSuccess)
            {
                status = FileMigrationStatus.Partial;
                foreach (var error in parseResult.Errors)
                {
                    issues.Add(new ConversionIssue
                    {
                        Severity = ConversionIssueSeverity.Error,
                        Message = $"Validation parse error: {error}"
                    });
                }
            }
            else
            {
                try
                {
                    var compileOptions = new CompilationOptions
                    {
                        EnforceEffects = false,
                        UnknownCallPolicy = UnknownCallPolicy.Permissive
                    };
                    var compileResult = Program.Compile(result.CalorSource, entry.OutputPath, compileOptions);
                    if (compileResult.HasErrors)
                    {
                        status = FileMigrationStatus.Partial;
                        foreach (var diag in compileResult.Diagnostics.Errors)
                        {
                            issues.Add(new ConversionIssue
                            {
                                Severity = ConversionIssueSeverity.Error,
                                Message = $"Validation compile error: {diag.Message}",
                                Line = diag.Span.Line,
                                Column = diag.Span.Column
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    status = FileMigrationStatus.Partial;
                    issues.Add(new ConversionIssue
                    {
                        Severity = ConversionIssueSeverity.Warning,
                        Message = $"Validation compile exception: {ex.Message}"
                    });
                }
            }
        }

        // Attach per-file analysis if available from a prior AnalyzeAsync call
        FileAnalysisResult? analysisResult = null;
        if (entry.AnalysisScore is { WasSkipped: false } score)
        {
            analysisResult = new FileAnalysisResult
            {
                FilePath = score.RelativePath,
                Score = score.TotalScore,
                Priority = score.Priority,
                DimensionScores = score.Dimensions.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.RawScore),
                UnsupportedConstructs = score.UnsupportedConstructs
            };
        }

        return new FileMigrationResult
        {
            SourcePath = entry.SourcePath,
            OutputPath = result.Success ? entry.OutputPath : null,
            Status = status,
            Duration = DateTime.UtcNow - startTime,
            Issues = issues,
            Metrics = metrics,
            Analysis = analysisResult
        };
    }

    private async Task<FileMigrationResult> ProcessCalorToCSharpAsync(MigrationPlanEntry entry, bool dryRun, DateTime startTime)
    {
        var source = await File.ReadAllTextAsync(entry.SourcePath);
        var result = Program.Compile(source, entry.SourcePath);

        FileMetrics? metrics = null;
        if (!result.HasErrors && _options.IncludeBenchmark)
        {
            metrics = BenchmarkIntegration.CalculateMetrics(source, result.GeneratedCode);
        }

        if (!dryRun && !result.HasErrors)
        {
            await File.WriteAllTextAsync(entry.OutputPath, result.GeneratedCode);
        }

        var status = result.HasErrors
            ? FileMigrationStatus.Failed
            : FileMigrationStatus.Success;

        var issues = result.Diagnostics.Errors
            .Select(d => new ConversionIssue
            {
                Severity = ConversionIssueSeverity.Error,
                Message = d.Message,
                Line = d.Span.Line,
                Column = d.Span.Column
            })
            .ToList();

        return new FileMigrationResult
        {
            SourcePath = entry.SourcePath,
            OutputPath = result.HasErrors ? null : entry.OutputPath,
            Status = status,
            Duration = DateTime.UtcNow - startTime,
            Issues = issues,
            Metrics = metrics
        };
    }

    /// <summary>
    /// Analyzes migration potential for each file in the plan using MigrationAnalyzer.
    /// </summary>
    public async Task<AnalysisSummaryReport> AnalyzeAsync(MigrationPlan plan, IProgress<string>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var analyzer = new MigrationAnalyzer();
        var fileResults = new List<FileAnalysisResult>();

        var entriesToAnalyze = plan.Entries
            .Where(e => e.Convertibility != FileConvertibility.Skip)
            .ToList();

        foreach (var entry in entriesToAnalyze)
        {
            progress?.Report($"Analyzing {Path.GetFileName(entry.SourcePath)}...");

            var score = await analyzer.AnalyzeFileAsync(entry.SourcePath, plan.ProjectPath);
            entry.AnalysisScore = score;

            if (!score.WasSkipped)
            {
                fileResults.Add(new FileAnalysisResult
                {
                    FilePath = score.RelativePath,
                    Score = score.TotalScore,
                    Priority = score.Priority,
                    DimensionScores = score.Dimensions.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value.RawScore),
                    UnsupportedConstructs = score.UnsupportedConstructs
                });
            }
        }

        sw.Stop();

        var analyzedScores = fileResults.ToList();
        var priorityBreakdown = analyzedScores
            .GroupBy(f => f.Priority)
            .ToDictionary(g => g.Key, g => g.Count());

        var dimensionAverages = new Dictionary<ScoreDimension, double>();
        if (analyzedScores.Count > 0)
        {
            foreach (var dim in Enum.GetValues<ScoreDimension>())
            {
                var scores = analyzedScores
                    .Where(f => f.DimensionScores.ContainsKey(dim))
                    .Select(f => f.DimensionScores[dim])
                    .ToList();
                if (scores.Count > 0)
                    dimensionAverages[dim] = scores.Average();
            }
        }

        return new AnalysisSummaryReport
        {
            FilesAnalyzed = analyzedScores.Count,
            AverageScore = analyzedScores.Count > 0 ? analyzedScores.Average(f => f.Score) : 0,
            PriorityBreakdown = priorityBreakdown,
            DimensionAverages = dimensionAverages,
            Duration = sw.Elapsed,
            FileResults = fileResults
        };
    }

    /// <summary>
    /// Verifies contracts in converted Calor files using Z3.
    /// </summary>
    public async Task<VerificationSummaryReport> VerifyAsync(
        MigrationReport report,
        uint timeoutMs = VerificationOptions.DefaultTimeoutMs,
        IProgress<string>? progress = null)
    {
        var sw = Stopwatch.StartNew();

        if (!Z3ContextFactory.IsAvailable)
        {
            sw.Stop();
            return new VerificationSummaryReport
            {
                Z3Available = false,
                Duration = sw.Elapsed
            };
        }

        var fileResults = new List<FileVerificationSummary>();
        var totalProven = 0;
        var totalUnproven = 0;
        var totalDisproven = 0;
        var totalUnsupported = 0;
        var totalSkipped = 0;
        var filesSkipped = 0;

        var successfulFiles = report.FileResults
            .Where(f => f.Status is FileMigrationStatus.Success or FileMigrationStatus.Partial
                        && f.OutputPath != null)
            .ToList();

        foreach (var fileResult in successfulFiles)
        {
            progress?.Report($"Verifying {Path.GetFileName(fileResult.OutputPath!)}...");

            try
            {
                var source = await File.ReadAllTextAsync(fileResult.OutputPath!);
                var compileOptions = new CompilationOptions
                {
                    VerifyContracts = true,
                    VerificationTimeoutMs = timeoutMs
                };

                var compileResult = Program.Compile(source, fileResult.OutputPath, compileOptions);

                if (compileOptions.VerificationResults != null)
                {
                    var summary = compileOptions.VerificationResults.GetSummary();
                    var disprovenDetails = new List<string>();

                    foreach (var func in compileOptions.VerificationResults.Functions)
                    {
                        var allResults = func.PreconditionResults
                            .Concat(func.PostconditionResults);
                        foreach (var r in allResults.Where(r => r.Status == ContractVerificationStatus.Disproven))
                        {
                            disprovenDetails.Add(
                                $"{func.FunctionName}: {r.CounterexampleDescription ?? "counterexample found"}");
                        }
                    }

                    var fileSummary = new FileVerificationSummary
                    {
                        CalorPath = fileResult.OutputPath!,
                        TotalContracts = summary.Total,
                        Proven = summary.Proven,
                        Unproven = summary.Unproven,
                        Disproven = summary.Disproven,
                        DisprovenDetails = disprovenDetails
                    };

                    // Attach per-file verification to the FileMigrationResult
                    fileResult.Verification = fileSummary;

                    fileResults.Add(fileSummary);
                    totalProven += summary.Proven;
                    totalUnproven += summary.Unproven;
                    totalDisproven += summary.Disproven;
                    totalUnsupported += summary.Unsupported;
                    totalSkipped += summary.Skipped;
                }
                else
                {
                    filesSkipped++;
                }
            }
            catch
            {
                filesSkipped++;
            }
        }

        sw.Stop();

        var totalContracts = totalProven + totalUnproven + totalDisproven + totalUnsupported + totalSkipped;

        return new VerificationSummaryReport
        {
            FilesVerified = fileResults.Count,
            FilesSkipped = filesSkipped,
            TotalContracts = totalContracts,
            Proven = totalProven,
            Unproven = totalUnproven,
            Disproven = totalDisproven,
            Unsupported = totalUnsupported,
            ContractsSkipped = totalSkipped,
            Z3Available = true,
            Duration = sw.Elapsed,
            FileResults = fileResults
        };
    }

    private void AddRecommendations(MigrationReportBuilder builder, MigrationPlan plan)
    {
        var unsupportedFeatures = plan.Entries
            .SelectMany(e => e.DetectedFeatures)
            .Where(f => !FeatureSupport.IsFullySupported(f))
            .Distinct()
            .ToList();

        if (unsupportedFeatures.Contains("goto") || unsupportedFeatures.Contains("labeled-statement"))
        {
            builder.AddRecommendation("Refactor goto statements to use structured control flow (if/while/for)");
        }

        if (unsupportedFeatures.Contains("unsafe") || unsupportedFeatures.Contains("pointer"))
        {
            builder.AddRecommendation("Move unsafe code to a separate C# interop module");
        }

        if (unsupportedFeatures.Contains("linq-query"))
        {
            builder.AddRecommendation("Review LINQ query syntax conversions for correctness");
        }

        if (unsupportedFeatures.Contains("ref-parameter") || unsupportedFeatures.Contains("out-parameter"))
        {
            builder.AddRecommendation("Consider refactoring ref/out parameters to return tuples or Result types");
        }

        if (plan.PartialFiles > plan.TotalFiles * 0.3)
        {
            builder.AddRecommendation("Many files have partial conversions - consider breaking into smaller migration phases");
        }
    }
}

/// <summary>
/// Progress information for migration.
/// </summary>
public sealed class MigrationProgress
{
    public required string CurrentFile { get; init; }
    public required int ProcessedFiles { get; init; }
    public required int TotalFiles { get; init; }
    public required FileMigrationStatus Status { get; init; }

    public double PercentComplete => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;
}
