using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>Returns compiler-authorized safe diagnostic patches without writing files.</summary>
public sealed class FixTool : McpToolBase
{
    public override string Name => "calor_fix";

    public override string Description =>
        "Apply deterministic safe diagnostic fixes and return freshly checked source without writing files. Never guesses constructor types, renames declarations or call targets, or changes sidecar identities.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to fix"
                },
                "filePath": {
                    "type": "string",
                    "description": "Path to a .calr file to fix. If 'source' is also provided, 'source' takes precedence. Also supplies diagnostic source context."
                },
                "errors": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Specific fix rules. Supported value: 'safe_diagnostics' (the default). The retired 'new_object' and 'id_conflicts' rules are rejected."
                }
            },
            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var unsupportedRule = GetStringArray(arguments, "errors").FirstOrDefault(
            rule => !rule.Equals("safe_diagnostics", StringComparison.OrdinalIgnoreCase));
        if (unsupportedRule != null)
        {
            return McpToolResult.Error(unsupportedRule.Equals("id_conflicts", StringComparison.OrdinalIgnoreCase)
                ? "Source ID conflict repair is no longer supported. Declaration identities live in .calor/ids.json; calor_fix does not rename declarations or call targets."
                : unsupportedRule.Equals("new_object", StringComparison.OrdinalIgnoreCase)
                    ? "The new_object rule is retired. Use explicit native new Type(args) constructors; calor_fix never guesses constructor types. Use safe_diagnostics for compiler-authorized patches."
                    : $"Unsupported fix rule: '{unsupportedRule}'. Supported rule: 'safe_diagnostics'.");
        }

        if (source == null)
        {
            if (string.IsNullOrEmpty(filePath))
                return McpToolResult.Error("Either 'source' or 'filePath' must be provided.");

            var pathError = ValidatePath(filePath, "filePath");
            if (pathError != null) return pathError;
            if (!File.Exists(filePath))
                return McpToolResult.Error($"File not found: {filePath}");
            source = await File.ReadAllTextAsync(filePath, cancellationToken);
        }

        var sizeError = ValidateSourceSize(source);
        if (sizeError != null) return sizeError;

        try
        {
            var descriptions = new List<string>();
            var current = DiagnosticFixes.Repair(source, filePath ?? "mcp-fix.calr",
                new CompilationOptions { CancellationToken = cancellationToken },
                out var result, out var editsApplied, descriptions);
            return McpToolResult.Json(new FixToolOutput
            {
                Success = !result.HasErrors,
                FixedSource = current,
                WasModified = current != source,
                FixCount = editsApplied,
                Fixes = descriptions.Count > 0 ? descriptions : null,
                Diagnostics = DiagnosticEnvelope.Build(result.Diagnostics)
            }, isError: result.HasErrors);
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Fix failed: {ex.Message}");
        }
    }

    private sealed class FixToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("fixedSource")]
        public required string FixedSource { get; init; }

        [JsonPropertyName("wasModified")]
        public bool WasModified { get; init; }

        [JsonPropertyName("fixCount")]
        public int FixCount { get; init; }

        [JsonPropertyName("fixes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Fixes { get; init; }

        [JsonPropertyName("diagnostics")]
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }
    }
}
