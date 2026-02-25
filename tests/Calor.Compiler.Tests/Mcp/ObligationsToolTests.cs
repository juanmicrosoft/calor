using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for the calor_obligations and calor_suggest_fixes MCP tools (Milestone 1).
/// </summary>
public sealed class ObligationsToolTests
{
    private readonly ObligationsTool _obligationsTool = new();
    private readonly SuggestFixesTool _suggestFixesTool = new();

    // ───── ObligationsTool ─────

    [Fact]
    public void ObligationsTool_Name_ReturnsCorrectName()
    {
        Assert.Equal("calor_obligations", _obligationsTool.Name);
    }

    [Fact]
    public void ObligationsTool_GetInputSchema_ReturnsValidSchema()
    {
        var schema = _obligationsTool.GetInputSchema();
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("timeout", out _));
        Assert.True(props.TryGetProperty("function_id", out _));
    }

    [Fact]
    public async Task ObligationsTool_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var result = await _obligationsTool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ObligationsTool_WithNullArgs_ReturnsError()
    {
        var result = await _obligationsTool.ExecuteAsync(null);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ObligationsTool_WithValidSource_ReturnsStructuredResult()
    {
        var source = """
            §M{m001:Test}
            §RTYPE{r1:NatInt:i32} (>= # INT:0)
            §F{f001:Main:pub}
              §I{i32:x} | (>= # INT:0)
              §O{void}
              §PROOF{p1:check} (>= x INT:0)
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            {
                "source": {{JsonSerializer.Serialize(source)}}
            }
            """).RootElement;

        var result = await _obligationsTool.ExecuteAsync(args);

        Assert.NotEmpty(result.Content);
        var text = result.Content[0].Text;
        Assert.NotNull(text);

        var json = JsonDocument.Parse(text);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("summary", out var summary));
        Assert.True(summary.TryGetProperty("total", out var total));
        Assert.True(total.GetInt32() >= 2);

        Assert.True(root.TryGetProperty("obligations", out var obligations));
        Assert.True(obligations.GetArrayLength() >= 2);

        // Each obligation should have required fields
        var firstObl = obligations[0];
        Assert.True(firstObl.TryGetProperty("id", out _));
        Assert.True(firstObl.TryGetProperty("kind", out _));
        Assert.True(firstObl.TryGetProperty("function_id", out _));
        Assert.True(firstObl.TryGetProperty("status", out _));
        Assert.True(firstObl.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task ObligationsTool_WithNoRefinements_ReturnsEmptyObligations()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            {
                "source": {{JsonSerializer.Serialize(source)}}
            }
            """).RootElement;

        var result = await _obligationsTool.ExecuteAsync(args);

        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ObligationsTool_WithFunctionIdFilter_FiltersResults()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Func1:pub}
              §I{i32:x} | (>= # INT:0)
              §O{void}
            §/F{f001}
            §F{f002:Func2:pub}
              §I{i32:y} | (> # INT:0)
              §O{void}
            §/F{f002}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            {
                "source": {{JsonSerializer.Serialize(source)}},
                "function_id": "f001"
            }
            """).RootElement;

        var result = await _obligationsTool.ExecuteAsync(args);

        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var obligations = json.RootElement.GetProperty("obligations");

        foreach (var obl in obligations.EnumerateArray())
        {
            Assert.Equal("f001", obl.GetProperty("function_id").GetString());
        }
    }

    // ───── SuggestFixesTool ─────

    [Fact]
    public void SuggestFixesTool_Name_ReturnsCorrectName()
    {
        Assert.Equal("calor_suggest_fixes", _suggestFixesTool.Name);
    }

    [Fact]
    public void SuggestFixesTool_GetInputSchema_ReturnsValidSchema()
    {
        var schema = _suggestFixesTool.GetInputSchema();
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task SuggestFixesTool_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var result = await _suggestFixesTool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SuggestFixesTool_WithValidSource_ReturnsFixSuggestions()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x} | (>= # INT:0)
              §O{void}
              §PROOF{p1:always-positive} (> x INT:0)
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            {
                "source": {{JsonSerializer.Serialize(source)}}
            }
            """).RootElement;

        var result = await _suggestFixesTool.ExecuteAsync(args);

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
