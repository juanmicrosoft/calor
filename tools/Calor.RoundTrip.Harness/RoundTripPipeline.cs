using System.Collections.Concurrent;
using Calor.Compiler.Migration;
using Calor.Compiler.CodeGen;

namespace Calor.RoundTrip.Harness;

/// <summary>
/// Orchestrates the full round-trip verification pipeline:
/// Snapshot → Baseline → Convert → Build → Test → Compare.
/// </summary>
public sealed class RoundTripPipeline
{
    /// <summary>
    /// Run the full round-trip pipeline for a target project.
    /// </summary>
    public async Task<RoundTripReport> RunAsync(
        RoundTripConfig config,
        CancellationToken cancellationToken = default)
    {
        var report = new RoundTripReport
        {
            ProjectName = config.ProjectName,
            CalorVersion = GetCalorVersion(),
            StartedAt = DateTimeOffset.UtcNow,
            MinimumCoverageFraction = config.MinimumCoverageFraction,
            MinimumNativeFraction = config.MinimumNativeFraction,
        };

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
            workDir, config, cancellationToken);
        if (!report.BaselineBuildResult.Succeeded)
            Console.WriteLine("  WARNING: baseline build failed (the vendored subject does not build clean here)");
        report.Baseline = await RunTestsAsync(
            workDir,
            config,
            noBuild: report.BaselineBuildResult.Succeeded,
            cancellationToken);
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
        report.BuildResult = await BuildProjectAsync(workDir, config, cancellationToken);

        if (!report.BuildResult.Succeeded)
        {
            Console.WriteLine("  Build failed — attempting recovery by reverting problematic files...");
            var revertedCount = await RecoverBuildAsync(
                workDir, config, report.FileResults, cancellationToken);
            if (revertedCount > 0)
            {
                Console.WriteLine($"  Reverted {revertedCount} file(s), rebuilding...");
                report.BuildResult = await BuildProjectAsync(
                    workDir, config, cancellationToken);
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
            report.RoundTripTests = await RunTestsAsync(
                workDir, config, cancellationToken: cancellationToken);
            Console.WriteLine($"  Round-trip: {report.RoundTripTests.Passed}/{report.RoundTripTests.TotalTests} passed, {report.RoundTripTests.Failed} failed");
        }
        else
        {
            Console.WriteLine("\nPhase 5/5: Skipped (build failed)");
        }

        // Compare
        report.Comparison = CompareTestResults(report.Baseline, report.RoundTripTests, report.BuildResult);

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

        report.FinishedAt = DateTimeOffset.UtcNow;
        return report;
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
                ParseErrors = parseErrors,
                Stdout = stdout,
                Stderr = stderr,
            };
        }

        // Fallback: if TRX parsing found no results, parse console output
        if (testResults.Count == 0)
        {
            return ParseConsoleTestOutput(exitCode, stdout, stderr);
        }

        return new TestRunResult
        {
            ExitCode = exitCode,
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

    private static TestRunResult ParseConsoleTestOutput(int exitCode, string stdout, string stderr)
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
        var results = new ConcurrentBag<FileConversionResult>();
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
            var relativePath = Path.GetRelativePath(workDir, csFile);
            var result = new FileConversionResult { FilePath = relativePath };
            config.FileConversionStarted?.Invoke(relativePath);

            try
            {
                var originalSource = await File.ReadAllTextAsync(csFile, token);
                var converter = new CSharpToCalorConverter(new ConversionOptions
                {
                    Fidelity = ConversionFidelity.Lossless,
                    GracefulFallback = true,
                    PassthroughOnError = true,
                    // The harness compiles the exact post-processed generated bytes
                    // below before writing them.
                    ValidateRoundTripCSharp = false,
                    PreserveComments = true,
                    AutoGenerateIds = true,
                });

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
                    var compileResult = Compiler.Program.Compile(
                        conversionResult.CalorSource, csFile, compileOptions);
                    token.ThrowIfCancellationRequested();

                    if (!compileResult.HasErrors && !string.IsNullOrWhiteSpace(compileResult.GeneratedCode))
                    {
                        // Step 3c: Post-process emitted C# for round-trip compatibility
                        var emitted = PostProcessEmittedCSharp(compileResult.GeneratedCode, originalSource);

                        // Step 3d: Syntax-check now. All emitted files are compiled
                        // together with project sources/references below before any
                        // project file is replaced.
                        var syntaxErrors = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
                            .ParseText(emitted)
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
                result.Errors = [ex.Message];
            }

            results.Add(result);
            var completed = Interlocked.Increment(ref completedCount);
            if (completed % 10 == 0 || completed == csFiles.Count)
                Console.Write($" {completed}/{csFiles.Count}");
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

        // Print status summary
        foreach (var group in orderedResults
            .GroupBy(r => r.Status)
            .OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        return orderedResults;
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
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(validationDir, candidate.FilePath);
                await File.WriteAllTextAsync(
                    path,
                    candidate.EmittedCSharp!,
                    cancellationToken);
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var build = await BuildProjectAsync(
                    validationDir,
                    config,
                    cancellationToken);
                var active = candidates
                    .Where(result => result.Status == FileStatus.Replaced)
                    .ToList();
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
                    Console.WriteLine(
                        $"  Project validation could not attribute {build.Errors.Count} build error(s); rejecting all {active.Count} remaining candidates");
                    foreach (var error in build.Errors.Take(5))
                        Console.WriteLine($"    {error}");
                    failed = active;
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
                    candidate.Errors = build.Errors
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
                        .Take(10)
                        .ToList();
                    if (candidate.Errors.Count == 0)
                        candidate.Errors = build.Errors.Take(10).ToList();
                }

                if (failed.Count == active.Count)
                    return;
            }
        }
        finally
        {
            if (Directory.Exists(validationDir))
                Directory.Delete(validationDir, recursive: true);
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
            Path.GetFullPath(errorPath),
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
                    }
                    return;
                }
            }

            foreach (var candidate in failed)
            {
                var path = Path.GetFullPath(Path.Combine(workDir, candidate.FilePath));
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
    internal string? ConvertSourceToRoundTripCSharp(string originalSource, string csFilePath)
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossless,
            GracefulFallback = true,
            PassthroughOnError = true,
            ValidateRoundTripCSharp = false,
            PreserveComments = true,
            AutoGenerateIds = true,
        });
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
            var buildResult = await BuildProjectAsync(workDir, config, cancellationToken);
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
                        var relativePath = Path.GetRelativePath(workDir, normalized);
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
        CancellationToken cancellationToken = default)
    {
        // Relative target (see RunTestsAsync) — absolute /var-symlink paths break
        // MSBuild path identity on macOS.
        var target = config.SolutionOrProjectFile;

        var args = $"build \"{target}\" ";
        if (config.TargetFramework != null)
            args += $" --framework {config.TargetFramework}";
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

        return new BuildResult
        {
            Succeeded = exitCode == 0,
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
            Errors = errors,
        };
    }

    private static TestComparison CompareTestResults(
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

        var comparison = new TestComparison
        {
            BaselineTotal = baseline.TotalTests,
            BaselinePassed = baseline.Passed,
            RoundTripTotal = roundTrip.TotalTests,
            RoundTripPassed = roundTrip.Passed,
        };

        if (baseline.TotalTests == 0 || roundTrip.TotalTests == 0 ||
            baseline.ExitCode != 0 ||
            baseline.ParseErrors.Count > 0 ||
            roundTrip.ParseErrors.Count > 0)
        {
            comparison.Status = ComparisonStatus.Incomplete;
            return comparison;
        }

        if (baseline.UsedConsoleFallback || roundTrip.UsedConsoleFallback)
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
        if (baselineByIdentity.Any(pair => pair.Value.Count != 1) ||
            roundTripByIdentity.Any(pair => pair.Value.Count != 1))
        {
            comparison.Status = ComparisonStatus.Incomplete;
            return comparison;
        }
        if (!baselineByIdentity.Keys.ToHashSet().SetEquals(roundTripByIdentity.Keys) ||
            baselineByIdentity.Count != roundTripByIdentity.Count)
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
            var bisectConfig = new RoundTripConfig
            {
                ProjectName = config.ProjectName,
                OriginalProjectPath = config.OriginalProjectPath,
                LibrarySourceRelativePath = config.LibrarySourceRelativePath,
                SolutionOrProjectFile = config.SolutionOrProjectFile,
                DotnetPath = config.DotnetPath,
                TargetFramework = config.TargetFramework,
                ExtraBuildProperties = config.ExtraBuildProperties,
                TestFilter = null,
                TestTimeout = config.TestTimeout,
                ConversionTimeout = config.ConversionTimeout,
                MinimumCoverageFraction = config.MinimumCoverageFraction,
                MinimumNativeFraction = config.MinimumNativeFraction,
            };
            var result = await RunTestsAsync(
                workDir, bisectConfig, cancellationToken: cancellationToken);

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
