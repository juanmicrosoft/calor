using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class CodexInitializerTests : IDisposable
{
    private readonly string _testDirectory;

    public CodexInitializerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-codex-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void EmbeddedResourceHelper_ReadTemplate_ReturnsAgentsMdTemplate()
    {
        var content = EmbeddedResourceHelper.ReadTemplate("AGENTS.md.template");

        Assert.NotEmpty(content);
        Assert.Contains("Calor-First Project", content);
        Assert.Contains("{{VERSION}}", content);
        Assert.Contains("calor_help", content);
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END CalorC SECTION -->", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_CreatesAgentsMdWithMarkers()
    {
        var initializer = new CodexInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");
        Assert.True(File.Exists(agentsMdPath));
        Assert.Contains(agentsMdPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(agentsMdPath);
        Assert.Contains("Calor-First Project", content);
        Assert.DoesNotContain("{{VERSION}}", content); // Version should be replaced
        Assert.Matches(@"calor v\d+\.\d+\.\d+", content);
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END CalorC SECTION -->", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_AgentsMdContainsGuidanceNote()
    {
        var initializer = new CodexInitializer();

        await initializer.InitializeAsync(_testDirectory, force: false);

        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");
        var content = await File.ReadAllTextAsync(agentsMdPath);

        // Should contain note about guidance-based enforcement
        Assert.Contains("guidance-based", content);
        Assert.Contains("Unlike Claude Code which uses hooks", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_AppendsCalorSectionWhenNoMarkers()
    {
        var initializer = new CodexInitializer();
        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");

        // Create existing AGENTS.md without Calor section markers
        var existingContent = @"# Project Guidelines

Follow the coding standards.

## Build Instructions
Run `dotnet build`.
";
        await File.WriteAllTextAsync(agentsMdPath, existingContent);

        // Run init
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.Contains(agentsMdPath, result.UpdatedFiles);

        var content = await File.ReadAllTextAsync(agentsMdPath);

        // Original content should be preserved
        Assert.Contains("# Project Guidelines", content);
        Assert.Contains("Follow the coding standards.", content);
        Assert.Contains("## Build Instructions", content);

        // Calor section should be appended
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END CalorC SECTION -->", content);
        Assert.Contains("## Calor-First Project", content);

        // Calor section should come after original content
        var userContentIndex = content.IndexOf("## Build Instructions");
        var calorSectionIndex = content.IndexOf("<!-- BEGIN CalorC SECTION");
        Assert.True(calorSectionIndex > userContentIndex);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_ReplacesExistingCalorSection()
    {
        var initializer = new CodexInitializer();
        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");

        // Create AGENTS.md with user content and an existing Calor section
        var existingContent = @"# My Project

Some user documentation here.

<!-- BEGIN CalorC SECTION - DO NOT EDIT -->
## Old Calor Section
This is old content that should be replaced.
<!-- END CalorC SECTION -->

## More User Content
This should be preserved.
";
        await File.WriteAllTextAsync(agentsMdPath, existingContent);

        // Run init
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.Contains(agentsMdPath, result.UpdatedFiles);

        var content = await File.ReadAllTextAsync(agentsMdPath);

        // User content should be preserved
        Assert.Contains("# My Project", content);
        Assert.Contains("Some user documentation here.", content);
        Assert.Contains("## More User Content", content);
        Assert.Contains("This should be preserved.", content);

        // Old Calor content should be replaced
        Assert.DoesNotContain("Old Calor Section", content);
        Assert.DoesNotContain("This is old content that should be replaced.", content);

        // New Calor content should be present
        Assert.Contains("## Calor-First Project", content);
        Assert.Matches(@"calor v\d+\.\d+\.\d+", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_PreservesUserContentBeforeAndAfterSection()
    {
        var initializer = new CodexInitializer();
        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");

        // First init creates the file with markers
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Add user content before and after the Calor section
        var content = await File.ReadAllTextAsync(agentsMdPath);
        var newContent = "# My Custom Header\n\nUser content before Calor.\n\n" + content + "\n## Footer\nUser content after Calor.\n";
        await File.WriteAllTextAsync(agentsMdPath, newContent);

        // Run init again
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);

        var finalContent = await File.ReadAllTextAsync(agentsMdPath);

        // User content should be preserved
        Assert.Contains("# My Custom Header", finalContent);
        Assert.Contains("User content before Calor.", finalContent);
        Assert.Contains("## Footer", finalContent);
        Assert.Contains("User content after Calor.", finalContent);

        // Calor section should still be present and valid
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", finalContent);
        Assert.Contains("<!-- END CalorC SECTION -->", finalContent);
        Assert.Contains("## Calor-First Project", finalContent);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_RunsMultipleTimesIdempotently()
    {
        var initializer = new CodexInitializer();
        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");

        // First init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterFirst = await File.ReadAllTextAsync(agentsMdPath);

        // Second init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterSecond = await File.ReadAllTextAsync(agentsMdPath);

        // Third init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterThird = await File.ReadAllTextAsync(agentsMdPath);

        // Content should remain the same across all runs
        Assert.Equal(contentAfterFirst, contentAfterSecond);
        Assert.Equal(contentAfterSecond, contentAfterThird);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_ReturnsSuccessMessage()
    {
        var initializer = new CodexInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Messages);
        Assert.Contains(result.Messages, m => m.Contains("OpenAI Codex"));
        Assert.Contains(result.Messages, m => m.Contains("MCP server"));
    }

    [Fact]
    public async Task CodexInitializer_Initialize_AgentsMdContainsMandatoryRules()
    {
        var initializer = new CodexInitializer();

        await initializer.InitializeAsync(_testDirectory, force: false);

        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");
        var content = await File.ReadAllTextAsync(agentsMdPath);

        // Verify mandatory rules are present
        Assert.Contains("MANDATORY Rules", content);
        Assert.Contains("Never create new `.cs` files", content);
        Assert.Contains("Convert C# to Calor before modifying", content);
        Assert.Contains("Never edit generated files", content);
    }

    [Fact]
    public void AiInitializerFactory_Create_ReturnsCodexInitializer()
    {
        var initializer = AiInitializerFactory.Create("codex");

        Assert.IsType<CodexInitializer>(initializer);
        Assert.Equal("OpenAI Codex", initializer.AgentName);
    }

    [Fact]
    public void AgentsMdTemplate_ContainsMcpToolReferences()
    {
        var template = EmbeddedResourceHelper.ReadTemplate("AGENTS.md.template");

        // Should reference MCP tools
        Assert.Contains("calor_help", template);
        Assert.Contains("calor_compile", template);
    }

    [Fact]
    public void AgentsMdTemplate_ContainsTypeMappings()
    {
        var template = EmbeddedResourceHelper.ReadTemplate("AGENTS.md.template");

        // Should contain type mapping table
        Assert.Contains("| C# | Calor |", template);
        Assert.Contains("i32", template);
        Assert.Contains("str", template);
    }
}
