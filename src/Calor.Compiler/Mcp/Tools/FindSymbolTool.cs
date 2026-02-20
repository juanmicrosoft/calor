using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for searching symbols by name across Calor files.
/// Can search a single file or a directory of .calr files.
/// </summary>
public sealed class FindSymbolTool : McpToolBase
{
    public override string Name => "calor_find_symbol";

    public override string Description =>
        "Search for symbols by name in Calor source files. " +
        "Can search in inline source, a single file, or a directory of .calr files. " +
        "Returns matching symbols with their kind, location, and file path.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Symbol name to search for (case-insensitive partial match)"
                },
                "source": {
                    "type": "string",
                    "description": "Calor source code to search in"
                },
                "filePath": {
                    "type": "string",
                    "description": "Path to a .calr file to search in"
                },
                "directory": {
                    "type": "string",
                    "description": "Directory to search for .calr files (recursive)"
                },
                "kind": {
                    "type": "string",
                    "description": "Filter by symbol kind: function, class, interface, enum, method, field, property"
                },
                "limit": {
                    "type": "integer",
                    "description": "Maximum number of results to return (default: 50)"
                }
            },
            "required": ["query"]
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var query = GetString(arguments, "query");
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var directory = GetString(arguments, "directory");
        var kindFilter = GetString(arguments, "kind");
        var limit = GetInt(arguments, "limit", 50);

        if (string.IsNullOrEmpty(query))
        {
            return McpToolResult.Error("Missing required parameter: query");
        }

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath) && string.IsNullOrEmpty(directory))
        {
            return McpToolResult.Error("At least one of 'source', 'filePath', or 'directory' is required");
        }

        var results = new List<SymbolMatch>();

        // Search inline source
        if (!string.IsNullOrEmpty(source))
        {
            var parseResult = CalorSourceHelper.Parse(source, "inline");
            if (parseResult.IsSuccess)
            {
                SearchAst(parseResult.Ast!, null, query, kindFilter, results);
            }
        }

        // Search single file
        if (!string.IsNullOrEmpty(filePath))
        {
            await SearchFileAsync(filePath, query, kindFilter, results);
        }

        // Search directory
        if (!string.IsNullOrEmpty(directory))
        {
            await SearchDirectoryAsync(directory, query, kindFilter, results, limit);
        }

        // Apply limit
        if (results.Count > limit)
        {
            results = results.Take(limit).ToList();
        }

        return McpToolResult.Json(new FindSymbolOutput
        {
            Success = true,
            Query = query,
            MatchCount = results.Count,
            Matches = results
        });
    }

    private static async Task SearchFileAsync(string filePath, string query, string? kindFilter, List<SymbolMatch> results)
    {
        if (!File.Exists(filePath))
            return;

        var parseResult = await CalorSourceHelper.ParseFileAsync(filePath);
        if (parseResult.IsSuccess)
        {
            SearchAst(parseResult.Ast!, filePath, query, kindFilter, results);
        }
    }

    private static async Task SearchDirectoryAsync(string directory, string query, string? kindFilter, List<SymbolMatch> results, int limit)
    {
        if (!Directory.Exists(directory))
            return;

        var files = Directory.GetFiles(directory, "*.calr", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            if (results.Count >= limit)
                break;

            await SearchFileAsync(file, query, kindFilter, results);
        }
    }

    private static void SearchAst(ModuleNode ast, string? filePath, string query, string? kindFilter, List<SymbolMatch> results)
    {
        var queryLower = query.ToLowerInvariant();

        // Search functions
        if (kindFilter == null || kindFilter == "function")
        {
            foreach (var func in ast.Functions)
            {
                if (MatchesQuery(func.Name, queryLower))
                {
                    results.Add(new SymbolMatch
                    {
                        Name = func.Name,
                        Kind = "function",
                        FilePath = filePath,
                        Line = func.Span.Line,
                        Column = func.Span.Column,
                        Detail = BuildFunctionDetail(func)
                    });
                }
            }
        }

        // Search classes
        if (kindFilter == null || kindFilter == "class")
        {
            foreach (var cls in ast.Classes)
            {
                if (MatchesQuery(cls.Name, queryLower))
                {
                    results.Add(new SymbolMatch
                    {
                        Name = cls.Name,
                        Kind = "class",
                        FilePath = filePath,
                        Line = cls.Span.Line,
                        Column = cls.Span.Column,
                        Detail = cls.BaseClass != null ? $": {cls.BaseClass}" : null
                    });
                }

                // Search class members
                if (kindFilter == null || kindFilter == "method")
                {
                    foreach (var method in cls.Methods)
                    {
                        if (MatchesQuery(method.Name, queryLower))
                        {
                            results.Add(new SymbolMatch
                            {
                                Name = method.Name,
                                Kind = "method",
                                ContainerName = cls.Name,
                                FilePath = filePath,
                                Line = method.Span.Line,
                                Column = method.Span.Column,
                                Detail = BuildMethodDetail(method)
                            });
                        }
                    }
                }

                if (kindFilter == null || kindFilter == "field")
                {
                    foreach (var field in cls.Fields)
                    {
                        if (MatchesQuery(field.Name, queryLower))
                        {
                            results.Add(new SymbolMatch
                            {
                                Name = field.Name,
                                Kind = "field",
                                ContainerName = cls.Name,
                                FilePath = filePath,
                                Line = field.Span.Line,
                                Column = field.Span.Column,
                                Detail = field.TypeName
                            });
                        }
                    }
                }

                if (kindFilter == null || kindFilter == "property")
                {
                    foreach (var prop in cls.Properties)
                    {
                        if (MatchesQuery(prop.Name, queryLower))
                        {
                            results.Add(new SymbolMatch
                            {
                                Name = prop.Name,
                                Kind = "property",
                                ContainerName = cls.Name,
                                FilePath = filePath,
                                Line = prop.Span.Line,
                                Column = prop.Span.Column,
                                Detail = prop.TypeName
                            });
                        }
                    }
                }
            }
        }

        // Search interfaces
        if (kindFilter == null || kindFilter == "interface")
        {
            foreach (var iface in ast.Interfaces)
            {
                if (MatchesQuery(iface.Name, queryLower))
                {
                    results.Add(new SymbolMatch
                    {
                        Name = iface.Name,
                        Kind = "interface",
                        FilePath = filePath,
                        Line = iface.Span.Line,
                        Column = iface.Span.Column
                    });
                }

                // Search interface properties
                if (kindFilter == null || kindFilter == "property")
                {
                    foreach (var prop in iface.Properties)
                    {
                        if (MatchesQuery(prop.Name, queryLower))
                        {
                            results.Add(new SymbolMatch
                            {
                                Name = prop.Name,
                                Kind = "property",
                                ContainerName = iface.Name,
                                FilePath = filePath,
                                Line = prop.Span.Line,
                                Column = prop.Span.Column
                            });
                        }
                    }
                }

                // Search interface methods
                if (kindFilter == null || kindFilter == "method")
                {
                    foreach (var method in iface.Methods)
                    {
                        if (MatchesQuery(method.Name, queryLower))
                        {
                            results.Add(new SymbolMatch
                            {
                                Name = method.Name,
                                Kind = "method signature",
                                ContainerName = iface.Name,
                                FilePath = filePath,
                                Line = method.Span.Line,
                                Column = method.Span.Column
                            });
                        }
                    }
                }
            }
        }

        // Search enums
        if (kindFilter == null || kindFilter == "enum")
        {
            foreach (var enumDef in ast.Enums)
            {
                if (MatchesQuery(enumDef.Name, queryLower))
                {
                    results.Add(new SymbolMatch
                    {
                        Name = enumDef.Name,
                        Kind = "enum",
                        FilePath = filePath,
                        Line = enumDef.Span.Line,
                        Column = enumDef.Span.Column,
                        Detail = enumDef.UnderlyingType
                    });
                }

                // Search enum members
                foreach (var member in enumDef.Members)
                {
                    if (MatchesQuery(member.Name, queryLower))
                    {
                        results.Add(new SymbolMatch
                        {
                            Name = member.Name,
                            Kind = "enum member",
                            ContainerName = enumDef.Name,
                            FilePath = filePath,
                            Line = member.Span.Line,
                            Column = member.Span.Column,
                            Detail = member.Value
                        });
                    }
                }
            }
        }

        // Search delegates
        if (kindFilter == null || kindFilter == "delegate")
        {
            foreach (var del in ast.Delegates)
            {
                if (MatchesQuery(del.Name, queryLower))
                {
                    results.Add(new SymbolMatch
                    {
                        Name = del.Name,
                        Kind = "delegate",
                        FilePath = filePath,
                        Line = del.Span.Line,
                        Column = del.Span.Column,
                        Detail = BuildDelegateDetail(del)
                    });
                }
            }
        }
    }

    private static bool MatchesQuery(string name, string queryLower)
    {
        return name.ToLowerInvariant().Contains(queryLower);
    }

    private static string BuildFunctionDetail(FunctionNode func)
    {
        var returnType = func.Output?.TypeName ?? "void";
        return $"-> {returnType}";
    }

    private static string BuildMethodDetail(MethodNode method)
    {
        var returnType = method.Output?.TypeName ?? "void";
        return $"-> {returnType}";
    }

    private static string BuildDelegateDetail(DelegateDefinitionNode del)
    {
        var returnType = del.Output?.TypeName ?? "void";
        return $"-> {returnType}";
    }

    private sealed class FindSymbolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("matchCount")]
        public int MatchCount { get; init; }

        [JsonPropertyName("matches")]
        public required List<SymbolMatch> Matches { get; init; }
    }

    private sealed class SymbolMatch
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("containerName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ContainerName { get; init; }

        [JsonPropertyName("filePath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FilePath { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }

        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; init; }
    }
}
