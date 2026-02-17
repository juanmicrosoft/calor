using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class VerifyContractsToolTests
{
    private readonly VerifyContractsTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorVerifyContracts()
    {
        Assert.Equal("calor_verify_contracts", _tool.Name);
    }

    [Fact]
    public void Description_ContainsVerifyInfo()
    {
        Assert.Contains("Verify", _tool.Description);
        Assert.Contains("contract", _tool.Description.ToLower());
        Assert.Contains("Z3", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("timeout", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithValidSource_ReturnsStructuredResult()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.NotEmpty(result.Content);
        var text = result.Content[0].Text;
        Assert.NotNull(text);

        var json = JsonDocument.Parse(text);
        Assert.True(json.RootElement.TryGetProperty("success", out _));
        Assert.True(json.RootElement.TryGetProperty("summary", out var summary));
        Assert.True(summary.TryGetProperty("total", out _));
        Assert.True(summary.TryGetProperty("proven", out _));
        Assert.True(summary.TryGetProperty("disproven", out _));
        Assert.True(json.RootElement.TryGetProperty("functions", out _));
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
    public async Task ExecuteAsync_WithContract_VerifiesContract()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Div:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§Q (!= b 0)\n§R (/ a b)\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        Assert.Contains("summary", text);
        Assert.Contains("functions", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithTimeout_UsesCustomTimeout()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n§/F{f001}\n§/M{m001}",
                "timeout": 1000
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.NotEmpty(result.Content);
        var text = result.Content[0].Text;
        Assert.NotNull(text);
        Assert.Contains("success", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithSyntaxError_ReportsCompilationErrors()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Bad} invalid syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("compilationErrors", text);
    }

    [Fact]
    public async Task ExecuteAsync_ContractStatusValues()
    {
        // Test that contract verification returns expected status values
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Div:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§Q (!= b 0)\n§R (/ a b)\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        // Check summary contains expected fields
        var summary = json.RootElement.GetProperty("summary");
        Assert.True(summary.TryGetProperty("proven", out _));
        Assert.True(summary.TryGetProperty("unproven", out _));
        Assert.True(summary.TryGetProperty("disproven", out _));
        Assert.True(summary.TryGetProperty("unsupported", out _));
        Assert.True(summary.TryGetProperty("skipped", out _));
    }
}
