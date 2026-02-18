using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class CodexInitializerMcpTests : IDisposable
{
    private readonly string _testDir;

    public CodexInitializerMcpTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"calor-codex-mcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    // --- Basic functionality ---

    [Fact]
    public async Task Initialize_CreatesConfigToml_WithMcpServer()
    {
        var initializer = new CodexInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Assert.True(File.Exists(configPath), "config.toml should be created");
        Assert.Contains(configPath, result.CreatedFiles);

        var content = await File.ReadAllTextAsync(configPath);
        Assert.Contains("[mcp_servers.calor]", content);
        Assert.Contains("command = \"calor\"", content);
        Assert.Contains("args = [\"mcp\", \"--stdio\"]", content);
    }

    [Fact]
    public async Task Initialize_ConfigToml_HasSectionMarkers()
    {
        var initializer = new CodexInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        var content = await File.ReadAllTextAsync(configPath);

        Assert.Contains("# BEGIN CalorC MCP SECTION - DO NOT EDIT", content);
        Assert.Contains("# END CalorC MCP SECTION", content);
    }

    [Fact]
    public async Task Initialize_ConfigToml_HasValidTomlSyntax()
    {
        var initializer = new CodexInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        var content = await File.ReadAllTextAsync(configPath);

        // Values should be properly quoted
        Assert.Contains("command = \"calor\"", content);
        // Array should use brackets
        Assert.Contains("args = [\"mcp\", \"--stdio\"]", content);
        // Table header should use dot notation
        Assert.Contains("[mcp_servers.calor]", content);
    }

    // --- Idempotency ---

    [Fact]
    public async Task Initialize_DoesNotDuplicateMcpServer_WhenAlreadyConfigured()
    {
        var initializer = new CodexInitializer();

        await initializer.InitializeAsync(_testDir, force: false);
        await initializer.InitializeAsync(_testDir, force: false);

        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        var content = await File.ReadAllTextAsync(configPath);

        var count = content.Split("[mcp_servers.calor]").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Initialize_RunsMultipleTimesIdempotently()
    {
        var initializer = new CodexInitializer();
        var configPath = Path.Combine(_testDir, ".codex", "config.toml");

        await initializer.InitializeAsync(_testDir, force: false);
        var contentAfterFirst = await File.ReadAllTextAsync(configPath);

        await initializer.InitializeAsync(_testDir, force: false);
        var contentAfterSecond = await File.ReadAllTextAsync(configPath);

        await initializer.InitializeAsync(_testDir, force: false);
        var contentAfterThird = await File.ReadAllTextAsync(configPath);

        Assert.Equal(contentAfterFirst, contentAfterSecond);
        Assert.Equal(contentAfterSecond, contentAfterThird);
    }

    [Fact]
    public async Task Initialize_ConfigToml_NotInCreatedOrUpdated_WhenUnchanged()
    {
        var initializer = new CodexInitializer();
        var configPath = Path.Combine(_testDir, ".codex", "config.toml");

        // First run creates it
        await initializer.InitializeAsync(_testDir, force: false);

        // Second run should not report it as created or updated
        var result = await initializer.InitializeAsync(_testDir, force: false);

        Assert.DoesNotContain(configPath, result.CreatedFiles);
        Assert.DoesNotContain(configPath, result.UpdatedFiles);
    }

    // --- Preserving existing content ---

    [Fact]
    public async Task Initialize_PreservesExistingConfigToml_Content()
    {
        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var existingContent = "model = \"o3\"\nprovider = \"openai\"\n";
        await File.WriteAllTextAsync(configPath, existingContent);

        var initializer = new CodexInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(configPath);

        Assert.Contains("model = \"o3\"", content);
        Assert.Contains("provider = \"openai\"", content);
        Assert.Contains("[mcp_servers.calor]", content);
    }

    [Fact]
    public async Task Initialize_PreservesUserTomlBeforeAndAfterMarkers()
    {
        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var existingContent = """
            model = "o3"

            # BEGIN CalorC MCP SECTION - DO NOT EDIT
            [mcp_servers.calor]
            command = "old-calor"
            args = ["old-arg"]
            # END CalorC MCP SECTION

            [some_other_section]
            key = "value"
            """;
        await File.WriteAllTextAsync(configPath, existingContent);

        var initializer = new CodexInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(configPath);

        // Before markers preserved
        Assert.Contains("model = \"o3\"", content);
        // After markers preserved
        Assert.Contains("[some_other_section]", content);
        Assert.Contains("key = \"value\"", content);
        // Old content replaced
        Assert.DoesNotContain("old-calor", content);
        Assert.DoesNotContain("old-arg", content);
        // New content present
        Assert.Contains("command = \"calor\"", content);
        Assert.Contains("args = [\"mcp\", \"--stdio\"]", content);
    }

    [Fact]
    public async Task Initialize_AppendsToExistingConfigWithoutMarkers()
    {
        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var existingContent = "model = \"o3\"\nprovider = \"openai\"\n";
        await File.WriteAllTextAsync(configPath, existingContent);

        var initializer = new CodexInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(configPath);

        // Original content preserved at the top
        Assert.Contains("model = \"o3\"", content);
        Assert.Contains("provider = \"openai\"", content);

        // MCP section appended
        Assert.Contains("# BEGIN CalorC MCP SECTION - DO NOT EDIT", content);
        Assert.Contains("[mcp_servers.calor]", content);

        // MCP section comes after original content
        var originalIdx = content.IndexOf("provider = \"openai\"");
        var mcpIdx = content.IndexOf("# BEGIN CalorC MCP SECTION");
        Assert.True(mcpIdx > originalIdx);
    }

    // --- Result tracking ---

    [Fact]
    public async Task Initialize_ConfigToml_InCreatedFiles_WhenNew()
    {
        var initializer = new CodexInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Assert.Contains(configPath, result.CreatedFiles);
    }

    [Fact]
    public async Task Initialize_ConfigToml_InUpdatedFiles_WhenModified()
    {
        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        // Pre-create without MCP section
        await File.WriteAllTextAsync(configPath, "model = \"o3\"\n");

        var initializer = new CodexInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        Assert.Contains(configPath, result.UpdatedFiles);
    }

    [Fact]
    public async Task Initialize_MessagesMentionMcpServer()
    {
        var initializer = new CodexInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        Assert.True(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("MCP server"));
    }

    [Fact]
    public async Task Initialize_DoesNotShowOldWarning()
    {
        var initializer = new CodexInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        Assert.DoesNotContain(result.Messages, m => m.Contains("WARNING: OpenAI Codex cannot enforce"));
    }

    // --- Edge cases ---

    [Fact]
    public async Task Initialize_CreatesConfigToml_EvenIfSkillsExist()
    {
        // Pre-create skills directory without config.toml
        var skillDir = Path.Combine(_testDir, ".codex", "skills", "calor");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), "custom skill");

        var initializer = new CodexInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Assert.True(File.Exists(configPath));
        Assert.Contains(configPath, result.CreatedFiles);
    }

    [Fact]
    public async Task Initialize_HandlesEmptyConfigToml()
    {
        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        // Pre-create empty config.toml
        await File.WriteAllTextAsync(configPath, "");

        var initializer = new CodexInitializer();
        var result = await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(configPath);
        Assert.Contains("[mcp_servers.calor]", content);
        Assert.Contains(configPath, result.UpdatedFiles);
    }

    [Fact]
    public async Task Initialize_ReplacesStaleSection_WithUpdatedContent()
    {
        var configPath = Path.Combine(_testDir, ".codex", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        // Pre-create with stale/wrong MCP section
        var staleContent = """
            # BEGIN CalorC MCP SECTION - DO NOT EDIT
            [mcp_servers.calor]
            command = "wrong-command"
            args = ["wrong-arg"]
            # END CalorC MCP SECTION
            """;
        await File.WriteAllTextAsync(configPath, staleContent);

        var initializer = new CodexInitializer();
        await initializer.InitializeAsync(_testDir, force: false);

        var content = await File.ReadAllTextAsync(configPath);

        // Old values replaced
        Assert.DoesNotContain("wrong-command", content);
        Assert.DoesNotContain("wrong-arg", content);

        // Correct values present
        Assert.Contains("command = \"calor\"", content);
        Assert.Contains("args = [\"mcp\", \"--stdio\"]", content);
    }
}
