using System.Text.Json;
using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class GitHubCopilotMcpTests : IDisposable
{
    private readonly string _testDir;

    public GitHubCopilotMcpTests()
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
    public async Task Initialize_CreatesMcpJson_InVscodeDirectory()
    {
        var initializer = new GitHubCopilotInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var mcpJsonPath = Path.Combine(_testDir, ".vscode", "mcp.json");
        Assert.True(File.Exists(mcpJsonPath), ".vscode/mcp.json should be created");

        var content = await File.ReadAllTextAsync(mcpJsonPath);
        var json = JsonDocument.Parse(content);

        // Check structure: { "servers": { "calor": { "command": "calor", "args": ["mcp", "--stdio"] } } }
        Assert.True(json.RootElement.TryGetProperty("servers", out var servers));
        Assert.True(servers.TryGetProperty("calor", out var calor));
        Assert.Equal("calor", calor.GetProperty("command").GetString());

        var args = calor.GetProperty("args");
        Assert.Equal("mcp", args[0].GetString());
        Assert.Equal("--stdio", args[1].GetString());
    }

    [Fact]
    public async Task Initialize_McpJson_DoesNotContainTypeField()
    {
        var initializer = new GitHubCopilotInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var mcpJsonPath = Path.Combine(_testDir, ".vscode", "mcp.json");
        var content = await File.ReadAllTextAsync(mcpJsonPath);
        var json = JsonDocument.Parse(content);

        var calor = json.RootElement.GetProperty("servers").GetProperty("calor");

        // Copilot MCP config should NOT have a "type" field
        Assert.False(calor.TryGetProperty("type", out _), "Copilot MCP config should not contain 'type' field");
    }

    [Fact]
    public async Task Initialize_PreservesExistingServers()
    {
        // Pre-create .vscode/mcp.json with an existing server
        var vscodeDir = Path.Combine(_testDir, ".vscode");
        Directory.CreateDirectory(vscodeDir);
        var mcpJsonPath = Path.Combine(vscodeDir, "mcp.json");
        var existingConfig = @"{
  ""servers"": {
    ""existing-server"": {
      ""command"": ""existing"",
      ""args"": [""--flag""]
    }
  }
}";
        await File.WriteAllTextAsync(mcpJsonPath, existingConfig);

        var initializer = new GitHubCopilotInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(mcpJsonPath);
        Assert.Contains("existing-server", content);
        Assert.Contains("\"calor\"", content);
    }

    [Fact]
    public async Task Initialize_DoesNotDuplicate_WhenCalorAlreadyConfigured()
    {
        var initializer = new GitHubCopilotInitializer();

        // First initialization
        await initializer.InitializeAsync(_testDir, force: false);

        // Second initialization
        await initializer.InitializeAsync(_testDir, force: false);

        var mcpJsonPath = Path.Combine(_testDir, ".vscode", "mcp.json");
        var content = await File.ReadAllTextAsync(mcpJsonPath);

        // Count occurrences of "calor": - should only appear once
        var count = content.Split("\"calor\":").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Initialize_WithExistingCalor_DoesNotOverwrite()
    {
        // Pre-create .vscode/mcp.json with calor already configured with custom args
        var vscodeDir = Path.Combine(_testDir, ".vscode");
        Directory.CreateDirectory(vscodeDir);
        var mcpJsonPath = Path.Combine(vscodeDir, "mcp.json");
        var existingConfig = @"{
  ""servers"": {
    ""calor"": {
      ""command"": ""custom-calor"",
      ""args"": [""custom-arg""]
    }
  }
}";
        await File.WriteAllTextAsync(mcpJsonPath, existingConfig);

        var initializer = new GitHubCopilotInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(mcpJsonPath);

        // Should preserve the existing custom calor configuration
        Assert.Contains("custom-calor", content);
        Assert.Contains("custom-arg", content);
    }

    [Fact]
    public async Task Initialize_WithForce_OverwritesInvalidJson()
    {
        // Pre-create invalid .vscode/mcp.json
        var vscodeDir = Path.Combine(_testDir, ".vscode");
        Directory.CreateDirectory(vscodeDir);
        var mcpJsonPath = Path.Combine(vscodeDir, "mcp.json");
        await File.WriteAllTextAsync(mcpJsonPath, "{ invalid json }");

        var initializer = new GitHubCopilotInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: true);

        Assert.True(result.Success);

        var content = await File.ReadAllTextAsync(mcpJsonPath);
        Assert.Contains("servers", content);
        Assert.Contains("\"calor\"", content);
    }

    [Fact]
    public async Task Initialize_WithoutForce_LeavesInvalidJsonUnchanged()
    {
        // Pre-create invalid .vscode/mcp.json
        var vscodeDir = Path.Combine(_testDir, ".vscode");
        Directory.CreateDirectory(vscodeDir);
        var mcpJsonPath = Path.Combine(vscodeDir, "mcp.json");
        var invalidJson = "{ invalid json }";
        await File.WriteAllTextAsync(mcpJsonPath, invalidJson);

        var initializer = new GitHubCopilotInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(mcpJsonPath);
        Assert.Equal(invalidJson, content);
    }

    [Fact]
    public async Task Initialize_PreservesExtensionData()
    {
        // Pre-create .vscode/mcp.json with extension data (e.g., "inputs" array)
        var vscodeDir = Path.Combine(_testDir, ".vscode");
        Directory.CreateDirectory(vscodeDir);
        var mcpJsonPath = Path.Combine(vscodeDir, "mcp.json");
        var existingConfig = @"{
  ""inputs"": [
    {
      ""type"": ""promptString"",
      ""id"": ""api-key"",
      ""description"": ""API Key""
    }
  ],
  ""servers"": {}
}";
        await File.WriteAllTextAsync(mcpJsonPath, existingConfig);

        var initializer = new GitHubCopilotInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(mcpJsonPath);

        // Extension data should be preserved
        Assert.Contains("inputs", content);
        Assert.Contains("promptString", content);
        Assert.Contains("api-key", content);
        // Calor server should be added
        Assert.Contains("\"calor\"", content);
    }

    [Fact]
    public async Task Initialize_MessagesMentionMcpServer()
    {
        var initializer = new GitHubCopilotInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        Assert.True(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("MCP server"));
    }

    [Fact]
    public async Task Initialize_McpJsonPath_InCreatedFiles()
    {
        var initializer = new GitHubCopilotInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        Assert.True(result.Success);
        var mcpJsonPath = Path.Combine(_testDir, ".vscode", "mcp.json");
        Assert.Contains(mcpJsonPath, result.CreatedFiles);
    }

    [Fact]
    public async Task Initialize_WithEmptyServersObject_AddsCalor()
    {
        // Pre-create .vscode/mcp.json with empty servers object
        var vscodeDir = Path.Combine(_testDir, ".vscode");
        Directory.CreateDirectory(vscodeDir);
        var mcpJsonPath = Path.Combine(vscodeDir, "mcp.json");
        var existingConfig = @"{
  ""servers"": {}
}";
        await File.WriteAllTextAsync(mcpJsonPath, existingConfig);

        var initializer = new GitHubCopilotInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(mcpJsonPath);
        Assert.Contains("\"calor\"", content);
    }

    [Fact]
    public async Task Initialize_WarningMessages_NoLongerSuggestSwitchingAgent()
    {
        var initializer = new GitHubCopilotInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        var allMessages = string.Join("\n", result.Messages);

        // Old warning should be gone
        Assert.DoesNotContain("cannot enforce Calor-first development", allMessages);
        Assert.DoesNotContain("For best results, use Claude Code", allMessages);

        // New messaging should be present
        Assert.Contains("MCP server", allMessages);
        Assert.Contains("guidance-based", allMessages);
    }
}
