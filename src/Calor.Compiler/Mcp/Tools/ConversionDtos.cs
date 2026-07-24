using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Maps converter <see cref="ConversionIssue"/> objects (and tool-level
/// messages) to envelope schema v1.1 <see cref="EnvelopeDiagnostic"/> entries
/// (loop plan D1.3 — final MCP sweep). Conversion issues carry
/// <see cref="DiagnosticCode.ConversionIssue"/> (Calor1343) with the feature
/// name prefixed, mirroring the <c>calor convert --format json</c> CLI mapping.
/// </summary>
internal static class ConversionIssueEnvelope
{
    /// <summary>Builds the envelope entry for one conversion issue.</summary>
    public static EnvelopeDiagnostic Build(ConversionIssue issue, string? filePath = null)
    {
        return new EnvelopeDiagnostic
        {
            Code = DiagnosticCode.ConversionIssue,
            Message = issue.Feature != null ? $"[{issue.Feature}] {issue.Message}" : issue.Message,
            Severity = issue.Severity switch
            {
                ConversionIssueSeverity.Error => "error",
                ConversionIssueSeverity.Warning => "warning",
                _ => "info"
            },
            Location = new EnvelopeLocation
            {
                File = filePath,
                Line = issue.Line ?? 1,
                Column = issue.Column ?? 1,
                Length = 0
            },
            Suggestion = issue.Suggestion
        };
    }

    /// <summary>
    /// Builds a message-only envelope entry for tool-level findings that carry
    /// no source position (exceptions, missing files, status notes).
    /// </summary>
    public static EnvelopeDiagnostic Message(
        string code, string severity, string message,
        string? filePath = null, string? suggestion = null)
    {
        return new EnvelopeDiagnostic
        {
            Code = code,
            Message = message,
            Severity = severity,
            Location = new EnvelopeLocation { File = filePath, Line = 1, Column = 1, Length = 0 },
            Suggestion = suggestion
        };
    }
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

    [JsonPropertyName("membersDropped")]
    public int MembersDropped { get; init; }

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; init; }
}
