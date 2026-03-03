using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for finding the definition of a symbol at a given position.
/// Returns the file path, line, column, and a preview of the definition.
/// </summary>
public sealed class GotoDefinitionTool : McpToolBase
{
    public override string Name => "calor_goto_definition";

    public override string Description =>
        "Find the definition of a symbol at a given position in Calor source code. " +
        "Provide either 'source' for inline code or 'filePath' to read from file. " +
        "Returns the definition location with file path, line, column, and preview.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code (use this OR filePath)"
                },
                "filePath": {
                    "type": "string",
                    "description": "Path to a .calr file (use this OR source)"
                },
                "line": {
                    "type": "integer",
                    "description": "Line number (1-based) where the symbol is located"
                },
                "column": {
                    "type": "integer",
                    "description": "Column number (1-based) where the symbol is located"
                }
            },
            "required": ["line", "column"]
        ,

        "additionalProperties": false

        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var line = GetInt(arguments, "line", 0);
        var column = GetInt(arguments, "column", 0);

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
        {
            return McpToolResult.Error("Either 'source' or 'filePath' is required");
        }

        if (line <= 0 || column <= 0)
        {
            return McpToolResult.Error("Both 'line' and 'column' must be positive integers");
        }

        ParseResult parseResult;
        if (!string.IsNullOrEmpty(filePath))
        {
            parseResult = await CalorSourceHelper.ParseFileAsync(filePath);
        }
        else
        {
            parseResult = CalorSourceHelper.Parse(source!, filePath);
        }

        if (!parseResult.IsSuccess)
        {
            return McpToolResult.Json(new GotoDefinitionOutput
            {
                Found = false,
                Errors = parseResult.Errors.ToList()
            }, isError: true);
        }

        var definition = FindDefinitionAtPosition(parseResult.Ast!, parseResult.Source!, line, column);

        if (definition == null)
        {
            return McpToolResult.Json(new GotoDefinitionOutput
            {
                Found = false,
                Message = $"No symbol found at line {line}, column {column}"
            });
        }

        return McpToolResult.Json(new GotoDefinitionOutput
        {
            Found = true,
            FilePath = filePath,
            Line = definition.Line,
            Column = definition.Column,
            SymbolName = definition.Name,
            SymbolKind = definition.Kind,
            Preview = definition.Preview
        });
    }

    private static DefinitionInfo? FindDefinitionAtPosition(ModuleNode ast, string source, int line, int column)
    {
        // Extract identifier at position
        var identifier = ExtractIdentifierAtPosition(source, line, column);
        if (string.IsNullOrEmpty(identifier))
            return null;

        // Search for definition in order of specificity

        // 1. Check if inside a function - look for parameters and locals
        foreach (var func in ast.Functions)
        {
            if (IsPositionInNode(line, func.Span))
            {
                // Check function name
                if (func.Name == identifier)
                {
                    return CreateDefinitionInfo(func.Name, "function", func.Span, source);
                }

                // Check parameters
                foreach (var param in func.Parameters)
                {
                    if (param.Name == identifier)
                    {
                        return CreateDefinitionInfo(param.Name, "parameter", param.Span, source);
                    }
                }

                // Check local bindings
                foreach (var stmt in func.Body)
                {
                    if (stmt is BindStatementNode bind && bind.Name == identifier && bind.Span.Line <= line)
                    {
                        return CreateDefinitionInfo(bind.Name, bind.IsMutable ? "mutable variable" : "variable", bind.Span, source);
                    }
                }
            }
        }

        // 2. Check if inside a class
        foreach (var cls in ast.Classes)
        {
            if (IsPositionInNode(line, cls.Span))
            {
                if (cls.Name == identifier)
                {
                    return CreateDefinitionInfo(cls.Name, "class", cls.Span, source);
                }

                foreach (var field in cls.Fields)
                {
                    if (field.Name == identifier)
                    {
                        return CreateDefinitionInfo(field.Name, "field", field.Span, source);
                    }
                }

                foreach (var prop in cls.Properties)
                {
                    if (prop.Name == identifier)
                    {
                        return CreateDefinitionInfo(prop.Name, "property", prop.Span, source);
                    }
                }

                foreach (var method in cls.Methods)
                {
                    if (method.Name == identifier)
                    {
                        return CreateDefinitionInfo(method.Name, "method", method.Span, source);
                    }

                    // Check method parameters and locals
                    if (IsPositionInNode(line, method.Span))
                    {
                        foreach (var param in method.Parameters)
                        {
                            if (param.Name == identifier)
                            {
                                return CreateDefinitionInfo(param.Name, "parameter", param.Span, source);
                            }
                        }

                        foreach (var stmt in method.Body)
                        {
                            if (stmt is BindStatementNode bind && bind.Name == identifier && bind.Span.Line <= line)
                            {
                                return CreateDefinitionInfo(bind.Name, bind.IsMutable ? "mutable variable" : "variable", bind.Span, source);
                            }
                        }
                    }
                }
            }
        }

        // 3. Search module-level definitions
        foreach (var func in ast.Functions)
        {
            if (func.Name == identifier)
            {
                return CreateDefinitionInfo(func.Name, "function", func.Span, source);
            }
        }

        foreach (var cls in ast.Classes)
        {
            if (cls.Name == identifier)
            {
                return CreateDefinitionInfo(cls.Name, "class", cls.Span, source);
            }
        }

        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == identifier)
            {
                return CreateDefinitionInfo(iface.Name, "interface", iface.Span, source);
            }
        }

        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == identifier)
            {
                return CreateDefinitionInfo(enumDef.Name, "enum", enumDef.Span, source);
            }
        }

        foreach (var del in ast.Delegates)
        {
            if (del.Name == identifier)
            {
                return CreateDefinitionInfo(del.Name, "delegate", del.Span, source);
            }
        }

        return null;
    }

    private static string? ExtractIdentifierAtPosition(string source, int line, int column)
    {
        var offset = CalorSourceHelper.GetOffset(source, line, column);
        if (offset < 0 || offset >= source.Length)
            return null;

        // Find the start of the identifier
        var start = offset;
        while (start > 0 && IsIdentifierChar(source[start - 1]))
            start--;

        // Find the end of the identifier
        var end = offset;
        while (end < source.Length && IsIdentifierChar(source[end]))
            end++;

        if (start == end)
            return null;

        return source.Substring(start, end - start);
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsPositionInNode(int line, Parsing.TextSpan span)
    {
        return line >= span.Line;
    }

    private static DefinitionInfo CreateDefinitionInfo(string name, string kind, Parsing.TextSpan span, string source)
    {
        return new DefinitionInfo
        {
            Name = name,
            Kind = kind,
            Line = span.Line,
            Column = span.Column,
            Preview = CalorSourceHelper.GetPreview(source, span.Line)
        };
    }

    private sealed class DefinitionInfo
    {
        public required string Name { get; init; }
        public required string Kind { get; init; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string? Preview { get; init; }
    }

    private sealed class GotoDefinitionOutput
    {
        [JsonPropertyName("found")]
        public bool Found { get; init; }

        [JsonPropertyName("filePath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FilePath { get; init; }

        [JsonPropertyName("line")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Column { get; init; }

        [JsonPropertyName("symbolName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SymbolName { get; init; }

        [JsonPropertyName("symbolKind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SymbolKind { get; init; }

        [JsonPropertyName("preview")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Preview { get; init; }

        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }
    }
}
