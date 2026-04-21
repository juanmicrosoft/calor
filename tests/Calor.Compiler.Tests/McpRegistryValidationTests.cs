using System.Reflection;
using System.Text.RegularExpressions;
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
