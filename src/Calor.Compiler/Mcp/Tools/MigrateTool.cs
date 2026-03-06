using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Analysis;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Verification.Z3.Cache;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for full project migration: assess → convert → compile → fix → report.
/// Orchestrates existing tools into a single pipeline call.
/// </summary>
public sealed class MigrateTool : McpToolBase
{
    private static readonly string[] ExcludedDirectories = ["bin", "obj", ".calor"];

    public override string Name => "calor_migrate";

    public override int TimeoutSeconds => 600;

    public override string Description =>
        "Full project migration pipeline: assess convertibility, convert C# to Calor, compile, auto-fix, and report results";

    public override McpToolAnnotations? Annotations => new() { DestructiveHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "projectPath": {
                    "type": "string",
                    "description": "Path to .csproj file or directory containing C# files"
                },
                "phase": {
                    "type": "string",
                    "enum": ["assess", "convert", "compile", "fix", "full"],
                    "description": "Which phase to run (default: full)",
                    "default": "full"
                },
                "maxFiles": {
                    "type": "integer",
                    "description": "Limit number of files to process"
                },
                "autoFix": {
                    "type": "boolean",
                    "description": "Whether to auto-fix common errors after conversion (default: true)",
                    "default": true
                }
            },
            "required": ["projectPath"],
            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var projectPath = GetString(arguments, "projectPath");
        if (string.IsNullOrEmpty(projectPath))
            return McpToolResult.Error("Missing required parameter: projectPath");

        var pathError = ValidatePath(projectPath, "projectPath");
        if (pathError != null) return pathError;

        var phase = GetString(arguments, "phase") ?? "full";
        var maxFiles = GetInt(arguments, "maxFiles", defaultValue: 0);
        var autoFix = GetBool(arguments, "autoFix", defaultValue: true);

        // Resolve directory from .csproj or directory path
        var directory = ResolveDirectory(projectPath);
        if (directory == null)
            return McpToolResult.Error($"Project path not found: {projectPath}");

        try
        {
            return phase switch
            {
                "assess" => RunAssess(directory, maxFiles, cancellationToken),
                "convert" => await RunConvertAsync(directory, projectPath, maxFiles, cancellationToken),
                "compile" => RunCompile(directory, maxFiles, cancellationToken),
                "fix" => await RunFixAsync(directory, maxFiles, cancellationToken),
                "full" => await RunFullAsync(directory, projectPath, maxFiles, autoFix, cancellationToken),
                _ => McpToolResult.Error($"Unknown phase: '{phase}'. Must be 'assess', 'convert', 'compile', 'fix', or 'full'.")
            };
        }
        catch (OperationCanceledException)
        {
            return McpToolResult.Error("Migration cancelled.");
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Migration failed: {ex.Message}");
        }
    }

    // ── Phase: assess ───────────────────────────────────────────────

    internal McpToolResult RunAssess(string directory, int maxFiles, CancellationToken ct)
    {
        var csFiles = DiscoverCsFiles(directory, maxFiles);
        if (csFiles.Count == 0)
            return McpToolResult.Error($"No .cs files found in: {directory}");

        var analyzer = new ConvertibilityAnalyzer();
        var perFile = new List<MigrateFileResult>();

        foreach (var file in csFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var source = File.ReadAllText(file);
                var result = analyzer.Analyze(source, file);
                var relativePath = Path.GetRelativePath(directory, file);

                perFile.Add(new MigrateFileResult
                {
                    Path = relativePath,
                    Status = result.Score >= 50 ? "convertible" : "blocked",
                    Errors = result.Blockers.Count > 0
                        ? result.Blockers.Select(b => $"{b.Name}: {b.Description} ({b.Count}x)").ToList()
                        : null,
                    Warnings = null,
                    Score = result.Score
                });
            }
            catch (Exception ex)
            {
                perFile.Add(new MigrateFileResult
                {
                    Path = Path.GetRelativePath(directory, file),
                    Status = "error",
                    Errors = [ex.Message]
                });
            }
        }

        return BuildOutput("assess", perFile);
    }

    // ── Phase: convert ──────────────────────────────────────────────

    internal async Task<McpToolResult> RunConvertAsync(string directory, string projectPath, int maxFiles, CancellationToken ct)
    {
        var options = new MigrationPlanOptions
        {
            IncludeTests = true,
            MaxFiles = maxFiles,
            PassthroughOnError = false
        };

        var migrator = new ProjectMigrator(options);
        ct.ThrowIfCancellationRequested();
        var plan = await migrator.CreatePlanAsync(projectPath, MigrationDirection.CSharpToCalor);
        ct.ThrowIfCancellationRequested();
        var report = await migrator.ExecuteAsync(plan);

        var perFile = report.FileResults.Select(f => new MigrateFileResult
        {
            Path = Path.GetRelativePath(directory, f.SourcePath),
            Status = f.Status switch
            {
                FileMigrationStatus.Success => "success",
                FileMigrationStatus.Partial => "partial",
                FileMigrationStatus.Skipped => "skipped",
                _ => "failed"
            },
            Errors = f.Issues
                .Where(i => i.Severity == ConversionIssueSeverity.Error)
                .Select(i => i.Message)
                .ToList() is { Count: > 0 } errs ? errs : null,
            Warnings = f.Issues
                .Where(i => i.Severity == ConversionIssueSeverity.Warning)
                .Select(i => i.Message)
                .ToList() is { Count: > 0 } warns ? warns : null
        }).ToList();

        return BuildOutput("convert", perFile);
    }

    // ── Phase: compile ──────────────────────────────────────────────

    internal McpToolResult RunCompile(string directory, int maxFiles, CancellationToken ct)
    {
        var calrFiles = DiscoverCalrFiles(directory, maxFiles);
        if (calrFiles.Count == 0)
            return McpToolResult.Error($"No .calr files found in: {directory}");

        var perFile = new List<MigrateFileResult>();

        foreach (var path in calrFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var source = File.ReadAllText(path);
                var compileOptions = new CompilationOptions
                {
                    ContractMode = ContractMode.Off,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive,
                    VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                    CancellationToken = ct
                };

                var compileResult = Program.Compile(source, path, compileOptions);
                var errors = compileResult.Diagnostics
                    .Where(d => d.IsError)
                    .Select(d => $"[{d.Code}] L{d.Span.Line}: {d.Message}")
                    .ToList();
                var warnings = compileResult.Diagnostics
                    .Where(d => !d.IsError)
                    .Select(d => $"[{d.Code}] L{d.Span.Line}: {d.Message}")
                    .ToList();

                perFile.Add(new MigrateFileResult
                {
                    Path = Path.GetRelativePath(directory, path),
                    Status = compileResult.HasErrors ? "failed" : "success",
                    Errors = errors.Count > 0 ? errors : null,
                    Warnings = warnings.Count > 0 ? warnings : null
                });
            }
            catch (Exception ex)
            {
                perFile.Add(new MigrateFileResult
                {
                    Path = Path.GetRelativePath(directory, path),
                    Status = "error",
                    Errors = [ex.Message]
                });
            }
        }

        return BuildOutput("compile", perFile);
    }

    // ── Phase: fix ──────────────────────────────────────────────────

    internal async Task<McpToolResult> RunFixAsync(string directory, int maxFiles, CancellationToken ct)
    {
        var calrFiles = DiscoverCalrFiles(directory, maxFiles);
        if (calrFiles.Count == 0)
            return McpToolResult.Error($"No .calr files found in: {directory}");

        var perFile = new List<MigrateFileResult>();

        foreach (var path in calrFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var source = await File.ReadAllTextAsync(path, ct);
                var (fixed1, fixes1) = FixTool.FixNewObject(source);
                var (fixed2, fixes2) = FixTool.FixArrowMultiStatement(fixed1);
                var (fixedFinal, fixes3) = FixTool.FixIdConflicts(fixed2);

                var totalFixes = fixes1.Count + fixes2.Count + fixes3.Count;

                if (totalFixes > 0)
                    await File.WriteAllTextAsync(path, fixedFinal, ct);

                perFile.Add(new MigrateFileResult
                {
                    Path = Path.GetRelativePath(directory, path),
                    Status = totalFixes > 0 ? "fixed" : "clean",
                    Warnings = totalFixes > 0
                        ? [$"{totalFixes} fix(es) applied"]
                        : null
                });
            }
            catch (Exception ex)
            {
                perFile.Add(new MigrateFileResult
                {
                    Path = Path.GetRelativePath(directory, path),
                    Status = "error",
                    Errors = [ex.Message]
                });
            }
        }

        return BuildOutput("fix", perFile);
    }

    // ── Phase: full ─────────────────────────────────────────────────

    internal async Task<McpToolResult> RunFullAsync(
        string directory, string projectPath, int maxFiles, bool autoFix, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var allPerFile = new Dictionary<string, MigrateFileResult>(StringComparer.OrdinalIgnoreCase);

        // Step 1: Assess
        var csFiles = DiscoverCsFiles(directory, maxFiles);
        if (csFiles.Count == 0)
            return McpToolResult.Error($"No .cs files found in: {directory}");

        var analyzer = new ConvertibilityAnalyzer();
        foreach (var file in csFiles)
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(directory, file);
            try
            {
                var source = File.ReadAllText(file);
                var result = analyzer.Analyze(source, file);
                allPerFile[relativePath] = new MigrateFileResult
                {
                    Path = relativePath,
                    Status = "assessed",
                    Score = result.Score
                };
            }
            catch
            {
                allPerFile[relativePath] = new MigrateFileResult
                {
                    Path = relativePath,
                    Status = "assess_failed"
                };
            }
        }

        // Step 2: Convert
        var options = new MigrationPlanOptions
        {
            IncludeTests = true,
            MaxFiles = maxFiles,
            PassthroughOnError = false
        };

        var migrator = new ProjectMigrator(options);
        ct.ThrowIfCancellationRequested();
        var plan = await migrator.CreatePlanAsync(projectPath, MigrationDirection.CSharpToCalor);
        ct.ThrowIfCancellationRequested();
        var report = await migrator.ExecuteAsync(plan);

        foreach (var f in report.FileResults)
        {
            var relativePath = Path.GetRelativePath(directory, f.SourcePath);
            var errors = f.Issues
                .Where(i => i.Severity == ConversionIssueSeverity.Error)
                .Select(i => i.Message)
                .ToList();
            var warnings = f.Issues
                .Where(i => i.Severity == ConversionIssueSeverity.Warning)
                .Select(i => i.Message)
                .ToList();

            var existing = allPerFile.GetValueOrDefault(relativePath);
            allPerFile[relativePath] = new MigrateFileResult
            {
                Path = relativePath,
                Status = f.Status switch
                {
                    FileMigrationStatus.Success => "converted",
                    FileMigrationStatus.Partial => "partial",
                    FileMigrationStatus.Skipped => "skipped",
                    _ => "convert_failed"
                },
                Score = existing?.Score,
                Errors = errors.Count > 0 ? errors : null,
                Warnings = warnings.Count > 0 ? warnings : null
            };
        }

        // Step 3: Compile converted .calr files
        var calrFiles = DiscoverCalrFiles(directory, maxFiles);
        foreach (var path in calrFiles)
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(directory, path);
            // Find corresponding source entry key (the .cs relative path)
            var sourceKey = allPerFile.Keys
                .FirstOrDefault(k => Path.ChangeExtension(k, ".calr") == relativePath);

            try
            {
                var source = File.ReadAllText(path);
                var compileOptions = new CompilationOptions
                {
                    ContractMode = ContractMode.Off,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive,
                    VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                    CancellationToken = ct
                };

                var compileResult = Program.Compile(source, path, compileOptions);
                if (compileResult.HasErrors)
                {
                    var key = sourceKey ?? relativePath;
                    var existing = allPerFile.GetValueOrDefault(key);
                    var compileErrors = compileResult.Diagnostics
                        .Where(d => d.IsError)
                        .Select(d => $"[{d.Code}] L{d.Span.Line}: {d.Message}")
                        .ToList();

                    allPerFile[key] = new MigrateFileResult
                    {
                        Path = existing?.Path ?? key,
                        Status = "compile_failed",
                        Score = existing?.Score,
                        Errors = compileErrors,
                        Warnings = existing?.Warnings
                    };
                }
                else if (sourceKey != null)
                {
                    var existing = allPerFile[sourceKey];
                    if (existing.Status is "converted" or "partial")
                    {
                        allPerFile[sourceKey] = new MigrateFileResult
                        {
                            Path = existing.Path,
                            Status = "compiled",
                            Score = existing.Score,
                            Errors = existing.Errors,
                            Warnings = existing.Warnings
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                var key = sourceKey ?? relativePath;
                var existing = allPerFile.GetValueOrDefault(key);
                allPerFile[key] = new MigrateFileResult
                {
                    Path = existing?.Path ?? key,
                    Status = "compile_error",
                    Score = existing?.Score,
                    Errors = [ex.Message]
                };
            }
        }

        // Step 4: Auto-fix files with compile errors, then re-compile
        if (autoFix)
        {
            var failedCalrFiles = calrFiles
                .Where(p =>
                {
                    var rel = Path.GetRelativePath(directory, p);
                    var sourceKey = allPerFile.Keys
                        .FirstOrDefault(k => Path.ChangeExtension(k, ".calr") == rel);
                    var key = sourceKey ?? rel;
                    return allPerFile.TryGetValue(key, out var r)
                           && r.Status is "compile_failed" or "compile_error";
                })
                .ToList();

            foreach (var path in failedCalrFiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var source = await File.ReadAllTextAsync(path, ct);
                    var (fixed1, fixes1) = FixTool.FixNewObject(source);
                    var (fixed2, fixes2) = FixTool.FixArrowMultiStatement(fixed1);
                    var (fixedFinal, fixes3) = FixTool.FixIdConflicts(fixed2);
                    var totalFixes = fixes1.Count + fixes2.Count + fixes3.Count;

                    if (totalFixes > 0)
                    {
                        await File.WriteAllTextAsync(path, fixedFinal, ct);

                        // Re-compile
                        var compileOptions = new CompilationOptions
                        {
                            ContractMode = ContractMode.Off,
                            UnknownCallPolicy = UnknownCallPolicy.Permissive,
                            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                            CancellationToken = ct
                        };
                        var recompile = Program.Compile(fixedFinal, path, compileOptions);

                        var relativePath = Path.GetRelativePath(directory, path);
                        var sourceKey = allPerFile.Keys
                            .FirstOrDefault(k => Path.ChangeExtension(k, ".calr") == relativePath);
                        var key = sourceKey ?? relativePath;
                        var existing = allPerFile.GetValueOrDefault(key);

                        allPerFile[key] = new MigrateFileResult
                        {
                            Path = existing?.Path ?? key,
                            Status = recompile.HasErrors ? "fix_incomplete" : "fixed",
                            Score = existing?.Score,
                            Errors = recompile.HasErrors
                                ? recompile.Diagnostics.Where(d => d.IsError)
                                    .Select(d => $"[{d.Code}] L{d.Span.Line}: {d.Message}")
                                    .ToList()
                                : null,
                            Warnings = [$"{totalFixes} fix(es) applied"]
                        };
                    }
                }
                catch
                {
                    // Leave status as-is on fix failure
                }
            }
        }

        sw.Stop();

        var perFile = allPerFile.Values.ToList();
        return BuildOutput("full", perFile, (int)sw.ElapsedMilliseconds);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    internal static string? ResolveDirectory(string projectPath)
    {
        if (projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(projectPath))
                return null;
            return Path.GetDirectoryName(projectPath);
        }

        return Directory.Exists(projectPath) ? projectPath : null;
    }

    internal static List<string> DiscoverCsFiles(string directory, int maxFiles)
    {
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ExcludedDirectories.Any(d =>
                f.Replace('\\', '/').Contains($"/{d}/", StringComparison.OrdinalIgnoreCase)));

        return maxFiles > 0 ? files.Take(maxFiles).ToList() : files.ToList();
    }

    internal static List<string> DiscoverCalrFiles(string directory, int maxFiles)
    {
        var files = Directory.EnumerateFiles(directory, "*.calr", SearchOption.AllDirectories)
            .Where(f => !ExcludedDirectories.Any(d =>
                f.Replace('\\', '/').Contains($"/{d}/", StringComparison.OrdinalIgnoreCase)));

        return maxFiles > 0 ? files.Take(maxFiles).ToList() : files.ToList();
    }

    private static McpToolResult BuildOutput(string phase, List<MigrateFileResult> perFile, int? durationMs = null)
    {
        var successCount = perFile.Count(f =>
            f.Status is "success" or "convertible" or "converted" or "compiled" or "fixed" or "clean");
        var failureCount = perFile.Count(f =>
            f.Status is "failed" or "error" or "blocked" or "convert_failed"
                or "compile_failed" or "compile_error" or "fix_incomplete" or "assess_failed");

        // Aggregate error categories
        var errorCategories = perFile
            .Where(f => f.Errors is { Count: > 0 })
            .SelectMany(f => f.Errors!)
            .GroupBy(e =>
            {
                var match = Regex.Match(e, @"^\[([^\]]+)\]");
                return match.Success ? match.Groups[1].Value : "general";
            })
            .ToDictionary(g => g.Key, g => g.Count());

        var topIssues = errorCategories
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => $"{kv.Key} ({kv.Value})")
            .ToList();

        var output = new MigrateOutput
        {
            Phase = phase,
            TotalFiles = perFile.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            PerFile = perFile,
            Summary = new MigrateSummary
            {
                ErrorCategories = errorCategories.Count > 0 ? errorCategories : null,
                TopIssues = topIssues.Count > 0 ? topIssues : null,
                DurationMs = durationMs
            }
        };

        return McpToolResult.Json(output, isError: failureCount > 0);
    }

    // ── DTOs ─────────────────────────────────────────────────────────

    internal sealed class MigrateOutput
    {
        [JsonPropertyName("phase")]
        public required string Phase { get; init; }

        [JsonPropertyName("totalFiles")]
        public int TotalFiles { get; init; }

        [JsonPropertyName("successCount")]
        public int SuccessCount { get; init; }

        [JsonPropertyName("failureCount")]
        public int FailureCount { get; init; }

        [JsonPropertyName("perFile")]
        public required List<MigrateFileResult> PerFile { get; init; }

        [JsonPropertyName("summary")]
        public required MigrateSummary Summary { get; init; }
    }

    internal sealed class MigrateFileResult
    {
        [JsonPropertyName("path")]
        public required string Path { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("score")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Score { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }

        [JsonPropertyName("warnings")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Warnings { get; init; }
    }

    internal sealed class MigrateSummary
    {
        [JsonPropertyName("errorCategories")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, int>? ErrorCategories { get; init; }

        [JsonPropertyName("topIssues")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? TopIssues { get; init; }

        [JsonPropertyName("durationMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DurationMs { get; init; }
    }
}
