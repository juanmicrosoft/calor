using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for formatting Calor source code to canonical style.
/// </summary>
public sealed class FormatTool : McpToolBase
{
    public override string Name => "calor_format";

    public override string Description =>
        "Format Calor source code to canonical style. Returns the formatted code.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to format"
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

        try
        {
            var result = FormatSource(source);

            var output = new FormatToolOutput
            {
                Success = result.Success,
                FormattedCode = result.Formatted,
                IsChanged = result.Original != result.Formatted,
                Errors = result.Errors.Count > 0 ? result.Errors : null
            };

            return Task.FromResult(McpToolResult.Json(output, isError: !result.Success));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Format failed: {ex.Message}"));
        }
    }

    private static FormatResult FormatSource(string source)
    {
        // Parse the source
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("mcp-input.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = diagnostics.Errors.Select(e => e.Message).ToList()
            };
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = diagnostics.Errors.Select(e => e.Message).ToList()
            };
        }

        // Format the AST
        var formatter = new CalorFormatter();
        var formatted = formatter.Format(ast);

        return new FormatResult
        {
            Success = true,
            Original = source,
            Formatted = formatted,
            Errors = new List<string>()
        };
    }

    private sealed class FormatResult
    {
        public bool Success { get; init; }
        public required string Original { get; init; }
        public required string Formatted { get; init; }
        public required List<string> Errors { get; init; }
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
        public List<string>? Errors { get; init; }
    }
}
