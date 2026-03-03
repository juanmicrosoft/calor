using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Calor.Compiler.Init;
using Calor.Compiler.Mcp.Tools;

namespace Calor.Compiler.Mcp;

/// <summary>
/// Handles MCP protocol messages and routes them to appropriate handlers.
/// </summary>
public sealed class McpMessageHandler
{
    private const string ProtocolVersion = "2025-03-26";
    private static readonly TimeSpan DefaultToolTimeout = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, IMcpTool> _tools;
    private readonly Dictionary<string, McpResource> _resources;
    private readonly Dictionary<string, McpPrompt> _prompts;
    private readonly bool _verbose;
    private readonly TextWriter? _log;
    private CancellationToken _serverCancellation;

    public McpMessageHandler(bool verbose = false, TextWriter? log = null)
    {
        _verbose = verbose;
        _log = log;

        // Register all tools
        _tools = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);

        // ── Core compilation & verification ─────────────────
        RegisterTool(new CompileTool());
        RegisterTool(new VerifyTool());
        RegisterTool(new AnalyzeTool());
        RegisterTool(new DiagnoseTool());

        // ── C# ↔ Calor conversion ──────────────────────────
        RegisterTool(new ConvertTool());
        RegisterTool(new AnalyzeConvertibilityTool());
        RegisterTool(new AssessTool());
        RegisterTool(new BatchConvertTool());
        RegisterTool(new CSharpMinimizeTool());

        // ── Syntax & documentation ──────────────────────────
        RegisterTool(new SyntaxLookupTool());
        RegisterTool(new ExplainErrorTool());
        RegisterTool(new FeatureSupportTool());

        // ── Code navigation ─────────────────────────────────
        RegisterTool(new GotoDefinitionTool());
        RegisterTool(new FindReferencesTool());
        RegisterTool(new SymbolInfoTool());
        RegisterTool(new DocumentOutlineTool());
        RegisterTool(new FindSymbolTool());
        RegisterTool(new ScopeInfoTool());

        // ── Edit support & analysis ─────────────────────────
        RegisterTool(new EditPreviewTool());
        RegisterTool(new ImpactAnalysisTool());
        RegisterTool(new CallGraphTool());
        RegisterTool(new LintTool());
        RegisterTool(new FormatTool());
        RegisterTool(new IdsTool());

        // ── Refinement types & obligations ──────────────────
        RegisterTool(new ObligationsTool());
        RegisterTool(new SuggestFixesTool());
        RegisterTool(new GuardDiscoveryTool());
        RegisterTool(new TypeSuggestionTool());
        RegisterTool(new DiagnoseRefinementTool());
        RegisterTool(new BoundsCheckTool());

        // ── Testing ─────────────────────────────────────────
        RegisterTool(new SelfTestTool());

        // ── Resources ───────────────────────────────────────
        _resources = new Dictionary<string, McpResource>(StringComparer.Ordinal);
        RegisterResource(new McpResource
        {
            Uri = "calor://syntax-reference",
            Name = "Calor Syntax Reference",
            Description = "Complete Calor syntax documentation with C# mappings and examples",
            MimeType = "application/json"
        });
        RegisterResource(new McpResource
        {
            Uri = "calor://error-catalog",
            Name = "Calor Error Catalog",
            Description = "All diagnostic codes with fix patterns and examples",
            MimeType = "application/json"
        });
        RegisterResource(new McpResource
        {
            Uri = "calor://type-mappings",
            Name = "C# to Calor Type Mappings",
            Description = "Mapping table between C# types and Calor equivalents",
            MimeType = "text/markdown"
        });

        // ── Prompts ─────────────────────────────────────────
        _prompts = new Dictionary<string, McpPrompt>(StringComparer.Ordinal);
        RegisterPrompt(new McpPrompt
        {
            Name = "convert-csharp-to-calor",
            Description = "Convert a C# file to Calor with validation",
            Arguments = new[]
            {
                new McpPromptArgument { Name = "csharpSource", Description = "The C# source code to convert", Required = true }
            }
        });
        RegisterPrompt(new McpPrompt
        {
            Name = "verify-and-fix",
            Description = "Verify Calor contracts and suggest fixes for failures",
            Arguments = new[]
            {
                new McpPromptArgument { Name = "calorSource", Description = "The Calor source code to verify", Required = true }
            }
        });
    }

    /// <summary>Sets the server-level cancellation token for propagation to tool calls.</summary>
    internal void SetCancellation(CancellationToken cancellationToken) => _serverCancellation = cancellationToken;

    private void RegisterTool(IMcpTool tool)
    {
        _tools[tool.Name] = tool;
    }

    private void RegisterResource(McpResource resource)
    {
        _resources[resource.Uri] = resource;
    }

    private void RegisterPrompt(McpPrompt prompt)
    {
        _prompts[prompt.Name] = prompt;
    }

    /// <summary>
    /// Handles a JSON-RPC request and returns a response (or null for notifications).
    /// </summary>
    public async Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request)
    {
        Log($"Received request: {request.Method}");

        try
        {
            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "notifications/initialized" => null,
                "tools/list" => HandleToolsList(request),
                "tools/call" => await HandleToolsCallAsync(request),
                "resources/list" => HandleResourcesList(request),
                "resources/read" => HandleResourcesRead(request),
                "prompts/list" => HandlePromptsList(request),
                "prompts/get" => HandlePromptsGet(request),
                "completion/complete" => HandleCompletion(request),
                "ping" => HandlePing(request),
                _ => JsonRpcResponse.Failure(request.Id, JsonRpcError.MethodNotFound,
                    $"Unknown method: {request.Method}")
            };
        }
        catch (Exception ex)
        {
            Log($"Error handling request: {ex.Message}");
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InternalError,
                $"Internal error: {ex.Message}");
        }
    }

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
    {
        Log("Handling initialize");

        var result = new McpInitializeResult
        {
            ProtocolVersion = ProtocolVersion,
            Capabilities = new McpCapabilities
            {
                Tools = new McpToolsCapability { ListChanged = false },
                Resources = new McpResourcesCapability { ListChanged = false },
                Prompts = new McpPromptsCapability { ListChanged = false }
            },
            ServerInfo = new McpServerInfo
            {
                Name = "calor",
                Version = EmbeddedResourceHelper.GetVersion()
            },
            Instructions = GetServerInstructions()
        };

        return JsonRpcResponse.Success(request.Id, result);
    }

    private JsonRpcResponse HandleToolsList(JsonRpcRequest request)
    {
        Log($"Handling tools/list ({_tools.Count} tools)");

        var tools = _tools.Values.Select(t => new McpTool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.GetInputSchema(),
            Annotations = t.Annotations
        }).ToList();

        var result = new McpToolsListResult { Tools = tools };
        return JsonRpcResponse.Success(request.Id, result);
    }

    private async Task<JsonRpcResponse> HandleToolsCallAsync(JsonRpcRequest request)
    {
        if (request.Params == null || request.Params.Value.ValueKind != JsonValueKind.Object)
        {
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams,
                "Missing or invalid params");
        }

        McpToolCallParams? callParams;
        try
        {
            callParams = JsonSerializer.Deserialize<McpToolCallParams>(
                request.Params.Value.GetRawText(), McpJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams,
                $"Invalid params: {ex.Message}");
        }

        if (callParams == null || string.IsNullOrEmpty(callParams.Name))
        {
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams,
                "Missing tool name");
        }

        Log($"Handling tools/call: {callParams.Name}");

        if (!_tools.TryGetValue(callParams.Name, out var tool))
        {
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams,
                $"Unknown tool: {callParams.Name}");
        }

        // Execute with timeout and cancellation
        var sw = Stopwatch.StartNew();
        McpToolResult result;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_serverCancellation);
            cts.CancelAfter(DefaultToolTimeout);
            result = await tool.ExecuteAsync(callParams.Arguments, cts.Token);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log($"Tool {callParams.Name} timed out after {sw.ElapsedMilliseconds}ms");
            return JsonRpcResponse.Success(request.Id,
                McpToolResult.Error($"Tool '{callParams.Name}' timed out after {DefaultToolTimeout.TotalSeconds}s"));
        }

        sw.Stop();
        Log($"Tool {callParams.Name} completed in {sw.ElapsedMilliseconds}ms (isError: {result.IsError})");

        return JsonRpcResponse.Success(request.Id, result);
    }

    // ── Resources ──────────────────────────────────────────────────────

    private JsonRpcResponse HandleResourcesList(JsonRpcRequest request)
    {
        Log($"Handling resources/list ({_resources.Count} resources)");
        var result = new McpResourcesListResult { Resources = _resources.Values.ToList() };
        return JsonRpcResponse.Success(request.Id, result);
    }

    private JsonRpcResponse HandleResourcesRead(JsonRpcRequest request)
    {
        var uri = request.Params?.ValueKind == JsonValueKind.Object
            && request.Params.Value.TryGetProperty("uri", out var uriProp)
            ? uriProp.GetString()
            : null;

        if (string.IsNullOrEmpty(uri))
        {
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams, "Missing 'uri' parameter");
        }

        Log($"Handling resources/read: {uri}");

        var content = LoadResourceContent(uri);
        if (content == null)
        {
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams, $"Unknown resource: {uri}");
        }

        var result = new McpResourceReadResult
        {
            Contents = new[] { content }
        };
        return JsonRpcResponse.Success(request.Id, result);
    }

    private static McpResourceContent? LoadResourceContent(string uri)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return uri switch
        {
            "calor://syntax-reference" => LoadEmbeddedResource(assembly,
                "Calor.Compiler.Resources.calor-syntax-documentation.json", uri, "application/json"),
            "calor://error-catalog" => LoadEmbeddedResource(assembly,
                "Calor.Compiler.Resources.error-suggestions.json", uri, "application/json"),
            "calor://type-mappings" => new McpResourceContent
            {
                Uri = uri,
                MimeType = "text/markdown",
                Text = GetTypeMappingsMarkdown()
            },
            _ => null
        };
    }

    private static McpResourceContent? LoadEmbeddedResource(Assembly assembly, string resourceName, string uri, string mimeType)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return new McpResourceContent { Uri = uri, MimeType = mimeType, Text = reader.ReadToEnd() };
    }

    private static string GetTypeMappingsMarkdown() => """
        # C# → Calor Type Mappings

        | C# Type | Calor Type | Notes |
        |---------|------------|-------|
        | `int` | `i32` | 32-bit signed integer |
        | `long` | `i64` | 64-bit signed integer |
        | `short` | `i16` | 16-bit signed integer |
        | `byte` | `u8` | 8-bit unsigned integer |
        | `float` | `f32` | 32-bit floating point |
        | `double` | `f64` | 64-bit floating point |
        | `decimal` | `dec` | 128-bit decimal |
        | `string` | `str` | UTF-16 string |
        | `bool` | `bool` | Boolean |
        | `char` | `char` | Unicode character |
        | `void` | `void` | No return value |
        | `object` | `obj` | Base type |
        | `int?` | `?i32` | Option type (compact) |
        | `Nullable<int>` | `OPTION[inner=INT]` | Option type (expanded) |
        | `Result<int,string>` | `i32!str` | Result type (compact) |
        | `Task<T>` | use `§AF`/`§AMT` | Async function/method |
        | `List<T>` | `§LIST{name:type}` | List collection |
        | `Dictionary<K,V>` | `§DICT{name:key:val}` | Dictionary collection |
        | `HashSet<T>` | `§HSET{name:type}` | HashSet collection |
        """;

    // ── Prompts ────────────────────────────────────────────────────────

    private JsonRpcResponse HandlePromptsList(JsonRpcRequest request)
    {
        Log($"Handling prompts/list ({_prompts.Count} prompts)");
        var result = new McpPromptsListResult { Prompts = _prompts.Values.ToList() };
        return JsonRpcResponse.Success(request.Id, result);
    }

    private JsonRpcResponse HandlePromptsGet(JsonRpcRequest request)
    {
        var name = request.Params?.ValueKind == JsonValueKind.Object
            && request.Params.Value.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString()
            : null;

        if (string.IsNullOrEmpty(name))
        {
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams, "Missing 'name' parameter");
        }

        Log($"Handling prompts/get: {name}");

        if (!_prompts.TryGetValue(name, out var prompt))
        {
            return JsonRpcResponse.Failure(request.Id, JsonRpcError.InvalidParams, $"Unknown prompt: {name}");
        }

        // Extract arguments
        Dictionary<string, string>? args = null;
        if (request.Params?.ValueKind == JsonValueKind.Object
            && request.Params.Value.TryGetProperty("arguments", out var argsProp)
            && argsProp.ValueKind == JsonValueKind.Object)
        {
            args = new Dictionary<string, string>();
            foreach (var prop in argsProp.EnumerateObject())
            {
                args[prop.Name] = prop.Value.GetString() ?? "";
            }
        }

        var result = BuildPromptResult(name, prompt, args);
        return JsonRpcResponse.Success(request.Id, result);
    }

    private static McpPromptGetResult BuildPromptResult(string name, McpPrompt prompt, Dictionary<string, string>? args)
    {
        var messages = name switch
        {
            "convert-csharp-to-calor" => new[]
            {
                new McpPromptMessage
                {
                    Role = "user",
                    Content = new McpContent
                    {
                        Type = "text",
                        Text = $"Convert this C# code to Calor. Use calor_convert first, then calor_diagnose to validate. " +
                               $"If there are §CSHARP interop blocks, check calor_syntax_lookup for native equivalents.\n\n" +
                               $"```csharp\n{args?.GetValueOrDefault("csharpSource", "// paste C# here")}\n```"
                    }
                }
            },
            "verify-and-fix" => new[]
            {
                new McpPromptMessage
                {
                    Role = "user",
                    Content = new McpContent
                    {
                        Type = "text",
                        Text = $"Verify the contracts in this Calor code using calor_verify. For any failures, " +
                               $"use calor_suggest_fixes to get repair strategies, then apply the best fix.\n\n" +
                               $"```calor\n{args?.GetValueOrDefault("calorSource", "// paste Calor here")}\n```"
                    }
                }
            },
            _ => Array.Empty<McpPromptMessage>()
        };

        return new McpPromptGetResult
        {
            Description = prompt.Description,
            Messages = messages
        };
    }

    // ── Completion ─────────────────────────────────────────────────────

    private JsonRpcResponse HandleCompletion(JsonRpcRequest request)
    {
        Log("Handling completion/complete");

        // Extract the argument name being completed
        string? argumentName = null;
        if (request.Params?.ValueKind == JsonValueKind.Object
            && request.Params.Value.TryGetProperty("argument", out var argProp)
            && argProp.ValueKind == JsonValueKind.Object
            && argProp.TryGetProperty("name", out var argNameProp))
        {
            argumentName = argNameProp.GetString();
        }

        var values = argumentName switch
        {
            "scenario" => SelfTest.SelfTestRunner.LoadScenarios().Select(s => s.Name).ToList(),
            "mode" => new List<string> { "standard", "interop" },
            "contractMode" => new List<string> { "off", "debug", "release" },
            "effectMode" => new List<string> { "strict", "default", "permissive" },
            _ => new List<string>()
        };

        var result = new McpCompletionResult
        {
            Completion = new McpCompletionData { Values = values, Total = values.Count }
        };
        return JsonRpcResponse.Success(request.Id, result);
    }

    // ── Ping ───────────────────────────────────────────────────────────

    private JsonRpcResponse HandlePing(JsonRpcRequest request)
    {
        Log("Handling ping");
        return JsonRpcResponse.Success(request.Id, new { });
    }

    // ── Server Instructions ────────────────────────────────────────────

    private static string GetServerInstructions() => """
        You are working with a Calor project. Calor is a DSL for AI agents that compiles to C# on .NET 10.

        RULES:
        1. All new code MUST be written in Calor (.calr files), never raw C# (.cs).
        2. Before modifying a .cs file, convert it to .calr first using calor_convert.
        3. After writing any .calr code, validate with calor_diagnose.
        4. Never edit .g.cs files — they are auto-generated from .calr sources.

        SYNTAX ESSENTIALS:
        - Module: §M{id:Name} ... §/M{id}
        - Function: §F{id:name:returnType:vis} ... §/F{id}
        - Binding: §B{name:type} expr (immutable), §B{~name:type} expr (mutable)
        - Loop: §L{id:var:from:to:step} ... §/L{id}
        - Conditional: §IF{id} (cond) → §R expr
        - Typed literals: INT:42, STR:"hello", BOOL:true, FLOAT:3.14
        - Expressions use prefix notation: (+ a b), (== x 0), (% i 15)
        - Types: i32, i64, str, bool, f64, void, ?i32 (option), i32!str (result)
        - Contracts: §Q (precondition), §S (postcondition)
        - Effects: §E{cw,db,net}

        WORKFLOW: Use calor_syntax_lookup for any unfamiliar construct. Use calor_verify for contract checking. Use calor_explain_error for diagnostic help.
        """;

    private void Log(string message)
    {
        if (_verbose && _log != null)
        {
            _log.WriteLine($"[MCP] {message}");
        }
    }
}
