using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Init;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for explaining Calor error codes and common mistakes.
/// Returns the relevant "Common Mistake" pattern with correct syntax and examples.
/// </summary>
public sealed class ExplainErrorTool : McpToolBase
{
    private static readonly Lazy<List<ErrorPattern>> ErrorPatterns = new(LoadErrorPatterns);

    public override string Name => "calor_explain_error";

    public override string Description =>
        "Explain a Calor error code or message. Returns the relevant common mistake pattern with the correct fix.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "error": {
                    "type": "string",
                    "description": "The error code (e.g., 'CALOR0042') or error message text to explain"
                }
            },
            "required": ["error"]
        ,

        "additionalProperties": false

        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var error = GetString(arguments, "error");
        if (string.IsNullOrEmpty(error))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: error"));
        }

        try
        {
            var patterns = ErrorPatterns.Value;
            var matches = FindMatchingPatterns(patterns, error);

            if (matches.Count == 0)
            {
                return Task.FromResult(McpToolResult.Text(
                    $"No known common mistake pattern matches '{error}'. " +
                    "Check the Calor syntax help for the relevant feature using calor_syntax_help."));
            }

            var output = new ExplainErrorOutput
            {
                Query = error,
                Matches = matches.Select(p => new ErrorExplanation
                {
                    Id = p.Id,
                    Title = p.Title,
                    Description = p.Description,
                    WrongExample = p.WrongExample,
                    CorrectExample = p.CorrectExample,
                    Explanation = p.Explanation
                }).ToList()
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to explain error: {ex.Message}"));
        }
    }

    private static List<ErrorPattern> FindMatchingPatterns(List<ErrorPattern> patterns, string error)
    {
        var normalizedError = error.ToLowerInvariant();
        var matches = new List<ErrorPattern>();

        foreach (var pattern in patterns)
        {
            // Check error codes
            if (pattern.MatchCodes.Any(code =>
                error.Contains(code, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(pattern);
                continue;
            }

            // Check message patterns
            if (pattern.MatchPatterns.Any(mp =>
                normalizedError.Contains(mp.ToLowerInvariant())))
            {
                matches.Add(pattern);
            }
        }

        return matches;
    }

    private static List<ErrorPattern> LoadErrorPatterns()
    {
        try
        {
            var json = EmbeddedResourceHelper.ReadResource(
                "Calor.Compiler.Resources.error-suggestions.json");
            var doc = JsonDocument.Parse(json);
            var patterns = new List<ErrorPattern>();

            foreach (var item in doc.RootElement.GetProperty("patterns").EnumerateArray())
            {
                patterns.Add(new ErrorPattern
                {
                    Id = item.GetProperty("id").GetString() ?? "",
                    Title = item.GetProperty("title").GetString() ?? "",
                    Description = item.GetProperty("description").GetString() ?? "",
                    WrongExample = item.GetProperty("wrongExample").GetString() ?? "",
                    CorrectExample = item.GetProperty("correctExample").GetString() ?? "",
                    Explanation = item.GetProperty("explanation").GetString() ?? "",
                    MatchPatterns = item.GetProperty("matchPatterns")
                        .EnumerateArray()
                        .Select(e => e.GetString() ?? "")
                        .ToList(),
                    MatchCodes = item.GetProperty("matchCodes")
                        .EnumerateArray()
                        .Select(e => e.GetString() ?? "")
                        .ToList()
                });
            }

            return patterns;
        }
        catch
        {
            return new List<ErrorPattern>();
        }
    }

    private sealed class ErrorPattern
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string WrongExample { get; init; }
        public required string CorrectExample { get; init; }
        public required string Explanation { get; init; }
        public required List<string> MatchPatterns { get; init; }
        public required List<string> MatchCodes { get; init; }
    }

    private sealed class ExplainErrorOutput
    {
        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("matches")]
        public required List<ErrorExplanation> Matches { get; init; }
    }

    private sealed class ErrorExplanation
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("wrongExample")]
        public required string WrongExample { get; init; }

        [JsonPropertyName("correctExample")]
        public required string CorrectExample { get; init; }

        [JsonPropertyName("explanation")]
        public required string Explanation { get; init; }
    }
}
