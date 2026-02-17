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
