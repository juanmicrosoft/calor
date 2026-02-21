using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for analyzing C# code convertibility to Calor without producing output.
/// Runs conversion in Interop mode and returns only stats/gaps.
/// </summary>
public sealed class AnalyzeConvertibilityTool : McpToolBase
{
    public override string Name => "calor_analyze_convertibility";

    public override string Description =>
        "Analyze how convertible C# source code is to Calor. Returns convertibility score, member counts, and gaps without generating Calor output.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "C# source code to analyze"
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
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        try
        {
            // Run conversion in Interop mode to detect what can and can't convert
            var options = new ConversionOptions
            {
                PreserveComments = false,
                AutoGenerateIds = true,
                GracefulFallback = true,
                Mode = ConversionMode.Interop
            };

            var converter = new CSharpToCalorConverter(options);
            var result = converter.Convert(source);

            var explanation = result.Context.GetExplanation();
            var stats = result.Context.Stats;

            var totalMembers = stats.MethodsConverted + stats.PropertiesConverted +
                               stats.FieldsConverted + stats.ClassesConverted +
                               stats.InterfacesConverted + stats.InteropBlocksEmitted;
            var convertibleMembers = totalMembers - stats.InteropBlocksEmitted;
            var score = totalMembers > 0
                ? Math.Round((double)convertibleMembers / totalMembers * 100, 1)
                : 100.0;

            var recommendation = score switch
            {
                100.0 => "Fully convertible. Use standard conversion mode.",
                >= 80.0 => "Mostly convertible. Use interop mode to preserve unsupported members.",
                >= 50.0 => "Partially convertible. Interop mode recommended; review preserved blocks.",
                _ => "Low convertibility. Consider manual migration or keeping as C#."
            };

            var gaps = explanation.UnsupportedFeatures
                .Select(kvp => new GapOutput
                {
                    Feature = kvp.Key,
                    Count = kvp.Value.Count,
                    Instances = kvp.Value.Select(i => new GapInstanceOutput
                    {
                        Line = i.Line,
                        Suggestion = i.Suggestion
                    }).ToList()
                }).ToList();

            var output = new AnalyzeOutput
            {
                ConvertibilityScore = score,
                TotalMembers = totalMembers,
                ConvertibleMembers = convertibleMembers,
                BlockedMembers = stats.InteropBlocksEmitted,
                Gaps = gaps,
                Recommendation = recommendation
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Analysis failed: {ex.Message}"));
        }
    }

    private sealed class AnalyzeOutput
    {
        [JsonPropertyName("convertibility_score")]
        public double ConvertibilityScore { get; init; }

        [JsonPropertyName("total_members")]
        public int TotalMembers { get; init; }

        [JsonPropertyName("convertible_members")]
        public int ConvertibleMembers { get; init; }

        [JsonPropertyName("blocked_members")]
        public int BlockedMembers { get; init; }

        [JsonPropertyName("gaps")]
        public required List<GapOutput> Gaps { get; init; }

        [JsonPropertyName("recommendation")]
        public required string Recommendation { get; init; }
    }

    private sealed class GapOutput
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("instances")]
        public required List<GapInstanceOutput> Instances { get; init; }
    }

    private sealed class GapInstanceOutput
    {
        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("suggestion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Suggestion { get; init; }
    }
}
