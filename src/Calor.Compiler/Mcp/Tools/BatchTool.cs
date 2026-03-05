using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for batch operations on C# projects: conversion to Calor or convertibility analysis.
/// Consolidates the former calor_batch_convert and calor_batch_analyze tools.
/// </summary>
public sealed class BatchTool : McpToolBase
{
    private static readonly string[] ExcludedDirectories = ["bin", "obj", ".calor"];

    public override string Name => "calor_batch";

    public override int TimeoutSeconds => 300;

    public override string Description =>
        "Batch operations on a C# project. " +
        "action='convert': Convert an entire C# project to Calor in a single call. " +
        "Discovers .cs files, converts each to Calor, and writes output files. " +
        "Module names are derived from C# namespace declarations. Use moduleNameOverride to force a specific namespace. " +
        "IMPORTANT: If results contain §CSHARP interop blocks, check calor_syntax_lookup or calor_feature_support. " +
        "action='analyze': Analyze convertibility of an entire C# project directory. " +
        "Returns aggregate scores, tier breakdowns, and top blockers across the project.";

    public override McpToolAnnotations? Annotations => new() { DestructiveHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["convert", "analyze"],
                    "description": "Batch operation to perform",
                    "default": "convert"
                },
                "projectPath": {
                    "type": "string",
                    "description": "Path to the C# project directory or .csproj file"
                },
                "maxFiles": {
                    "type": "integer",
                    "description": "Maximum number of files to process (convert default: 0 = no limit, analyze default: 500)"
                },
                "parallel": {
                    "type": "boolean",
                    "description": "Run conversions in parallel (default: false to limit memory usage, convert only)"
                },
                "outputDirectory": {
                    "type": "string",
                    "description": "Directory to write converted .calr files (default: alongside source files, convert only)"
                },
                "quick": {
                    "type": "boolean",
                    "default": false,
                    "description": "Use quick analysis only — stage 1 static analysis, no conversion attempt (analyze only)"
                },
                "mode": {
                    "type": "string",
                    "enum": ["standard", "interop"],
                    "description": "Conversion mode: 'standard' (default) or 'interop' (convert only)"
                },
                "validate": {
                    "type": "boolean",
                    "description": "Validate each converted file by parsing and compiling the generated Calor (default: false, convert only)"
                },
                "excludePatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Glob-like patterns for files to exclude (e.g., [\"**/Emit/**\", \"**/DynamicProxy*\"]). Also reads patterns from .calor-ignore file if present. (analyze only)"
                },
                "includeTests": {
                    "type": "boolean",
                    "description": "Include test files in the migration (default: true, convert only)"
                },
                "dryRun": {
                    "type": "boolean",
                    "description": "Preview what would be converted without writing files (default: false, convert only)"
                },
                "offset": {
                    "type": "integer",
                    "description": "Number of files to skip before processing — for pagination with maxFiles (convert only)"
                },
                "directoryFilter": {
                    "type": "string",
                    "description": "Glob pattern to filter directories (e.g., 'src/**' to only include src/, convert only)"
                },
                "skipConverted": {
                    "type": "boolean",
                    "description": "Skip files that already have a corresponding .calr output file (default: false, convert only)"
                },
                "moduleNameOverride": {
                    "type": "string",
                    "description": "Override the module name for all converted files (convert only)"
                }
            },
            "required": ["projectPath"],
            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var action = GetString(arguments, "action") ?? "convert";

        return action switch
        {
            "convert" => await HandleConvertAsync(arguments, cancellationToken),
            "analyze" => HandleAnalyze(arguments, cancellationToken),
            _ => McpToolResult.Error($"Unknown action: '{action}'. Must be 'convert' or 'analyze'.")
        };
    }

    private async Task<McpToolResult> HandleConvertAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectPath = GetString(arguments, "projectPath");
        if (string.IsNullOrEmpty(projectPath))
        {
            return McpToolResult.Error("Missing required parameter: projectPath");
        }

        if (!Directory.Exists(projectPath) && !File.Exists(projectPath))
        {
            return McpToolResult.Error($"Project path not found: {projectPath}");
        }

        var includeTests = GetBool(arguments, "includeTests", defaultValue: true);
        var dryRun = GetBool(arguments, "dryRun", defaultValue: false);
        var parallel = GetBool(arguments, "parallel", defaultValue: false);
        var outputDirectory = GetString(arguments, "outputDirectory");
        var maxFiles = GetInt(arguments, "maxFiles", defaultValue: 0);
        var offset = GetInt(arguments, "offset", defaultValue: 0);
        var directoryFilter = GetString(arguments, "directoryFilter");
        var skipConverted = GetBool(arguments, "skipConverted", defaultValue: false);
        var validate = GetBool(arguments, "validate", defaultValue: false);
        var moduleNameOverride = GetString(arguments, "moduleNameOverride");

        try
        {
            var options = new MigrationPlanOptions
            {
                IncludeTests = includeTests,
                Parallel = parallel,
                MaxFiles = maxFiles,
                Offset = offset,
                DirectoryFilter = directoryFilter,
                SkipConverted = skipConverted,
                ValidateOutput = validate,
                ModuleNameOverride = moduleNameOverride
            };

            var migrator = new ProjectMigrator(options);
            cancellationToken.ThrowIfCancellationRequested();
            var plan = await migrator.CreatePlanAsync(projectPath, MigrationDirection.CSharpToCalor);

            // Override output directory if specified by rebuilding entries with new output paths
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var updatedEntries = plan.Entries.Select(entry =>
                {
                    var relativePath = Path.GetRelativePath(plan.ProjectPath, entry.SourcePath);
                    var calorPath = Path.ChangeExtension(relativePath, ".calr");
                    var newOutputPath = Path.Combine(outputDirectory, calorPath);

                    // Ensure subdirectories exist
                    var dir = Path.GetDirectoryName(newOutputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    return new MigrationPlanEntry
                    {
                        SourcePath = entry.SourcePath,
                        OutputPath = newOutputPath,
                        Convertibility = entry.Convertibility,
                        DetectedFeatures = entry.DetectedFeatures,
                        PotentialIssues = entry.PotentialIssues,
                        EstimatedIssues = entry.EstimatedIssues,
                        FileSizeBytes = entry.FileSizeBytes,
                        SkipReason = entry.SkipReason,
                        AnalysisScore = entry.AnalysisScore
                    };
                }).ToList();

                plan = new MigrationPlan
                {
                    ProjectPath = plan.ProjectPath,
                    Direction = plan.Direction,
                    Entries = updatedEntries,
                    Options = plan.Options
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            var report = dryRun
                ? await migrator.DryRunAsync(plan)
                : await migrator.ExecuteAsync(plan);

            var fileResults = report.FileResults.Select(f => new ConvertFileResult
            {
                SourcePath = f.SourcePath,
                OutputPath = f.OutputPath,
                Status = f.Status.ToString().ToLowerInvariant(),
                DurationMs = (int)f.Duration.TotalMilliseconds,
                ErrorCount = f.Issues.Count(i => i.Severity == ConversionIssueSeverity.Error),
                WarningCount = f.Issues.Count(i => i.Severity == ConversionIssueSeverity.Warning),
                Issues = f.Issues
                    .Where(i => i.Severity is ConversionIssueSeverity.Error or ConversionIssueSeverity.Warning)
                    .Select(i => new ConvertIssue
                    {
                        Severity = i.Severity == ConversionIssueSeverity.Error ? "error" : "warning",
                        Message = i.Message,
                        Line = i.Line,
                        Column = i.Column,
                        Category = i.Feature,
                        Suggestion = i.Suggestion
                    })
                    .ToList()
            }).ToList();

            // Add feature hint recommendation if any files had interop blocks
            var recommendations = report.Recommendations.ToList();
            var hasInteropBlocks = report.Summary.PartialFiles > 0;
            if (hasInteropBlocks)
            {
                recommendations.Add(
                    "Some files contain §CSHARP interop blocks. Many C# constructs have native Calor equivalents " +
                    "(foreach, switch, async/await, yield, structs, delegates, events, operators, preprocessor directives). " +
                    "Use calor_syntax_lookup or calor_feature_support to check before leaving code in §CSHARP blocks.");
            }

            var output = new BatchConvertOutput
            {
                Success = report.Summary.FailedFiles == 0,
                DryRun = dryRun,
                Summary = new ConvertSummary
                {
                    TotalFiles = report.Summary.TotalFiles,
                    SuccessfulFiles = report.Summary.SuccessfulFiles,
                    PartialFiles = report.Summary.PartialFiles,
                    FailedFiles = report.Summary.FailedFiles,
                    SuccessRate = report.Summary.SuccessRate,
                    TotalDurationMs = (int)report.Summary.TotalDuration.TotalMilliseconds
                },
                Files = fileResults,
                Recommendations = recommendations
            };

            return McpToolResult.Json(output, isError: report.Summary.FailedFiles > 0);
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Batch conversion failed: {ex.Message}");
        }
    }

    private McpToolResult HandleAnalyze(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectPath = GetString(arguments, "projectPath");
        if (string.IsNullOrEmpty(projectPath))
        {
            return McpToolResult.Error("Missing required parameter: 'projectPath'");
        }

        var quick = GetBool(arguments, "quick", false);
        var maxFiles = GetInt(arguments, "maxFiles", 500);
        var excludePatterns = GetStringArray(arguments, "excludePatterns");

        try
        {
            // If projectPath is a .csproj file, use its directory
            var directory = projectPath;
            if (projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(projectPath))
                {
                    return McpToolResult.Error($"Project file not found: {projectPath}");
                }
                directory = Path.GetDirectoryName(projectPath)!;
            }

            if (!Directory.Exists(directory))
            {
                return McpToolResult.Error($"Directory not found: {directory}");
            }

            var sw = Stopwatch.StartNew();

            // Load exclude patterns from .calor-ignore file if present
            var calorIgnorePath = Path.Combine(directory, ".calor-ignore");
            if (File.Exists(calorIgnorePath))
            {
                foreach (var line in File.ReadAllLines(calorIgnorePath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                        excludePatterns.Add(trimmed);
                }
            }

            // Find all .cs files, excluding bin/, obj/, .calor/ directories
            var csFiles = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !ExcludedDirectories.Any(d =>
                    f.Replace('\\', '/').Contains($"/{d}/", StringComparison.OrdinalIgnoreCase)))
                .Take(maxFiles)
                .ToList();

            // Apply exclude patterns
            var excludedCount = 0;
            if (excludePatterns.Count > 0)
            {
                var included = new List<string>();
                foreach (var file in csFiles)
                {
                    var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
                    var fileName = Path.GetFileName(file);
                    var excluded = excludePatterns.Any(pattern =>
                        relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase));

                    if (excluded)
                        excludedCount++;
                    else
                        included.Add(file);
                }
                csFiles = included;
            }

            if (csFiles.Count == 0)
            {
                return McpToolResult.Error($"No .cs files found in: {directory}");
            }

            var analyzer = new ConvertibilityAnalyzer();
            var fileResults = new List<AnalyzeFileResult>();
            var blockerAggregation = new Dictionary<string, BlockerAggregation>(StringComparer.Ordinal);

            foreach (var file in csFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = File.ReadAllText(file);
                var result = quick
                    ? analyzer.AnalyzeQuick(source, file)
                    : analyzer.Analyze(source, file);

                var relativePath = Path.GetRelativePath(directory, file);
                fileResults.Add(new AnalyzeFileResult
                {
                    Path = relativePath,
                    Score = result.Score,
                    BlockerCount = result.Blockers.Count
                });

                // Aggregate blockers
                foreach (var blocker in result.Blockers)
                {
                    if (!blockerAggregation.TryGetValue(blocker.Name, out var agg))
                    {
                        agg = new BlockerAggregation
                        {
                            Name = blocker.Name,
                            Description = blocker.Description
                        };
                        blockerAggregation[blocker.Name] = agg;
                    }
                    agg.FileCount++;
                    agg.TotalInstances += blocker.Count;
                }
            }

            sw.Stop();

            // Compute weighted average score by file size
            var totalSize = 0L;
            var weightedSum = 0.0;
            for (var i = 0; i < csFiles.Count; i++)
            {
                var size = new FileInfo(csFiles[i]).Length;
                totalSize += size;
                weightedSum += fileResults[i].Score * size;
            }
            var overallScore = totalSize > 0 ? weightedSum / totalSize : 0.0;

            // Top blockers sorted by file count descending
            var topBlockers = blockerAggregation.Values
                .OrderByDescending(b => b.FileCount)
                .ThenByDescending(b => b.TotalInstances)
                .Take(10)
                .Select(b => new TopBlockerEntry
                {
                    Name = b.Name,
                    Description = b.Description,
                    FileCount = b.FileCount,
                    TotalInstances = b.TotalInstances
                })
                .ToList();

            var output = new BatchAnalyzeOutput
            {
                TotalFiles = fileResults.Count,
                ExcludedFiles = excludedCount,
                OverallScore = Math.Round(overallScore, 1),
                Tiers = new TierCounts
                {
                    Tier1Clean = fileResults.Count(f => f.Score >= 80),
                    Tier2MinorIssues = fileResults.Count(f => f.Score >= 50 && f.Score < 80),
                    Tier3MajorBlockers = fileResults.Count(f => f.Score < 50)
                },
                TopBlockers = topBlockers,
                Files = fileResults.OrderByDescending(f => f.Score).ToList(),
                DurationMs = (int)sw.ElapsedMilliseconds
            };

            return McpToolResult.Json(output);
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Batch analysis failed: {ex.Message}");
        }
    }

    #region Convert DTOs

    private sealed class BatchConvertOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; init; }

        [JsonPropertyName("summary")]
        public required ConvertSummary Summary { get; init; }

        [JsonPropertyName("files")]
        public required List<ConvertFileResult> Files { get; init; }

        [JsonPropertyName("recommendations")]
        public required List<string> Recommendations { get; init; }
    }

    private sealed class ConvertSummary
    {
        [JsonPropertyName("totalFiles")]
        public int TotalFiles { get; init; }

        [JsonPropertyName("successfulFiles")]
        public int SuccessfulFiles { get; init; }

        [JsonPropertyName("partialFiles")]
        public int PartialFiles { get; init; }

        [JsonPropertyName("failedFiles")]
        public int FailedFiles { get; init; }

        [JsonPropertyName("successRate")]
        public double SuccessRate { get; init; }

        [JsonPropertyName("totalDurationMs")]
        public int TotalDurationMs { get; init; }
    }

    private sealed class ConvertFileResult
    {
        [JsonPropertyName("sourcePath")]
        public required string SourcePath { get; init; }

        [JsonPropertyName("outputPath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OutputPath { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; init; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; init; }

        [JsonPropertyName("issues")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ConvertIssue>? Issues { get; init; }
    }

    private sealed class ConvertIssue
    {
        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("line")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Line { get; init; }

        [JsonPropertyName("column")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Column { get; init; }

        [JsonPropertyName("category")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Category { get; init; }

        [JsonPropertyName("suggestion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Suggestion { get; init; }
    }

    #endregion

    #region Analyze DTOs

    private sealed class BlockerAggregation
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public int FileCount { get; set; }
        public int TotalInstances { get; set; }
    }

    private sealed class BatchAnalyzeOutput
    {
        [JsonPropertyName("totalFiles")]
        public int TotalFiles { get; init; }

        [JsonPropertyName("excludedFiles")]
        public int ExcludedFiles { get; init; }

        [JsonPropertyName("overallScore")]
        public double OverallScore { get; init; }

        [JsonPropertyName("tiers")]
        public required TierCounts Tiers { get; init; }

        [JsonPropertyName("topBlockers")]
        public required List<TopBlockerEntry> TopBlockers { get; init; }

        [JsonPropertyName("files")]
        public required List<AnalyzeFileResult> Files { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }
    }

    private sealed class TierCounts
    {
        [JsonPropertyName("tier1_clean")]
        public int Tier1Clean { get; init; }

        [JsonPropertyName("tier2_minor_issues")]
        public int Tier2MinorIssues { get; init; }

        [JsonPropertyName("tier3_major_blockers")]
        public int Tier3MajorBlockers { get; init; }
    }

    private sealed class TopBlockerEntry
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("fileCount")]
        public int FileCount { get; init; }

        [JsonPropertyName("totalInstances")]
        public int TotalInstances { get; init; }
    }

    private sealed class AnalyzeFileResult
    {
        [JsonPropertyName("path")]
        public required string Path { get; init; }

        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("blockerCount")]
        public int BlockerCount { get; init; }
    }

    #endregion
}
