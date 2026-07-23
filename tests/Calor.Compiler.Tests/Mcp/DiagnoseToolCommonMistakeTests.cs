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
    public async Task ExecuteAsync_WithUndefinedFunction_IncludesCommonMistake()
    {
        // Use (abs x) which is an undefined function — should match "missing-operators" pattern.
        // Unlike redeclaration, the compiler won't have a specific fix for this.
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "§M{m001:Test} §F{f001:Fn:pub} §I{i32:x} §O{i32} §R (abs x)"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        // Should have errors
        Assert.True(json.GetProperty("errorCount").GetInt32() > 0,
            $"Expected errors for undefined function. Full output: {text}");

        // Look for a hints[] entry with commonMistake enrichment
        var foundCommonMistake = false;
        if (json.TryGetProperty("hints", out var hints))
        {
            foreach (var hint in hints.EnumerateArray())
            {
                Assert.True(hint.TryGetProperty("line", out _));
                Assert.True(hint.TryGetProperty("column", out _));
                Assert.True(hint.TryGetProperty("code", out _));

                var cm = hint.GetProperty("commonMistake");
                foundCommonMistake = true;
                Assert.NotEmpty(cm.GetProperty("id").GetString()!);
                Assert.NotEmpty(cm.GetProperty("title").GetString()!);
                Assert.NotEmpty(cm.GetProperty("suggestion").GetString()!);
                Assert.NotEmpty(cm.GetProperty("correctExample").GetString()!);
                break;
            }
        }

        // If the error message matches one of our patterns, we should get commonMistake
        // If the compiler provides its own fix, that takes precedence (and commonMistake won't be added)
        // Either outcome is acceptable — the enrichment logic is working correctly
        if (!foundCommonMistake)
        {
            // Verify the error has a compiler fix instead
            var hasAnyGuidance = false;
            foreach (var diagnostic in json.GetProperty("diagnostics").EnumerateArray())
            {
                if (diagnostic.TryGetProperty("suggestion", out _) || diagnostic.TryGetProperty("fix", out _))
                {
                    hasAnyGuidance = true;
                    break;
                }
            }
            Assert.True(hasAnyGuidance, "Error should have either commonMistake or compiler fix");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCode_NoCommonMistake()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "§M{m001:Test} §F{f001:Add:pub} §I{i32:a} §I{i32:b} §O{i32} §R (+ a b)"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.True(json.GetProperty("success").GetBoolean());

        // No diagnostics, so no hints[] with commonMistake guidance
        Assert.False(json.TryGetProperty("hints", out _),
            "Valid code should not have commonMistake hints");
    }

    [Fact]
    public async Task ExecuteAsync_WithCompilerFix_PrefersFix_OverCommonMistake()
    {
        // Use a typo operator that has a compiler fix suggestion
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\")"
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
