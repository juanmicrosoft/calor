namespace Calor.Compiler.Mcp;

/// <summary>
/// Exposes MCP resource content for validation by the benchmark harness.
/// </summary>
public static class McpResourceValidator
{
    public static string GetEffectCatalog() => McpMessageHandler.GetEffectCatalogJsonPublic();
    public static string GetTagCatalog() => McpMessageHandler.GetTagCatalogJsonPublic();
    public static string GetIdPrefixCatalog() => McpMessageHandler.GetIdPrefixCatalogJsonPublic();
    public static string GetWorkflows() => McpMessageHandler.GetWorkflowsJsonPublic();
}
