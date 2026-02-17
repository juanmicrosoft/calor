using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for validating Calor code fragments in isolation.
/// Wraps snippets in synthetic module/function structure for validation.
/// </summary>
public sealed class ValidateSnippetTool : McpToolBase
{
    public override string Name => "calor_validate_snippet";

    public override string Description =>
        "Validate Calor code fragments in isolation with optional context. " +
        "Useful for incremental validation during code generation.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "snippet": {
                    "type": "string",
                    "description": "The Calor code fragment to validate"
                },
                "context": {
                    "type": "object",
                    "description": "Context for wrapping the snippet",
                    "properties": {
                        "location": {
                            "type": "string",
                            "enum": ["expression", "statement", "function_body", "module_body"],
                            "default": "statement",
                            "description": "Where in code structure this snippet appears"
                        },
                        "returnType": {
                            "type": "string",
                            "description": "Expected return type for the containing function"
                        },
                        "parameters": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": { "type": "string" },
                                    "type": { "type": "string" }
                                },
                                "required": ["name", "type"]
                            },
                            "description": "Variables in scope"
                        },
                        "surroundingCode": {
                            "type": "string",
                            "description": "Code that precedes the snippet"
                        }
                    }
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "lexerOnly": {
                            "type": "boolean",
                            "default": false,
                            "description": "Stop after lexer (token validation only)"
                        },
                        "showTokens": {
                            "type": "boolean",
                            "default": false,
                            "description": "Include token stream in output"
                        }
                    }
                }
            },
            "required": ["snippet"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var snippet = GetString(arguments, "snippet");
        if (snippet == null)
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: snippet"));
        }

        // Handle empty or whitespace-only snippets
        if (string.IsNullOrWhiteSpace(snippet))
        {
            var emptyResult = new ValidateSnippetOutput
            {
                Success = true,
                Valid = true,
                Snippet = snippet,
                Diagnostics = [],
                ValidationLevel = "none",
                Warnings = ["Snippet is empty or contains only whitespace"]
            };
            return Task.FromResult(McpToolResult.Json(emptyResult));
        }

        var context = ParseContext(arguments);
        var options = GetOptions(arguments);
        var lexerOnly = GetBool(options, "lexerOnly");
        var showTokens = GetBool(options, "showTokens");

        try
        {
            var wrapper = SnippetWrapper.Wrap(snippet, context);
            var diagnostics = new DiagnosticBag();

            // Run lexer
            var lexer = new Lexer(wrapper.WrappedSource, diagnostics);
            var tokens = lexer.TokenizeAll();

            List<TokenOutput>? tokenOutput = null;
            if (showTokens)
            {
                tokenOutput = tokens
                    .Where(t => t.Kind != TokenKind.Whitespace && t.Kind != TokenKind.Newline && t.Kind != TokenKind.Eof)
                    .Where(t => wrapper.IsInSnippet(t.Span.Line, t.Span.Column))
                    .Select(t => new TokenOutput
                    {
                        Kind = t.Kind.ToString(),
                        Text = t.Text,
                        Line = wrapper.AdjustLine(t.Span.Line),
                        Column = wrapper.AdjustColumn(t.Span.Line, t.Span.Column)
                    })
                    .ToList();
            }

            var filteredDiagnostics = FilterAndAdjustDiagnostics(diagnostics, wrapper);

            // Combine wrapper warnings with any we might add
            var warnings = wrapper.Warnings.Count > 0 ? wrapper.Warnings : null;

            if (lexerOnly || diagnostics.HasErrors)
            {
                var output = new ValidateSnippetOutput
                {
                    Success = true,
                    Valid = !filteredDiagnostics.Any(d => d.Severity == "error"),
                    Snippet = snippet,
                    Diagnostics = filteredDiagnostics,
                    ValidationLevel = "lexer",
                    Tokens = tokenOutput,
                    Warnings = warnings
                };

                return Task.FromResult(McpToolResult.Json(output));
            }

            // Run parser
            var parser = new Parser(tokens, diagnostics);
            parser.Parse();

            filteredDiagnostics = FilterAndAdjustDiagnostics(diagnostics, wrapper);

            var result = new ValidateSnippetOutput
            {
                Success = true,
                Valid = !filteredDiagnostics.Any(d => d.Severity == "error"),
                Snippet = snippet,
                Diagnostics = filteredDiagnostics,
                ValidationLevel = "parser",
                Tokens = tokenOutput,
                Warnings = warnings
            };

            return Task.FromResult(McpToolResult.Json(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Validation failed: {ex.Message}"));
        }
    }

    private static SnippetContext ParseContext(JsonElement? arguments)
    {
        if (arguments == null || !arguments.Value.TryGetProperty("context", out var contextElement))
        {
            return SnippetContext.Default;
        }

        // Handle null or non-object context
        if (contextElement.ValueKind != JsonValueKind.Object)
        {
            return SnippetContext.Default;
        }

        var location = SnippetLocation.Statement;
        if (contextElement.TryGetProperty("location", out var locationProp) &&
            locationProp.ValueKind == JsonValueKind.String)
        {
            location = locationProp.GetString() switch
            {
                "expression" => SnippetLocation.Expression,
                "statement" => SnippetLocation.Statement,
                "function_body" => SnippetLocation.FunctionBody,
                "module_body" => SnippetLocation.ModuleBody,
                _ => SnippetLocation.Statement
            };
        }

        string? returnType = null;
        if (contextElement.TryGetProperty("returnType", out var returnTypeProp) &&
            returnTypeProp.ValueKind == JsonValueKind.String)
        {
            returnType = returnTypeProp.GetString();
        }

        var parameters = new List<SnippetParameter>();
        if (contextElement.TryGetProperty("parameters", out var paramsProp) &&
            paramsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var param in paramsProp.EnumerateArray())
            {
                if (param.TryGetProperty("name", out var nameProp) &&
                    param.TryGetProperty("type", out var typeProp) &&
                    nameProp.ValueKind == JsonValueKind.String &&
                    typeProp.ValueKind == JsonValueKind.String)
                {
                    parameters.Add(new SnippetParameter(nameProp.GetString()!, typeProp.GetString()!));
                }
            }
        }

        string? surroundingCode = null;
        if (contextElement.TryGetProperty("surroundingCode", out var surroundingProp) &&
            surroundingProp.ValueKind == JsonValueKind.String)
        {
            surroundingCode = surroundingProp.GetString();
        }

        return new SnippetContext(location, returnType, parameters, surroundingCode);
    }

    private static List<DiagnosticOutput> FilterAndAdjustDiagnostics(DiagnosticBag diagnostics, SnippetWrapper wrapper)
    {
        var result = new List<DiagnosticOutput>();

        foreach (var d in diagnostics.Errors.Concat(diagnostics.Warnings))
        {
            if (wrapper.IsInSnippet(d.Span.Line, d.Span.Column))
            {
                result.Add(new DiagnosticOutput
                {
                    Severity = d.IsError ? "error" : "warning",
                    Code = d.Code,
                    Message = d.Message,
                    Line = wrapper.AdjustLine(d.Span.Line),
                    Column = wrapper.AdjustColumn(d.Span.Line, d.Span.Column)
                });
            }
        }

        return result;
    }

    private sealed class ValidateSnippetOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("valid")]
        public bool Valid { get; init; }

        [JsonPropertyName("snippet")]
        public required string Snippet { get; init; }

        [JsonPropertyName("diagnostics")]
        public required List<DiagnosticOutput> Diagnostics { get; init; }

        [JsonPropertyName("validationLevel")]
        public required string ValidationLevel { get; init; }

        [JsonPropertyName("tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TokenOutput>? Tokens { get; init; }

        [JsonPropertyName("warnings")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<string>? Warnings { get; init; }
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

    private sealed class TokenOutput
    {
        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("text")]
        public required string Text { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }
    }
}

/// <summary>
/// Location type for snippet context.
/// </summary>
internal enum SnippetLocation
{
    Expression,
    Statement,
    FunctionBody,
    ModuleBody
}

/// <summary>
/// Parameter definition for snippet context.
/// </summary>
internal readonly record struct SnippetParameter(string Name, string Type);

/// <summary>
/// Context information for wrapping a snippet.
/// </summary>
internal sealed class SnippetContext
{
    public static readonly SnippetContext Default = new(SnippetLocation.Statement, null, [], null);

    public SnippetLocation Location { get; }
    public string? ReturnType { get; }
    public IReadOnlyList<SnippetParameter> Parameters { get; }
    public string? SurroundingCode { get; }

    public SnippetContext(
        SnippetLocation location,
        string? returnType,
        IReadOnlyList<SnippetParameter> parameters,
        string? surroundingCode)
    {
        Location = location;
        ReturnType = returnType;
        Parameters = parameters;
        SurroundingCode = surroundingCode;
    }
}

/// <summary>
/// Helper class that wraps a snippet in synthetic module/function structure
/// and tracks line/column offsets for diagnostic adjustment.
/// </summary>
internal sealed class SnippetWrapper
{
    public string WrappedSource { get; }
    public int SnippetStartLine { get; }
    public int SnippetEndLine { get; }

    /// <summary>
    /// Column offset for the first line of the snippet (used for expression wrapping).
    /// For multi-line snippets, only the first line has this offset.
    /// </summary>
    public int FirstLineColumnOffset { get; }

    /// <summary>
    /// Warnings about context configuration issues.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    private SnippetWrapper(string wrappedSource, int snippetStartLine, int snippetEndLine, int firstLineColumnOffset = 0, IReadOnlyList<string>? warnings = null)
    {
        WrappedSource = wrappedSource;
        SnippetStartLine = snippetStartLine;
        SnippetEndLine = snippetEndLine;
        FirstLineColumnOffset = firstLineColumnOffset;
        Warnings = warnings ?? [];
    }

    /// <summary>
    /// Wraps the snippet in appropriate synthetic code based on context.
    /// </summary>
    public static SnippetWrapper Wrap(string snippet, SnippetContext context)
    {
        var sb = new StringBuilder();
        var warnings = new List<string>();
        int currentLine = 1;
        int firstLineColumnOffset = 0;

        // Module header
        sb.AppendLine("§M{_:_snippet_}");
        currentLine++;

        if (context.Location == SnippetLocation.ModuleBody)
        {
            // Warn about ignored context for module_body location
            if (context.Parameters.Count > 0)
            {
                warnings.Add("Context 'parameters' ignored for module_body location");
            }
            if (context.ReturnType != null)
            {
                warnings.Add("Context 'returnType' ignored for module_body location");
            }
            if (context.SurroundingCode != null)
            {
                warnings.Add("Context 'surroundingCode' ignored for module_body location");
            }

            // Snippet goes directly in module body
            int snippetStart = currentLine;
            sb.Append(snippet);
            int snippetLines = CountLines(snippet);
            currentLine += snippetLines;
            int snippetEnd = currentLine - 1;

            if (!snippet.EndsWith('\n'))
            {
                sb.AppendLine();
                currentLine++;
            }

            sb.AppendLine("§/M{_}");

            return new SnippetWrapper(sb.ToString(), snippetStart, snippetEnd, 0, warnings);
        }

        // Function header
        sb.AppendLine("§F{_:_validate_:pri}");
        currentLine++;

        // Parameters
        foreach (var param in context.Parameters)
        {
            sb.AppendLine($"  §I{{{param.Type}:{param.Name}}}");
            currentLine++;
        }

        // Return type
        var returnType = context.ReturnType ?? "unit";
        sb.AppendLine($"  §O{{{returnType}}}");
        currentLine++;

        // Surrounding code if any
        if (!string.IsNullOrEmpty(context.SurroundingCode))
        {
            sb.Append("  ");
            sb.AppendLine(context.SurroundingCode);
            currentLine += CountLines(context.SurroundingCode);
        }

        int snippetStartLine = currentLine;

        // Handle different location types
        switch (context.Location)
        {
            case SnippetLocation.Expression:
                // Wrap expression in a bind: "  §B{_result} " = 15 characters
                const string expressionPrefix = "  §B{_result} ";
                sb.Append(expressionPrefix);
                sb.Append(snippet);
                // Column offset: the prefix length (1-based columns, so subtract 1 from the prefix length for offset)
                firstLineColumnOffset = expressionPrefix.Length;
                break;

            case SnippetLocation.Statement:
            case SnippetLocation.FunctionBody:
            default:
                // Indent snippet lines with 2 spaces
                const string statementIndent = "  ";
                var lines = snippet.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    sb.Append(statementIndent);
                    sb.Append(lines[i]);
                    if (i < lines.Length - 1)
                    {
                        sb.AppendLine();
                    }
                }
                // All lines have the same 2-character indent
                firstLineColumnOffset = statementIndent.Length;
                break;
        }

        int snippetLines2 = CountLines(snippet);
        int snippetEndLine = snippetStartLine + snippetLines2 - 1;
        currentLine += snippetLines2;

        if (!snippet.EndsWith('\n'))
        {
            sb.AppendLine();
            currentLine++;
        }

        // Close function and module
        sb.AppendLine("§/F{_}");
        sb.AppendLine("§/M{_}");

        return new SnippetWrapper(sb.ToString(), snippetStartLine, snippetEndLine, firstLineColumnOffset, warnings);
    }

    /// <summary>
    /// Checks if a line number (1-based) falls within the snippet bounds.
    /// </summary>
    public bool IsInSnippet(int line)
    {
        return line >= SnippetStartLine && line <= SnippetEndLine;
    }

    /// <summary>
    /// Checks if a position (line and column) falls within the snippet bounds.
    /// This handles expression wrapping where the first line has a prefix.
    /// </summary>
    public bool IsInSnippet(int line, int column)
    {
        if (line < SnippetStartLine || line > SnippetEndLine)
            return false;

        // For the first line with expression prefix, check column bounds
        if (line == SnippetStartLine && FirstLineColumnOffset > 0)
        {
            // Column must be past the prefix
            return column > FirstLineColumnOffset;
        }

        return true;
    }

    /// <summary>
    /// Adjusts a line number from wrapped source to snippet-relative (1-based).
    /// </summary>
    public int AdjustLine(int line)
    {
        return line - SnippetStartLine + 1;
    }

    /// <summary>
    /// Adjusts a column number based on line position and location type.
    /// </summary>
    public int AdjustColumn(int line, int column)
    {
        if (FirstLineColumnOffset == 0)
        {
            // module_body: no adjustment needed
            return column;
        }

        if (line == SnippetStartLine)
        {
            // First line always has the full prefix offset
            return Math.Max(1, column - FirstLineColumnOffset);
        }

        // For subsequent lines:
        // - Expression location: snippet text after newline has no prefix
        // - Statement location: each line has "  " indent (2 chars)
        if (FirstLineColumnOffset == 2)
        {
            // Statement/function_body: all lines have 2-char indent
            return Math.Max(1, column - 2);
        }

        // Expression location (15-char prefix on first line only)
        // Subsequent lines have no prefix - they're raw snippet content
        return column;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }
        return count;
    }
}
