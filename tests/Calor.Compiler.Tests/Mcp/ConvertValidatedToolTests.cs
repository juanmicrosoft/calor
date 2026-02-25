using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class ConvertValidatedToolTests
{
    private readonly ConvertValidatedTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorConvertValidated()
    {
        Assert.Equal("calor_convert_validated", _tool.Name);
    }

    [Fact]
    public void Description_ContainsPipelineInfo()
    {
        Assert.Contains("pipeline", _tool.Description);
        Assert.Contains("convert", _tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("expectedNamespace", out _));
        Assert.True(props.TryGetProperty("expectedPatterns", out _));
        Assert.True(props.TryGetProperty("forbiddenPatterns", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;
        var result = await _tool.ExecuteAsync(args);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_WithSimpleClass_ReturnsComplete()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calculator { public int Add(int a, int b) => a + b; }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Tool returned error: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("complete", root.GetProperty("stage").GetString());
        Assert.True(root.TryGetProperty("calorSource", out var calor));
        Assert.False(string.IsNullOrWhiteSpace(calor.GetString()));
        Assert.True(root.TryGetProperty("generatedCSharp", out var csharp));
        Assert.False(string.IsNullOrWhiteSpace(csharp.GetString()));
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidCSharp_ReturnsConversionStage()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class { invalid"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // The converter may still produce partial output; check that stage reflects the issue
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;
        var stage = root.GetProperty("stage").GetString();

        // Could fail at conversion, parse, or diagnose — but shouldn't be "complete"
        Assert.NotEqual("complete", stage);
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedPatterns_ChecksCompat()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calculator { public int Add(int a, int b) => a + b; }",
                "expectedPatterns": ["class Calculator"]
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Tool returned error: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("complete", root.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithForbiddenPattern_ReturnsCompatStage()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calculator { public int Add(int a, int b) => a + b; }",
                "forbiddenPatterns": ["class Calculator"]
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("compat", root.GetProperty("stage").GetString());
        var compatIssues = root.GetProperty("compatIssues");
        Assert.True(compatIssues.GetArrayLength() > 0);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsStats()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Foo { public int Bar() => 42; }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Tool returned error: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("stats", out var stats));
        Assert.True(stats.TryGetProperty("classesConverted", out _));
        Assert.True(root.TryGetProperty("durationMs", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithInteropMode_Succeeds()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Foo { public int Bar() => 42; }",
                "mode": "interop"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Tool returned error: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
    }
}
