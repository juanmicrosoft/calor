using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for migrating entire projects between C# and Calor.
/// Unlike other MCP tools that take inline source strings, this tool operates
/// on filesystem paths. Safety default: dryRun is true so agents don't write
/// files without explicit intent.
/// </summary>
public sealed class MigrateTool : McpToolBase
{
    public override string Name => "calor_migrate";

    public override string Description =>
        "Migrate an entire project between C# and Calor. " +
        "Runs a 4-phase workflow: Discover → Analyze → Convert → Verify. " +
        "Defaults to dry-run mode for safety.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Project directory or .csproj file to migrate"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "direction": {
                            "type": "string",
                            "enum": ["cs-to-calor", "calor-to-cs"],
                            "default": "cs-to-calor",
                            "description": "Migration direction (default: cs-to-calor)"
                        },
                        "dryRun": {
                            "type": "boolean",
                            "default": true,
                            "description": "Preview changes without writing files (default: true for MCP safety)"
                        },
                        "parallel": {
                            "type": "boolean",
                            "default": true,
                            "description": "Run conversions in parallel"
                        },
                        "includeBenchmark": {
                            "type": "boolean",
                            "default": false,
                            "description": "Include before/after metrics comparison"
                        },
                        "skipAnalyze": {
                            "type": "boolean",
                            "default": false,
                            "description": "Skip the migration analysis phase"
                        },
                        "skipVerify": {
                            "type": "boolean",
                            "default": false,
                            "description": "Skip the Z3 contract verification phase"
                        },
                        "verificationTimeoutMs": {
                            "type": "integer",
                            "default": 5000,
                            "minimum": 100,
                            "description": "Z3 verification timeout per contract in milliseconds (minimum: 100)"
                        },
                        "maxFileResults": {
                            "type": "integer",
                            "default": 200,
                            "minimum": 1,
                            "description": "Maximum number of per-file results to include in the response (default: 200)"
                        }
                    }
                }
            },
            "required": ["path"]
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var path = GetString(arguments, "path");
        if (string.IsNullOrEmpty(path))
        {
            return McpToolResult.Error("Missing required parameter: 'path'");
        }

        // Validate path exists
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return McpToolResult.Error($"Path not found: {path}");
        }

        var options = GetOptions(arguments);
        var directionStr = GetString(options, "direction") ?? "cs-to-calor";
        var dryRun = GetBool(options, "dryRun", defaultValue: true);
        var parallel = GetBool(options, "parallel", defaultValue: true);
        var includeBenchmark = GetBool(options, "includeBenchmark", defaultValue: false);
        var skipAnalyze = GetBool(options, "skipAnalyze", defaultValue: false);
        var skipVerify = GetBool(options, "skipVerify", defaultValue: false);
        var verificationTimeoutMs = Math.Max(100, GetInt(options, "verificationTimeoutMs", defaultValue: 5000));
        var maxFileResults = Math.Max(1, GetInt(options, "maxFileResults", defaultValue: 200));

        var direction = directionStr.ToLowerInvariant() switch
        {
            "calor-to-cs" or "calor-to-csharp" or "calor-to-c#" => MigrationDirection.CalorToCSharp,
            _ => MigrationDirection.CSharpToCalor
        };

        var sw = Stopwatch.StartNew();

        try
        {
            var planOptions = new MigrationPlanOptions
            {
                Parallel = parallel,
                IncludeBenchmark = includeBenchmark,
                SkipAnalyze = skipAnalyze,
                SkipVerify = skipVerify,
                VerificationTimeoutMs = (uint)verificationTimeoutMs
            };

            var migrator = new ProjectMigrator(planOptions);

            // Phase 1: Discover
            var plan = await migrator.CreatePlanAsync(path, direction);

            var planOutput = new PlanOutput
            {
                TotalFiles = plan.TotalFiles,
                ConvertibleFiles = plan.ConvertibleFiles,
                PartialFiles = plan.PartialFiles,
                SkippedFiles = plan.SkippedFiles,
                EstimatedIssues = plan.EstimatedIssues
            };

            if (plan.ConvertibleFiles == 0 && plan.PartialFiles == 0)
            {
                sw.Stop();
                var emptyOutput = new MigrateToolOutput
                {
                    Success = true,
                    DryRun = dryRun,
                    Plan = planOutput,
                    DurationMs = (int)sw.ElapsedMilliseconds
                };
                return McpToolResult.Json(emptyOutput);
            }

            // Phase 2: Analyze
            AnalysisOutput? analysisOutput = null;
            var shouldAnalyze = !skipAnalyze && direction == MigrationDirection.CSharpToCalor;

            if (shouldAnalyze)
            {
                var analysisSummary = await migrator.AnalyzeAsync(plan);
                analysisOutput = new AnalysisOutput
                {
                    FilesAnalyzed = analysisSummary.FilesAnalyzed,
                    AverageScore = Math.Round(analysisSummary.AverageScore, 1),
                    PriorityBreakdown = analysisSummary.PriorityBreakdown
                        .ToDictionary(kv => kv.Key.ToString().ToLowerInvariant(), kv => kv.Value)
                };
            }

            // Phase 3: Convert
            var report = dryRun
                ? await migrator.DryRunAsync(plan)
                : await migrator.ExecuteAsync(plan, dryRun: false);

            var summaryOutput = new SummaryOutput
            {
                TotalFiles = report.Summary.TotalFiles,
                SuccessfulFiles = report.Summary.SuccessfulFiles,
                PartialFiles = report.Summary.PartialFiles,
                FailedFiles = report.Summary.FailedFiles,
                TotalErrors = report.Summary.TotalErrors,
                TotalWarnings = report.Summary.TotalWarnings
            };

            // Phase 4: Verify
            VerificationOutput? verificationOutput = null;
            var shouldVerify = !skipVerify && !dryRun && direction == MigrationDirection.CSharpToCalor;

            if (shouldVerify)
            {
                var verificationSummary = await migrator.VerifyAsync(report, (uint)verificationTimeoutMs);
                if (verificationSummary.Z3Available)
                {
                    verificationOutput = new VerificationOutput
                    {
                        TotalContracts = verificationSummary.TotalContracts,
                        Proven = verificationSummary.Proven,
                        Unproven = verificationSummary.Unproven,
                        Disproven = verificationSummary.Disproven,
                        ProvenRate = Math.Round(verificationSummary.ProvenRate, 1)
                    };
                }
            }

            // Build file results (capped to prevent oversized MCP responses)
            var allFileResults = report.FileResults
                .Where(f => f.Status != FileMigrationStatus.Skipped)
                .ToList();
            var truncated = allFileResults.Count > maxFileResults;
            var fileResults = allFileResults
                .Take(maxFileResults)
                .Select(f => new FileResultOutput
                {
                    SourcePath = f.SourcePath,
                    OutputPath = f.OutputPath,
                    Status = f.Status.ToString().ToLowerInvariant(),
                    IssueCount = f.Issues.Count
                })
                .ToList();

            sw.Stop();

            var hasFailures = report.Summary.FailedFiles > 0;
            var output = new MigrateToolOutput
            {
                Success = !hasFailures,
                DryRun = dryRun,
                Plan = planOutput,
                Summary = summaryOutput,
                Analysis = analysisOutput,
                Verification = verificationOutput,
                FileResults = fileResults.Count > 0 ? fileResults : null,
                FileResultsTruncated = truncated ? true : null,
                TotalFileResultCount = truncated ? allFileResults.Count : null,
                DurationMs = (int)sw.ElapsedMilliseconds
            };

            return McpToolResult.Json(output, isError: hasFailures);
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Migration failed: {ex.Message}");
        }
    }

    // ── Output DTOs ──

    private sealed class MigrateToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; init; }

        [JsonPropertyName("plan")]
        public required PlanOutput Plan { get; init; }

        [JsonPropertyName("summary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SummaryOutput? Summary { get; init; }

        [JsonPropertyName("analysis")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AnalysisOutput? Analysis { get; init; }

        [JsonPropertyName("verification")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public VerificationOutput? Verification { get; init; }

        [JsonPropertyName("fileResults")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FileResultOutput>? FileResults { get; init; }

        [JsonPropertyName("fileResultsTruncated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? FileResultsTruncated { get; init; }

        [JsonPropertyName("totalFileResultCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TotalFileResultCount { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }
    }

    private sealed class PlanOutput
    {
        [JsonPropertyName("totalFiles")]
        public int TotalFiles { get; init; }

        [JsonPropertyName("convertibleFiles")]
        public int ConvertibleFiles { get; init; }

        [JsonPropertyName("partialFiles")]
        public int PartialFiles { get; init; }

        [JsonPropertyName("skippedFiles")]
        public int SkippedFiles { get; init; }

        [JsonPropertyName("estimatedIssues")]
        public int EstimatedIssues { get; init; }
    }

    private sealed class SummaryOutput
    {
        [JsonPropertyName("totalFiles")]
        public int TotalFiles { get; init; }

        [JsonPropertyName("successfulFiles")]
        public int SuccessfulFiles { get; init; }

        [JsonPropertyName("partialFiles")]
        public int PartialFiles { get; init; }

        [JsonPropertyName("failedFiles")]
        public int FailedFiles { get; init; }

        [JsonPropertyName("totalErrors")]
        public int TotalErrors { get; init; }

        [JsonPropertyName("totalWarnings")]
        public int TotalWarnings { get; init; }
    }

    private sealed class AnalysisOutput
    {
        [JsonPropertyName("filesAnalyzed")]
        public int FilesAnalyzed { get; init; }

        [JsonPropertyName("averageScore")]
        public double AverageScore { get; init; }

        [JsonPropertyName("priorityBreakdown")]
        public required Dictionary<string, int> PriorityBreakdown { get; init; }
    }

    private sealed class VerificationOutput
    {
        [JsonPropertyName("totalContracts")]
        public int TotalContracts { get; init; }

        [JsonPropertyName("proven")]
        public int Proven { get; init; }

        [JsonPropertyName("unproven")]
        public int Unproven { get; init; }

        [JsonPropertyName("disproven")]
        public int Disproven { get; init; }

        [JsonPropertyName("provenRate")]
        public double ProvenRate { get; init; }
    }

    private sealed class FileResultOutput
    {
        [JsonPropertyName("sourcePath")]
        public required string SourcePath { get; init; }

        [JsonPropertyName("outputPath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OutputPath { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("issueCount")]
        public int IssueCount { get; init; }
    }
}
