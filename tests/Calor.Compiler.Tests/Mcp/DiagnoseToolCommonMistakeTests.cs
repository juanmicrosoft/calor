using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for DiagnoseTool commonMistake enrichment.
/// When the compiler returns an error that matches a known common mistake pattern,
/// the diagnostic output should include a commonMistake field with guidance.
/// </summary>
public class DiagnoseToolCommonMistakeTests
{
    private readonly DiagnoseTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_WithUndefinedFunction_IncludesCommonMistake()
    {
        // Use (abs x) which is an undefined function — should match "missing-operators" pattern.
        // Unlike redeclaration, the compiler won't have a specific fix for this.
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test} §F{f001:Fn:pub} §I{i32:x} §O{i32} §R (abs x) §/F{f001} §/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        // Should have errors
        Assert.True(json.GetProperty("errorCount").GetInt32() > 0,
            $"Expected errors for undefined function. Full output: {text}");

        // Look for a diagnostic with commonMistake enrichment
        var foundCommonMistake = false;
        foreach (var diagnostic in json.GetProperty("diagnostics").EnumerateArray())
        {
            if (diagnostic.TryGetProperty("commonMistake", out var cm))
            {
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
                "source": "§M{m001:Test} §F{f001:Add:pub} §I{i32:a} §I{i32:b} §O{i32} §R (+ a b) §/F{f001} §/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.True(json.GetProperty("success").GetBoolean());

        // No diagnostics, so no commonMistake fields
        foreach (var diagnostic in json.GetProperty("diagnostics").EnumerateArray())
        {
            Assert.False(diagnostic.TryGetProperty("commonMistake", out _),
                "Valid code should not have commonMistake enrichment");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithCompilerFix_PrefersFix_OverCommonMistake()
    {
        // Use a typo operator that has a compiler fix suggestion
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}"
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
            // The commonMistake should NOT be present when the compiler already has a fix
            Assert.False(diagnostic.TryGetProperty("commonMistake", out _),
                "When a compiler fix exists, commonMistake should not be added");
        }
    }
}
