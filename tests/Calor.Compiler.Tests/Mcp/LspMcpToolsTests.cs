using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for LSP-style MCP tools: goto_definition, find_references, symbol_info, document_outline, find_symbol.
/// </summary>
public class LspMcpToolsTests
{
    private static readonly string SampleSource = """
        §M{m001:UserModule}
          §F{f001:calculateTotal:pub}
            §I{f64:total}
            §O{f64}
            §R total
          §/F{f001}

          §CL{c001:Item:pub}
            §FLD{f64:Price:pub}
            §FLD{str:Name:pub}
          §/CL{c001}

          §F{f002:greet:pub}
            §I{str:name}
            §O{str}
            §R name
          §/F{f002}
        §/M{m001}
        """;

    #region GotoDefinitionTool Tests

    [Fact]
    public async Task GotoDefinition_FindsFunction()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 13, column: 15, action: "definition"); // "greet" on function definition

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("found").GetBoolean());
        Assert.Equal("greet", output.GetProperty("symbolName").GetString());
        Assert.Equal("function", output.GetProperty("symbolKind").GetString());
    }

    [Fact]
    public async Task GotoDefinition_FindsParameter()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 3, column: 17, action: "definition"); // "total" parameter

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("found").GetBoolean());
        Assert.Equal("total", output.GetProperty("symbolName").GetString());
        Assert.Equal("parameter", output.GetProperty("symbolKind").GetString());
    }

    [Fact]
    public async Task GotoDefinition_FindsClass()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 8, column: 15, action: "definition"); // "Item" class

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("found").GetBoolean());
        Assert.Equal("Item", output.GetProperty("symbolName").GetString());
        Assert.Equal("class", output.GetProperty("symbolKind").GetString());
    }

    [Fact]
    public async Task GotoDefinition_ReturnsNotFound_WhenNoSymbol()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 1, column: 1, action: "definition"); // Start of file, on §

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.False(output.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task GotoDefinition_RequiresSourceOrFilePath()
    {
        var tool = new NavigateTool();
        var args = JsonDocument.Parse(@"{""action"": ""definition"", ""line"": 1, ""column"": 1}").RootElement;

        var result = await tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    #endregion

    #region SymbolInfoTool Tests

    [Fact]
    public async Task SymbolInfo_ReturnsFunctionInfo()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 2, column: 15, action: "info"); // "calculateTotal"

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("found").GetBoolean());
        Assert.Equal("calculateTotal", output.GetProperty("name").GetString());
        Assert.Equal("function", output.GetProperty("kind").GetString());
        // Type can be returned as "FLOAT" or "f64" depending on normalization
        var typeVal = output.GetProperty("type").GetString();
        Assert.True(typeVal == "f64" || typeVal == "FLOAT", $"Expected f64 or FLOAT, got {typeVal}");
    }

    [Fact]
    public async Task SymbolInfo_ReturnsClassInfo()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 8, column: 15, action: "info"); // "Item" class

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("found").GetBoolean());
        Assert.Equal("Item", output.GetProperty("name").GetString());
        Assert.Equal("class", output.GetProperty("kind").GetString());
        Assert.Equal(2, output.GetProperty("memberCount").GetInt32()); // 2 fields
    }

    [Fact]
    public async Task SymbolInfo_ReturnsFieldInfo()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 9, column: 17, action: "info"); // "Price" field

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("found").GetBoolean());
        Assert.Equal("Price", output.GetProperty("name").GetString());
        Assert.Equal("field", output.GetProperty("kind").GetString());
        // Type can be returned as "FLOAT" or "f64" depending on normalization
        var typeVal = output.GetProperty("type").GetString();
        Assert.True(typeVal == "f64" || typeVal == "FLOAT", $"Expected f64 or FLOAT, got {typeVal}");
    }

    #endregion

    #region DocumentOutlineTool Tests

    [Fact]
    public async Task DocumentOutline_ReturnsStructure()
    {
        var tool = new StructureTool();
        var args = CreateOutlineArgs(SampleSource);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.Equal("UserModule", output.GetProperty("moduleName").GetString());

        var symbols = output.GetProperty("symbols");
        Assert.Equal(3, symbols.GetArrayLength()); // 2 functions + 1 class

        var summary = output.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("functionCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("classCount").GetInt32());
    }

    [Fact]
    public async Task DocumentOutline_IncludesClassChildren()
    {
        var tool = new StructureTool();
        var args = CreateOutlineArgs(SampleSource);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        var symbols = output.GetProperty("symbols");

        // Find the Item class
        JsonElement? classSymbol = null;
        foreach (var symbol in symbols.EnumerateArray())
        {
            if (symbol.GetProperty("name").GetString() == "Item")
            {
                classSymbol = symbol;
                break;
            }
        }

        Assert.NotNull(classSymbol);
        Assert.True(classSymbol.Value.TryGetProperty("children", out var children));
        Assert.Equal(2, children.GetArrayLength()); // Price and Name fields
    }

    [Fact]
    public async Task DocumentOutline_WithoutDetails()
    {
        var tool = new StructureTool();
        var args = CreateArgsWithOptions(SampleSource, ("action", "outline"), ("includeDetails", "false"));

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("success").GetBoolean());

        // Functions should not have children (parameters) when includeDetails is false
        var symbols = output.GetProperty("symbols");
        foreach (var symbol in symbols.EnumerateArray())
        {
            if (symbol.GetProperty("kind").GetString() == "function")
            {
                // Children should be null or not present when includeDetails is false
                Assert.False(symbol.TryGetProperty("children", out _));
            }
        }
    }

    #endregion

    #region FindSymbolTool Tests

    [Fact]
    public async Task FindSymbol_FindsByName()
    {
        var tool = new NavigateTool();
        var args = CreateFindSymbolArgs(SampleSource, "calculate");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.True(output.GetProperty("matchCount").GetInt32() >= 1);

        var matches = output.GetProperty("matches");
        var found = false;
        foreach (var match in matches.EnumerateArray())
        {
            if (match.GetProperty("name").GetString() == "calculateTotal")
            {
                found = true;
                Assert.Equal("function", match.GetProperty("kind").GetString());
            }
        }
        Assert.True(found);
    }

    [Fact]
    public async Task FindSymbol_FiltersByKind()
    {
        var tool = new NavigateTool();
        var args = CreateFindSymbolArgsWithKind(SampleSource, "Item", "class");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.Equal(1, output.GetProperty("matchCount").GetInt32());

        var matches = output.GetProperty("matches");
        var match = matches[0];
        Assert.Equal("Item", match.GetProperty("name").GetString());
        Assert.Equal("class", match.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task FindSymbol_RespectsLimit()
    {
        var tool = new NavigateTool();
        var args = CreateFindSymbolArgsWithLimit(SampleSource, "e", 2);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.True(output.GetProperty("matchCount").GetInt32() <= 2);
    }

    #endregion

    #region FindReferencesTool Tests

    [Fact]
    public async Task FindReferences_FindsAllUsages()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 3, column: 17, action: "references"); // "total" parameter

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.Equal("total", output.GetProperty("symbolName").GetString());

        // "total" appears in parameter and return
        var references = output.GetProperty("references");
        Assert.True(references.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task FindReferences_MarksDefinition()
    {
        var tool = new NavigateTool();
        var args = CreateArgs(SampleSource, line: 3, column: 17, action: "references"); // "total" parameter

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        var references = output.GetProperty("references");

        // At least one reference should be marked as definition
        var hasDefinition = false;
        foreach (var reference in references.EnumerateArray())
        {
            if (reference.GetProperty("isDefinition").GetBoolean())
            {
                hasDefinition = true;
                break;
            }
        }
        Assert.True(hasDefinition);
    }

    [Fact]
    public async Task FindReferences_ExcludesDefinition_WhenRequested()
    {
        var tool = new NavigateTool();
        var args = CreateFindReferencesArgs(SampleSource, 3, 17, includeDefinition: false);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var output = ParseOutput(result.Content);
        var references = output.GetProperty("references");

        // No reference should be marked as definition
        foreach (var reference in references.EnumerateArray())
        {
            Assert.False(reference.GetProperty("isDefinition").GetBoolean());
        }
    }

    #endregion

    #region Helper Methods

    private static JsonElement CreateArgs(string source, int line, int column, string? action = null)
    {
        var obj = new Dictionary<string, object>
        {
            ["source"] = source,
            ["line"] = line,
            ["column"] = column
        };
        if (action != null)
            obj["action"] = action;
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateOutlineArgs(string source)
    {
        var obj = new Dictionary<string, object>
        {
            ["action"] = "outline",
            ["source"] = source
        };
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateSourceArgs(string source)
    {
        var obj = new Dictionary<string, object>
        {
            ["source"] = source
        };
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateArgsWithOptions(string source, params (string key, string value)[] options)
    {
        var obj = new Dictionary<string, object>
        {
            ["source"] = source
        };
        foreach (var (key, value) in options)
        {
            if (value == "false")
                obj[key] = false;
            else if (value == "true")
                obj[key] = true;
            else
                obj[key] = value;
        }
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateFindSymbolArgs(string source, string query)
    {
        var obj = new Dictionary<string, object>
        {
            ["action"] = "find",
            ["source"] = source,
            ["query"] = query
        };
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateFindSymbolArgsWithKind(string source, string query, string kind)
    {
        var obj = new Dictionary<string, object>
        {
            ["action"] = "find",
            ["source"] = source,
            ["query"] = query,
            ["kind"] = kind
        };
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateFindSymbolArgsWithLimit(string source, string query, int limit)
    {
        var obj = new Dictionary<string, object>
        {
            ["action"] = "find",
            ["source"] = source,
            ["query"] = query,
            ["limit"] = limit
        };
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateFindReferencesArgs(string source, int line, int column, bool includeDefinition)
    {
        var obj = new Dictionary<string, object>
        {
            ["action"] = "references",
            ["source"] = source,
            ["line"] = line,
            ["column"] = column,
            ["includeDefinition"] = includeDefinition
        };
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement ParseOutput(IReadOnlyList<Calor.Compiler.Mcp.McpContent> content)
    {
        var text = content[0].Text ?? throw new InvalidOperationException("No text content");
        return JsonDocument.Parse(text).RootElement;
    }

    #endregion
}
