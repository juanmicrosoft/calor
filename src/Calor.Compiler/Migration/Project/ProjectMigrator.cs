using System.Diagnostics;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Verification.Z3;

namespace Calor.Compiler.Migration.Project;

/// <summary>
/// Orchestrates project-level migration.
/// </summary>
public sealed class ProjectMigrator
{
    private readonly MigrationPlanOptions _options;
    private IReadOnlyList<ProjectCompilationInputs>? _projectCompilationInputs;
    private string? _projectCompilationInputError;

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
    public async Task<MigrationReport> ExecuteAsync(MigrationPlan plan, bool dryRun = false, IProgress<MigrationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var projectFilePath = plan.ProjectFilePath
            ?? (File.Exists(plan.ProjectPath)
                && Path.GetExtension(plan.ProjectPath).Equals(
                    ".csproj",
                    StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(plan.ProjectPath)
                : null);
        if (plan.Direction == MigrationDirection.CSharpToCalor)
        {
            if (projectFilePath == null)
            {
                _projectCompilationInputs = null;
                _projectCompilationInputError = null;
            }
            else
            {
                try
                {
                    _projectCompilationInputs =
                        await ResolveProjectCompilationInputsAsync(
                            projectFilePath,
                            _options.Configuration,
                            _options.TargetFramework,
                            cancellationToken);
                    _projectCompilationInputError = null;
                }
                catch (Exception ex) when (ex is IOException
                    or InvalidOperationException
                    or System.Text.Json.JsonException)
                {
                    _projectCompilationInputs = null;
                    _projectCompilationInputError =
                        ex.Message;
                }
            }
        }

        var reportBuilder = new MigrationReportBuilder()
            .SetDirection(plan.Direction)
            .IncludeBenchmark(_options.IncludeBenchmark);

        var collisionPaths = new HashSet<string>(
            BuildStateCache.GetPathComparer());
        foreach (var collision in plan.Entries
                     .Where(entry =>
                         entry.Convertibility
                             != FileConvertibility.Skip)
                     .GroupBy(
                         entry => Path.GetFullPath(
                             entry.OutputPath),
                         BuildStateCache.GetPathComparer())
                     .Where(group => group.Count() > 1))
        {
            var sources = collision
                .Select(entry => entry.SourcePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (var entry in collision)
            {
                collisionPaths.Add(
                    Path.GetFullPath(entry.SourcePath));
                reportBuilder.AddFileResult(
                    new FileMigrationResult
                    {
                        SourcePath = entry.SourcePath,
                        OutputPath = null,
                        Status = FileMigrationStatus.Failed,
                        Issues =
                        [
                            new ConversionIssue
                            {
                                Severity =
                                    ConversionIssueSeverity.Error,
                                Feature =
                                    "migration-output-collision",
                                Message =
                                    $"Output path '{collision.Key}' "
                                    + "is shared by: "
                                    + string.Join(", ", sources)
                            }
                        ]
                    });
            }
        }

        var entriesToProcess = plan.Entries
            .Where(e => e.Convertibility != FileConvertibility.Skip)
            .Where(entry => !collisionPaths.Contains(
                Path.GetFullPath(entry.SourcePath)))
            .ToList();

        var processedCount = 0;
        var totalCount = entriesToProcess.Count;
        var crossModuleMap = plan.Direction == MigrationDirection.CalorToCSharp
            ? CompilationDriver.BuildCrossModuleFunctionMap(
                entriesToProcess.Select(entry => new FileInfo(entry.SourcePath)).ToList())
            : null;

        var wasCancelled = false;

        if (_options.Parallel && !dryRun)
        {
            var semaphore = new SemaphoreSlim(_options.MaxParallelism);
            var tasks = entriesToProcess.Select(async entry =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await ProcessEntryAsync(
                        entry, plan.Direction, dryRun, crossModuleMap, cancellationToken);

                    var newCount = Interlocked.Increment(ref processedCount);
                    progress?.Report(new MigrationProgress
                    {
                        CurrentFile = Path.GetFileName(entry.SourcePath),
                        ProcessedFiles = newCount,
                        TotalFiles = totalCount,
                        Status = result.Status
                    });

                    return result;
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
                reportBuilder.AddFileResult(result);
        }
        else
        {
            foreach (var entry in entriesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ProcessEntryAsync(
                    entry, plan.Direction, dryRun, crossModuleMap, cancellationToken);
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
        report.Summary.WasCancelled = wasCancelled;

        if (plan.Direction == MigrationDirection.CSharpToCalor)
        {
            await FinalizeLosslessProjectAsync(
                report,
                projectFilePath,
                dryRun,
                cancellationToken);
            RefreshSummary(report);
        }
        else if (plan.Direction == MigrationDirection.CalorToCSharp)
        {
            await FinalizeCalorToCSharpProjectAsync(
                report, plan.ProjectPath, dryRun, cancellationToken);
            RefreshSummary(report);
        }

        // Post-conversion: merge partial classes if enabled
        if (_options.MergePartialClasses &&
            _options.Fidelity == ConversionFidelity.Lossy &&
        _options.PreprocessorMode
            != PreprocessorConversionMode.SelectActiveBranchLossy &&
            !dryRun &&
            plan.Direction == MigrationDirection.CSharpToCalor)
        {
            await MergePartialClassesAsync(report);
        }

        return report;
    }

    /// <summary>
    /// Performs a dry run showing what would be migrated.
    /// </summary>
    public async Task<MigrationReport> DryRunAsync(MigrationPlan plan, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(plan, dryRun: true, cancellationToken: cancellationToken);
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
                var parseResult = CalorSourceHelper.Parse(
                    source,
                    file.OutputPath!);
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
                var validation = CalorSourceHelper.Parse(
                    newSource,
                    modules[i].File.OutputPath!);
                if (!validation.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Merged partial-class output failed validation: {modules[i].File.OutputPath}");
                }
                await ConversionFileWriter.WriteAtomicAsync(modules[i].File.OutputPath!, newSource);
            }
        }
    }

    private async Task<FileMigrationResult> ProcessEntryAsync(
        MigrationPlanEntry entry,
        MigrationDirection direction,
        bool dryRun,
        IReadOnlyDictionary<string, string>? crossModuleMap,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.PerFileTimeoutSeconds == 0)
        {
            cts.Cancel();
        }
        else
        {
            cts.CancelAfter(TimeSpan.FromSeconds(_options.PerFileTimeoutSeconds));
        }
        try
        {
            if (direction == MigrationDirection.CSharpToCalor)
            {
                return await ProcessCSharpToCalorAsync(entry, dryRun, startTime, cts.Token);
            }
            else
            {
                return await ProcessCalorToCSharpAsync(
                    entry, dryRun, startTime, crossModuleMap, cts.Token);
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
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
        catch (OperationCanceledException)
        {
            throw;
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

    private async Task<FileMigrationResult> ProcessCSharpToCalorAsync(
        MigrationPlanEntry entry,
        bool dryRun,
        DateTime startTime,
        CancellationToken cancellationToken)
    {
        var conversionOptions = new ConversionOptions
        {
            IncludeBenchmark = _options.IncludeBenchmark,
            Fidelity = _options.Fidelity,
            PreprocessorMode = _options.PreprocessorMode,
            ValidateRoundTripCSharp = false,
            PassthroughOnError = _options.PassthroughOnError,
            UseImplicitCallCloser = _options.UseImplicitCallCloser,
            Configuration = _options.Configuration,
            TargetFramework = _options.TargetFramework
        };
        var (projectInput, selectionError) = SelectProjectInput(entry.SourcePath);
        if (selectionError != null)
        {
            var excluded = selectionError.StartsWith(
                "Excluded from evaluated Compile items:",
                StringComparison.Ordinal);
            return new FileMigrationResult
            {
                SourcePath = entry.SourcePath,
                OutputPath = null,
                Status = excluded
                    ? FileMigrationStatus.Skipped
                    : FileMigrationStatus.Failed,
                Duration = DateTime.UtcNow - startTime,
                Issues =
                [
                    new ConversionIssue
                    {
                        Severity = excluded
                            ? ConversionIssueSeverity.Info
                            : ConversionIssueSeverity.Error,
                        Feature = "preprocessor-project-selection",
                        Message = selectionError
                    }
                ]
            };
        }
        if (projectInput != null)
        {
            conversionOptions.ParseOptions =
                new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                    _options.LanguageVersion ?? projectInput.LanguageVersion,
                    _options.DocumentationMode,
                    Path.GetExtension(entry.SourcePath)
                        .Equals(
                            ".csx",
                            StringComparison.OrdinalIgnoreCase)
                        ? Microsoft.CodeAnalysis.SourceCodeKind.Script
                        : Microsoft.CodeAnalysis.SourceCodeKind.Regular,
                    projectInput.PreprocessorSymbols)
                .WithFeatures(_options.ParseFeatures);
            conversionOptions.TargetFramework = projectInput.TargetFramework;
            conversionOptions.OutputKind = projectInput.OutputKind;
            conversionOptions.References = BuildGeneratedReferences(projectInput)
                .Select(reference => new ConversionReference(
                    reference.Path,
                    reference.Aliases))
                .ToArray();
        }
        else
        {
            conversionOptions.ParseOptions =
                new Microsoft.CodeAnalysis.CSharp
                    .CSharpParseOptions(
                        _options.LanguageVersion
                            ?? Microsoft.CodeAnalysis.CSharp
                                .LanguageVersion.Preview,
                        _options.DocumentationMode,
                        Path.GetExtension(entry.SourcePath)
                            .Equals(
                                ".csx",
                                StringComparison.OrdinalIgnoreCase)
                            ? Microsoft.CodeAnalysis
                                .SourceCodeKind.Script
                            : Microsoft.CodeAnalysis
                                .SourceCodeKind.Regular,
                        _options.DefinedSymbols)
                    .WithFeatures(
                        _options.ParseFeatures);
        }
        conversionOptions.DefinedSymbols = _options.DefinedSymbols;

        if (!string.IsNullOrEmpty(_options.ModuleNameOverride))
        {
            conversionOptions.ModuleName = _options.ModuleNameOverride;
        }

        var converter = new CSharpToCalorConverter(conversionOptions);

        var result = await converter.ConvertFileAsync(entry.SourcePath, cancellationToken);

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
            var originalSource = await File.ReadAllTextAsync(entry.SourcePath, cancellationToken);
            metrics = BenchmarkIntegration.CalculateMetrics(originalSource, result.CalorSource);
        }

        var status = result.Success
            ? (result.Context.HasWarnings ? FileMigrationStatus.Partial : FileMigrationStatus.Success)
            : FileMigrationStatus.Failed;

        // Validate: parse and compile the generated Calor to catch false-positive "success"
        var issues = result.Issues.ToList();
        string? generatedCSharp = null;
        if (result.Success &&
            result.CalorSource != null)
        {
            var compileResult = Program.Compile(
                result.CalorSource,
                entry.OutputPath,
                new CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive,
                    DeferGeneratedOutputValidation = true,
                    CancellationToken = cancellationToken
                });
            if (compileResult.HasErrors)
            {
                status = FileMigrationStatus.Failed;
                issues.AddRange(compileResult.Diagnostics.Errors.Select(diagnostic => new ConversionIssue
                {
                    Severity = ConversionIssueSeverity.Error,
                    Feature = "roundtrip-calor-validation",
                    Message = $"Generated Calor failed compilation: {diagnostic.Message}",
                    Line = diagnostic.Span.Line,
                    Column = diagnostic.Span.Column
                }));
            }
            else
            {
                generatedCSharp = compileResult.GeneratedCode;
            }
        }
        if (_options.ValidateOutput && result.Success && result.CalorSource != null)
        {
            var parseResult = CalorSourceHelper.Parse(
                result.CalorSource,
                entry.OutputPath);
            if (!parseResult.IsSuccess)
            {
                status = FileMigrationStatus.Failed;
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
                        UnknownCallPolicy = UnknownCallPolicy.Permissive,
                        DeferGeneratedOutputValidation = true,
                        CancellationToken = cancellationToken
                    };
                    var compileResult = Program.Compile(result.CalorSource, entry.OutputPath, compileOptions);
                    if (compileResult.HasErrors)
                    {
                        status = FileMigrationStatus.Failed;
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
                    status = FileMigrationStatus.Failed;
                    issues.Add(new ConversionIssue
                    {
                        Severity = ConversionIssueSeverity.Error,
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
            OutputPath = status is FileMigrationStatus.Success or FileMigrationStatus.Partial
                ? entry.OutputPath
                : null,
            Status = status,
            Duration = DateTime.UtcNow - startTime,
            Issues = issues,
            Losses = result.Losses,
            Metadata = result.Metadata,
            Metrics = metrics,
            Analysis = analysisResult,
            ConvertedSource = result.CalorSource,
            GeneratedCSharp = generatedCSharp
        };
    }

    private async Task FinalizeLosslessProjectAsync(
        MigrationReport report,
        string? projectFilePath,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var converted = report.FileResults
            .Where(result => result.Status is FileMigrationStatus.Success or FileMigrationStatus.Partial)
            .ToList();

        if (report.FileResults.Any(result =>
                result.Status is FileMigrationStatus.Failed or FileMigrationStatus.TimedOut))
        {
            foreach (var result in converted)
            {
                result.Status = FileMigrationStatus.Failed;
                result.OutputPath = null;
                result.Issues.Add(new ConversionIssue
                {
                    Severity = ConversionIssueSeverity.Error,
                    Feature = "project-validation",
                    Message = "Project migration was not written because another file failed conversion."
                });
            }
            return;
        }

        IReadOnlyList<ProjectCompilationInputs> projectInputs;
        try
        {
            projectInputs = projectFilePath == null
                ? [CreateLooseDirectoryInputs(
                    converted.Select(result =>
                        result.SourcePath))]
                : await ResolveProjectCompilationInputsAsync(
                    projectFilePath,
                    _options.Configuration,
                    _options.TargetFramework,
                    cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                   or System.Text.Json.JsonException)
        {
            MarkProjectValidationFailure(
                converted,
                $"Project references could not be resolved for generated C# validation: {ex.Message}");
            return;
        }
        var convertedSourcePaths = converted
            .Select(result => Path.GetFullPath(result.SourcePath))
            .ToHashSet(BuildStateCache.GetPathComparer());
        var errors = projectInputs.SelectMany(inputs =>
        {
            var targetSourcePaths = inputs.SourcePaths
                .Select(Path.GetFullPath)
                .ToHashSet(BuildStateCache.GetPathComparer());
            var targetResults = converted
                .Where(result => !string.IsNullOrWhiteSpace(result.GeneratedCSharp))
                .Where(result => targetSourcePaths.Count == 0 ||
                    targetSourcePaths.Contains(Path.GetFullPath(result.SourcePath))
                    || Path.GetExtension(result.SourcePath).Equals(
                        ".csx",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
            var sources = targetResults
                .Where(result => !Path.GetExtension(result.SourcePath)
                    .Equals(".csx", StringComparison.OrdinalIgnoreCase))
                .Select(result => new CodeGen.GeneratedCSharpSource(
                    result.GeneratedCSharp!,
                    Path.ChangeExtension(result.OutputPath ?? result.SourcePath, ".g.cs"),
                    result.OutputPath))
                .ToArray();
            var scriptResults = targetResults
                .Where(result => Path.GetExtension(result.SourcePath)
                    .Equals(".csx", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sources.Length == 0 && scriptResults.Count == 0)
                return [];

            var additionalSources = inputs.SourcePaths
                .Where(path => !convertedSourcePaths.Contains(Path.GetFullPath(path)))
                .Select(path => new CodeGen.GeneratedCSharpSource(
                    File.ReadAllText(path), path));
            var context = new CodeGen.GeneratedCSharpCompilationContext
            {
                ReferencePaths = inputs.ReferencePaths,
                References = BuildGeneratedReferences(inputs),
                AdditionalSources = additionalSources,
                AllowUnsafe = inputs.AllowUnsafe,
                OutputKind = inputs.OutputKind,
                LanguageVersion =
                    _options.LanguageVersion ?? inputs.LanguageVersion,
                DocumentationMode = _options.DocumentationMode,
                Features = _options.ParseFeatures,
                PreprocessorSymbols = EffectivePreprocessorSymbols(inputs),
                IncludeImplicitGlobalUsings = inputs.IncludeImplicitGlobalUsings,
                AnalyzerPaths = inputs.AnalyzerPaths,
                AdditionalFilePaths = inputs.AdditionalFilePaths,
                NullableContextOptions = inputs.NullableContextOptions,
                TreatWarningsAsErrors = inputs.TreatWarningsAsErrors,
                AnalyzerConfigPath = inputs.AnalyzerConfigPath
            };
            var diagnostics = new List<Microsoft.CodeAnalysis.Diagnostic>();
            if (sources.Length > 0)
            {
                var validation = CodeGen.GeneratedCSharpCompiler.Validate(
                    sources,
                    context);
                diagnostics.AddRange(
                    validation.SyntaxErrors.Concat(
                        validation.CompilationErrors));
            }
            foreach (var script in scriptResults)
            {
                var validation = CodeGen.GeneratedCSharpCompiler.Validate(
                    [new CodeGen.GeneratedCSharpSource(
                        script.GeneratedCSharp!,
                        script.SourcePath)],
                    context with
                    {
                        AdditionalSources = null,
                        SourceCodeKind =
                            Microsoft.CodeAnalysis.SourceCodeKind.Script,
                        OutputKind = Microsoft.CodeAnalysis.OutputKind
                            .DynamicallyLinkedLibrary,
                        LanguageVersion = Microsoft.CodeAnalysis.CSharp
                            .LanguageVersionFacts.TryParse(
                                script.Metadata?.LanguageVersion,
                                out var scriptLanguage)
                            ? scriptLanguage
                            : context.LanguageVersion,
                        DocumentationMode = Enum.TryParse<
                            Microsoft.CodeAnalysis.DocumentationMode>(
                                script.Metadata?.DocumentationMode,
                                true,
                                out var scriptDocumentation)
                            ? scriptDocumentation
                            : context.DocumentationMode,
                        Features = script.Metadata?.Features
                            ?? _options.ParseFeatures,
                        PreprocessorSymbols =
                            script.Metadata?.DefinedSymbols
                            ?? EffectivePreprocessorSymbols(inputs)
                    });
                diagnostics.AddRange(
                    validation.SyntaxErrors.Concat(
                        validation.CompilationErrors));
            }
            return diagnostics;
        }).ToList();
        if (errors.Count > 0)
        {
            foreach (var result in converted)
            {
                result.Status = FileMigrationStatus.Failed;
                result.OutputPath = null;
                foreach (var error in errors.Take(20))
                {
                    result.Issues.Add(new ConversionIssue
                    {
                        Severity = ConversionIssueSeverity.Error,
                        Feature = "roundtrip-csharp-validation",
                        Message = $"Lossless project C# validation failed ({error.Id}): {error.GetMessage()}"
                    });
                }
            }

            return;
        }

        if (dryRun)
        {
            return;
        }

        foreach (var result in converted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.OutputPath != null && result.ConvertedSource != null)
            {
                await ConversionFileWriter.WriteAtomicAsync(
                    result.OutputPath,
                    result.ConvertedSource,
                    new System.Text.UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: false),
                    cancellationToken: cancellationToken);
            }
        }

        static ProjectCompilationInputs
            CreateLooseDirectoryInputs(
                IEnumerable<string> sourcePaths)
            => new(
                TargetFramework: null,
                ReferencePaths: [],
                ReferenceAliases:
                    new Dictionary<string,
                        IReadOnlyList<string>>(),
                SourcePaths: sourcePaths
                    .Select(Path.GetFullPath)
                    .ToArray(),
                CalorSourcePaths: [],
                AllowUnsafe: true,
                OutputKind:
                    Microsoft.CodeAnalysis.OutputKind
                        .DynamicallyLinkedLibrary,
                LanguageVersion:
                    Microsoft.CodeAnalysis.CSharp
                        .LanguageVersion.Preview,
                PreprocessorSymbols: [],
                IncludeImplicitGlobalUsings: false,
                AnalyzerPaths: [],
                AdditionalFilePaths: [],
                NullableContextOptions:
                    Microsoft.CodeAnalysis
                        .NullableContextOptions.Disable,
                TreatWarningsAsErrors: false,
                AnalyzerConfigPath: null);
    }

    private async Task FinalizeCalorToCSharpProjectAsync(
        MigrationReport report,
        string projectPath,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var converted = report.FileResults
            .Where(result => result.Status == FileMigrationStatus.Success &&
                result.OutputPath != null &&
                result.GeneratedCSharp != null)
            .ToList();
        if (report.FileResults.Any(result =>
                result.Status is FileMigrationStatus.Failed or FileMigrationStatus.TimedOut))
        {
            MarkProjectValidationFailure(
                converted,
                "Calor-to-C# project migration was not written because another file failed compilation.");
            return;
        }

        try
        {
            var projectInputs = await ResolveProjectCompilationInputsAsync(
                projectPath,
                _options.Configuration,
                _options.TargetFramework,
                cancellationToken);
            var generatedOutputPaths = converted
                .Select(result => Path.GetFullPath(result.OutputPath!))
                .ToHashSet(BuildStateCache.GetPathComparer());
            foreach (var inputs in projectInputs)
            {
                var targetCalorPaths = inputs.CalorSourcePaths
                    .Select(Path.GetFullPath)
                    .ToHashSet(BuildStateCache.GetPathComparer());
                var targetConverted = converted
                    .Where(result => targetCalorPaths.Count == 0 ||
                        targetCalorPaths.Contains(Path.GetFullPath(result.SourcePath)))
                    .ToList();
                if (targetConverted.Count == 0)
                    continue;
                var sources = targetConverted.Select(result =>
                    new CodeGen.GeneratedCSharpSource(
                        result.GeneratedCSharp!,
                        result.OutputPath!,
                        result.SourcePath));
                var additionalSources = inputs.SourcePaths
                    .Where(path => !generatedOutputPaths.Contains(Path.GetFullPath(path)))
                    .Select(path => new CodeGen.GeneratedCSharpSource(
                        File.ReadAllText(path), path))
                    .ToList();
                var validation = CodeGen.GeneratedCSharpCompiler.Validate(
                    sources,
                    new CodeGen.GeneratedCSharpCompilationContext
                    {
                        ReferencePaths = inputs.ReferencePaths,
                        References = BuildGeneratedReferences(inputs),
                        AdditionalSources = additionalSources,
                        AllowUnsafe = inputs.AllowUnsafe,
                        OutputKind = inputs.OutputKind,
                        LanguageVersion =
                            _options.LanguageVersion ?? inputs.LanguageVersion,
                        PreprocessorSymbols = EffectivePreprocessorSymbols(inputs),
                        IncludeImplicitGlobalUsings = inputs.IncludeImplicitGlobalUsings,
                        AnalyzerPaths = inputs.AnalyzerPaths,
                        AdditionalFilePaths = inputs.AdditionalFilePaths,
                        NullableContextOptions = inputs.NullableContextOptions,
                        TreatWarningsAsErrors = inputs.TreatWarningsAsErrors,
                        AnalyzerConfigPath = inputs.AnalyzerConfigPath
                    });
                if (validation.CompilationSuccess)
                    continue;

                var messages = validation.CompilationErrors
                    .Take(20)
                    .Select(error =>
                        $"Calor project C# validation failed ({error.Id}): {error.GetMessage()}")
                    .ToList();
                MarkProjectValidationFailure(converted, messages);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                   or System.Text.Json.JsonException)
        {
            MarkProjectValidationFailure(
                converted,
                $"Project references could not be resolved for generated C# validation: {ex.Message}");
            return;
        }

        if (dryRun)
            return;

        foreach (var result in converted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ConversionFileWriter.WriteAtomicAsync(
                result.OutputPath!,
                result.GeneratedCSharp!,
                cancellationToken: cancellationToken);
        }
    }

    private static void MarkProjectValidationFailure(
        IEnumerable<FileMigrationResult> results,
        string message)
        => MarkProjectValidationFailure(results, [message]);

    private static void MarkProjectValidationFailure(
        IEnumerable<FileMigrationResult> results,
        IReadOnlyList<string> messages)
    {
        foreach (var result in results)
        {
            result.Status = FileMigrationStatus.Failed;
            result.OutputPath = null;
            foreach (var message in messages)
            {
                result.Issues.Add(new ConversionIssue
                {
                    Severity = ConversionIssueSeverity.Error,
                    Feature = "project-csharp-validation",
                    Message = message
                });
            }
        }
    }

    private static void RefreshSummary(MigrationReport report)
    {
        var summary = report.Summary;
        var allIssues = report.FileResults.SelectMany(result => result.Issues).ToList();

        summary.SuccessfulFiles = report.FileResults.Count(result => result.Status == FileMigrationStatus.Success);
        summary.PartialFiles = report.FileResults.Count(result => result.Status == FileMigrationStatus.Partial);
        summary.FailedFiles = report.FileResults.Count(result => result.Status == FileMigrationStatus.Failed);
        summary.SkippedFiles = report.FileResults.Count(result => result.Status == FileMigrationStatus.Skipped);
        summary.TimedOutFiles = report.FileResults.Count(result => result.Status == FileMigrationStatus.TimedOut);
        summary.TotalErrors = allIssues.Count(issue => issue.Severity == ConversionIssueSeverity.Error);
        summary.TotalWarnings = allIssues.Count(issue => issue.Severity == ConversionIssueSeverity.Warning);

        summary.MostCommonIssues.Clear();
        summary.MostCommonIssues.AddRange(allIssues
            .GroupBy(issue => issue.Message)
            .OrderByDescending(group => group.Count())
            .Take(10)
            .Select(group => $"{group.Key} ({group.Count()}x)"));

        summary.UnsupportedFeatures.Clear();
        summary.UnsupportedFeatures.AddRange(allIssues
            .Where(issue => issue.Feature != null && !FeatureSupport.IsFullySupported(issue.Feature))
            .Select(issue => issue.Feature!)
            .Distinct());
    }

    private (ProjectCompilationInputs? Input, string? Error) SelectProjectInput(
        string sourcePath)
    {
        if (_projectCompilationInputs == null)
        {
            return _options.PreprocessorMode
                == PreprocessorConversionMode.SelectActiveBranchLossy
                ? (null,
                    "Selected-branch project migration requires resolved project parse options. "
                    + (_projectCompilationInputError ?? "No project compilation inputs were available."))
                : (null, null);
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var candidates = _projectCompilationInputs
            .Where(input => input.SourcePaths.Count == 0
                || input.SourcePaths.Any(path =>
                    BuildStateCache.GetPathComparer().Equals(
                        Path.GetFullPath(path),
                        fullSourcePath)))
            .ToList();
        if (!string.IsNullOrWhiteSpace(_options.TargetFramework))
        {
            candidates = candidates.Where(input =>
                    string.Equals(
                        input.TargetFramework,
                        _options.TargetFramework,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return (null,
                $"Excluded from evaluated Compile items: '{sourcePath}'.");
        }

        if (candidates.Count == 1)
            return (candidates[0], null);

        var configurations = candidates
            .GroupBy(ParseConfigurationIdentity)
            .ToList();
        if (configurations.Count != 1)
        {
            return (null,
                $"Conditional-compilation configuration for '{sourcePath}' is ambiguous across "
                + $"{candidates.Count} target frameworks ({string.Join(", ", candidates.Select(input => input.TargetFramework ?? "<default>"))}). "
                + "Specify a target framework explicitly; symbols are never unioned across frameworks.");
        }

        return (candidates[0] with
        {
            TargetFramework = null
        }, null);

        static string ParseConfigurationIdentity(
            ProjectCompilationInputs input)
            => $"{input.LanguageVersion}|{input.OutputKind}|"
                + string.Join(
                    ";",
                    input.PreprocessorSymbols
                        .OrderBy(symbol => symbol, StringComparer.Ordinal));
    }

    private IReadOnlyList<string> EffectivePreprocessorSymbols(
        ProjectCompilationInputs inputs)
        => inputs.PreprocessorSymbols
            .Concat(_options.DefinedSymbols)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<GeneratedCSharpReference>
        BuildGeneratedReferences(ProjectCompilationInputs inputs)
        => inputs.ReferencePaths
            .Select(path => new GeneratedCSharpReference(
                path,
                inputs.ReferenceAliases.TryGetValue(
                    path,
                    out var aliases)
                    ? aliases
                    : Array.Empty<string>()))
            .ToList();

    internal sealed record ProjectCompilationInputs(
        string? TargetFramework,
        IReadOnlyList<string> ReferencePaths,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ReferenceAliases,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyList<string> CalorSourcePaths,
        bool AllowUnsafe,
        Microsoft.CodeAnalysis.OutputKind OutputKind,
        Microsoft.CodeAnalysis.CSharp.LanguageVersion LanguageVersion,
        IReadOnlyList<string> PreprocessorSymbols,
        bool IncludeImplicitGlobalUsings,
        IReadOnlyList<string> AnalyzerPaths,
        IReadOnlyList<string> AdditionalFilePaths,
        Microsoft.CodeAnalysis.NullableContextOptions NullableContextOptions,
        bool TreatWarningsAsErrors,
        string? AnalyzerConfigPath);

    internal static async Task<IReadOnlyList<ProjectCompilationInputs>> ResolveProjectCompilationInputsAsync(
        string projectPath,
        string configuration,
        string? selectedTargetFramework,
        CancellationToken cancellationToken)
    {
        var projectFile = FindProjectFile(projectPath);
        if (projectFile == null)
        {
            return [new ProjectCompilationInputs(
                selectedTargetFramework, [],
                new Dictionary<string, IReadOnlyList<string>>(),
                [], [], true,
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.Default,
                [], true, [], [],
                Microsoft.CodeAnalysis.NullableContextOptions.Disable,
                false, null)];
        }

        var targetFrameworks = await ResolveTargetFrameworksAsync(
            projectFile,
            configuration,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(selectedTargetFramework))
        {
            if (!targetFrameworks.Any(targetFramework =>
                    string.Equals(
                        targetFramework,
                        selectedTargetFramework,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Target framework '{selectedTargetFramework}' is not declared by '{projectFile}'.");
            }
            targetFrameworks = [selectedTargetFramework];
        }
        var results = new List<ProjectCompilationInputs>();
        foreach (var targetFramework in targetFrameworks)
        {
            results.Add(await ResolveProjectCompilationInputAsync(
                projectFile,
                targetFramework,
                configuration,
                cancellationToken));
        }
        return results;
    }

    private static async Task<ProjectCompilationInputs> ResolveProjectCompilationInputAsync(
        string projectFile,
        string? targetFramework,
        string configuration,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add(
            "-t:ResolveAssemblyReferences;GenerateMSBuildEditorConfigFile");
        startInfo.ArgumentList.Add(
            "-getItem:ReferencePath,Compile,CalorCompile,Analyzer,AdditionalFiles");
        startInfo.ArgumentList.Add(
            "-getProperty:AllowUnsafeBlocks,OutputType,DefineConstants,LangVersion,ImplicitUsings,Nullable,TreatWarningsAsErrors,GeneratedMSBuildEditorConfigFile");
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            startInfo.ArgumentList.Add($"-property:TargetFramework={targetFramework}");
        }
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            startInfo.ArgumentList.Add($"-property:Configuration={configuration}");
        }
        startInfo.ArgumentList.Add("-verbosity:quiet");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start dotnet msbuild.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet msbuild failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        using var document = System.Text.Json.JsonDocument.Parse(output);
        if (!document.RootElement.TryGetProperty("Items", out var items) ||
            !items.TryGetProperty("ReferencePath", out var references) ||
            !document.RootElement.TryGetProperty("Properties", out var properties))
        {
            throw new InvalidOperationException(
                "dotnet msbuild did not return project compilation inputs.");
        }

        var referencePaths = references.EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(BuildStateCache.GetPathComparer())
            .ToList();
        var referenceAliases = references.EnumerateArray()
            .Select(item => (
                Path: item.GetProperty("Identity").GetString(),
                Aliases: item.TryGetProperty("Aliases", out var aliases)
                    ? aliases.GetString()
                    : null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Path)
                && File.Exists(item.Path))
            .ToDictionary(
                item => Path.GetFullPath(item.Path!),
                item => (IReadOnlyList<string>)(item.Aliases ?? "")
                    .Split(
                        [';', ','],
                        StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries),
                BuildStateCache.GetPathComparer());
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var sourcePaths = items.TryGetProperty("Compile", out var compileItems)
            ? compileItems.EnumerateArray()
                .Select(item => item.GetProperty("Identity").GetString())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.IsPathRooted(path!)
                    ? Path.GetFullPath(path!)
                    : Path.GetFullPath(Path.Combine(projectDirectory, path!)))
                .Where(File.Exists)
                .Distinct(BuildStateCache.GetPathComparer())
                .ToList()
            : [];
        var allowUnsafeText = GetProperty(properties, "AllowUnsafeBlocks");
        var outputType = GetProperty(properties, "OutputType");
        var languageVersionText = GetProperty(properties, "LangVersion");
        var implicitUsings = GetProperty(properties, "ImplicitUsings");
        var languageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersionFacts.TryParse(
            languageVersionText, out var parsedLanguageVersion)
            ? parsedLanguageVersion
            : Microsoft.CodeAnalysis.CSharp.LanguageVersion.Default;

        return new ProjectCompilationInputs(
            targetFramework,
            referencePaths,
            referenceAliases,
            sourcePaths,
            ResolveItems(items, "CalorCompile", projectDirectory),
            allowUnsafeText.Equals("true", StringComparison.OrdinalIgnoreCase),
            outputType.Equals("winexe", StringComparison.OrdinalIgnoreCase)
                ? Microsoft.CodeAnalysis.OutputKind.WindowsApplication
                : outputType.Equals("exe", StringComparison.OrdinalIgnoreCase)
                ? Microsoft.CodeAnalysis.OutputKind.ConsoleApplication
                : Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
            languageVersion,
            GetProperty(properties, "DefineConstants").Split(
                [';', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            implicitUsings.Equals("enable", StringComparison.OrdinalIgnoreCase) ||
                implicitUsings.Equals("true", StringComparison.OrdinalIgnoreCase),
            ResolveItems(items, "Analyzer", projectDirectory),
            ResolveItems(items, "AdditionalFiles", projectDirectory),
            ParseNullableContextOptions(GetProperty(properties, "Nullable")),
            GetProperty(properties, "TreatWarningsAsErrors")
                .Equals("true", StringComparison.OrdinalIgnoreCase),
            ResolveOptionalPath(
                GetProperty(properties, "GeneratedMSBuildEditorConfigFile"),
                projectDirectory));

        static string GetProperty(System.Text.Json.JsonElement properties, string name)
            => properties.TryGetProperty(name, out var property)
                ? property.GetString() ?? string.Empty
                : string.Empty;

        static IReadOnlyList<string> ResolveItems(
            System.Text.Json.JsonElement items,
            string name,
            string projectDirectory)
            => items.TryGetProperty(name, out var values)
                ? values.EnumerateArray()
                    .Select(item => item.GetProperty("Identity").GetString())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => Path.IsPathRooted(path!)
                        ? Path.GetFullPath(path!)
                        : Path.GetFullPath(Path.Combine(projectDirectory, path!)))
                    .Where(File.Exists)
                    .Distinct(BuildStateCache.GetPathComparer())
                    .ToList()
                : [];

        static string? ResolveOptionalPath(string path, string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(projectDirectory, path));
            return File.Exists(fullPath) ? fullPath : null;
        }

        static Microsoft.CodeAnalysis.NullableContextOptions ParseNullableContextOptions(
            string value)
            => value.ToLowerInvariant() switch
            {
                "enable" => Microsoft.CodeAnalysis.NullableContextOptions.Enable,
                "warnings" => Microsoft.CodeAnalysis.NullableContextOptions.Warnings,
                "annotations" => Microsoft.CodeAnalysis.NullableContextOptions.Annotations,
                _ => Microsoft.CodeAnalysis.NullableContextOptions.Disable
            };
    }

    private static async Task<IReadOnlyList<string?>> ResolveTargetFrameworksAsync(
        string projectFile,
        string configuration,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("-getProperty:TargetFramework,TargetFrameworks");
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            startInfo.ArgumentList.Add(
                $"-property:Configuration={configuration}");
        }
        startInfo.ArgumentList.Add("-verbosity:quiet");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start dotnet msbuild.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet msbuild failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        using var document = System.Text.Json.JsonDocument.Parse(output);
        if (!document.RootElement.TryGetProperty("Properties", out var properties))
            throw new InvalidOperationException("dotnet msbuild did not return project properties.");

        var targetFrameworks = properties.TryGetProperty("TargetFrameworks", out var plural)
            ? plural.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(targetFrameworks))
        {
            return targetFrameworks.Split(
                    ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => (string?)value)
                .ToList();
        }

        var targetFramework = properties.TryGetProperty("TargetFramework", out var single)
            ? single.GetString()
            : null;
        return [string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework];
    }

    private static string? FindProjectFile(string projectPath)
    {
        if (File.Exists(projectPath) &&
            string.Equals(Path.GetExtension(projectPath), ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(projectPath);
        }

        var directory = Directory.Exists(projectPath)
            ? projectPath
            : Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        var originalDirectory = Path.GetFullPath(directory);
        for (var current = originalDirectory;
             current != null;
             current = Directory.GetParent(current)?.FullName)
        {
            var projects = Directory.GetFiles(
                current, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projects.Length == 1)
                return Path.GetFullPath(projects[0]);
            if (projects.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Project context is ambiguous: '{current}' contains multiple project files.");
            }
        }

        var nestedProjects = Directory.GetFiles(
            originalDirectory, "*.csproj", SearchOption.AllDirectories);
        if (nestedProjects.Length == 1)
            return Path.GetFullPath(nestedProjects[0]);
        if (nestedProjects.Length > 1)
        {
            throw new InvalidOperationException(
                $"Project context is ambiguous: '{originalDirectory}' contains multiple project files.");
        }

        return null;
    }

    private async Task<FileMigrationResult> ProcessCalorToCSharpAsync(
        MigrationPlanEntry entry,
        bool dryRun,
        DateTime startTime,
        IReadOnlyDictionary<string, string>? crossModuleMap,
        CancellationToken cancellationToken)
    {
        var source = await File.ReadAllTextAsync(entry.SourcePath, cancellationToken);
        var compileOptions = new CompilationOptions
        {
            DeferGeneratedOutputValidation = true,
            CancellationToken = cancellationToken
        };
        compileOptions.CrossModuleFunctionModules = crossModuleMap;
        var result = Program.Compile(source, entry.SourcePath, compileOptions);

        FileMetrics? metrics = null;
        if (!result.HasErrors && _options.IncludeBenchmark)
        {
            metrics = BenchmarkIntegration.CalculateMetrics(source, result.GeneratedCode);
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
            Metrics = metrics,
            GeneratedCSharp = result.HasErrors ? null : result.GeneratedCode
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
