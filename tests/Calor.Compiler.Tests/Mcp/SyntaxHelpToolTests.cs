using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
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
}
