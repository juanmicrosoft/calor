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
                "source": "return `"
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
                "source": "let x = \"unterminated"
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
                "source": "1 + 2",
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
                "source": "let x = 10",
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
                "source": "return 42",
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

    [Theory]
    [InlineData("statement", "let x = 10")]
    [InlineData("expression", "1 + 2")]
    [InlineData("module_body", "func Test public () → int\n  !\n  return 1")]
    public async Task ExecuteAsync_ValidNativeSnippet_ReachesTheParser(string location, string source)
    {
        // The wrapper itself must lex: a wrapper written in retired syntax stops validation at
        // the lexer (validationLevel "lexer") while the snippet-range filter hides its errors.
        var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            action = "validate",
            source,
            context = new { location }
        })).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
        Assert.Contains("\"validationLevel\":\"parser\"", text);
    }

    [Fact]
    public async Task ExecuteAsync_FunctionBodyWithParametersAndReturnType_IsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "if x > 0\n  return x\nreturn 0",
                "context": {
                    "location": "function_body",
                    "returnType": "int",
                    "parameters": [ { "name": "x", "type": "int" } ]
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
        Assert.Contains("\"validationLevel\":\"parser\"", text);
    }

    [Fact]
    public async Task ExecuteAsync_ParseErrorOnSnippetLine3_IsReportedOnLine3()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "let a = 1\nlet b = 2\nlet c = a + )",
                "context": { "location": "function_body" }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.False(json.RootElement.GetProperty("valid").GetBoolean());
        var lines = json.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("location").GetProperty("line").GetInt32()).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal(3, line));
    }

    #endregion
    #region Column Number Tests

    [Fact]
    public async Task ExecuteAsync_ExpressionLocation_ColumnNumbersAreCorrect()
    {
        // Expression location adds a "    let _result = " prefix
        // Expression at original column 1 should report as column 1 after adjustment
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "1 + 2",
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
                "source": "func Test public () → void\n  return\n",
                "context": {
                    "location": "module_body",
                    "parameters": [
                        { "name": "x", "type": "int" }
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
                "source": "func Test public () → void\n  return\n",
                "context": {
                    "location": "module_body",
                    "returnType": "int"
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
                "source": "return x",
                "context": {
                    "location": "statement",
                    "parameters": [
                        { "name": "x", "type": "int" }
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
        // The expression "(1 +\n\"bad" has an unterminated string on line 2
        var args = JsonDocument.Parse("""
            {
                "action": "validate",
                "source": "(1 +\n\"unterminated",
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
        // Column should be reasonable (the string starts at column 1)
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
                "source": "(1 +\n  2 +\n  3)",
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

        Assert.NotEmpty(line1Tokens); // ( 1 +
        Assert.NotEmpty(line2Tokens); // 2 +
        Assert.NotEmpty(line3Tokens); // 3 )

        // Verify column numbers make sense (should start at actual position in snippet)
        var firstTokenLine2 = line2Tokens.First();
        // "  2 +" - the 2 is at column 3 in the original snippet line
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
                "source": "return 42",
                "context": {
                    "surroundingCode": "let x = \"unterminated"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);

        // The surrounding code error should be filtered out since it's not in the snippet
        // The snippet itself (return 42) is valid
        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        // Should have no diagnostics from the snippet (return 42 is valid)
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
                "source": "return \"unterminated",
                "context": {
                    "surroundingCode": "let x = 10"
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
                "source": "let 変数 = 42\nreturn 変数",
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
