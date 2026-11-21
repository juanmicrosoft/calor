using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for DiagnoseTool fix suggestion output.
/// </summary>
public class DiagnoseToolFixTests
{
    private readonly CheckTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_WithNameof_CompilesSuccessfully()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "module Test Test block\n  func Fn (int:x) → string\n    !\n    return nameof(x)"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.Equal(0, json.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCode_NoSuggestions()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "module Test Test block\n  func Add (int:a, int:b) → int\n    !\n    return (a + b)"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownSectionMarker_IncludesHelpfulMessage()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "§FUNC{f001:Test}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.True(json.GetProperty("diagnostics").GetArrayLength() > 0);

        var diagnostic = json.GetProperty("diagnostics")[0];
        var message = diagnostic.GetProperty("message").GetString()!;
        // Should suggest §F for §FUNC
        Assert.Contains("§F", message);
    }
}
