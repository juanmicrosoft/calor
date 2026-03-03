using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Mcp;

/// <summary>
/// JSON-RPC 2.0 request message.
/// </summary>
public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement? Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; init; }

    /// <summary>
    /// True if this is a notification (no id, no response expected).
    /// </summary>
    [JsonIgnore]
    public bool IsNotification => Id == null || Id.Value.ValueKind == JsonValueKind.Undefined;
}

/// <summary>
/// JSON-RPC 2.0 response message.
/// </summary>
public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement? Id { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; init; }

    public static JsonRpcResponse Success(JsonElement? id, object? result) => new()
    {
        Id = id,
        Result = result
    };

    public static JsonRpcResponse Failure(JsonElement? id, int code, string message, object? data = null) => new()
    {
        Id = id,
        Error = new JsonRpcError { Code = code, Message = message, Data = data }
    };
}

/// <summary>
/// JSON-RPC 2.0 error object.
/// </summary>
public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }

    // Standard JSON-RPC error codes
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

/// <summary>
/// MCP server information returned during initialization.
/// </summary>
public sealed class McpServerInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

/// <summary>
/// MCP server capabilities.
/// </summary>
public sealed class McpCapabilities
{
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpToolsCapability? Tools { get; init; }

    [JsonPropertyName("resources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpResourcesCapability? Resources { get; init; }

    [JsonPropertyName("prompts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpPromptsCapability? Prompts { get; init; }
}

/// <summary>
/// Resources capability declaration.
/// </summary>
public sealed class McpResourcesCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

/// <summary>
/// Prompts capability declaration.
/// </summary>
public sealed class McpPromptsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

/// <summary>
/// Tools capability declaration.
/// </summary>
public sealed class McpToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

/// <summary>
/// MCP initialize result.
/// </summary>
public sealed class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; init; }

    [JsonPropertyName("capabilities")]
    public required McpCapabilities Capabilities { get; init; }

    [JsonPropertyName("serverInfo")]
    public required McpServerInfo ServerInfo { get; init; }

    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; init; }
}

/// <summary>
/// MCP tool definition.
/// </summary>
public sealed class McpTool
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public required JsonElement InputSchema { get; init; }

    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpToolAnnotations? Annotations { get; init; }
}

/// <summary>
/// Tool annotations providing hints to clients about tool behavior.
/// All fields are hints — not guarantees — per the MCP spec.
/// </summary>
public sealed class McpToolAnnotations
{
    /// <summary>If true, the tool does not modify state (safe to call without confirmation).</summary>
    [JsonPropertyName("readOnlyHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReadOnlyHint { get; init; }

    /// <summary>If true, the tool may perform destructive or irreversible operations.</summary>
    [JsonPropertyName("destructiveHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DestructiveHint { get; init; }

    /// <summary>If true, calling the tool again with the same arguments produces the same result.</summary>
    [JsonPropertyName("idempotentHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IdempotentHint { get; init; }

    /// <summary>If true, the tool interacts with external entities beyond the server.</summary>
    [JsonPropertyName("openWorldHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OpenWorldHint { get; init; }
}

/// <summary>
/// MCP tools/list result.
/// </summary>
public sealed class McpToolsListResult
{
    [JsonPropertyName("tools")]
    public required IReadOnlyList<McpTool> Tools { get; init; }
}

/// <summary>
/// MCP tools/call parameters.
/// </summary>
public sealed class McpToolCallParams
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; init; }
}

/// <summary>
/// MCP tool result.
/// </summary>
public sealed class McpToolResult
{
    [JsonPropertyName("content")]
    public required IReadOnlyList<McpContent> Content { get; init; }

    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; init; }

    public static McpToolResult Text(string text, bool isError = false) => new()
    {
        Content = [new McpContent { Type = "text", Text = text }],
        IsError = isError
    };

    public static McpToolResult Json(object value, bool isError = false)
    {
        var json = JsonSerializer.Serialize(value, McpJsonOptions.Default);
        return new McpToolResult
        {
            Content = [new McpContent { Type = "text", Text = json }],
            IsError = isError
        };
    }

    public static McpToolResult Error(string message) => Text(message, isError: true);
}

/// <summary>
/// MCP content item.
/// </summary>
public sealed class McpContent
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; init; }
}

/// <summary>
/// Shared JSON serialization options for MCP.
/// </summary>
public static class McpJsonOptions
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static JsonSerializerOptions Indented { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}

// ── Resources ──────────────────────────────────────────────────────

/// <summary>MCP resource definition (static, URI-addressed content).</summary>
public sealed class McpResource
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }
}

/// <summary>Result for resources/list.</summary>
public sealed class McpResourcesListResult
{
    [JsonPropertyName("resources")]
    public required IReadOnlyList<McpResource> Resources { get; init; }
}

/// <summary>Result for resources/read.</summary>
public sealed class McpResourceReadResult
{
    [JsonPropertyName("contents")]
    public required IReadOnlyList<McpResourceContent> Contents { get; init; }
}

/// <summary>A single resource content item.</summary>
public sealed class McpResourceContent
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }
}

// ── Prompts ────────────────────────────────────────────────────────

/// <summary>MCP prompt definition (reusable workflow template).</summary>
public sealed class McpPrompt
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<McpPromptArgument>? Arguments { get; init; }
}

/// <summary>A prompt argument definition.</summary>
public sealed class McpPromptArgument
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Required { get; init; }
}

/// <summary>Result for prompts/list.</summary>
public sealed class McpPromptsListResult
{
    [JsonPropertyName("prompts")]
    public required IReadOnlyList<McpPrompt> Prompts { get; init; }
}

/// <summary>Result for prompts/get.</summary>
public sealed class McpPromptGetResult
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<McpPromptMessage> Messages { get; init; }
}

/// <summary>A message in a prompt result.</summary>
public sealed class McpPromptMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required McpContent Content { get; init; }
}

// ── Completion ─────────────────────────────────────────────────────

/// <summary>Result for completion/complete.</summary>
public sealed class McpCompletionResult
{
    [JsonPropertyName("completion")]
    public required McpCompletionData Completion { get; init; }
}

/// <summary>Completion data with values and pagination.</summary>
public sealed class McpCompletionData
{
    [JsonPropertyName("values")]
    public required IReadOnlyList<string> Values { get; init; }

    [JsonPropertyName("hasMore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasMore { get; init; }

    [JsonPropertyName("total")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Total { get; init; }
}
