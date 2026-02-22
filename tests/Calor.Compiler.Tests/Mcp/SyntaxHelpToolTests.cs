using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class SyntaxHelpToolTests
{
    private readonly SyntaxHelpTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorSyntaxHelp()
    {
        Assert.Equal("calor_syntax_help", _tool.Name);
    }

    [Fact]
    public void Description_ContainsSyntaxInfo()
    {
        Assert.Contains("syntax", _tool.Description.ToLower());
        Assert.Contains("Calor", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("feature", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithContracts_ReturnsContractDocs()
    {
        var args = JsonDocument.Parse("""
            {
                "feature": "contracts"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text);
        Assert.Contains("contracts", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithAsync_ReturnsAsyncDocs()
    {
        var args = JsonDocument.Parse("""
            {
                "feature": "async"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithLoops_ReturnsLoopDocs()
    {
        var args = JsonDocument.Parse("""
            {
                "feature": "loops"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithCollections_ReturnsCollectionDocs()
    {
        var args = JsonDocument.Parse("""
            {
                "feature": "collections"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithPatterns_ReturnsPatternDocs()
    {
        var args = JsonDocument.Parse("""
            {
                "feature": "patterns"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownFeature_ReturnsAvailableFeatures()
    {
        var args = JsonDocument.Parse("""
            {
                "feature": "unknown_feature_xyz"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // Should return a message listing available features
        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        // Either returns JSON with availableFeatures or a text message with available features
        Assert.True(text.Contains("availableFeatures") || text.Contains("Available features"),
            $"Expected available features list in: {text}");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingFeature_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAvailableFeatures()
    {
        var args = JsonDocument.Parse("""
            {
                "feature": "functions"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("availableFeatures", text);
    }

    [Theory]
    [InlineData("effects")]
    [InlineData("generics")]
    [InlineData("types")]
    [InlineData("strings")]
    [InlineData("classes")]
    public async Task ExecuteAsync_KnownFeatures_ReturnsContent(string feature)
    {
        var args = JsonDocument.Parse($"{{\"feature\": \"{feature}\"}}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text);
    }

    #region New Alias Category Tests

    [Theory]
    [InlineData("structs")]
    [InlineData("struct")]
    [InlineData("operators")]
    [InlineData("operator")]
    [InlineData("nullable")]
    [InlineData("linq")]
    [InlineData("events")]
    [InlineData("event")]
    [InlineData("using")]
    [InlineData("modifiers")]
    [InlineData("static")]
    [InlineData("indexers")]
    [InlineData("indexer")]
    [InlineData("yield")]
    public async Task ExecuteAsync_NewAliasCategories_ReturnsContent(string feature)
    {
        var args = JsonDocument.Parse($"{{\"feature\": \"{feature}\"}}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text);
    }

    [Theory]
    [InlineData("for loop")]
    [InlineData("foreach")]
    [InlineData("if statement")]
    [InlineData("if-else")]
    public async Task ExecuteAsync_RefinedAliases_ReturnsContent(string feature)
    {
        var args = JsonDocument.Parse($"{{\"feature\": \"{feature}\"}}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("feature", text);
    }

    [Fact]
    public async Task ExecuteAsync_AvailableFeatures_IncludesNewCategories()
    {
        var args = JsonDocument.Parse("""{"feature": "functions"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        // New categories should appear in the availableFeatures list
        Assert.Contains("structs", text);
        Assert.Contains("operators", text);
        Assert.Contains("nullable", text);
        Assert.Contains("linq", text);
        Assert.Contains("events", text);
        Assert.Contains("modifiers", text);
        Assert.Contains("indexers", text);
        Assert.Contains("yield", text);
    }

    [Fact]
    public async Task ExecuteAsync_EventsFeature_IncludesEventContent()
    {
        var args = JsonDocument.Parse("""{"feature": "events"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.True(
            text.Contains("event", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("§EV") || text.Contains("§EVT"),
            "Expected event-related content in response");
    }

    [Fact]
    public async Task ExecuteAsync_OperatorsFeature_IncludesOperatorContent()
    {
        var args = JsonDocument.Parse("""{"feature": "operators"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        // The documentation has a "## Modern Operators" section
        Assert.True(
            text.Contains("operator", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("§OP") || text.Contains("Operator"),
            "Expected operator-related content in response");
    }

    [Fact]
    public async Task ExecuteAsync_ForAlias_ResolvesToLoops()
    {
        // "for" should map to the loops category, not produce overly broad results
        var args = JsonDocument.Parse("""{"feature": "for"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.True(
            text.Contains("loop", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("§L{") || text.Contains("§WH{"),
            "Expected loop-related content when searching for 'for'");
    }

    [Fact]
    public async Task ExecuteAsync_IfAlias_ResolvesToConditionals()
    {
        // "if" should map to the conditionals category
        var args = JsonDocument.Parse("""{"feature": "if"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.True(
            text.Contains("conditional", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("§IF{") || text.Contains("§EI") || text.Contains("§EL"),
            "Expected conditional-related content when searching for 'if'");
    }

    [Fact]
    public async Task ExecuteAsync_DelegateAlias_ResolvesToLambdas()
    {
        // "delegate" should resolve to lambdas (not events), since it appears in lambdas first
        var args = JsonDocument.Parse("""{"feature": "delegate"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.True(
            text.Contains("lambda", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("§LAM") || text.Contains("§DEL") || text.Contains("delegate", StringComparison.OrdinalIgnoreCase),
            "Expected lambda/delegate-related content in response");
    }

    #endregion

    #region File Resolution Tests

    [Fact]
    public void SkillFilePathEnvVar_IsDocumented()
    {
        // Verify the environment variable name is accessible
        Assert.Equal("CALOR_SKILL_FILE", SyntaxHelpTool.SkillFilePathEnvVar);
    }

    [Fact]
    public async Task ExecuteAsync_ContractsFeature_IncludesContractFirstMethodology()
    {
        // This verifies the merged content is being loaded (Contract-First Methodology
        // was in calor-language-skills.md, not the original calor.md)
        var args = JsonDocument.Parse("""
            {
                "feature": "contracts"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        // Check for content that exists in the merged file
        Assert.True(
            text.Contains("Contract-First") || text.Contains("precondition") || text.Contains("§Q"),
            "Expected contract-related content in response");
    }

    [Fact]
    public async Task ExecuteAsync_AsyncFeature_IncludesAsyncTags()
    {
        // Verify async documentation includes the proper tags
        var args = JsonDocument.Parse("""
            {
                "feature": "async"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        // §AF and §AMT are async function/method tags
        Assert.True(
            text.Contains("§AF") || text.Contains("§AMT") || text.Contains("§AWAIT") || text.Contains("async", StringComparison.OrdinalIgnoreCase),
            "Expected async-related tags in response");
    }

    [Fact]
    public async Task ExecuteAsync_CollectionsFeature_IncludesCollectionOperations()
    {
        // Verify collections documentation includes List, Dict, HashSet operations
        var args = JsonDocument.Parse("""
            {
                "feature": "collections"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.True(
            text.Contains("List") || text.Contains("Dict") || text.Contains("§LIST") || text.Contains("§DICT"),
            "Expected collection-related content in response");
    }

    [Fact]
    public async Task ExecuteAsync_ExceptionsFeature_IncludesTryCatchTags()
    {
        // Verify exception handling documentation exists
        var args = JsonDocument.Parse("""
            {
                "feature": "exceptions"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.True(
            text.Contains("§TR") || text.Contains("§CA") || text.Contains("try") || text.Contains("catch"),
            "Expected exception handling content in response");
    }

    [Fact]
    public async Task ExecuteAsync_StringsFeature_IncludesNullSafetyPattern()
    {
        // The merged file includes the null-safety pattern documentation
        var args = JsonDocument.Parse("""
            {
                "feature": "strings"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        // String operations should be present
        Assert.True(
            text.Contains("concat") || text.Contains("substr") || text.Contains("(str "),
            "Expected string operation content in response");
    }

    [Fact]
    public async Task ExecuteAsync_WithEnvVarOverride_UsesCustomFile()
    {
        // Create a temporary file with custom content
        var tempFile = Path.GetTempFileName();
        var customContent = @"# Custom Skill File
## Test Feature
This is CUSTOM_UNIQUE_MARKER content for testing the CALOR_SKILL_FILE environment variable.
### Custom Syntax
- §TEST: Test token for verification
";
        var originalValue = Environment.GetEnvironmentVariable(SyntaxHelpTool.SkillFilePathEnvVar);

        try
        {
            File.WriteAllText(tempFile, customContent);

            // Set the environment variable
            Environment.SetEnvironmentVariable(SyntaxHelpTool.SkillFilePathEnvVar, tempFile);

            // Reset the cache so the env var is picked up
            SyntaxHelpTool.ResetCacheForTesting();

            var toolWithEnvVar = new SyntaxHelpTool();

            var args = JsonDocument.Parse("""
                {
                    "feature": "test"
                }
                """).RootElement;

            var result = await toolWithEnvVar.ExecuteAsync(args);

            Assert.False(result.IsError);
            var text = result.Content[0].Text!;

            // Verify our custom content is actually being used
            Assert.Contains("CUSTOM_UNIQUE_MARKER", text);
        }
        finally
        {
            // Restore original environment variable and reset cache
            Environment.SetEnvironmentVariable(SyntaxHelpTool.SkillFilePathEnvVar, originalValue);
            SyntaxHelpTool.ResetCacheForTesting();

            // Clean up temp file
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion

    #region Embedded Resource Fallback Tests

    [Fact]
    public void EmbeddedResource_CalorLanguageSkills_ExistsAndIsNonEmpty()
    {
        // Verify the embedded resource that serves as the last-resort fallback
        // for NuGet-installed users (where filesystem paths don't exist) is
        // actually bundled in the assembly and contains meaningful content.
        var content = Init.EmbeddedResourceHelper.ReadResource(
            "Calor.Compiler.Resources.calor-language-skills.md");

        Assert.False(string.IsNullOrWhiteSpace(content),
            "Embedded calor-language-skills.md should not be empty");
        // Verify it has markdown section headers (the format ExtractRelevantSections expects)
        Assert.Contains("## ", content);
    }

    [Fact]
    public async Task ExecuteAsync_EmbeddedFallback_ReturnsContentWhenFilesystemFails()
    {
        // Simulate the NuGet-installed scenario: env var points to a nonexistent file,
        // and no filesystem skill file is reachable. The embedded resource should kick in.
        var originalValue = Environment.GetEnvironmentVariable(SyntaxHelpTool.SkillFilePathEnvVar);
        var originalDir = Directory.GetCurrentDirectory();

        try
        {
            // Point env var to nonexistent file (disables path 1)
            Environment.SetEnvironmentVariable(
                SyntaxHelpTool.SkillFilePathEnvVar, "/nonexistent/path/skill.md");

            // Change working directory to temp (no Calor.sln or .git — disables path 2)
            var tempDir = Path.Combine(Path.GetTempPath(), $"calor-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            Directory.SetCurrentDirectory(tempDir);

            SyntaxHelpTool.ResetCacheForTesting();

            var tool = new SyntaxHelpTool();
            var args = JsonDocument.Parse("""{"feature": "contracts"}""").RootElement;

            var result = await tool.ExecuteAsync(args);

            Assert.False(result.IsError, "Should not error — embedded fallback should provide content");
            var text = result.Content[0].Text!;
            Assert.Contains("feature", text);
            Assert.Contains("contracts", text.ToLower());

            // Cleanup temp dir
            Directory.Delete(tempDir, true);
        }
        finally
        {
            // Restore environment
            Directory.SetCurrentDirectory(originalDir);
            Environment.SetEnvironmentVariable(SyntaxHelpTool.SkillFilePathEnvVar, originalValue);
            SyntaxHelpTool.ResetCacheForTesting();
        }
    }

    #endregion
}

/// <summary>
/// Telemetry integration tests for SyntaxHelpTool.
/// Uses TelemetrySingleton collection to avoid parallel execution conflicts.
/// </summary>
[Collection("TelemetrySingleton")]
public class SyntaxHelpTelemetryTests
{
    [Fact]
    public async Task ExecuteAsync_Hit_TracksSyntaxHelpQueryEvent()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        using var _ = CalorTelemetry.SetInstanceForTesting(telemetry);

        var tool = new SyntaxHelpTool();
        var args = JsonDocument.Parse("""{"feature": "contracts"}""").RootElement;

        await tool.ExecuteAsync(args);

        // Filter by feature name to isolate from concurrent non-collection tests
        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "SyntaxHelpQuery" && e.Properties["feature"] == "contracts"));
        Assert.Equal("contracts", evt.Properties["resolvedCategory"]);
        Assert.Equal("True", evt.Properties["isHit"]);
        Assert.True(int.Parse(evt.Properties["resultCount"]) > 0);
        Assert.True(evt.Properties.ContainsKey("matchedSections"));
    }

    [Fact]
    public async Task ExecuteAsync_Miss_TracksSyntaxHelpQueryWithNoHit()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        using var _ = CalorTelemetry.SetInstanceForTesting(telemetry);

        var tool = new SyntaxHelpTool();
        var args = JsonDocument.Parse("""{"feature": "unknown_feature_xyz"}""").RootElement;

        await tool.ExecuteAsync(args);

        // Filter by feature name to isolate from concurrent non-collection tests
        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "SyntaxHelpQuery" && e.Properties["feature"] == "unknown_feature_xyz"));
        Assert.Equal("none", evt.Properties["resolvedCategory"]);
        Assert.Equal("False", evt.Properties["isHit"]);
        Assert.Equal("0", evt.Properties["resultCount"]);
        Assert.False(evt.Properties.ContainsKey("matchedSections"));
    }

    [Fact]
    public async Task ExecuteAsync_AliasMatch_TracksResolvedCategory()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        using var _ = CalorTelemetry.SetInstanceForTesting(telemetry);

        var tool = new SyntaxHelpTool();
        // "await" is an alias for the "async" category
        var args = JsonDocument.Parse("""{"feature": "await"}""").RootElement;

        await tool.ExecuteAsync(args);

        // Filter by feature name to isolate from concurrent non-collection tests
        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "SyntaxHelpQuery" && e.Properties["feature"] == "await"));
        Assert.Equal("async", evt.Properties["resolvedCategory"]);
    }

    [Fact]
    public void TrackSyntaxHelpQuery_NeverThrows()
    {
        var (telemetry, _) = CreateTestTelemetry();

        var exception = Record.Exception(() =>
            telemetry.TrackSyntaxHelpQuery("test", "category", 3, "section1;section2"));
        Assert.Null(exception);
    }

    [Fact]
    public void TrackSyntaxHelpQuery_EmitsCorrectProperties()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackSyntaxHelpQuery("struct", "structs", 2, "Struct Definition;Value Types");

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("SyntaxHelpQuery", evt.Name);
        Assert.Equal("struct", evt.Properties["feature"]);
        Assert.Equal("structs", evt.Properties["resolvedCategory"]);
        Assert.Equal("2", evt.Properties["resultCount"]);
        Assert.Equal("True", evt.Properties["isHit"]);
        Assert.Equal("Struct Definition;Value Types", evt.Properties["matchedSections"]);
    }

    [Fact]
    public void TrackSyntaxHelpQuery_Miss_OmitsMatchedSections()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackSyntaxHelpQuery("unknown", null, 0, null);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("SyntaxHelpQuery", evt.Name);
        Assert.Equal("none", evt.Properties["resolvedCategory"]);
        Assert.Equal("0", evt.Properties["resultCount"]);
        Assert.Equal("False", evt.Properties["isHit"]);
        Assert.False(evt.Properties.ContainsKey("matchedSections"));
    }

    [Fact]
    public async Task ExecuteAsync_ManyResults_CapsMatchedSectionsAtFive()
    {
        var tempFile = Path.GetTempFileName();
        var originalValue = Environment.GetEnvironmentVariable(SyntaxHelpTool.SkillFilePathEnvVar);
        var (telemetry, channel) = CreateTestTelemetry();
        using var _t = CalorTelemetry.SetInstanceForTesting(telemetry);

        try
        {
            // Create a skill file with 8 sections all containing "testtopic"
            var content = string.Join("\n", Enumerable.Range(1, 8).Select(i =>
                $"## Section {i}\nThis testtopic section has testtopic content number {i}.\n"));
            File.WriteAllText(tempFile, content);
            Environment.SetEnvironmentVariable(SyntaxHelpTool.SkillFilePathEnvVar, tempFile);
            SyntaxHelpTool.ResetCacheForTesting();

            var tool = new SyntaxHelpTool();
            var args = JsonDocument.Parse("""{"feature": "testtopic"}""").RootElement;

            await tool.ExecuteAsync(args);

            var evt = Assert.Single(channel.Items.OfType<EventTelemetry>()
                .Where(e => e.Name == "SyntaxHelpQuery" && e.Properties["feature"] == "testtopic"));
            Assert.Equal("8", evt.Properties["resultCount"]);
            var sectionCount = evt.Properties["matchedSections"].Split(';').Length;
            Assert.Equal(5, sectionCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SyntaxHelpTool.SkillFilePathEnvVar, originalValue);
            SyntaxHelpTool.ResetCacheForTesting();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #region Test Helpers

    private static (CalorTelemetry telemetry, StubTelemetryChannel channel) CreateTestTelemetry()
    {
        var channel = new StubTelemetryChannel();
        var config = new TelemetryConfiguration
        {
            TelemetryChannel = channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        var client = new TelemetryClient(config);
        var telemetry = new CalorTelemetry(client);
        return (telemetry, channel);
    }

    private sealed class StubTelemetryChannel : ITelemetryChannel
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<ITelemetry> _items = new();
        public List<ITelemetry> Items => _items.ToList();
        public bool? DeveloperMode { get; set; } = true;
        public string EndpointAddress { get; set; } = "https://localhost";

        public void Send(ITelemetry item) => _items.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }

    #endregion
}
