using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class CalorConfigTests : IDisposable
{
    private readonly string _testDirectory;

    public CalorConfigTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
    }

    // === CalorConfig Serialization ===

    [Fact]
    public void Config_Serialize_RoundTrips()
    {
        var config = new CalorConfig
        {
            Agents = new List<AgentEntry>
            {
                new() { Name = "claude", AddedAt = "2026-01-01T00:00:00Z" },
                new() { Name = "github", AddedAt = "2026-01-02T00:00:00Z" }
            },
            CreatedAt = "2026-01-01T00:00:00Z"
        };

        var json = config.Serialize();
        var deserialized = CalorConfig.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Version);
        Assert.Equal(2, deserialized.Agents.Count);
        Assert.Equal("claude", deserialized.Agents[0].Name);
        Assert.Equal("github", deserialized.Agents[1].Name);
    }

    [Fact]
    public void Config_Deserialize_MalformedJson_ReturnsNull()
    {
        var result = CalorConfig.Deserialize("not valid json {{{");
        Assert.Null(result);
    }

    [Fact]
    public void Config_Deserialize_EmptyJson_ReturnsDefaults()
    {
        var result = CalorConfig.Deserialize("{}");
        Assert.NotNull(result);
        Assert.Empty(result.Agents);
    }

    // === CalorConfigManager.EnsureExists ===

    [Fact]
    public void EnsureExists_CreatesConfigFile()
    {
        var created = CalorConfigManager.EnsureExists(_testDirectory);

        Assert.True(created);
        Assert.True(File.Exists(CalorConfigManager.GetConfigPath(_testDirectory)));

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Equal(1, config.Version);
        Assert.Empty(config.Agents);
    }

    [Fact]
    public void EnsureExists_DoesNotOverwriteExisting()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, false);
        var created = CalorConfigManager.EnsureExists(_testDirectory);

        Assert.False(created);
        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Single(config.Agents);
        Assert.Equal("claude", config.Agents[0].Name);
    }

    // === CalorConfigManager.AddAgents ===

    [Fact]
    public void AddAgents_CreatesConfigWithAgent()
    {
        var isNew = CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, false);

        Assert.True(isNew);
        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Single(config.Agents);
        Assert.Equal("claude", config.Agents[0].Name);
        Assert.NotEmpty(config.Agents[0].AddedAt);
    }

    [Fact]
    public void AddAgents_AppendsNewAgent()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, false);
        CalorConfigManager.AddAgents(_testDirectory, new[] { "github" }, false);

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Equal(2, config.Agents.Count);
        Assert.Equal("claude", config.Agents[0].Name);
        Assert.Equal("github", config.Agents[1].Name);
    }

    [Fact]
    public void AddAgents_DeduplicatesByName()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, false);
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, false);

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Single(config.Agents);
    }

    [Fact]
    public void AddAgents_DeduplicatesCaseInsensitive()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "Claude" }, false);
        CalorConfigManager.AddAgents(_testDirectory, new[] { "CLAUDE" }, false);

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Single(config.Agents);
        Assert.Equal("claude", config.Agents[0].Name);
    }

    [Fact]
    public void AddAgents_MultipleAtOnce()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude", "gemini", "github" }, false);

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Equal(3, config.Agents.Count);
        Assert.Equal("claude", config.Agents[0].Name);
        Assert.Equal("gemini", config.Agents[1].Name);
        Assert.Equal("github", config.Agents[2].Name);
    }

    [Fact]
    public void AddAgents_ForceReplacesAgentsList()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude", "gemini" }, false);
        CalorConfigManager.AddAgents(_testDirectory, new[] { "github" }, force: true);

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Single(config.Agents);
        Assert.Equal("github", config.Agents[0].Name);
    }

    [Fact]
    public void AddAgents_EmptyList_CreatesConfigWithNoAgents()
    {
        var isNew = CalorConfigManager.AddAgents(_testDirectory, Array.Empty<string>(), false);

        Assert.True(isNew);
        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Empty(config.Agents);
    }

    [Fact]
    public void AddAgents_EmptyListWithForce_DoesNotClearAgents()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, false);
        CalorConfigManager.AddAgents(_testDirectory, Array.Empty<string>(), force: true);

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Single(config.Agents);
        Assert.Equal("claude", config.Agents[0].Name);
    }

    // === CalorConfigManager.Read (malformed) ===

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        var config = CalorConfigManager.Read(_testDirectory);
        Assert.Null(config);
    }

    [Fact]
    public void Read_MalformedJson_ReturnsEmptyConfig()
    {
        var configDir = Path.Combine(_testDirectory, CalorConfigManager.ConfigDirectory);
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, CalorConfigManager.ConfigFileName), "{{not json}}}");

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Empty(config.Agents);
    }

    [Fact]
    public void Read_EmptyFile_ReturnsEmptyConfig()
    {
        var configDir = Path.Combine(_testDirectory, CalorConfigManager.ConfigDirectory);
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, CalorConfigManager.ConfigFileName), "");

        var config = CalorConfigManager.Read(_testDirectory);
        Assert.NotNull(config);
        Assert.Empty(config.Agents);
    }

    // === CalorConfigManager.Discover (walk-up) ===

    [Fact]
    public void Discover_FindsConfigInCurrentDirectory()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, false);

        var result = CalorConfigManager.Discover(_testDirectory);
        Assert.NotNull(result);
        Assert.Single(result.Value.Config.Agents);
        Assert.Equal("claude", result.Value.Config.Agents[0].Name);
        Assert.Equal(_testDirectory, result.Value.Directory);
    }

    [Fact]
    public void Discover_FindsConfigInParentDirectory()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "github" }, false);

        var childDir = Path.Combine(_testDirectory, "src", "project");
        Directory.CreateDirectory(childDir);

        var result = CalorConfigManager.Discover(childDir);
        Assert.NotNull(result);
        Assert.Single(result.Value.Config.Agents);
        Assert.Equal("github", result.Value.Config.Agents[0].Name);
        Assert.Equal(_testDirectory, result.Value.Directory);
    }

    [Fact]
    public void Discover_FindsConfigFromFilePath()
    {
        CalorConfigManager.AddAgents(_testDirectory, new[] { "gemini" }, false);

        var childDir = Path.Combine(_testDirectory, "src");
        Directory.CreateDirectory(childDir);
        var filePath = Path.Combine(childDir, "test.calr");
        File.WriteAllText(filePath, "// test");

        var result = CalorConfigManager.Discover(filePath);
        Assert.NotNull(result);
        Assert.Equal("gemini", result.Value.Config.Agents[0].Name);
    }

    [Fact]
    public void Discover_ReturnsNullWhenNotFound()
    {
        var result = CalorConfigManager.Discover(_testDirectory);
        Assert.Null(result);
    }

    [Fact]
    public void Discover_FindsNearestConfig()
    {
        // Root config with "claude"
        CalorConfigManager.AddAgents(_testDirectory, new[] { "claude" }, false);

        // Nested config with "github"
        var nestedDir = Path.Combine(_testDirectory, "sub");
        Directory.CreateDirectory(nestedDir);
        CalorConfigManager.AddAgents(nestedDir, new[] { "github" }, false);

        // Should find the nearest (nested) config
        var result = CalorConfigManager.Discover(nestedDir);
        Assert.NotNull(result);
        Assert.Single(result.Value.Config.Agents);
        Assert.Equal("github", result.Value.Config.Agents[0].Name);
        Assert.Equal(nestedDir, result.Value.Directory);
    }

    // === CalorConfigManager.GetAgentString ===

    [Fact]
    public void GetAgentString_NullConfig_ReturnsNone()
    {
        Assert.Equal("none", CalorConfigManager.GetAgentString(null));
    }

    [Fact]
    public void GetAgentString_EmptyAgents_ReturnsNone()
    {
        Assert.Equal("none", CalorConfigManager.GetAgentString(new CalorConfig()));
    }

    [Fact]
    public void GetAgentString_SingleAgent_ReturnsName()
    {
        var config = new CalorConfig
        {
            Agents = new List<AgentEntry> { new() { Name = "claude" } }
        };
        Assert.Equal("claude", CalorConfigManager.GetAgentString(config));
    }

    [Fact]
    public void GetAgentString_MultipleAgents_ReturnsCommaSeparated()
    {
        var config = new CalorConfig
        {
            Agents = new List<AgentEntry>
            {
                new() { Name = "claude" },
                new() { Name = "github" }
            }
        };
        Assert.Equal("claude,github", CalorConfigManager.GetAgentString(config));
    }
}
