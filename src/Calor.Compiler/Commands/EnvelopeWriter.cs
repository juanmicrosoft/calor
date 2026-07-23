using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;

namespace Calor.Compiler.Commands;

/// <summary>
/// Serializes the envelope schema v1.1 document for data-carrying CLI commands
/// (loop plan D1.3): <c>{ version, command, diagnostics, summary, data }</c>.
/// The command's payload keeps its existing shape, unchanged, under
/// <c>data</c>; <c>diagnostics</c> is always present (possibly empty) so
/// findings can never hide inside the payload.
/// </summary>
internal static class EnvelopeWriter
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds the envelope document for <paramref name="command"/> with
    /// <paramref name="data"/> as the command-specific payload.
    /// </summary>
    /// <param name="command">The producing surface (e.g. <c>"coverage"</c>).</param>
    /// <param name="data">Command payload; serialized camelCase with null fields omitted.</param>
    /// <param name="diagnostics">Source-anchored diagnostics, if any.</param>
    /// <param name="resolver">Optional resolver so entries carry <c>declarationId</c>.</param>
    public static string Serialize(
        string command,
        object? data,
        IEnumerable<Diagnostic>? diagnostics = null,
        DeclarationIdResolver? resolver = null)
    {
        var list = diagnostics as ICollection<Diagnostic> ?? diagnostics?.ToList() ?? (ICollection<Diagnostic>)Array.Empty<Diagnostic>();
        var envelope = new Envelope
        {
            Version = JsonDiagnosticFormatter.SchemaVersion,
            Command = command,
            Diagnostics = list.Select(d => DiagnosticEnvelope.Build(d, resolver)).ToList(),
            Summary = DiagnosticEnvelope.Summarize(list),
            Data = data
        };

        return JsonSerializer.Serialize(envelope, s_options);
    }

    /// <summary>
    /// Wraps an already-serialized JSON payload (e.g. a report produced by a
    /// generator that owns its own serialization) under <c>data</c>.
    /// </summary>
    public static string SerializeRaw(string command, string dataJson)
    {
        using var doc = JsonDocument.Parse(dataJson);
        return Serialize(command, doc.RootElement.Clone());
    }

    private sealed class Envelope
    {
        public required string Version { get; init; }
        public required string Command { get; init; }
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }
        public required EnvelopeSummary Summary { get; init; }
        public object? Data { get; init; }
    }
}
