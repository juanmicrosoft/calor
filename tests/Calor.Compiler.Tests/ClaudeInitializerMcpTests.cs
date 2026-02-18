using System.Text.Json;
using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class ClaudeInitializerMcpTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _claudeJsonPath;

    public ClaudeInitializerMcpTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"calor-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        // Use a test-specific claude.json file
        _claudeJsonPath = Path.Combine(_testDir, ".claude.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private ClaudeInitializer CreateInitializer()
    {
        return new ClaudeInitializer { ClaudeJsonPathOverride = _claudeJsonPath };
    }

    [Fact]
    public async Task Initialize_ConfiguresMcpServer_InClaudeJson()
    {
        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        // MCP servers should be in ~/.claude.json per-project section
        Assert.True(File.Exists(_claudeJsonPath), ".claude.json should be created");

        var content = await File.ReadAllTextAsync(_claudeJsonPath);
        var json = JsonDocument.Parse(content);

        // Check structure
        Assert.True(json.RootElement.TryGetProperty("projects", out var projects));
        Assert.True(projects.TryGetProperty(_testDir, out var project));
        Assert.True(project.TryGetProperty("mcpServers", out var mcpServers));
        Assert.True(mcpServers.TryGetProperty("calor", out var calor));

        // Check calor MCP server config
        Assert.Equal("stdio", calor.GetProperty("type").GetString());
        Assert.Equal("calor", calor.GetProperty("command").GetString());
        var args = calor.GetProperty("args");
        Assert.Equal("mcp", args[0].GetString());
        Assert.Equal("--stdio", args[1].GetString());
    }

    [Fact]
    public async Task Initialize_SettingsJson_DoesNotContainMcpServers()
    {
        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        // settings.json should only contain hooks, not MCP servers
        var settingsPath = Path.Combine(_testDir, ".claude", "settings.json");
        var content = await File.ReadAllTextAsync(settingsPath);

        Assert.Contains("hooks", content);
        Assert.DoesNotContain("mcpServers", content);
    }

    [Fact]
    public async Task Initialize_PreservesExistingMcpServers()
    {
        // Pre-create .claude.json with existing MCP server
        var existingConfig = $@"{{
  ""projects"": {{
    ""{_testDir.Replace("\\", "\\\\")}"": {{
      ""mcpServers"": {{
        ""existing-server"": {{
          ""type"": ""stdio"",
          ""command"": ""existing""
        }}
      }}
    }}
  }}
}}";
        await File.WriteAllTextAsync(_claudeJsonPath, existingConfig);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);

        Assert.Contains("existing-server", content);
        Assert.Contains("\"calor\"", content);
    }

    [Fact]
    public async Task Initialize_DoesNotDuplicateMcpServer_WhenAlreadyConfigured()
    {
        var initializer = CreateInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDir, force: false);

        // Second initialization
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);

        // Count occurrences of "calor": - should only appear once
        var count = content.Split("\"calor\":").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Initialize_MessagesMentionMcpServer()
    {
        var initializer = CreateInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        Assert.True(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("MCP server"));
    }

    [Fact]
    public async Task Initialize_McpServerConfigHasCorrectFormat()
    {
        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);
        var json = JsonDocument.Parse(content);

        var projects = json.RootElement.GetProperty("projects");
        var project = projects.GetProperty(_testDir);
        var mcpServers = project.GetProperty("mcpServers");
        var calor = mcpServers.GetProperty("calor");

        Assert.Equal("stdio", calor.GetProperty("type").GetString());
        Assert.Equal("calor", calor.GetProperty("command").GetString());
        Assert.Equal(JsonValueKind.Array, calor.GetProperty("args").ValueKind);
    }

    [Fact]
    public async Task Initialize_WithForce_OverwritesInvalidClaudeJson()
    {
        // Pre-create invalid .claude.json file
        await File.WriteAllTextAsync(_claudeJsonPath, "{ invalid json }");

        var initializer = CreateInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: true);

        Assert.True(result.Success);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);
        Assert.Contains("mcpServers", content);
        Assert.Contains("\"calor\"", content);
    }

    [Fact]
    public async Task Initialize_HooksAndMcpServers_InSeparateFiles()
    {
        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        // Hooks should be in .claude/settings.json
        var settingsPath = Path.Combine(_testDir, ".claude", "settings.json");
        var settingsContent = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("hooks", settingsContent);
        Assert.Contains("PreToolUse", settingsContent);
        Assert.Contains("calor hook validate-write", settingsContent);

        // MCP servers should be in ~/.claude.json
        var mcpContent = await File.ReadAllTextAsync(_claudeJsonPath);
        Assert.Contains("mcpServers", mcpContent);
        Assert.Contains("\"calor\"", mcpContent);
    }

    [Fact]
    public async Task Initialize_WithEmptyMcpServersObject_AddsCalor()
    {
        // Pre-create .claude.json with empty mcpServers object
        var existingConfig = $@"{{
  ""projects"": {{
    ""{_testDir.Replace("\\", "\\\\")}"": {{
      ""mcpServers"": {{}}
    }}
  }}
}}";
        await File.WriteAllTextAsync(_claudeJsonPath, existingConfig);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);
        Assert.Contains("\"calor\"", content);
    }

    [Fact]
    public async Task Initialize_WithExistingCalor_DoesNotOverwrite()
    {
        // Pre-create .claude.json with calor already configured with custom args
        var existingConfig = $@"{{
  ""projects"": {{
    ""{_testDir.Replace("\\", "\\\\")}"": {{
      ""mcpServers"": {{
        ""calor"": {{
          ""type"": ""stdio"",
          ""command"": ""custom-calor"",
          ""args"": [""custom-arg""]
        }}
      }}
    }}
  }}
}}";
        await File.WriteAllTextAsync(_claudeJsonPath, existingConfig);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);

        // Should preserve the existing custom calor configuration
        Assert.Contains("custom-calor", content);
        Assert.Contains("custom-arg", content);
    }

    [Fact]
    public async Task Initialize_RemovesIncorrectCalorLspEntry()
    {
        // Pre-create .claude.json with incorrect calor-lsp entry (LSP is not MCP)
        var existingConfig = $@"{{
  ""projects"": {{
    ""{_testDir.Replace("\\", "\\\\")}"": {{
      ""mcpServers"": {{
        ""calor-lsp"": {{
          ""type"": ""stdio"",
          ""command"": ""calor"",
          ""args"": [""lsp""]
        }}
      }}
    }}
  }}
}}";
        await File.WriteAllTextAsync(_claudeJsonPath, existingConfig);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);

        // Should remove the incorrect calor-lsp entry
        Assert.DoesNotContain("calor-lsp", content);
        // Should add the correct calor MCP server
        Assert.Contains("\"calor\"", content);
        Assert.Contains("\"mcp\"", content);
    }

    [Fact]
    public async Task Initialize_PreservesOtherClaudeJsonProperties()
    {
        // Pre-create .claude.json with other properties
        var existingConfig = @"{
  ""numStartups"": 42,
  ""projects"": {}
}";
        await File.WriteAllTextAsync(_claudeJsonPath, existingConfig);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);

        // Should preserve existing properties
        Assert.Contains("numStartups", content);
        Assert.Contains("42", content);
        // Should add MCP servers
        Assert.Contains("\"calor\"", content);
    }

    [Fact]
    public async Task Initialize_FromSubdirectory_KeysByGitRoot()
    {
        // Simulate a git repo: create .git at the root
        var gitRoot = _testDir;
        Directory.CreateDirectory(Path.Combine(gitRoot, ".git"));

        // Create a nested subdirectory (simulates running from a subdirectory)
        var subDir = Path.Combine(gitRoot, "src", "MyProject");
        Directory.CreateDirectory(subDir);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(subDir, force: false);

        // MCP config should be keyed by git root, not the subdirectory
        var content = await File.ReadAllTextAsync(_claudeJsonPath);
        var json = JsonDocument.Parse(content);
        var projects = json.RootElement.GetProperty("projects");

        Assert.True(projects.TryGetProperty(gitRoot, out var project),
            $"Expected project key '{gitRoot}' but found: {projects}");
        Assert.True(project.TryGetProperty("mcpServers", out var mcpServers));
        Assert.True(mcpServers.TryGetProperty("calor", out _));

        // The subdirectory path should NOT be a key
        Assert.False(projects.TryGetProperty(subDir, out _),
            "Subdirectory should not be a project key");
    }

    [Fact]
    public async Task Initialize_FromSubdirectory_PlacesArtifactsAtGitRoot()
    {
        // Simulate a git repo
        var gitRoot = _testDir;
        Directory.CreateDirectory(Path.Combine(gitRoot, ".git"));

        var subDir = Path.Combine(gitRoot, "src", "MyProject");
        Directory.CreateDirectory(subDir);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(subDir, force: false);

        // Skills, CLAUDE.md, and settings.json should be at git root, not subdirectory
        Assert.True(File.Exists(Path.Combine(gitRoot, "CLAUDE.md")),
            "CLAUDE.md should be at git root");
        Assert.True(File.Exists(Path.Combine(gitRoot, ".claude", "settings.json")),
            "settings.json should be at git root");
        Assert.True(Directory.Exists(Path.Combine(gitRoot, ".claude", "skills", "calor")),
            "skills should be at git root");

        // Should NOT be in subdirectory
        Assert.False(File.Exists(Path.Combine(subDir, "CLAUDE.md")),
            "CLAUDE.md should not be in subdirectory");
        Assert.False(Directory.Exists(Path.Combine(subDir, ".claude")),
            ".claude dir should not be in subdirectory");
    }

    [Fact]
    public async Task Initialize_FromDifferentSubdirs_MergesMcpConfig()
    {
        // Simulate a git repo
        var gitRoot = _testDir;
        Directory.CreateDirectory(Path.Combine(gitRoot, ".git"));

        var subDirA = Path.Combine(gitRoot, "src", "ProjectA");
        var subDirB = Path.Combine(gitRoot, "src", "ProjectB");
        Directory.CreateDirectory(subDirA);
        Directory.CreateDirectory(subDirB);

        var initializer = CreateInitializer();

        // Init from two different subdirectories
        await initializer.InitializeAsync(subDirA, force: false);
        await initializer.InitializeAsync(subDirB, force: false);

        // Should have exactly one project entry keyed by git root
        var content = await File.ReadAllTextAsync(_claudeJsonPath);
        var json = JsonDocument.Parse(content);
        var projects = json.RootElement.GetProperty("projects");

        Assert.True(projects.TryGetProperty(gitRoot, out var project));
        Assert.True(project.TryGetProperty("mcpServers", out var mcpServers));
        Assert.True(mcpServers.TryGetProperty("calor", out _));

        // Count project entries - should be exactly 1
        var projectCount = 0;
        foreach (var _ in projects.EnumerateObject()) projectCount++;
        Assert.Equal(1, projectCount);
    }

    [Fact]
    public async Task Initialize_GitWorktree_DetectsGitFile()
    {
        // In a git worktree, .git is a file (not a directory) containing "gitdir: ..."
        var gitRoot = _testDir;
        await File.WriteAllTextAsync(Path.Combine(gitRoot, ".git"),
            "gitdir: /some/other/path/.git/worktrees/my-branch");

        var subDir = Path.Combine(gitRoot, "src", "MyProject");
        Directory.CreateDirectory(subDir);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(subDir, force: false);

        // MCP config should be keyed by the worktree root (where .git file is)
        var content = await File.ReadAllTextAsync(_claudeJsonPath);
        var json = JsonDocument.Parse(content);
        var projects = json.RootElement.GetProperty("projects");

        Assert.True(projects.TryGetProperty(gitRoot, out _),
            "Should key by worktree root where .git file exists");
        Assert.False(projects.TryGetProperty(subDir, out _),
            "Subdirectory should not be a project key");
    }

    [Fact]
    public async Task Initialize_CreatesNewProjectEntry_WhenNotExists()
    {
        // Pre-create .claude.json without this project
        var existingConfig = @"{
  ""projects"": {
    ""/some/other/path"": {
      ""mcpServers"": {}
    }
  }
}";
        await File.WriteAllTextAsync(_claudeJsonPath, existingConfig);

        var initializer = CreateInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(_claudeJsonPath);
        var json = JsonDocument.Parse(content);

        // Should preserve other project
        var projects = json.RootElement.GetProperty("projects");
        Assert.True(projects.TryGetProperty("/some/other/path", out _));

        // Should create new project entry
        Assert.True(projects.TryGetProperty(_testDir, out var project));
        Assert.True(project.TryGetProperty("mcpServers", out var mcpServers));
        Assert.True(mcpServers.TryGetProperty("calor", out _));
    }
}
