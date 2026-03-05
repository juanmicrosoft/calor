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
    public void RefineTool_Guards_Name_IsCorrect()
    {
        var tool = new RefineTool();
        Assert.Equal("calor_refine", tool.Name);
    }

    [Fact]
    public void RefineTool_Types_Name_IsCorrect()
    {
        var tool = new RefineTool();
        Assert.Equal("calor_refine", tool.Name);
    }

    [Fact]
    public void RefineTool_Diagnose_Name_IsCorrect()
    {
        var tool = new RefineTool();
        Assert.Equal("calor_refine", tool.Name);
    }

    [Fact]
    public async Task RefineTool_Guards_WithValidSource_ReturnsGuards()
    {
        var tool = new RefineTool();
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x} | (>= # INT:0)
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            { "action": "guards", "source": {{JsonSerializer.Serialize(source)}} }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);
        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.TryGetProperty("guards", out _));
    }

    [Fact]
    public async Task RefineTool_Types_WithDivisor_SuggestsNonZero()
    {
        var tool = new RefineTool();
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
            { "action": "types", "source": {{JsonSerializer.Serialize(source)}} }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);
        Assert.NotEmpty(result.Content);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());

        var suggestions = json.RootElement.GetProperty("suggestions");
        Assert.True(suggestions.GetArrayLength() > 0);
    }

    [Fact]
    public async Task RefineTool_Diagnose_WithFailures_ReturnsPatches()
    {
        var tool = new RefineTool();
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
            { "action": "diagnose", "source": {{JsonSerializer.Serialize(source)}} }
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
    public async Task RefineTool_Diagnose_WithStrictPolicy_SetsPolicy()
    {
        var tool = new RefineTool();
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{i32:x}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var args = JsonDocument.Parse($$"""
            { "action": "diagnose", "source": {{JsonSerializer.Serialize(source)}}, "policy": "strict" }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal("strict", json.RootElement.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task RefineTool_AllActions_WithMissingSource_ReturnError()
    {
        var tool = new RefineTool();

        var actions = new[] { "guards", "types", "diagnose", "obligations", "bounds", "fixes" };

        foreach (var action in actions)
        {
            var args = JsonDocument.Parse($$"""{"action":"{{action}}"}""").RootElement;
            var result = await tool.ExecuteAsync(args);
            Assert.True(result.IsError, $"Action '{action}' should error on missing source");
        }
    }

    [Fact]
    public void RefineTool_HasValidSchema()
    {
        var tool = new RefineTool();
        var schema = tool.GetInputSchema();
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props),
            $"{tool.Name} schema missing properties");
        Assert.True(props.TryGetProperty("source", out _),
            $"{tool.Name} schema missing source property");
    }

    // ───── GuardDiscovery: Nested FormatCondition ─────

    [Fact]
    public void GuardDiscovery_NestedAndPredicate_FormatsCorrectly()
    {
        // Test that a compound predicate like (&& (>= # INT:0) (<= # INT:100)) formats correctly
        var source = """
            §M{m001:Test}
            §F{f001:Main:priv}
              §I{i32:x} | (&& (>= # INT:0) (<= # INT:100))
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        // Mark as failed to trigger guard discovery
        foreach (var obl in tracker.Obligations)
            obl.Status = ObligationStatus.Failed;

        var discovery = new GuardDiscovery();
        var guards = discovery.DiscoverGuards(tracker);

        Assert.NotEmpty(guards);
        // The precondition guard should contain the formatted condition with >= and <=
        var precondGuard = guards.First(g => g.InsertionKind == "precondition");
        Assert.Contains(">=", precondGuard.CalorExpression);
        Assert.Contains("<=", precondGuard.CalorExpression);
        Assert.Contains("§Q", precondGuard.CalorExpression);
    }

    [Fact]
    public void GuardDiscovery_SelfRefInPredicate_ReplacedWithParamName()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:priv}
              §I{i32:age} | (>= # INT:0)
              §O{void}
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

        // # should be replaced with the parameter name 'age'
        var precondGuard = guards.First(g => g.InsertionKind == "precondition");
        Assert.Contains("age", precondGuard.CalorExpression);
        Assert.DoesNotContain("#", precondGuard.CalorExpression);
    }

    // ───── GuardDiscovery: Z3 Validation ─────

    [SkippableFact]
    public void GuardDiscovery_ValidateWithZ3_PreconditionGuardsValidated()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

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

        foreach (var obl in tracker.Obligations)
            obl.Status = ObligationStatus.Failed;

        var discovery = new GuardDiscovery();
        var guards = discovery.DiscoverGuards(tracker);

        // Validate guards with Z3
        var parameters = module.Functions[0].Parameters
            .Select(p => (p.Name, p.TypeName))
            .ToList();
        discovery.ValidateWithZ3(guards, tracker.Obligations, parameters);

        // Precondition guards should be validated (assuming the condition discharges it)
        var precondGuard = guards.First(g => g.InsertionKind == "precondition");
        Assert.True(precondGuard.Validated);

        // if_guard guards should have null (not applicable for Z3 validation)
        var ifGuard = guards.First(g => g.InsertionKind == "if_guard");
        Assert.Null(ifGuard.Validated);
    }

    // ───── Policy Pipeline Integration ─────

    [Fact]
    public void Pipeline_PermissivePolicy_FailedIsWarningNotError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Check:priv}
              §I{i32:x}
              §O{void}
              §PROOF{p1:always-positive} (> x INT:0)
            §/F{f001}
            §/M{m001}
            """;

        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            ObligationPolicy = ObligationPolicy.Permissive
        };

        var result = Program.Compile(source, "test.calr", options);

        // Under permissive policy, failed obligations should be warnings, not errors
        Assert.NotNull(options.ObligationResults);
        var proofObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.ProofObligation);

        if (proofObl != null && proofObl.Status == ObligationStatus.Failed)
        {
            // The diagnostic should be a warning, not an error
            Assert.DoesNotContain(result.Diagnostics.Errors,
                d => d.Code == DiagnosticCode.ProofObligationFailed);
        }
    }

    [Fact]
    public void Pipeline_DefaultPolicy_FailedIsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Check:priv}
              §I{i32:x}
              §O{void}
              §PROOF{p1:always-positive} (> x INT:0)
            §/F{f001}
            §/M{m001}
            """;

        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            ObligationPolicy = ObligationPolicy.Default
        };

        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);
        var proofObl = options.ObligationResults.Obligations.FirstOrDefault(
            o => o.Kind == ObligationKind.ProofObligation);

        if (proofObl != null && proofObl.Status == ObligationStatus.Failed)
        {
            // Under default policy, failed obligations should produce errors
            Assert.Contains(result.Diagnostics.Errors,
                d => d.Code == DiagnosticCode.ObligationFailed
                    || d.Code == DiagnosticCode.ProofObligationFailed);
        }
    }
}
