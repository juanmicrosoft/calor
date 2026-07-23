using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Diagnostics;

/// <summary>
/// Serializer for the top-level envelope document (schema v1.1, loop plan D1.3)
/// emitted by data-carrying CLI commands:
/// <c>{ version, command, diagnostics, summary, data }</c>.
/// Diagnostics entries are the shared <see cref="EnvelopeDiagnostic"/> shape
/// built via <see cref="DiagnosticEnvelope"/>; <c>data</c> is the command's own
/// payload (its shape is owned by the command's doc). Serialized camelCase with
/// null fields omitted, matching <see cref="JsonDiagnosticFormatter"/>.
/// </summary>
public static class CommandEnvelope
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes one envelope document for <paramref name="command"/> with the
    /// given aggregated diagnostics and command-specific <paramref name="data"/>
    /// payload (omitted when null).
    /// </summary>
    public static string Serialize(string command, DiagnosticBag diagnostics, object? data)
    {
        var document = new CommandDocument
        {
            Version = JsonDiagnosticFormatter.SchemaVersion,
            Command = command,
            Diagnostics = DiagnosticEnvelope.Build(diagnostics),
            Summary = DiagnosticEnvelope.Summarize(diagnostics),
            Data = data
        };

        return JsonSerializer.Serialize(document, s_options);
    }

    private sealed class CommandDocument
    {
        public required string Version { get; init; }
        public required string Command { get; init; }
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }
        public required EnvelopeSummary Summary { get; init; }
        public object? Data { get; init; }
    }
}
