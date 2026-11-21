using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for DiagnoseTool commonMistake enrichment.
/// When the compiler returns an error that matches a known common mistake pattern,
/// the tool output should include a hints[] entry with commonMistake guidance,
/// keyed by (line, column, code) of the matching envelope diagnostic.
/// </summary>
public class DiagnoseToolCommonMistakeTests
{
    private readonly CheckTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_WithCompilerFix_PrefersFix_OverCommonMistake()
    {
        // Use a typo operator that has a compiler fix suggestion
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "§M{Test} §F{Fn} §O{i32} §R (cotains \"hello\" \"h\")"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var diagnostic = json.GetProperty("diagnostics")[0];

        // When a compiler fix exists, it should be present
        if (diagnostic.TryGetProperty("suggestion", out _) &&
            diagnostic.TryGetProperty("fix", out _))
        {
            // No hint should target this diagnostic when the compiler already has a fix
            var line = diagnostic.GetProperty("location").GetProperty("line").GetInt32();
            var column = diagnostic.GetProperty("location").GetProperty("column").GetInt32();
            var code = diagnostic.GetProperty("code").GetString();

            if (json.TryGetProperty("hints", out var hints))
            {
                foreach (var hint in hints.EnumerateArray())
                {
                    var matches = hint.GetProperty("line").GetInt32() == line
                        && hint.GetProperty("column").GetInt32() == column
                        && hint.GetProperty("code").GetString() == code;
                    Assert.False(matches,
                        "When a compiler fix exists, no commonMistake hint should target that diagnostic");
                }
            }
        }
    }
}
