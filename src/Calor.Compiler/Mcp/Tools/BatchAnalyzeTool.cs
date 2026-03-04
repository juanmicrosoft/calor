using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for analyzing convertibility of an entire C# project directory.
/// Discovers .cs files and runs ConvertibilityAnalyzer on each, returning aggregate results.
/// </summary>
public sealed class BatchAnalyzeTool : McpToolBase
{
    private static readonly string[] ExcludedDirectories = ["bin", "obj", ".calor"];

    public override string Name => "calor_batch_analyze";

    public override int TimeoutSeconds => 300;

    public override string Description =>
        "Analyze convertibility of an entire C# project directory to Calor. " +
        "Discovers .cs files, runs convertibility analysis on each, and returns " +
        "aggregate scores, tier breakdowns, and top blockers across the project.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "projectPath": {
                    "type": "string",
                    "description": "Path to C# project directory or .csproj file"
                },
                "quick": {
                    "type": "boolean",
                    "default": false,
                    "description": "Use quick analysis only (stage 1 static analysis, no conversion attempt)"
                },
                "maxFiles": {
                    "type": "integer",
                    "default": 500,
                    "description": "Maximum number of files to analyze"
                },
                "excludePatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Glob-like patterns for files to exclude (e.g., [\"**/Emit/**\", \"**/DynamicProxy*\"]). A file matches if its relative path contains the pattern (case-insensitive) or filename ends with it. Also reads patterns from .calor-ignore file if present."
                }
            },
            "required": ["projectPath"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var projectPath = GetString(arguments, "projectPath");
        if (string.IsNullOrEmpty(projectPath))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: 'projectPath'"));
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
                    return Task.FromResult(McpToolResult.Error($"Project file not found: {projectPath}"));
                }
                directory = Path.GetDirectoryName(projectPath)!;
            }

            if (!Directory.Exists(directory))
            {
                return Task.FromResult(McpToolResult.Error($"Directory not found: {directory}"));
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
                return Task.FromResult(McpToolResult.Error($"No .cs files found in: {directory}"));
            }

            var analyzer = new ConvertibilityAnalyzer();
            var fileResults = new List<FileAnalysisResult>();
            var blockerAggregation = new Dictionary<string, BlockerAggregation>(StringComparer.Ordinal);

            foreach (var file in csFiles)
            {
                var source = File.ReadAllText(file);
                var result = quick
                    ? analyzer.AnalyzeQuick(source, file)
                    : analyzer.Analyze(source, file);

                var relativePath = Path.GetRelativePath(directory, file);
                fileResults.Add(new FileAnalysisResult
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

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Batch analysis failed: {ex.Message}"));
        }
    }

    // Helper for aggregating blockers across files
    private sealed class BlockerAggregation
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public int FileCount { get; set; }
        public int TotalInstances { get; set; }
    }

    // Output DTOs
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
        public required List<FileAnalysisResult> Files { get; init; }

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

    private sealed class FileAnalysisResult
    {
        [JsonPropertyName("path")]
        public required string Path { get; init; }

        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("blockerCount")]
        public int BlockerCount { get; init; }
    }
}
