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
    public async Task ExecuteAsync_CheckCompat_InvalidSource_ReturnsCompilationError()
    {
        // Arrange: Invalid Calor syntax
        var args = JsonDocument.Parse("{\"source\": \"this is not valid calor code\", \"checkCompat\": true}").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args);

        // Assert
        Assert.True(result.IsError);
    }
}
