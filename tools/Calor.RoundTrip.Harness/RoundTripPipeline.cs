using System.Collections.Concurrent;
using Calor.Compiler.Migration;
using Calor.Compiler.CodeGen;

namespace Calor.RoundTrip.Harness;

internal sealed record MultiContextConversionPlan(
    ProjectFileParseContext? Representative,
    PreprocessorConversionMode PreprocessorMode,
    string SelectionMode,
    IReadOnlyList<ProjectFileParseContext> Contexts,
    IReadOnlyList<string> Errors);

/// <summary>
/// Orchestrates the full round-trip verification pipeline:
/// Snapshot → Baseline → Convert → Build → Test → Compare.
/// </summary>
public sealed class RoundTripPipeline
{
    private RunEvidence? _evidence;

    /// <summary>
    /// Run the full round-trip pipeline for a target project.
    /// </summary>
    public async Task<RoundTripReport> RunAsync(
        RoundTripConfig config,
        CancellationToken cancellationToken = default)
    {
        if (config.TestAttemptsPerLeg is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(config), "TestAttemptsPerLeg must be between 1 and 3.");
        var report = new RoundTripReport
        {
            ProjectName = config.ProjectName,
            CalorVersion = GetCalorVersion(),
            StartedAt = DateTimeOffset.UtcNow,
            MinimumCoverageFraction = config.MinimumCoverageFraction,
            MinimumNativeFraction = config.MinimumNativeFraction,
            Evidence = new RunEvidence { DeclaredTestAttemptsPerLeg = config.TestAttemptsPerLeg },
        };
        _evidence = report.Evidence;
        try
        {
            CaptureInventory(config.OriginalProjectPath, config, report);
            await CaptureProvenanceAsync(config, report.Evidence, cancellationToken);
            await RunCoreAsync(config, report, cancellationToken);
        }
        catch (Exception ex)
        {
            report.Inconclusive = true;
            report.InconclusiveReason = $"{ex.GetType().Name}: {ex.Message}";
            report.Evidence.Failures.Add(ex.ToString());
            report.Comparison ??= new TestComparison { Status = ComparisonStatus.Incomplete };
        }
        finally
        {
            report.FinishedAt = DateTimeOffset.UtcNow;
            _evidence = null;
        }
        return report;
    }

    private async Task RunCoreAsync(RoundTripConfig config, RoundTripReport report, CancellationToken cancellationToken)
    {
        // Step 1: Snapshot
        Console.WriteLine($"Phase 1/5: Creating working copy of {config.ProjectName}...");
        var workDir = PrepareWorkingCopy(config, cancellationToken);
        Console.WriteLine($"  Working directory: {workDir}");

        // No separate pre-restore step: `dotnet build`/`dotnet test` restore the graph
        // coherently in one invocation, so a standalone restore only adds latency.

        // Step 2: Baseline tests. An explicit `dotnet build` first, then `dotnet test
        // --no-build`, so a failed baseline build is reported as exactly that (rather
        // than surfacing as an empty test run) and the round-trip build later compares
        // against a known-good baseline.
        Console.WriteLine("\nPhase 2/5: Running baseline tests...");
        TrxParser.CleanTrxFiles(workDir);
        report.BaselineBuildResult = await BuildProjectAsync(
            workDir, config, cancellationToken, phase: "baseline-build");
        if (!report.BaselineBuildResult.Succeeded)
            Console.WriteLine("  WARNING: baseline build failed (the vendored subject does not build clean here)");
        report.Baseline = await RunTestAttemptsAsync(
            workDir, config, "baseline", report.BaselineBuildResult.Succeeded, cancellationToken);
        Console.WriteLine($"  Baseline: {report.Baseline.Passed}/{report.Baseline.TotalTests} passed, {report.Baseline.Failed} failed, {report.Baseline.Skipped} skipped");

        // Step 3: Convert & Replace
        Console.WriteLine("\nPhase 3/5: Converting library source files...");
        report.FileResults = await ConvertAndReplaceAsync(
            workDir, config, report, cancellationToken);
        var replaced = report.FileResults.Count(f => f.Status == FileStatus.Replaced);
        var total = report.FileResults.Count;
        Console.WriteLine($"  Converted: {replaced}/{total} files replaced");

        // Step 4: Build (with recovery — revert files that cause build errors)
        Console.WriteLine("\nPhase 4/5: Building modified project...");
        report.BuildResult = await BuildProjectAsync(workDir, config, cancellationToken, phase: "candidate-build",
            candidateFiles: report.FileResults.Where(file => file.Status == FileStatus.Replaced).Select(file => file.FilePath).ToList());

        if (!report.BuildResult.Succeeded)
        {
            Console.WriteLine("  Build failed — attempting recovery by reverting problematic files...");
            var revertedCount = await RecoverBuildAsync(
                workDir, config, report.FileResults, cancellationToken);
            if (revertedCount > 0)
            {
                Console.WriteLine($"  Reverted {revertedCount} file(s), rebuilding...");
                report.BuildResult = await BuildProjectAsync(
                    workDir, config, cancellationToken, phase: "post-recovery-build",
                    candidateFiles: report.FileResults.Where(file => file.Status == FileStatus.Replaced).Select(file => file.FilePath).ToList());
            }
        }

        Console.WriteLine($"  Build: {(report.BuildResult.Succeeded ? "Success" : "FAILED")}");

        // A failed build that recovery could NOT attribute to any file (zero extractable
        // error files — the recovery-build-timeout signature, but any such case) reverts
        // nothing, so the coverage fraction would be spuriously inflated. Flag the run
        // inconclusive so no fidelity number is trusted or emitted for it.
        if (!report.BuildResult.Succeeded && report.BuildResult.Errors.Count == 0)
        {
            report.Inconclusive = true;
            report.InconclusiveReason = report.BuildResult.ExitCode == -1
                ? "recovery build did not complete within the build timeout — file reverts could not be attributed, so coverage is unreliable"
                : "post-conversion build failed with no extractable error files — file reverts could not be attributed, so coverage is unreliable";
            Console.WriteLine($"  INCONCLUSIVE: {report.InconclusiveReason}");
        }

        // Step 5: Test (only if build succeeded)
        if (report.BuildResult.Succeeded)
        {
            Console.WriteLine("\nPhase 5/5: Running round-trip tests...");
            TrxParser.CleanTrxFiles(workDir);
            report.RoundTripTests = await RunTestAttemptsAsync(
                workDir, config, "candidate", false, cancellationToken);
            Console.WriteLine($"  Round-trip: {report.RoundTripTests.Passed}/{report.RoundTripTests.TotalTests} passed, {report.RoundTripTests.Failed} failed");
        }
        else
        {
            Console.WriteLine("\nPhase 5/5: Skipped (build failed)");
        }

        // Compare
        report.Comparison = CompareTestResults(config, report.Baseline, report.RoundTripTests, report.BuildResult);
        foreach (var attempt in report.Evidence!.TestAttempts.Where(attempt => attempt.Leg == "candidate"))
        {
            var baseline = report.Evidence.TestAttempts
                .SingleOrDefault(item => item.Leg == "baseline" && item.Attempt == attempt.Attempt);
            attempt.Comparison = CompareTestResults(
                config with { ExpectedFlakyTestFullyQualifiedNames = [] },
                baseline?.Result, attempt.Result, report.BuildResult);
            if (attempt.Comparison.Status != ComparisonStatus.Pass
                || attempt.Result.ExitCode != 0 || baseline?.Result.ExitCode != 0)
                report.Evidence.Failures.Add($"Subject-test attempt {attempt.Attempt} is not clean on both legs; "
                    + "legacy flake allowances do not waive evidence acceptance.");
        }
        if (report.Evidence.TestAttempts.Count(attempt => attempt.Leg == "candidate") != config.TestAttemptsPerLeg)
            report.Evidence.Failures.Add("Not all declared candidate subject-test attempts were reached.");

        // Fidelity: separated verdict dimensions (coverage / build / tests)
        report.Fidelity = ProjectFidelity.Compute(report);
        if (report.Inconclusive)
        {
            // Do NOT print a coverage fraction for an unattributable build failure.
            Console.WriteLine($"\nFidelity: INCONCLUSIVE — {report.InconclusiveReason}. No coverage fraction emitted.");
        }
        else
        {
            var cov = report.Fidelity.Coverage;
            Console.WriteLine(
                $"\nFidelity: coverage {cov.CoverageFraction:P1} " +
                $"({cov.ConvertedNative} native + {cov.ConvertedWithLosses} with-losses of {cov.TotalConvertibleFiles}; " +
                $"{cov.Reverted} reverted, {cov.FailedConversion} failed)");
        }

        // Bisect regressions if enabled and there are few enough
        if (config.EnableBisect
            && report.Comparison.Regressions.Count > 0
            && report.Comparison.Regressions.Count <= config.BisectMaxRegressions)
        {
            Console.WriteLine($"\nBisecting {report.Comparison.Regressions.Count} regressions...");
            report.BisectResults = await BisectRegressionsAsync(
                workDir,
                config,
                report.Comparison.Regressions,
                report.FileResults,
                cancellationToken);
        }

    }

    private async Task<TestRunResult> RunTestAttemptsAsync(
        string workDir, RoundTripConfig config, string leg, bool noBuild, CancellationToken cancellationToken)
    {
        TestRunResult? first = null;
        for (var attempt = 1; attempt <= config.TestAttemptsPerLeg; attempt++)
        {
            TrxParser.CleanTrxFiles(workDir);
            var result = await RunTestsAsync(workDir, config, noBuild, cancellationToken);
            _evidence?.TestAttempts.Add(new TestAttemptEvidence { Leg = leg, Attempt = attempt, Result = result });
            first ??= result;
        }
        return first!;
    }

    private static void CaptureInventory(string root, RoundTripConfig config, RoundTripReport report)
    {
        report.Evidence ??= new RunEvidence { DeclaredTestAttemptsPerLeg = config.TestAttemptsPerLeg };
        if (report.Evidence.InventoryComplete)
            return;
        var library = Path.Combine(root, config.LibrarySourceRelativePath);
        if (!Directory.Exists(library))
            throw new DirectoryNotFoundException($"Library source directory not found: {library}");
        report.Evidence.Provenance.Compiler ??= ReportGenerator.Fingerprint(
            typeof(Compiler.Program).Assembly.Location, typeof(Compiler.Program).Assembly);
        report.Evidence.Provenance.Harness ??= ReportGenerator.Fingerprint(
            typeof(RoundTripPipeline).Assembly.Location, typeof(RoundTripPipeline).Assembly);
        report.Evidence.Provenance.HarnessOptions = ReportGenerator.SnapshotOptions(config);
        foreach (var path in Directory.EnumerateFiles(library, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path)).OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = ReportGenerator.RelativePath(root, path);
            var fileId = $"{config.ProjectName}/{relative}";
            string? hash = null;
            string? readError = null;
            try { hash = ReportGenerator.HashFile(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { readError = ex.ToString(); }
            var excluded = ShouldExclude(path, config.ExcludePatterns);
            report.Evidence.Inputs.Add(new InputEvidence
            {
                FileId = fileId, Path = relative, Sha256 = hash, Excluded = excluded, ReadError = readError,
            });
            if (!excluded)
                report.FileResults.Add(new FileConversionResult
                {
                    FilePath = relative,
                    Status = FileStatus.NotAttempted,
                    Candidate = new CandidateEvidence { FileId = fileId, InputSha256 = hash },
                });
        }
        report.ExcludedFileCount = report.Evidence.Inputs.Count(input => input.Excluded);
        report.Evidence.InventoryComplete = true;
    }

    internal static async Task CaptureProvenanceAsync(
        RoundTripConfig config, RunEvidence evidence, CancellationToken cancellationToken)
    {
        var compiler = typeof(Compiler.Program).Assembly;
        var harness = typeof(RoundTripPipeline).Assembly;
        var roslyn = typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly;
        var provenance = evidence.Provenance;
        provenance.Compiler = ReportGenerator.Fingerprint(compiler.Location, compiler);
        provenance.Harness = ReportGenerator.Fingerprint(harness.Location, harness);
        provenance.Roslyn = ReportGenerator.Fingerprint(roslyn.Location, roslyn);
        provenance.Host = $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; "
            + $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}; "
            + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        provenance.HarnessOptions = ReportGenerator.SnapshotOptions(config);
        foreach (var key in new[] { "CALOR_NO_TYPE_CHECK", "DOTNET_ROLL_FORWARD", "DOTNET_ROOT",
            "NuGetAudit", "RestoreSources", "TMPDIR", "MSBuildSDKsPath" })
            provenance.Environment[key] = Environment.GetEnvironmentVariable(key);
        provenance.GeneratedValidationReferencePool = GeneratedCSharpCompiler.References
            .OfType<Microsoft.CodeAnalysis.PortableExecutableReference>()
            .Where(reference => reference.FilePath is not null)
            .OrderBy(reference => reference.FilePath, StringComparer.Ordinal)
            .Select(reference => ReportGenerator.Fingerprint(reference.FilePath!)).ToList();
        provenance.RepositoryRevision = await Git("rev-parse HEAD", AppContext.BaseDirectory);
        var repositoryRoot = await Git("rev-parse --show-toplevel", AppContext.BaseDirectory);
        provenance.RepositoryDiffSha256 = repositoryRoot is null ? null : await GitHash(repositoryRoot);
        provenance.CorpusRevision = await Git("rev-parse HEAD", config.OriginalProjectPath);
        provenance.CorpusDiffSha256 = await GitHash(config.OriginalProjectPath);
        var info = await ProcessRunner.RunAsync(config.DotnetPath, "--info",
            AppContext.BaseDirectory, TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);
        provenance.DotnetInfo = info.Stdout + info.Stderr;
        if (info.ExitCode != 0)
            evidence.Failures.Add($"dotnet --info failed with exit {info.ExitCode}.");
        evidence.Limitations.Add("Program.Compile diagnostics expose Binder provenance only when BindingContext "
            + "is present. Other producer passes are unavailable, not inferred from shared codes.");
        evidence.Limitations.Add("Semantic resolution is unassessed; evaluated project context or absence of "
            + "diagnostics does not prove every reference resolved or an accepted file is null-safe.");
        evidence.Limitations.Add("GeneratedValidationReferencePool is the actual public reference pool, not a claim "
            + "that every member was selected by the private lazy MetadataBinder. Project validation references "
            + "are separately captured from evaluated compiler inputs.");
        evidence.Limitations.Add("Calor0272/0273/0274 retain AnalysisOnly routing. Independent binding observations "
            + "are shadow evidence, not a production nullability rejection or Stage A/B acceptance.");
        evidence.Limitations.Add("Working-copy preparation omits .git, global.json, bin and obj. Compiler options "
            + "are harness options (effects/contract enforcement off, generated validation deferred), not CLI defaults.");

        async Task<string?> Git(string arguments, string directory)
        {
            var result = await ProcessRunner.RunAsync("git", arguments, directory,
                TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);
            if (result.ExitCode == 0) return result.Stdout.Trim();
            evidence.Failures.Add($"Provenance git {arguments} unavailable for {directory}: {result.Stderr}");
            return null;
        }
        async Task<string?> GitHash(string directory)
        {
            var diff = await Git("diff --no-ext-diff --binary HEAD -- .", directory);
            return diff is null ? null : ReportGenerator.Hash(diff);
        }
    }

    internal string PrepareWorkingCopy(
        RoundTripConfig config,
        CancellationToken cancellationToken = default)
    {
        var workDir = config.WorkingDirectory
            ?? Path.Combine(Path.GetTempPath(), "calor-roundtrip", config.ProjectName, Guid.NewGuid().ToString("N")[..8]);

        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        CopyDirectory(config.OriginalProjectPath, workDir, cancellationToken);
        var sharedPackageProps = Path.Combine(
            Path.GetDirectoryName(config.OriginalProjectPath)!,
            "Directory.Packages.props");
        var localPackageProps = Path.Combine(
            config.OriginalProjectPath,
            "Directory.Packages.props");
        if (!File.Exists(localPackageProps)
            && File.Exists(sharedPackageProps))
        {
            File.Copy(
                sharedPackageProps,
                Path.Combine(workDir, "Directory.Packages.props"),
                overwrite: true);
        }
        return workDir;
    }

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            // Skip the submodule gitlink: for a git submodule, `.git` is a FILE
            // ("gitdir: …"), not a directory, so the directory-exclusion below misses
            // it. Copying it leaves a stray/invalid gitlink in the working copy that
            // makes MinVer/SourceLink git tasks resolve the wrong repo and perturbs the
            // first-pass restore of transitive project references. Drop it.
            if (string.Equals(fileName, ".git", StringComparison.Ordinal))
                continue;
            // Neutralize corpus SDK pins: a vendored subject may pin its own SDK via
            // global.json (e.g. FluentValidation pins 9.0.0). We build every subject on
            // Calor's pinned .NET 10 SDK (D-W4.2), so the working copy drops global.json
            // and lets the ambient SDK resolve. The vendored source stays verbatim; only
            // the throwaway working copy is affected.
            if (string.Equals(fileName, "global.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var destFile = Path.Combine(destination, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dirName = Path.GetFileName(dir);
            // Skip .git, bin, obj to speed up copy
            if (dirName is ".git" or "bin" or "obj" or ".vs" or ".idea")
                continue;
            CopyDirectory(dir, Path.Combine(destination, dirName), cancellationToken);
        }
    }

    internal async Task<TestRunResult> RunTestsAsync(
        string workDir,
        RoundTripConfig config,
        bool noBuild = false,
        CancellationToken cancellationToken = default)
    {
        // Pass the project RELATIVE to workDir (which is the process working directory),
        // never an absolute path. On macOS the temp root is /var/folders/… — a symlink
        // to /private/var/… — and passing the absolute /var path while MSBuild
        // canonicalizes the working directory to /private/var creates a path-identity
        // mismatch that silently breaks Directory.Build.props and ProjectReference
        // resolution (spurious CS0246 on the subject's own/transitive types). A relative
        // path keeps every path in one canonical form.
        var target = config.SolutionOrProjectFile;

        var args = $"test \"{target}\" --logger \"trx;LogFilePrefix=roundtrip\" --logger \"console;verbosity=normal\"";
        args += $" --configuration {config.Configuration}";
        if (config.TargetFramework != null)
            args += $" --framework {config.TargetFramework}";
        if (noBuild)
            args += " --no-build";
        if (config.TestFilter != null)
            args += $" --filter \"{config.TestFilter}\"";
        if (!string.IsNullOrWhiteSpace(config.ExtraBuildProperties))
            args += $" {config.ExtraBuildProperties}";

        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
            config.DotnetPath,
            args,
            workDir,
            config.TestTimeout,
            cancellationToken: cancellationToken);

        // Parse and aggregate ALL TRX files (one per test assembly)
        var (testResults, trxFiles, parseErrors) = TrxParser.ParseAll(workDir);
        if (parseErrors.Count > 0)
        {
            return new TestRunResult
            {
                ExitCode = exitCode == 0 ? -2 : exitCode,
                Command = $"{config.DotnetPath} {args}",
                WorkingDirectory = workDir,
                Results = testResults,
                TrxFiles = trxFiles.Select(f => Path.GetRelativePath(workDir, f)).ToList(),
                ParseErrors = parseErrors,
                Stdout = stdout,
                Stderr = stderr,
            };
        }

        // Fallback: if TRX parsing found no results, parse console output
        if (testResults.Count == 0)
        {
            return ParseConsoleTestOutput(exitCode, stdout, stderr, $"{config.DotnetPath} {args}", workDir);
        }

        return new TestRunResult
        {
            ExitCode = exitCode,
            Command = $"{config.DotnetPath} {args}",
            WorkingDirectory = workDir,
            TotalTests = testResults.Count,
            Passed = testResults.Count(t => t.Outcome == "Passed"),
            Failed = testResults.Count(t => t.Outcome == "Failed"),
            Skipped = testResults.Count(t => t.Outcome is "NotExecuted" or "Skipped"),
            Results = testResults,
            TrxFiles = trxFiles.Select(f => Path.GetRelativePath(workDir, f)).ToList(),
            Stdout = stdout,
            Stderr = stderr,
        };
    }

    private static TestRunResult ParseConsoleTestOutput(int exitCode, string stdout, string stderr,
        string command, string workDir)
    {
        // Parse "Total tests: N" and "Passed: N" etc. from console output
        var combined = stdout + "\n" + stderr;
        var total = ParseIntFromOutput(combined, "Total tests:");
        var passed = ParseIntFromOutput(combined, "Passed:");
        var failed = ParseIntFromOutput(combined, "Failed:");
        var skipped = ParseIntFromOutput(combined, "Skipped:");

        return new TestRunResult
        {
            ExitCode = exitCode,
            Command = command,
            WorkingDirectory = workDir,
            TotalTests = total,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            Results = [],
            UsedConsoleFallback = true,
            Stdout = stdout,
            Stderr = stderr,
        };
    }

    private static int ParseIntFromOutput(string output, string label)
    {
        var idx = output.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        var rest = output[(idx + label.Length)..].TrimStart();
        var numStr = new string(rest.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(numStr, out var val) ? val : 0;
    }

    internal async Task<List<FileConversionResult>> ConvertAndReplaceAsync(
        string workDir,
        RoundTripConfig config,
        RoundTripReport report,
        CancellationToken cancellationToken = default)
    {
        CaptureInventory(workDir, config, report);
        _evidence = report.Evidence;
        var results = new ConcurrentBag<FileConversionResult>();
        var inventoryResults = report.FileResults.ToDictionary(file => file.FilePath, StringComparer.Ordinal);
        var libDir = Path.Combine(workDir, config.LibrarySourceRelativePath);

        if (!Directory.Exists(libDir))
        {
            Console.Error.WriteLine($"  ERROR: Library source directory not found: {libDir}");
            return [];
        }

        var allCsFiles = Directory.GetFiles(libDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path))
            .OrderBy(f => f)
            .ToList();
        var csFiles = allCsFiles
            .Where(f => !ShouldExclude(f, config.ExcludePatterns))
            .ToList();
        report.ExcludedFileCount = allCsFiles.Count - csFiles.Count;

        Console.WriteLine($"  Found {csFiles.Count} C# files to convert ({report.ExcludedFileCount} excluded by pattern)");
        var parseResolutions = await ProjectParseContextResolver.ResolveAsync(
            workDir,
            config,
            csFiles,
            cancellationToken);
        var parseContexts = parseResolutions
            .Where(pair => pair.Value
                is ResolvedProjectFileParseContext
                    or AmbiguousProjectFileParseContext)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value switch
                {
                    ResolvedProjectFileParseContext resolved =>
                        resolved.Context,
                    AmbiguousProjectFileParseContext ambiguous =>
                        ambiguous.Contexts[0],
                    _ => throw new InvalidOperationException()
                },
                ProjectParseContextResolver.PathComparer);
        report.EvaluatedParseContexts = parseContexts;
        report.EvaluatedParseResolutions = parseResolutions;

        var completedCount = 0;
        await Parallel.ForEachAsync(
            csFiles,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Min(2, Math.Max(1, Environment.ProcessorCount)),
            },
            async (csFile, token) =>
        {
            var relativePath = ReportGenerator.RelativePath(workDir, csFile);
            var result = inventoryResults[relativePath];
            var candidate = result.Candidate!;
            candidate.Attempted = true;
            config.FileConversionStarted?.Invoke(relativePath);
            void Complete()
            {
                candidate.StatusBeforeRecovery = result.Status;
                candidate.Errors = result.Errors.ToList();
                candidate.CandidateId = ReportGenerator.Hash(System.Text.Json.JsonSerializer.Serialize(new
                {
                    candidate.FileId, candidate.InputSha256, candidate.ConvertedCalorSha256,
                    candidate.CompilerOptions, candidate.ConversionOptions,
                    Compiler = report.Evidence?.Provenance.Compiler?.Sha256,
                    Contexts = result.ValidatedContexts.Select(context => new
                    {
                        context.Configuration, context.Platform, context.TargetFramework,
                        context.CompilationProperties, context.DefinedSymbols,
                        References = context.References.Select(reference => new
                        {
                            reference.Identity, reference.Sha256, reference.Aliases,
                        }),
                    }),
                }));
                results.Add(result);
                var completed = Interlocked.Increment(
                    ref completedCount);
                if (completed % 10 == 0
                    || completed == csFiles.Count)
                    Console.Write($" {completed}/{csFiles.Count}");
            }

            try
            {
                var originalSource = await File.ReadAllTextAsync(csFile, token);
                var canonicalFile =
                    ProjectParseContextResolver.Canonicalize(csFile);
                parseResolutions.TryGetValue(
                    canonicalFile,
                    out var parseResolution);
                IReadOnlyList<ProjectFileParseContext> observedContexts;
                switch (parseResolution)
                {
                    case ResolvedProjectFileParseContext resolved:
                        observedContexts = [resolved.Context];
                        candidate.ContextResolution = "Resolved";
                        break;
                    case AmbiguousProjectFileParseContext ambiguous:
                        observedContexts = ambiguous.Contexts;
                        candidate.ContextResolution = "MultipleObserved";
                        break;
                    case MissingProjectFileParseContext missing
                        when !config.LooseDirectoryMode:
                        result.Status = FileStatus.ConversionFailed;
                        candidate.ContextResolution = "Missing";
                        result.Errors = [missing.Diagnostic];
                        Complete();
                        return;
                    default:
                        observedContexts = [];
                        candidate.ContextResolution = "LooseDirectory";
                        break;
                }
                var plan = CreateMultiContextConversionPlan(
                    originalSource,
                    csFile,
                    observedContexts,
                    token);
                var parseContext = plan.Representative;
                result.ObservedContexts = plan.Contexts;
                result.ValidatedContexts = plan.Contexts
                    .Select(CreateFileContextDetail)
                    .ToList();
                result.ContextSelectionMode = plan.SelectionMode;
                if (plan.Errors.Count > 0)
                {
                    result.Status = FileStatus.ConversionFailed;
                    result.Errors = plan.Errors.ToList();
                    Complete();
                    return;
                }
                var conversionOptions = CreateHarnessConversionOptions(
                        config,
                        parseContext,
                        plan.PreprocessorMode);
                candidate.ConversionOptions = ReportGenerator.SnapshotOptions(conversionOptions);
                var converter = new CSharpToCalorConverter(conversionOptions);

                // Step 3a: Convert C# → Calor
                using var conversionCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(token);
                conversionCancellation.CancelAfter(config.ConversionTimeout);
                var conversionResult = await Task.Run(
                    () => converter.Convert(
                        originalSource,
                        csFile,
                        conversionCancellation.Token),
                    CancellationToken.None);
                token.ThrowIfCancellationRequested();

                result.ConversionSuccess = conversionResult.Success;
                result.ConversionRate = conversionResult.Context.Stats.ConversionRate;
                result.PreprocessorMode =
                    conversionResult.Metadata.PreprocessorMode.ToString();
                result.Configuration =
                    conversionResult.Metadata.Configuration;
                result.TargetFramework =
                    conversionResult.Metadata.TargetFramework;
                result.LanguageVersion =
                    conversionResult.Metadata.LanguageVersion;
                result.DefinedSymbols =
                    conversionResult.Metadata.DefinedSymbols.ToList();
                candidate.ConvertedCalor = conversionResult.CalorSource;
                candidate.ConvertedCalorSha256 = conversionResult.CalorSource is null
                    ? null : ReportGenerator.Hash(conversionResult.CalorSource);
                candidate.Diagnostics.AddRange(conversionResult.Issues.Select(issue =>
                    ReportGenerator.Identify(new DiagnosticEvidence
                    {
                        Severity = issue.Severity.ToString(),
                        Message = issue.Message,
                        Phase = "CSharpToCalorConverter.Convert",
                        Producer = null,
                        SourceKind = "OriginalCSharp",
                        Path = relativePath,
                        SourceSha256 = candidate.InputSha256,
                        Line = issue.Line,
                        Column = issue.Column,
                    })));

                // Consume the Slice-3 conversion loss ledger (#770): populate
                // Gaps / InteropBlocks / loss counts from real conversion data.
                result.ApplyLossLedger(conversionResult.Context.Losses);

                if (conversionResult.Success && conversionResult.CalorSource != null)
                {
                    // Step 3b: Compile Calor → C# with permissive options
                    var compileOptions = new Compiler.CompilationOptions
                    {
                        EnforceEffects = false,
                        ContractMode = Compiler.ContractMode.Off,
                        DeferGeneratedOutputValidation = true,
                        CancellationToken = token,
                    };
                    candidate.CompilerOptions = ReportGenerator.SnapshotOptions(compileOptions);
                    var compileResult = Compiler.Program.Compile(
                        conversionResult.CalorSource, csFile, compileOptions);
                    candidate.CompilationOutcome = compileResult.HasErrors ? "Rejected" : "Accepted";
                    candidate.Diagnostics.AddRange(compileResult.Diagnostics.Select(diagnostic =>
                        ReportGenerator.CaptureDiagnostic(diagnostic, "Program.Compile", relativePath,
                            conversionResult.CalorSource)));
                    if (config.CaptureBindingAnalysis)
                        CaptureBindingAnalysis(candidate, conversionResult.CalorSource, csFile, relativePath);
                    token.ThrowIfCancellationRequested();

                    if (!compileResult.HasErrors && !string.IsNullOrWhiteSpace(compileResult.GeneratedCode))
                    {
                        // Step 3c: Post-process emitted C# for round-trip compatibility
                        var emitted = PostProcessEmittedCSharp(compileResult.GeneratedCode, originalSource);
                        candidate.EmittedCSharpSha256 = ReportGenerator.Hash(emitted);

                        // Step 3d: Syntax-check now. All emitted files are compiled
                        // together with project sources/references below before any
                        // project file is replaced.
                        var syntaxErrors = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
                            .ParseText(emitted, path: csFile)
                            .GetDiagnostics()
                            .Where(diagnostic =>
                                diagnostic.Severity ==
                                Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                            .ToList();
                        if (syntaxErrors.Count > 0)
                        {
                            result.Status = FileStatus.EmitSyntaxError;
                            result.Errors = syntaxErrors
                                .Select(FormatDiagnostic)
                                .ToList();
                            candidate.Diagnostics.AddRange(syntaxErrors.Select(diagnostic =>
                                ReportGenerator.CaptureRoslynDiagnostic(diagnostic, "emitted-syntax", workDir)));
                        }
                        else
                        {
                            result.Status = FileStatus.Replaced;
                            result.EmittedCSharp = emitted;
                        }
                    }
                    else
                    {
                        result.Status = FileStatus.CompileError;
                        result.Errors = compileResult.Diagnostics.Errors
                            .Select(d => d.Message).ToList();
                    }
                }
                else
                {
                    result.Status = FileStatus.ConversionFailed;
                    result.Errors = conversionResult.Issues
                        .Where(i => i.Severity == ConversionIssueSeverity.Error)
                        .Select(i => i.Message).ToList();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                result.Status = FileStatus.ConversionTimedOut;
                result.Errors =
                [
                    $"Conversion exceeded {config.ConversionTimeout.TotalSeconds:F0}s timeout"
                ];
            }
            catch (Exception ex)
            {
                result.Status = FileStatus.Crashed;
                candidate.CompilationOutcome = candidate.CompilationOutcome == "NotReached"
                    ? "CrashedBeforeCompilation" : candidate.CompilationOutcome;
                result.Errors = [ex.Message];
            }

            Complete();
        });
        Console.WriteLine();

        var orderedResults = results
            .OrderBy(result => result.FilePath, StringComparer.Ordinal)
            .ToList();
        if (File.Exists(Path.Combine(workDir, config.SolutionOrProjectFile)))
        {
            await ValidateAndPublishProjectCandidatesAsync(
                workDir,
                config,
                orderedResults,
                cancellationToken);
        }
        else
        {
            await ValidateAndPublishGeneratedFilesAsync(
                workDir,
                orderedResults,
                allCsFiles,
                cancellationToken);
        }

        foreach (var result in orderedResults)
        {
            result.Candidate!.StatusBeforeRecovery = result.Status;
            result.Candidate.Errors = result.Errors.ToList();
        }
        // Print status summary
        foreach (var group in orderedResults
            .GroupBy(r => r.Status)
            .OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        return orderedResults;
    }

    private static void CaptureBindingAnalysis(CandidateEvidence candidate, string source, string sourcePath, string relativePath)
    {
        try
        {
            var diagnostics = new Compiler.Diagnostics.DiagnosticBag();
            diagnostics.SetFilePath(sourcePath);
            var tokens = new Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser();
            var module = new Compiler.Parsing.Parser(tokens, diagnostics).Parse();
            candidate.AnalysisDiagnostics.AddRange(diagnostics.Select(diagnostic =>
                ReportGenerator.CaptureDiagnostic(diagnostic, "shadow-reparse", relativePath, source)));
            if (diagnostics.HasErrors)
            {
                candidate.AnalysisOutcome = "NotBound: reparse failed";
                return;
            }
            var bindingDiagnostics = new Compiler.Diagnostics.DiagnosticBag();
            bindingDiagnostics.SetFilePath(sourcePath);
            new Compiler.Binding.Binder(bindingDiagnostics, sourcePath).Bind(module);
            candidate.AnalysisDiagnostics.AddRange(bindingDiagnostics.Select(diagnostic =>
                ReportGenerator.CaptureDiagnostic(diagnostic, "shadow-bind", relativePath, source, independentBinding: true)));
            candidate.AnalysisOutcome = "Observed independently; production reachability not asserted";
        }
        catch (Exception ex)
        {
            candidate.AnalysisOutcome = $"Crashed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    internal async Task ValidateAndPublishProjectCandidatesAsync(
        string workDir,
        RoundTripConfig config,
        List<FileConversionResult> results,
        CancellationToken cancellationToken)
    {
        var candidates = results
            .Where(result =>
                result.Status == FileStatus.Replaced &&
                result.EmittedCSharp != null)
            .ToList();
        if (candidates.Count == 0)
            return;

        var validationDir = Path.Combine(
            Path.GetTempPath(),
            "calor-roundtrip-validation",
            config.ProjectName,
            Guid.NewGuid().ToString("N")[..8]);
        try
        {
            CopyDirectory(workDir, validationDir, cancellationToken);
            var originalBuild = await BuildAllObservedContextsAsync(
                validationDir,
                workDir,
                config,
                candidates,
                cancellationToken, phase: "project-original-validation");
            if (!originalBuild.Succeeded)
            {
                throw new InvalidOperationException(
                    "Original project sources fail validation in at least one "
                    + "observed build context; refusing to attribute the "
                    + "failure to converted output."
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        originalBuild.Errors.Take(10)));
            }
            DeleteBuildOutputs(
                validationDir,
                cancellationToken);
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(validationDir, candidate.FilePath);
                await File.WriteAllTextAsync(
                    path,
                    candidate.EmittedCSharp!,
                    cancellationToken);
            }

            var unattributedCleanRetries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var active = candidates
                    .Where(result => result.Status == FileStatus.Replaced)
                    .ToList();
                var build = await BuildAllObservedContextsAsync(
                    validationDir,
                    workDir,
                    config,
                    active,
                    cancellationToken);
                if (build.Succeeded)
                {
                    foreach (var candidate in active)
                    {
                        await File.WriteAllTextAsync(
                            Path.Combine(workDir, candidate.FilePath),
                            candidate.EmittedCSharp!,
                            cancellationToken);
                    }
                    return;
                }

                var directlyFailed = active
                    .Where(candidate => build.Errors.Any(error =>
                        BuildErrorReferencesFile(
                            validationDir,
                            candidate.FilePath,
                            error) ||
                        BuildErrorReferencesFile(
                            workDir,
                            candidate.FilePath,
                            error)))
                    .ToList();
                var failed = directlyFailed.Count > 0
                    ? directlyFailed
                    : active
                        .Where(candidate => build.Errors.Any(error =>
                            CandidateIsReferenced(candidate, error)))
                        .ToList();
                if (failed.Count == 0)
                {
                    if (unattributedCleanRetries < 1)
                    {
                        Console.WriteLine(
                            "  Project validation produced unattributed errors; retrying from clean build outputs");
                        DeleteBuildOutputs(
                            validationDir,
                            cancellationToken);
                        unattributedCleanRetries++;
                        await Task.Delay(
                            TimeSpan.FromSeconds(1),
                            cancellationToken);
                        continue;
                    }
                    failed = await IsolateUnattributedProjectFailuresAsync(
                        validationDir,
                        workDir,
                        config,
                        active,
                        cancellationToken);
                    if (failed.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Project validation fails with every candidate "
                            + "restored to original source; refusing to "
                            + "attribute the baseline failure to conversions."
                            + Environment.NewLine
                            + string.Join(
                                Environment.NewLine,
                                build.Errors.Take(10)));
                    }
                    else
                    {
                        Console.WriteLine(
                            $"  Project validation isolated {failed.Count} unattributed candidate(s): "
                            + string.Join(
                                ", ",
                                failed.Select(candidate =>
                                    candidate.FilePath)));
                    }
                }
                else
                {
                    Console.WriteLine(
                        $"  Project validation rejected {failed.Count} candidate(s): " +
                        string.Join(", ", failed.Select(candidate => candidate.FilePath)));
                }

                foreach (var candidate in failed)
                {
                    var originalPath = Path.Combine(
                        config.OriginalProjectPath,
                        candidate.FilePath);
                    var validationPath = Path.Combine(
                        validationDir,
                        candidate.FilePath);
                    if (File.Exists(originalPath))
                    {
                        var original = await File.ReadAllTextAsync(
                            originalPath,
                            cancellationToken);
                        await File.WriteAllTextAsync(
                            validationPath,
                            original,
                            cancellationToken);
                    }
                    candidate.Status = FileStatus.EmitCompilationError;
                    var attributedErrors = build.Errors
                        .Where(error =>
                            BuildErrorReferencesFile(
                                validationDir,
                                candidate.FilePath,
                                error) ||
                            BuildErrorReferencesFile(
                                workDir,
                                candidate.FilePath,
                                error) ||
                            CandidateIsReferenced(candidate, error))
                        .ToList();
                    candidate.Errors = (attributedErrors.Count > 0 ? attributedErrors : build.Errors).Take(10).ToList();
                    candidate.Candidate?.Diagnostics.AddRange(ReportGenerator.CaptureBuildDiagnostics(
                        attributedErrors.Count > 0 ? attributedErrors : build.Errors,
                        build.Phase, validationDir, workDir,
                        attributedErrors.Count > 0 ? "file-or-referenced-symbol" : "isolated-subset"));
                }

                if (failed.Count == active.Count)
                    return;
                DeleteBuildOutputs(
                    validationDir,
                    cancellationToken);
                unattributedCleanRetries = 0;
            }
        }
        finally
        {
            if (Directory.Exists(validationDir))
                Directory.Delete(validationDir, recursive: true);
        }
    }

    private async Task<BuildResult> BuildAllObservedContextsAsync(
        string validationDir,
        string sourceWorkDir,
        RoundTripConfig config,
        IReadOnlyCollection<FileConversionResult> candidates,
        CancellationToken cancellationToken,
        bool includeRootBuild = true,
        string phase = "project-candidate-validation")
    {
        if (includeRootBuild)
        {
            var rootBuild = await BuildProjectAsync(
                validationDir,
                config,
                cancellationToken,
                noIncremental: true,
                disableBuildServers: true,
                phase: phase,
                reportedSourceRoot: sourceWorkDir,
                candidateFiles: phase == "project-original-validation"
                    ? [] : candidates.Select(candidate => candidate.FilePath).ToList());
            if (!rootBuild.Succeeded)
                return rootBuild;
        }

        var contexts = candidates
            .SelectMany(candidate => candidate.ObservedContexts)
            .SelectMany(context => context.BuildStates)
            .GroupBy(
                state => state.ProjectFile
                    + "|"
                    + string.Concat(state.GlobalProperties
                        .OrderBy(
                            property => property.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(property =>
                            $"{property.Key.Length}:{property.Key}"
                            + $"{property.Value.Length}:{property.Value}")),
                StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        foreach (var context in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeProject = Path.GetRelativePath(
                sourceWorkDir,
                context.ProjectFile);
            if (Path.IsPathRooted(relativeProject)
                || relativeProject == ".."
                || relativeProject.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                return new BuildResult
                {
                    Succeeded = false,
                    ExitCode = -1,
                    Errors =
                    [
                        $"Observed context project '{context.ProjectFile}' "
                        + "is outside the validation working copy."
                    ]
                };
            }
            var arguments =
                $"build \"{relativeProject}\" --no-incremental -m:1 "
                + "-p:BuildInParallel=false "
                + ProjectParseContextResolver
                    .BuildMsBuildPropertyArguments(
                        context.GlobalProperties)
                + "--disable-build-servers --verbosity quiet";
            var (exitCode, stdout, stderr) =
                await ProcessRunner.RunAsync(
                    config.DotnetPath,
                    arguments,
                    validationDir,
                    config.BuildTimeout,
                    cancellationToken: cancellationToken);
            var errors = (stdout + "\n" + stderr)
                .Split('\n')
                .Where(line => line.Contains(": error "))
                .Select(line => line.Trim())
                .ToList();
            var result = new BuildResult
            {
                Succeeded = exitCode == 0,
                ExitCode = exitCode,
                Phase = phase,
                Command = $"{config.DotnetPath} {arguments}",
                WorkingDirectory = validationDir,
                Stdout = stdout,
                Stderr = stderr,
                Diagnostics = ReportGenerator.CaptureBuildDiagnostics(errors, phase, validationDir, sourceWorkDir),
                CandidateFiles = phase == "project-original-validation"
                    ? [] : candidates.Select(candidate => candidate.FilePath).ToList(),
                Errors = errors.Count > 0 || exitCode == 0
                    ? errors
                    :
                    [
                        $"Observed context build failed for "
                        + $"'{context.ProjectFile}'.",
                        stderr.Trim()
                    ]
            };
            _evidence?.BuildAttempts.Add(result);
            if (!result.Succeeded)
                return result;
        }
        return new BuildResult
        {
            Succeeded = true,
            ExitCode = 0,
            Errors = []
        };
    }

    private static void DeleteBuildOutputs(
        string root,
        CancellationToken cancellationToken)
    {
        var directories = Directory.EnumerateDirectories(
                root,
                "*",
                SearchOption.AllDirectories)
            .Where(directory =>
                Path.GetFileName(directory) is "bin" or "obj")
            .OrderByDescending(
                directory => directory.Length)
            .ToList();
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<List<FileConversionResult>>
        IsolateUnattributedProjectFailuresAsync(
            string validationDir,
            string sourceWorkDir,
            RoundTripConfig config,
            IReadOnlyList<FileConversionResult> active,
            CancellationToken cancellationToken)
    {
        var suspects = active.ToList();
        var granularity = 2;
        var buildCache = new Dictionary<string, bool>(
            StringComparer.Ordinal);
        if (!await GeneratedSubsetBuildsAsync(
                Array.Empty<FileConversionResult>()))
        {
            return [];
        }
        while (suspects.Count >= 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partitions = Partition(
                suspects,
                granularity);
            var reduced = false;
            foreach (var partition in partitions)
            {
                if (!await GeneratedSubsetBuildsAsync(partition))
                {
                    suspects = partition;
                    granularity = 2;
                    reduced = true;
                    break;
                }
            }
            if (reduced)
                continue;
            foreach (var partition in partitions)
            {
                var complement = suspects
                    .Except(partition)
                    .ToList();
                if (complement.Count > 0
                    && !await GeneratedSubsetBuildsAsync(complement))
                {
                    suspects = complement;
                    granularity = Math.Max(2, granularity - 1);
                    reduced = true;
                    break;
                }
            }
            if (reduced)
                continue;
            if (granularity >= suspects.Count)
                break;
            granularity = Math.Min(
                suspects.Count,
                granularity * 2);
        }

        for (var index = 0;
             index < suspects.Count && suspects.Count > 1;)
        {
            var trial = suspects
                .Where((_, candidateIndex) =>
                    candidateIndex != index)
                .ToList();
            if (!await GeneratedSubsetBuildsAsync(trial))
            {
                suspects = trial;
                index = 0;
            }
            else
            {
                index++;
            }
        }
        await ApplyGeneratedSubsetAsync(
            active.Except(suspects).ToHashSet());
        return suspects;

        async Task<bool> GeneratedSubsetBuildsAsync(
            IReadOnlyCollection<FileConversionResult> subset)
        {
            var cacheKey = string.Join(
                "\0",
                subset.Select(candidate => candidate.FilePath)
                    .OrderBy(path => path, StringComparer.Ordinal));
            if (buildCache.TryGetValue(cacheKey, out var cached))
                return cached;
            await ApplyGeneratedSubsetAsync(subset.ToHashSet());
            var build = await BuildAllObservedContextsAsync(
                validationDir,
                sourceWorkDir,
                config,
                subset.ToList(),
                cancellationToken,
                includeRootBuild: true,
                phase: "project-isolation");
            buildCache[cacheKey] = build.Succeeded;
            return build.Succeeded;
        }

        async Task ApplyGeneratedSubsetAsync(
            IReadOnlySet<FileConversionResult> generated)
        {
            foreach (var candidate in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(
                    validationDir,
                    candidate.FilePath);
                if (generated.Contains(candidate))
                {
                    await File.WriteAllTextAsync(
                        destination,
                        candidate.EmittedCSharp!,
                        cancellationToken);
                }
                else
                {
                    var original = await File.ReadAllTextAsync(
                        Path.Combine(
                            config.OriginalProjectPath,
                            candidate.FilePath),
                        cancellationToken);
                    await File.WriteAllTextAsync(
                        destination,
                        original,
                        cancellationToken);
                }

            }
            DeleteBuildOutputs(
                validationDir,
                cancellationToken);
        }

        static List<List<FileConversionResult>> Partition(
            IReadOnlyList<FileConversionResult> candidates,
            int count)
        {
            var partitions = Enumerable.Range(0, count)
                .Select(_ => new List<FileConversionResult>())
                .ToList();
            for (var index = 0; index < candidates.Count; index++)
                partitions[index % count].Add(candidates[index]);
            return partitions
                .Where(partition => partition.Count > 0)
                .ToList();
        }
    }

    private static bool BuildErrorReferencesFile(
        string workDir,
        string relativePath,
        string error)
    {
        var parenIndex = error.IndexOf('(');
        if (parenIndex <= 0)
            return false;

        var errorPath = error[..parenIndex]
            .Trim()
            .Replace("/private/var/", "/var/");
        var expectedPath = Path.GetFullPath(Path.Combine(workDir, relativePath))
            .Replace("/private/var/", "/var/");
        return string.Equals(
            Path.GetFullPath(errorPath, workDir),
            expectedPath,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task ValidateAndPublishGeneratedFilesAsync(
        string workDir,
        IReadOnlyList<FileConversionResult> results,
        IReadOnlyList<string> projectSourcePaths,
        CancellationToken cancellationToken)
    {
        var candidates = results
            .Where(result =>
                result.Status == FileStatus.Replaced &&
                result.EmittedCSharp != null)
            .ToList();
        if (candidates.Count == 0)
            return;

        var projectSources = projectSourcePaths
            .ToDictionary(
                Path.GetFullPath,
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase);
        var referencePaths = DiscoverProjectReferencePaths(workDir);

        var active = candidates.ToList();
        while (active.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var activePaths = active
                .Select(result => Path.GetFullPath(
                    Path.Combine(workDir, result.FilePath)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var generatedSources = active.Select(result => new GeneratedCSharpSource(
                result.EmittedCSharp!,
                Path.GetFullPath(Path.Combine(workDir, result.FilePath))));
            var additionalSources = projectSources
                .Where(pair => !activePaths.Contains(pair.Key))
                .Select(pair => new GeneratedCSharpSource(pair.Value, pair.Key));
            var validation = GeneratedCSharpCompiler.Validate(
                generatedSources,
                new GeneratedCSharpCompilationContext
                {
                    AdditionalSources = additionalSources,
                    ReferencePaths = referencePaths,
                });
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var candidate in active)
            {
                var path = Path.GetFullPath(Path.Combine(workDir, candidate.FilePath));
                candidate.Candidate?.Diagnostics.AddRange(validation.CompilationErrors
                    .Where(diagnostic => string.Equals(diagnostic.Location.SourceTree?.FilePath, path,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(diagnostic => ReportGenerator.CaptureRoslynDiagnostic(
                        diagnostic, "generated-project-validation", workDir)));
            }

            if (validation.CompilationSuccess)
            {
                foreach (var candidate in active)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = Path.Combine(workDir, candidate.FilePath);
                    await File.WriteAllTextAsync(
                        path,
                        candidate.EmittedCSharp!,
                        cancellationToken);
                }
                return;
            }

            var errorsByPath = validation.CompilationErrors
                .Where(diagnostic => diagnostic.Location.IsInSource)
                .GroupBy(
                    diagnostic => Path.GetFullPath(
                        diagnostic.Location.SourceTree?.FilePath ?? workDir),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(FormatDiagnostic).ToList(),
                    StringComparer.OrdinalIgnoreCase);
            var failed = active
                .Where(candidate => errorsByPath.ContainsKey(Path.GetFullPath(
                    Path.Combine(workDir, candidate.FilePath))))
                .ToList();
            if (failed.Count == 0)
            {
                failed = FindCandidatesReferencedByDiagnostics(
                    active,
                    validation.CompilationErrors);
                if (failed.Count == 0)
                {
                    var errors = validation.FormattedCompilationErrors.ToList();
                    foreach (var candidate in active)
                    {
                        candidate.Status = FileStatus.EmitCompilationError;
                        candidate.Errors = errors;
                        candidate.Candidate?.Diagnostics.AddRange(validation.CompilationErrors
                            .Select(diagnostic => ReportGenerator.CaptureRoslynDiagnostic(
                                diagnostic, "generated-project-validation", workDir, "unattributed-project-failure")));
                    }
                    return;
                }
            }

            foreach (var candidate in failed)
            {
                var path = Path.GetFullPath(Path.Combine(workDir, candidate.FilePath));
                if (!errorsByPath.ContainsKey(path))
                    candidate.Candidate?.Diagnostics.AddRange(validation.CompilationErrors
                        .Where(diagnostic => CandidateIsReferenced(candidate, diagnostic.GetMessage()))
                        .Select(diagnostic => ReportGenerator.CaptureRoslynDiagnostic(
                            diagnostic, "generated-project-validation", workDir, "referenced-symbol")));
                candidate.Status = FileStatus.EmitCompilationError;
                candidate.Errors = errorsByPath.TryGetValue(path, out var directErrors)
                    ? directErrors
                    : validation.CompilationErrors
                        .Where(diagnostic => CandidateIsReferenced(
                            candidate,
                            diagnostic.GetMessage()))
                        .Select(FormatDiagnostic)
                        .ToList();
                active.Remove(candidate);
            }
        }

    }

    private static List<FileConversionResult> FindCandidatesReferencedByDiagnostics(
        IEnumerable<FileConversionResult> candidates,
        IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        var messages = diagnostics.Select(diagnostic => diagnostic.GetMessage()).ToList();
        return candidates
            .Where(candidate => messages.Any(message =>
                CandidateIsReferenced(candidate, message)))
            .ToList();
    }

    internal static bool CandidateIsReferenced(
        FileConversionResult candidate,
        string diagnosticMessage)
    {
        if (candidate.EmittedCSharp == null)
            return false;

        var syntaxNames = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
            .ParseText(candidate.EmittedCSharp)
            .GetRoot()
            .DescendantNodes()
            .Select(node => node switch
            {
                Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax type =>
                    type.Identifier.ValueText,
                Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax declaration =>
                    declaration.Identifier.ValueText,
                _ => null,
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!);
        var lexicalNames = System.Text.RegularExpressions.Regex
            .Matches(
                candidate.EmittedCSharp,
                @"\b(?:class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)|\brecord(?:\s+(?:class|struct))?\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(match => match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value);
        var declaredNames = syntaxNames
            .Concat(lexicalNames)
            .Distinct(StringComparer.Ordinal);
        return declaredNames.Any(name =>
            diagnosticMessage.Contains(
                $"'{name}'",
                StringComparison.Ordinal));
    }

    internal static IReadOnlyList<string> DiscoverProjectReferencePaths(string workDir)
    {
        var platformAssemblyNames = ((string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(TryGetAssemblyName)
            .Where(name => name != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectAssemblyNames = Directory
            .GetFiles(workDir, "*.*proj", SearchOption.AllDirectories)
            .Select(path =>
            {
                try
                {
                    var document = System.Xml.Linq.XDocument.Load(path);
                    return document
                        .Descendants()
                        .FirstOrDefault(element =>
                            element.Name.LocalName == "AssemblyName")
                        ?.Value;
                }
                catch (System.Xml.XmlException)
                {
                    return null;
                }
            })
            .Concat(Directory
                .GetFiles(workDir, "*.*proj", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory
            .GetFiles(workDir, "*.dll", SearchOption.AllDirectories)
            .Select(path => (Path: path, Name: TryGetAssemblyName(path)))
            .Where(item =>
                item.Name != null &&
                !item.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) &&
                !platformAssemblyNames.Contains(item.Name) &&
                !projectAssemblyNames.Contains(item.Name))
            .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Path)
            .ToList();

        static string? TryGetAssemblyName(string path)
        {
            try
            {
                return System.Reflection.AssemblyName.GetAssemblyName(path).Name;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                return null;
            }
        }
    }

    private static string FormatDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic)
    {
        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
        return $"{diagnostic.Id}: {diagnostic.GetMessage()} (line {line})";
    }

    /// <summary>
    /// Convert one C# source string to its round-tripped C# (Calor→C#) exactly as
    /// <see cref="ConvertAndReplaceAsync"/> does per file, WITHOUT touching the filesystem.
    /// Returns the emitted C# or null if the file does not convert-and-recompile cleanly.
    /// Used by the D-W4.1 task generator's attribution check to obtain the UNMUTATED-CONVERTED
    /// form of a file (converter output of the clean original), so the mutation can be isolated
    /// from any converter divergence localized to the same file (review [M]#2).
    /// </summary>
    internal string? ConvertSourceToRoundTripCSharp(
        string originalSource,
        string csFilePath,
        RoundTripConfig config,
        ProjectFileParseContext parseContext,
        PreprocessorConversionMode preprocessorMode =
            PreprocessorConversionMode.SelectActiveBranchLossy)
    {
        var converter = new CSharpToCalorConverter(
            CreateHarnessConversionOptions(
                config,
                parseContext,
                preprocessorMode));
        var conversionResult = converter.Convert(originalSource, csFilePath);
        if (!conversionResult.Success || string.IsNullOrWhiteSpace(conversionResult.CalorSource))
            return null;

        var compileResult = Compiler.Program.Compile(
            conversionResult.CalorSource, csFilePath,
            new Compiler.CompilationOptions
            {
                EnforceEffects = false,
                ContractMode = Compiler.ContractMode.Off,
                DeferGeneratedOutputValidation = true,
            });
        if (compileResult.HasErrors || string.IsNullOrWhiteSpace(compileResult.GeneratedCode))
            return null;

        var emitted = PostProcessEmittedCSharp(compileResult.GeneratedCode, originalSource);
        return GeneratedCSharpCompiler.Validate(emitted).CompilationSuccess
            ? emitted
            : null;
    }

    internal static ConversionOptions CreateHarnessConversionOptions(
        RoundTripConfig config,
        ProjectFileParseContext? parseContext,
        PreprocessorConversionMode preprocessorMode =
            PreprocessorConversionMode.SelectActiveBranchLossy)
        => new()
        {
            Fidelity = preprocessorMode
                    == PreprocessorConversionMode.PreserveAllBranches
                ? ConversionFidelity.Lossless
                : ConversionFidelity.Lossy,
            PreprocessorMode = preprocessorMode,
            ParseOptions = parseContext?.ParseOptions
                ?? new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                    Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
                    Microsoft.CodeAnalysis.DocumentationMode.Parse,
                    Microsoft.CodeAnalysis.SourceCodeKind.Regular,
                    preprocessorSymbols: Array.Empty<string>()),
            Configuration = parseContext?.Configuration
                ?? config.Configuration,
            TargetFramework = parseContext?.TargetFramework
                ?? config.TargetFramework,
            GracefulFallback = true,
            PassthroughOnError = true,
            // The harness compiles the exact post-processed generated bytes
            // below before writing them.
            ValidateRoundTripCSharp = false,
            PreserveComments = true,
            AutoGenerateIds = true,
        };

    private static string NormalizeSelectedSource(string source)
        => source.Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);

    internal static MultiContextConversionPlan
        CreateMultiContextConversionPlan(
            string source,
            string sourcePath,
            IReadOnlyList<ProjectFileParseContext> contexts,
            CancellationToken cancellationToken = default)
    {
        var errors = contexts
            .SelectMany(context =>
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
                    .ParseText(
                        source,
                        context.ParseOptions,
                        sourcePath)
                    .GetDiagnostics()
                    .Where(diagnostic =>
                        diagnostic.Id == "CS1029"
                        && diagnostic.Severity
                            == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(diagnostic =>
                        $"{DescribeContext(context)}: "
                        + FormatDiagnostic(diagnostic)))
            .ToList();
        if (errors.Count > 0)
        {
            return new MultiContextConversionPlan(
                contexts.FirstOrDefault(),
                PreprocessorConversionMode.SelectActiveBranchLossy,
                "active-error-rejected",
                contexts,
                errors);
        }

        var mode = PreprocessorConversionMode
            .SelectActiveBranchLossy;
        if (contexts.Count > 1)
        {
            var selectedSources = contexts
                .Select(context => NormalizeSelectedSource(
                    PreprocessorStripper.SelectActiveBranchLossy(
                        source,
                        context.ParseOptions,
                        cancellationToken).Source))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count();
            if (selectedSources > 1)
                mode = PreprocessorConversionMode.PreserveAllBranches;
        }
        return new MultiContextConversionPlan(
            contexts.FirstOrDefault(),
            mode,
            contexts.Count <= 1
                ? "single-context-selected"
                : mode == PreprocessorConversionMode
                    .SelectActiveBranchLossy
                    ? "multi-context-identical-selected"
                    : "multi-context-divergent-preserve",
            contexts,
            []);
    }

    private static FileContextDetail CreateFileContextDetail(
        ProjectFileParseContext context)
        => new()
        {
            ProjectFile = context.ProjectFile,
            Configuration = context.Configuration,
            Platform = context.Platform,
            TargetFramework = context.TargetFramework,
            LanguageVersion =
                context.ParseOptions.LanguageVersion.ToString(),
            DocumentationMode =
                context.ParseOptions.DocumentationMode.ToString(),
            SourceCodeKind =
                context.ParseOptions.Kind.ToString(),
            DefinedSymbols = context.ParseOptions
                .PreprocessorSymbolNames
                .Distinct(StringComparer.Ordinal)
                .OrderBy(symbol => symbol, StringComparer.Ordinal)
                .ToList(),
            Provenance = context.Provenance
                .Select(provenance => string.Join(
                    " -> ",
                    provenance.ProjectGraphPath))
                .ToList(),
            BuildStates = context.BuildStates.Select(state => new
                FileBuildStateDetail
                {
                    ProjectFile = state.ProjectFile,
                    Configuration = state.Configuration,
                    Platform = state.Platform,
                    TargetFramework = state.TargetFramework,
                    GlobalProperties = new Dictionary<string, string>(
                        state.GlobalProperties,
                        StringComparer.OrdinalIgnoreCase),
                    ProjectGraphPath =
                        state.ProjectGraphPath.ToList()
                })
                .ToList(),
            CompilationProperties = new Dictionary<string, string>(context.CompilationProperties),
            References = context.References.Select(reference => new ReferenceEvidence
            {
                Path = reference.Path,
                Identity = ReportGenerator.Fingerprint(reference.Path).Identity,
                Sha256 = reference.ContentHash,
                Aliases = reference.Aliases.ToList(),
                Properties = new Dictionary<string, string>(reference.Properties),
            }).ToList(),
            CompileInputHashes = context.CompileInputHashes.ToList(),
            AdditionalInputHashes = context.AdditionalInputHashes.ToList(),
            AnalyzerConfigHashes = context.AnalyzerConfigHashes.ToList(),
            Analyzers = context.AnalyzerPaths.Select(path => ReportGenerator.Fingerprint(path)).ToList(),
        };

    private static string DescribeContext(
        ProjectFileParseContext context)
        => $"{context.ProjectFile}"
            + $" [configuration={context.Configuration}, "
            + $"platform={context.Platform}, "
            + $"tfm={context.TargetFramework ?? "<none>"}, "
            + $"symbols={string.Join(
                ",",
                context.ParseOptions.PreprocessorSymbolNames
                    .OrderBy(symbol => symbol, StringComparer.Ordinal))}]";

    /// <summary>
    /// When build fails, identify files mentioned in build errors, revert them
    /// to their originals, and update their status. Iterates up to 5 times.
    /// </summary>
    internal async Task<int> RecoverBuildAsync(
        string workDir,
        RoundTripConfig config,
        List<FileConversionResult> fileResults,
        CancellationToken cancellationToken = default)
    {
        var totalReverted = 0;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buildResult = await BuildProjectAsync(workDir, config, cancellationToken, phase: "build-recovery",
                candidateFiles: fileResults.Where(file => file.Status == FileStatus.Replaced).Select(file => file.FilePath).ToList());
            if (buildResult.Succeeded) break;

            // Extract file paths from build error lines, KEEPING the diagnostics per file. The
            // reverted bucket is the largest failure class, and without the attributed errors a
            // cause census over it is impossible — the file just says "build error".
            var errorFiles = new HashSet<string>();
            var errorsByFile = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var error in buildResult.Errors)
            {
                // Build errors look like: /path/to/file.cs(line,col): error CS...
                var parenIdx = error.IndexOf('(');
                if (parenIdx > 0)
                {
                    var filePath = error[..parenIdx].Trim();
                    if (filePath.EndsWith(".cs"))
                    {
                        // macOS resolves /var → /private/var in build output
                        var normalized = filePath.Replace("/private/var/", "/var/");
                        var relativePath = ReportGenerator.RelativePath(workDir, Path.GetFullPath(normalized, workDir));
                        if (!relativePath.StartsWith(".."))
                        {
                            errorFiles.Add(relativePath);
                            if (!errorsByFile.TryGetValue(relativePath, out var list))
                                errorsByFile[relativePath] = list = [];
                            if (list.Count < 10) list.Add(error.Trim());   // cap: a file can emit hundreds
                        }
                    }
                }
            }

            if (errorFiles.Count == 0) break;

            var revertedThisRound = 0;
            foreach (var relPath in errorFiles)
            {
                var fileResult = fileResults.FirstOrDefault(f => f.FilePath == relPath);
                if (fileResult is not { Status: FileStatus.Replaced }) continue;

                var originalPath = Path.Combine(config.OriginalProjectPath, relPath);
                var workPath = Path.Combine(workDir, relPath);
                if (!File.Exists(originalPath)) continue;

                var original = await File.ReadAllTextAsync(originalPath, cancellationToken);
                await File.WriteAllTextAsync(workPath, original, cancellationToken);
                // A reverted file is a coverage FAILURE: it stays in the denominator
                // and is never counted as converted. Do NOT relabel it CompileError —
                // it compiled standalone; the round-tripped output broke the build.
                fileResult.Status = FileStatus.Reverted;
                fileResult.RevertReason = $"build-recovery round {attempt + 1}: build error in round-tripped output";
                fileResult.Errors = errorsByFile.TryGetValue(relPath, out var attributed) && attributed.Count > 0
                    ? attributed
                    : [$"Reverted: build error in round-tripped output (recovery round {attempt + 1})"];
                fileResult.Recovery.Add(new RecoveryEvidence
                {
                    Phase = "build-recovery", Attempt = attempt + 1, Outcome = "Reverted",
                    Errors = fileResult.Errors.ToList(),
                    Diagnostics = buildResult.Diagnostics.Where(diagnostic => diagnostic.Path == relPath).ToList(),
                });
                revertedThisRound++;
                Console.WriteLine($"    Reverted: {relPath}");
            }

            totalReverted += revertedThisRound;
            if (revertedThisRound == 0) break;
        }

        return totalReverted;
    }

    /// <summary>
    /// Post-process emitted C# to make it compatible with the original project.
    /// The CSharpEmitter adds Calor-specific using directives and headers that
    /// the target project doesn't know about.
    /// </summary>
    private static string PostProcessEmittedCSharp(string emittedCode, string originalSource)
    {
        var lines = emittedCode.Split('\n').ToList();
        var result = new List<string>();

        // Check what the original source had
        var originalHadNullable = originalSource.Contains("#nullable enable");
        var originalUsings = new HashSet<string>();
        foreach (var line in originalSource.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
                originalUsings.Add(trimmed);
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Strip auto-generated header comments
            if (trimmed.StartsWith("// <auto-generated") || trimmed.StartsWith("// </auto-generated"))
                continue;

            // Strip Calor.Runtime using (target project doesn't reference it)
            if (trimmed == "using Calor.Runtime;")
                continue;

            // Strip #nullable enable if original didn't have it
            if (trimmed == "#nullable enable" && !originalHadNullable)
                continue;

            result.Add(line);
        }

        // Remove leading blank lines
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[0]))
            result.RemoveAt(0);

        return string.Join('\n', result);
    }

    private static bool ShouldExclude(string filePath, List<string> patterns)
    {
        var normalized = filePath.Replace('\\', '/');
        foreach (var pattern in patterns)
        {
            if (MatchGlob(normalized, pattern))
                return true;
        }
        return false;
    }

    private static bool MatchGlob(string path, string pattern)
    {
        // Simple glob matching for our use case
        if (pattern.StartsWith("**/"))
        {
            var suffix = pattern[3..];
            if (suffix.Contains("**"))
            {
                // Pattern like **/obj/** — check if segment exists in path
                var segment = suffix.Replace("/**", "");
                return path.Contains($"/{segment}/") || path.EndsWith($"/{segment}");
            }
            // Pattern like **/*.g.cs — star-suffix must match by extension, not
            // literally (#837 review M-1: the literal comparison made every
            // `**/*.x` exclude inert, letting generated files into the coverage
            // denominator the fidelity gate thresholds on).
            if (suffix.StartsWith("*."))
            {
                return path.EndsWith(suffix[1..]);
            }
            // Pattern like **/AssemblyInfo.cs
            return path.EndsWith("/" + suffix) || path.EndsWith(suffix);
        }
        if (pattern.StartsWith("*."))
        {
            return path.EndsWith(pattern[1..]);
        }
        return path.Contains(pattern);
    }

    private static bool IsBuildOutputPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    internal async Task<BuildResult> BuildProjectAsync(
        string workDir,
        RoundTripConfig config,
        CancellationToken cancellationToken = default,
        bool noIncremental = false,
        bool disableBuildServers = false,
        string phase = "build",
        IReadOnlyList<string>? candidateFiles = null,
        string? reportedSourceRoot = null)
    {
        // Relative target (see RunTestsAsync) — absolute /var-symlink paths break
        // MSBuild path identity on macOS.
        var target = config.SolutionOrProjectFile;

        var args = $"build \"{target}\" ";
        args += $" --configuration {config.Configuration}";
        if (config.TargetFramework != null)
            args += $" --framework {config.TargetFramework}";
        if (noIncremental)
            args += " --no-incremental";
        if (disableBuildServers)
            args += " --disable-build-servers -m:1 -p:BuildInParallel=false";
        if (!string.IsNullOrWhiteSpace(config.ExtraBuildProperties))
            args += $" {config.ExtraBuildProperties}";

        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
            config.DotnetPath,
            args,
            workDir,
            config.BuildTimeout,
            cancellationToken: cancellationToken);

        var errors = new List<string>();
        foreach (var line in (stdout + "\n" + stderr).Split('\n'))
        {
            if (line.Contains(": error "))
                errors.Add(line.Trim());
        }

        var result = new BuildResult
        {
            Phase = phase,
            Command = $"{config.DotnetPath} {args}",
            WorkingDirectory = workDir,
            Diagnostics = ReportGenerator.CaptureBuildDiagnostics(errors, phase, workDir, reportedSourceRoot),
            CandidateFiles = candidateFiles?.ToList() ?? [],
            Succeeded = exitCode == 0,
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
            Errors = errors,
        };
        _evidence?.BuildAttempts.Add(result);
        return result;
    }

    private static TestComparison CompareTestResults(
        RoundTripConfig config,
        TestRunResult? baseline, TestRunResult? roundTrip, BuildResult? buildResult)
    {
        if (buildResult is { Succeeded: false })
        {
            return new TestComparison
            {
                Status = ComparisonStatus.BuildFailed,
                BaselineTotal = baseline?.TotalTests ?? 0,
                BaselinePassed = baseline?.Passed ?? 0,
            };
        }

        if (baseline == null || roundTrip == null)
            return new TestComparison { Status = ComparisonStatus.Incomplete };

        var allowlist = config.ExpectedFlakyTestFullyQualifiedNames;
        var comparison = new TestComparison
        {
            BaselineTotal = baseline.TotalTests,
            BaselinePassed = baseline.Passed,
            RoundTripTotal = roundTrip.TotalTests,
            RoundTripPassed = roundTrip.Passed,
            // Recorded before any early return so an Incomplete/blocked report
            // still shows which failures were known upstream flakes.
            IgnoredFlakyBaselineFailures = baseline.Results
                .Where(t => t.Outcome == "Failed" && allowlist.Contains(t.FullyQualifiedName))
                .ToList(),
            IgnoredFlakyRoundTripFailures = roundTrip.Results
                .Where(t => t.Outcome == "Failed" && allowlist.Contains(t.FullyQualifiedName))
                .ToList(),
        };

        // A baseline that exited non-zero is normally unusable as a reference.
        // The one exception: `dotnet test` exited 1 and every failure it reports
        // is an allowlisted upstream flake — then the baseline is still a valid
        // reference and the comparison proceeds (the flakes stay visible on
        // IgnoredFlakyBaselineFailures).
        var baselineExitUsable = baseline.ExitCode == 0
            || RoundTripExitPolicy.IsTestExitExplainedByKnownFlakes(
                baseline, comparison.IgnoredFlakyBaselineFailures);

        if (baseline.TotalTests == 0 || roundTrip.TotalTests == 0 ||
            !baselineExitUsable ||
            baseline.ParseErrors.Count > 0 ||
            roundTrip.ParseErrors.Count > 0)
        {
            comparison.Status = ComparisonStatus.Incomplete;
            return comparison;
        }

        if (baseline.UsedConsoleFallback || roundTrip.UsedConsoleFallback
            || baseline.Results.Concat(roundTrip.Results).Any(result =>
                result.Outcome is not ("Passed" or "Failed" or "Skipped" or "NotExecuted")))
        {
            comparison.Status = ComparisonStatus.Incomplete;
            return comparison;
        }

        var baselineByIdentity = baseline.Results
            .GroupBy(t => t.Identity)
            .ToDictionary(group => group.Key, group => group.ToList());
        var roundTripByIdentity = roundTrip.Results
            .GroupBy(t => t.Identity)
            .ToDictionary(group => group.Key, group => group.ToList());
        // Some adapters emit indistinguishable theory rows with the same case ID and
        // display name. Compare their outcome counts as one equivalence class, but
        // fail closed if the number of rows in any class changes.
        if (!baselineByIdentity.Keys.ToHashSet().SetEquals(roundTripByIdentity.Keys) ||
            baselineByIdentity.Count != roundTripByIdentity.Count ||
            baselineByIdentity.Any(pair =>
                roundTripByIdentity[pair.Key].Count != pair.Value.Count))
        {
            comparison.Status = ComparisonStatus.Incomplete;
            return comparison;
        }

        foreach (var (identity, baselineResults) in baselineByIdentity)
        {
            var baselinePassed = baselineResults.Count(t => t.Outcome == "Passed");
            var baselineFailed = baselineResults.Count(t => t.Outcome == "Failed");
            var baselineSkipped = baselineResults.Count(t =>
                t.Outcome is "Skipped" or "NotExecuted");
            var roundTripResults = roundTripByIdentity[identity];
            var roundTripPassed = roundTripResults.Count(t => t.Outcome == "Passed");
            var roundTripFailed = roundTripResults.Count(t => t.Outcome == "Failed");
            var roundTripSkipped = roundTripResults.Count(t =>
                t.Outcome is "Skipped" or "NotExecuted");

            var passDeficit = Math.Max(0, baselinePassed - roundTripPassed);
            var failedExcess = Math.Max(0, roundTripFailed - baselineFailed);
            var skippedExcess = Math.Max(0, roundTripSkipped - baselineSkipped);
            var regressionCount = Math.Max(passDeficit, failedExcess + skippedExcess);
            var regressions = roundTripResults
                .Where(t => t.Outcome == "Failed")
                .Take(failedExcess)
                .Concat(roundTripResults
                    .Where(t => t.Outcome is "Skipped" or "NotExecuted")
                    .Take(skippedExcess))
                .ToList();
            regressions.AddRange(
                roundTripResults
                    .Where(t => t.Outcome != "Passed" && !regressions.Contains(t))
                    .Take(regressionCount - regressions.Count));

            // Route regressions that land on a known-flaky upstream test into
            // IgnoredFlakyRegressions instead of Regressions. The gate then
            // doesn't count them toward the block/warn thresholds, but the
            // report still surfaces them so drift stays visible. See
            // RoundTripConfig.ExpectedFlakyTestFullyQualifiedNames for
            // provenance requirements.
            if (allowlist.Count > 0)
            {
                var flaky = regressions
                    .Where(t => allowlist.Contains(t.FullyQualifiedName))
                    .ToList();
                if (flaky.Count > 0)
                {
                    comparison.IgnoredFlakyRegressions.AddRange(flaky);
                    regressions = regressions.Except(flaky).ToList();
                }
            }

            comparison.Regressions.AddRange(regressions);
        }

        // Pre-existing failures
        comparison.PreExistingFailures = baseline.Results.Count(t => t.Outcome == "Failed");

        // New passes: failing in baseline, passing in round-trip
        foreach (var (identity, baselineResults) in baselineByIdentity)
        {
            var baselinePassed = baselineResults.Count(t => t.Outcome == "Passed");
            var roundTripPassed = roundTripByIdentity[identity].Count(t => t.Outcome == "Passed");
            comparison.NewPasses.AddRange(
                roundTripByIdentity[identity]
                    .Where(t => t.Outcome == "Passed")
                    .Take(Math.Max(0, roundTripPassed - baselinePassed))
                    .Select(t => t.TestName));
        }

        // Verdict
        if (comparison.Regressions.Count == 0)
            comparison.Status = ComparisonStatus.Pass;
        else if (comparison.BaselinePassed > 0 &&
                 (double)comparison.Regressions.Count / comparison.BaselinePassed < 0.05)
            comparison.Status = ComparisonStatus.MinorRegressions;
        else
            comparison.Status = ComparisonStatus.MajorRegressions;

        return comparison;
    }

    private async Task<Dictionary<string, List<string>>> BisectRegressionsAsync(
        string workDir,
        RoundTripConfig config,
        List<TestResult> regressions,
        List<FileConversionResult> convertedFiles,
        CancellationToken cancellationToken)
    {
        var culprits = new Dictionary<string, List<string>>();
        var failingTestNames = regressions.Select(t => t.TestName).ToHashSet();

        foreach (var file in convertedFiles.Where(f => f.Status == FileStatus.Replaced && f.EmittedCSharp != null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(workDir, file.FilePath);
            var emittedContent = await File.ReadAllTextAsync(fullPath, cancellationToken);

            // Revert this one file to original
            var originalPath = Path.Combine(config.OriginalProjectPath, file.FilePath);
            if (!File.Exists(originalPath)) continue;
            var originalContent = await File.ReadAllTextAsync(originalPath, cancellationToken);
            await File.WriteAllTextAsync(fullPath, originalContent, cancellationToken);

            // Re-run just the failing tests
            TrxParser.CleanTrxFiles(workDir);
            var bisectConfig = config with { TestFilter = null, EnableBisect = false };
            var result = await RunTestsAsync(
                workDir, bisectConfig, cancellationToken: cancellationToken);
            _evidence?.TestAttempts.Add(new TestAttemptEvidence
            {
                Leg = $"bisect:{file.FilePath}", Attempt = 1, Result = result,
            });

            // Check if any previously-failing tests now pass
            var nowPassing = result.Results
                .Where(t => t.Outcome == "Passed" && failingTestNames.Contains(t.TestName))
                .Select(t => t.TestName)
                .ToList();

            if (nowPassing.Count > 0)
            {
                culprits[file.FilePath] = nowPassing;
                Console.WriteLine($"  Culprit: {file.FilePath} → {nowPassing.Count} test(s)");
            }

            // Restore the emitted version
            await File.WriteAllTextAsync(fullPath, emittedContent, cancellationToken);
        }

        return culprits;
    }

    private static string GetCalorVersion()
    {
        var assembly = typeof(Compiler.Program).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "unknown";
    }
}
