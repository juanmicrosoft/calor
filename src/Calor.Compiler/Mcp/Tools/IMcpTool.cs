using System.Text.Json;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Interface for MCP tools that can be executed by the server.
/// </summary>
public interface IMcpTool
{
    /// <summary>
    /// The unique name of the tool (e.g., "calor_compile").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description of what the tool does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Returns the JSON Schema for the tool's input parameters.
    /// </summary>
    JsonElement GetInputSchema();

    /// <summary>
    /// Optional annotations hinting at tool behavior for clients.
    /// </summary>
    McpToolAnnotations? Annotations => null;

    /// <summary>
    /// Executes the tool with the given arguments.
    /// </summary>
    /// <param name="arguments">The tool arguments as a JSON element, or null if no arguments.</param>
    /// <param name="cancellationToken">Cancellation token for aborting long-running operations.</param>
    /// <returns>The tool result.</returns>
    Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for MCP tools with common functionality.
/// </summary>
public abstract class McpToolBase : IMcpTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    /// <summary>Override to provide tool annotations (readOnlyHint, destructiveHint, etc.).</summary>
    public virtual McpToolAnnotations? Annotations => null;

    private JsonElement? _cachedSchema;

    public JsonElement GetInputSchema()
    {
        _cachedSchema ??= JsonDocument.Parse(GetInputSchemaJson()).RootElement.Clone();
        return _cachedSchema.Value;
    }

    /// <summary>
    /// Override to provide the JSON schema string.
    /// </summary>
    protected abstract string GetInputSchemaJson();

    public abstract Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default);

    // Maximum allowed source input size (512 KB). Larger inputs are rejected.
    protected const int MaxSourceLength = 512 * 1024;

    /// <summary>
    /// Helper to get a required string property from arguments.
    /// </summary>
    protected static string? GetString(JsonElement? arguments, string propertyName)
    {
        if (arguments == null || arguments.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (arguments.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();

        return null;
    }

    /// <summary>
    /// Helper to get an optional boolean property from arguments.
    /// </summary>
    protected static bool GetBool(JsonElement? arguments, string propertyName, bool defaultValue = false)
    {
        if (arguments == null || arguments.Value.ValueKind != JsonValueKind.Object)
            return defaultValue;

        if (arguments.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.True)
            return true;
        if (prop.ValueKind == JsonValueKind.False)
            return false;

        return defaultValue;
    }

    /// <summary>
    /// Helper to get an optional integer property from arguments.
    /// </summary>
    protected static int GetInt(JsonElement? arguments, string propertyName, int defaultValue = 0)
    {
        if (arguments == null || arguments.Value.ValueKind != JsonValueKind.Object)
            return defaultValue;

        if (arguments.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();

        return defaultValue;
    }

    /// <summary>
    /// Helper to get nested options object.
    /// </summary>
    protected static JsonElement? GetOptions(JsonElement? arguments, string propertyName = "options")
    {
        if (arguments == null || arguments.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (arguments.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Object)
            return prop;

        return null;
    }

    /// <summary>
    /// Validates that a source string is not too large. Returns an error result if it exceeds MaxSourceLength.
    /// </summary>
    protected static McpToolResult? ValidateSourceSize(string source, string paramName = "source")
    {
        if (source.Length > MaxSourceLength)
            return McpToolResult.Error($"Parameter '{paramName}' exceeds maximum allowed size of {MaxSourceLength / 1024} KB");
        return null;
    }

    /// <summary>
    /// Validates that a file path is safe (no path traversal, within a reasonable scope).
    /// Returns an error result if the path is unsafe.
    /// </summary>
    protected static McpToolResult? ValidatePath(string path, string paramName = "path")
    {
        if (string.IsNullOrWhiteSpace(path))
            return McpToolResult.Error($"Parameter '{paramName}' must not be empty");

        // Reject path traversal attempts
        var normalized = Path.GetFullPath(path);
        if (normalized.Contains("..") || path.Contains(".."))
            return McpToolResult.Error($"Parameter '{paramName}' must not contain path traversal sequences");

        return null;
    }
}
