using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class GeminiInitializerTests : IDisposable
{
    private readonly string _testDirectory;

    public GeminiInitializerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-gemini-test-{Guid.NewGuid():N}");
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
    public void EmbeddedResourceHelper_ReadSkill_ReturnsGeminiCalorSkill()
    {
        var content = EmbeddedResourceHelper.ReadSkill("gemini-calor-SKILL.md");

        Assert.NotEmpty(content);
        Assert.Contains("---", content); // YAML frontmatter
        Assert.Contains("name: calor", content);
        Assert.Contains("description:", content);
        Assert.Contains("Calor", content);
        Assert.Contains("§M", content);
    }

    [Fact]
    public void EmbeddedResourceHelper_ReadSkill_ReturnsGeminiConvertSkill()
    {
        var content = EmbeddedResourceHelper.ReadSkill("gemini-calor-convert-SKILL.md");

        Assert.NotEmpty(content);
        Assert.Contains("---", content); // YAML frontmatter
        Assert.Contains("name: calor-convert", content);
        Assert.Contains("description:", content);
        Assert.Contains("Type Mappings", content);
    }

    [Fact]
    public void EmbeddedResourceHelper_ReadTemplate_ReturnsGeminiMdTemplate()
    {
        var content = EmbeddedResourceHelper.ReadTemplate("GEMINI.md.template");

        Assert.NotEmpty(content);
        Assert.Contains("Calor-First Project", content);
        Assert.Contains("{{VERSION}}", content);
        Assert.Contains("@calor", content);
        Assert.Contains("@calor-convert", content);
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END CalorC SECTION -->", content);
    }

    [Fact]
    public void EmbeddedResourceHelper_ReadTemplate_ReturnsGeminiSettingsTemplate()
    {
        var content = EmbeddedResourceHelper.ReadTemplate("gemini-settings.json.template");

        Assert.NotEmpty(content);
        Assert.Contains("BeforeTool", content);
        Assert.Contains("write_file|replace", content);
        Assert.Contains("calor hook validate-write", content);
        Assert.Contains("--format gemini", content);
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_CreatesSkillsDirectories()
    {
        var initializer = new GeminiInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".gemini", "skills", "calor")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".gemini", "skills", "calor-convert")));
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_CreatesCalorSkill()
    {
        var initializer = new GeminiInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var skillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor", "SKILL.md");
        Assert.True(File.Exists(skillPath));
        Assert.Contains(skillPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(skillPath);
        Assert.Contains("name: calor", content);
        Assert.Contains("Calor", content);
        Assert.Contains("@calor", content); // Gemini uses @ prefix
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_CreatesConvertSkill()
    {
        var initializer = new GeminiInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var skillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor-convert", "SKILL.md");
        Assert.True(File.Exists(skillPath));
        Assert.Contains(skillPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(skillPath);
        Assert.Contains("name: calor-convert", content);
        Assert.Contains("@calor-convert", content); // Gemini uses @ prefix
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_CreatesGeminiMdWithMarkers()
    {
        var initializer = new GeminiInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var geminiMdPath = Path.Combine(_testDirectory, "GEMINI.md");
        Assert.True(File.Exists(geminiMdPath));
        Assert.Contains(geminiMdPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(geminiMdPath);
        Assert.Contains("Calor-First Project", content);
        Assert.DoesNotContain("{{VERSION}}", content); // Version should be replaced
        Assert.Matches(@"calor v\d+\.\d+\.\d+", content);
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("<!-- END CalorC SECTION -->", content);
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_CreatesSettingsWithHooks()
    {
        var initializer = new GeminiInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var settingsPath = Path.Combine(_testDirectory, ".gemini", "settings.json");
        Assert.True(File.Exists(settingsPath));
        Assert.Contains(settingsPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("BeforeTool", content);
        Assert.Contains("write_file|replace", content);
        Assert.Contains("calor hook validate-write", content);
        Assert.Contains("--format gemini", content);
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_SkipsExistingSkillFilesWithoutForce()
    {
        var initializer = new GeminiInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Modify a skill file
        var skillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor", "SKILL.md");
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
    public async Task GeminiInitializer_Initialize_OverwritesSkillsWithForce()
    {
        var initializer = new GeminiInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Modify a skill file
        var skillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor", "SKILL.md");
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
    public async Task GeminiInitializer_Initialize_AppendsCalorSectionWhenNoMarkers()
    {
        var initializer = new GeminiInitializer();
        var geminiMdPath = Path.Combine(_testDirectory, "GEMINI.md");

        // Create existing GEMINI.md without Calor section markers
        var existingContent = @"# Project Guidelines

Follow the coding standards.

## Build Instructions
Run `dotnet build`.
";
        await File.WriteAllTextAsync(geminiMdPath, existingContent);

        // Run init
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.Contains(geminiMdPath, result.UpdatedFiles);

        var content = await File.ReadAllTextAsync(geminiMdPath);

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
    public async Task GeminiInitializer_Initialize_ReplacesExistingCalorSection()
    {
        var initializer = new GeminiInitializer();
        var geminiMdPath = Path.Combine(_testDirectory, "GEMINI.md");

        // Create GEMINI.md with user content and an existing Calor section
        var existingContent = @"# My Project

Some user documentation here.

<!-- BEGIN CalorC SECTION - DO NOT EDIT -->
## Old Calor Section
This is old content that should be replaced.
<!-- END CalorC SECTION -->

## More User Content
This should be preserved.
";
        await File.WriteAllTextAsync(geminiMdPath, existingContent);

        // Run init
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.Contains(geminiMdPath, result.UpdatedFiles);

        var content = await File.ReadAllTextAsync(geminiMdPath);

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
    public async Task GeminiInitializer_Initialize_PreservesUserContentBeforeAndAfterSection()
    {
        var initializer = new GeminiInitializer();
        var geminiMdPath = Path.Combine(_testDirectory, "GEMINI.md");

        // First init creates the file with markers
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Add user content before and after the Calor section
        var content = await File.ReadAllTextAsync(geminiMdPath);
        var newContent = "# My Custom Header\n\nUser content before Calor.\n\n" + content + "\n## Footer\nUser content after Calor.\n";
        await File.WriteAllTextAsync(geminiMdPath, newContent);

        // Run init again
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);

        var finalContent = await File.ReadAllTextAsync(geminiMdPath);

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
    public async Task GeminiInitializer_Initialize_RunsMultipleTimesIdempotently()
    {
        var initializer = new GeminiInitializer();
        var geminiMdPath = Path.Combine(_testDirectory, "GEMINI.md");

        // First init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterFirst = await File.ReadAllTextAsync(geminiMdPath);

        // Second init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterSecond = await File.ReadAllTextAsync(geminiMdPath);

        // Third init
        await initializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterThird = await File.ReadAllTextAsync(geminiMdPath);

        // Content should remain the same across all runs
        Assert.Equal(contentAfterFirst, contentAfterSecond);
        Assert.Equal(contentAfterSecond, contentAfterThird);
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_ReturnsSuccessMessageWithHookInfo()
    {
        var initializer = new GeminiInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Messages);
        Assert.Contains(result.Messages, m => m.Contains("Google Gemini"));
        Assert.Contains(result.Messages, m => m.Contains("BeforeTool") || m.Contains("hooks"));
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_GeminiMdContainsMandatoryRules()
    {
        var initializer = new GeminiInitializer();

        await initializer.InitializeAsync(_testDirectory, force: false);

        var geminiMdPath = Path.Combine(_testDirectory, "GEMINI.md");
        var content = await File.ReadAllTextAsync(geminiMdPath);

        // Verify mandatory rules are present
        Assert.Contains("MANDATORY Rules", content);
        Assert.Contains("Never create new `.cs` files", content);
        Assert.Contains("Convert C# to Calor before modifying", content);
        Assert.Contains("Never edit generated files", content);
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_SkillsHaveYamlFrontmatter()
    {
        var initializer = new GeminiInitializer();

        await initializer.InitializeAsync(_testDirectory, force: false);

        var calorSkillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor", "SKILL.md");
        var convertSkillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor-convert", "SKILL.md");

        var calorContent = await File.ReadAllTextAsync(calorSkillPath);
        var convertContent = await File.ReadAllTextAsync(convertSkillPath);

        // Both skills should start with YAML frontmatter
        Assert.StartsWith("---", calorContent);
        Assert.StartsWith("---", convertContent);

        // YAML frontmatter should have required fields
        Assert.Contains("name:", calorContent);
        Assert.Contains("description:", calorContent);
        Assert.Contains("name:", convertContent);
        Assert.Contains("description:", convertContent);
    }

    [Fact]
    public void AiInitializerFactory_Create_ReturnsGeminiInitializer()
    {
        var initializer = AiInitializerFactory.Create("gemini");

        Assert.IsType<GeminiInitializer>(initializer);
        Assert.Equal("Google Gemini", initializer.AgentName);
    }

    [Fact]
    public void GeminiMdTemplate_ContainsSkillReferences()
    {
        var template = EmbeddedResourceHelper.ReadTemplate("GEMINI.md.template");

        // Should reference Gemini skill format (@skill)
        Assert.Contains("@calor", template);
        Assert.Contains("@calor-convert", template);
    }

    [Fact]
    public void GeminiMdTemplate_ContainsTypeMappings()
    {
        var template = EmbeddedResourceHelper.ReadTemplate("GEMINI.md.template");

        // Should contain type mapping table
        Assert.Contains("| C# | Calor |", template);
        Assert.Contains("i32", template);
        Assert.Contains("str", template);
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_SettingsNotOverwrittenIfHookExists()
    {
        var initializer = new GeminiInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Modify settings file but keep our hook command
        var settingsPath = Path.Combine(_testDirectory, ".gemini", "settings.json");
        var customSettings = @"{
  ""hooks"": {
    ""BeforeTool"": [
      {
        ""matcher"": ""write_file|replace"",
        ""hooks"": [
          {
            ""name"": ""calor-validate-write"",
            ""type"": ""command"",
            ""command"": ""calor hook validate-write --format gemini $TOOL_INPUT"",
            ""description"": ""Custom description""
          }
        ]
      }
    ]
  },
  ""customSetting"": true
}";
        await File.WriteAllTextAsync(settingsPath, customSettings);

        // Second initialization without force
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.True(result.Success);
        Assert.DoesNotContain(settingsPath, result.CreatedFiles);
        Assert.DoesNotContain(settingsPath, result.UpdatedFiles);

        // Settings should not be modified
        var content = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("customSetting", content);
    }

    [Fact]
    public async Task GeminiInitializer_Initialize_SettingsOverwrittenWithForce()
    {
        var initializer = new GeminiInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDirectory, force: false);

        // Modify settings file to remove our hook
        var settingsPath = Path.Combine(_testDirectory, ".gemini", "settings.json");
        await File.WriteAllTextAsync(settingsPath, @"{ ""customSetting"": true }");

        // Second initialization with force
        var result = await initializer.InitializeAsync(_testDirectory, force: true);

        Assert.True(result.Success);
        Assert.Contains(settingsPath, result.UpdatedFiles);

        // Settings should be overwritten with our hook
        var content = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("calor hook validate-write", content);
        Assert.DoesNotContain("customSetting", content);
    }
}
