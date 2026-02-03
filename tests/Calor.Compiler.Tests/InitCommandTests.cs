using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class InitCommandTests : IDisposable
{
    private readonly string _testDirectory;

    public InitCommandTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-init-test-{Guid.NewGuid():N}");
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
    public void EmbeddedResourceHelper_ReadSkill_ReturnsCalorSkill()
    {
        var content = EmbeddedResourceHelper.ReadSkill("calor.md");

        Assert.NotEmpty(content);
        Assert.Contains("Calor", content);
        Assert.Contains("§M", content);
    }

    [Fact]
    public void EmbeddedResourceHelper_ReadSkill_ReturnsConvertSkill()
    {
        var content = EmbeddedResourceHelper.ReadSkill("calor-convert.md");

        Assert.NotEmpty(content);
        Assert.Contains("Convert", content);
        Assert.Contains("Type Mappings", content);
    }

    [Fact]
    public void EmbeddedResourceHelper_ReadTemplate_ReturnsClaudeMdTemplate()
    {
        var content = EmbeddedResourceHelper.ReadTemplate("CLAUDE.md.template");

        Assert.NotEmpty(content);
        Assert.Contains("Calor-First Project", content);
        Assert.Contains("{{VERSION}}", content);
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END CalorC SECTION -->", content);
    }

    [Fact]
    public void EmbeddedResourceHelper_GetVersion_ReturnsVersionString()
    {
        var version = EmbeddedResourceHelper.GetVersion();

        Assert.NotEmpty(version);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public void AiInitializerFactory_SupportedAgents_ContainsExpectedTypes()
    {
        Assert.Contains("claude", AiInitializerFactory.SupportedAgents);
        Assert.Contains("codex", AiInitializerFactory.SupportedAgents);
        Assert.Contains("gemini", AiInitializerFactory.SupportedAgents);
        Assert.Contains("github", AiInitializerFactory.SupportedAgents);
    }

    [Fact]
    public void AiInitializerFactory_Create_ReturnsClaudeInitializer()
    {
        var initializer = AiInitializerFactory.Create("claude");

        Assert.IsType<ClaudeInitializer>(initializer);
        Assert.Equal("Claude Code", initializer.AgentName);
    }

    [Fact]
    public void AiInitializerFactory_Create_IsCaseInsensitive()
    {
        var lower = AiInitializerFactory.Create("claude");
        var upper = AiInitializerFactory.Create("CLAUDE");
        var mixed = AiInitializerFactory.Create("Claude");

        Assert.IsType<ClaudeInitializer>(lower);
        Assert.IsType<ClaudeInitializer>(upper);
        Assert.IsType<ClaudeInitializer>(mixed);
    }

    [Fact]
    public void AiInitializerFactory_Create_ThrowsForUnknownType()
    {
        var ex = Assert.Throws<ArgumentException>(() => AiInitializerFactory.Create("unknown"));

        Assert.Contains("Unknown AI agent type", ex.Message);
        Assert.Contains("unknown", ex.Message);
    }

    [Fact]
    public void AiInitializerFactory_IsSupported_ReturnsTrueForValidTypes()
    {
        Assert.True(AiInitializerFactory.IsSupported("claude"));
        Assert.True(AiInitializerFactory.IsSupported("CLAUDE"));
        Assert.True(AiInitializerFactory.IsSupported("codex"));
    }

    [Fact]
    public void AiInitializerFactory_IsSupported_ReturnsFalseForInvalidTypes()
    {
        Assert.False(AiInitializerFactory.IsSupported("unknown"));
        Assert.False(AiInitializerFactory.IsSupported(""));
    }

    [Fact]
    public async Task ClaudeInitializer_Initialize_CreatesSkillsDirectories()
    {
        var initializer = new ClaudeInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor-convert")));
    }

    [Fact]
    public async Task ClaudeInitializer_Initialize_CreatesCalorSkill()
    {
        var initializer = new ClaudeInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var skillPath = Path.Combine(_testDirectory, ".claude", "skills", "calor", "SKILL.md");
        Assert.True(File.Exists(skillPath));
        Assert.Contains(skillPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(skillPath);
        Assert.Contains("Calor", content);
        Assert.Contains("name: calor", content); // YAML frontmatter
    }

    [Fact]
    public async Task ClaudeInitializer_Initialize_CreatesConvertSkill()
    {
        var initializer = new ClaudeInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var skillPath = Path.Combine(_testDirectory, ".claude", "skills", "calor-convert", "SKILL.md");
        Assert.True(File.Exists(skillPath));
        Assert.Contains(skillPath, result.CreatedFiles);
    }

    [Fact]
    public async Task ClaudeInitializer_Initialize_CreatesClaudeMdWithMarkers()
    {
        var initializer = new ClaudeInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");
        Assert.True(File.Exists(claudeMdPath));
        Assert.Contains(claudeMdPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(claudeMdPath);
        Assert.Contains("Calor-First Project", content);
        Assert.DoesNotContain("{{VERSION}}", content); // Version should be replaced
        Assert.Matches(@"calor v\d+\.\d+\.\d+", content);
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END CalorC SECTION -->", content);
    }

    [Fact]
    public async Task ClaudeInitializer_Initialize_SkipsExistingSkillFilesWithoutForce()
    {
        var initializer = new ClaudeInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Modify a skill file
        var skillPath = Path.Combine(_testDirectory, ".claude", "skills", "calor", "SKILL.md");
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
    public async Task ClaudeInitializer_Initialize_OverwritesSkillsWithForce()
    {
        var initializer = new ClaudeInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Modify a skill file
        var skillPath = Path.Combine(_testDirectory, ".claude", "skills", "calor", "SKILL.md");
        await File.WriteAllTextAsync(skillPath, "Custom skill content");

        // Second initialization with force
        var result = await initializer.InitializeAsync(_testDirectory, force: true);

        Assert.True(result.Success);
        Assert.Contains(skillPath, result.CreatedFiles);

        // Skill file should be overwritten
        var content = await File.ReadAllTextAsync(skillPath);
        Assert.Contains("Calor", content);
        Assert.DoesNotContain("Custom skill content", content);
    }

    [Fact]
    public async Task ClaudeInitializer_Initialize_ReplacesExistingCalorSection()
    {
        var initializer = new ClaudeInitializer();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");

        // Create CLAUDE.md with user content and an existing Calor section
        var existingContent = @"# My Project

Some user documentation here.

<!-- BEGIN CalorC SECTION - DO NOT EDIT -->
## Old Calor Section
This is old content that should be replaced.
<!-- END CalorC SECTION -->

## More User Content
This should be preserved.
";
        await File.WriteAllTextAsync(claudeMdPath, existingContent);

        // Run init
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.Contains(claudeMdPath, result.UpdatedFiles);

        var content = await File.ReadAllTextAsync(claudeMdPath);

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
    public async Task ClaudeInitializer_Initialize_AppendsCalorSectionWhenNoMarkers()
    {
        var initializer = new ClaudeInitializer();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");

        // Create CLAUDE.md without Calor section markers
        var existingContent = @"# My Project

This is my custom documentation.

## Build Instructions
Run `dotnet build` to compile.
";
        await File.WriteAllTextAsync(claudeMdPath, existingContent);

        // Run init
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.Contains(claudeMdPath, result.UpdatedFiles);

        var content = await File.ReadAllTextAsync(claudeMdPath);

        // Original content should be preserved
        Assert.Contains("# My Project", content);
        Assert.Contains("This is my custom documentation.", content);
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
    public async Task ClaudeInitializer_Initialize_PreservesUserContentBeforeAndAfterSection()
    {
        var initializer = new ClaudeInitializer();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");

        // First init creates the file with markers
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Add user content before and after the Calor section
        var content = await File.ReadAllTextAsync(claudeMdPath);
        var newContent = "# My Custom Header\n\nUser content before Calor.\n\n" + content + "\n## Footer\nUser content after Calor.\n";
        await File.WriteAllTextAsync(claudeMdPath, newContent);

        // Run init again
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);

        var finalContent = await File.ReadAllTextAsync(claudeMdPath);

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
    public async Task ClaudeInitializer_Initialize_RunsMultipleTimesIdempotently()
    {
        var initializer = new ClaudeInitializer();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");

        // First init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterFirst = await File.ReadAllTextAsync(claudeMdPath);

        // Second init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterSecond = await File.ReadAllTextAsync(claudeMdPath);

        // Third init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterThird = await File.ReadAllTextAsync(claudeMdPath);

        // Content should remain the same across all runs
        Assert.Equal(contentAfterFirst, contentAfterSecond);
        Assert.Equal(contentAfterSecond, contentAfterThird);
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_ReturnsSuccess()
    {
        var initializer = new GeminiInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("Google Gemini"));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".gemini", "skills", "calor")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, "GEMINI.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".gemini", "settings.json")));
    }

    [Fact]
    public async Task GitHubCopilotInitializer_Initialize_CreatesExpectedFiles()
    {
        var initializer = new GitHubCopilotInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.Contains("GitHub Copilot", result.Messages[0]);

        // Verify skill files are created
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor-convert", "SKILL.md")));

        // Verify copilot-instructions.md is created
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".github", "copilot-instructions.md")));

        // Verify guidance-based enforcement note is present
        Assert.Contains("guidance-based only", string.Join("\n", result.Messages));
    }

    [Fact]
    public async Task GitHubCopilotInitializer_Initialize_CopilotInstructionsContainsCalorSection()
    {
        var initializer = new GitHubCopilotInitializer();

        await initializer.InitializeAsync(_testDirectory, force: false);

        var instructionsPath = Path.Combine(_testDirectory, ".github", "copilot-instructions.md");
        var content = await File.ReadAllTextAsync(instructionsPath);

        // Verify Calor-first section markers
        Assert.Contains("BEGIN CalorC SECTION", content);
        Assert.Contains("END CalorC SECTION", content);

        // Verify Calor-first mandatory rules are in the generated file
        Assert.Contains("Calor-First Project", content);
        Assert.Contains("MANDATORY Rules for AI Agents", content);
        Assert.Contains("Rule 1: Never create new `.cs` files", content);
        Assert.Contains("Rule 2: Convert C# to Calor before modifying", content);
        Assert.Contains("calor analyze", content);
        Assert.Contains("guidance-based only", content);
    }

    [Fact]
    public async Task GitHubCopilotInitializer_Initialize_WithExistingInstructions_AppendsSection()
    {
        var initializer = new GitHubCopilotInitializer();

        // Create existing copilot-instructions.md with custom content
        var githubDir = Path.Combine(_testDirectory, ".github");
        Directory.CreateDirectory(githubDir);
        var instructionsPath = Path.Combine(githubDir, "copilot-instructions.md");
        var existingContent = "# My Project Instructions\n\nThis is my project.\n";
        await File.WriteAllTextAsync(instructionsPath, existingContent);

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);

        var content = await File.ReadAllTextAsync(instructionsPath);

        // Verify original content is preserved
        Assert.Contains("# My Project Instructions", content);
        Assert.Contains("This is my project.", content);

        // Verify Calor section is appended
        Assert.Contains("BEGIN CalorC SECTION", content);
        Assert.Contains("Calor-First Project", content);
    }

    [Fact]
    public void ClaudeMdTemplate_ContainsAiCodingGuidelines()
    {
        var template = EmbeddedResourceHelper.ReadTemplate("CLAUDE.md.template");

        // Verify the Calor-first mandatory rules section exists
        Assert.Contains("Calor-First Project", template);
        Assert.Contains("MANDATORY Rules for AI Agents", template);
        Assert.Contains("Rule 1: Never create new `.cs` files", template);
        Assert.Contains("All new code MUST be written in Calor", template);
        Assert.Contains("Rule 2: Convert C# to Calor before modifying", template);
        Assert.Contains("Rule 3: Never edit generated files", template);
        Assert.Contains("calor analyze", template);
    }

    [Fact]
    public async Task ClaudeInitializer_Initialize_ClaudeMdContainsAiGuidelines()
    {
        var initializer = new ClaudeInitializer();

        await initializer.InitializeAsync(_testDirectory, force: false);

        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");
        var content = await File.ReadAllTextAsync(claudeMdPath);

        // Verify Calor-first mandatory rules are in the generated file
        Assert.Contains("Calor-First Project", content);
        Assert.Contains("MANDATORY Rules for AI Agents", content);
        Assert.Contains("Rule 1: Never create new `.cs` files", content);
        Assert.Contains("Rule 2: Convert C# to Calor before modifying", content);
        Assert.Contains("calor analyze", content);
    }
}
