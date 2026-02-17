using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class CompileToolTests
{
    private readonly CompileTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorCompile()
    {
        Assert.Equal("calor_compile", _tool.Name);
    }

    [Fact]
    public void Description_ContainsCompileInfo()
    {
        Assert.Contains("Compile", _tool.Description);
        Assert.Contains("Calor", _tool.Description);
        Assert.Contains("C#", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithValidSource_ReturnsSuccess()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        Assert.NotEmpty(result.Content);

        var text = result.Content[0].Text;
        Assert.NotNull(text);
        Assert.Contains("success", text);
        Assert.Contains("true", text.ToLower());
        Assert.Contains("generatedCode", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidSource_ReturnsErrors()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Bad} invalid syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        Assert.Contains("diagnostics", text);
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
    public async Task ExecuteAsync_WithNullArguments_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilePath_UsesInDiagnostics()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "invalid §§§",
                "filePath": "test-file.calr"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // The diagnostics should be present (errors or warnings)
        var text = result.Content[0].Text!;
        Assert.Contains("diagnostics", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithContractModeOff_Compiles()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Div:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§Q (!= b 0)\n§R (/ a b)\n§/F{f001}\n§/M{m001}",
                "options": {
                    "contractMode": "off"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithContractModeRelease_Compiles()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Div:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§Q (!= b 0)\n§R (/ a b)\n§/F{f001}\n§/M{m001}",
                "options": {
                    "contractMode": "release"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
    }
}
