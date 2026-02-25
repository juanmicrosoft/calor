using System.Text.Json.Serialization;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Shared DTO for conversion issues emitted by MCP convert tools.
/// </summary>
internal sealed class ConversionIssueOutput
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

/// <summary>
/// Shared DTO for conversion statistics emitted by MCP convert tools.
/// </summary>
internal sealed class ConversionStatsOutput
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
