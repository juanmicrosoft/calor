using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool: calor_suggest_types
/// Analyzes parameter usage patterns and suggests refined types.
/// </summary>
public sealed class TypeSuggestionTool : McpToolBase
{
    public override string Name => "calor_suggest_types";

    public override string Description =>
        "Analyze how parameters are used in function bodies and suggest refined types. " +
        "Detects patterns: used as divisor (!=0), used as index (>=0), compared with >=0.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to analyze"
                },
                "function_id": {
                    "type": "string",
                    "description": "Optional: filter suggestions to a specific function"
                }
            },
            "required": ["source"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));

        var functionId = GetString(arguments, "function_id");

        try
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.TokenizeAll();
            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            if (diagnostics.HasErrors)
            {
                return Task.FromResult(McpToolResult.Json(new TypeSuggestionOutput
                {
                    Success = false,
                    Suggestions = new List<SuggestionOutput>(),
                    Errors = diagnostics.Errors.Select(d => d.Message).ToList()
                }, isError: true));
            }

            var suggester = new TypeSuggester();
            var suggestions = suggester.Suggest(module);

            if (functionId != null)
                suggestions = suggestions.Where(s => s.FunctionId == functionId).ToList();

            return Task.FromResult(McpToolResult.Json(new TypeSuggestionOutput
            {
                Success = true,
                Suggestions = suggestions.Select(s => new SuggestionOutput
                {
                    FunctionId = s.FunctionId,
                    ParameterName = s.ParameterName,
                    CurrentType = s.CurrentType,
                    SuggestedPredicate = s.SuggestedPredicate,
                    Reason = s.Reason,
                    Confidence = s.Confidence,
                    CalorSyntax = s.CalorSyntax
                }).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Type suggestion failed: {ex.Message}"));
        }
    }

    private sealed class TypeSuggestionOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("suggestions")]
        public required List<SuggestionOutput> Suggestions { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }
    }

    private sealed class SuggestionOutput
    {
        [JsonPropertyName("function_id")]
        public required string FunctionId { get; init; }

        [JsonPropertyName("parameter_name")]
        public required string ParameterName { get; init; }

        [JsonPropertyName("current_type")]
        public required string CurrentType { get; init; }

        [JsonPropertyName("suggested_predicate")]
        public required string SuggestedPredicate { get; init; }

        [JsonPropertyName("reason")]
        public required string Reason { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("calor_syntax")]
        public required string CalorSyntax { get; init; }
    }
}
