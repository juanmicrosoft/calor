using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Migration;

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

        try
        {
            var options = new ConversionOptions
            {
                ModuleName = moduleName,
                PreserveComments = true,
                AutoGenerateIds = true
            };

            var converter = new CSharpToCalorConverter(options);
            var result = converter.Convert(source);

            var output = new ConvertToolOutput
            {
                Success = result.Success,
                CalorSource = result.CalorSource,
                Issues = result.Issues.Select(i => new ConversionIssueOutput
                {
                    Severity = i.Severity.ToString().ToLowerInvariant(),
                    Message = i.Message,
                    Line = i.Line ?? 0,
                    Column = i.Column ?? 0,
                    Suggestion = i.Suggestion
                }).ToList(),
                Stats = new ConversionStatsOutput
                {
                    ClassesConverted = result.Context.Stats.ClassesConverted,
                    InterfacesConverted = result.Context.Stats.InterfacesConverted,
                    MethodsConverted = result.Context.Stats.MethodsConverted,
                    PropertiesConverted = result.Context.Stats.PropertiesConverted,
                    FieldsConverted = result.Context.Stats.FieldsConverted,
                    DurationMs = (int)result.Duration.TotalMilliseconds
                }
            };

            return Task.FromResult(McpToolResult.Json(output, isError: !result.Success));
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

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }
    }
}
