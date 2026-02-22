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

        // Step 1b: Restore dependencies in working copy
        Console.WriteLine("  Restoring dependencies...");
        var restoreResult = await RestoreProjectAsync(workDir, config);
        if (!restoreResult)
            Console.WriteLine("  WARNING: Restore returned non-zero exit code");

        // Step 2: Baseline tests
        Console.WriteLine("\nPhase 2/5: Running baseline tests...");
        TrxParser.CleanTrxFiles(workDir);
        report.Baseline = await RunTestsAsync(workDir, config);
        Console.WriteLine($"  Baseline: {report.Baseline.Passed}/{report.Baseline.TotalTests} passed, {report.Baseline.Failed} failed, {report.Baseline.Skipped} skipped");

        // Step 3: Convert & Replace
        Console.WriteLine("\nPhase 3/5: Converting library source files...");
        report.FileResults = await ConvertAndReplaceAsync(workDir, config);
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

    private string PrepareWorkingCopy(RoundTripConfig config)
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
            var destFile = Path.Combine(destination, Path.GetFileName(file));
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

    private async Task<bool> RestoreProjectAsync(string workDir, RoundTripConfig config)
    {
        var target = Path.Combine(workDir, config.SolutionOrProjectFile);
        var (exitCode, _, _) = await ProcessRunner.RunAsync(
            config.DotnetPath, $"restore \"{target}\"", workDir, TimeSpan.FromMinutes(5));
        return exitCode == 0;
    }

    private async Task<TestRunResult> RunTestsAsync(string workDir, RoundTripConfig config)
    {
        var target = config.SolutionOrProjectFile;
        var targetPath = Path.Combine(workDir, target);

        var args = $"test \"{targetPath}\" --logger \"trx;LogFileName=results.trx\" --logger \"console;verbosity=normal\"";
        if (config.TargetFramework != null)
            args += $" --framework {config.TargetFramework}";
        if (config.TestFilter != null)
            args += $" --filter \"{config.TestFilter}\"";

        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
            config.DotnetPath, args, workDir, config.TestTimeout);

        // Parse the TRX file for structured results
        var trxPath = TrxParser.FindTrxFile(workDir);
        var testResults = trxPath != null ? TrxParser.Parse(trxPath) : [];

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

    private async Task<List<FileConversionResult>> ConvertAndReplaceAsync(
        string workDir, RoundTripConfig config)
    {
        var results = new List<FileConversionResult>();
        var libDir = Path.Combine(workDir, config.LibrarySourceRelativePath);

        if (!Directory.Exists(libDir))
        {
            Console.Error.WriteLine($"  ERROR: Library source directory not found: {libDir}");
            return results;
        }

        var csFiles = Directory.GetFiles(libDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ShouldExclude(f, config.ExcludePatterns))
            .OrderBy(f => f)
            .ToList();

        Console.WriteLine($"  Found {csFiles.Count} C# files to convert");

        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
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
    /// When build fails, identify files mentioned in build errors, revert them
    /// to their originals, and update their status. Iterates up to 5 times.
    /// </summary>
    private async Task<int> RecoverBuildAsync(
        string workDir, RoundTripConfig config, List<FileConversionResult> fileResults)
    {
        var totalReverted = 0;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var buildResult = await BuildProjectAsync(workDir, config);
            if (buildResult.Succeeded) break;

            // Extract file paths from build error lines
            var errorFiles = new HashSet<string>();

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
                            errorFiles.Add(relativePath);
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
                fileResult.Status = FileStatus.CompileError;
                fileResult.Errors = [$"Reverted: build error in round-tripped output (recovery round {attempt + 1})"];
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
            // Pattern like **/AssemblyInfo.cs
            return path.EndsWith("/" + suffix) || path.EndsWith(suffix);
        }
        if (pattern.StartsWith("*."))
        {
            return path.EndsWith(pattern[1..]);
        }
        return path.Contains(pattern);
    }

    private async Task<BuildResult> BuildProjectAsync(string workDir, RoundTripConfig config)
    {
        var target = config.SolutionOrProjectFile;
        var targetPath = Path.Combine(workDir, target);

        var args = $"build \"{targetPath}\" ";
        if (config.TargetFramework != null)
            args += $" --framework {config.TargetFramework}";

        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
            config.DotnetPath, args, workDir, TimeSpan.FromMinutes(5));

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

        // Find regressions: passing in baseline, failing after round-trip
        var baselinePassedSet = baseline.Results
            .Where(t => t.Outcome == "Passed")
            .Select(t => t.TestName)
            .ToHashSet();

        var roundTripFailedSet = roundTrip.Results
            .Where(t => t.Outcome == "Failed")
            .Select(t => t.TestName)
            .ToHashSet();

        comparison.Regressions = baselinePassedSet
            .Intersect(roundTripFailedSet)
            .Select(name => roundTrip.Results.First(t => t.TestName == name))
            .ToList();

        // Pre-existing failures
        var baselineFailedSet = baseline.Results
            .Where(t => t.Outcome == "Failed")
            .Select(t => t.TestName)
            .ToHashSet();

        comparison.PreExistingFailures = baselineFailedSet.Count;

        // New passes: failing in baseline, passing in round-trip
        comparison.NewPasses = roundTrip.Results
            .Where(t => t.Outcome == "Passed" && baselineFailedSet.Contains(t.TestName))
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
