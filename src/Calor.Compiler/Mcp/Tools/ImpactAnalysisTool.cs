using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for computing the impact of changing a symbol.
/// Given a symbol (by ID or position), returns what would be affected.
/// </summary>
public sealed class ImpactAnalysisTool : McpToolBase
{
    public override string Name => "calor_impact_analysis";

    public override string Description =>
        "Compute what would be affected by changing a symbol. " +
        "Returns direct and transitive impacts through call chains, " +
        "contract dependencies, and effect chain implications. " +
        "Use before making changes to understand blast radius.";

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
                    "description": "Calor unique ID of the symbol (e.g., 'f001')"
                },
                "line": {
                    "type": "integer",
                    "description": "Line number (1-based) of the symbol"
                },
                "column": {
                    "type": "integer",
                    "description": "Column number (1-based) of the symbol"
                },
                "changeType": {
                    "type": "string",
                    "enum": ["signature", "type", "rename", "delete", "contract"],
                    "description": "Type of change being considered (default: signature)"
                },
                "depth": {
                    "type": "integer",
                    "description": "Maximum depth for transitive impact analysis (1-5, default: 3)"
                }
            }
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var symbolId = GetString(arguments, "symbolId");
        var line = GetInt(arguments, "line", 0);
        var column = GetInt(arguments, "column", 0);
        var changeType = GetString(arguments, "changeType") ?? "signature";
        var depth = Math.Clamp(GetInt(arguments, "depth", 3), 1, 5);

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

        // Resolve the target symbol
        string? targetId = symbolId;
        string? targetName = null;

        if (string.IsNullOrEmpty(targetId) && line > 0 && column > 0)
        {
            var identifier = ExtractIdentifierAtPosition(parseResult.Source!, line, column);
            if (string.IsNullOrEmpty(identifier))
                return McpToolResult.Json(new { success = false, message = $"No symbol found at line {line}, column {column}" });

            targetName = identifier;
            targetId = ResolveSymbolId(ast, identifier);
        }
        else if (!string.IsNullOrEmpty(targetId))
        {
            targetName = ResolveName(ast, targetId);
        }

        if (string.IsNullOrEmpty(targetId))
        {
            // Fall back to name-based analysis
            if (string.IsNullOrEmpty(targetName))
                return McpToolResult.Json(new { success = false, message = "Could not resolve symbol" });
        }

        // Build call graph
        var callGraph = CallGraphAnalysis.Build(ast);

        // Compute impacts
        var directImpacts = new List<ImpactInfo>();
        var transitiveImpacts = new List<ImpactInfo>();
        var contractImpacts = new List<string>();
        var effectImpacts = new List<string>();

        // Find direct callers (functions that call this one)
        var resolvedId = targetId != null && callGraph.Functions.ContainsKey(targetId)
            ? targetId
            : (targetName != null ? callGraph.ResolveToInternalId(targetName) : null);

        if (resolvedId != null)
        {
            // Direct impacts: callers of this function
            var callers = callGraph.GetCallers(resolvedId);
            foreach (var callerId in callers)
            {
                if (callGraph.Functions.TryGetValue(callerId, out var callerFunc))
                {
                    directImpacts.Add(new ImpactInfo
                    {
                        SymbolId = callerId,
                        SymbolName = callerFunc.Name,
                        Relationship = "calls_target",
                        Line = callerFunc.Span.Line
                    });
                }
            }

            // Also direct: functions this one calls (for delete/signature changes)
            if (changeType is "delete" or "signature")
            {
                var callees = callGraph.GetCallees(resolvedId);
                foreach (var (calleeId, calleeName, span) in callees)
                {
                    if (callGraph.Functions.ContainsKey(calleeId))
                    {
                        directImpacts.Add(new ImpactInfo
                        {
                            SymbolId = calleeId,
                            SymbolName = calleeName,
                            Relationship = "called_by_target",
                            Line = span.Line
                        });
                    }
                }
            }

            // Transitive impacts via BFS
            if (depth > 1)
            {
                var visited = new HashSet<string> { resolvedId };
                visited.UnionWith(directImpacts.Select(i => i.SymbolId!).Where(id => id != null));

                var frontier = new HashSet<string>(directImpacts
                    .Where(i => i.Relationship == "calls_target" && i.SymbolId != null)
                    .Select(i => i.SymbolId!));

                for (int d = 2; d <= depth && frontier.Count > 0; d++)
                {
                    var nextFrontier = new HashSet<string>();
                    foreach (var fid in frontier)
                    {
                        var transCallers = callGraph.GetCallers(fid);
                        foreach (var tc in transCallers)
                        {
                            if (visited.Add(tc) && callGraph.Functions.TryGetValue(tc, out var tcFunc))
                            {
                                transitiveImpacts.Add(new ImpactInfo
                                {
                                    SymbolId = tc,
                                    SymbolName = tcFunc.Name,
                                    Relationship = $"transitive_caller_depth_{d}",
                                    Line = tcFunc.Span.Line
                                });
                                nextFrontier.Add(tc);
                            }
                        }
                    }
                    frontier = nextFrontier;
                }
            }

            // Contract impacts: find functions with contracts that reference this symbol
            if (callGraph.Functions.TryGetValue(resolvedId, out var targetFunc))
            {
                // Check if target has contracts
                if (targetFunc.HasContracts)
                {
                    contractImpacts.Add($"Function '{targetFunc.Name}' has {targetFunc.Preconditions.Count} precondition(s) and {targetFunc.Postconditions.Count} postcondition(s) that may need updating");
                }

                // Check callers with contracts
                foreach (var impact in directImpacts.Where(i => i.Relationship == "calls_target"))
                {
                    if (impact.SymbolId != null && callGraph.Functions.TryGetValue(impact.SymbolId, out var callerFunc) && callerFunc.HasContracts)
                    {
                        contractImpacts.Add($"Caller '{callerFunc.Name}' has contracts that may depend on '{targetFunc.Name}'");
                    }
                }

                // Effect impacts
                if (targetFunc.Effects != null && targetFunc.Effects.Effects.Count > 0)
                {
                    effectImpacts.Add($"Function '{targetFunc.Name}' declares effects: {string.Join(", ", targetFunc.Effects.Effects.Select(e => $"{e.Key}:{e.Value}"))}");

                    foreach (var impact in directImpacts.Where(i => i.Relationship == "calls_target"))
                    {
                        if (impact.SymbolId != null && callGraph.Functions.TryGetValue(impact.SymbolId, out var callerFunc) && callerFunc.Effects != null)
                        {
                            effectImpacts.Add($"Caller '{callerFunc.Name}' has effect declarations that may need updating if '{targetFunc.Name}' changes");
                        }
                    }
                }
            }
        }

        var totalAffected = directImpacts.Count + transitiveImpacts.Count;
        var safeToEdit = totalAffected == 0 || (changeType == "contract" && directImpacts.Count == 0);

        return McpToolResult.Json(new ImpactAnalysisOutput
        {
            Success = true,
            TargetSymbol = targetName ?? targetId,
            TargetId = resolvedId,
            ChangeType = changeType,
            Depth = depth,
            DirectImpacts = directImpacts,
            TransitiveImpacts = transitiveImpacts,
            ContractImpacts = contractImpacts,
            EffectImpacts = effectImpacts,
            TotalAffectedSymbols = totalAffected,
            SafeToEdit = safeToEdit,
            RequiredCoChanges = directImpacts.Count
        });
    }

    private static string? ResolveSymbolId(ModuleNode ast, string name)
    {
        foreach (var func in ast.Functions)
        {
            if (func.Name == name || func.Id == name)
                return func.Id;
        }
        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                if (method.Name == name || method.Id == name)
                    return $"{cls.Name}.{method.Id}";
            }
        }
        return null;
    }

    private static string? ResolveName(ModuleNode ast, string id)
    {
        foreach (var func in ast.Functions)
        {
            if (func.Id == id) return func.Name;
        }
        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                if (method.Id == id || $"{cls.Name}.{method.Id}" == id) return method.Name;
            }
        }
        return id;
    }

    private static string? ExtractIdentifierAtPosition(string source, int line, int column)
    {
        var offset = CalorSourceHelper.GetOffset(source, line, column);
        if (offset < 0 || offset >= source.Length) return null;

        var start = offset;
        while (start > 0 && IsIdentifierChar(source[start - 1])) start--;

        var end = offset;
        while (end < source.Length && IsIdentifierChar(source[end])) end++;

        return start == end ? null : source.Substring(start, end - start);
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private sealed class ImpactAnalysisOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("targetSymbol")] public string? TargetSymbol { get; init; }
        [JsonPropertyName("targetId")] public string? TargetId { get; init; }
        [JsonPropertyName("changeType")] public string? ChangeType { get; init; }
        [JsonPropertyName("depth")] public int Depth { get; init; }
        [JsonPropertyName("directImpacts")] public List<ImpactInfo> DirectImpacts { get; init; } = new();
        [JsonPropertyName("transitiveImpacts")] public List<ImpactInfo> TransitiveImpacts { get; init; } = new();
        [JsonPropertyName("contractImpacts")] public List<string> ContractImpacts { get; init; } = new();
        [JsonPropertyName("effectImpacts")] public List<string> EffectImpacts { get; init; } = new();
        [JsonPropertyName("totalAffectedSymbols")] public int TotalAffectedSymbols { get; init; }
        [JsonPropertyName("safeToEdit")] public bool SafeToEdit { get; init; }
        [JsonPropertyName("requiredCoChanges")] public int RequiredCoChanges { get; init; }
    }

    private sealed class ImpactInfo
    {
        [JsonPropertyName("symbolId")] public string? SymbolId { get; init; }
        [JsonPropertyName("symbolName")] public string? SymbolName { get; init; }
        [JsonPropertyName("relationship")] public string? Relationship { get; init; }
        [JsonPropertyName("line")] public int Line { get; init; }
    }
}
