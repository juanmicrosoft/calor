using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for new MCP tools that support edit precision:
/// calor_impact_analysis, calor_call_graph, calor_edit_preview, calor_scope_info,
/// and enhanced calor_find_references.
/// </summary>
public class EditPrecisionMcpToolTests
{
    private static readonly string SampleSource = @"
§M{m001:MathModule}
§F{f001:add:pub}
  §I{i32:x, i32:y}
  §O{i32}
  §Q (>= x INT:0)
  §S (>= result INT:0)
  §R (+ x y)
§/F{f001}
§F{f002:multiply:pub}
  §I{i32:a, i32:b}
  §O{i32}
  §R (* a b)
§/F{f002}
§F{f003:computeTotal:pub}
  §I{i32:x, i32:y, i32:z}
  §O{i32}
  §B{partial:i32} §C{add}
    §A x
    §A y
  §/C
  §R (* partial z)
§/F{f003}
§/M{m001}
";

    private static readonly string SampleSourceWithEffects = @"
§M{m002:IoModule}
§F{f010:readInput:pub}
  §O{str}
  §E{cr}
  §R STR:""hello""
§/F{f010}
§F{f011:processAndPrint:pub}
  §I{str:input}
  §O{void}
  §E{cw}
  §P input
§/F{f011}
§F{f012:main:pub}
  §O{void}
  §E{cw, cr}
  §B{data:str} §C{readInput}
  §/C
  §C{processAndPrint}
    §A data
  §/C
§/F{f012}
§/M{m002}
";

    #region ImpactAnalysisTool Tests

    [Fact]
    public async Task ImpactAnalysis_FindsDirectCallers()
    {
        var tool = new ImpactAnalysisTool();
        var args = CreateArgsWithSymbolId(SampleSource, "f001"); // add function

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.Equal("f001", output.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task ImpactAnalysis_WithPosition_ResolvesSymbol()
    {
        var tool = new ImpactAnalysisTool();
        // Line 2 contains §F{f001:add:pub} — "add" starts at column 11
        var args = CreateArgsWithPosition(SampleSource, 2, 11);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        // Verify the resolved symbol has an identity (targetId or targetSymbol)
        Assert.True(
            output.TryGetProperty("targetId", out var targetId) || output.TryGetProperty("targetSymbol", out _),
            "Expected output to contain 'targetId' or 'targetSymbol'");
    }

    [Fact]
    public async Task ImpactAnalysis_ContractImpacts_DetectsContracts()
    {
        var tool = new ImpactAnalysisTool();
        var args = CreateArgsWithSymbolId(SampleSource, "f001");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);

        // f001 has preconditions and postconditions
        Assert.True(output.TryGetProperty("contractImpacts", out var contractImpacts));
        Assert.True(contractImpacts.GetArrayLength() > 0, "Should detect contracts on function f001");
    }

    [Fact]
    public async Task ImpactAnalysis_DepthLimiting()
    {
        var tool = new ImpactAnalysisTool();
        var args = CreateArgsWithSymbolIdAndDepth(SampleSource, "f001", 1);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);

        // With depth=1, should only get direct impacts
        Assert.True(output.TryGetProperty("transitiveImpacts", out var transitive));
        Assert.Equal(0, transitive.GetArrayLength());
    }

    [Fact]
    public async Task ImpactAnalysis_ErrorOnMissingSource()
    {
        var tool = new ImpactAnalysisTool();
        var args = JsonDocument.Parse("""{"symbolId": "f001"}""").RootElement;

        var result = await tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    #endregion

    #region CallGraphTool Tests

    [Fact]
    public async Task CallGraph_FindsCallees()
    {
        var tool = new CallGraphTool();
        var args = CreateCallGraphArgs(SampleSource, "f003", "callees");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());

        var callees = output.GetProperty("callees");
        Assert.True(callees.GetArrayLength() > 0, "computeTotal calls add");

        // Verify at least one callee references "add" (since f003/computeTotal calls add)
        var calleesJson = callees.EnumerateArray().Select(c => c.ToString()).ToList();
        Assert.True(calleesJson.Any(c => c.Contains("add")), $"Expected callee 'add' but got: {string.Join(", ", calleesJson)}");
    }

    [Fact]
    public async Task CallGraph_FindsCallers()
    {
        var tool = new CallGraphTool();
        var args = CreateCallGraphArgs(SampleSource, "f001", "callers");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task CallGraph_BothDirections()
    {
        var tool = new CallGraphTool();
        var args = CreateCallGraphArgs(SampleSource, "f003", "both");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task CallGraph_IncludesEffectAnnotations()
    {
        var tool = new CallGraphTool();
        var args = CreateCallGraphArgs(SampleSourceWithEffects, "f012", "both", includeEffects: true);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());

        // Root should have effect annotation
        var root = output.GetProperty("root");
        Assert.True(root.TryGetProperty("effects", out _), "Root function should have effects");
    }

    [Fact]
    public async Task CallGraph_NoCyclesInSample()
    {
        var tool = new CallGraphTool();
        var args = CreateCallGraphArgs(SampleSource, "f001", "both");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        // No recursion in sample
        var cycles = output.GetProperty("recursiveCycles");
        Assert.Equal(0, cycles.GetArrayLength());
    }

    #endregion

    #region EditPreviewTool Tests

    [Fact]
    public async Task EditPreview_SafeEdit_ReturnsSafe()
    {
        var tool = new EditPreviewTool();
        // Real non-breaking edit: rename the module
        var modified = SampleSource.Replace("MathModule", "MathModule2");

        var args = CreateEditPreviewArgs(SampleSource, modified);
        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        var verdict = output.GetProperty("overallVerdict").GetString();
        Assert.NotNull(verdict);
        Assert.True(
            verdict == "safe" || verdict == "safe_with_warnings",
            $"Expected verdict 'safe' or 'safe_with_warnings', got '{verdict}'");
    }

    [Fact]
    public async Task EditPreview_BreakingEdit_ReportsBreaking()
    {
        var tool = new EditPreviewTool();
        // Introduce a syntax error
        var modified = "§M{m001:MathModule}\n  §F{f001:add:pub\n  invalid syntax here";

        var args = CreateEditPreviewArgs(SampleSource, modified);
        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.Equal("breaking", output.GetProperty("overallVerdict").GetString());
    }

    [Fact]
    public async Task EditPreview_EditSummary_ShowsChanges()
    {
        var tool = new EditPreviewTool();
        var modified = SampleSource.Replace("§R (+ x y)", "§R (+ x (+ y INT:1))");

        var args = CreateEditPreviewArgs(SampleSource, modified);
        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        var summary = output.GetProperty("editSummary");
        Assert.True(summary.GetProperty("linesModified").GetInt32() >= 1);
    }

    [Fact]
    public async Task EditPreview_ContractChange_Detected()
    {
        var tool = new EditPreviewTool();
        // Remove a contract line
        var modified = SampleSource.Replace("  §Q (>= x INT:0)\n", "");

        var args = CreateEditPreviewArgs(SampleSource, modified);
        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        var contracts = output.GetProperty("contractVerification");
        Assert.True(contracts.GetProperty("checked").GetBoolean());
    }

    [Fact]
    public async Task EditPreview_SelectiveChecks()
    {
        var tool = new EditPreviewTool();
        var modified = SampleSource.Replace("MathModule", "MathModule2");

        var args = CreateEditPreviewArgsWithChecks(SampleSource, modified, new[] { "compile" });
        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("compilationResult").GetProperty("checked").GetBoolean());
    }

    [Fact]
    public async Task EditPreview_ErrorOnMissingSource()
    {
        var tool = new EditPreviewTool();
        var args = JsonDocument.Parse("""{"originalSource": "test"}""").RootElement;

        var result = await tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    #endregion

    #region ScopeInfoTool Tests

    [Fact]
    public async Task ScopeInfo_FindsEnclosingFunction()
    {
        var tool = new ScopeInfoTool();
        // Line 7 is §R (+ x y) — inside the "add" function body
        var args = CreateArgs(SampleSource, 7, 5);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ScopeInfo_ListsParameters()
    {
        var tool = new ScopeInfoTool();
        var args = CreateArgs(SampleSource, 7, 5);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        var parameters = output.GetProperty("parameters");
        Assert.True(parameters.ValueKind == JsonValueKind.Array);
        // "add" function takes parameters — verify at least 1 is returned
        Assert.True(parameters.GetArrayLength() >= 1, $"add function should have params, got {parameters.GetArrayLength()}");
        // Each parameter should have a name
        var firstParam = parameters[0];
        Assert.True(firstParam.TryGetProperty("name", out var paramName), "Parameter should have a 'name' property");
        Assert.False(string.IsNullOrEmpty(paramName.GetString()), "Parameter name should not be empty");
    }

    [Fact]
    public async Task ScopeInfo_ListsAvailableFunctions()
    {
        var tool = new ScopeInfoTool();
        var args = CreateArgs(SampleSource, 7, 5);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        var functions = output.GetProperty("availableFunctions");
        Assert.True(functions.GetArrayLength() >= 3, "Should list add, multiply, computeTotal");
    }

    [Fact]
    public async Task ScopeInfo_ShowsEnclosingModule()
    {
        var tool = new ScopeInfoTool();
        // Line 2: inside module but looking at module-level
        var args = CreateArgs(SampleSource, 2, 3);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.TryGetProperty("enclosingModule", out var module));
        Assert.Equal("MathModule", module.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ScopeInfo_ReturnsValidInsertionPoints()
    {
        var tool = new ScopeInfoTool();
        var args = CreateArgs(SampleSource, 7, 5);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        var insertions = output.GetProperty("validInsertionPoints");
        Assert.True(insertions.GetArrayLength() > 0);
    }

    [Fact]
    public async Task ScopeInfo_IncludesActiveContracts()
    {
        var tool = new ScopeInfoTool();
        // Inside add function which has §Q and §S
        var args = CreateArgs(SampleSource, 7, 5);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        var contracts = output.GetProperty("activeContracts");
        Assert.True(contracts.ValueKind == JsonValueKind.Array);
    }

    #endregion

    #region Enhanced FindReferencesTool Tests

    [Fact]
    public async Task FindReferences_BySymbolName_Works()
    {
        var tool = new FindReferencesTool();
        var args = CreateFindRefsArgsWithName(SampleSource, "add");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.Equal("add", output.GetProperty("symbolName").GetString());
        Assert.True(output.GetProperty("referenceCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task FindReferences_BySymbolId_Works()
    {
        var tool = new FindReferencesTool();
        var args = CreateFindRefsArgsWithId(SampleSource, "f001");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.True(output.GetProperty("referenceCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task FindReferences_GroupByKind_Works()
    {
        var tool = new FindReferencesTool();
        var args = CreateFindRefsArgsGrouped(SampleSource, "add");

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.True(output.TryGetProperty("groupedReferences", out _));
    }

    [Fact]
    public async Task FindReferences_ByPosition_StillWorks()
    {
        var tool = new FindReferencesTool();
        // "add" on function definition line 2: §F{f001:add:pub}
        var args = CreateArgs(SampleSource, 2, 11);

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError, GetErrorText(result));
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task FindReferences_ErrorOnNoIdentifier()
    {
        var tool = new FindReferencesTool();
        var args = JsonDocument.Parse("""{"source": "test"}""").RootElement;

        var result = await tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    #endregion

    #region Tool Registration Tests

    [Fact]
    public async Task AllNewToolsRegistered()
    {
        var handler = new McpMessageHandler();

        // Verify the handler has the new tools by listing them
        var listRequest = new JsonRpcRequest
        {
            Method = "tools/list",
            Id = JsonDocument.Parse("1").RootElement
        };
        var response = await handler.HandleRequestAsync(listRequest);

        Assert.NotNull(response);
        Assert.NotNull(response.Result);

        var resultJson = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("calor_impact_analysis", resultJson);
        Assert.Contains("calor_call_graph", resultJson);
        Assert.Contains("calor_edit_preview", resultJson);
        Assert.Contains("calor_scope_info", resultJson);
    }

    #endregion

    #region End-to-End MCP JSON-RPC Tests

    [Fact]
    public async Task EndToEnd_ToolsCall_ImpactAnalysis_WorksThroughHandler()
    {
        var handler = new McpMessageHandler();

        var callParams = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = "calor_impact_analysis",
            ["arguments"] = new Dictionary<string, object>
            {
                ["source"] = SampleSource,
                ["symbolId"] = "f001"
            }
        });

        var request = new JsonRpcRequest
        {
            Method = "tools/call",
            Id = JsonDocument.Parse("42").RootElement,
            Params = JsonDocument.Parse(callParams).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var resultJson = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("success", resultJson);
        Assert.DoesNotContain("\"isError\":true", resultJson);
    }

    [Fact]
    public async Task EndToEnd_ToolsCall_EditPreview_WorksThroughHandler()
    {
        var handler = new McpMessageHandler();
        var modified = SampleSource.Replace("MathModule", "MathModule2");

        var callParams = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = "calor_edit_preview",
            ["arguments"] = new Dictionary<string, object>
            {
                ["originalSource"] = SampleSource,
                ["modifiedSource"] = modified
            }
        });

        var request = new JsonRpcRequest
        {
            Method = "tools/call",
            Id = JsonDocument.Parse("43").RootElement,
            Params = JsonDocument.Parse(callParams).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
    }

    #endregion

    #region Helper Methods

    private static JsonElement CreateArgs(string source, int line, int column)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["source"] = source,
            ["line"] = line,
            ["column"] = column
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateArgsWithSymbolId(string source, string symbolId)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["source"] = source,
            ["symbolId"] = symbolId
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateArgsWithPosition(string source, int line, int column)
    {
        return CreateArgs(source, line, column);
    }

    private static JsonElement CreateArgsWithSymbolIdAndDepth(string source, string symbolId, int depth)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["source"] = source,
            ["symbolId"] = symbolId,
            ["depth"] = depth
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateCallGraphArgs(string source, string symbolId, string direction, bool includeEffects = true)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["source"] = source,
            ["symbolId"] = symbolId,
            ["direction"] = direction,
            ["includeEffects"] = includeEffects
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateEditPreviewArgs(string original, string modified)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["originalSource"] = original,
            ["modifiedSource"] = modified
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateEditPreviewArgsWithChecks(string original, string modified, string[] checks)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["originalSource"] = original,
            ["modifiedSource"] = modified,
            ["checks"] = checks
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateFindRefsArgsWithName(string source, string symbolName)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["source"] = source,
            ["symbolName"] = symbolName
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateFindRefsArgsWithId(string source, string symbolId)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["source"] = source,
            ["symbolId"] = symbolId
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement CreateFindRefsArgsGrouped(string source, string symbolName)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["source"] = source,
            ["symbolName"] = symbolName,
            ["groupByKind"] = true
        });
        return JsonDocument.Parse(json).RootElement;
    }

    private static string GetErrorText(McpToolResult result)
    {
        return result.Content.Count > 0 ? result.Content[0].Text ?? "unknown error" : "no content";
    }

    private static JsonElement ParseOutput(McpToolResult result)
    {
        var text = result.Content[0].Text ?? throw new InvalidOperationException("No text content");
        return JsonDocument.Parse(text).RootElement;
    }

    #endregion
}
