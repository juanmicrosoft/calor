using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Migration;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for converting C# source code to Calor.
/// </summary>
public sealed class ConvertTool : McpToolBase
{
    public override string Name => "calor_convert";

    public override string Description =>
        "Convert C# source code to Calor. Returns the generated Calor code and any conversion issues.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "C# source code to convert to Calor"
                },
                "moduleName": {
                    "type": "string",
                    "description": "Module name for the generated Calor code"
                },
                "fallback": {
                    "type": "boolean",
                    "description": "Enable graceful fallback for unsupported constructs (default: true)"
                },
                "explain": {
                    "type": "boolean",
                    "description": "Include detailed explanation of unsupported features in output (default: false)"
                },
                "mode": {
                    "type": "string",
                    "enum": ["standard", "interop"],
                    "description": "Conversion mode: 'standard' (default) produces TODO comments for unsupported code, 'interop' wraps unsupported members in §CSHARP{...}§/CSHARP blocks"
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

        var moduleName = GetString(arguments, "moduleName");
        var fallback = GetBool(arguments, "fallback", defaultValue: true);
        var explain = GetBool(arguments, "explain", defaultValue: false);
        var modeStr = GetString(arguments, "mode") ?? "standard";
        var mode = modeStr.Equals("interop", StringComparison.OrdinalIgnoreCase)
            ? ConversionMode.Interop : ConversionMode.Standard;

        try
        {
            var options = new ConversionOptions
            {
                ModuleName = moduleName,
                PreserveComments = true,
                AutoGenerateIds = true,
                GracefulFallback = fallback,
                Explain = explain,
                Mode = mode
            };

            var converter = new CSharpToCalorConverter(options);
            var result = converter.Convert(source);

            // Build explanation if requested
            ExplanationOutput? explanationOutput = null;
            if (explain)
            {
                var explanation = result.Context.GetExplanation();
                explanationOutput = new ExplanationOutput
                {
                    UnsupportedFeatures = explanation.UnsupportedFeatures
                        .Select(kvp => new UnsupportedFeatureOutput
                        {
                            Feature = kvp.Key,
                            Count = kvp.Value.Count,
                            Instances = kvp.Value.Select(i => new FeatureInstanceOutput
                            {
                                Code = i.Code,
                                Line = i.Line,
                                Suggestion = i.Suggestion
                            }).ToList()
                        }).ToList(),
                    TotalUnsupportedCount = explanation.TotalUnsupportedCount,
                    PartialFeatures = explanation.PartialFeatures,
                    ManualRequiredFeatures = explanation.ManualRequiredFeatures
                };
            }

            // Track unsupported features in telemetry
            if (CalorTelemetry.IsInitialized)
            {
                var telExplanation = result.Context.GetExplanation();
                if (telExplanation.TotalUnsupportedCount > 0)
                {
                    CalorTelemetry.Instance.TrackUnsupportedFeatures(
                        telExplanation.GetFeatureCounts(),
                        telExplanation.TotalUnsupportedCount);
                }
            }

            // Post-conversion validation: re-parse the generated Calor to catch invalid output
            var issues = result.Issues.Select(i => new ConversionIssueOutput
            {
                Severity = i.Severity.ToString().ToLowerInvariant(),
                Message = i.Message,
                Line = i.Line ?? 0,
                Column = i.Column ?? 0,
                Suggestion = i.Suggestion
            }).ToList();

            var success = result.Success;

            if (success && !string.IsNullOrWhiteSpace(result.CalorSource))
            {
                var parseResult = CalorSourceHelper.Parse(result.CalorSource, "converted-output.calr");
                if (!parseResult.IsSuccess)
                {
                    success = false;
                    foreach (var error in parseResult.Errors)
                    {
                        issues.Add(new ConversionIssueOutput
                        {
                            Severity = "error",
                            Message = $"Generated Calor failed to parse: {error}",
                            Line = 0,
                            Column = 0,
                            Suggestion = "The converter produced invalid Calor syntax. This is a converter bug — please report it."
                        });
                    }
                }
            }

            var output = new ConvertToolOutput
            {
                Success = success,
                CalorSource = result.CalorSource,
                Issues = issues,
                Stats = new ConversionStatsOutput
                {
                    ClassesConverted = result.Context.Stats.ClassesConverted,
                    InterfacesConverted = result.Context.Stats.InterfacesConverted,
                    MethodsConverted = result.Context.Stats.MethodsConverted,
                    PropertiesConverted = result.Context.Stats.PropertiesConverted,
                    FieldsConverted = result.Context.Stats.FieldsConverted,
                    InteropBlocksEmitted = result.Context.Stats.InteropBlocksEmitted,
                    DurationMs = (int)result.Duration.TotalMilliseconds
                },
                Explanation = explanationOutput
            };

            return Task.FromResult(McpToolResult.Json(output, isError: !success));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Conversion failed: {ex.Message}"));
        }
    }

    private sealed class ConvertToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("calorSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalorSource { get; init; }

        [JsonPropertyName("issues")]
        public required List<ConversionIssueOutput> Issues { get; init; }

        [JsonPropertyName("stats")]
        public required ConversionStatsOutput Stats { get; init; }

        [JsonPropertyName("explanation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ExplanationOutput? Explanation { get; init; }
    }

    private sealed class ExplanationOutput
    {
        [JsonPropertyName("unsupportedFeatures")]
        public required List<UnsupportedFeatureOutput> UnsupportedFeatures { get; init; }

        [JsonPropertyName("totalUnsupportedCount")]
        public int TotalUnsupportedCount { get; init; }

        [JsonPropertyName("partialFeatures")]
        public required List<string> PartialFeatures { get; init; }

        [JsonPropertyName("manualRequiredFeatures")]
        public required List<string> ManualRequiredFeatures { get; init; }
    }

    private sealed class UnsupportedFeatureOutput
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("instances")]
        public required List<FeatureInstanceOutput> Instances { get; init; }
    }

    private sealed class FeatureInstanceOutput
    {
        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("suggestion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Suggestion { get; init; }
    }

    private sealed class ConversionIssueOutput
    {
        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }

        [JsonPropertyName("suggestion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Suggestion { get; init; }
    }

    private sealed class ConversionStatsOutput
    {
        [JsonPropertyName("classesConverted")]
        public int ClassesConverted { get; init; }

        [JsonPropertyName("interfacesConverted")]
        public int InterfacesConverted { get; init; }

        [JsonPropertyName("methodsConverted")]
        public int MethodsConverted { get; init; }

        [JsonPropertyName("propertiesConverted")]
        public int PropertiesConverted { get; init; }

        [JsonPropertyName("fieldsConverted")]
        public int FieldsConverted { get; init; }

        [JsonPropertyName("interopBlocksEmitted")]
        public int InteropBlocksEmitted { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }
    }
}
