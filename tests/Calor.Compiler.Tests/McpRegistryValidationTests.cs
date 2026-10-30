using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using Calor.Compiler.Mcp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Validates that CLAUDE.md.template, AGENTS.md.template, GEMINI.md.template,
/// and server instructions only reference MCP tools and resources that actually exist.
/// Prevents tool-name drift when tools are renamed or consolidated.
/// </summary>
public class McpRegistryValidationTests
{
    private static readonly HashSet<string> RegisteredTools = GetRegisteredToolNames();
    private static readonly HashSet<string> RegisteredResources = GetRegisteredResourceUris();

    [Fact]
    public void ClaudeMdTemplate_ReferencesOnlyRegisteredTools()
    {
        var template = LoadEmbeddedTemplate("CLAUDE.md.template");
        AssertNoPhantomTools(template, "CLAUDE.md.template");
    }

    [Fact]
    public void AgentsMdTemplate_ReferencesOnlyRegisteredTools()
    {
        var template = LoadEmbeddedTemplate("AGENTS.md.template");
        AssertNoPhantomTools(template, "AGENTS.md.template");
    }

    [Fact]
    public void GeminiMdTemplate_ReferencesOnlyRegisteredTools()
    {
        var template = LoadEmbeddedTemplate("GEMINI.md.template");
        AssertNoPhantomTools(template, "GEMINI.md.template");
    }

    [Theory]
    [InlineData("CLAUDE.md.template")]
    [InlineData("AGENTS.md.template")]
    [InlineData("GEMINI.md.template")]
    public void Templates_ReferenceOnlyRegisteredResources(string templateName)
    {
        var template = LoadEmbeddedTemplate(templateName);
        AssertNoPhantomResources(template, templateName);
    }

    [Fact]
    public void ServerInstructions_ReferenceOnlyRegisteredTools()
    {
        // Construct a real handler to get the live server instructions
        // The handler registers tools in its constructor — this tests the real path
        var handler = new Calor.Compiler.Mcp.McpMessageHandler(verbose: false);
        var instructions = handler.GetServerInstructionsForTest();
        AssertNoPhantomTools(instructions, "GetServerInstructions()");
    }

    [Theory]
    [InlineData("calor://reference", "current-language-reference.md")]
    [InlineData("calor://syntax-reference", "calor-syntax-documentation.json")]
    [InlineData("calor://type-mappings", "type-mappings.md")]
    [InlineData("calor://effects", "effects.json")]
    [InlineData("calor://tags", "tags.json")]
    [InlineData("calor://id-prefixes", "id-prefixes.json")]
    [InlineData("calor://workflows", "workflows.json")]
    public async Task TeachingResources_ServePackagedGeneratedOutput(string uri, string resource)
    {
        var handler = new McpMessageHandler();
        var response = await handler.HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonSerializer.SerializeToElement(1),
            Method = "resources/read",
            Params = JsonSerializer.SerializeToElement(new { uri })
        });
        Assert.NotNull(response);
        Assert.Null(response.Error);
        var result = JsonSerializer.SerializeToElement(response.Result, McpJsonOptions.Default);
        var content = Assert.Single(result.GetProperty("contents").EnumerateArray());
        Assert.Equal(uri, content.GetProperty("uri").GetString());
        Assert.Equal(Init.EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources." + resource),
            content.GetProperty("text").GetString());
    }

    [Fact]
    public void TeachingTagCatalog_RetainsDelimiterContractWithoutSourceIds()
    {
        using var catalog = JsonDocument.Parse(McpMessageHandler.GetTagCatalogJsonPublic());
        foreach (var tag in catalog.RootElement.GetProperty("tags").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(tag.GetProperty("open").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(tag.GetProperty("close").GetString()));
            Assert.False(tag.TryGetProperty("idPrefix", out _));
        }
        var call = catalog.RootElement.GetProperty("tags").EnumerateArray()
            .Single(tag => tag.GetProperty("tag").GetString() == "§C");
        Assert.Equal("§/C", call.GetProperty("close").GetString());
        using var identity = JsonDocument.Parse(McpMessageHandler.GetIdPrefixCatalogJsonPublic());
        Assert.Empty(identity.RootElement.GetProperty("prefixes").EnumerateArray());
    }

    private static void AssertNoPhantomTools(string content, string source)
    {
        var toolRefs = Regex.Matches(content, @"calor_[a-z_]+")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        var phantoms = toolRefs.Where(t => !RegisteredTools.Contains(t)).ToList();

        Assert.True(phantoms.Count == 0,
            $"{source} references {phantoms.Count} phantom tool(s): {string.Join(", ", phantoms)}. " +
            $"Registered tools: {string.Join(", ", RegisteredTools.OrderBy(t => t))}");
    }

    private static void AssertNoPhantomResources(string content, string source)
    {
        var uriRefs = Regex.Matches(content, @"calor://[a-z\-]+")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        var phantoms = uriRefs.Where(u => !RegisteredResources.Contains(u)).ToList();

        Assert.True(phantoms.Count == 0,
            $"{source} references {phantoms.Count} phantom resource(s): {string.Join(", ", phantoms)}. " +
            $"Registered resources: {string.Join(", ", RegisteredResources.OrderBy(u => u))}");
    }

    private static string LoadEmbeddedTemplate(string templateName)
    {
        var assembly = typeof(Program).Assembly;
        var resourceName = $"Calor.Compiler.Resources.Templates.{templateName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets tool names by constructing a real McpMessageHandler (which registers all tools
    /// in its constructor) and extracting the names. Tests the live registry, not a parallel list.
    /// </summary>
    private static HashSet<string> GetRegisteredToolNames()
    {
        var handler = new Calor.Compiler.Mcp.McpMessageHandler(verbose: false);
        return handler.GetRegisteredToolNamesForTest();
    }

    private static HashSet<string> GetRegisteredResourceUris()
    {
        var handler = new Calor.Compiler.Mcp.McpMessageHandler(verbose: false);
        return handler.GetRegisteredResourceUrisForTest();
    }
}
