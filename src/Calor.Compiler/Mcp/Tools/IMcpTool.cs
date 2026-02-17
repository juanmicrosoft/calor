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
    /// Executes the tool with the given arguments.
    /// </summary>
    /// <param name="arguments">The tool arguments as a JSON element, or null if no arguments.</param>
    /// <returns>The tool result.</returns>
    Task<McpToolResult> ExecuteAsync(JsonElement? arguments);
}

/// <summary>
/// Base class for MCP tools with common functionality.
/// </summary>
public abstract class McpToolBase : IMcpTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }

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

    public abstract Task<McpToolResult> ExecuteAsync(JsonElement? arguments);

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
}
