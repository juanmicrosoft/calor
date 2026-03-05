using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for ExplainErrorTool — the calor_explain_error MCP tool.
/// </summary>
public class ExplainErrorToolTests
{
    private readonly HelpTool _tool = new();

    [Fact]
    public void Name_ReturnsExpectedToolName()
    {
        Assert.Equal("calor_help", _tool.Name);
    }

    [Fact]
    public void GetInputSchema_HasErrorProperty()
    {
        var schema = _tool.GetInputSchema();
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingParameter_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"action": "error"}""").RootElement;
        var result = await _tool.ExecuteAsync(args);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullArguments_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"action": "error"}""").RootElement;
        var result = await _tool.ExecuteAsync(args);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_WithVariableRedeclaration_MatchesPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "error",
                "error": "Variable 'k' already defined in this scope"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        Assert.True(json.TryGetProperty("matches", out var matches));
        Assert.True(matches.GetArrayLength() > 0);

        var match = matches[0];
        Assert.Equal("variable-redeclaration", match.GetProperty("id").GetString());
        Assert.Contains("§ASSIGN", match.GetProperty("correctExample").GetString()!);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullReference_MatchesNullCheckOrdering()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "error",
                "error": "NullReferenceException when calling (len arr)"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var matches = json.GetProperty("matches");
        Assert.True(matches.GetArrayLength() > 0);

        var match = matches[0];
        Assert.Equal("null-check-ordering", match.GetProperty("id").GetString());
        Assert.Contains("null", match.GetProperty("correctExample").GetString()!);
    }

    [Fact]
    public async Task ExecuteAsync_WithCharLiteralError_MatchesCharPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "error",
                "error": "Unexpected character '''"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var matches = json.GetProperty("matches");
        Assert.True(matches.GetArrayLength() > 0);
        Assert.Equal("char-literals", matches[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithErrorCode_MatchesByCode()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "error",
                "error": "Calor0201"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var matches = json.GetProperty("matches");
        Assert.True(matches.GetArrayLength() > 0);
        Assert.Equal("variable-redeclaration", matches[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithUndefinedFunction_MatchesMissingOperators()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "error",
                "error": "undefined function 'abs'"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var matches = json.GetProperty("matches");
        Assert.True(matches.GetArrayLength() > 0);
        Assert.Equal("missing-operators", matches[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownError_ReturnsNoMatches()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "error",
                "error": "completely unrelated gibberish xyz123"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        // Should return a text message about no matches, not JSON
        Assert.Contains("No known common mistake", text);
    }

    [Fact]
    public async Task ExecuteAsync_MatchOutput_HasRequiredFields()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "error",
                "error": "already defined"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var match = json.GetProperty("matches")[0];

        // Verify all required output fields exist
        Assert.True(match.TryGetProperty("id", out _));
        Assert.True(match.TryGetProperty("title", out _));
        Assert.True(match.TryGetProperty("description", out _));
        Assert.True(match.TryGetProperty("wrongExample", out _));
        Assert.True(match.TryGetProperty("correctExample", out _));
        Assert.True(match.TryGetProperty("explanation", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithArraySyntaxError_MatchesArrayPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "error",
                "error": "i32[] is wrong type syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text).RootElement;

        var matches = json.GetProperty("matches");
        Assert.True(matches.GetArrayLength() > 0);
        Assert.Equal("array-type-syntax", matches[0].GetProperty("id").GetString());
    }
}
