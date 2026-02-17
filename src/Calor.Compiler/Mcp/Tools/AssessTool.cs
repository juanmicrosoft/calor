using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for assessing C# code for Calor migration potential.
/// </summary>
public sealed class AssessTool : McpToolBase
{
    public override string Name => "calor_assess";

    public override string Description =>
        "Assess C# source code for Calor migration potential. Returns scores across 8 dimensions " +
        "(contracts, effects, null safety, error handling, pattern matching, API complexity, async, LINQ) " +
        "plus detection of unsupported C# constructs.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "C# source code to assess (single file mode)"
                },
                "files": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "path": { "type": "string", "description": "File path/name for identification" },
                            "source": { "type": "string", "description": "C# source code content" }
                        },
                        "required": ["path", "source"]
                    },
                    "description": "Multiple C# files to assess (multi-file mode)"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "threshold": {
                            "type": "integer",
                            "default": 0,
                            "description": "Minimum score (0-100) to include in results"
                        }
                    }
                }
            }
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filesElement = GetArray(arguments, "files");
        var options = GetOptions(arguments);
        var threshold = GetInt(options, "threshold", 0);

        // Validate input - need either source or files
        if (string.IsNullOrEmpty(source) && filesElement == null)
        {
            return Task.FromResult(McpToolResult.Error(
                "Missing required parameter: provide either 'source' (single file) or 'files' (multi-file)"));
        }

        try
        {
            var analyzer = new MigrationAnalyzer();
            var files = new List<AssessFileResult>();

            if (!string.IsNullOrEmpty(source))
            {
                // Single file mode
                var result = analyzer.AnalyzeSource(source, "input.cs", "input.cs");
                if (!result.WasSkipped && result.TotalScore >= threshold)
                {
                    files.Add(CreateFileResult(result));
                }
            }
            else if (filesElement != null)
            {
                // Multi-file mode
                foreach (var fileElement in filesElement.Value.EnumerateArray())
                {
                    var path = fileElement.TryGetProperty("path", out var pathProp)
                        ? pathProp.GetString() ?? "unknown.cs"
                        : "unknown.cs";
                    var fileSource = fileElement.TryGetProperty("source", out var sourceProp)
                        ? sourceProp.GetString() ?? ""
                        : "";

                    if (string.IsNullOrEmpty(fileSource)) continue;

                    var result = analyzer.AnalyzeSource(fileSource, path, path);
                    if (!result.WasSkipped && result.TotalScore >= threshold)
                    {
                        files.Add(CreateFileResult(result));
                    }
                }
            }

            // Sort by score descending
            files.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Calculate summary
            var summary = new AssessSummary
            {
                TotalFiles = files.Count,
                AverageScore = files.Count > 0 ? Math.Round(files.Average(f => f.Score), 1) : 0,
                PriorityBreakdown = new PriorityBreakdown
                {
                    Critical = files.Count(f => f.Priority == "critical"),
                    High = files.Count(f => f.Priority == "high"),
                    Medium = files.Count(f => f.Priority == "medium"),
                    Low = files.Count(f => f.Priority == "low")
                }
            };

            var output = new AssessToolOutput
            {
                Success = true,
                Summary = summary,
                Files = files
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Assessment failed: {ex.Message}"));
        }
    }

    private static AssessFileResult CreateFileResult(FileMigrationScore score)
    {
        var dimensions = new Dictionary<string, DimensionResult>();
        foreach (var (dimension, dimScore) in score.Dimensions)
        {
            dimensions[dimension.ToString()] = new DimensionResult
            {
                Score = (int)Math.Round(dimScore.RawScore),
                Patterns = dimScore.PatternCount
            };
        }

        var unsupported = score.UnsupportedConstructs.Select(c => new UnsupportedConstructResult
        {
            Name = c.Name,
            Count = c.Count,
            Description = c.Description
        }).ToList();

        return new AssessFileResult
        {
            Path = score.RelativePath,
            Score = (int)Math.Round(score.TotalScore),
            Priority = FileMigrationScore.GetPriorityLabel(score.Priority).ToLowerInvariant(),
            Dimensions = dimensions,
            UnsupportedConstructs = unsupported,
            LineCount = score.LineCount,
            MethodCount = score.MethodCount,
            TypeCount = score.TypeCount
        };
    }

    /// <summary>
    /// Helper to get an array property from arguments.
    /// </summary>
    private static JsonElement? GetArray(JsonElement? arguments, string propertyName)
    {
        if (arguments == null || arguments.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (arguments.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
            return prop;

        return null;
    }

    // Output DTOs
    private sealed class AssessToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("summary")]
        public required AssessSummary Summary { get; init; }

        [JsonPropertyName("files")]
        public required List<AssessFileResult> Files { get; init; }
    }

    private sealed class AssessSummary
    {
        [JsonPropertyName("totalFiles")]
        public int TotalFiles { get; init; }

        [JsonPropertyName("averageScore")]
        public double AverageScore { get; init; }

        [JsonPropertyName("priorityBreakdown")]
        public required PriorityBreakdown PriorityBreakdown { get; init; }
    }

    private sealed class PriorityBreakdown
    {
        [JsonPropertyName("critical")]
        public int Critical { get; init; }

        [JsonPropertyName("high")]
        public int High { get; init; }

        [JsonPropertyName("medium")]
        public int Medium { get; init; }

        [JsonPropertyName("low")]
        public int Low { get; init; }
    }

    private sealed class AssessFileResult
    {
        [JsonPropertyName("path")]
        public required string Path { get; init; }

        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("priority")]
        public required string Priority { get; init; }

        [JsonPropertyName("dimensions")]
        public required Dictionary<string, DimensionResult> Dimensions { get; init; }

        [JsonPropertyName("unsupportedConstructs")]
        public required List<UnsupportedConstructResult> UnsupportedConstructs { get; init; }

        [JsonPropertyName("lineCount")]
        public int LineCount { get; init; }

        [JsonPropertyName("methodCount")]
        public int MethodCount { get; init; }

        [JsonPropertyName("typeCount")]
        public int TypeCount { get; init; }
    }

    private sealed class DimensionResult
    {
        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("patterns")]
        public int Patterns { get; init; }
    }

    private sealed class UnsupportedConstructResult
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }
    }
}
