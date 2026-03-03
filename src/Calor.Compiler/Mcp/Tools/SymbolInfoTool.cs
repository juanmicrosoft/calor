using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for getting type information and contracts for a symbol at a given position.
/// Similar to LSP hover but returns structured data for AI agents.
/// </summary>
public sealed class SymbolInfoTool : McpToolBase
{
    public override string Name => "calor_symbol_info";

    public override string Description =>
        "Get type information, contracts, and documentation for a symbol at a given position. " +
        "Returns structured information including type, kind, contracts (preconditions/postconditions), " +
        "and a formatted signature.";

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

        // Search for symbol in context

        // 1. Check functions
        foreach (var func in ast.Functions)
        {
            if (func.Name == identifier)
            {
                return BuildFunctionInfo(func);
            }

            if (IsPositionInNode(line, func.Span))
            {
                // Check parameters
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

                // Check local bindings
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
            {
                return BuildClassInfo(cls);
            }

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
                    {
                        return BuildMethodInfo(method);
                    }

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

            // Check enum members
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
        {
            contracts.Add(new ContractInfo { Type = "requires", Expression = FormatExpression(pre.Condition) });
        }
        foreach (var post in func.Postconditions)
        {
            contracts.Add(new ContractInfo { Type = "ensures", Expression = FormatExpression(post.Condition) });
        }

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
            Parameters = func.Parameters.Select(p => new ParameterInfo { Name = p.Name, Type = p.TypeName }).ToList(),
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
        {
            contracts.Add(new ContractInfo { Type = "requires", Expression = FormatExpression(pre.Condition) });
        }
        foreach (var post in method.Postconditions)
        {
            contracts.Add(new ContractInfo { Type = "ensures", Expression = FormatExpression(post.Condition) });
        }

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
            Parameters = method.Parameters.Select(p => new ParameterInfo { Name = p.Name, Type = p.TypeName }).ToList(),
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
        // Simple string representation - could be enhanced
        return expr.ToString() ?? "...";
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

    private static bool IsPositionInNode(int line, Parsing.TextSpan span) => line >= span.Line;

    private sealed class SymbolInfoOutput
    {
        [JsonPropertyName("found")]
        public bool Found { get; init; }

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; init; }

        [JsonPropertyName("kind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Kind { get; init; }

        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; init; }

        [JsonPropertyName("visibility")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Visibility { get; init; }

        [JsonPropertyName("isAsync")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsAsync { get; init; }

        [JsonPropertyName("modifiers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Modifiers { get; init; }

        [JsonPropertyName("signature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Signature { get; init; }

        [JsonPropertyName("parameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ParameterInfo>? Parameters { get; init; }

        [JsonPropertyName("contracts")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ContractInfo>? Contracts { get; init; }

        [JsonPropertyName("effects")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Effects { get; init; }

        [JsonPropertyName("baseClass")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BaseClass { get; init; }

        [JsonPropertyName("interfaces")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Interfaces { get; init; }

        [JsonPropertyName("memberCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int MemberCount { get; init; }

        [JsonPropertyName("enumMembers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? EnumMembers { get; init; }

        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }
    }

    private sealed class ParameterInfo
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("type")]
        public required string Type { get; init; }
    }

    private sealed class ContractInfo
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("expression")]
        public required string Expression { get; init; }
    }
}
