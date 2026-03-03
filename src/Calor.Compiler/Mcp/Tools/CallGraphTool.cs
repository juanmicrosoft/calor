using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for querying the call graph of a function.
/// Returns callers, callees, and effect annotations.
/// </summary>
public sealed class CallGraphTool : McpToolBase
{
    public override string Name => "calor_call_graph";

    public override string Description =>
        "Get callers and/or callees of a function with effect annotations. " +
        "Useful for understanding function dependencies and effect propagation. " +
        "Detects recursive cycles via strongly connected components.";

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
                "symbolId": {
                    "type": "string",
                    "description": "Calor unique ID of the function (e.g., 'f001')"
                },
                "line": {
                    "type": "integer",
                    "description": "Line number (1-based) of the function"
                },
                "column": {
                    "type": "integer",
                    "description": "Column number (1-based) of the function"
                },
                "direction": {
                    "type": "string",
                    "enum": ["callers", "callees", "both"],
                    "description": "Which direction to traverse (default: both)"
                },
                "depth": {
                    "type": "integer",
                    "description": "Maximum depth to traverse (1-5, default: 1)"
                },
                "includeEffects": {
                    "type": "boolean",
                    "description": "Include effect annotations on each node (default: true)"
                }
            },

            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var symbolId = GetString(arguments, "symbolId");
        var line = GetInt(arguments, "line", 0);
        var column = GetInt(arguments, "column", 0);
        var direction = GetString(arguments, "direction") ?? "both";
        var depth = Math.Clamp(GetInt(arguments, "depth", 1), 1, 5);
        var includeEffects = GetBool(arguments, "includeEffects", true);

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(filePath))
            return McpToolResult.Error("Either 'source' or 'filePath' is required");

        if (string.IsNullOrEmpty(symbolId) && (line <= 0 || column <= 0))
            return McpToolResult.Error("Either 'symbolId' or both 'line' and 'column' are required");

        ParseResult parseResult;
        if (!string.IsNullOrEmpty(filePath))
            parseResult = await CalorSourceHelper.ParseFileAsync(filePath);
        else
            parseResult = CalorSourceHelper.Parse(source!, filePath);

        if (!parseResult.IsSuccess)
            return McpToolResult.Json(new { success = false, errors = parseResult.Errors.ToList() }, isError: true);

        var ast = parseResult.Ast!;
        var callGraph = CallGraphAnalysis.Build(ast);

        // Resolve target
        string? resolvedId = null;
        if (!string.IsNullOrEmpty(symbolId))
        {
            resolvedId = callGraph.Functions.ContainsKey(symbolId) ? symbolId : callGraph.ResolveToInternalId(symbolId);
        }
        else if (line > 0 && column > 0)
        {
            var identifier = ExtractIdentifierAtPosition(parseResult.Source!, line, column);
            if (!string.IsNullOrEmpty(identifier))
                resolvedId = ResolveSymbolId(ast, identifier, callGraph);
        }

        if (resolvedId == null || !callGraph.Functions.TryGetValue(resolvedId, out var targetFunc))
            return McpToolResult.Json(new { success = false, message = "Function not found in call graph" });

        var callers = new List<CallGraphEntry>();
        var callees = new List<CallGraphEntry>();
        var cycles = new List<List<string>>();

        // Collect callers
        if (direction is "callers" or "both")
        {
            CollectCallers(resolvedId, callGraph, callers, includeEffects, depth);
        }

        // Collect callees
        if (direction is "callees" or "both")
        {
            CollectCallees(resolvedId, callGraph, callees, includeEffects, depth);
        }

        // Detect cycles from SCCs
        foreach (var scc in callGraph.StronglyConnectedComponents)
        {
            if (scc.Count > 1 && scc.Contains(resolvedId))
            {
                cycles.Add(scc.Select(id =>
                    callGraph.Functions.TryGetValue(id, out var f) ? f.Name : id).ToList());
            }
        }

        string? effectAnnotation = null;
        if (includeEffects && targetFunc.Effects?.Effects.Count > 0)
        {
            effectAnnotation = string.Join(", ", targetFunc.Effects.Effects.Select(e => $"{e.Key}:{e.Value}"));
        }

        return McpToolResult.Json(new CallGraphOutput
        {
            Success = true,
            Root = new CallGraphEntry
            {
                SymbolId = resolvedId,
                SymbolName = targetFunc.Name,
                Line = targetFunc.Span.Line,
                Effects = effectAnnotation
            },
            Callers = callers,
            Callees = callees,
            RecursiveCycles = cycles,
            TotalCallers = callers.Count,
            TotalCallees = callees.Count
        });
    }

    private static void CollectCallers(string functionId, CallGraphAnalysis cg, List<CallGraphEntry> result,
        bool includeEffects, int depth, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string> { functionId };
        var callers = cg.GetCallers(functionId);

        foreach (var callerId in callers)
        {
            if (!visited.Add(callerId)) continue;
            if (!cg.Functions.TryGetValue(callerId, out var callerFunc)) continue;

            string? effects = null;
            if (includeEffects && callerFunc.Effects?.Effects.Count > 0)
                effects = string.Join(", ", callerFunc.Effects.Effects.Select(e => $"{e.Key}:{e.Value}"));

            result.Add(new CallGraphEntry
            {
                SymbolId = callerId,
                SymbolName = callerFunc.Name,
                Line = callerFunc.Span.Line,
                Effects = effects
            });

            if (depth > 1)
                CollectCallers(callerId, cg, result, includeEffects, depth - 1, visited);
        }
    }

    private static void CollectCallees(string functionId, CallGraphAnalysis cg, List<CallGraphEntry> result,
        bool includeEffects, int depth, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string> { functionId };
        var callees = cg.GetCallees(functionId);

        foreach (var (calleeId, calleeName, span) in callees)
        {
            if (!visited.Add(calleeId)) continue;

            string? effects = null;
            if (includeEffects && cg.Functions.TryGetValue(calleeId, out var calleeFunc))
            {
                if (calleeFunc.Effects?.Effects.Count > 0)
                    effects = string.Join(", ", calleeFunc.Effects.Effects.Select(e => $"{e.Key}:{e.Value}"));
            }

            result.Add(new CallGraphEntry
            {
                SymbolId = calleeId,
                SymbolName = calleeName,
                Line = span.Line,
                Effects = effects
            });

            if (depth > 1 && cg.Functions.ContainsKey(calleeId))
                CollectCallees(calleeId, cg, result, includeEffects, depth - 1, visited);
        }
    }

    private static string? ResolveSymbolId(ModuleNode ast, string name, CallGraphAnalysis cg)
    {
        foreach (var func in ast.Functions)
        {
            if (func.Name == name || func.Id == name) return func.Id;
        }
        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var qid = $"{cls.Name}.{method.Id}";
                if (method.Name == name || method.Id == name || qid == name) return qid;
            }
        }
        return cg.ResolveToInternalId(name);
    }

    private static string? ExtractIdentifierAtPosition(string source, int line, int column)
    {
        var offset = CalorSourceHelper.GetOffset(source, line, column);
        if (offset < 0 || offset >= source.Length) return null;

        var start = offset;
        while (start > 0 && IsIdChar(source[start - 1])) start--;
        var end = offset;
        while (end < source.Length && IsIdChar(source[end])) end++;

        return start == end ? null : source.Substring(start, end - start);
    }

    private static bool IsIdChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private sealed class CallGraphOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("root")] public CallGraphEntry? Root { get; init; }
        [JsonPropertyName("callers")] public List<CallGraphEntry> Callers { get; init; } = new();
        [JsonPropertyName("callees")] public List<CallGraphEntry> Callees { get; init; } = new();
        [JsonPropertyName("recursiveCycles")] public List<List<string>> RecursiveCycles { get; init; } = new();
        [JsonPropertyName("totalCallers")] public int TotalCallers { get; init; }
        [JsonPropertyName("totalCallees")] public int TotalCallees { get; init; }
    }

    private sealed class CallGraphEntry
    {
        [JsonPropertyName("symbolId")] public string? SymbolId { get; init; }
        [JsonPropertyName("symbolName")] public string? SymbolName { get; init; }
        [JsonPropertyName("line")] public int Line { get; init; }
        [JsonPropertyName("effects")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Effects { get; init; }
    }
}
