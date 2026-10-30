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
    private readonly Sessions.ProjectSessionManager _projectSessions = new();
    private readonly Dictionary<string, McpResource> _resources;
    private readonly Dictionary<string, McpPrompt> _prompts;
    private readonly bool _verbose;
    private readonly TextWriter? _log;
    private static readonly int MaxConcurrentTools = Math.Max(2, Environment.ProcessorCount / 2);
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrentTools);

    private readonly McpMemoryAdmissionPolicy _memoryAdmission;
    private CancellationToken _serverCancellation;

    public McpMessageHandler(bool verbose = false, TextWriter? log = null, string? rootDirectory = null)
        : this(McpMemoryAdmissionPolicy.Disabled, verbose, log, rootDirectory)
    {
    }

    internal McpMessageHandler(
        McpMemoryAdmissionPolicy memoryAdmission,
        bool verbose = false,
        TextWriter? log = null,
        string? rootDirectory = null)
    {
        _memoryAdmission = memoryAdmission;
        _verbose = verbose;
        _log = log;

        // Register all tools
        _tools = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);

        // ── Core compilation & verification ─────────────────
        RegisterTool(new CompileTool());
        RegisterTool(new VerifyTool());
        RegisterTool(new AnalyzeTool());
        RegisterTool(new CheckTool());

        // ── C# ↔ Calor conversion ──────────────────────────
        RegisterTool(new ConvertTool());
        RegisterTool(new BatchTool());

        // ── Syntax & documentation ──────────────────────────
        RegisterTool(new HelpTool());

        // ── Code navigation ─────────────────────────────────
        RegisterTool(new NavigateTool());
        RegisterTool(new StructureTool());
        // v0.16 E7: the project index (callers / callees / impact / effects),
        // read the way `calor query` reads it. rootDirectory confines it the
        // way it confines the write tools below: resolving a stale or missing
        // index REBUILDS it, so this tool writes.
        RegisterTool(new QueryTool(rootDirectory));

        // ── Edit support & formatting ───────────────────────
        RegisterTool(new EditPreviewTool());
        RegisterTool(new FormatTool());

        // ── Project sessions & transactional writes (loop plan WS2 D2.1/D2.4)
        // rootDirectory pins the write-confinement root explicitly (mcp
        // --root); when null the tools fall back to the server process CWD —
        // which depends on how the MCP client spawned us, so harnesses
        // should always pin it.
        RegisterTool(new SessionOpenTool(_projectSessions, rootDirectory));
        RegisterTool(new SessionCloseTool(_projectSessions));
        RegisterTool(new FileWriteTool(_projectSessions, rootDirectory));

        // ── Refinement types & obligations ──────────────────
        RegisterTool(new RefineTool());

        // ── Auto-fix ─────────────────────────────────────────
        RegisterTool(new FixTool());

        // ── Migration pipeline ──────────────────────────────
        RegisterTool(new MigrateTool());

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
        RegisterResource(new McpResource
        {
            Uri = "calor://reference",
            Name = "Current Language Reference",
            Description = "Generated current constructs and complete examples, shared by help and agent task consumers",
            MimeType = "text/markdown"
        });
        RegisterResource(new McpResource
        {
            Uri = "calor://primer",
            Name = "Calor Language Primer",
            Description = "Canonical examples for agents: module, function, effects, contracts, branching, loops, bindings, class. Indent-only; every example compiles. Read once at session start.",
            MimeType = "text/plain"
        });
        RegisterResource(new McpResource
        {
            Uri = "calor://effects",
            Name = "Effect Code Catalog",
            Description = "All valid effect codes with descriptions and common examples",
            MimeType = "application/json"
        });
        RegisterResource(new McpResource
        {
            Uri = "calor://tags",
            Name = "Section Tag Catalog",
            Description = "Current documented Calor tags, syntax and delimiter guidance",
            MimeType = "application/json"
        });
        RegisterResource(new McpResource
        {
            Uri = "calor://id-prefixes",
            Name = "Tooling Identity Guidance",
            Description = "Tooling-owned identity; source declarations have no ID prefixes (legacy resource URI retained)",
            MimeType = "application/json"
        });
        RegisterResource(new McpResource
        {
            Uri = "calor://workflows",
            Name = "Agent Workflow Guide",
            Description = "Structured decision trees for common tasks: write function, fix errors, convert C#",
            MimeType = "application/json"
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

    /// <summary>The memory admission policy in force. Exposed so tests can pin which contexts gate on process memory.</summary>
    internal McpMemoryAdmissionPolicy MemoryAdmission => _memoryAdmission;

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

        // Execute with timeout, cancellation, and concurrency limiting
        var timeout = tool is McpToolBase toolBase
            ? TimeSpan.FromSeconds(toolBase.TimeoutSeconds)
            : DefaultToolTimeout;
        var sw = Stopwatch.StartNew();
        McpToolResult result;

        // Wait for memory pressure to subside before running heavy tools.
        //
        // This is admission control for a long-lived stdio server: `calor mcp` owns
        // its process, so process RSS is a fair proxy for "how much the tools I have
        // been running are costing". It is measured with WorkingSet64, which counts
        // the WHOLE process — so in any host the handler does not own, it attributes
        // the host's memory to the next MCP tool and refuses it. Enabled only when a
        // server opts in (see McpServer); direct/embedded construction gets
        // McpMemoryAdmissionPolicy.Disabled.
        //
        // That distinction is not hypothetical. Six tests here construct the handler
        // directly and ran inside an xUnit host holding 6,810 tests' worth of heap
        // plus Roslyn and Z3. The host crossed the 50%-of-RAM threshold on Linux CI
        // runners but not on higher-memory dev machines, so each of the six spent the
        // full 30s wait and then failed with "Server under memory pressure" — green
        // locally, red on CI, and blamed on flakiness (#897) for two weeks. An
        // embedded host's memory is not the MCP tool's to police.
        if (_memoryAdmission.Enabled && tool is BatchTool or ConvertTool or CompileTool or AnalyzeTool)
        {
            var threshold = _memoryAdmission.ThresholdBytes;
            var memoryUsed = GetProcessMemory();
            if (memoryUsed > threshold)
            {
                var usedMb = memoryUsed / (1024 * 1024);
                Log($"Tool {callParams.Name} waiting for memory pressure to subside ({usedMb} MB used)");

                // Try a GC and wait for memory to drop
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                var maxWaitMs = (int)_memoryAdmission.MaxWait.TotalMilliseconds;
                var pollIntervalMs = (int)_memoryAdmission.PollInterval.TotalMilliseconds;
                var waited = 0;
                while (waited < maxWaitMs)
                {
                    memoryUsed = GetProcessMemory();
                    if (memoryUsed <= threshold)
                        break;
                    var jitter = Random.Shared.Next(0, 500); // 0-500ms random jitter to avoid thundering herd
                    await Task.Delay(pollIntervalMs + jitter, _serverCancellation);
                    waited += pollIntervalMs + jitter;
                }

                memoryUsed = GetProcessMemory();
                if (memoryUsed > threshold)
                {
                    usedMb = memoryUsed / (1024 * 1024);
                    var thresholdMb = threshold / (1024 * 1024);
                    var waitedSeconds = _memoryAdmission.MaxWait.TotalSeconds;
                    Log($"Tool {callParams.Name} rejected after waiting: memory still at {usedMb} MB");
                    return JsonRpcResponse.Success(request.Id,
                        McpToolResult.Error($"Server under memory pressure ({usedMb} MB used, {thresholdMb} MB threshold) " +
                            $"after waiting {waitedSeconds:0.#}s. Wait for current operations to finish, then retry. " +
                            "Set CALOR_MCP_MAX_MEMORY_MB to adjust the threshold."));
                }

                Log($"Tool {callParams.Name} proceeding after memory dropped to {memoryUsed / (1024 * 1024)} MB");
            }
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_serverCancellation);
            cts.CancelAfter(timeout);

            // Limit concurrent tool executions to prevent runaway memory usage
            await _concurrencyLimiter.WaitAsync(cts.Token);
            try
            {
                result = await tool.ExecuteAsync(callParams.Arguments, cts.Token);
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            var queueOrExec = sw.ElapsedMilliseconds < 1000 ? "waiting for execution slot" : "during execution";
            Log($"Tool {callParams.Name} cancelled {queueOrExec} after {sw.ElapsedMilliseconds}ms");
            return JsonRpcResponse.Success(request.Id,
                McpToolResult.Error($"Tool '{callParams.Name}' timed out after {timeout.TotalSeconds}s ({queueOrExec})"));
        }

        sw.Stop();
        Log($"Tool {callParams.Name} completed in {sw.ElapsedMilliseconds}ms (isError: {result.IsError})");

        // Prompt GC to reclaim Roslyn/Z3 objects after memory-intensive operations
        if (tool is BatchTool or ConvertTool or CompileTool or AnalyzeTool)
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        }

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
            "calor://reference" => LoadEmbeddedResource(assembly,
                "Calor.Compiler.Resources.current-language-reference.md", uri, "text/markdown"),
            "calor://error-catalog" => LoadEmbeddedResource(assembly,
                "Calor.Compiler.Resources.error-suggestions.json", uri, "application/json"),
            "calor://type-mappings" => new McpResourceContent
            {
                Uri = uri,
                MimeType = "text/markdown",
                Text = GetTypeMappingsMarkdown()
            },
            "calor://primer" => new McpResourceContent
            {
                Uri = uri,
                MimeType = "text/plain",
                Text = GetPrimerContent()
            },
            "calor://effects" => new McpResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = GetEffectCatalogJson()
            },
            "calor://tags" => new McpResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = GetTagCatalogJson()
            },
            "calor://id-prefixes" => new McpResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = GetIdPrefixCatalogJson()
            },
            "calor://workflows" => new McpResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = GetWorkflowsJson()
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

    private static string GetTypeMappingsMarkdown() =>
        Init.EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.type-mappings.md");

    private static string? _primerCache;

    private static string GetPrimerContent()
    {
        // Single source of truth: the self-check-guarded syntax exemplar
        // (src/Calor.Compiler/Resources/agent-syntax-exemplar.md). E1a (2026-07-17,
        // n=30/arm) measured a syntax exemplar bringing green-field Calor to C#
        // iteration parity on one pair; this is that sheet. Throws if missing so a
        // packaging error fails loudly rather than serving a stub.
        if (_primerCache != null) return _primerCache;
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Calor.Compiler.Resources.agent-syntax-exemplar.md")
            ?? throw new InvalidOperationException(
                "Embedded resource 'Calor.Compiler.Resources.agent-syntax-exemplar.md' not found — " +
                "csproj LogicalName and this string have drifted.");
        using var reader = new StreamReader(stream);
        var raw = reader.ReadToEnd();
        // Strip authoring-only self-check suppression markers; they are noise to a
        // reading agent and mean nothing outside the drift checker.
        var lines = raw.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.Trim() != "<!-- drift:ignore -->");
        _primerCache = string.Join("\n", lines);
        return _primerCache;
    }

    internal static string GetPrimerContentPublic() => GetPrimerContent();

    internal static string GetEffectCatalogJsonPublic() => GetEffectCatalogJson();
    private static string GetEffectCatalogJson() =>
        Init.EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.effects.json");

    internal static string GetTagCatalogJsonPublic() => GetTagCatalogJson();
    private static string GetTagCatalogJson() =>
        Init.EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.tags.json");

    internal static string GetIdPrefixCatalogJsonPublic() => GetIdPrefixCatalogJson();
    private static string GetIdPrefixCatalogJson() =>
        Init.EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.id-prefixes.json");

    internal static string GetWorkflowsJsonPublic() => GetWorkflowsJson();
    private static string GetWorkflowsJson() =>
        Init.EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.workflows.json");

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
                        Text = $"Convert this C# code to Calor. Use calor_convert first, then calor_compile to validate (autoFix is on by default). " +
                               $"If there are §CSHARP interop blocks, use calor_help to check for native equivalents.\n\n" +
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

    private static string GetServerInstructions() =>
        Init.EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.server-instructions.md");

    // ── Test helpers (for McpRegistryValidationTests) ──────────────

    internal HashSet<string> GetRegisteredToolNamesForTest()
        => new(_tools.Keys, StringComparer.Ordinal);

    internal HashSet<string> GetRegisteredResourceUrisForTest()
        => new(_resources.Keys, StringComparer.Ordinal);

    internal string GetServerInstructionsForTest()
        => GetServerInstructions();

    private void Log(string message)
    {
        if (_verbose && _log != null)
        {
            _log.WriteLine($"[MCP] {message}");
        }
    }

    private static long GetProcessMemory()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            return proc.WorkingSet64;
        }
        catch
        {
            // Fallback to managed heap if process info unavailable
            return GC.GetTotalMemory(forceFullCollection: false);
        }
    }
}
