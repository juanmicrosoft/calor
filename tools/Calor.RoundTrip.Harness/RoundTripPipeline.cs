using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    public async Task<RoundTripReport> RunAsync(RoundTripConfig config)
    {
        var report = new RoundTripReport
        {
            ProjectName = config.ProjectName,
            CalorVersion = GetCalorVersion(),
            StartedAt = DateTimeOffset.UtcNow,
        };

        // Step 1: Snapshot
        Console.WriteLine($"Phase 1/5: Creating working copy of {config.ProjectName}...");
        var workDir = PrepareWorkingCopy(config);
        Console.WriteLine($"  Working directory: {workDir}");

        // No separate pre-restore step: `dotnet build`/`dotnet test` restore the graph
        // coherently in one invocation, so a standalone restore only adds latency.

        // Step 2: Baseline tests. An explicit `dotnet build` first, then `dotnet test
        // --no-build`, so a failed baseline build is reported as exactly that (rather
        // than surfacing as an empty test run) and the round-trip build later compares
        // against a known-good baseline.
        Console.WriteLine("\nPhase 2/5: Running baseline tests...");
        TrxParser.CleanTrxFiles(workDir);
        var baselineBuild = await BuildProjectAsync(workDir, config);
        if (!baselineBuild.Succeeded)
            Console.WriteLine("  WARNING: baseline build failed (the vendored subject does not build clean here)");
        report.Baseline = await RunTestsAsync(workDir, config, noBuild: baselineBuild.Succeeded);
        Console.WriteLine($"  Baseline: {report.Baseline.Passed}/{report.Baseline.TotalTests} passed, {report.Baseline.Failed} failed, {report.Baseline.Skipped} skipped");

        // Step 3: Convert & Replace
        Console.WriteLine("\nPhase 3/5: Converting library source files...");
        report.FileResults = await ConvertAndReplaceAsync(workDir, config, report);
        var replaced = report.FileResults.Count(f => f.Status == FileStatus.Replaced);
        var total = report.FileResults.Count;
        Console.WriteLine($"  Converted: {replaced}/{total} files replaced");

        // Step 4: Build (with recovery — revert files that cause build errors)
        Console.WriteLine("\nPhase 4/5: Building modified project...");
        report.BuildResult = await BuildProjectAsync(workDir, config);

        if (!report.BuildResult.Succeeded)
        {
            Console.WriteLine("  Build failed — attempting recovery by reverting problematic files...");
            var revertedCount = await RecoverBuildAsync(workDir, config, report.FileResults);
            if (revertedCount > 0)
            {
                Console.WriteLine($"  Reverted {revertedCount} file(s), rebuilding...");
                report.BuildResult = await BuildProjectAsync(workDir, config);
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
            report.RoundTripTests = await RunTestsAsync(workDir, config);
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
                workDir, config, report.Comparison.Regressions, report.FileResults);
        }

        report.FinishedAt = DateTimeOffset.UtcNow;
        return report;
    }

    internal string PrepareWorkingCopy(RoundTripConfig config)
    {
        var workDir = config.WorkingDirectory
            ?? Path.Combine(Path.GetTempPath(), "calor-roundtrip", config.ProjectName, Guid.NewGuid().ToString("N")[..8]);

        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        CopyDirectory(config.OriginalProjectPath, workDir);
        return workDir;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
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
            var dirName = Path.GetFileName(dir);
            // Skip .git, bin, obj to speed up copy
            if (dirName is ".git" or "bin" or "obj" or ".vs" or ".idea")
                continue;
            CopyDirectory(dir, Path.Combine(destination, dirName));
        }
    }

    internal async Task<TestRunResult> RunTestsAsync(string workDir, RoundTripConfig config, bool noBuild = false)
    {
        // Pass the project RELATIVE to workDir (which is the process working directory),
        // never an absolute path. On macOS the temp root is /var/folders/… — a symlink
        // to /private/var/… — and passing the absolute /var path while MSBuild
        // canonicalizes the working directory to /private/var creates a path-identity
        // mismatch that silently breaks Directory.Build.props and ProjectReference
        // resolution (spurious CS0246 on the subject's own/transitive types). A relative
        // path keeps every path in one canonical form.
        var target = config.SolutionOrProjectFile;

        var args = $"test \"{target}\" --logger \"trx;LogFileName=results.trx\" --logger \"console;verbosity=normal\"";
        if (config.TargetFramework != null)
            args += $" --framework {config.TargetFramework}";
        if (noBuild)
            args += " --no-build";
        if (config.TestFilter != null)
            args += $" --filter \"{config.TestFilter}\"";
        if (!string.IsNullOrWhiteSpace(config.ExtraBuildProperties))
            args += $" {config.ExtraBuildProperties}";

        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
            config.DotnetPath, args, workDir, config.TestTimeout);

        // Parse and aggregate ALL TRX files (one per test assembly)
        var (testResults, trxFiles) = TrxParser.ParseAll(workDir);

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
        string workDir, RoundTripConfig config, RoundTripReport report)
    {
        var results = new List<FileConversionResult>();
        var libDir = Path.Combine(workDir, config.LibrarySourceRelativePath);

        if (!Directory.Exists(libDir))
        {
            Console.Error.WriteLine($"  ERROR: Library source directory not found: {libDir}");
            return results;
        }

        var allCsFiles = Directory.GetFiles(libDir, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();
        var csFiles = allCsFiles
            .Where(f => !ShouldExclude(f, config.ExcludePatterns))
            .ToList();
        report.ExcludedFileCount = allCsFiles.Count - csFiles.Count;

        Console.WriteLine($"  Found {csFiles.Count} C# files to convert ({report.ExcludedFileCount} excluded by pattern)");

        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            GracefulFallback = true,
            PreserveComments = true,
            AutoGenerateIds = true,
        });

        var convertedCount = 0;
        foreach (var csFile in csFiles)
        {
            var relativePath = Path.GetRelativePath(workDir, csFile);
            var result = new FileConversionResult { FilePath = relativePath };

            try
            {
                var originalSource = await File.ReadAllTextAsync(csFile);

                // Step 3a: Convert C# → Calor
                var conversionResult = converter.Convert(originalSource, csFile);

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
                    };
                    var compileResult = Compiler.Program.Compile(
                        conversionResult.CalorSource, csFile, compileOptions);

                    if (!compileResult.HasErrors && !string.IsNullOrWhiteSpace(compileResult.GeneratedCode))
                    {
                        // Step 3c: Post-process emitted C# for round-trip compatibility
                        var emitted = PostProcessEmittedCSharp(compileResult.GeneratedCode, originalSource);

                        // Step 3d: Verify emitted C# parses
                        var syntaxTree = CSharpSyntaxTree.ParseText(emitted);
                        var parseDiags = syntaxTree.GetDiagnostics()
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .ToList();

                        if (parseDiags.Count > 0)
                        {
                            result.Status = FileStatus.EmitSyntaxError;
                            result.Errors = parseDiags.Select(d => d.GetMessage()).ToList();
                        }
                        else
                        {
                            // Step 3e: Replace the file
                            await File.WriteAllTextAsync(csFile, emitted);
                            result.Status = FileStatus.Replaced;
                            result.EmittedCSharp = emitted;
                            convertedCount++;
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
            catch (Exception ex)
            {
                result.Status = FileStatus.Crashed;
                result.Errors = [ex.Message];
            }

            results.Add(result);

            if (convertedCount % 10 == 0 && convertedCount > 0)
                Console.Write(".");
        }
        Console.WriteLine();

        // Print status summary
        foreach (var group in results.GroupBy(r => r.Status).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        return results;
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
            Fidelity = ConversionFidelity.Lossy,
            GracefulFallback = true,
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
            });
        if (compileResult.HasErrors || string.IsNullOrWhiteSpace(compileResult.GeneratedCode))
            return null;

        var emitted = PostProcessEmittedCSharp(compileResult.GeneratedCode, originalSource);
        var parseDiags = CSharpSyntaxTree.ParseText(emitted).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);
        return parseDiags.Any() ? null : emitted;
    }

    /// <summary>
    /// When build fails, identify files mentioned in build errors, revert them
    /// to their originals, and update their status. Iterates up to 5 times.
    /// </summary>
    internal async Task<int> RecoverBuildAsync(
        string workDir, RoundTripConfig config, List<FileConversionResult> fileResults)
    {
        var totalReverted = 0;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var buildResult = await BuildProjectAsync(workDir, config);
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

                var original = await File.ReadAllTextAsync(originalPath);
                await File.WriteAllTextAsync(workPath, original);
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

    internal async Task<BuildResult> BuildProjectAsync(string workDir, RoundTripConfig config)
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
            config.DotnetPath, args, workDir, config.BuildTimeout);

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

        // Find regressions: passing in baseline, failing after round-trip.
        // Match on the robust Identity (assembly + executor + class + display name)
        // so duplicate display names across test assemblies never collide.
        var baselinePassedSet = baseline.Results
            .Where(t => t.Outcome == "Passed")
            .Select(t => t.Identity)
            .ToHashSet();

        var roundTripFailedSet = roundTrip.Results
            .Where(t => t.Outcome == "Failed")
            .Select(t => t.Identity)
            .ToHashSet();

        comparison.Regressions = baselinePassedSet
            .Intersect(roundTripFailedSet)
            .Select(id => roundTrip.Results.First(t => t.Identity == id))
            .ToList();

        // Pre-existing failures
        var baselineFailedSet = baseline.Results
            .Where(t => t.Outcome == "Failed")
            .Select(t => t.Identity)
            .ToHashSet();

        comparison.PreExistingFailures = baselineFailedSet.Count;

        // New passes: failing in baseline, passing in round-trip
        comparison.NewPasses = roundTrip.Results
            .Where(t => t.Outcome == "Passed" && baselineFailedSet.Contains(t.Identity))
            .Select(t => t.TestName)
            .ToList();

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
        List<FileConversionResult> convertedFiles)
    {
        var culprits = new Dictionary<string, List<string>>();
        var failingTestNames = regressions.Select(t => t.TestName).ToHashSet();
        var testFilter = string.Join("|", failingTestNames);

        foreach (var file in convertedFiles.Where(f => f.Status == FileStatus.Replaced && f.EmittedCSharp != null))
        {
            var fullPath = Path.Combine(workDir, file.FilePath);
            var emittedContent = await File.ReadAllTextAsync(fullPath);

            // Revert this one file to original
            var originalPath = Path.Combine(config.OriginalProjectPath, file.FilePath);
            if (!File.Exists(originalPath)) continue;
            var originalContent = await File.ReadAllTextAsync(originalPath);
            await File.WriteAllTextAsync(fullPath, originalContent);

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
                TestFilter = testFilter,
                TestTimeout = config.TestTimeout,
            };
            var result = await RunTestsAsync(workDir, bisectConfig);

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
            await File.WriteAllTextAsync(fullPath, emittedContent);
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
