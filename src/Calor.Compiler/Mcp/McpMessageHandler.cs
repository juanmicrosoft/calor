using System.Text.Json;
using Calor.Compiler.Init;
using Calor.Compiler.Mcp.Tools;

namespace Calor.Compiler.Mcp;

/// <summary>
/// Handles MCP protocol messages and routes them to appropriate handlers.
/// </summary>
public sealed class McpMessageHandler
{
    private const string ProtocolVersion = "2024-11-05";

    private readonly Dictionary<string, IMcpTool> _tools;
    private readonly bool _verbose;
    private readonly TextWriter? _log;

    public McpMessageHandler(bool verbose = false, TextWriter? log = null)
    {
        _verbose = verbose;
        _log = log;

        // Register all tools
        _tools = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);
        RegisterTool(new CompileTool());
        RegisterTool(new VerifyTool());
        RegisterTool(new AnalyzeTool());
        RegisterTool(new ConvertTool());
        RegisterTool(new SyntaxHelpTool());
        RegisterTool(new LintTool());
        RegisterTool(new FormatTool());
        RegisterTool(new DiagnoseTool());
        RegisterTool(new IdsTool());
        RegisterTool(new AssessTool());
        RegisterTool(new SyntaxLookupTool());
        RegisterTool(new TypeCheckTool());
        RegisterTool(new VerifyContractsTool());
        RegisterTool(new ValidateSnippetTool());
    }

    private void RegisterTool(IMcpTool tool)
    {
        _tools[tool.Name] = tool;
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
                "initialized" => null, // Notification, no response
                "tools/list" => HandleToolsList(request),
                "tools/call" => await HandleToolsCallAsync(request),
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
                Tools = new McpToolsCapability { ListChanged = false }
            },
            ServerInfo = new McpServerInfo
            {
                Name = "calor",
                Version = EmbeddedResourceHelper.GetVersion()
            }
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
            InputSchema = t.GetInputSchema()
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

        var result = await tool.ExecuteAsync(callParams.Arguments);
        Log($"Tool {callParams.Name} completed (isError: {result.IsError})");

        return JsonRpcResponse.Success(request.Id, result);
    }

    private JsonRpcResponse HandlePing(JsonRpcRequest request)
    {
        Log("Handling ping");
        return JsonRpcResponse.Success(request.Id, new { });
    }

    private void Log(string message)
    {
        if (_verbose && _log != null)
        {
            _log.WriteLine($"[MCP] {message}");
        }
    }
}
