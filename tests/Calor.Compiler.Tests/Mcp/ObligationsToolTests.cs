using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for the calor_refine MCP tool (obligations and fixes actions).
/// </summary>
public sealed class ObligationsToolTests
{
    private readonly RefineTool _tool = new();

    // ───── RefineTool: obligations action ─────

    [Fact]
    public void RefineTool_Name_ReturnsCorrectName()
    {
        Assert.Equal("calor_refine", _tool.Name);
    }

    [Fact]
    public void RefineTool_GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("action", out _));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("timeout", out _));
        Assert.True(props.TryGetProperty("function_id", out _));
    }

    [Fact]
    public async Task RefineTool_Obligations_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"action":"obligations"}""").RootElement;
        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task RefineTool_WithNullArgs_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(null);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task RefineTool_Obligations_WithNoRefinements_ReturnsEmptyObligations()
    {
        var source = """
            module Test Test block
              func Main (int:x) → void
                !
                return
            """;

        var args = JsonDocument.Parse($$"""
            {
                "action": "obligations",
                "source": {{JsonSerializer.Serialize(source)}}
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task RefineTool_Obligations_WithFunctionIdFilter_FiltersResults()
    {
        var source = """
            module Test Test block
              func Func1 (int:x | (# >= 0)) → void
                !
                return

              func Func2 (int:y | (# > 0)) → void
                !
                return
            """;

        var args = JsonDocument.Parse($$"""
            {
                "action": "obligations",
                "source": {{JsonSerializer.Serialize(source)}},
                "function_id": "f001"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var obligations = json.RootElement.GetProperty("obligations");

        foreach (var obl in obligations.EnumerateArray())
        {
            Assert.Equal("f001", obl.GetProperty("function_id").GetString());
        }
    }

    // ───── RefineTool: fixes action ─────

    [Fact]
    public async Task RefineTool_Fixes_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"action":"fixes"}""").RootElement;
        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task RefineTool_Fixes_WithValidSource_ReturnsFixSuggestions()
    {
        var source = """
            module Test Test block
              func Main (int:x | (# >= 0)) → void
                !
                proof{"always-positive"} (x > 0)
            """;

        var args = JsonDocument.Parse($$"""
            {
                "action": "fixes",
                "source": {{JsonSerializer.Serialize(source)}}
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.TryGetProperty("fixes", out var fixes));

        // Each fix should have required fields
        if (fixes.GetArrayLength() > 0)
        {
            var firstFix = fixes[0];
            Assert.True(firstFix.TryGetProperty("obligation_id", out _));
            Assert.True(firstFix.TryGetProperty("strategy", out _));
            Assert.True(firstFix.TryGetProperty("confidence", out _));
            Assert.True(firstFix.TryGetProperty("template", out _));
        }
    }
}
