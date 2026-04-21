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
    private static readonly int MaxConcurrentTools = Math.Max(2, Environment.ProcessorCount / 2);
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrentTools);

    // Reject new heavy work when the process exceeds this memory threshold.
    // Defaults to 50% of available physical memory (min 512 MB). Override with CALOR_MCP_MAX_MEMORY_MB.
    private static readonly long MemoryPressureThresholdBytes =
        long.TryParse(Environment.GetEnvironmentVariable("CALOR_MCP_MAX_MEMORY_MB"), out var mb)
            ? mb * 1024L * 1024L
            : Math.Max(512L * 1024L * 1024L, (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * 0.5));
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
        RegisterTool(new CheckTool());

        // ── C# ↔ Calor conversion ──────────────────────────
        RegisterTool(new ConvertTool());
        RegisterTool(new BatchTool());

        // ── Syntax & documentation ──────────────────────────
        RegisterTool(new HelpTool());

        // ── Code navigation ─────────────────────────────────
        RegisterTool(new NavigateTool());
        RegisterTool(new StructureTool());

        // ── Edit support & formatting ───────────────────────
        RegisterTool(new EditPreviewTool());
        RegisterTool(new FormatTool());

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
            Uri = "calor://primer",
            Name = "Calor Language Primer",
            Description = "Canonical examples for agents: module, function, effects, contracts, class, closing tags, IDs. Read once at session start.",
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
            Description = "All Calor section tags with opening/closing forms",
            MimeType = "application/json"
        });
        RegisterResource(new McpResource
        {
            Uri = "calor://id-prefixes",
            Name = "ID Prefix Catalog",
            Description = "ULID ID prefixes for each declaration kind",
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

        // Wait for memory pressure to subside before running heavy tools
        if (tool is BatchTool or ConvertTool or CompileTool or AnalyzeTool)
        {
            var memoryUsed = GetProcessMemory();
            if (memoryUsed > MemoryPressureThresholdBytes)
            {
                var usedMb = memoryUsed / (1024 * 1024);
                Log($"Tool {callParams.Name} waiting for memory pressure to subside ({usedMb} MB used)");

                // Try a GC and wait up to 30s for memory to drop
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                const int maxWaitMs = 30_000;
                const int pollIntervalMs = 2_000;
                var waited = 0;
                while (waited < maxWaitMs)
                {
                    memoryUsed = GetProcessMemory();
                    if (memoryUsed <= MemoryPressureThresholdBytes)
                        break;
                    var jitter = Random.Shared.Next(0, 500); // 0-500ms random jitter to avoid thundering herd
                    await Task.Delay(pollIntervalMs + jitter, _serverCancellation);
                    waited += pollIntervalMs + jitter;
                }

                memoryUsed = GetProcessMemory();
                if (memoryUsed > MemoryPressureThresholdBytes)
                {
                    usedMb = memoryUsed / (1024 * 1024);
                    var thresholdMb = MemoryPressureThresholdBytes / (1024 * 1024);
                    Log($"Tool {callParams.Name} rejected after waiting: memory still at {usedMb} MB");
                    return JsonRpcResponse.Success(request.Id,
                        McpToolResult.Error($"Server under memory pressure ({usedMb} MB used, {thresholdMb} MB threshold) " +
                            "after waiting 30s. Wait for current operations to finish, then retry. " +
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

    private static string GetPrimerContent() => """
        CALOR LANGUAGE PRIMER — Read this once to understand the syntax.

        IDs are ULID-based: f_01JWDG3K..., m_01JWDG3K... (not sequential).
        Closing tags use abbreviated forms: §/F (not §/IF), §/M, §/CL, etc.

        ── Module ──────────────────────────────────────────

        §M{m_01JWDG3KABCDEFGHJKMNPQRST:Calculator}

        §F{f_01JWDG3LABCDEFGHJKMNPQRST:Add:pub}
          §I{i32:a}
          §I{i32:b}
          §O{i32}
          §R (+ a b)
        §/F{f_01JWDG3LABCDEFGHJKMNPQRST}

        §/M{m_01JWDG3KABCDEFGHJKMNPQRST}

        ── Function with effects ───────────────────────────

        §F{f_01JWDG3MABCDEFGHJKMNPQRST:SaveUser:pub}
          §I{User:user}
          §O{void}
          §E{db:w,cw}
          §C{DbContext.SaveChanges} §/C
          §C{ILogger.LogInformation} §A STR:"User saved" §/C
        §/F{f_01JWDG3MABCDEFGHJKMNPQRST}

        ── Function with contracts ─────────────────────────

        §F{f_01JWDG3NABCDEFGHJKMNPQRST:Divide:pub}
          §I{f64:a}
          §I{f64:b}
          §O{f64}
          §Q (!= b FLOAT:0.0)
          §S (>= §RESULT FLOAT:0.0)
          §R (/ a b)
        §/F{f_01JWDG3NABCDEFGHJKMNPQRST}

        ── Immutable and mutable bindings ──────────────────

        §B{name:str} STR:"hello"
        §B{~counter:i32} INT:0

        ── If/else ─────────────────────────────────────────

        §IF{if01} (> x INT:0)
          §R x
        §EL
          §R (- INT:0 x)
        §/I{if01}

        ── For loop ────────────────────────────────────────

        §L{l01:i:INT:0:INT:10:INT:1}
          §C{Console.WriteLine} §A i §/C
        §/L{l01}

        ── Class with method ───────────────────────────────

        §CL{c_01JWDG3PABCDEFGHJKMNPQRST:UserService:pub}

          §MT{mt_01JWDG3QABCDEFGHJKMNPQRST:GetName:pub}
            §I{i32:id}
            §O{str}
            §E{db:r}
            §R STR:"user"
          §/MT{mt_01JWDG3QABCDEFGHJKMNPQRST}

        §/CL{c_01JWDG3PABCDEFGHJKMNPQRST}

        ── Key rules ───────────────────────────────────────

        CLOSING TAGS: §/F (function), §/M (module), §/CL (class),
          §/I (if block — NOT §/IF), §/L (loop), §/C (call),
          §/MT (method), §/CT (constructor)

        TYPED LITERALS: INT:42, STR:"hello", BOOL:true, FLOAT:3.14

        EFFECTS: §E{db:w,cw} — declares side effects. See calor://effects.

        IDS: Every declaration needs a ULID ID with the right prefix.
          Use calor_generate_ids to get fresh IDs. See calor://id-prefixes.
        """;

    internal static string GetEffectCatalogJsonPublic() => GetEffectCatalogJson();
    private static string GetEffectCatalogJson() => """
        {"effects":[
          {"code":"cw","kind":"IO","description":"Console write","examples":["Console.WriteLine","ILogger.Log"]},
          {"code":"cr","kind":"IO","description":"Console read","examples":["Console.ReadLine"]},
          {"code":"fs:r","kind":"IO","description":"Filesystem read","examples":["File.ReadAllText","StreamReader.ReadToEnd"]},
          {"code":"fs:w","kind":"IO","description":"Filesystem write","examples":["File.WriteAllText","StreamWriter.Write"]},
          {"code":"fs:rw","kind":"IO","description":"Filesystem read+write (encompasses fs:r and fs:w)","examples":["FileStream.Open"]},
          {"code":"net:r","kind":"IO","description":"Network read","examples":["HttpClient.GetStringAsync"]},
          {"code":"net:w","kind":"IO","description":"Network write","examples":["HttpResponse.WriteAsync"]},
          {"code":"net:rw","kind":"IO","description":"Network read+write (encompasses net:r and net:w)","examples":["HttpClient.GetAsync","TcpClient.Connect"]},
          {"code":"db:r","kind":"IO","description":"Database read","examples":["DbContext.Find","DbCommand.ExecuteReader","SqlMapper.Query"]},
          {"code":"db:w","kind":"IO","description":"Database write","examples":["DbContext.SaveChanges","DbCommand.ExecuteNonQuery","SqlMapper.Execute"]},
          {"code":"db:rw","kind":"IO","description":"Database read+write (encompasses db:r and db:w)","examples":["DbConnection.Open"]},
          {"code":"env:r","kind":"IO","description":"Environment/config read","examples":["Environment.GetEnvironmentVariable","IConfiguration.GetSection"]},
          {"code":"env:rw","kind":"IO","description":"Environment read+write","examples":["Environment.SetEnvironmentVariable"]},
          {"code":"proc","kind":"IO","description":"Process execution","examples":["Process.Start"]},
          {"code":"alloc","kind":"Memory","description":"Significant allocation","examples":["GC.Collect"]},
          {"code":"unsafe","kind":"Memory","description":"Unsafe memory operation","examples":["Marshal.AllocHGlobal"]},
          {"code":"time","kind":"Nondeterminism","description":"System time dependency","examples":["DateTime.Now","Stopwatch.StartNew"]},
          {"code":"rand","kind":"Nondeterminism","description":"Random number generation","examples":["Random.Next","Guid.NewGuid"]},
          {"code":"mut","kind":"Mutation","description":"Heap mutation","examples":["List.Add","Dictionary.TryAdd"]},
          {"code":"throw","kind":"Exception","description":"Intentional exception throw","examples":["ArgumentException","InvalidOperationException"]}
        ],"subtyping":[
          {"broad":"fs:rw","encompasses":["fs:r","fs:w"]},
          {"broad":"net:rw","encompasses":["net:r","net:w"]},
          {"broad":"db:rw","encompasses":["db:r","db:w"]},
          {"broad":"env:rw","encompasses":["env:r"]}
        ]}
        """;

    internal static string GetTagCatalogJsonPublic() => GetTagCatalogJson();
    private static string GetTagCatalogJson() => """
        {"tags":[
          {"open":"§M{id:Name}","close":"§/M{id}","description":"Module","idPrefix":"m_"},
          {"open":"§F{id:Name:vis}","close":"§/F{id}","description":"Function","idPrefix":"f_"},
          {"open":"§AF{id:Name:vis}","close":"§/AF{id}","description":"Async function","idPrefix":"f_"},
          {"open":"§CL{id:Name:vis}","close":"§/CL{id}","description":"Class","idPrefix":"c_"},
          {"open":"§IF{id}","close":"§/I{id}","description":"If block (NOTE: close is §/I not §/IF)"},
          {"open":"§EI","close":"(none)","description":"Else-if branch"},
          {"open":"§EL","close":"(none)","description":"Else branch"},
          {"open":"§L{id:var:from:to:step}","close":"§/L{id}","description":"For loop"},
          {"open":"§C{target}","close":"§/C","description":"Method call"},
          {"open":"§A","close":"(inline)","description":"Argument to call"},
          {"open":"§R","close":"(inline)","description":"Return expression"},
          {"open":"§B{name:type}","close":"(inline)","description":"Immutable binding"},
          {"open":"§B{~name:type}","close":"(inline)","description":"Mutable binding (~ prefix)"},
          {"open":"§I{type:name}","close":"(inline)","description":"Function input parameter"},
          {"open":"§O{type}","close":"(inline)","description":"Function output type"},
          {"open":"§E{effects}","close":"(inline)","description":"Effect declaration (comma-separated codes)"},
          {"open":"§Q (expr)","close":"(inline)","description":"Precondition contract"},
          {"open":"§S (expr)","close":"(inline)","description":"Postcondition contract"},
          {"open":"§MT{id:Name:vis}","close":"§/MT{id}","description":"Method (inside class)","idPrefix":"mt_"},
          {"open":"§AMT{id:Name:vis}","close":"§/AMT{id}","description":"Async method","idPrefix":"mt_"},
          {"open":"§CT{id:vis}","close":"§/CT{id}","description":"Constructor","idPrefix":"ctor_"},
          {"open":"§P{id:Name:type:vis}","close":"§/P{id}","description":"Property","idPrefix":"p_"},
          {"open":"§IN{id:Name}","close":"§/IN{id}","description":"Interface","idPrefix":"i_"},
          {"open":"§EN{id:Name:vis}","close":"§/EN{id}","description":"Enum","idPrefix":"e_"},
          {"open":"§OP{id:operator:vis}","close":"§/OP{id}","description":"Operator overload","idPrefix":"op_"}
        ],"closingTagRules":"Closing tags use ABBREVIATED forms: §/F (not §/IF), §/M, §/CL, §/I (for if blocks), §/L, §/MT, §/CT, §/P, §/IN, §/EN, §/OP. The most common error is using §/IF instead of §/I for if-blocks."}
        """;

    internal static string GetIdPrefixCatalogJsonPublic() => GetIdPrefixCatalogJson();
    private static string GetIdPrefixCatalogJson() => """
        {"prefixes":[
          {"prefix":"m_","kind":"module","example":"m_01JWDG3KABCDEFGHJKMNPQRST"},
          {"prefix":"f_","kind":"function","example":"f_01JWDG3LABCDEFGHJKMNPQRST"},
          {"prefix":"c_","kind":"class","example":"c_01JWDG3MABCDEFGHJKMNPQRST"},
          {"prefix":"i_","kind":"interface","example":"i_01JWDG3NABCDEFGHJKMNPQRST"},
          {"prefix":"p_","kind":"property","example":"p_01JWDG3PABCDEFGHJKMNPQRST"},
          {"prefix":"mt_","kind":"method","example":"mt_01JWDG3QABCDEFGHJKMNPQRST"},
          {"prefix":"ctor_","kind":"constructor","example":"ctor_01JWDG3RABCDEFGHJKMNPQRST"},
          {"prefix":"e_","kind":"enum","example":"e_01JWDG3SABCDEFGHJKMNPQRST"},
          {"prefix":"op_","kind":"operator","example":"op_01JWDG3TABCDEFGHJKMNPQRST"}
        ],"format":"IDs are ULID-based (26-character Crockford Base32). Generate with calor_generate_ids. Sequential IDs like f001 are test-only and flagged by Calor0804 in production."}
        """;

    internal static string GetWorkflowsJsonPublic() => GetWorkflowsJson();
    private static string GetWorkflowsJson() => """
        {"tasks":{
          "write-new-function":{"description":"Write a new Calor function from scratch","steps":[
            {"tool":"calor_generate_ids","args":{"needs":[{"kind":"function","count":1}]},"why":"Get a ULID with correct f_ prefix"},
            {"tool":"calor_help","args":{"action":"effects_for","calls":["DbContext.SaveChanges"]},"why":"Look up effects for planned external calls"},
            {"action":"write_code","why":"Write the function using the ID, effects, and correct closing tags from calor://tags"},
            {"tool":"calor_compile","args":{"autoFix":true},"why":"Compile and auto-fix syntax/ID/effect errors"}
          ]},
          "fix-compilation-errors":{"description":"Fix errors in existing Calor code","steps":[
            {"tool":"calor_compile","args":{"autoFix":true},"why":"Auto-fix all high-confidence errors (parser, ID, effects)"},
            {"condition":"remaining medium/low diagnostics","action":"Read diagnostics and apply suggested fixes manually"},
            {"condition":"Calor0411 unknown external calls","tool":"calor_help","args":{"action":"effects_for"},"why":"Look up effects for unresolved calls"}
          ]},
          "convert-csharp-to-calor":{"description":"Convert a C# file to Calor","steps":[
            {"tool":"calor_convert","args":{"includeEffects":true},"why":"Convert with auto-inferred effect declarations"},
            {"tool":"calor_compile","args":{"autoFix":true},"why":"Verify and fix any remaining issues"}
          ]},
          "add-effects-to-function":{"description":"Add correct effect declarations to a function","steps":[
            {"tool":"calor_compile","args":{"includeEffectSummary":true},"why":"Get declared vs computed effects per function"},
            {"action":"Add missing effects from the effectSummary.missing field to §E{...}"}
          ]},
          "write-new-module":{"description":"Write a new Calor module with multiple functions","steps":[
            {"tool":"calor_generate_ids","args":{"needs":[{"kind":"module","count":1},{"kind":"function","count":3}]},"why":"Get ULIDs for all declarations"},
            {"action":"Write module using IDs, consulting calor://primer for syntax patterns"},
            {"tool":"calor_compile","args":{"autoFix":true,"includeEffectSummary":true},"why":"Compile, fix, and verify effects"}
          ]}
        }}
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

    private static string GetServerInstructions() => """
        You are working with a Calor project. Calor is a DSL for AI agents that compiles to C# on .NET 10.

        RULES:
        1. All new code MUST be written in Calor (.calr files), never raw C# (.cs).
        2. Before modifying a .cs file, convert it to .calr first using calor_convert.
        3. After writing any .calr code, compile with calor_compile (autoFix is on by default).
        4. Never edit .g.cs files — they are auto-generated from .calr sources.

        SYNTAX ESSENTIALS:
        - Module: §M{id:Name} ... §/M{id}
        - Function: §F{id:Name:vis} ... §/F{id}
        - Binding: §B{name:type} expr (immutable), §B{~name:type} expr (mutable)
        - Loop: §L{id:var:from:to:step} ... §/L{id}
        - Conditional: §IF{id} (cond) ... §/I{id} (NOTE: close with §/I, not §/IF)
        - Typed literals: INT:42, STR:"hello", BOOL:true, FLOAT:3.14
        - Expressions use prefix notation: (+ a b), (== x 0), (% i 15)
        - Types: i32, i64, str, bool, f64, void, ?i32 (option), i32!str (result)
        - Contracts: §Q (precondition), §S (postcondition)
        - Effects: §E{cw,db:w,net:rw}

        WORKFLOW: Read calor://primer at session start. Use calor_help for unfamiliar syntax. Use calor_verify for contract checking.
        """;

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
