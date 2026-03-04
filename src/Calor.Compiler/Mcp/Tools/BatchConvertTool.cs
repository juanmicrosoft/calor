using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for batch/project-level C# to Calor conversion.
/// Wraps ProjectMigrator to convert an entire project in a single call.
/// </summary>
public sealed class BatchConvertTool : McpToolBase
{
    public override string Name => "calor_batch_convert";

    public override string Description =>
        "Convert an entire C# project to Calor in a single call. " +
        "Discovers .cs files, converts each to Calor, and writes output files. " +
        "Returns per-file results and a summary. " +
        "Module names are derived from C# namespace declarations. Use moduleNameOverride to force a specific namespace for all files. " +
        "IMPORTANT: If results contain §CSHARP interop blocks, check calor_syntax_lookup " +
        "or calor_feature_support — many C# constructs (foreach, switch, async, yield, structs, " +
        "events, operators, preprocessor directives) have native Calor equivalents.";

    public override McpToolAnnotations? Annotations => new() { DestructiveHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "projectPath": {
                    "type": "string",
                    "description": "Path to the C# project directory or .csproj file"
                },
                "mode": {
                    "type": "string",
                    "enum": ["standard", "interop"],
                    "description": "Conversion mode: 'standard' (default) or 'interop'"
                },
                "includeTests": {
                    "type": "boolean",
                    "description": "Include test files in the migration (default: true)"
                },
                "dryRun": {
                    "type": "boolean",
                    "description": "Preview what would be converted without writing files (default: false)"
                },
                "parallel": {
                    "type": "boolean",
                    "description": "Run conversions in parallel (default: true)"
                },
                "outputDirectory": {
                    "type": "string",
                    "description": "Directory to write converted .calr files (default: alongside source files)"
                },
                "maxFiles": {
                    "type": "integer",
                    "description": "Maximum number of files to convert in this batch (0 = no limit, default: 0)"
                },
                "offset": {
                    "type": "integer",
                    "description": "Number of files to skip before processing (for pagination with maxFiles)"
                },
                "directoryFilter": {
                    "type": "string",
                    "description": "Glob pattern to filter directories (e.g., 'src/**' to only include src/)"
                },
                "skipConverted": {
                    "type": "boolean",
                    "description": "Skip files that already have a corresponding .calr output file (default: false)"
                },
                "validate": {
                    "type": "boolean",
                    "description": "Validate each converted file by parsing and compiling the generated Calor (default: false). Catches false-positive successes where conversion output doesn't actually compile."
                },
                "moduleNameOverride": {
                    "type": "string",
                    "description": "Override the module name for all converted files. By default, module names are derived from C# namespace declarations."
                }
            },
            "required": ["projectPath"]
        ,

        "additionalProperties": false

        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
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
        var parallel = GetBool(arguments, "parallel", defaultValue: true);
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

            var report = dryRun
                ? await migrator.DryRunAsync(plan)
                : await migrator.ExecuteAsync(plan);

            var fileResults = report.FileResults.Select(f => new BatchFileResult
            {
                SourcePath = f.SourcePath,
                OutputPath = f.OutputPath,
                Status = f.Status.ToString().ToLowerInvariant(),
                DurationMs = (int)f.Duration.TotalMilliseconds,
                ErrorCount = f.Issues.Count(i => i.Severity == ConversionIssueSeverity.Error),
                WarningCount = f.Issues.Count(i => i.Severity == ConversionIssueSeverity.Warning),
                Issues = f.Issues
                    .Where(i => i.Severity is ConversionIssueSeverity.Error or ConversionIssueSeverity.Warning)
                    .Select(i => new BatchIssue
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
                Summary = new BatchSummary
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

    private sealed class BatchConvertOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; init; }

        [JsonPropertyName("summary")]
        public required BatchSummary Summary { get; init; }

        [JsonPropertyName("files")]
        public required List<BatchFileResult> Files { get; init; }

        [JsonPropertyName("recommendations")]
        public required List<string> Recommendations { get; init; }
    }

    private sealed class BatchSummary
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

    private sealed class BatchFileResult
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
        public List<BatchIssue>? Issues { get; init; }
    }

    private sealed class BatchIssue
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
}
