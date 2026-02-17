using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class ValidateSnippetToolTests
{
    private readonly ValidateSnippetTool _tool = new();

    #region Basic Tests

    [Fact]
    public void Name_ReturnsCalorValidateSnippet()
    {
        Assert.Equal("calor_validate_snippet", _tool.Name);
    }

    [Fact]
    public void Description_ContainsValidateInfo()
    {
        Assert.Contains("Validate", _tool.Description);
        Assert.Contains("Calor", _tool.Description);
        Assert.Contains("fragment", _tool.Description.ToLower());
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("snippet", out _));
        Assert.True(props.TryGetProperty("context", out _));
        Assert.True(props.TryGetProperty("options", out _));
    }

    #endregion

    #region Valid Snippets

    [Fact]
    public async Task ExecuteAsync_WithValidReturnStatement_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R 42"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
        Assert.Contains("\"validationLevel\":\"parser\"", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidBindStatement_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{x} 10"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidSomeExpression_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R §SM 42",
                "context": {
                    "returnType": "Option<i32>"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidNoneExpression_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R §NN",
                "context": {
                    "returnType": "Option<i32>"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultiLineSnippet_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{x} 10\n§B{y} 20\n§R (+ x y)"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    #endregion

    #region Invalid Snippets

    [Fact]
    public async Task ExecuteAsync_WithReturnNoExpression_ReturnsValid()
    {
        // §R without expression returns unit, which is valid
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithBadToken_ReturnsInvalid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R §§§"
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
                "snippet": "§B{x} STR:\"unterminated"
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
    public async Task ExecuteAsync_WithParameters_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R (+ a b)",
                "context": {
                    "returnType": "i32",
                    "parameters": [
                        { "name": "a", "type": "i32" },
                        { "name": "b", "type": "i32" }
                    ]
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithSurroundingCode_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R y",
                "context": {
                    "surroundingCode": "§B{y} (+ x 1)",
                    "parameters": [
                        { "name": "x", "type": "i32" }
                    ]
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithExpressionLocation_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "(+ 1 2)",
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

    [Fact]
    public async Task ExecuteAsync_WithModuleBodyLocation_ReturnsValid()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§F{f001:Add:pub}\n  §I{i32:a}\n  §I{i32:b}\n  §O{i32}\n  §R (+ a b)\n§/F{f001}",
                "context": {
                    "location": "module_body"
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
    public async Task ExecuteAsync_WithLexerOnly_StopsAfterLexer()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{x} 10",
                "options": {
                    "lexerOnly": true
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"validationLevel\":\"lexer\"", text);
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithShowTokens_IncludesTokens()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{x} 10",
                "options": {
                    "showTokens": true
                }
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
                "snippet": "§R 42",
                "options": {
                    "lexerOnly": true,
                    "showTokens": true
                }
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
                "snippet": ""
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
                "snippet": "   \n   "
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
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("snippet", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithNullArguments_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_DiagnosticLineNumbers_AreRelativeToSnippet()
    {
        // A multi-line snippet with an error on line 2 of the snippet
        // Use an unclosed string to get an error
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{x} 10\n§B{y} STR:\"unterminated"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":false", text);

        // Parse the result to check line numbers
        var json = JsonDocument.Parse(text);
        var diagnostics = json.RootElement.GetProperty("diagnostics");
        Assert.NotEmpty(diagnostics.EnumerateArray().ToList());

        // The error should be on line 2 (relative to snippet), not the wrapped line
        var firstDiag = diagnostics.EnumerateArray().First();
        var line = firstDiag.GetProperty("line").GetInt32();
        Assert.Equal(2, line);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public async Task ExecuteAsync_WithIfStatement_ReturnsValid()
    {
        // IF needs an ID in block form: §IF{id} condition
        var args = JsonDocument.Parse("""
            {
                "snippet": "§IF{_if1} (> x 0)\n  §R x\n§EL\n  §R (- 0 x)\n§/I{_if1}",
                "context": {
                    "parameters": [
                        { "name": "x", "type": "i32" }
                    ],
                    "returnType": "i32"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithMatchExpression_ReturnsValid()
    {
        // Match syntax: §W{id} expr
        var args = JsonDocument.Parse("""
            {
                "snippet": "§W{_w1} value\n  §K §SM x\n    §R x\n  §/K\n  §K §NN\n    §R 0\n  §/K\n§/W{_w1}",
                "context": {
                    "parameters": [
                        { "name": "value", "type": "Option<i32>" }
                    ],
                    "returnType": "i32"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithLoop_ReturnsValid()
    {
        // For loop syntax: §L{id:var:start:end:step} ... §/L{id}
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{sum} 0\n§L{l1:i:1:10:1}\n  §B{sum} (+ sum i)\n§/L{l1}\n§R sum",
                "context": {
                    "returnType": "i32"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    #endregion

    #region Column Number Tests

    [Fact]
    public async Task ExecuteAsync_StatementLocation_ColumnNumbersAreCorrect()
    {
        // Statement location adds 2-char indent to each line
        // Token at original column 1 should report as column 1 after adjustment
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R 42",
                "options": { "showTokens": true }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var tokens = json.RootElement.GetProperty("tokens").EnumerateArray().ToList();

        // First token (§R) should be at column 1
        var returnToken = tokens.First(t => t.GetProperty("kind").GetString() == "Return");
        Assert.Equal(1, returnToken.GetProperty("column").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionLocation_ColumnNumbersAreCorrect()
    {
        // Expression location adds "  §B{_result} " (15 chars) prefix
        // Expression at original column 1 should report as column 1 after adjustment
        var args = JsonDocument.Parse("""
            {
                "snippet": "(+ 1 2)",
                "context": { "location": "expression" },
                "options": { "showTokens": true }
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

    [Fact]
    public async Task ExecuteAsync_MultiLineStatement_ColumnNumbersAreCorrectOnAllLines()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{x} 10\n§R x",
                "options": { "showTokens": true }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var tokens = json.RootElement.GetProperty("tokens").EnumerateArray().ToList();

        // §B on line 1 should be at column 1
        var bindToken = tokens.First(t => t.GetProperty("kind").GetString() == "Bind");
        Assert.Equal(1, bindToken.GetProperty("line").GetInt32());
        Assert.Equal(1, bindToken.GetProperty("column").GetInt32());

        // §R on line 2 should be at column 1
        var returnToken = tokens.First(t => t.GetProperty("kind").GetString() == "Return");
        Assert.Equal(2, returnToken.GetProperty("line").GetInt32());
        Assert.Equal(1, returnToken.GetProperty("column").GetInt32());
    }

    #endregion

    #region Warning Tests

    [Fact]
    public async Task ExecuteAsync_ModuleBodyWithParameters_ReturnsWarning()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§F{f001:Test:pub}\n  §O{unit}\n  §R\n§/F{f001}",
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
                "snippet": "§F{f001:Test:pub}\n  §O{unit}\n  §R\n§/F{f001}",
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
                "snippet": "§R x",
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

    [Fact]
    public async Task ExecuteAsync_WithInvalidLocationString_DefaultsToStatement()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R 42",
                "context": {
                    "location": "invalid_location"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithMalformedParameters_IgnoresInvalidOnes()
    {
        // Parameters missing required fields should be silently ignored
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R 42",
                "context": {
                    "parameters": [
                        { "name": "x" },
                        { "type": "i32" },
                        { "name": "y", "type": "i32" }
                    ]
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // Should not crash, and should process the valid parameter
        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeSnippet_HandlesCorrectly()
    {
        // Generate a large snippet with many statements
        var lines = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            lines.AppendLine($"§B{{x{i}}} {i}");
        }
        lines.Append("§R x99");

        var args = JsonDocument.Parse($@"{{
            ""snippet"": {JsonSerializer.Serialize(lines.ToString())}
        }}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithContextAsNull_UsesDefaults()
    {
        var args = JsonDocument.Parse("""
            {
                "snippet": "§R 42",
                "context": null
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("\"valid\":true", text);
    }

    [Fact]
    public async Task ExecuteAsync_DiagnosticColumnNumbers_AreCorrectForStatements()
    {
        // Create an error at a known column position
        // An unterminated string starting at column 8 (after "§B{y} " which is 6 chars)
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{y} STR:\"unterminated"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.NotEmpty(diagnostics);
        // The error should be at a reasonable column (after §B{y} )
        var col = diagnostics[0].GetProperty("column").GetInt32();
        Assert.True(col >= 7, $"Expected column >= 7, got {col}");
    }

    #endregion

    #region Additional Edge Case Tests

    [Fact]
    public async Task ExecuteAsync_MultiLineExpression_ColumnNumbersCorrectOnLine2()
    {
        // Multi-line expression: error on line 2 should have correct column
        // The expression "(+ 1\nSTR:\"bad)" has an unterminated string on line 2
        var args = JsonDocument.Parse("""
            {
                "snippet": "(+ 1\nSTR:\"unterminated",
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
        Assert.Equal(2, diag.GetProperty("line").GetInt32());
        // Column should be reasonable (the STR: starts at column 1)
        var col = diag.GetProperty("column").GetInt32();
        Assert.True(col >= 1, $"Expected column >= 1, got {col}");
    }

    [Fact]
    public async Task ExecuteAsync_MultiLineExpression_TokensCorrectOnAllLines()
    {
        // Multi-line expression with tokens on multiple lines
        var args = JsonDocument.Parse("""
            {
                "snippet": "(+\n  1\n  2)",
                "context": { "location": "expression" },
                "options": { "showTokens": true }
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
                "snippet": "§R 42",
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
                "snippet": "§R STR:\"unterminated",
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
        Assert.Equal(1, diagnostics[0].GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_UnicodeInSnippet_HandlesCorrectly()
    {
        // Snippet with unicode characters
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{name} STR:\"Hello, 世界! 🌍\"",
                "options": { "showTokens": true }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());

        var tokens = json.RootElement.GetProperty("tokens").EnumerateArray().ToList();
        // Should have the string literal token
        var strToken = tokens.FirstOrDefault(t => t.GetProperty("kind").GetString() == "StrLiteral");
        Assert.True(strToken.ValueKind != JsonValueKind.Undefined, "Expected StrLiteral token");
        Assert.Contains("世界", strToken.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_UnicodeIdentifiers_HandlesCorrectly()
    {
        // Some languages allow unicode identifiers
        var args = JsonDocument.Parse("""
            {
                "snippet": "§B{変数} 42\n§R 変数",
                "options": { "showTokens": true }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // Should at least not crash - whether unicode identifiers are valid depends on lexer
        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_VeryLongLine_HandlesCorrectly()
    {
        // Create a very long line (1000+ characters)
        var longExpr = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"x{i}"));
        var snippet = $"§B{{result}} (+ {longExpr})";

        var args = JsonDocument.Parse($@"{{
            ""snippet"": {JsonSerializer.Serialize(snippet)}
        }}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_VeryLongLineWithError_ReportsCorrectColumn()
    {
        // Very long line with error - the STR: token starts after §B{val} (9 chars)
        var padding = new string('x', 500);
        var snippet = $"§B{{val}} STR:\"{padding}";  // Unterminated string

        var args = JsonDocument.Parse($@"{{
            ""snippet"": {JsonSerializer.Serialize(snippet)}
        }}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.False(json.RootElement.GetProperty("valid").GetBoolean());

        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.NotEmpty(diagnostics);

        // Column should be at least 9 (where STR: starts, after "§B{val} ")
        var col = diagnostics[0].GetProperty("column").GetInt32();
        Assert.True(col >= 9, $"Expected column >= 9 for STR: token position, got {col}");
    }

    [Fact]
    public async Task ExecuteAsync_ManyLines_HandlesCorrectly()
    {
        // Create a snippet with many lines (500+)
        var lines = new System.Text.StringBuilder();
        for (int i = 0; i < 500; i++)
        {
            lines.AppendLine($"§B{{x{i}}} {i}");
        }
        lines.Append("§R x499");

        var args = JsonDocument.Parse($@"{{
            ""snippet"": {JsonSerializer.Serialize(lines.ToString())}
        }}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_ManyLinesWithErrorAtEnd_ReportsCorrectLine()
    {
        // Many lines with error on the last line
        var lines = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            lines.AppendLine($"§B{{x{i}}} {i}");
        }
        lines.Append("§R STR:\"unterminated");  // Line 101

        var args = JsonDocument.Parse($@"{{
            ""snippet"": {JsonSerializer.Serialize(lines.ToString())}
        }}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.False(json.RootElement.GetProperty("valid").GetBoolean());

        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.NotEmpty(diagnostics);

        // Error should be on line 101
        var line = diagnostics[0].GetProperty("line").GetInt32();
        Assert.Equal(101, line);
    }

    #endregion
}
