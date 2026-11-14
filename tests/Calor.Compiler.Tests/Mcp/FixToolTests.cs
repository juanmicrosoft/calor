using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class FixToolTests
{
    private readonly FixTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorFix()
    {
        Assert.Equal("calor_fix", _tool.Name);
    }

    [Fact]
    public void Description_ContainsFixInfo()
    {
        Assert.Contains("fix", _tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NEW{object}", _tool.Description);
        Assert.Contains("safe diagnostic", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("filePath", out _));
        Assert.True(props.TryGetProperty("errors", out _));
    }

    // ── FixNewObject ────────────────────────────────────────────────

    [Fact]
    public async Task FixNewObject_ReplacesFromBindingType()
    {
        var source = "§B{result:List<int>} §NEW{object}§/NEW{object}";
        await AssertRetiredConstructorUnchanged(source);
    }

    [Fact]
    public async Task FixNewObject_ReplacesFromThrowContext()
    {
        var source = "§TH §NEW{object} §A \"bad input\" §/NEW";
        await AssertRetiredConstructorUnchanged(source);
    }

    [Fact]
    public async Task FixNewObject_ReplacesFromReturnType()
    {
        var source = "§O{StringBuilder}\n§R §NEW{object}§/NEW";
        await AssertRetiredConstructorUnchanged(source);
    }

    [Fact]
    public async Task FixNewObject_ReplacesFromFieldType()
    {
        var source = "§FLD{Dictionary<string,int>:_cache:priv}\n  §NEW{object}§/NEW";
        await AssertRetiredConstructorUnchanged(source);
    }

    [Fact]
    public async Task FixNewObject_LeavesUnresolvable()
    {
        var source = "§B{x} §NEW{object}§/NEW";
        await AssertRetiredConstructorUnchanged(source);
    }

    private async Task AssertRetiredConstructorUnchanged(string source)
    {
        var result = await _tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { source }));
        Assert.True(result.IsError);
        var output = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.False(output.GetProperty("success").GetBoolean());
        Assert.Equal(source, output.GetProperty("fixedSource").GetString());
        Assert.Equal(0, output.GetProperty("fixCount").GetInt32());
        Assert.NotEmpty(output.GetProperty("diagnostics").EnumerateArray());
    }

    [Theory]
    [InlineData("id_conflicts")]
    [InlineData("ID_CONFLICTS")]
    public async Task ExecuteAsync_SourceIdConflictRule_IsRejected(string rule)
    {
        var source = "§F{Foo:pub}\n§R 1\n\n§F{Bar:pub}\n§R 2\n";
        var result = await _tool.ExecuteAsync(JsonSerializer.SerializeToElement(
            new { source, errors = new[] { "safe_diagnostics", rule } }));

        Assert.True(result.IsError);
        Assert.Contains(".calor/ids.json", result.Content[0].Text!);
        Assert.DoesNotContain("fixedSource", result.Content[0].Text!);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultRules_PreserveOverloadsAndRepeatedCalls()
    {
        const string source = """
            module Names
              func Echo (int:value) → int
                !
                return value
              func Echo (string:value) → string
                !
                return value
              func Use () → int
                !
                let int:first = Echo(1)
                return Echo(first)
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        var result = await _tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { source }));

        Assert.False(result.IsError);
        var output = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal(source, output.GetProperty("fixedSource").GetString());
        Assert.False(output.GetProperty("wasModified").GetBoolean());
        Assert.Equal(0, output.GetProperty("fixCount").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownRule_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(JsonSerializer.SerializeToElement(
            new { source = "module Test Test block", errors = new[] { "unknown_rule" } }));

        Assert.True(result.IsError);
        Assert.Contains("Unsupported fix rule", result.Content[0].Text!);
    }

    // ── Integration via MCP ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingSourceAndPath_ReturnsError()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_FixesNewObject_ReturnsFixedSource()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§TH §NEW{object} §A \"error\" §/NEW",
                "errors": ["new_object"]
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("retired", text);
        Assert.DoesNotContain("NEW{Exception}", text);
        Assert.DoesNotContain("fixedSource", text);
    }

    [Fact]
    public async Task ExecuteAsync_FixAll_AppliesSupportedRules()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§TH §NEW{object} §A \"err\" §/NEW"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"success\":false", text);
        Assert.Contains("\"wasModified\":false", text);
    }

    [Fact]
    public async Task ExecuteAsync_CleanSource_NoModification()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "module Test\n  func Foo () → void\n    return\n"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"wasModified\":false", text);
        Assert.Contains("\"fixCount\":0", text);
    }
}
