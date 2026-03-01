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
        "Convert C# source code to Calor. Returns generated Calor code and conversion issues. " +
        "IMPORTANT: If the result contains §CSHARP interop blocks, check calor_syntax_lookup " +
        "or calor_feature_support — many C# constructs (foreach, switch, async, yield, structs, " +
        "events, operators, preprocessor directives) have native Calor equivalents.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "C# source code to convert to Calor (required unless inputPath is provided)"
                },
                "inputPath": {
                    "type": "string",
                    "description": "Path to a C# file to convert (alternative to source for large files)"
                },
                "outputPath": {
                    "type": "string",
                    "description": "Path to write the generated Calor output file (optional)"
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
            }
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var inputPath = GetString(arguments, "inputPath");
        var outputPath = GetString(arguments, "outputPath");

        // Resolve source from file if inputPath provided
        if (!string.IsNullOrEmpty(inputPath))
        {
            if (!File.Exists(inputPath))
            {
                return Task.FromResult(McpToolResult.Error($"Input file not found: {inputPath}"));
            }

            try
            {
                source = File.ReadAllText(inputPath);
            }
            catch (Exception ex)
            {
                return Task.FromResult(McpToolResult.Error($"Failed to read input file: {ex.Message}"));
            }
        }

        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Either 'source' or 'inputPath' must be provided"));
        }

        var moduleName = GetString(arguments, "moduleName");
        // Derive module name from filename when not specified
        if (string.IsNullOrEmpty(moduleName) && !string.IsNullOrEmpty(inputPath))
        {
            moduleName = Path.GetFileNameWithoutExtension(inputPath);
        }
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

            // Always compute explanation for unsupported feature summary
            var explanation = result.Context.GetExplanation();
            var unsupportedFeatureCount = explanation.TotalUnsupportedCount;
            var unsupportedFeatureSummary = explanation.UnsupportedFeatures.Count > 0
                ? explanation.UnsupportedFeatures
                    .Select(kvp => $"{kvp.Key} ({kvp.Value.Count})")
                    .OrderBy(s => s)
                    .ToList()
                : null;

            // Build detailed explanation if requested
            ExplanationOutput? explanationOutput = null;
            if (explain)
            {
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
                if (explanation.TotalUnsupportedCount > 0)
                {
                    CalorTelemetry.Instance.TrackUnsupportedFeatures(
                        explanation.GetFeatureCounts(),
                        explanation.TotalUnsupportedCount);
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
            var calorSourceForOutput = result.CalorSource;

            if (success && !string.IsNullOrWhiteSpace(calorSourceForOutput))
            {
                var parseResult = CalorSourceHelper.Parse(calorSourceForOutput, "converted-output.calr");
                if (!parseResult.IsSuccess)
                {
                    // Attempt auto-fix before reporting errors
                    var fixer = new PostConversionFixer();
                    var fixResult = fixer.Fix(calorSourceForOutput);

                    if (fixResult.WasModified)
                    {
                        var retryParse = CalorSourceHelper.Parse(fixResult.FixedSource, "converted-output.calr");
                        if (retryParse.IsSuccess)
                        {
                            // Auto-fix succeeded — use fixed source
                            calorSourceForOutput = fixResult.FixedSource;
                            foreach (var fix in fixResult.AppliedFixes)
                            {
                                issues.Add(new ConversionIssueOutput
                                {
                                    Severity = "info",
                                    Message = $"Auto-fixed: {fix.Description} (rule: {fix.Rule})",
                                    Line = 0,
                                    Column = 0,
                                    Suggestion = null
                                });
                            }
                        }
                        else
                        {
                            // Auto-fix didn't fully resolve — report original errors
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
                    else
                    {
                        // No fixes applicable — report original errors
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
            }

            // Write output file if requested
            if (!string.IsNullOrEmpty(outputPath) && success && !string.IsNullOrWhiteSpace(calorSourceForOutput))
            {
                try
                {
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }
                    File.WriteAllText(outputPath, calorSourceForOutput);
                }
                catch (Exception ex)
                {
                    issues.Add(new ConversionIssueOutput
                    {
                        Severity = "warning",
                        Message = $"Failed to write output file: {ex.Message}",
                        Line = 0,
                        Column = 0,
                        Suggestion = null
                    });
                }
            }

            // Analyze interop blocks for features that have native Calor equivalents
            var featureHints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSourceForOutput);

            var output = new ConvertToolOutput
            {
                Success = success,
                CalorSource = calorSourceForOutput,
                OutputPath = outputPath,
                Issues = issues,
                Stats = new ConversionStatsOutput
                {
                    ClassesConverted = result.Context.Stats.ClassesConverted,
                    InterfacesConverted = result.Context.Stats.InterfacesConverted,
                    MethodsConverted = result.Context.Stats.MethodsConverted,
                    PropertiesConverted = result.Context.Stats.PropertiesConverted,
                    FieldsConverted = result.Context.Stats.FieldsConverted,
                    InteropBlocksEmitted = result.Context.Stats.InteropBlocksEmitted,
                    MembersDropped = result.Context.Stats.MembersDropped,
                    DurationMs = (int)result.Duration.TotalMilliseconds
                },
                UnsupportedFeatureCount = unsupportedFeatureCount,
                UnsupportedFeatureSummary = unsupportedFeatureSummary,
                Explanation = explanationOutput,
                FeatureHints = featureHints.Count > 0 ? featureHints : null
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

        [JsonPropertyName("outputPath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OutputPath { get; init; }

        [JsonPropertyName("issues")]
        public required List<ConversionIssueOutput> Issues { get; init; }

        [JsonPropertyName("stats")]
        public required ConversionStatsOutput Stats { get; init; }

        [JsonPropertyName("unsupportedFeatureCount")]
        public int UnsupportedFeatureCount { get; init; }

        [JsonPropertyName("unsupportedFeatureSummary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? UnsupportedFeatureSummary { get; init; }

        [JsonPropertyName("explanation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ExplanationOutput? Explanation { get; init; }

        [JsonPropertyName("featureHints")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FeatureHints { get; init; }
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
}
