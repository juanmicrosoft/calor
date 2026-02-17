using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for DiagnoseTool fix suggestion output.
/// </summary>
public class DiagnoseToolFixTests
{
    private readonly DiagnoseTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_WithTypoOperator_IncludesSuggestion()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.True(json.GetProperty("diagnostics").GetArrayLength() > 0);

        var diagnostic = json.GetProperty("diagnostics")[0];
        Assert.True(diagnostic.TryGetProperty("suggestion", out var suggestion));
        Assert.Contains("contains", suggestion.GetString()!);
    }

    [Fact]
    public async Task ExecuteAsync_WithTypoOperator_IncludesFix()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var diagnostic = json.GetProperty("diagnostics")[0];
        Assert.True(diagnostic.TryGetProperty("fix", out var fix));
        Assert.True(fix.TryGetProperty("description", out _));
        Assert.True(fix.TryGetProperty("edits", out var edits));
        Assert.True(edits.GetArrayLength() > 0);

        var edit = edits[0];
        Assert.True(edit.TryGetProperty("startLine", out _));
        Assert.True(edit.TryGetProperty("startColumn", out _));
        Assert.True(edit.TryGetProperty("endLine", out _));
        Assert.True(edit.TryGetProperty("endColumn", out _));
        Assert.True(edit.TryGetProperty("newText", out var newText));
        Assert.Equal("contains", newText.GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithCSharpConstruct_IncludesHelpfulMessage()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test} §F{f001:Fn} §O{str} §R (nameof x) §/F{f001} §/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var diagnostic = json.GetProperty("diagnostics")[0];
        var message = diagnostic.GetProperty("message").GetString()!;
        Assert.Contains("nameof", message);
        Assert.Contains("string literal", message);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCode_NoSuggestions()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test} §F{f001:Add} §I{i32:a} §I{i32:b} §O{i32} §R (+ a b) §/F{f001} §/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_WithMismatchedId_IncludesFix()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test} §F{f001:Add} §O{i32} §R 42 §/F{f002} §/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        // Should have an error about mismatched IDs
        Assert.True(json.GetProperty("diagnostics").GetArrayLength() > 0);

        // Find the mismatched ID diagnostic
        var diagnostics = json.GetProperty("diagnostics");
        var foundMismatch = false;
        foreach (var diag in diagnostics.EnumerateArray())
        {
            var message = diag.GetProperty("message").GetString()!;
            if (message.Contains("f002") && message.Contains("f001"))
            {
                foundMismatch = true;
                Assert.True(diag.TryGetProperty("fix", out var fix));
                Assert.True(fix.TryGetProperty("edits", out _));
                break;
            }
        }
        Assert.True(foundMismatch, "Expected a mismatched ID diagnostic");
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownSectionMarker_IncludesHelpfulMessage()
    {
        var args = JsonDocument.Parse("""
            {
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

    [Fact]
    public async Task ExecuteAsync_DiagnosticOutput_HasCorrectSchema()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        // Verify top-level schema
        Assert.True(json.TryGetProperty("success", out _));
        Assert.True(json.TryGetProperty("errorCount", out _));
        Assert.True(json.TryGetProperty("warningCount", out _));
        Assert.True(json.TryGetProperty("diagnostics", out _));

        // Verify diagnostic schema
        var diagnostic = json.GetProperty("diagnostics")[0];
        Assert.True(diagnostic.TryGetProperty("severity", out _));
        Assert.True(diagnostic.TryGetProperty("code", out _));
        Assert.True(diagnostic.TryGetProperty("message", out _));
        Assert.True(diagnostic.TryGetProperty("line", out _));
        Assert.True(diagnostic.TryGetProperty("column", out _));
        // Optional fields
        Assert.True(diagnostic.TryGetProperty("suggestion", out _));
        Assert.True(diagnostic.TryGetProperty("fix", out _));
    }
}
