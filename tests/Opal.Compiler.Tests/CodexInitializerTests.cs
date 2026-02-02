using Opal.Compiler.Init;
using Xunit;

namespace Opal.Compiler.Tests;

public class CodexInitializerTests : IDisposable
{
    private readonly string _testDirectory;

    public CodexInitializerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"opal-codex-test-{Guid.NewGuid():N}");
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
    public void EmbeddedResourceHelper_ReadSkill_ReturnsCodexOpalSkill()
    {
        var content = EmbeddedResourceHelper.ReadSkill("codex-opal-SKILL.md");

        Assert.NotEmpty(content);
        Assert.Contains("---", content); // YAML frontmatter
        Assert.Contains("name: opal", content);
        Assert.Contains("description:", content);
        Assert.Contains("OPAL", content);
        Assert.Contains("§M", content);
    }

    [Fact]
    public void EmbeddedResourceHelper_ReadSkill_ReturnsCodexConvertSkill()
    {
        var content = EmbeddedResourceHelper.ReadSkill("codex-opal-convert-SKILL.md");

        Assert.NotEmpty(content);
        Assert.Contains("---", content); // YAML frontmatter
        Assert.Contains("name: opal-convert", content);
        Assert.Contains("description:", content);
        Assert.Contains("Type Mappings", content);
    }

    [Fact]
    public void EmbeddedResourceHelper_ReadTemplate_ReturnsAgentsMdTemplate()
    {
        var content = EmbeddedResourceHelper.ReadTemplate("AGENTS.md.template");

        Assert.NotEmpty(content);
        Assert.Contains("OPAL-First Project", content);
        Assert.Contains("{{VERSION}}", content);
        Assert.Contains("$opal", content);
        Assert.Contains("$opal-convert", content);
        Assert.Contains("<!-- BEGIN OPALC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END OPALC SECTION -->", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_CreatesSkillsDirectories()
    {
        var initializer = new CodexInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".codex", "skills", "opal")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".codex", "skills", "opal-convert")));
    }

    [Fact]
    public async Task CodexInitializer_Initialize_CreatesOpalSkill()
    {
        var initializer = new CodexInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var skillPath = Path.Combine(_testDirectory, ".codex", "skills", "opal", "SKILL.md");
        Assert.True(File.Exists(skillPath));
        Assert.Contains(skillPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(skillPath);
        Assert.Contains("name: opal", content);
        Assert.Contains("OPAL", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_CreatesConvertSkill()
    {
        var initializer = new CodexInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var skillPath = Path.Combine(_testDirectory, ".codex", "skills", "opal-convert", "SKILL.md");
        Assert.True(File.Exists(skillPath));
        Assert.Contains(skillPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(skillPath);
        Assert.Contains("name: opal-convert", content);
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
        Assert.Contains("OPAL-First Project", content);
        Assert.DoesNotContain("{{VERSION}}", content); // Version should be replaced
        Assert.Matches(@"opalc v\d+\.\d+\.\d+", content);
        Assert.Contains("<!-- BEGIN OPALC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END OPALC SECTION -->", content);
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
    public async Task CodexInitializer_Initialize_SkipsExistingSkillFilesWithoutForce()
    {
        var initializer = new CodexInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Modify a skill file
        var skillPath = Path.Combine(_testDirectory, ".codex", "skills", "opal", "SKILL.md");
        await File.WriteAllTextAsync(skillPath, "Custom skill content");

        // Second initialization without force
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains(skillPath));

        // Skill file should not be overwritten
        var content = await File.ReadAllTextAsync(skillPath);
        Assert.Equal("Custom skill content", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_OverwritesSkillsWithForce()
    {
        var initializer = new CodexInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Modify a skill file
        var skillPath = Path.Combine(_testDirectory, ".codex", "skills", "opal", "SKILL.md");
        await File.WriteAllTextAsync(skillPath, "Custom skill content");

        // Second initialization with force
        var result = await initializer.InitializeAsync(_testDirectory, force: true);

        Assert.True(result.Success);
        Assert.Contains(skillPath, result.CreatedFiles);

        // Skill file should be overwritten
        var content = await File.ReadAllTextAsync(skillPath);
        Assert.Contains("OPAL", content);
        Assert.DoesNotContain("Custom skill content", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_AppendsOpalSectionWhenNoMarkers()
    {
        var initializer = new CodexInitializer();
        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");

        // Create existing AGENTS.md without OPAL section markers
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

        // OPAL section should be appended
        Assert.Contains("<!-- BEGIN OPALC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END OPALC SECTION -->", content);
        Assert.Contains("## OPAL-First Project", content);

        // OPAL section should come after original content
        var userContentIndex = content.IndexOf("## Build Instructions");
        var opalSectionIndex = content.IndexOf("<!-- BEGIN OPALC SECTION");
        Assert.True(opalSectionIndex > userContentIndex);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_ReplacesExistingOpalSection()
    {
        var initializer = new CodexInitializer();
        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");

        // Create AGENTS.md with user content and an existing OPAL section
        var existingContent = @"# My Project

Some user documentation here.

<!-- BEGIN OPALC SECTION - DO NOT EDIT -->
## Old OPAL Section
This is old content that should be replaced.
<!-- END OPALC SECTION -->

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

        // Old OPAL content should be replaced
        Assert.DoesNotContain("Old OPAL Section", content);
        Assert.DoesNotContain("This is old content that should be replaced.", content);

        // New OPAL content should be present
        Assert.Contains("## OPAL-First Project", content);
        Assert.Matches(@"opalc v\d+\.\d+\.\d+", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_PreservesUserContentBeforeAndAfterSection()
    {
        var initializer = new CodexInitializer();
        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");

        // First init creates the file with markers
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Add user content before and after the OPAL section
        var content = await File.ReadAllTextAsync(agentsMdPath);
        var newContent = "# My Custom Header\n\nUser content before OPAL.\n\n" + content + "\n## Footer\nUser content after OPAL.\n";
        await File.WriteAllTextAsync(agentsMdPath, newContent);

        // Run init again
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);

        var finalContent = await File.ReadAllTextAsync(agentsMdPath);

        // User content should be preserved
        Assert.Contains("# My Custom Header", finalContent);
        Assert.Contains("User content before OPAL.", finalContent);
        Assert.Contains("## Footer", finalContent);
        Assert.Contains("User content after OPAL.", finalContent);

        // OPAL section should still be present and valid
        Assert.Contains("<!-- BEGIN OPALC SECTION - DO NOT EDIT -->", finalContent);
        Assert.Contains("<!-- END OPALC SECTION -->", finalContent);
        Assert.Contains("## OPAL-First Project", finalContent);
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
        Assert.Contains(result.Messages, m => m.Contains("guidance-based"));
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
        Assert.Contains("Convert C# to OPAL before modifying", content);
        Assert.Contains("Never edit generated files", content);
    }

    [Fact]
    public async Task CodexInitializer_Initialize_SkillsHaveYamlFrontmatter()
    {
        var initializer = new CodexInitializer();

        await initializer.InitializeAsync(_testDirectory, force: false);

        var opalSkillPath = Path.Combine(_testDirectory, ".codex", "skills", "opal", "SKILL.md");
        var convertSkillPath = Path.Combine(_testDirectory, ".codex", "skills", "opal-convert", "SKILL.md");

        var opalContent = await File.ReadAllTextAsync(opalSkillPath);
        var convertContent = await File.ReadAllTextAsync(convertSkillPath);

        // Both skills should start with YAML frontmatter
        Assert.StartsWith("---", opalContent);
        Assert.StartsWith("---", convertContent);

        // YAML frontmatter should have required fields
        Assert.Contains("name:", opalContent);
        Assert.Contains("description:", opalContent);
        Assert.Contains("name:", convertContent);
        Assert.Contains("description:", convertContent);
    }

    [Fact]
    public void AiInitializerFactory_Create_ReturnsCodexInitializer()
    {
        var initializer = AiInitializerFactory.Create("codex");

        Assert.IsType<CodexInitializer>(initializer);
        Assert.Equal("OpenAI Codex", initializer.AgentName);
    }

    [Fact]
    public void AgentsMdTemplate_ContainsSkillReferences()
    {
        var template = EmbeddedResourceHelper.ReadTemplate("AGENTS.md.template");

        // Should reference Codex skill format ($skill)
        Assert.Contains("$opal", template);
        Assert.Contains("$opal-convert", template);
    }

    [Fact]
    public void AgentsMdTemplate_ContainsTypeMappings()
    {
        var template = EmbeddedResourceHelper.ReadTemplate("AGENTS.md.template");

        // Should contain type mapping table
        Assert.Contains("| C# | OPAL |", template);
        Assert.Contains("i32", template);
        Assert.Contains("str", template);
    }
}
