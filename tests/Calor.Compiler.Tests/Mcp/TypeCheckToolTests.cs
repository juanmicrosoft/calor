using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class TypeCheckToolTests
{
    private readonly TypeCheckTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorTypecheck()
    {
        Assert.Equal("calor_typecheck", _tool.Name);
    }

    [Fact]
    public void Description_ContainsTypeCheckInfo()
    {
        Assert.Contains("Type check", _tool.Description);
        Assert.Contains("Calor", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("filePath", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithValidSource_ReturnsSuccess()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        Assert.NotEmpty(result.Content);

        var text = result.Content[0].Text;
        Assert.NotNull(text);
        Assert.Contains("success", text);
        Assert.Contains("true", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;

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

    [Fact]
    public async Task ExecuteAsync_WithFilePath_UsesInDiagnostics()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n§/F{f001}\n§/M{m001}",
                "filePath": "test-file.calr"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("typeErrors", text);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsStructuredOutput()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        Assert.True(json.RootElement.TryGetProperty("success", out _));
        Assert.True(json.RootElement.TryGetProperty("errorCount", out _));
        Assert.True(json.RootElement.TryGetProperty("warningCount", out _));
        Assert.True(json.RootElement.TryGetProperty("typeErrors", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithSyntaxError_ReportsError()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Bad} invalid syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("typeErrors", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithUndefinedReference_ReportsUndefinedReferenceCategory()
    {
        // Source with undefined variable reference
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Test:pub}\n§O{i32}\n§B{counter} 0\n§R undefinedVar\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.GetProperty("errorCount").GetInt32() > 0);

        var typeErrors = json.RootElement.GetProperty("typeErrors");
        Assert.True(typeErrors.GetArrayLength() > 0);

        // Find the undefined reference error
        var hasUndefinedRef = false;
        foreach (var error in typeErrors.EnumerateArray())
        {
            if (error.GetProperty("category").GetString() == "undefined_reference")
            {
                hasUndefinedRef = true;
                Assert.Equal("Calor0200", error.GetProperty("code").GetString());
                Assert.Contains("undefinedVar", error.GetProperty("message").GetString());
                Assert.Equal("error", error.GetProperty("severity").GetString());
                break;
            }
        }
        Assert.True(hasUndefinedRef, "Expected undefined_reference category error");
    }

    [Fact]
    public async Task ExecuteAsync_WithTypeMismatch_ReportsTypeMismatchCategory()
    {
        // Source with type mismatch: WHILE condition must be BOOL, not INT
        // §WH{id} (condition) is the correct WHILE syntax
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Test:pub}\n§O{i32}\n§B{x} 0\n§WH{wh1} (+ x 1)\n  §B{x} (+ x 1)\n§/WH{wh1}\n§R x\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        Assert.False(json.RootElement.GetProperty("success").GetBoolean());

        var typeErrors = json.RootElement.GetProperty("typeErrors");
        Assert.True(typeErrors.GetArrayLength() > 0);

        // Find the type mismatch error
        var hasTypeMismatch = false;
        foreach (var error in typeErrors.EnumerateArray())
        {
            if (error.GetProperty("category").GetString() == "type_mismatch")
            {
                hasTypeMismatch = true;
                Assert.Equal("Calor0202", error.GetProperty("code").GetString());
                Assert.Contains("BOOL", error.GetProperty("message").GetString());
                break;
            }
        }
        Assert.True(hasTypeMismatch, "Expected type_mismatch category error");
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleErrors_ReportsAllErrors()
    {
        // Source with multiple errors: undefined var AND type mismatch (WHILE condition not BOOL)
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Test:pub}\n§O{i32}\n§B{x} 0\n§WH{wh1} (+ x 1)\n  §B{y} undefinedVar\n§/WH{wh1}\n§R x\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        var errorCount = json.RootElement.GetProperty("errorCount").GetInt32();
        Assert.True(errorCount >= 2, $"Expected at least 2 errors, got {errorCount}");

        var typeErrors = json.RootElement.GetProperty("typeErrors");
        var categories = new HashSet<string>();
        foreach (var error in typeErrors.EnumerateArray())
        {
            categories.Add(error.GetProperty("category").GetString()!);
        }

        Assert.Contains("type_mismatch", categories);
        Assert.Contains("undefined_reference", categories);
    }

    [Fact]
    public async Task ExecuteAsync_ErrorsIncludeLineAndColumn()
    {
        // Source with error on specific line
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Test:pub}\n§O{i32}\n§R undefinedVar\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        var typeErrors = json.RootElement.GetProperty("typeErrors");
        Assert.True(typeErrors.GetArrayLength() > 0);

        var firstError = typeErrors[0];
        Assert.True(firstError.TryGetProperty("line", out var line));
        Assert.True(firstError.TryGetProperty("column", out var column));
        Assert.True(line.GetInt32() > 0, "Line should be positive");
        Assert.True(column.GetInt32() >= 0, "Column should be non-negative");
    }

    [Fact]
    public async Task ExecuteAsync_IfConditionTypeMismatch_ReportsError()
    {
        // IF condition must be BOOL, using INT should fail
        // §IF{id} condition syntax
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Test:pub}\n§I{i32:x}\n§O{i32}\n§IF{if1} (+ x 1)\n  §R 1\n§/I{if1}\n§R 0\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("type_mismatch", text);
        Assert.Contains("BOOL", text);
    }

    [Fact]
    public async Task ExecuteAsync_ForLoopWithNonNumericBounds_ReportsError()
    {
        // FOR loop bounds must be numeric
        // §L{id:var:from:to:step} syntax - using BOOL:true as from should fail
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Test:pub}\n§O{i32}\n§B{x} 0\n§L{l1:i:BOOL:true:10:1}\n  §B{x} (+ x i)\n§/L{l1}\n§R x\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("type_mismatch", text);
        Assert.Contains("numeric", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_ValidComplexSource_NoErrors()
    {
        // Complex but valid source with multiple constructs
        // Using correct IF syntax: §IF{id} condition ... §EL ... §/I{id}
        // Using correct FOR syntax: §L{id:var:from:to:step} ... §/L{id}
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Complex:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§B{result} 0\n§IF{if1} (> a b)\n  §B{result} a\n§EL\n  §B{result} b\n§/I{if1}\n§L{l1:i:0:result:1}\n  §B{result} (+ result i)\n§/L{l1}\n§R result\n§/F{f001}\n§/M{m001}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("errorCount").GetInt32());
    }
}
