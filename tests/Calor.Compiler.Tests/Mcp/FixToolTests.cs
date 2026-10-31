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
        Assert.Contains("NEW{object}", _tool.Description);
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
    public void FixNewObject_ReplacesFromBindingType()
    {
        var source = "§B{result:List<int>} §NEW{object}§/NEW{object}";
        var (result, fixes) = FixTool.FixNewObject(source);

        Assert.Contains("§NEW{List<int>}", result);
        Assert.Contains("§/NEW{List<int>}", result);
        Assert.DoesNotContain("§NEW{object}", result);
        Assert.Single(fixes);
        Assert.Equal("new_object", fixes[0].Rule);
    }

    [Fact]
    public void FixNewObject_ReplacesFromThrowContext()
    {
        var source = "§TH §NEW{object} §A \"bad input\" §/NEW";
        var (result, fixes) = FixTool.FixNewObject(source);

        Assert.Contains("§NEW{Exception}", result);
        Assert.Single(fixes);
    }

    [Fact]
    public void FixNewObject_ReplacesFromReturnType()
    {
        var source = "§O{StringBuilder}\n§R §NEW{object}§/NEW";
        var (result, fixes) = FixTool.FixNewObject(source);

        Assert.Contains("§NEW{StringBuilder}", result);
        Assert.Single(fixes);
    }

    [Fact]
    public void FixNewObject_ReplacesFromFieldType()
    {
        var source = "§FLD{Dictionary<string,int>:_cache:priv}\n  §NEW{object}§/NEW";
        var (result, fixes) = FixTool.FixNewObject(source);

        Assert.Contains("§NEW{Dictionary<string,int>}", result);
        Assert.Single(fixes);
    }

    [Fact]
    public void FixNewObject_LeavesUnresolvable()
    {
        var source = "§B{x} §NEW{object}§/NEW";
        var (result, fixes) = FixTool.FixNewObject(source);

        Assert.Contains("§NEW{object}", result);
        Assert.Empty(fixes);
    }

    [Theory]
    [InlineData("id_conflicts")]
    [InlineData("ID_CONFLICTS")]
    public async Task ExecuteAsync_SourceIdConflictRule_IsRejected(string rule)
    {
        var source = "§F{Foo:pub}\n§R 1\n\n§F{Bar:pub}\n§R 2\n";
        var result = await _tool.ExecuteAsync(JsonSerializer.SerializeToElement(
            new { source, errors = new[] { "new_object", rule } }));

        Assert.True(result.IsError);
        Assert.Contains(".calor/ids.json", result.Content[0].Text!);
        Assert.DoesNotContain("fixedSource", result.Content[0].Text!);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultRules_PreserveOverloadsAndRepeatedCalls()
    {
        const string source = """
            §M{Names}
              §F{Echo:pub} (i32:value) -> i32
                §R value
              §F{Echo:pub} (str:value) -> str
                §R value
              §F{Use:pub} () -> i32
                §B{first:i32} §C{Echo} §A 1 §/C
                §R §C{Echo} §A first §/C
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
            new { source = "§M{Test}", errors = new[] { "unknown_rule" } }));

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

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("fixedSource", text);
        Assert.Contains("NEW{Exception}", text);
        Assert.Contains("\"wasModified\":true", text);
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

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"success\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_CleanSource_NoModification()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{Test}\n§F{Foo:pub}\n§O{void}\n\n"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"wasModified\":false", text);
        Assert.Contains("\"fixCount\":0", text);
    }
}
