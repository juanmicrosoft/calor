using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class TypeCheckToolTests
{
    private readonly CheckTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorTypecheck()
    {
        Assert.Equal("calor_check", _tool.Name);
    }

    [Fact]
    public void Description_ContainsTypeCheckInfo()
    {
        Assert.Contains("checks", _tool.Description.ToLower());
        Assert.Contains("Calor", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("filePath", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"action": "typecheck"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("source", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithNullArguments_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsStructuredOutput()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "typecheck",
                "source": "§M{Test}\n§F{Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n\n"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        Assert.True(json.RootElement.TryGetProperty("success", out _));
        Assert.True(json.RootElement.TryGetProperty("errorCount", out _));
        Assert.True(json.RootElement.TryGetProperty("warningCount", out _));
        Assert.True(json.RootElement.TryGetProperty("typeErrors", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithSyntaxError_ReportsError()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "typecheck",
                "source": "§M{Test}\n§F{Bad} invalid syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("typeErrors", text);
    }

    [Fact]
    public async Task ExecuteAsync_ErrorsIncludeLineAndColumn()
    {
        // Source with error on specific line
        var args = JsonDocument.Parse("""
            {
                "action": "typecheck",
                "source": "§M{Test}\n§F{Test:pub}\n§O{i32}\n§R undefinedVar\n\n"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        var typeErrors = json.RootElement.GetProperty("typeErrors");
        Assert.True(typeErrors.GetArrayLength() > 0);

        // Envelope schema v1.1: line/column live under location
        var firstError = typeErrors[0];
        Assert.True(firstError.TryGetProperty("location", out var location));
        Assert.True(location.TryGetProperty("line", out var line));
        Assert.True(location.TryGetProperty("column", out var column));
        Assert.True(line.GetInt32() > 0, "Line should be positive");
        Assert.True(column.GetInt32() >= 0, "Column should be non-negative");
    }
}
