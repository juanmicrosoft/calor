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
        var source = "§B{b1:result:List<int>} §NEW{object}§/NEW{object}";
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
        var source = "§B{b1:x} §NEW{object}§/NEW";
        var (result, fixes) = FixTool.FixNewObject(source);

        Assert.Contains("§NEW{object}", result);
        Assert.Empty(fixes);
    }

    // ── FixArrowMultiStatement ──────────────────────────────────────

    [Fact]
    public void FixArrowMultiStatement_ConvertsToBlock()
    {
        var source = "  §IF{if1} (x) → §B{a} 1 §R a";
        var (result, fixes) = FixTool.FixArrowMultiStatement(source);

        Assert.DoesNotContain("→", result);
        Assert.Contains("§IF{if1} (x)", result);
        Assert.Contains("§B{a} 1", result);
        Assert.Contains("§R a", result);
        Assert.Contains("§/I{if1}", result);
        Assert.Single(fixes);
        Assert.Equal("arrow_multi_statement", fixes[0].Rule);
    }

    [Fact]
    public void FixArrowMultiStatement_LeavesSingleStatement()
    {
        var source = "  §IF{if1} (x) → §R 1";
        var (result, fixes) = FixTool.FixArrowMultiStatement(source);

        Assert.Contains("→", result);
        Assert.Empty(fixes);
    }

    // ── FixIdConflicts ──────────────────────────────────────────────

    [Fact]
    public void FixIdConflicts_RenumbersDuplicates()
    {
        var source = "§F{f1:Foo:pub}\n§R 1\n§/F{f1}\n§F{f1:Bar:pub}\n§R 2\n§/F{f1}";
        var (result, fixes) = FixTool.FixIdConflicts(source);

        // First f1 should remain, second should be renumbered
        Assert.Single(fixes);
        Assert.Equal("id_conflicts", fixes[0].Rule);
        // The renamed ID should appear in both opening and closing tags
        Assert.Contains("§F{f1:Foo:pub}", result);
        Assert.DoesNotContain("§F{f1:Bar", result);
    }

    [Fact]
    public void FixIdConflicts_NoConflictsNoChange()
    {
        var source = "§F{f1:Foo:pub}\n§/F{f1}\n§F{f2:Bar:pub}\n§/F{f2}";
        var (result, fixes) = FixTool.FixIdConflicts(source);

        Assert.Equal(source, result);
        Assert.Empty(fixes);
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
    public async Task ExecuteAsync_FixAll_AppliesAllPasses()
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
                "source": "§M{m1:Test}\n§F{f1:Foo:pub}\n§O{void}\n§/F{f1}\n§/M{m1}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"wasModified\":false", text);
        Assert.Contains("\"fixCount\":0", text);
    }
}
