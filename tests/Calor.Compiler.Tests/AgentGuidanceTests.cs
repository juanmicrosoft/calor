using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for M3: Agent Guidance and Validation (GuardDiscovery, TypeSuggester, ObligationPolicy, MCP tools).
/// </summary>
public sealed class AgentGuidanceTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    // ───── ObligationPolicy ─────

    [Fact]
    public void Policy_Default_FailedIsError()
    {
        var policy = ObligationPolicy.Default;
        Assert.Equal(ObligationAction.Error, policy.GetAction(ObligationStatus.Failed));
    }

    [Fact]
    public void Policy_Default_BoundaryIsAlwaysGuard()
    {
        var policy = ObligationPolicy.Default;
        Assert.Equal(ObligationAction.AlwaysGuard, policy.GetAction(ObligationStatus.Boundary));
    }

    [Fact]
    public void Policy_Default_TimeoutIsWarnAndGuard()
    {
        var policy = ObligationPolicy.Default;
        Assert.Equal(ObligationAction.WarnAndGuard, policy.GetAction(ObligationStatus.Timeout));
    }

    [Fact]
    public void Policy_Default_DischargedIsIgnore()
    {
        var policy = ObligationPolicy.Default;
        Assert.Equal(ObligationAction.Ignore, policy.GetAction(ObligationStatus.Discharged));
    }

    [Fact]
    public void Policy_Strict_AllNonDischargedAreErrors()
    {
        var policy = ObligationPolicy.Strict;
        Assert.Equal(ObligationAction.Error, policy.GetAction(ObligationStatus.Failed));
        Assert.Equal(ObligationAction.Error, policy.GetAction(ObligationStatus.Timeout));
        Assert.Equal(ObligationAction.Error, policy.GetAction(ObligationStatus.Boundary));
        Assert.Equal(ObligationAction.Error, policy.GetAction(ObligationStatus.Unsupported));
        Assert.Equal(ObligationAction.Ignore, policy.GetAction(ObligationStatus.Discharged));
    }

    [Fact]
    public void Policy_Permissive_NothingIsError()
    {
        var policy = ObligationPolicy.Permissive;
        Assert.NotEqual(ObligationAction.Error, policy.GetAction(ObligationStatus.Failed));
        Assert.NotEqual(ObligationAction.Error, policy.GetAction(ObligationStatus.Timeout));
        Assert.NotEqual(ObligationAction.Error, policy.GetAction(ObligationStatus.Boundary));
    }

    [Fact]
    public void Policy_RequiresGuard_CorrectForActions()
    {
        Assert.True(ObligationPolicy.RequiresGuard(ObligationAction.WarnAndGuard));
        Assert.True(ObligationPolicy.RequiresGuard(ObligationAction.AlwaysGuard));
        Assert.False(ObligationPolicy.RequiresGuard(ObligationAction.Error));
        Assert.False(ObligationPolicy.RequiresGuard(ObligationAction.WarnOnly));
        Assert.False(ObligationPolicy.RequiresGuard(ObligationAction.Ignore));
    }

    // ───── GuardDiscovery ─────

    [Fact]
    public void GuardDiscovery_FailedRefinementEntry_SuggestsPreconditionAndGuard()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:priv}
              §I{i32:x} | (>= # INT:0)
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        // Mark as failed for guard discovery
        foreach (var obl in tracker.Obligations)
            obl.Status = ObligationStatus.Failed;

        var discovery = new GuardDiscovery();
        var guards = discovery.DiscoverGuards(tracker);

        Assert.True(guards.Count >= 2);
        Assert.Contains(guards, g => g.InsertionKind == "precondition");
        Assert.Contains(guards, g => g.InsertionKind == "if_guard");
        Assert.Contains(guards, g => g.Confidence == "high");
    }

    [Fact]
    public void GuardDiscovery_FailedProofObligation_SuggestsGuards()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x}
              §O{void}
              §PROOF{p1:check} (>= x INT:0)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        foreach (var obl in tracker.Obligations)
            obl.Status = ObligationStatus.Failed;

        var discovery = new GuardDiscovery();
        var guards = discovery.DiscoverGuards(tracker);

        Assert.NotEmpty(guards);
        Assert.Contains(guards, g => g.CalorExpression.Contains("§Q"));
    }

    [Fact]
    public void GuardDiscovery_DischargedObligations_NoGuards()
    {
        var tracker = new ObligationTracker();
        var dummyExpr = new IntLiteralNode(new TextSpan(0, 0, 1, 1), 0);
        var obl = tracker.Add(ObligationKind.ProofObligation, "f1", "test", dummyExpr, new TextSpan(0, 0, 1, 1));
        obl.Status = ObligationStatus.Discharged;

        var discovery = new GuardDiscovery();
        var guards = discovery.DiscoverGuards(tracker);

        Assert.Empty(guards);
    }

    // ───── TypeSuggester ─────

    [Fact]
    public void TypeSuggester_ParameterUsedAsDivisor_SuggestsNonZero()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Divide:pub}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §R (/ x y)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var suggester = new TypeSuggester();
        var suggestions = suggester.Suggest(module);

        Assert.Contains(suggestions, s =>
            s.ParameterName == "y" &&
            s.SuggestedPredicate.Contains("!=") &&
            s.Confidence == "high");
    }

    [Fact]
    public void TypeSuggester_ParameterAlreadyRefined_NoSuggestion()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Divide:pub}
              §I{i32:x}
              §I{i32:y} | (!= # INT:0)
              §O{i32}
              §R (/ x y)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var suggester = new TypeSuggester();
        var suggestions = suggester.Suggest(module);

        // y already has inline refinement — no suggestion needed
        Assert.DoesNotContain(suggestions, s => s.ParameterName == "y");
    }

    [Fact]
    public void TypeSuggester_ParameterWithPrecondition_NoSuggestion()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Divide:pub}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §Q (!= y INT:0)
              §R (/ x y)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var suggester = new TypeSuggester();
        var suggestions = suggester.Suggest(module);

        // y already has a precondition — no suggestion needed
        Assert.DoesNotContain(suggestions, s => s.ParameterName == "y");
    }

    [Fact]
    public void TypeSuggester_NoPatterns_NoSuggestions()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x}
              §O{i32}
              §R (+ x INT:1)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var suggester = new TypeSuggester();
        var suggestions = suggester.Suggest(module);

        Assert.Empty(suggestions);
    }

    // ───── MCP Tools ─────

    [Fact]
    public void GuardDiscoveryTool_Name_IsCorrect()
    {
        var tool = new GuardDiscoveryTool();
        Assert.Equal("calor_discover_guards", tool.Name);
    }

    [Fact]
    public void TypeSuggestionTool_Name_IsCorrect()
    {
        var tool = new TypeSuggestionTool();
        Assert.Equal("calor_suggest_types", tool.Name);
    }

    [Fact]
    public void DiagnoseRefinementTool_Name_IsCorrect()
    {
        var tool = new DiagnoseRefinementTool();
        Assert.Equal("calor_diagnose_refinement", tool.Name);
    }

    [Fact]
    public async Task GuardDiscoveryTool_WithValidSource_ReturnsGuards()
    {
        var tool = new GuardDiscoveryTool();
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x} | (>= # INT:0)
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            { "source": {{JsonSerializer.Serialize(source)}} }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);
        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.TryGetProperty("guards", out _));
    }

    [Fact]
    public async Task TypeSuggestionTool_WithDivisor_SuggestsNonZero()
    {
        var tool = new TypeSuggestionTool();
        var source = """
            §M{m001:Test}
            §F{f001:Divide:pub}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §R (/ x y)
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            { "source": {{JsonSerializer.Serialize(source)}} }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);
        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());

        var suggestions = json.RootElement.GetProperty("suggestions");
        Assert.True(suggestions.GetArrayLength() > 0);
    }

    [Fact]
    public async Task DiagnoseRefinementTool_WithFailures_ReturnsPatches()
    {
        var tool = new DiagnoseRefinementTool();
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x} | (>= # INT:0)
              §I{i32:y}
              §O{i32}
              §PROOF{p1:positive} (> x INT:0)
              §R (/ x y)
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            { "source": {{JsonSerializer.Serialize(source)}} }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);
        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);

        Assert.True(json.RootElement.TryGetProperty("patches", out var patches));
        Assert.True(patches.GetArrayLength() > 0);
        Assert.True(json.RootElement.TryGetProperty("policy", out var policy));
        Assert.Equal("default", policy.GetString());
    }

    [Fact]
    public async Task DiagnoseRefinementTool_WithStrictPolicy_SetsPolicy()
    {
        var tool = new DiagnoseRefinementTool();
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            { "source": {{JsonSerializer.Serialize(source)}}, "policy": "strict" }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal("strict", json.RootElement.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task AllMcpTools_WithMissingSource_ReturnError()
    {
        var empty = JsonDocument.Parse("{}").RootElement;

        var tools = new McpToolBase[]
        {
            new GuardDiscoveryTool(),
            new TypeSuggestionTool(),
            new DiagnoseRefinementTool()
        };

        foreach (var tool in tools)
        {
            var result = await tool.ExecuteAsync(empty);
            Assert.True(result.IsError, $"{tool.Name} should error on missing source");
        }
    }

    [Fact]
    public void AllMcpTools_HaveValidSchema()
    {
        var tools = new McpToolBase[]
        {
            new GuardDiscoveryTool(),
            new TypeSuggestionTool(),
            new DiagnoseRefinementTool()
        };

        foreach (var tool in tools)
        {
            var schema = tool.GetInputSchema();
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            Assert.True(schema.TryGetProperty("properties", out var props),
                $"{tool.Name} schema missing properties");
            Assert.True(props.TryGetProperty("source", out _),
                $"{tool.Name} schema missing source property");
        }
    }
}
