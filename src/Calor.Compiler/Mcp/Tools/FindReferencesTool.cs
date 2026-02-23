using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for finding all references to a symbol.
/// Given a position, finds the symbol and all places it's used.
/// </summary>
public sealed class FindReferencesTool : McpToolBase
{
    public override string Name => "calor_find_references";

    public override string Description =>
        "Find all references to a symbol at a given position. " +
        "Returns all locations where the symbol is used, including the definition. " +
        "Useful for understanding impact before refactoring.";

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
                "symbolId": {
                    "type": "string",
                    "description": "Calor unique ID of the symbol (e.g., 'f001'). Use this OR symbolName OR line+column"
                },
                "symbolName": {
                    "type": "string",
                    "description": "Name of the symbol to find. Use this OR symbolId OR line+column"
                },
                "line": {
                    "type": "integer",
                    "description": "Line number (1-based) where the symbol is located"
                },
                "column": {
                    "type": "integer",
                    "description": "Column number (1-based) where the symbol is located"
                },
                "includeDefinition": {
                    "type": "boolean",
                    "description": "Include the definition location in results (default: true)"
                },
                "groupByKind": {
                    "type": "boolean",
                    "description": "Group results by kind (definition, call_site, type_usage, reference) (default: false)"
                }
            }
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var symbolId = GetString(arguments, "symbolId");
        var symbolName = GetString(arguments, "symbolName");
        var line = GetInt(arguments, "line", 0);
        var column = GetInt(arguments, "column", 0);
        var includeDefinition = GetBool(arguments, "includeDefinition", true);
        var groupByKind = GetBool(arguments, "groupByKind", false);

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
        {
            return McpToolResult.Error("Either 'source' or 'filePath' is required");
        }

        // Need at least one way to identify the symbol
        if (string.IsNullOrEmpty(symbolId) && string.IsNullOrEmpty(symbolName) && (line <= 0 || column <= 0))
        {
            return McpToolResult.Error("Provide 'symbolId', 'symbolName', or both 'line' and 'column'");
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
            return McpToolResult.Json(new FindReferencesOutput
            {
                Success = false,
                Errors = parseResult.Errors.ToList()
            }, isError: true);
        }

        // Resolve symbol name from ID, name, or position
        string? identifier = null;

        if (!string.IsNullOrEmpty(symbolId))
        {
            // Look up by Calor unique ID
            identifier = ResolveNameFromId(parseResult.Ast!, symbolId);
            if (string.IsNullOrEmpty(identifier))
            {
                return McpToolResult.Json(new FindReferencesOutput
                {
                    Success = false,
                    Message = $"No symbol found with ID '{symbolId}'"
                });
            }
        }
        else if (!string.IsNullOrEmpty(symbolName))
        {
            identifier = symbolName;
        }
        else if (line > 0 && column > 0)
        {
            identifier = ExtractIdentifierAtPosition(parseResult.Source!, line, column);
            if (string.IsNullOrEmpty(identifier))
            {
                return McpToolResult.Json(new FindReferencesOutput
                {
                    Success = false,
                    Message = $"No symbol found at line {line}, column {column}"
                });
            }
        }

        // Find all references to this identifier
        var references = FindReferences(parseResult.Ast!, parseResult.Source!, identifier!, filePath, includeDefinition);

        if (groupByKind)
        {
            var grouped = references.GroupBy(r => r.Kind ?? "reference")
                .ToDictionary(g => g.Key, g => g.ToList());

            return McpToolResult.Json(new FindReferencesGroupedOutput
            {
                Success = true,
                SymbolName = identifier,
                SymbolId = symbolId,
                ReferenceCount = references.Count,
                GroupedReferences = grouped
            });
        }

        return McpToolResult.Json(new FindReferencesOutput
        {
            Success = true,
            SymbolName = identifier,
            SymbolId = symbolId,
            ReferenceCount = references.Count,
            References = references
        });
    }

    private static string? ResolveNameFromId(ModuleNode ast, string id)
    {
        foreach (var func in ast.Functions)
        {
            if (func.Id == id) return func.Name;
        }
        foreach (var cls in ast.Classes)
        {
            if (cls.Name == id) return cls.Name;
            foreach (var method in cls.Methods)
            {
                if (method.Id == id || $"{cls.Name}.{method.Id}" == id) return method.Name;
            }
            foreach (var field in cls.Fields)
            {
                if (field.Name == id) return field.Name;
            }
        }
        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == id) return iface.Name;
        }
        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == id) return enumDef.Name;
        }
        return null;
    }

    private static List<ReferenceLocation> FindReferences(ModuleNode ast, string source, string symbolName, string? filePath, bool includeDefinition)
    {
        var references = new List<ReferenceLocation>();

        // Find the definition first
        var definitionKind = FindDefinitionKind(ast, symbolName);

        // Scan the entire source for occurrences of the identifier
        var lines = source.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var lineContent = lines[lineIndex];
            var searchStart = 0;

            while (true)
            {
                var pos = lineContent.IndexOf(symbolName, searchStart, StringComparison.Ordinal);
                if (pos < 0)
                    break;

                // Check that it's a complete identifier (not part of a larger word)
                var charBefore = pos > 0 ? lineContent[pos - 1] : ' ';
                var charAfter = pos + symbolName.Length < lineContent.Length ? lineContent[pos + symbolName.Length] : ' ';

                if (!IsIdentifierChar(charBefore) && !IsIdentifierChar(charAfter))
                {
                    var refLine = lineIndex + 1;
                    var refColumn = pos + 1;

                    // Determine if this is the definition
                    var isDefinition = IsDefinitionLocation(ast, symbolName, refLine);

                    if (isDefinition && !includeDefinition)
                    {
                        searchStart = pos + 1;
                        continue;
                    }

                    // Get context - the line content trimmed
                    var context = lineContent.Trim();
                    if (context.Length > 100)
                        context = context.Substring(0, 100) + "...";

                    references.Add(new ReferenceLocation
                    {
                        FilePath = filePath,
                        Line = refLine,
                        Column = refColumn,
                        IsDefinition = isDefinition,
                        Context = context,
                        Kind = isDefinition ? definitionKind : "reference"
                    });
                }

                searchStart = pos + 1;
            }
        }

        // Sort by line number
        references.Sort((a, b) => a.Line.CompareTo(b.Line));

        return references;
    }

    private static string? FindDefinitionKind(ModuleNode ast, string name)
    {
        foreach (var func in ast.Functions)
        {
            if (func.Name == name)
                return "function";
        }

        foreach (var cls in ast.Classes)
        {
            if (cls.Name == name)
                return "class";

            foreach (var method in cls.Methods)
            {
                if (method.Name == name)
                    return "method";
            }

            foreach (var field in cls.Fields)
            {
                if (field.Name == name)
                    return "field";
            }

            foreach (var prop in cls.Properties)
            {
                if (prop.Name == name)
                    return "property";
            }
        }

        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == name)
                return "interface";
        }

        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == name)
                return "enum";
        }

        return null;
    }

    private static bool IsDefinitionLocation(ModuleNode ast, string name, int line)
    {
        // Check if this line contains a definition of the symbol

        foreach (var func in ast.Functions)
        {
            if (func.Name == name && func.Span.Line == line)
                return true;

            // Check parameters (defined on the same line as function)
            foreach (var param in func.Parameters)
            {
                if (param.Name == name && param.Span.Line == line)
                    return true;
            }

            // Check local bindings
            foreach (var stmt in func.Body)
            {
                if (stmt is BindStatementNode bind && bind.Name == name && bind.Span.Line == line)
                    return true;
            }
        }

        foreach (var cls in ast.Classes)
        {
            if (cls.Name == name && cls.Span.Line == line)
                return true;

            foreach (var field in cls.Fields)
            {
                if (field.Name == name && field.Span.Line == line)
                    return true;
            }

            foreach (var prop in cls.Properties)
            {
                if (prop.Name == name && prop.Span.Line == line)
                    return true;
            }

            foreach (var method in cls.Methods)
            {
                if (method.Name == name && method.Span.Line == line)
                    return true;

                foreach (var param in method.Parameters)
                {
                    if (param.Name == name && param.Span.Line == line)
                        return true;
                }

                foreach (var stmt in method.Body)
                {
                    if (stmt is BindStatementNode bind && bind.Name == name && bind.Span.Line == line)
                        return true;
                }
            }
        }

        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == name && iface.Span.Line == line)
                return true;
        }

        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == name && enumDef.Span.Line == line)
                return true;

            foreach (var member in enumDef.Members)
            {
                if (member.Name == name && member.Span.Line == line)
                    return true;
            }
        }

        return false;
    }

    private static string? ExtractIdentifierAtPosition(string source, int line, int column)
    {
        var offset = CalorSourceHelper.GetOffset(source, line, column);
        if (offset < 0 || offset >= source.Length)
            return null;

        var start = offset;
        while (start > 0 && IsIdentifierChar(source[start - 1]))
            start--;

        var end = offset;
        while (end < source.Length && IsIdentifierChar(source[end]))
            end++;

        if (start == end)
            return null;

        return source.Substring(start, end - start);
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private sealed class FindReferencesOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("symbolName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SymbolName { get; init; }

        [JsonPropertyName("symbolId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SymbolId { get; init; }

        [JsonPropertyName("referenceCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int ReferenceCount { get; init; }

        [JsonPropertyName("references")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ReferenceLocation>? References { get; init; }

        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }
    }

    private sealed class FindReferencesGroupedOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("symbolName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SymbolName { get; init; }

        [JsonPropertyName("symbolId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SymbolId { get; init; }

        [JsonPropertyName("referenceCount")]
        public int ReferenceCount { get; init; }

        [JsonPropertyName("groupedReferences")]
        public Dictionary<string, List<ReferenceLocation>>? GroupedReferences { get; init; }
    }

    private sealed class ReferenceLocation
    {
        [JsonPropertyName("filePath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FilePath { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }

        [JsonPropertyName("isDefinition")]
        public bool IsDefinition { get; init; }

        [JsonPropertyName("kind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Kind { get; init; }

        [JsonPropertyName("context")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Context { get; init; }
    }
}
