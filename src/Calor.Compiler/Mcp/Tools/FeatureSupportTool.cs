using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for querying the Calor feature support registry.
/// Exposes feature support levels, descriptions, and workarounds to agents.
/// </summary>
public sealed class FeatureSupportTool : McpToolBase
{
    public override string Name => "calor_feature_support";

    public override string Description =>
        "Query the Calor feature support registry. Returns support levels, descriptions, and workarounds for C# features during migration.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "feature": {
                    "type": "string",
                    "description": "Query a specific feature by name (e.g., 'named-argument', 'with-expression')"
                },
                "supportLevel": {
                    "type": "string",
                    "enum": ["Full", "Partial", "NotSupported", "ManualRequired"],
                    "description": "List all features at a specific support level"
                }
            },

            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var feature = GetString(arguments, "feature");
        var supportLevelStr = GetString(arguments, "supportLevel");

        try
        {
            // Mode 1: Query a specific feature
            if (!string.IsNullOrEmpty(feature))
            {
                var info = FeatureSupport.GetFeatureInfo(feature);
                if (info == null)
                {
                    return Task.FromResult(McpToolResult.Text(
                        $"Feature '{feature}' not found in registry. Use calor_feature_support with no arguments for a summary of all features."));
                }

                var result = new FeatureQueryResult
                {
                    Feature = info.Name,
                    Support = info.Support.ToString(),
                    Description = info.Description,
                    Workaround = info.Workaround
                };

                return Task.FromResult(McpToolResult.Json(result));
            }

            // Mode 2: List features by support level
            if (!string.IsNullOrEmpty(supportLevelStr))
            {
                if (!Enum.TryParse<SupportLevel>(supportLevelStr, ignoreCase: true, out var level))
                {
                    return Task.FromResult(McpToolResult.Error(
                        $"Invalid support level '{supportLevelStr}'. Valid values: Full, Partial, NotSupported, ManualRequired"));
                }

                var features = FeatureSupport.GetFeaturesBySupport(level)
                    .Select(f => new FeatureListItem
                    {
                        Feature = f.Name,
                        Description = f.Description,
                        Workaround = f.Workaround
                    })
                    .OrderBy(f => f.Feature)
                    .ToList();

                var result = new FeatureListResult
                {
                    SupportLevel = level.ToString(),
                    Count = features.Count,
                    Features = features
                };

                return Task.FromResult(McpToolResult.Json(result));
            }

            // Mode 3: Summary (no arguments)
            var allFeatures = FeatureSupport.GetAllFeatures().ToList();
            var summary = new FeatureSummaryResult
            {
                TotalFeatures = allFeatures.Count,
                FullCount = allFeatures.Count(f => f.Support == SupportLevel.Full),
                PartialCount = allFeatures.Count(f => f.Support == SupportLevel.Partial),
                NotSupportedCount = allFeatures.Count(f => f.Support == SupportLevel.NotSupported),
                ManualRequiredCount = allFeatures.Count(f => f.Support == SupportLevel.ManualRequired),
                FullFeatures = allFeatures.Where(f => f.Support == SupportLevel.Full)
                    .Select(f => f.Name).OrderBy(n => n).ToList(),
                PartialFeatures = allFeatures.Where(f => f.Support == SupportLevel.Partial)
                    .Select(f => f.Name).OrderBy(n => n).ToList(),
                NotSupportedFeatures = allFeatures.Where(f => f.Support == SupportLevel.NotSupported)
                    .Select(f => f.Name).OrderBy(n => n).ToList(),
                ManualRequiredFeatures = allFeatures.Where(f => f.Support == SupportLevel.ManualRequired)
                    .Select(f => f.Name).OrderBy(n => n).ToList()
            };

            return Task.FromResult(McpToolResult.Json(summary));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to query feature support: {ex.Message}"));
        }
    }

    private sealed class FeatureQueryResult
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("support")]
        public required string Support { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; init; }

        [JsonPropertyName("workaround")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Workaround { get; init; }
    }

    private sealed class FeatureListResult
    {
        [JsonPropertyName("supportLevel")]
        public required string SupportLevel { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("features")]
        public required List<FeatureListItem> Features { get; init; }
    }

    private sealed class FeatureListItem
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; init; }

        [JsonPropertyName("workaround")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Workaround { get; init; }
    }

    private sealed class FeatureSummaryResult
    {
        [JsonPropertyName("totalFeatures")]
        public int TotalFeatures { get; init; }

        [JsonPropertyName("fullCount")]
        public int FullCount { get; init; }

        [JsonPropertyName("partialCount")]
        public int PartialCount { get; init; }

        [JsonPropertyName("notSupportedCount")]
        public int NotSupportedCount { get; init; }

        [JsonPropertyName("manualRequiredCount")]
        public int ManualRequiredCount { get; init; }

        [JsonPropertyName("fullFeatures")]
        public required List<string> FullFeatures { get; init; }

        [JsonPropertyName("partialFeatures")]
        public required List<string> PartialFeatures { get; init; }

        [JsonPropertyName("notSupportedFeatures")]
        public required List<string> NotSupportedFeatures { get; init; }

        [JsonPropertyName("manualRequiredFeatures")]
        public required List<string> ManualRequiredFeatures { get; init; }
    }
}
