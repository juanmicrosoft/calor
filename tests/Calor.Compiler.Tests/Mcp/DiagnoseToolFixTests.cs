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
    public async Task ExecuteAsync_WithTypoOperator_IncludesSuggestion()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\")"
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
                "action": "diagnose",
                "source": "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\")"
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
    public async Task ExecuteAsync_WithNameof_CompilesSuccessfully()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "§M{m001:Test} §F{f001:Fn} §O{str} §R (nameof x)"
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
                "source": "§M{m001:Test} §F{f001:Add} §I{i32:a} §I{i32:b} §O{i32} §R (+ a b)"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.GetProperty("errorCount").GetInt32());
    }

    [Fact(Skip = "Phase 4d: mismatched-ID diagnostic is obsolete under indent-only (no closing tags)")]
    public async Task ExecuteAsync_WithMismatchedId_IncludesFix()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "source": "§M{m001:Test} §F{f001:Add} §O{i32} §R 42"
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
    public async Task ExecuteAsync_WithFourFieldHeader_Apply_HealsToReturningSource()
    {
        // A four-field §F header hard-errors (Calor0116). With apply=true the
        // diagnose path must rewrite it to a three-field header plus a signature
        // via its SuggestedFix and return source that compiles clean — and, in
        // particular, keeps the return type instead of silently becoming void.
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "apply": true,
                "source": "§M{m1:Calc}\n  §F{f1:Add:i32:pub}\n    §R (+ 1 2)"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;

        // A Calor0116 diagnostic must be present with a fix attached.
        var hasHeaderDiag = false;
        foreach (var diag in json.GetProperty("diagnostics").EnumerateArray())
        {
            if (diag.GetProperty("code").GetString() == "Calor0116")
            {
                hasHeaderDiag = true;
                Assert.True(diag.TryGetProperty("fix", out var fix));
                Assert.True(fix.TryGetProperty("edits", out var edits));
                Assert.True(edits.GetArrayLength() > 0);
            }
        }
        Assert.True(hasHeaderDiag, "Expected a Calor0116 diagnostic");

        Assert.True(json.TryGetProperty("fixedSource", out var fixedSourceEl));
        var healed = fixedSourceEl.GetString()!;
        Assert.Contains("§F{f1:Add:pub} () -> i32", healed);
        Assert.DoesNotContain("i32:pub", healed);
        Assert.True(json.GetProperty("fixesApplied").GetInt32() >= 1);

        // Re-diagnose the healed source: it must now compile clean.
        var reArgs = JsonSerializer.SerializeToElement(new { action = "diagnose", source = healed });
        var reResult = await _tool.ExecuteAsync(reArgs);
        var reJson = JsonDocument.Parse(reResult.Content[0].Text!).RootElement;

        Assert.True(reJson.GetProperty("success").GetBoolean());
        Assert.Equal(0, reJson.GetProperty("errorCount").GetInt32());
        foreach (var diag in reJson.GetProperty("diagnostics").EnumerateArray())
        {
            Assert.NotEqual("Calor0116", diag.GetProperty("code").GetString());
        }
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

    private const string LegacyCloserSource =
        "§M{m1:Calc}\n  §F{f1:Add:pub}\n    §I{i32:a}\n    §I{i32:b}\n    §O{i32}\n    §R (+ a b)\n  §/F{f1}\n§/M{m1}";

    [Fact]
    public async Task ExecuteAsync_WithLegacyClosers_ReportsCalor0830WithFix()
    {
        // Closer-form source hard-errors (Calor0830) and the diagnostic must
        // carry a machine-applicable fix stripping the closer.
        var args = JsonSerializer.SerializeToElement(new { action = "diagnose", source = LegacyCloserSource });

        var result = await _tool.ExecuteAsync(args);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;

        var hasCloserDiag = false;
        foreach (var diag in json.GetProperty("diagnostics").EnumerateArray())
        {
            if (diag.GetProperty("code").GetString() == "Calor0830")
            {
                hasCloserDiag = true;
                Assert.True(diag.TryGetProperty("fix", out var fix));
                Assert.True(fix.TryGetProperty("edits", out var edits));
                Assert.True(edits.GetArrayLength() > 0);
            }
        }
        Assert.True(hasCloserDiag, "Expected a Calor0830 diagnostic");
    }

    [Fact]
    public async Task ExecuteAsync_WithLegacyClosers_Apply_HealsToCompilingSource()
    {
        // With apply=true the diagnose path strips the closers via their
        // SuggestedFix (plus the source healer for the emptied lines) and
        // returns source that compiles clean indent-only. When healed=true,
        // success/errorCount/diagnostics describe the POST-heal fixedSource —
        // the pre-heal Calor0830s are consumed by the repair, not echoed with
        // stale coordinates.
        var args = JsonDocument.Parse("""
            {
                "action": "diagnose",
                "apply": true,
                "source": "§M{m1:Calc}\n  §F{f1:Add:pub}\n    §I{i32:a}\n    §I{i32:b}\n    §O{i32}\n    §R (+ a b)\n  §/F{f1}\n§/M{m1}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;

        Assert.True(json.TryGetProperty("fixedSource", out var fixedSourceEl));
        var healed = fixedSourceEl.GetString()!;
        Assert.DoesNotContain("§/", healed);
        Assert.True(json.GetProperty("fixesApplied").GetInt32() >= 2);

        // The repair fully fixed the file, so the response reflects that.
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.GetProperty("errorCount").GetInt32());
        Assert.False(result.IsError);

        // Re-diagnose the healed source: it must now compile clean.
        var reArgs = JsonSerializer.SerializeToElement(new { action = "diagnose", source = healed });
        var reResult = await _tool.ExecuteAsync(reArgs);
        var reJson = JsonDocument.Parse(reResult.Content[0].Text!).RootElement;

        Assert.True(reJson.GetProperty("success").GetBoolean());
        Assert.Equal(0, reJson.GetProperty("errorCount").GetInt32());
        foreach (var diag in reJson.GetProperty("diagnostics").EnumerateArray())
        {
            Assert.NotEqual("Calor0830", diag.GetProperty("code").GetString());
        }
    }
}
