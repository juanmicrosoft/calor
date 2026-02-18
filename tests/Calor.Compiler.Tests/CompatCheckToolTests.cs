using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the CompatCheckTool MCP tool.
/// </summary>
public class CompatCheckToolTests
{
    private readonly CompatCheckTool _tool = new();

    [Fact]
    public void Name_ReturnsCorrectName()
    {
        Assert.Equal("calor_compile_check_compat", _tool.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(_tool.Description));
        Assert.Contains("compatible", _tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidJson()
    {
        var schema = _tool.GetInputSchema();
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out _));
        Assert.True(schema.TryGetProperty("required", out _));
    }

    [Fact]
    public async Task ExecuteAsync_MissingSource_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_ValidSource_ReturnsCompatible()
    {
        // Arrange: Simple valid Calor source
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($"{{\"source\": \"{source}\"}}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);
        Assert.Contains("compatible", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ExpectedNamespace_ChecksNamespacePresence()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""expectedNamespace"": ""TestNs""
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.True(parsed.RootElement.GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_WrongExpectedNamespace_ReturnsIncompatible()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""expectedNamespace"": ""WrongNamespace""
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.False(parsed.RootElement.GetProperty("compatible").GetBoolean());

        var issues = parsed.RootElement.GetProperty("issues");
        Assert.True(issues.GetArrayLength() > 0);
    }

    [Fact]
    public async Task ExecuteAsync_ExpectedPattern_FoundInOutput()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:TestFunction:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""expectedPatterns"": [""TestFunction""]
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.True(parsed.RootElement.GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_ExpectedPattern_NotFound_ReturnsIssue()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""expectedPatterns"": [""NonExistentPattern""]
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.False(parsed.RootElement.GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_ForbiddenPattern_Found_ReturnsIssue()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""forbiddenPatterns"": [""namespace TestNs""]
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.False(parsed.RootElement.GetProperty("compatible").GetBoolean());

        var issues = parsed.RootElement.GetProperty("issues");
        Assert.True(issues.GetArrayLength() > 0);
        Assert.Contains("Forbidden pattern", issues[0].GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ForbiddenPattern_NotFound_ReturnsCompatible()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($@"{{
            ""source"": ""{source}"",
            ""forbiddenPatterns"": [""SomePatternNotInCode""]
        }}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.False(result.IsError, "Should not have error");
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.True(parsed.RootElement.GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidSource_ReturnsCompilationError()
    {
        // Arrange: Invalid Calor syntax
        var args = JsonDocument.Parse("{\"source\": \"this is not valid calor code\"}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesGeneratedCode()
    {
        // Arrange
        var source = "§M{m1:TestNs}\\n§F{f1:Test:pub}\\n§O{void}\\n§/F{f1}\\n§/M{m1}";
        var args = JsonDocument.Parse($"{{\"source\": \"{source}\"}}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        var content = result.Content.FirstOrDefault()?.Text;
        Assert.NotNull(content);

        var parsed = JsonDocument.Parse(content);
        Assert.True(parsed.RootElement.TryGetProperty("generatedCode", out var generatedCode));
        Assert.NotEmpty(generatedCode.GetString()!);
    }
}
