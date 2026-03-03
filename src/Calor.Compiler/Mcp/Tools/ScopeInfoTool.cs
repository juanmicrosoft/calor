using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for querying what's in scope at a given position.
/// Returns available variables, parameters, functions, and active contracts.
/// </summary>
public sealed class ScopeInfoTool : McpToolBase
{
    public override string Name => "calor_scope_info";

    public override string Description =>
        "Get everything in scope at a given position. " +
        "Returns enclosing function/class/module, local variables, parameters, " +
        "available functions, active contracts, and valid insertion points. " +
        "Useful for understanding what's available when writing new code.";

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
                    "description": "Line number (1-based)"
                },
                "column": {
                    "type": "integer",
                    "description": "Column number (1-based)"
                },
                "includeTypes": {
                    "type": "boolean",
                    "description": "Include type information for variables (default: true)"
                },
                "includeContracts": {
                    "type": "boolean",
                    "description": "Include active contracts in scope (default: true)"
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
        var includeTypes = GetBool(arguments, "includeTypes", true);
        var includeContracts = GetBool(arguments, "includeContracts", true);

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

        // Find enclosing function
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

        // Collect local variables from enclosing function
        var localVariables = new List<VariableInfo>();
        var parameters = new List<ParameterInfo>();
        var activeContracts = new List<string>();

        if (enclosingFunction != null)
        {
            // Parameters
            foreach (var param in enclosingFunction.Parameters)
            {
                parameters.Add(new ParameterInfo
                {
                    Name = param.Name,
                    Type = includeTypes ? param.TypeName : null
                });
            }

            // Local bindings defined before the current position
            foreach (var stmt in enclosingFunction.Body)
            {
                if (stmt.Span.Start > offset) break; // Only include bindings before cursor

                if (stmt is BindStatementNode bind)
                {
                    localVariables.Add(new VariableInfo
                    {
                        Name = bind.Name,
                        Type = includeTypes ? bind.TypeName : null,
                        IsMutable = bind.IsMutable,
                        DefinedAtLine = bind.Span.Line
                    });
                }
            }

            // Active contracts
            if (includeContracts)
            {
                foreach (var pre in enclosingFunction.Preconditions)
                {
                    activeContracts.Add($"requires: {pre.Message ?? pre.Condition.ToString()}");
                }
                foreach (var post in enclosingFunction.Postconditions)
                {
                    activeContracts.Add($"ensures: {post.Message ?? post.Condition.ToString()}");
                }
            }
        }

        // Available functions (sibling functions in module)
        var availableFunctions = new List<FunctionInfo>();
        foreach (var func in ast.Functions)
        {
            availableFunctions.Add(new FunctionInfo
            {
                Id = func.Id,
                Name = func.Name,
                ReturnType = func.Output?.TypeName,
                ParameterCount = func.Parameters.Count
            });
        }

        // Functions from classes
        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                availableFunctions.Add(new FunctionInfo
                {
                    Id = $"{cls.Name}.{method.Id}",
                    Name = $"{cls.Name}.{method.Name}",
                    ReturnType = method.Output?.TypeName,
                    ParameterCount = method.Parameters.Count
                });
            }
        }

        // Valid insertion points
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
            EnclosingFunction = enclosingFunction != null ? new FunctionInfo
            {
                Id = enclosingFunction.Id,
                Name = enclosingFunction.Name,
                ReturnType = enclosingFunction.Output?.TypeName,
                ParameterCount = enclosingFunction.Parameters.Count
            } : null,
            EnclosingClass = enclosingClass?.Name,
            EnclosingModule = new ModuleInfo { Id = ast.Id, Name = ast.Name },
            LocalVariables = localVariables,
            Parameters = parameters,
            AvailableFunctions = availableFunctions,
            ActiveContracts = activeContracts,
            ValidInsertionPoints = validInsertions
        });
    }

    private sealed class ScopeInfoOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("enclosingFunction")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FunctionInfo? EnclosingFunction { get; init; }
        [JsonPropertyName("enclosingClass")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EnclosingClass { get; init; }
        [JsonPropertyName("enclosingModule")] public ModuleInfo? EnclosingModule { get; init; }
        [JsonPropertyName("localVariables")] public List<VariableInfo> LocalVariables { get; init; } = new();
        [JsonPropertyName("parameters")] public List<ParameterInfo> Parameters { get; init; } = new();
        [JsonPropertyName("availableFunctions")] public List<FunctionInfo> AvailableFunctions { get; init; } = new();
        [JsonPropertyName("activeContracts")] public List<string> ActiveContracts { get; init; } = new();
        [JsonPropertyName("validInsertionPoints")] public List<string> ValidInsertionPoints { get; init; } = new();
    }

    private sealed class ModuleInfo
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
    }

    private sealed class FunctionInfo
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("returnType")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReturnType { get; init; }
        [JsonPropertyName("parameterCount")] public int ParameterCount { get; init; }
    }

    private sealed class VariableInfo
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("type")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; init; }
        [JsonPropertyName("isMutable")] public bool IsMutable { get; init; }
        [JsonPropertyName("definedAtLine")] public int DefinedAtLine { get; init; }
    }

    private sealed class ParameterInfo
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("type")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; init; }
    }
}
