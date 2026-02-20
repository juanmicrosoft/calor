using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class ConvertToolTests
{
    private readonly ConvertTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorConvert()
    {
        Assert.Equal("calor_convert", _tool.Name);
    }

    [Fact]
    public void Description_ContainsConvertInfo()
    {
        Assert.Contains("Convert", _tool.Description);
        Assert.Contains("C#", _tool.Description);
        Assert.Contains("Calor", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("moduleName", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithSimpleClass_ReturnsCalorCode()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calculator { public int Add(int a, int b) => a + b; }",
                "moduleName": "TestModule"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
        Assert.Contains("calorSource", text);
        Assert.Contains("TestModule", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutModuleName_DerivesFromSource()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "namespace MyNamespace { public class Test { } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidCSharp_ReturnsErrors()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class { invalid syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("issues", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("source", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsStats()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Test { public int Value { get; set; } public void DoSomething() { } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        Assert.Contains("stats", text);
        Assert.Contains("classesConverted", text);
        Assert.Contains("methodsConverted", text);
        Assert.Contains("propertiesConverted", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithInterface_ReportsParseValidationIssues()
    {
        // Interface conversion generates §SIG tags that the parser doesn't yet recognize.
        // Post-conversion validation (R5) now correctly reports this instead of false success.
        var args = JsonDocument.Parse("""
            {
                "source": "public interface IService { void Process(); string GetValue(); }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("issues", text);
        Assert.Contains("Generated Calor failed to parse", text);
        Assert.Contains("interfacesConverted", text);
    }
}
