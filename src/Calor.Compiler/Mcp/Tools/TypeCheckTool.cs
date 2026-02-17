using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for type checking Calor source code.
/// </summary>
public sealed class TypeCheckTool : McpToolBase
{
    public override string Name => "calor_typecheck";

    public override string Description =>
        "Type check Calor source code. Returns type errors with precise locations and categories.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to type check"
                },
                "filePath": {
                    "type": "string",
                    "description": "Optional file path for diagnostic messages"
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

        var filePath = GetString(arguments, "filePath") ?? "mcp-typecheck.calr";

        try
        {
            var options = new CompilationOptions
            {
                EnableTypeChecking = true
            };

            var result = Program.Compile(source, filePath, options);

            var typeErrors = new List<TypeErrorOutput>();
            foreach (var diag in result.Diagnostics)
            {
                typeErrors.Add(new TypeErrorOutput
                {
                    Code = diag.Code,
                    Message = diag.Message,
                    Line = diag.Span.Line,
                    Column = diag.Span.Column,
                    Severity = diag.Severity switch
                    {
                        DiagnosticSeverity.Error => "error",
                        DiagnosticSeverity.Warning => "warning",
                        _ => "info"
                    },
                    Category = CategorizeError(diag.Code)
                });
            }

            var output = new TypeCheckToolOutput
            {
                Success = !result.HasErrors,
                ErrorCount = typeErrors.Count(e => e.Severity == "error"),
                WarningCount = typeErrors.Count(e => e.Severity == "warning"),
                TypeErrors = typeErrors
            };

            return Task.FromResult(McpToolResult.Json(output, isError: result.HasErrors));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Type checking failed: {ex.Message}"));
        }
    }

    private static string CategorizeError(string code) => code switch
    {
        DiagnosticCode.TypeMismatch => "type_mismatch",
        DiagnosticCode.UndefinedReference => "undefined_reference",
        DiagnosticCode.DuplicateDefinition => "duplicate_definition",
        DiagnosticCode.InvalidReference => "invalid_reference",
        _ => "other"
    };

    private sealed class TypeCheckToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; init; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; init; }

        [JsonPropertyName("typeErrors")]
        public required List<TypeErrorOutput> TypeErrors { get; init; }
    }

    private sealed class TypeErrorOutput
    {
        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }

        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("category")]
        public required string Category { get; init; }
    }
}
