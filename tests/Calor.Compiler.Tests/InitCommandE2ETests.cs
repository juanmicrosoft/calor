using System.Text.Json;
using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Comprehensive E2E tests that verify `calor init` creates all expected files
/// and configurations for all supported AI coding agents.
/// </summary>
public class InitCommandE2ETests : IDisposable
{
    private readonly string _testDirectory;

    public InitCommandE2ETests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-init-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private async Task CreateTestCsproj(string name = "TestApp")
    {
        var csprojContent = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{name}.csproj"), csprojContent);
    }

    #region Claude E2E Tests

    [Fact]
    public async Task ClaudeInit_CreatesAllExpectedFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        var claudeInitializer = new ClaudeInitializer();

        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        await csprojInitializer.InitializeAsync(csprojPath);
        await GitAttributesInitializer.InitializeAsync(_testDirectory);

        // Assert - All skill directories exist
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor-convert")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor-semantics")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor-analyze")));

        // Assert - All skill files exist and have content
        var calorSkillPath = Path.Combine(_testDirectory, ".claude", "skills", "calor", "SKILL.md");
        var convertSkillPath = Path.Combine(_testDirectory, ".claude", "skills", "calor-convert", "SKILL.md");
        var semanticsSkillPath = Path.Combine(_testDirectory, ".claude", "skills", "calor-semantics", "SKILL.md");
        var analyzeSkillPath = Path.Combine(_testDirectory, ".claude", "skills", "calor-analyze", "SKILL.md");

        Assert.True(File.Exists(calorSkillPath));
        Assert.True(File.Exists(convertSkillPath));
        Assert.True(File.Exists(semanticsSkillPath));
        Assert.True(File.Exists(analyzeSkillPath));

        Assert.True((await File.ReadAllTextAsync(calorSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(convertSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(semanticsSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(analyzeSkillPath)).Length > 100);

        // Assert - CLAUDE.md exists with correct content
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");
        Assert.True(File.Exists(claudeMdPath));
        var claudeMdContent = await File.ReadAllTextAsync(claudeMdPath);
        Assert.Contains("<!-- BEGIN CalorC SECTION", claudeMdContent);
        Assert.Contains("<!-- END CalorC SECTION -->", claudeMdContent);
        Assert.Contains("Calor-First Project", claudeMdContent);
        Assert.Matches(@"calor v\d+\.\d+\.\d+", claudeMdContent);

        // Assert - settings.json exists with hooks and MCP servers
        var settingsPath = Path.Combine(_testDirectory, ".claude", "settings.json");
        Assert.True(File.Exists(settingsPath));
        var settingsContent = await File.ReadAllTextAsync(settingsPath);

        // Verify hooks
        Assert.Contains("PreToolUse", settingsContent);
        Assert.Contains("PostToolUse", settingsContent);
        Assert.Contains("calor hook validate-write", settingsContent);
        Assert.Contains("calor hook validate-edit", settingsContent);
        Assert.Contains("calor hook validate-calr-content", settingsContent);
        Assert.Contains("calor hook validate-ids", settingsContent);
        Assert.Contains("calor hook post-write-lint", settingsContent);

        // Verify MCP servers - both LSP and MCP
        Assert.Contains("mcpServers", settingsContent);
        Assert.Contains("calor-lsp", settingsContent);
        Assert.Contains("\"calor\":", settingsContent); // The MCP server entry

        // Parse JSON to verify structure
        var settingsJson = JsonDocument.Parse(settingsContent);
        var mcpServers = settingsJson.RootElement.GetProperty("mcpServers");

        Assert.True(mcpServers.TryGetProperty("calor-lsp", out var lspServer));
        Assert.Equal("calor", lspServer.GetProperty("command").GetString());
        Assert.Contains("lsp", lspServer.GetProperty("args").EnumerateArray().Select(a => a.GetString()));

        Assert.True(mcpServers.TryGetProperty("calor", out var mcpServer));
        Assert.Equal("calor", mcpServer.GetProperty("command").GetString());
        var mcpArgs = mcpServer.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToList();
        Assert.Contains("mcp", mcpArgs);
        Assert.Contains("--stdio", mcpArgs);

        // Assert - .csproj has Calor targets
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", csprojContent);
        Assert.Contains("CalorOutputDirectory", csprojContent);
        Assert.Contains("IncludeCalorGeneratedFiles", csprojContent);

        // Assert - .gitattributes exists
        var gitAttributesPath = Path.Combine(_testDirectory, ".gitattributes");
        Assert.True(File.Exists(gitAttributesPath));
        var gitAttributesContent = await File.ReadAllTextAsync(gitAttributesPath);
        Assert.Contains("*.calr", gitAttributesContent);
    }

    #endregion

    #region Codex E2E Tests

    [Fact]
    public async Task CodexInit_CreatesAllExpectedFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        var codexInitializer = new CodexInitializer();
        await codexInitializer.InitializeAsync(_testDirectory, force: false);
        await csprojInitializer.InitializeAsync(csprojPath);
        await GitAttributesInitializer.InitializeAsync(_testDirectory);

        // Assert - All skill directories exist
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".codex", "skills", "calor")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".codex", "skills", "calor-convert")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".codex", "skills", "calor-analyze")));

        // Assert - All skill files exist and have content
        var calorSkillPath = Path.Combine(_testDirectory, ".codex", "skills", "calor", "SKILL.md");
        var convertSkillPath = Path.Combine(_testDirectory, ".codex", "skills", "calor-convert", "SKILL.md");
        var analyzeSkillPath = Path.Combine(_testDirectory, ".codex", "skills", "calor-analyze", "SKILL.md");

        Assert.True(File.Exists(calorSkillPath));
        Assert.True(File.Exists(convertSkillPath));
        Assert.True(File.Exists(analyzeSkillPath));

        Assert.True((await File.ReadAllTextAsync(calorSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(convertSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(analyzeSkillPath)).Length > 100);

        // Assert - AGENTS.md exists with correct content
        var agentsMdPath = Path.Combine(_testDirectory, "AGENTS.md");
        Assert.True(File.Exists(agentsMdPath));
        var agentsMdContent = await File.ReadAllTextAsync(agentsMdPath);
        Assert.Contains("<!-- BEGIN CalorC SECTION", agentsMdContent);
        Assert.Contains("<!-- END CalorC SECTION -->", agentsMdContent);
        Assert.Matches(@"calor v\d+\.\d+\.\d+", agentsMdContent);

        // Assert - No settings.json (Codex doesn't support hooks)
        var settingsPath = Path.Combine(_testDirectory, ".codex", "settings.json");
        Assert.False(File.Exists(settingsPath));

        // Assert - .csproj has Calor targets
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", csprojContent);

        // Assert - .gitattributes exists
        var gitAttributesPath = Path.Combine(_testDirectory, ".gitattributes");
        Assert.True(File.Exists(gitAttributesPath));
    }

    #endregion

    #region Gemini E2E Tests

    [Fact]
    public async Task GeminiInit_CreatesAllExpectedFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        var geminiInitializer = new GeminiInitializer();
        await geminiInitializer.InitializeAsync(_testDirectory, force: false);
        await csprojInitializer.InitializeAsync(csprojPath);
        await GitAttributesInitializer.InitializeAsync(_testDirectory);

        // Assert - All skill directories exist
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".gemini", "skills", "calor")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".gemini", "skills", "calor-convert")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".gemini", "skills", "calor-analyze")));

        // Assert - All skill files exist and have content
        var calorSkillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor", "SKILL.md");
        var convertSkillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor-convert", "SKILL.md");
        var analyzeSkillPath = Path.Combine(_testDirectory, ".gemini", "skills", "calor-analyze", "SKILL.md");

        Assert.True(File.Exists(calorSkillPath));
        Assert.True(File.Exists(convertSkillPath));
        Assert.True(File.Exists(analyzeSkillPath));

        Assert.True((await File.ReadAllTextAsync(calorSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(convertSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(analyzeSkillPath)).Length > 100);

        // Assert - GEMINI.md exists with correct content
        var geminiMdPath = Path.Combine(_testDirectory, "GEMINI.md");
        Assert.True(File.Exists(geminiMdPath));
        var geminiMdContent = await File.ReadAllTextAsync(geminiMdPath);
        Assert.Contains("<!-- BEGIN CalorC SECTION", geminiMdContent);
        Assert.Contains("<!-- END CalorC SECTION -->", geminiMdContent);
        Assert.Matches(@"calor v\d+\.\d+\.\d+", geminiMdContent);

        // Assert - settings.json exists with hooks (Gemini supports hooks)
        var settingsPath = Path.Combine(_testDirectory, ".gemini", "settings.json");
        Assert.True(File.Exists(settingsPath));
        var settingsContent = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("calor hook validate-write", settingsContent);

        // Assert - .csproj has Calor targets
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", csprojContent);

        // Assert - .gitattributes exists
        var gitAttributesPath = Path.Combine(_testDirectory, ".gitattributes");
        Assert.True(File.Exists(gitAttributesPath));
    }

    #endregion

    #region GitHub Copilot E2E Tests

    [Fact]
    public async Task GitHubInit_CreatesAllExpectedFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        var githubInitializer = new GitHubCopilotInitializer();
        await githubInitializer.InitializeAsync(_testDirectory, force: false);
        await csprojInitializer.InitializeAsync(csprojPath);
        await GitAttributesInitializer.InitializeAsync(_testDirectory);

        // Assert - All skill directories exist
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor-convert")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor-analyze")));

        // Assert - All skill files exist and have content
        var calorSkillPath = Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor", "SKILL.md");
        var convertSkillPath = Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor-convert", "SKILL.md");
        var analyzeSkillPath = Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor-analyze", "SKILL.md");

        Assert.True(File.Exists(calorSkillPath));
        Assert.True(File.Exists(convertSkillPath));
        Assert.True(File.Exists(analyzeSkillPath));

        Assert.True((await File.ReadAllTextAsync(calorSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(convertSkillPath)).Length > 100);
        Assert.True((await File.ReadAllTextAsync(analyzeSkillPath)).Length > 100);

        // Assert - copilot-instructions.md exists with correct content
        var instructionsPath = Path.Combine(_testDirectory, ".github", "copilot-instructions.md");
        Assert.True(File.Exists(instructionsPath));
        var instructionsContent = await File.ReadAllTextAsync(instructionsPath);
        Assert.Contains("<!-- BEGIN CalorC SECTION", instructionsContent);
        Assert.Contains("<!-- END CalorC SECTION -->", instructionsContent);
        Assert.Matches(@"calor v\d+\.\d+\.\d+", instructionsContent);

        // Assert - No settings.json (GitHub Copilot doesn't support hooks)
        var settingsPath = Path.Combine(_testDirectory, ".github", "settings.json");
        Assert.False(File.Exists(settingsPath));

        // Assert - .csproj has Calor targets
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", csprojContent);

        // Assert - .gitattributes exists
        var gitAttributesPath = Path.Combine(_testDirectory, ".gitattributes");
        Assert.True(File.Exists(gitAttributesPath));
    }

    #endregion

    #region All Agents Combined Test

    [Fact]
    public async Task AllAgentsInit_CreatesAllExpectedFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act - Initialize all agents
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);

        foreach (var agent in AiInitializerFactory.SupportedAgents)
        {
            var initializer = AiInitializerFactory.Create(agent);
            var result = await initializer.InitializeAsync(_testDirectory, force: false);
            Assert.True(result.Success, $"Init failed for {agent}: {string.Join(", ", result.Warnings)}");
        }

        await csprojInitializer.InitializeAsync(csprojPath);
        await GitAttributesInitializer.InitializeAsync(_testDirectory);

        // Assert - All agent-specific files exist
        // Claude
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".claude", "settings.json")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));

        // Codex
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".codex", "skills", "calor", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, "AGENTS.md")));

        // Gemini
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".gemini", "skills", "calor", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".gemini", "settings.json")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, "GEMINI.md")));

        // GitHub
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".github", "copilot", "skills", "calor", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".github", "copilot-instructions.md")));

        // Common files
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".gitattributes")));
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", csprojContent);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("gemini")]
    [InlineData("github")]
    public async Task AgentInit_IsIdempotent(string agent)
    {
        // Arrange
        await CreateTestCsproj();

        var initializer = AiInitializerFactory.Create(agent);

        // Act - Run init 3 times
        var result1 = await initializer.InitializeAsync(_testDirectory, force: false);
        var result2 = await initializer.InitializeAsync(_testDirectory, force: false);
        var result3 = await initializer.InitializeAsync(_testDirectory, force: false);

        // Assert - All succeed
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.True(result3.Success);

        // Assert - Second and third runs create no new files (already exist)
        Assert.Empty(result2.CreatedFiles);
        Assert.Empty(result3.CreatedFiles);
    }

    #endregion

    #region .calor/config.json Tests

    [Fact]
    public async Task CalorConfigManager_CreatesConfigJson()
    {
        // Arrange
        await CreateTestCsproj();

        // Act - CalorConfigManager.AddAgents creates .calor/config.json
        // This is called by InitCommand to track which agents are configured
        var configCreated = CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, force: false);

        // Assert - .calor/config.json exists
        var configPath = Path.Combine(_testDirectory, ".calor", "config.json");
        Assert.True(File.Exists(configPath));
        Assert.True(configCreated);

        var configContent = await File.ReadAllTextAsync(configPath);
        Assert.Contains("version", configContent.ToLower());
        Assert.Contains("claude", configContent.ToLower());
    }

    [Fact]
    public async Task CalorConfigManager_TracksMultipleAgents()
    {
        // Arrange
        await CreateTestCsproj();

        // Act - Add multiple agents
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude", "gemini" }, force: false);

        // Assert
        var configPath = Path.Combine(_testDirectory, ".calor", "config.json");
        var configContent = await File.ReadAllTextAsync(configPath);
        Assert.Contains("claude", configContent.ToLower());
        Assert.Contains("gemini", configContent.ToLower());
    }

    #endregion

    #region .gitattributes Tests

    [Fact]
    public async Task GitAttributesInit_CreatesCorrectContent()
    {
        // Arrange
        await CreateTestCsproj();

        // Act
        await GitAttributesInitializer.InitializeAsync(_testDirectory);

        // Assert
        var gitAttributesPath = Path.Combine(_testDirectory, ".gitattributes");
        Assert.True(File.Exists(gitAttributesPath));

        var content = await File.ReadAllTextAsync(gitAttributesPath);
        Assert.Contains("*.calr", content);
        Assert.Contains("linguist-language=Calor", content);
    }

    [Fact]
    public async Task GitAttributesInit_PreservesExistingContent()
    {
        // Arrange
        await CreateTestCsproj();

        var gitAttributesPath = Path.Combine(_testDirectory, ".gitattributes");
        var existingContent = "*.txt text\n*.bin binary\n";
        await File.WriteAllTextAsync(gitAttributesPath, existingContent);

        // Act
        await GitAttributesInitializer.InitializeAsync(_testDirectory);

        // Assert
        var content = await File.ReadAllTextAsync(gitAttributesPath);
        Assert.Contains("*.txt text", content);
        Assert.Contains("*.bin binary", content);
        Assert.Contains("*.calr", content);
    }

    #endregion

    #region MSBuild Targets Tests

    [Fact]
    public async Task CsprojInit_AddsAllRequiredMsBuildTargets()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        await csprojInitializer.InitializeAsync(csprojPath);

        // Assert
        var csprojContent = await File.ReadAllTextAsync(csprojPath);

        // Required targets
        Assert.Contains("CompileCalorFiles", csprojContent);
        Assert.Contains("IncludeCalorGeneratedFiles", csprojContent);
        Assert.Contains("CleanCalorFiles", csprojContent);

        // Required properties
        Assert.Contains("CalorOutputDirectory", csprojContent);

        // Required conditions
        Assert.Contains("*.calr", csprojContent);
    }

    [Fact]
    public async Task CsprojInit_CreatesBackup()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");
        var originalContent = await File.ReadAllTextAsync(csprojPath);

        // Act
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        await csprojInitializer.InitializeAsync(csprojPath);

        // Assert
        var backupPath = csprojPath + ".bak";
        Assert.True(File.Exists(backupPath));
        var backupContent = await File.ReadAllTextAsync(backupPath);
        Assert.Equal(originalContent, backupContent);
    }

    #endregion
}
