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
    public async Task Initialize_ConfiguresMcpServer_InMcpJson()
    {
        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        // MCP servers should be in .mcp.json, not settings.json
        var mcpJsonPath = Path.Combine(_testDir, ".mcp.json");
        Assert.True(File.Exists(mcpJsonPath), ".mcp.json should be created");

        var content = await File.ReadAllTextAsync(mcpJsonPath);

        Assert.Contains("mcpServers", content);
        Assert.Contains("calor-lsp", content);
        Assert.Contains("\"command\": \"calor\"", content);
        Assert.Contains("\"lsp\"", content);
        Assert.Contains("\"type\": \"stdio\"", content);
    }

    [Fact]
    public async Task Initialize_SettingsJson_DoesNotContainMcpServers()
    {
        var initializer = new ClaudeInitializer();
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
        // Pre-create .mcp.json with existing MCP server
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, ".mcp.json"),
            "{\"mcpServers\": {\"existing-server\": {\"type\": \"stdio\", \"command\": \"existing\"}}}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(_testDir, ".mcp.json"));

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

        var mcpJsonPath = Path.Combine(_testDir, ".mcp.json");
        var content = await File.ReadAllTextAsync(mcpJsonPath);

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

        var mcpJsonPath = Path.Combine(_testDir, ".mcp.json");
        var content = await File.ReadAllTextAsync(mcpJsonPath);

        // Verify the JSON structure matches expected Claude Code format
        Assert.Contains("\"mcpServers\"", content);
        Assert.Contains("\"calor-lsp\"", content);
        Assert.Contains("\"type\": \"stdio\"", content);
        Assert.Contains("\"command\": \"calor\"", content);
        Assert.Contains("\"args\"", content);
        Assert.Contains("[", content); // args should be an array
    }

    [Fact]
    public async Task Initialize_WithForce_OverwritesInvalidMcpJson()
    {
        // Pre-create invalid .mcp.json file
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, ".mcp.json"),
            "{ invalid json }");

        var initializer = new ClaudeInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: true);

        Assert.True(result.Success);

        var content = await File.ReadAllTextAsync(Path.Combine(_testDir, ".mcp.json"));
        Assert.Contains("mcpServers", content);
        Assert.Contains("calor-lsp", content);
    }

    [Fact]
    public async Task Initialize_HooksAndMcpServers_InSeparateFiles()
    {
        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        // Hooks should be in .claude/settings.json
        var settingsPath = Path.Combine(_testDir, ".claude", "settings.json");
        var settingsContent = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("hooks", settingsContent);
        Assert.Contains("PreToolUse", settingsContent);
        Assert.Contains("calor hook validate-write", settingsContent);

        // MCP servers should be in .mcp.json
        var mcpJsonPath = Path.Combine(_testDir, ".mcp.json");
        var mcpContent = await File.ReadAllTextAsync(mcpJsonPath);
        Assert.Contains("mcpServers", mcpContent);
        Assert.Contains("calor-lsp", mcpContent);
    }

    // Edge case tests

    [Fact]
    public async Task Initialize_WithEmptyMcpServersObject_AddsCalorLsp()
    {
        // Pre-create .mcp.json with empty mcpServers object
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, ".mcp.json"),
            "{\"mcpServers\": {}}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(_testDir, ".mcp.json"));
        Assert.Contains("calor-lsp", content);
    }

    [Fact]
    public async Task Initialize_WithNullMcpServers_AddsCalorLsp()
    {
        // Pre-create .mcp.json with null mcpServers
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, ".mcp.json"),
            "{\"mcpServers\": null}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(_testDir, ".mcp.json"));
        Assert.Contains("calor-lsp", content);
    }

    [Fact]
    public async Task Initialize_WithExistingCalorLsp_DoesNotOverwrite()
    {
        // Pre-create .mcp.json with calor-lsp already configured with custom args
        var existingConfig = @"{
  ""mcpServers"": {
    ""calor-lsp"": {
      ""type"": ""stdio"",
      ""command"": ""custom-calor"",
      ""args"": [""custom-arg""]
    }
  }
}";
        await File.WriteAllTextAsync(Path.Combine(_testDir, ".mcp.json"), existingConfig);

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(_testDir, ".mcp.json"));

        // Should preserve the existing custom calor-lsp configuration
        Assert.Contains("custom-calor", content);
        Assert.Contains("custom-arg", content);
        // Should add the calor MCP server (for AI agent tools)
        Assert.Contains("\"calor\":", content);
        Assert.Contains("\"mcp\"", content);
    }

    [Fact]
    public async Task Initialize_WithOnlyMcpServers_HooksAddedToSettingsJson()
    {
        // Pre-create .mcp.json with only mcpServers
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, ".mcp.json"),
            "{\"mcpServers\": {\"other-server\": {\"type\": \"stdio\", \"command\": \"other\"}}}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var mcpContent = await File.ReadAllTextAsync(Path.Combine(_testDir, ".mcp.json"));
        // Should preserve existing MCP server
        Assert.Contains("other-server", mcpContent);
        // Should add calor-lsp
        Assert.Contains("calor-lsp", mcpContent);

        var settingsContent = await File.ReadAllTextAsync(Path.Combine(_testDir, ".claude", "settings.json"));
        // Should add hooks
        Assert.Contains("hooks", settingsContent);
        Assert.Contains("PreToolUse", settingsContent);
    }

    [Fact]
    public async Task Initialize_WithEmptyMcpJsonObject_AddsAllMcpServers()
    {
        // Pre-create empty .mcp.json object
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, ".mcp.json"),
            "{}");

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(Path.Combine(_testDir, ".mcp.json"));

        // Should add MCP servers
        Assert.Contains("mcpServers", content);
        Assert.Contains("calor-lsp", content);
        Assert.Contains("calor", content);
    }

    [Fact]
    public async Task Initialize_McpJsonHasCorrectJsonStructure()
    {
        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var mcpJsonPath = Path.Combine(_testDir, ".mcp.json");
        var content = await File.ReadAllTextAsync(mcpJsonPath);

        // Verify it's valid JSON by parsing it
        var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        // Verify mcpServers structure
        Assert.True(root.TryGetProperty("mcpServers", out var mcpServers));
        Assert.True(mcpServers.TryGetProperty("calor-lsp", out var calorLsp));
        Assert.True(calorLsp.TryGetProperty("type", out var type));
        Assert.Equal("stdio", type.GetString());
        Assert.True(calorLsp.TryGetProperty("command", out var command));
        Assert.Equal("calor", command.GetString());
        Assert.True(calorLsp.TryGetProperty("args", out var args));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, args.ValueKind);
        Assert.Equal("lsp", args[0].GetString());
    }

    [Fact]
    public async Task Initialize_ConfiguresBothCalorMcpServers()
    {
        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var mcpJsonPath = Path.Combine(_testDir, ".mcp.json");
        var content = await File.ReadAllTextAsync(mcpJsonPath);

        // Should configure both calor-lsp and calor MCP servers
        Assert.Contains("calor-lsp", content);
        Assert.Contains("\"calor\":", content);

        // Verify the calor MCP server has correct args
        var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
        var mcpServers = jsonDoc.RootElement.GetProperty("mcpServers");

        // calor-lsp runs "calor lsp"
        var calorLsp = mcpServers.GetProperty("calor-lsp");
        Assert.Equal("lsp", calorLsp.GetProperty("args")[0].GetString());

        // calor runs "calor mcp --stdio"
        var calor = mcpServers.GetProperty("calor");
        Assert.Equal("mcp", calor.GetProperty("args")[0].GetString());
        Assert.Equal("--stdio", calor.GetProperty("args")[1].GetString());
    }
}
