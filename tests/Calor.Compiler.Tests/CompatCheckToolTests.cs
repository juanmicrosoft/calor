using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the CompileTool's checkCompat feature (formerly CompatCheckTool).
/// </summary>
public class CompatCheckToolTests
{
    private readonly CompileTool _tool = new();

    [Fact]
    public void GetInputSchema_ContainsCheckCompatProperty()
    {
        var schema = _tool.GetInputSchema();
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("checkCompat", out _));
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_MissingSource_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{\"checkCompat\": true}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_ValidSource_ReturnsCompatible()
    {
        // Arrange: Simple valid Calor source
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($"{{\"source\": \"{source}\", \"checkCompat\": true}}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        var compatCheck = parsed.RootElement.GetProperty("compatCheck");
        Assert.True(compatCheck.GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_ExpectedNamespace_ChecksNamespacePresence()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""checkCompat"": true,
            ""expectedNamespace"": ""TestNs""
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.True(parsed.RootElement.GetProperty("compatCheck").GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_WrongExpectedNamespace_ReturnsIncompatible()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""checkCompat"": true,
            ""expectedNamespace"": ""WrongNamespace""
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        var compatCheck = parsed.RootElement.GetProperty("compatCheck");
        Assert.False(compatCheck.GetProperty("compatible").GetBoolean());

        var issues = compatCheck.GetProperty("issues");
        Assert.True(issues.GetArrayLength() > 0);
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_ExpectedPattern_FoundInOutput()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:TestFunction:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""checkCompat"": true,
            ""expectedPatterns"": [""TestFunction""]
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.True(parsed.RootElement.GetProperty("compatCheck").GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_ExpectedPattern_NotFound_ReturnsIssue()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""checkCompat"": true,
            ""expectedPatterns"": [""NonExistentPattern""]
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.False(parsed.RootElement.GetProperty("compatCheck").GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_ForbiddenPattern_Found_ReturnsIssue()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""checkCompat"": true,
            ""forbiddenPatterns"": [""namespace TestNs""]
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        var compatCheck = parsed.RootElement.GetProperty("compatCheck");
        Assert.False(compatCheck.GetProperty("compatible").GetBoolean());

        var issues = compatCheck.GetProperty("issues");
        Assert.True(issues.GetArrayLength() > 0);
        Assert.Contains("Forbidden pattern", issues[0].GetString());
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_ForbiddenPattern_NotFound_ReturnsCompatible()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""checkCompat"": true,
            ""forbiddenPatterns"": [""SomePatternNotInCode""]
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.True(parsed.RootElement.GetProperty("compatCheck").GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_InvalidSource_ReturnsCompilationError()
    {
        // Arrange: Invalid Calor syntax
        var args = JsonDocument.Parse("{\"source\": \"this is not valid calor code\", \"checkCompat\": true}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_CheckCompat_IncludesGeneratedCode()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($"{{\"source\": \"{source}\", \"checkCompat\": true}}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.True(parsed.RootElement.TryGetProperty("generatedCode", out var generatedCode));
        Assert.NotEmpty(generatedCode.GetString()!);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutCheckCompat_NoCompatCheckInOutput()
    {
        // Arrange: Compile without checkCompat — output should not include compatCheck
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($"{{\"source\": \"{source}\"}}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.False(parsed.RootElement.TryGetProperty("compatCheck", out _));
    }
}
