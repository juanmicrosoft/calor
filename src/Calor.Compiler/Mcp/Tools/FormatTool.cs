using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>MCP tool for formatting ID-free Calor source.</summary>
public sealed class FormatTool : McpToolBase
{
    public override string Name => "calor_format";

    public override string Description =>
        "Format ID-free Calor source. Persisted declaration IDs belong to .calor/ids.json; use calor ids sync PROJECT --register FILE for identity operations.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "ID-free Calor source code to format"
                },
                "action": {
                    "type": "string",
                    "enum": ["format", "ids"],
                    "description": "format formats code. ids reports that persisted identity checks require a project file.",
                    "default": "format"
                },
                "idsAction": {
                    "type": "string",
                    "enum": ["check"],
                    "default": "check",
                    "description": "In-memory source has no persisted identities. Use calor ids sync PROJECT --register FILE. The former assign action is retired."
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "allowTestIds": {
                            "type": "boolean",
                            "default": false,
                            "description": "Compatibility option; in-memory source has no persisted identities"
                        }
                    }
                }
            },
            "required": ["source"],
            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        if (source == null)
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));

        var action = GetString(arguments, "action") ?? "format";
        if (action.Equals("ids", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(McpToolResult.Error(
                "Source-ID assignment and in-memory identity checks are unavailable. " +
                "Declaration IDs are tooling-owned in .calor/ids.json. " +
                "Run calor ids sync PROJECT --register FILE, then calor ids check FILE."));
        if (!action.Equals("format", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(McpToolResult.Error($"Unknown format action: {action}"));

        try
        {
            var result = new CalorFormatter().FormatSource(source, "mcp-input.calr");
            var errors = result.Diagnostics.Where(diagnostic => diagnostic.IsError)
                .Select(diagnostic => DiagnosticEnvelope.Build(diagnostic)).ToList();
            return Task.FromResult(McpToolResult.Json(new FormatToolOutput
            {
                Success = result.Success,
                FormattedCode = result.Formatted,
                IsChanged = result.Original != result.Formatted,
                Errors = errors.Count > 0 ? errors : null,
                ConservativeFallbackReason = result.ConservativeFallbackReason
            }, isError: !result.Success));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Operation failed: {ex.Message}"));
        }
    }

    private sealed class FormatToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("formattedCode")]
        public required string FormattedCode { get; init; }

        [JsonPropertyName("isChanged")]
        public bool IsChanged { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EnvelopeDiagnostic>? Errors { get; init; }

        [JsonPropertyName("conservativeFallbackReason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ConservativeFallbackReason { get; init; }
    }
}
