using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Integration tests for the calor init command that test the full workflow
/// including MSBuild-only init and MSBuild + AI agent init.
/// </summary>
public class InitCommandIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public InitCommandIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-init-integration-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task MsBuildOnlyInit_AddsCalorTargets_DoesNotCreateClaudeFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act - Initialize MSBuild targets only (simulating calor init without --ai)
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        var result = await csprojInitializer.InitializeAsync(csprojPath);

        // Assert - MSBuild targets should be added
        Assert.True(result.IsSuccess);
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", csprojContent);
        Assert.Contains("CalorOutputDirectory", csprojContent);

        // Assert - Claude files should NOT exist
        Assert.False(Directory.Exists(Path.Combine(_testDirectory, ".claude")));
        Assert.False(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));
    }

    [Fact]
    public async Task ClaudeInit_AddsCalorTargets_AndCreatesClaudeFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act - Initialize with Claude (simulating calor init --ai claude)
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        var claudeInitializer = new ClaudeInitializer();

        var claudeResult = await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        var csprojResult = await csprojInitializer.InitializeAsync(csprojPath);

        // Assert - MSBuild targets should be added
        Assert.True(csprojResult.IsSuccess);
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", csprojContent);

        // Assert - Claude files should exist
        Assert.True(claudeResult.Success);
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor-convert")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor-convert", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));
    }

    [Fact]
    public async Task MsBuildInit_ThenClaudeInit_IsAdditive()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act - First: Initialize MSBuild only
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        await csprojInitializer.InitializeAsync(csprojPath);

        // Verify Claude files don't exist yet
        Assert.False(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));

        // Act - Second: Initialize Claude
        var claudeInitializer = new ClaudeInitializer();
        var claudeResult = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert - Claude files should now exist
        Assert.True(claudeResult.Success);
        Assert.True(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".claude", "skills", "calor", "SKILL.md")));

        // Assert - MSBuild targets should still be present (not removed)
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", csprojContent);
    }

    [Fact]
    public async Task ClaudeInit_WithExistingClaudeMd_AppendsCalorSection()
    {
        // Arrange
        await CreateTestCsproj();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");
        var existingContent = """
            # My Project

            This is my custom documentation.

            ## Build Instructions

            Run `dotnet build` to build.
            """;
        await File.WriteAllTextAsync(claudeMdPath, existingContent);

        // Act
        var claudeInitializer = new ClaudeInitializer();
        var result = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(claudeMdPath, result.UpdatedFiles);

        var content = await File.ReadAllTextAsync(claudeMdPath);

        // Original content should be preserved
        Assert.Contains("# My Project", content);
        Assert.Contains("This is my custom documentation.", content);
        Assert.Contains("## Build Instructions", content);

        // Calor section should be appended
        Assert.Contains("<!-- BEGIN CalorC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("## Calor-First Project", content);
        Assert.Contains("MANDATORY Rules for AI Agents", content);
        Assert.Contains("<!-- END CalorC SECTION -->", content);
    }

    [Fact]
    public async Task ClaudeInit_WithExistingCalorSection_ReplacesSection()
    {
        // Arrange
        await CreateTestCsproj();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");
        var existingContent = """
            # My Project

            Custom content before.

            <!-- BEGIN CalorC SECTION - DO NOT EDIT -->
            ## Old Calor Section
            This is OLD content that should be REPLACED.
            Old version info here.
            <!-- END CalorC SECTION -->

            ## Custom Section After

            More custom content after.
            """;
        await File.WriteAllTextAsync(claudeMdPath, existingContent);

        // Act
        var claudeInitializer = new ClaudeInitializer();
        var result = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert
        Assert.True(result.Success);

        var content = await File.ReadAllTextAsync(claudeMdPath);

        // Custom content before and after should be preserved
        Assert.Contains("# My Project", content);
        Assert.Contains("Custom content before.", content);
        Assert.Contains("## Custom Section After", content);
        Assert.Contains("More custom content after.", content);

        // Old Calor content should be replaced
        Assert.DoesNotContain("Old Calor Section", content);
        Assert.DoesNotContain("OLD content that should be REPLACED", content);
        Assert.DoesNotContain("Old version info here", content);

        // New Calor content should be present
        Assert.Contains("## Calor-First Project", content);
        Assert.Contains("MANDATORY Rules for AI Agents", content);
        Assert.Matches(@"calor v\d+\.\d+\.\d+", content);
    }

    [Fact]
    public async Task ClaudeInit_MultipleRuns_IsIdempotent()
    {
        // Arrange
        await CreateTestCsproj();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");

        var claudeInitializer = new ClaudeInitializer();

        // Act - Run init 3 times
        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterFirst = await File.ReadAllTextAsync(claudeMdPath);

        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterSecond = await File.ReadAllTextAsync(claudeMdPath);

        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterThird = await File.ReadAllTextAsync(claudeMdPath);

        // Assert - Content should be identical after each run
        Assert.Equal(contentAfterFirst, contentAfterSecond);
        Assert.Equal(contentAfterSecond, contentAfterThird);

        // Should only have one Calor section
        var beginCount = CountOccurrences(contentAfterThird, "<!-- BEGIN CalorC SECTION");
        var endCount = CountOccurrences(contentAfterThird, "<!-- END CalorC SECTION -->");
        Assert.Equal(1, beginCount);
        Assert.Equal(1, endCount);
    }

    [Fact]
    public async Task CsprojInit_MultipleRuns_IsIdempotent()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);

        // Act - Run init 3 times
        await csprojInitializer.InitializeAsync(csprojPath, force: true);
        var contentAfterFirst = await File.ReadAllTextAsync(csprojPath);

        await csprojInitializer.InitializeAsync(csprojPath, force: true);
        var contentAfterSecond = await File.ReadAllTextAsync(csprojPath);

        await csprojInitializer.InitializeAsync(csprojPath, force: true);
        var contentAfterThird = await File.ReadAllTextAsync(csprojPath);

        // Assert - Should only have one set of Calor targets
        var compileTargetCount = CountOccurrences(contentAfterThird, "Name=\"CompileCalorFiles\"");
        var includeTargetCount = CountOccurrences(contentAfterThird, "Name=\"IncludeCalorGeneratedFiles\"");
        var cleanTargetCount = CountOccurrences(contentAfterThird, "Name=\"CleanCalorFiles\"");

        Assert.Equal(1, compileTargetCount);
        Assert.Equal(1, includeTargetCount);
        Assert.Equal(1, cleanTargetCount);
    }

    [Fact]
    public async Task CsprojInit_CreatesBackupFile()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");
        var originalContent = await File.ReadAllTextAsync(csprojPath);

        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);

        // Act
        await csprojInitializer.InitializeAsync(csprojPath);

        // Assert
        var backupPath = csprojPath + ".bak";
        Assert.True(File.Exists(backupPath));
        var backupContent = await File.ReadAllTextAsync(backupPath);
        Assert.Equal(originalContent, backupContent);
    }

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    [Fact]
    public async Task ClaudeInit_CreatesMcpJsonWithMcpServer()
    {
        // Arrange
        await CreateTestCsproj();

        // Act
        var claudeInitializer = new ClaudeInitializer();
        var result = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert
        Assert.True(result.Success);

        // MCP servers should be in .mcp.json (not settings.json)
        var mcpJsonPath = Path.Combine(_testDirectory, ".mcp.json");
        Assert.True(File.Exists(mcpJsonPath));

        var content = await File.ReadAllTextAsync(mcpJsonPath);
        Assert.Contains("mcpServers", content);
        Assert.Contains("\"calor\"", content);
        Assert.Contains("\"type\": \"stdio\"", content);
        Assert.Contains("\"command\": \"calor\"", content);
        Assert.Contains("\"args\"", content);
        Assert.Contains("\"mcp\"", content);
        Assert.Contains("\"--stdio\"", content);
    }

    [Fact]
    public async Task ClaudeInit_PreservesExistingMcpServers()
    {
        // Arrange
        await CreateTestCsproj();

        // Create existing .mcp.json with another MCP server
        var existingMcpConfig = """
            {
              "mcpServers": {
                "existing-server": {
                  "type": "stdio",
                  "command": "existing-command",
                  "args": ["arg1", "arg2"]
                }
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, ".mcp.json"), existingMcpConfig);

        // Act
        var claudeInitializer = new ClaudeInitializer();
        var result = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert
        Assert.True(result.Success);

        var content = await File.ReadAllTextAsync(Path.Combine(_testDirectory, ".mcp.json"));

        // Both servers should exist
        Assert.Contains("existing-server", content);
        Assert.Contains("existing-command", content);
        Assert.Contains("\"calor\"", content);
        Assert.Contains("\"command\": \"calor\"", content);
    }

    [Fact]
    public async Task ClaudeInit_WithExistingHooksButNoMcp_AddsMcpServerToMcpJson()
    {
        // Arrange
        await CreateTestCsproj();

        // Create existing settings with hooks but no MCP servers
        var claudeDir = Path.Combine(_testDirectory, ".claude");
        Directory.CreateDirectory(claudeDir);
        var existingSettings = """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "CustomMatcher",
                    "hooks": [{"type": "command", "command": "custom-command"}]
                  }
                ]
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(claudeDir, "settings.json"), existingSettings);

        // Act
        var claudeInitializer = new ClaudeInitializer();
        var result = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert
        Assert.True(result.Success);

        // Existing hooks should be preserved in settings.json
        var settingsContent = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));
        Assert.Contains("CustomMatcher", settingsContent);
        Assert.Contains("custom-command", settingsContent);

        // MCP server should be added to .mcp.json (separate file)
        var mcpContent = await File.ReadAllTextAsync(Path.Combine(_testDirectory, ".mcp.json"));
        Assert.Contains("mcpServers", mcpContent);
        Assert.Contains("\"calor\"", mcpContent);
    }

    [Fact]
    public async Task ClaudeInit_MultipleRuns_DoesNotDuplicateMcpServer()
    {
        // Arrange
        await CreateTestCsproj();
        var claudeInitializer = new ClaudeInitializer();

        // Act - Run init 3 times
        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert - check .mcp.json (not settings.json)
        var mcpJsonPath = Path.Combine(_testDirectory, ".mcp.json");
        var content = await File.ReadAllTextAsync(mcpJsonPath);

        // "calor": should only appear once (the key, not in args)
        var count = CountOccurrences(content, "\"calor\":");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ClaudeInit_ResultMessagesIncludeMcpInfo()
    {
        // Arrange
        await CreateTestCsproj();

        // Act
        var claudeInitializer = new ClaudeInitializer();
        var result = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("MCP server"));
    }
}
