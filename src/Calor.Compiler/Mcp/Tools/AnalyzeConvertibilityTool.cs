using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for analyzing C# code convertibility to Calor.
/// </summary>
public sealed class AnalyzeConvertibilityTool : McpToolBase
{
    public override string Name => "calor_analyze_convertibility";

    public override string Description =>
        "Analyze how likely C# code is to successfully convert to Calor. " +
        "Combines static analysis of unsupported constructs with an actual conversion attempt " +
        "to produce a practical score (0-100) with blocker details.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "C# source code to analyze"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "quick": {
                            "type": "boolean",
                            "default": false,
                            "description": "Stage 1 only: static analysis without conversion attempt (faster)"
                        }
                    }
                }
            },
            "required": ["source"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: 'source'"));
        }

        var options = GetOptions(arguments);
        var quick = GetBool(options, "quick", false);

        try
        {
            var analyzer = new ConvertibilityAnalyzer();
            var result = quick
                ? analyzer.AnalyzeQuick(source)
                : analyzer.Analyze(source);

            var blockers = result.Blockers.Select(b => new ToolBlocker
            {
                Name = b.Name,
                Description = b.Description,
                Count = b.Count,
                Category = b.Category
            }).ToList();

            var output = new ToolOutput
            {
                Score = result.Score,
                Summary = result.Summary,
                ConversionAttempted = result.ConversionAttempted,
                ConversionSucceeded = result.ConversionSucceeded,
                CompilationSucceeded = result.CompilationSucceeded,
                ConversionRate = result.ConversionRate,
                Blockers = blockers,
                TotalBlockerInstances = result.TotalBlockerInstances,
                LanguageGapCount = blockers.Count(b => b.Category == "language_unsupported"),
                ConverterBugCount = blockers.Count(b => b.Category == "converter_not_implemented"),
                DurationMs = (int)result.Duration.TotalMilliseconds
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Analysis failed: {ex.Message}"));
        }
    }

    // Output DTOs
    private sealed class ToolOutput
    {
        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("summary")]
        public required string Summary { get; init; }

        [JsonPropertyName("conversionAttempted")]
        public bool ConversionAttempted { get; init; }

        [JsonPropertyName("conversionSucceeded")]
        public bool ConversionSucceeded { get; init; }

        [JsonPropertyName("compilationSucceeded")]
        public bool CompilationSucceeded { get; init; }

        [JsonPropertyName("conversionRate")]
        public double ConversionRate { get; init; }

        [JsonPropertyName("blockers")]
        public required List<ToolBlocker> Blockers { get; init; }

        [JsonPropertyName("totalBlockerInstances")]
        public int TotalBlockerInstances { get; init; }

        [JsonPropertyName("languageGapCount")]
        public int LanguageGapCount { get; init; }

        [JsonPropertyName("converterBugCount")]
        public int ConverterBugCount { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }
    }

    private sealed class ToolBlocker
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("category")]
        public required string Category { get; init; }
    }
}
