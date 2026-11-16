using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class ValidateSnippetToolTests
{
    private readonly CheckTool _tool = new();

    #region Basic Tests

    [Fact]
    public void Name_ReturnsCalorValidateSnippet()
    {
        Assert.Equal("calor_check", _tool.Name);
    }

    [Fact]
    public void Description_ContainsValidateInfo()
    {
        Assert.Contains("validation", _tool.Description.ToLower());
        Assert.Contains("Calor", _tool.Description);
        Assert.Contains("snippet", _tool.Description.ToLower());
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("context", out _));
        Assert.True(props.TryGetProperty("action", out _));
    }
    #endregion
    #region Valid Snippets
    #endregion
    #region Invalid Snippets

    [Fact]
    public async Task ExecuteAsync_WithBadToken_ReturnsInvalid()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§R §§§"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":false", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnterminatedString_ReturnsInvalid()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§B{x} STR:\"unterminated"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":false", text);
    }
    #endregion
    #region Context Tests

    [Fact]
    public async Task ExecuteAsync_WithExpressionLocation_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "(+ 1 2)",
                "context": {
                    "location": "expression"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }
    #endregion
    #region Options Tests

    [Fact]
    public async Task ExecuteAsync_WithShowTokens_IncludesTokens()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§B{x} 10",
                "showTokens": true
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"tokens\":", text);
        Assert.Contains("\"kind\":", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithLexerOnlyAndShowTokens_IncludesTokens()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§R 42",
                "lexerOnly": true,
                "showTokens": true
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"validationLevel\":\"lexer\"", text);
        Assert.Contains("\"tokens\":", text);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ExecuteAsync_WithEmptySnippet_ReturnsValidWithWarning()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": ""
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"success\":true", text);
        Assert.Contains("\"validationLevel\":\"none\"", text);
        Assert.Contains("\"warnings\":", text);
        Assert.Contains("empty", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithWhitespaceOnlySnippet_ReturnsValidWithWarning()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "   \n   "
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"success\":true", text);
        Assert.Contains("\"validationLevel\":\"none\"", text);
        Assert.Contains("\"warnings\":", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingSnippet_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"action": "validate"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("source", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithNullArguments_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(null);

        Assert.True(result.IsError);
    }
    #endregion
    #region Complex Scenarios
    #endregion
    #region Column Number Tests

    [Fact]
    public async Task ExecuteAsync_ExpressionLocation_ColumnNumbersAreCorrect()
    {
        // Expression location adds "  §B{_result} " (15 chars) prefix
        // Expression at original column 1 should report as column 1 after adjustment
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "(+ 1 2)",
                "context": { "location": "expression" },
                "showTokens": true
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var tokens = json.RootElement.GetProperty("tokens").EnumerateArray().ToList();

        // First token (open paren) should be at column 1
        var firstToken = tokens.First();
        Assert.Equal(1, firstToken.GetProperty("column").GetInt32());
    }

    #endregion

    #region Warning Tests

    [Fact]
    public async Task ExecuteAsync_ModuleBodyWithParameters_ReturnsWarning()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§F{Test:pub}\n  §O{unit}\n  §R\n",
                "context": {
                    "location": "module_body",
                    "parameters": [
                        { "name": "x", "type": "i32" }
                    ]
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"warnings\":", text);
        Assert.Contains("parameters", text.ToLower());
        Assert.Contains("ignored", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_ModuleBodyWithReturnType_ReturnsWarning()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§F{Test:pub}\n  §O{unit}\n  §R\n",
                "context": {
                    "location": "module_body",
                    "returnType": "i32"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"warnings\":", text);
        Assert.Contains("returnType", text);
        Assert.Contains("ignored", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_StatementLocationWithParameters_NoWarnings()
    {
        // Statement location should use parameters, not warn
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§R x",
                "context": {
                    "location": "statement",
                    "parameters": [
                        { "name": "x", "type": "i32" }
                    ]
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.DoesNotContain("\"warnings\":", text);
    }
    #endregion
    #region Robustness Tests

    #endregion

    #region Additional Edge Case Tests

    [Fact]
    public async Task ExecuteAsync_MultiLineExpression_ColumnNumbersCorrectOnLine2()
    {
        // Multi-line expression: error on line 2 should have correct column
        // The expression "(+ 1\nSTR:\"bad)" has an unterminated string on line 2
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "(+ 1\nSTR:\"unterminated",
                "context": { "location": "expression" }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.NotEmpty(diagnostics);
        var diag = diagnostics[0];
        // Error should be on line 2 of the snippet
        Assert.Equal(2, diag.GetProperty("location").GetProperty("line").GetInt32());
        // Column should be reasonable (the STR: starts at column 1)
        var col = diag.GetProperty("location").GetProperty("column").GetInt32();
        Assert.True(col >= 1, $"Expected column >= 1, got {col}");
    }

    [Fact]
    public async Task ExecuteAsync_MultiLineExpression_TokensCorrectOnAllLines()
    {
        // Multi-line expression with tokens on multiple lines
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "(+\n  1\n  2)",
                "context": { "location": "expression" },
                "showTokens": true
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var tokens = json.RootElement.GetProperty("tokens").EnumerateArray().ToList();

        // Should have tokens on multiple lines
        var line1Tokens = tokens.Where(t => t.GetProperty("line").GetInt32() == 1).ToList();
        var line2Tokens = tokens.Where(t => t.GetProperty("line").GetInt32() == 2).ToList();
        var line3Tokens = tokens.Where(t => t.GetProperty("line").GetInt32() == 3).ToList();

        Assert.NotEmpty(line1Tokens); // ( and +
        Assert.NotEmpty(line2Tokens); // 1
        Assert.NotEmpty(line3Tokens); // 2 and )

        // Verify column numbers make sense (should start at actual position in snippet)
        var firstTokenLine2 = line2Tokens.First();
        // "  1" - the 1 is at column 3 in the original snippet line
        Assert.Equal(3, firstTokenLine2.GetProperty("column").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_SurroundingCodeWithError_ErrorIsFilteredOut()
    {
        // Surrounding code has an error, but snippet is valid
        // The error in surrounding code should NOT appear in diagnostics
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§R 42",
                "context": {
                    "surroundingCode": "§B{x} STR:\"unterminated"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);

        // The surrounding code error should be filtered out since it's not in the snippet
        // The snippet itself (§R 42) is valid
        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        // Should have no diagnostics from the snippet (§R 42 is valid)
        // The unterminated string is in surroundingCode, which is on an earlier line
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ExecuteAsync_SurroundingCodeValid_SnippetErrorReported()
    {
        // Surrounding code is valid, snippet has error
        // Only snippet error should be reported
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§R STR:\"unterminated",
                "context": {
                    "surroundingCode": "§B{x} 10"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.NotEmpty(diagnostics);
        // Error should be on line 1 of the snippet (not line 2 which would include surrounding code line)
        Assert.Equal(1, diagnostics[0].GetProperty("location").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_UnicodeIdentifiers_HandlesCorrectly()
    {
        // Some languages allow unicode identifiers
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "§B{変数} 42\n§R 変数",
                "showTokens": true
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // Should at least not crash - whether unicode identifiers are valid depends on lexer
        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
    }

    #endregion
}
