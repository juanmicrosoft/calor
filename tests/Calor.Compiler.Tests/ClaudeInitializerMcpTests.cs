using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class ClaudeInitializerMcpTests : IDisposable
{
    private readonly string _testDir;

    public ClaudeInitializerMcpTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"calor-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task Initialize_ConfiguresMcpServer_InNewSettingsJson()
    {
        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var settingsPath = Path.Combine(_testDir, ".claude", "settings.json");
        var content = await File.ReadAllTextAsync(settingsPath);

        Assert.Contains("mcpServers", content);
        Assert.Contains("calor-lsp", content);
        Assert.Contains("\"command\": \"calor\"", content);
        Assert.Contains("\"lsp\"", content);
    }

    [Fact]
    public async Task Initialize_PreservesExistingMcpServers()
    {
        // Pre-create settings with existing MCP server
        var claudeDir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(
            Path.Combine(claudeDir, "settings.json"),
            "{\"mcpServers\": {\"existing-server\": {\"command\": \"existing\"}}}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));

        Assert.Contains("existing-server", content);
        Assert.Contains("calor-lsp", content);
    }

    [Fact]
    public async Task Initialize_DoesNotDuplicateMcpServer_WhenAlreadyConfigured()
    {
        // First initialization
        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        // Second initialization
        await initializer.InitializeAsync(_testDir, force: false);

        var settingsPath = Path.Combine(_testDir, ".claude", "settings.json");
        var content = await File.ReadAllTextAsync(settingsPath);

        // Count occurrences of calor-lsp - should only appear once
        var count = content.Split("calor-lsp").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Initialize_MessagesMentionMcpServer()
    {
        var initializer = new ClaudeInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        Assert.True(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("calor-lsp") || m.Contains("MCP server"));
    }

    [Fact]
    public async Task Initialize_McpServerConfigHasCorrectFormat()
    {
        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var settingsPath = Path.Combine(_testDir, ".claude", "settings.json");
        var content = await File.ReadAllTextAsync(settingsPath);

        // Verify the JSON structure matches expected Claude Code format
        Assert.Contains("\"mcpServers\"", content);
        Assert.Contains("\"calor-lsp\"", content);
        Assert.Contains("\"command\": \"calor\"", content);
        Assert.Contains("\"args\"", content);
        Assert.Contains("[", content); // args should be an array
    }

    [Fact]
    public async Task Initialize_WithForce_OverwritesInvalidJson()
    {
        // Pre-create invalid settings file
        var claudeDir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(
            Path.Combine(claudeDir, "settings.json"),
            "{ invalid json }");

        var initializer = new ClaudeInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: true);

        Assert.True(result.Success);

        var content = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));
        Assert.Contains("mcpServers", content);
        Assert.Contains("calor-lsp", content);
    }

    [Fact]
    public async Task Initialize_PreservesExistingHooks_WhenAddingMcpServer()
    {
        // Pre-create settings with hooks but no MCP servers
        var claudeDir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        var existingSettings = @"{
  ""hooks"": {
    ""PreToolUse"": [
      {
        ""matcher"": ""Write"",
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""calor hook validate-write $TOOL_INPUT""
          }
        ]
      }
    ]
  }
}";
        await File.WriteAllTextAsync(Path.Combine(claudeDir, "settings.json"), existingSettings);

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));

        // Should preserve hooks
        Assert.Contains("hooks", content);
        Assert.Contains("PreToolUse", content);
        Assert.Contains("calor hook validate-write", content);

        // Should add MCP servers
        Assert.Contains("mcpServers", content);
        Assert.Contains("calor-lsp", content);
    }

    // Edge case tests

    [Fact]
    public async Task Initialize_WithEmptyMcpServersObject_AddsCalorLsp()
    {
        // Pre-create settings with empty mcpServers object
        var claudeDir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(
            Path.Combine(claudeDir, "settings.json"),
            "{\"mcpServers\": {}}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));
        Assert.Contains("calor-lsp", content);
    }

    [Fact]
    public async Task Initialize_WithNullMcpServers_AddsCalorLsp()
    {
        // Pre-create settings with null mcpServers
        var claudeDir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(
            Path.Combine(claudeDir, "settings.json"),
            "{\"mcpServers\": null}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));
        Assert.Contains("calor-lsp", content);
    }

    [Fact]
    public async Task Initialize_WithExistingCalorLsp_DoesNotOverwrite()
    {
        // Pre-create settings with calor-lsp already configured with custom args
        var claudeDir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        var existingSettings = @"{
  ""mcpServers"": {
    ""calor-lsp"": {
      ""command"": ""custom-calor"",
      ""args"": [""custom-arg""]
    }
  }
}";
        await File.WriteAllTextAsync(Path.Combine(claudeDir, "settings.json"), existingSettings);

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));

        // Should preserve the existing custom configuration
        Assert.Contains("custom-calor", content);
        Assert.Contains("custom-arg", content);
        // Should not have the default config
        Assert.DoesNotContain("\"command\": \"calor\"", content);
    }

    [Fact]
    public async Task Initialize_WithOnlyMcpServers_PreservesAndAddsHooks()
    {
        // Pre-create settings with only mcpServers (no hooks)
        var claudeDir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(
            Path.Combine(claudeDir, "settings.json"),
            "{\"mcpServers\": {\"other-server\": {\"command\": \"other\"}}}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));

        // Should preserve existing MCP server
        Assert.Contains("other-server", content);
        // Should add calor-lsp
        Assert.Contains("calor-lsp", content);
        // Should add hooks
        Assert.Contains("hooks", content);
        Assert.Contains("PreToolUse", content);
    }

    [Fact]
    public async Task Initialize_WithEmptySettingsObject_AddsAllConfiguration()
    {
        // Pre-create empty settings object
        var claudeDir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(
            Path.Combine(claudeDir, "settings.json"),
            "{}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(claudeDir, "settings.json"));

        // Should add both hooks and MCP servers
        Assert.Contains("hooks", content);
        Assert.Contains("mcpServers", content);
        Assert.Contains("calor-lsp", content);
    }

    [Fact]
    public async Task Initialize_SettingsJsonHasCorrectJsonStructure()
    {
        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var settingsPath = Path.Combine(_testDir, ".claude", "settings.json");
        var content = await File.ReadAllTextAsync(settingsPath);

        // Verify it's valid JSON by parsing it
        var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        // Verify mcpServers structure
        Assert.True(root.TryGetProperty("mcpServers", out var mcpServers));
        Assert.True(mcpServers.TryGetProperty("calor-lsp", out var calorLsp));
        Assert.True(calorLsp.TryGetProperty("command", out var command));
        Assert.Equal("calor", command.GetString());
        Assert.True(calorLsp.TryGetProperty("args", out var args));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, args.ValueKind);
        Assert.Equal("lsp", args[0].GetString());
    }
}
