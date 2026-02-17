using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for getting machine-readable diagnostics from Calor source code.
/// </summary>
public sealed class DiagnoseTool : McpToolBase
{
    public override string Name => "calor_diagnose";

    public override string Description =>
        "Get machine-readable diagnostics from Calor source code. Returns errors and warnings with precise locations.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to diagnose"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "strictApi": {
                            "type": "boolean",
                            "default": false,
                            "description": "Enable strict API checking"
                        },
                        "requireDocs": {
                            "type": "boolean",
                            "default": false,
                            "description": "Require documentation on public functions"
                        }
                    }
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

        var options = GetOptions(arguments);
        var strictApi = GetBool(options, "strictApi");
        var requireDocs = GetBool(options, "requireDocs");

        try
        {
            var compileOptions = new CompilationOptions
            {
                StrictApi = strictApi,
                RequireDocs = requireDocs
            };

            var result = Program.Compile(source, "mcp-input.calr", compileOptions);

            var diagnostics = result.Diagnostics.Select(d => new DiagnosticOutput
            {
                Severity = d.IsError ? "error" : "warning",
                Code = d.Code.ToString(),
                Message = d.Message,
                Line = d.Span.Line,
                Column = d.Span.Column
            }).ToList();

            var output = new DiagnoseToolOutput
            {
                Success = !result.HasErrors,
                ErrorCount = diagnostics.Count(d => d.Severity == "error"),
                WarningCount = diagnostics.Count(d => d.Severity == "warning"),
                Diagnostics = diagnostics
            };

            return Task.FromResult(McpToolResult.Json(output, isError: result.HasErrors));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Diagnose failed: {ex.Message}"));
        }
    }

    private sealed class DiagnoseToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; init; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; init; }

        [JsonPropertyName("diagnostics")]
        public required List<DiagnosticOutput> Diagnostics { get; init; }
    }

    private sealed class DiagnosticOutput
    {
        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }
    }
}
