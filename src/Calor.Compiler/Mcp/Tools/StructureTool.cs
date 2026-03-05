using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Unified MCP tool for code structure analysis — document outline, call graph, or change impact analysis.
/// </summary>
public sealed class StructureTool : McpToolBase
{
    public override string Name => "calor_structure";

    public override string Description =>
        "Analyze code structure — document outline, call graph, or change impact analysis";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["outline", "callgraph", "impact"],
                    "description": "Structure analysis. outline=hierarchical symbol tree, callgraph=callers/callees/cycles, impact=blast radius of changes",
                    "default": "outline"
                },
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
                    "description": "Calor unique ID of the symbol (e.g., 'f001') — used by callgraph and impact"
                },
                "line": {
                    "type": "integer",
                    "description": "Line number (1-based) of the symbol — used by callgraph and impact"
                },
                "column": {
                    "type": "integer",
                    "description": "Column number (1-based) of the symbol — used by callgraph and impact"
                },
                "depth": {
                    "type": "integer",
                    "description": "Maximum traversal depth (1-5, default: 1 for callgraph, 3 for impact)"
                },
                "changeType": {
                    "type": "string",
                    "enum": ["signature", "type", "rename", "delete", "contract"],
                    "description": "Type of change being considered (default: signature) — used by impact"
                },
                "includeDetails": {
                    "type": "boolean",
                    "description": "Include detailed information like parameter types and contracts (default: true) — used by outline"
                },
                "direction": {
                    "type": "string",
                    "enum": ["callers", "callees", "both"],
                    "description": "Which direction to traverse (default: both) — used by callgraph"
                },
                "includeEffects": {
                    "type": "boolean",
                    "description": "Include effect annotations on each node (default: true) — used by callgraph"
                }
            },

            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var action = GetString(arguments, "action") ?? "outline";

        return action switch
        {
            "outline" => await HandleOutline(arguments),
            "callgraph" => await HandleCallgraph(arguments),
            "impact" => await HandleImpact(arguments),
            _ => McpToolResult.Error($"Unknown action '{action}'. Use 'outline', 'callgraph', or 'impact'.")
        };
    }

    // ───────────────────────────── Outline ─────────────────────────────

    private async Task<McpToolResult> HandleOutline(JsonElement? arguments)
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

    // ───────────────────────────── Call Graph ─────────────────────────────

    private async Task<McpToolResult> HandleCallgraph(JsonElement? arguments)
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
                resolvedId = ResolveSymbolIdForCallGraph(ast, identifier, callGraph);
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

    private static string? ResolveSymbolIdForCallGraph(ModuleNode ast, string name, CallGraphAnalysis cg)
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

    // ───────────────────────────── Impact Analysis ─────────────────────────────

    private async Task<McpToolResult> HandleImpact(JsonElement? arguments)
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

    // ───────────────────────────── Shared Helpers ─────────────────────────────

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

    // ───────────────────────────── Output Models ─────────────────────────────

    // Outline models

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

    // Call graph models

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

    // Impact analysis models

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
