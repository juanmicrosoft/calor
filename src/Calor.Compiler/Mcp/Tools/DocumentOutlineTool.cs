using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for getting a structured outline of all symbols in a Calor file.
/// Returns a hierarchical list of functions, classes, methods, etc.
/// </summary>
public sealed class DocumentOutlineTool : McpToolBase
{
    public override string Name => "calor_document_outline";

    public override string Description =>
        "Get a structured outline of all symbols in a Calor source file. " +
        "Returns a hierarchical tree of modules, classes, functions, methods, fields, etc. " +
        "Useful for understanding file structure before making changes.";

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
                "includeDetails": {
                    "type": "boolean",
                    "description": "Include detailed information like parameter types and contracts (default: true)"
                }
            }
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var includeDetails = GetBool(arguments, "includeDetails", true);

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
        {
            return McpToolResult.Error("Either 'source' or 'filePath' is required");
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
            return McpToolResult.Json(new DocumentOutlineOutput
            {
                Success = false,
                Errors = parseResult.Errors.ToList()
            }, isError: true);
        }

        var outline = BuildOutline(parseResult.Ast!, includeDetails);

        return McpToolResult.Json(new DocumentOutlineOutput
        {
            Success = true,
            ModuleName = parseResult.Ast!.Name,
            ModuleId = parseResult.Ast.Id,
            FilePath = filePath,
            Symbols = outline,
            Summary = BuildSummary(parseResult.Ast)
        });
    }

    private static List<OutlineSymbol> BuildOutline(ModuleNode ast, bool includeDetails)
    {
        var symbols = new List<OutlineSymbol>();

        // Functions
        foreach (var func in ast.Functions)
        {
            var funcSymbol = new OutlineSymbol
            {
                Name = func.Name,
                Kind = "function",
                Line = func.Span.Line,
                Detail = includeDetails ? BuildFunctionDetail(func) : null,
                Children = includeDetails ? BuildFunctionChildren(func) : null
            };
            symbols.Add(funcSymbol);
        }

        // Classes
        foreach (var cls in ast.Classes)
        {
            var classSymbol = new OutlineSymbol
            {
                Name = cls.Name,
                Kind = "class",
                Line = cls.Span.Line,
                Detail = includeDetails ? BuildClassDetail(cls) : null,
                Children = BuildClassChildren(cls, includeDetails)
            };
            symbols.Add(classSymbol);
        }

        // Interfaces
        foreach (var iface in ast.Interfaces)
        {
            var ifaceSymbol = new OutlineSymbol
            {
                Name = iface.Name,
                Kind = "interface",
                Line = iface.Span.Line,
                Children = includeDetails ? BuildInterfaceChildren(iface) : null
            };
            symbols.Add(ifaceSymbol);
        }

        // Enums
        foreach (var enumDef in ast.Enums)
        {
            var enumSymbol = new OutlineSymbol
            {
                Name = enumDef.Name,
                Kind = "enum",
                Line = enumDef.Span.Line,
                Detail = enumDef.UnderlyingType,
                Children = includeDetails ? BuildEnumChildren(enumDef) : null
            };
            symbols.Add(enumSymbol);
        }

        // Delegates
        foreach (var del in ast.Delegates)
        {
            var delSymbol = new OutlineSymbol
            {
                Name = del.Name,
                Kind = "delegate",
                Line = del.Span.Line,
                Detail = includeDetails ? BuildDelegateDetail(del) : null
            };
            symbols.Add(delSymbol);
        }

        return symbols;
    }

    private static string BuildFunctionDetail(FunctionNode func)
    {
        var parameters = string.Join(", ", func.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = func.Output?.TypeName ?? "void";
        var asyncPrefix = func.IsAsync ? "async " : "";
        return $"{asyncPrefix}({parameters}) -> {returnType}";
    }

    private static List<OutlineSymbol>? BuildFunctionChildren(FunctionNode func)
    {
        var children = new List<OutlineSymbol>();

        foreach (var param in func.Parameters)
        {
            children.Add(new OutlineSymbol
            {
                Name = param.Name,
                Kind = "parameter",
                Line = param.Span.Line,
                Detail = param.TypeName
            });
        }

        // Add contracts as children for visibility
        foreach (var pre in func.Preconditions)
        {
            children.Add(new OutlineSymbol
            {
                Name = "requires",
                Kind = "contract",
                Line = pre.Span.Line,
                Detail = "precondition"
            });
        }

        foreach (var post in func.Postconditions)
        {
            children.Add(new OutlineSymbol
            {
                Name = "ensures",
                Kind = "contract",
                Line = post.Span.Line,
                Detail = "postcondition"
            });
        }

        return children.Count > 0 ? children : null;
    }

    private static string BuildClassDetail(ClassDefinitionNode cls)
    {
        var parts = new List<string>();
        if (cls.IsAbstract) parts.Add("abstract");
        if (cls.IsSealed) parts.Add("sealed");
        if (cls.IsStatic) parts.Add("static");
        if (cls.BaseClass != null) parts.Add($": {cls.BaseClass}");
        return string.Join(" ", parts);
    }

    private static List<OutlineSymbol>? BuildClassChildren(ClassDefinitionNode cls, bool includeDetails)
    {
        var children = new List<OutlineSymbol>();

        // Fields
        foreach (var field in cls.Fields)
        {
            children.Add(new OutlineSymbol
            {
                Name = field.Name,
                Kind = "field",
                Line = field.Span.Line,
                Detail = includeDetails ? $"{field.Visibility.ToString().ToLower()} {field.TypeName}" : null
            });
        }

        // Properties
        foreach (var prop in cls.Properties)
        {
            children.Add(new OutlineSymbol
            {
                Name = prop.Name,
                Kind = "property",
                Line = prop.Span.Line,
                Detail = includeDetails ? prop.TypeName : null
            });
        }

        // Constructors
        foreach (var ctor in cls.Constructors)
        {
            children.Add(new OutlineSymbol
            {
                Name = cls.Name,
                Kind = "constructor",
                Line = ctor.Span.Line,
                Detail = includeDetails ? BuildConstructorDetail(ctor) : null
            });
        }

        // Methods
        foreach (var method in cls.Methods)
        {
            children.Add(new OutlineSymbol
            {
                Name = method.Name,
                Kind = "method",
                Line = method.Span.Line,
                Detail = includeDetails ? BuildMethodDetail(method) : null
            });
        }

        return children.Count > 0 ? children : null;
    }

    private static string BuildConstructorDetail(ConstructorNode ctor)
    {
        var parameters = string.Join(", ", ctor.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        return $"({parameters})";
    }

    private static string BuildMethodDetail(MethodNode method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = method.Output?.TypeName ?? "void";
        var modifiers = new List<string>();
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsVirtual) modifiers.Add("virtual");
        if (method.IsOverride) modifiers.Add("override");
        if (method.IsAbstract) modifiers.Add("abstract");
        if (method.IsAsync) modifiers.Add("async");
        var modifierStr = modifiers.Count > 0 ? string.Join(" ", modifiers) + " " : "";
        return $"{modifierStr}({parameters}) -> {returnType}";
    }

    private static List<OutlineSymbol>? BuildInterfaceChildren(InterfaceDefinitionNode iface)
    {
        var children = new List<OutlineSymbol>();

        foreach (var prop in iface.Properties)
        {
            children.Add(new OutlineSymbol
            {
                Name = prop.Name,
                Kind = "property",
                Line = prop.Span.Line,
                Detail = prop.TypeName
            });
        }

        foreach (var method in iface.Methods)
        {
            var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
            var returnType = method.Output?.TypeName ?? "void";
            children.Add(new OutlineSymbol
            {
                Name = method.Name,
                Kind = "method signature",
                Line = method.Span.Line,
                Detail = $"({parameters}) -> {returnType}"
            });
        }

        return children.Count > 0 ? children : null;
    }

    private static List<OutlineSymbol>? BuildEnumChildren(EnumDefinitionNode enumDef)
    {
        var children = new List<OutlineSymbol>();

        foreach (var member in enumDef.Members)
        {
            children.Add(new OutlineSymbol
            {
                Name = member.Name,
                Kind = "enum member",
                Line = member.Span.Line,
                Detail = member.Value
            });
        }

        return children.Count > 0 ? children : null;
    }

    private static string BuildDelegateDetail(DelegateDefinitionNode del)
    {
        var parameters = string.Join(", ", del.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = del.Output?.TypeName ?? "void";
        return $"({parameters}) -> {returnType}";
    }

    private static OutlineSummary BuildSummary(ModuleNode ast)
    {
        return new OutlineSummary
        {
            FunctionCount = ast.Functions.Count,
            ClassCount = ast.Classes.Count,
            InterfaceCount = ast.Interfaces.Count,
            EnumCount = ast.Enums.Count,
            DelegateCount = ast.Delegates.Count
        };
    }

    private sealed class DocumentOutlineOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("moduleName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ModuleName { get; init; }

        [JsonPropertyName("moduleId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ModuleId { get; init; }

        [JsonPropertyName("filePath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FilePath { get; init; }

        [JsonPropertyName("symbols")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OutlineSymbol>? Symbols { get; init; }

        [JsonPropertyName("summary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OutlineSummary? Summary { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }
    }

    private sealed class OutlineSymbol
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; init; }

        [JsonPropertyName("children")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OutlineSymbol>? Children { get; init; }
    }

    private sealed class OutlineSummary
    {
        [JsonPropertyName("functionCount")]
        public int FunctionCount { get; init; }

        [JsonPropertyName("classCount")]
        public int ClassCount { get; init; }

        [JsonPropertyName("interfaceCount")]
        public int InterfaceCount { get; init; }

        [JsonPropertyName("enumCount")]
        public int EnumCount { get; init; }

        [JsonPropertyName("delegateCount")]
        public int DelegateCount { get; init; }
    }
}
