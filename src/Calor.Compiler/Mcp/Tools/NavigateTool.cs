using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Consolidated MCP tool for navigating Calor source code.
/// Supports go-to-definition, find-references, symbol search, type info, and scope inspection.
/// </summary>
public sealed class NavigateTool : McpToolBase
{
    public override string Name => "calor_navigate";

    public override string Description =>
        "Navigate Calor source code \u2014 go to definition, find references, search symbols, get type info, or inspect scope";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["definition", "references", "find", "info", "scope"],
                    "description": "Navigation action. definition=jump to symbol definition, references=find all usages, find=search symbols by name, info=get type/contract info at position, scope=list what is in scope at position"
                },
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
                },
                "query": {
                    "type": "string",
                    "description": "Symbol name to search for (action=find, case-insensitive partial match)"
                },
                "symbolId": {
                    "type": "string",
                    "description": "Calor unique ID of the symbol (action=references)"
                },
                "kind": {
                    "type": "string",
                    "description": "Filter by symbol kind: function, class, interface, enum, method, field, property (action=find)"
                },
                "includeTypes": {
                    "type": "boolean",
                    "description": "Include type information for variables (action=scope, default: true)"
                },
                "includeDetails": {
                    "type": "boolean",
                    "description": "Include extra details such as contracts and grouped references (action=references uses this as groupByKind; action=scope uses this as includeContracts; default varies by action)"
                }
            },
            "required": ["action"],
            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var action = GetString(arguments, "action");

        return action switch
        {
            "definition" => await HandleDefinition(arguments),
            "references" => await HandleReferences(arguments),
            "find" => await HandleFind(arguments),
            "info" => await HandleInfo(arguments),
            "scope" => await HandleScope(arguments),
            _ => McpToolResult.Error("Invalid 'action'. Must be one of: definition, references, find, info, scope")
        };
    }

    // ── definition ──────────────────────────────────────────

    private async Task<McpToolResult> HandleDefinition(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var line = GetInt(arguments, "line", 0);
        var column = GetInt(arguments, "column", 0);

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
            return McpToolResult.Error("Either 'source' or 'filePath' is required");

        if (line <= 0 || column <= 0)
            return McpToolResult.Error("Both 'line' and 'column' must be positive integers");

        ParseResult parseResult;
        if (!string.IsNullOrEmpty(filePath))
            parseResult = await CalorSourceHelper.ParseFileAsync(filePath);
        else
            parseResult = CalorSourceHelper.Parse(source!, filePath);

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
        var identifier = ExtractIdentifierAtPosition(source, line, column);
        if (string.IsNullOrEmpty(identifier))
            return null;

        // 1. Check if inside a function - look for parameters and locals
        foreach (var func in ast.Functions)
        {
            if (IsPositionInNode(line, func.Span))
            {
                if (func.Name == identifier)
                    return CreateDefinitionInfo(func.Name, "function", func.Span, source);

                foreach (var param in func.Parameters)
                {
                    if (param.Name == identifier)
                        return CreateDefinitionInfo(param.Name, "parameter", param.Span, source);
                }

                foreach (var stmt in func.Body)
                {
                    if (stmt is BindStatementNode bind && bind.Name == identifier && bind.Span.Line <= line)
                        return CreateDefinitionInfo(bind.Name, bind.IsMutable ? "mutable variable" : "variable", bind.Span, source);
                }
            }
        }

        // 2. Check if inside a class
        foreach (var cls in ast.Classes)
        {
            if (IsPositionInNode(line, cls.Span))
            {
                if (cls.Name == identifier)
                    return CreateDefinitionInfo(cls.Name, "class", cls.Span, source);

                foreach (var field in cls.Fields)
                {
                    if (field.Name == identifier)
                        return CreateDefinitionInfo(field.Name, "field", field.Span, source);
                }

                foreach (var prop in cls.Properties)
                {
                    if (prop.Name == identifier)
                        return CreateDefinitionInfo(prop.Name, "property", prop.Span, source);
                }

                foreach (var method in cls.Methods)
                {
                    if (method.Name == identifier)
                        return CreateDefinitionInfo(method.Name, "method", method.Span, source);

                    if (IsPositionInNode(line, method.Span))
                    {
                        foreach (var param in method.Parameters)
                        {
                            if (param.Name == identifier)
                                return CreateDefinitionInfo(param.Name, "parameter", param.Span, source);
                        }

                        foreach (var stmt in method.Body)
                        {
                            if (stmt is BindStatementNode bind && bind.Name == identifier && bind.Span.Line <= line)
                                return CreateDefinitionInfo(bind.Name, bind.IsMutable ? "mutable variable" : "variable", bind.Span, source);
                        }
                    }
                }
            }
        }

        // 3. Search module-level definitions
        foreach (var func in ast.Functions)
        {
            if (func.Name == identifier)
                return CreateDefinitionInfo(func.Name, "function", func.Span, source);
        }

        foreach (var cls in ast.Classes)
        {
            if (cls.Name == identifier)
                return CreateDefinitionInfo(cls.Name, "class", cls.Span, source);
        }

        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == identifier)
                return CreateDefinitionInfo(iface.Name, "interface", iface.Span, source);
        }

        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == identifier)
                return CreateDefinitionInfo(enumDef.Name, "enum", enumDef.Span, source);
        }

        foreach (var del in ast.Delegates)
        {
            if (del.Name == identifier)
                return CreateDefinitionInfo(del.Name, "delegate", del.Span, source);
        }

        return null;
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

    // ── references ──────────────────────────────────────────

    private async Task<McpToolResult> HandleReferences(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var symbolId = GetString(arguments, "symbolId");
        var symbolName = GetString(arguments, "query");
        var line = GetInt(arguments, "line", 0);
        var column = GetInt(arguments, "column", 0);
        var groupByKind = GetBool(arguments, "includeDetails", false);
        var includeDefinition = GetBool(arguments, "includeDefinition", true);

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
            return McpToolResult.Error("Either 'source' or 'filePath' is required");

        if (string.IsNullOrEmpty(symbolId) && string.IsNullOrEmpty(symbolName) && (line <= 0 || column <= 0))
            return McpToolResult.Error("Provide 'symbolId', 'query' (symbol name), or both 'line' and 'column'");

        ParseResult parseResult;
        if (!string.IsNullOrEmpty(filePath))
            parseResult = await CalorSourceHelper.ParseFileAsync(filePath);
        else
            parseResult = CalorSourceHelper.Parse(source!, filePath);

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

        var references = FindReferences(parseResult.Ast!, parseResult.Source!, identifier!, filePath);

        if (!includeDefinition)
            references = references.Where(r => !r.IsDefinition).ToList();

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

    private static List<ReferenceLocation> FindReferences(ModuleNode ast, string source, string symbolName, string? filePath)
    {
        var references = new List<ReferenceLocation>();
        var definitionKind = FindDefinitionKind(ast, symbolName);

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

                var charBefore = pos > 0 ? lineContent[pos - 1] : ' ';
                var charAfter = pos + symbolName.Length < lineContent.Length ? lineContent[pos + symbolName.Length] : ' ';

                if (!IsIdentifierChar(charBefore) && !IsIdentifierChar(charAfter))
                {
                    var refLine = lineIndex + 1;
                    var refColumn = pos + 1;
                    var isDefinition = IsDefinitionLocation(ast, symbolName, refLine);

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

        references.Sort((a, b) => a.Line.CompareTo(b.Line));
        return references;
    }

    private static string? FindDefinitionKind(ModuleNode ast, string name)
    {
        foreach (var func in ast.Functions)
        {
            if (func.Name == name) return "function";
        }

        foreach (var cls in ast.Classes)
        {
            if (cls.Name == name) return "class";
            foreach (var method in cls.Methods)
            {
                if (method.Name == name) return "method";
            }
            foreach (var field in cls.Fields)
            {
                if (field.Name == name) return "field";
            }
            foreach (var prop in cls.Properties)
            {
                if (prop.Name == name) return "property";
            }
        }

        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == name) return "interface";
        }

        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == name) return "enum";
        }

        return null;
    }

    private static bool IsDefinitionLocation(ModuleNode ast, string name, int line)
    {
        foreach (var func in ast.Functions)
        {
            if (func.Name == name && func.Span.Line == line) return true;
            foreach (var param in func.Parameters)
            {
                if (param.Name == name && param.Span.Line == line) return true;
            }
            foreach (var stmt in func.Body)
            {
                if (stmt is BindStatementNode bind && bind.Name == name && bind.Span.Line == line) return true;
            }
        }

        foreach (var cls in ast.Classes)
        {
            if (cls.Name == name && cls.Span.Line == line) return true;
            foreach (var field in cls.Fields)
            {
                if (field.Name == name && field.Span.Line == line) return true;
            }
            foreach (var prop in cls.Properties)
            {
                if (prop.Name == name && prop.Span.Line == line) return true;
            }
            foreach (var method in cls.Methods)
            {
                if (method.Name == name && method.Span.Line == line) return true;
                foreach (var param in method.Parameters)
                {
                    if (param.Name == name && param.Span.Line == line) return true;
                }
                foreach (var stmt in method.Body)
                {
                    if (stmt is BindStatementNode bind && bind.Name == name && bind.Span.Line == line) return true;
                }
            }
        }

        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == name && iface.Span.Line == line) return true;
        }

        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == name && enumDef.Span.Line == line) return true;
            foreach (var member in enumDef.Members)
            {
                if (member.Name == name && member.Span.Line == line) return true;
            }
        }

        return false;
    }

    // ── find ────────────────────────────────────────────────

    private async Task<McpToolResult> HandleFind(JsonElement? arguments)
    {
        var query = GetString(arguments, "query");
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var kindFilter = GetString(arguments, "kind");
        var limit = GetInt(arguments, "limit", 50);

        if (string.IsNullOrEmpty(query))
            return McpToolResult.Error("Missing required parameter: query");

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
            return McpToolResult.Error("At least one of 'source' or 'filePath' is required");

        var results = new List<SymbolMatch>();

        if (!string.IsNullOrEmpty(source))
        {
            var parseResult = CalorSourceHelper.Parse(source, "inline");
            if (parseResult.IsSuccess)
                SearchAst(parseResult.Ast!, null, query, kindFilter, results);
        }

        if (!string.IsNullOrEmpty(filePath))
            await SearchFileAsync(filePath, query, kindFilter, results);

        if (results.Count > limit)
            results = results.Take(limit).ToList();

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
            SearchAst(parseResult.Ast!, filePath, query, kindFilter, results);
    }

    private static void SearchAst(ModuleNode ast, string? filePath, string query, string? kindFilter, List<SymbolMatch> results)
    {
        var queryLower = query.ToLowerInvariant();

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

    private static bool MatchesQuery(string name, string queryLower) =>
        name.ToLowerInvariant().Contains(queryLower);

    private static string BuildFunctionDetail(FunctionNode func) =>
        $"-> {func.Output?.TypeName ?? "void"}";

    private static string BuildMethodDetail(MethodNode method) =>
        $"-> {method.Output?.TypeName ?? "void"}";

    private static string BuildDelegateDetail(DelegateDefinitionNode del) =>
        $"-> {del.Output?.TypeName ?? "void"}";

    // ── info ────────────────────────────────────────────────

    private async Task<McpToolResult> HandleInfo(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var line = GetInt(arguments, "line", 0);
        var column = GetInt(arguments, "column", 0);

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
            return McpToolResult.Error("Either 'source' or 'filePath' is required");

        if (line <= 0 || column <= 0)
            return McpToolResult.Error("Both 'line' and 'column' must be positive integers");

        ParseResult parseResult;
        if (!string.IsNullOrEmpty(filePath))
            parseResult = await CalorSourceHelper.ParseFileAsync(filePath);
        else
            parseResult = CalorSourceHelper.Parse(source!, filePath);

        if (!parseResult.IsSuccess)
        {
            return McpToolResult.Json(new SymbolInfoOutput
            {
                Found = false,
                Errors = parseResult.Errors.ToList()
            }, isError: true);
        }

        var info = GetSymbolInfoAtPosition(parseResult.Ast!, parseResult.Source!, line, column);

        if (info == null)
        {
            return McpToolResult.Json(new SymbolInfoOutput
            {
                Found = false,
                Message = $"No symbol found at line {line}, column {column}"
            });
        }

        return McpToolResult.Json(info);
    }

    private static SymbolInfoOutput? GetSymbolInfoAtPosition(ModuleNode ast, string source, int line, int column)
    {
        var identifier = ExtractIdentifierAtPosition(source, line, column);
        if (string.IsNullOrEmpty(identifier))
            return null;

        // 1. Check functions
        foreach (var func in ast.Functions)
        {
            if (func.Name == identifier)
                return BuildFunctionInfo(func);

            if (IsPositionInNode(line, func.Span))
            {
                foreach (var param in func.Parameters)
                {
                    if (param.Name == identifier)
                    {
                        return new SymbolInfoOutput
                        {
                            Found = true,
                            Name = param.Name,
                            Kind = "parameter",
                            Type = param.TypeName,
                            Signature = $"(parameter) {param.Name}: {param.TypeName}"
                        };
                    }
                }

                foreach (var stmt in func.Body)
                {
                    if (stmt is BindStatementNode bind && bind.Name == identifier && bind.Span.Line <= line)
                    {
                        return new SymbolInfoOutput
                        {
                            Found = true,
                            Name = bind.Name,
                            Kind = bind.IsMutable ? "mutable variable" : "variable",
                            Type = bind.TypeName,
                            Signature = $"({(bind.IsMutable ? "mutable " : "")}variable) {bind.Name}: {bind.TypeName ?? "inferred"}"
                        };
                    }
                }
            }
        }

        // 2. Check classes
        foreach (var cls in ast.Classes)
        {
            if (cls.Name == identifier)
                return BuildClassInfo(cls);

            if (IsPositionInNode(line, cls.Span))
            {
                foreach (var field in cls.Fields)
                {
                    if (field.Name == identifier)
                    {
                        return new SymbolInfoOutput
                        {
                            Found = true,
                            Name = field.Name,
                            Kind = "field",
                            Type = field.TypeName,
                            Visibility = field.Visibility.ToString().ToLower(),
                            Signature = $"(field) {field.Name}: {field.TypeName}"
                        };
                    }
                }

                foreach (var prop in cls.Properties)
                {
                    if (prop.Name == identifier)
                    {
                        return new SymbolInfoOutput
                        {
                            Found = true,
                            Name = prop.Name,
                            Kind = "property",
                            Type = prop.TypeName,
                            Signature = $"(property) {prop.Name}: {prop.TypeName}"
                        };
                    }
                }

                foreach (var method in cls.Methods)
                {
                    if (method.Name == identifier)
                        return BuildMethodInfo(method);

                    if (IsPositionInNode(line, method.Span))
                    {
                        foreach (var param in method.Parameters)
                        {
                            if (param.Name == identifier)
                            {
                                return new SymbolInfoOutput
                                {
                                    Found = true,
                                    Name = param.Name,
                                    Kind = "parameter",
                                    Type = param.TypeName,
                                    Signature = $"(parameter) {param.Name}: {param.TypeName}"
                                };
                            }
                        }
                    }
                }
            }
        }

        // 3. Check interfaces
        foreach (var iface in ast.Interfaces)
        {
            if (iface.Name == identifier)
            {
                return new SymbolInfoOutput
                {
                    Found = true,
                    Name = iface.Name,
                    Kind = "interface",
                    Signature = $"interface {iface.Name}",
                    MemberCount = iface.Methods.Count
                };
            }
        }

        // 4. Check enums
        foreach (var enumDef in ast.Enums)
        {
            if (enumDef.Name == identifier)
            {
                return new SymbolInfoOutput
                {
                    Found = true,
                    Name = enumDef.Name,
                    Kind = "enum",
                    Type = enumDef.UnderlyingType,
                    Signature = $"enum {enumDef.Name}" + (enumDef.UnderlyingType != null ? $" : {enumDef.UnderlyingType}" : ""),
                    MemberCount = enumDef.Members.Count,
                    EnumMembers = enumDef.Members.Select(m => m.Name).ToList()
                };
            }

            foreach (var member in enumDef.Members)
            {
                if (member.Name == identifier)
                {
                    return new SymbolInfoOutput
                    {
                        Found = true,
                        Name = member.Name,
                        Kind = "enum member",
                        Type = enumDef.Name,
                        Signature = $"{enumDef.Name}.{member.Name}" + (member.Value != null ? $" = {member.Value}" : "")
                    };
                }
            }
        }

        // 5. Check delegates
        foreach (var del in ast.Delegates)
        {
            if (del.Name == identifier)
            {
                var parameters = string.Join(", ", del.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
                var returnType = del.Output?.TypeName ?? "void";
                return new SymbolInfoOutput
                {
                    Found = true,
                    Name = del.Name,
                    Kind = "delegate",
                    Type = returnType,
                    Signature = $"delegate {del.Name}({parameters}) -> {returnType}"
                };
            }
        }

        return null;
    }

    private static SymbolInfoOutput BuildFunctionInfo(FunctionNode func)
    {
        var parameters = string.Join(", ", func.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = func.Output?.TypeName ?? "void";
        var asyncPrefix = func.IsAsync ? "async " : "";

        var contracts = new List<ContractInfo>();
        foreach (var pre in func.Preconditions)
            contracts.Add(new ContractInfo { Type = "requires", Expression = FormatExpression(pre.Condition) });
        foreach (var post in func.Postconditions)
            contracts.Add(new ContractInfo { Type = "ensures", Expression = FormatExpression(post.Condition) });

        var effects = func.Effects?.Effects.Values.ToList();

        return new SymbolInfoOutput
        {
            Found = true,
            Name = func.Name,
            Kind = "function",
            Type = returnType,
            Visibility = func.Visibility.ToString().ToLower(),
            IsAsync = func.IsAsync,
            Signature = $"{asyncPrefix}function {func.Name}({parameters}) -> {returnType}",
            Parameters = func.Parameters.Select(p => new ParameterInfoDto { Name = p.Name, Type = p.TypeName }).ToList(),
            Contracts = contracts.Count > 0 ? contracts : null,
            Effects = effects?.Count > 0 ? effects : null
        };
    }

    private static SymbolInfoOutput BuildMethodInfo(MethodNode method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = method.Output?.TypeName ?? "void";

        var modifiers = new List<string>();
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsVirtual) modifiers.Add("virtual");
        if (method.IsOverride) modifiers.Add("override");
        if (method.IsAbstract) modifiers.Add("abstract");
        if (method.IsAsync) modifiers.Add("async");

        var contracts = new List<ContractInfo>();
        foreach (var pre in method.Preconditions)
            contracts.Add(new ContractInfo { Type = "requires", Expression = FormatExpression(pre.Condition) });
        foreach (var post in method.Postconditions)
            contracts.Add(new ContractInfo { Type = "ensures", Expression = FormatExpression(post.Condition) });

        return new SymbolInfoOutput
        {
            Found = true,
            Name = method.Name,
            Kind = "method",
            Type = returnType,
            Visibility = method.Visibility.ToString().ToLower(),
            IsAsync = method.IsAsync,
            Modifiers = modifiers.Count > 0 ? modifiers : null,
            Signature = $"method {method.Name}({parameters}) -> {returnType}",
            Parameters = method.Parameters.Select(p => new ParameterInfoDto { Name = p.Name, Type = p.TypeName }).ToList(),
            Contracts = contracts.Count > 0 ? contracts : null
        };
    }

    private static SymbolInfoOutput BuildClassInfo(ClassDefinitionNode cls)
    {
        var modifiers = new List<string>();
        if (cls.IsAbstract) modifiers.Add("abstract");
        if (cls.IsSealed) modifiers.Add("sealed");
        if (cls.IsStatic) modifiers.Add("static");
        if (cls.IsPartial) modifiers.Add("partial");

        return new SymbolInfoOutput
        {
            Found = true,
            Name = cls.Name,
            Kind = "class",
            Modifiers = modifiers.Count > 0 ? modifiers : null,
            BaseClass = cls.BaseClass,
            Interfaces = cls.ImplementedInterfaces.Count > 0 ? cls.ImplementedInterfaces.ToList() : null,
            Signature = $"class {cls.Name}" + (cls.BaseClass != null ? $" : {cls.BaseClass}" : ""),
            MemberCount = cls.Fields.Count + cls.Properties.Count + cls.Methods.Count
        };
    }

    private static string FormatExpression(ExpressionNode? expr)
    {
        if (expr == null) return "...";
        return expr.ToString() ?? "...";
    }

    // ── scope ───────────────────────────────────────────────

    private async Task<McpToolResult> HandleScope(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var line = GetInt(arguments, "line", 0);
        var column = GetInt(arguments, "column", 0);
        var includeTypes = GetBool(arguments, "includeTypes", true);
        var includeContracts = GetBool(arguments, "includeDetails", true);

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
            return McpToolResult.Error("Either 'source' or 'filePath' is required");

        if (line <= 0 || column <= 0)
            return McpToolResult.Error("Both 'line' and 'column' must be positive integers");

        ParseResult parseResult;
        if (!string.IsNullOrEmpty(filePath))
            parseResult = await CalorSourceHelper.ParseFileAsync(filePath);
        else
            parseResult = CalorSourceHelper.Parse(source!, filePath);

        if (!parseResult.IsSuccess)
            return McpToolResult.Json(new { success = false, errors = parseResult.Errors.ToList() }, isError: true);

        var ast = parseResult.Ast!;
        var offset = CalorSourceHelper.GetOffset(parseResult.Source!, line, column);

        FunctionNode? enclosingFunction = null;
        ClassDefinitionNode? enclosingClass = null;

        foreach (var func in ast.Functions)
        {
            if (func.Span.Start <= offset && offset <= func.Span.End)
            {
                enclosingFunction = func;
                break;
            }
        }

        if (enclosingFunction == null)
        {
            foreach (var cls in ast.Classes)
            {
                if (cls.Span.Start <= offset && offset <= cls.Span.End)
                {
                    enclosingClass = cls;
                    foreach (var method in cls.Methods)
                    {
                        if (method.Span.Start <= offset && offset <= method.Span.End)
                        {
                            enclosingFunction = new FunctionNode(
                                method.Span, $"{cls.Name}.{method.Id}", method.Name,
                                method.Visibility, method.Parameters, method.Output,
                                method.Effects, method.Preconditions, method.Postconditions,
                                method.Body, method.Attributes);
                            break;
                        }
                    }
                    break;
                }
            }
        }

        var localVariables = new List<VariableInfoDto>();
        var scopeParameters = new List<ScopeParameterInfoDto>();
        var activeContracts = new List<string>();

        if (enclosingFunction != null)
        {
            foreach (var param in enclosingFunction.Parameters)
            {
                scopeParameters.Add(new ScopeParameterInfoDto
                {
                    Name = param.Name,
                    Type = includeTypes ? param.TypeName : null
                });
            }

            foreach (var stmt in enclosingFunction.Body)
            {
                if (stmt.Span.Start > offset) break;

                if (stmt is BindStatementNode bind)
                {
                    localVariables.Add(new VariableInfoDto
                    {
                        Name = bind.Name,
                        Type = includeTypes ? bind.TypeName : null,
                        IsMutable = bind.IsMutable,
                        DefinedAtLine = bind.Span.Line
                    });
                }
            }

            if (includeContracts)
            {
                foreach (var pre in enclosingFunction.Preconditions)
                    activeContracts.Add($"requires: {pre.Message ?? pre.Condition.ToString()}");
                foreach (var post in enclosingFunction.Postconditions)
                    activeContracts.Add($"ensures: {post.Message ?? post.Condition.ToString()}");
            }
        }

        var availableFunctions = new List<ScopeFunctionInfoDto>();
        foreach (var func in ast.Functions)
        {
            availableFunctions.Add(new ScopeFunctionInfoDto
            {
                Id = func.Id,
                Name = func.Name,
                ReturnType = func.Output?.TypeName,
                ParameterCount = func.Parameters.Count
            });
        }

        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                availableFunctions.Add(new ScopeFunctionInfoDto
                {
                    Id = $"{cls.Name}.{method.Id}",
                    Name = $"{cls.Name}.{method.Name}",
                    ReturnType = method.Output?.TypeName,
                    ParameterCount = method.Parameters.Count
                });
            }
        }

        var validInsertions = new List<string>();
        if (enclosingFunction != null)
        {
            validInsertions.Add("statement");
            validInsertions.Add("variable_binding");
            validInsertions.Add("function_call");
            validInsertions.Add("return_statement");
        }
        else if (enclosingClass != null)
        {
            validInsertions.Add("method_definition");
            validInsertions.Add("field_definition");
            validInsertions.Add("property_definition");
        }
        else
        {
            validInsertions.Add("function_definition");
            validInsertions.Add("class_definition");
            validInsertions.Add("using_directive");
        }

        return McpToolResult.Json(new ScopeInfoOutput
        {
            Success = true,
            EnclosingFunction = enclosingFunction != null ? new ScopeFunctionInfoDto
            {
                Id = enclosingFunction.Id,
                Name = enclosingFunction.Name,
                ReturnType = enclosingFunction.Output?.TypeName,
                ParameterCount = enclosingFunction.Parameters.Count
            } : null,
            EnclosingClass = enclosingClass?.Name,
            EnclosingModule = new ModuleInfoDto { Id = ast.Id, Name = ast.Name },
            LocalVariables = localVariables,
            Parameters = scopeParameters,
            AvailableFunctions = availableFunctions,
            ActiveContracts = activeContracts,
            ValidInsertionPoints = validInsertions
        });
    }

    // ── shared helpers ──────────────────────────────────────

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

    private static bool IsPositionInNode(int line, Parsing.TextSpan span) => line >= span.Line;

    // ── DTOs ────────────────────────────────────────────────

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
        [JsonPropertyName("found")] public bool Found { get; init; }
        [JsonPropertyName("filePath")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FilePath { get; init; }
        [JsonPropertyName("line")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Line { get; init; }
        [JsonPropertyName("column")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Column { get; init; }
        [JsonPropertyName("symbolName")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SymbolName { get; init; }
        [JsonPropertyName("symbolKind")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SymbolKind { get; init; }
        [JsonPropertyName("preview")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Preview { get; init; }
        [JsonPropertyName("message")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; init; }
        [JsonPropertyName("errors")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Errors { get; init; }
    }

    private sealed class FindReferencesOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("symbolName")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SymbolName { get; init; }
        [JsonPropertyName("symbolId")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SymbolId { get; init; }
        [JsonPropertyName("referenceCount")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ReferenceCount { get; init; }
        [JsonPropertyName("references")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ReferenceLocation>? References { get; init; }
        [JsonPropertyName("message")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; init; }
        [JsonPropertyName("errors")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Errors { get; init; }
    }

    private sealed class FindReferencesGroupedOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("symbolName")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SymbolName { get; init; }
        [JsonPropertyName("symbolId")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SymbolId { get; init; }
        [JsonPropertyName("referenceCount")] public int ReferenceCount { get; init; }
        [JsonPropertyName("groupedReferences")] public Dictionary<string, List<ReferenceLocation>>? GroupedReferences { get; init; }
    }

    private sealed class ReferenceLocation
    {
        [JsonPropertyName("filePath")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FilePath { get; init; }
        [JsonPropertyName("line")] public int Line { get; init; }
        [JsonPropertyName("column")] public int Column { get; init; }
        [JsonPropertyName("isDefinition")] public bool IsDefinition { get; init; }
        [JsonPropertyName("kind")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; init; }
        [JsonPropertyName("context")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Context { get; init; }
    }

    private sealed class FindSymbolOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("query")] public required string Query { get; init; }
        [JsonPropertyName("matchCount")] public int MatchCount { get; init; }
        [JsonPropertyName("matches")] public required List<SymbolMatch> Matches { get; init; }
    }

    private sealed class SymbolMatch
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("kind")] public required string Kind { get; init; }
        [JsonPropertyName("containerName")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContainerName { get; init; }
        [JsonPropertyName("filePath")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FilePath { get; init; }
        [JsonPropertyName("line")] public int Line { get; init; }
        [JsonPropertyName("column")] public int Column { get; init; }
        [JsonPropertyName("detail")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Detail { get; init; }
    }

    private sealed class SymbolInfoOutput
    {
        [JsonPropertyName("found")] public bool Found { get; init; }
        [JsonPropertyName("name")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; init; }
        [JsonPropertyName("kind")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; init; }
        [JsonPropertyName("type")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Type { get; init; }
        [JsonPropertyName("visibility")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Visibility { get; init; }
        [JsonPropertyName("isAsync")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsAsync { get; init; }
        [JsonPropertyName("modifiers")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Modifiers { get; init; }
        [JsonPropertyName("signature")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Signature { get; init; }
        [JsonPropertyName("parameters")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ParameterInfoDto>? Parameters { get; init; }
        [JsonPropertyName("contracts")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ContractInfo>? Contracts { get; init; }
        [JsonPropertyName("effects")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Effects { get; init; }
        [JsonPropertyName("baseClass")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BaseClass { get; init; }
        [JsonPropertyName("interfaces")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Interfaces { get; init; }
        [JsonPropertyName("memberCount")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int MemberCount { get; init; }
        [JsonPropertyName("enumMembers")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? EnumMembers { get; init; }
        [JsonPropertyName("message")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; init; }
        [JsonPropertyName("errors")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Errors { get; init; }
    }

    private sealed class ParameterInfoDto
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("type")] public required string Type { get; init; }
    }

    private sealed class ContractInfo
    {
        [JsonPropertyName("type")] public required string Type { get; init; }
        [JsonPropertyName("expression")] public required string Expression { get; init; }
    }

    private sealed class ScopeInfoOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("enclosingFunction")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ScopeFunctionInfoDto? EnclosingFunction { get; init; }
        [JsonPropertyName("enclosingClass")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EnclosingClass { get; init; }
        [JsonPropertyName("enclosingModule")] public ModuleInfoDto? EnclosingModule { get; init; }
        [JsonPropertyName("localVariables")] public List<VariableInfoDto> LocalVariables { get; init; } = new();
        [JsonPropertyName("parameters")] public List<ScopeParameterInfoDto> Parameters { get; init; } = new();
        [JsonPropertyName("availableFunctions")] public List<ScopeFunctionInfoDto> AvailableFunctions { get; init; } = new();
        [JsonPropertyName("activeContracts")] public List<string> ActiveContracts { get; init; } = new();
        [JsonPropertyName("validInsertionPoints")] public List<string> ValidInsertionPoints { get; init; } = new();
    }

    private sealed class ModuleInfoDto
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
    }

    private sealed class ScopeFunctionInfoDto
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("returnType")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReturnType { get; init; }
        [JsonPropertyName("parameterCount")] public int ParameterCount { get; init; }
    }

    private sealed class VariableInfoDto
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("type")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Type { get; init; }
        [JsonPropertyName("isMutable")] public bool IsMutable { get; init; }
        [JsonPropertyName("definedAtLine")] public int DefinedAtLine { get; init; }
    }

    private sealed class ScopeParameterInfoDto
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("type")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Type { get; init; }
    }
}
