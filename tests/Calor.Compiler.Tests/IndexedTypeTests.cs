using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for indexed type (§ITYPE) parsing, emission, obligation generation,
/// and Z3-based bounds checking (Milestone 2).
/// </summary>
public sealed class IndexedTypeTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAllForParser();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string ParseAndEmitCSharp(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors:\n{string.Join("\n", diagnostics.Select(d => d.Message))}");

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string ParseAndEmitCalor(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors:\n{string.Join("\n", diagnostics.Select(d => d.Message))}");

        var emitter = new CalorEmitter();
        return emitter.Emit(module);
    }

    // ───── Feature Support ─────

    [Fact]
    public void FeatureSupport_IndexedType_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("indexed-type"));
    }

    [Fact]
    public void FeatureSupport_IndexBounds_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("index-bounds"));
    }

    [Fact]
    public void Generate_ArrayAccessOnNonIndexedType_CreatesRuntimeBoundsObligation()
    {
        var source = """
            module Test Test block
              func Sum private (int[]:items, int:i) → int
                return items[i]
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var tracker = new ObligationTracker();
        var generator = new ObligationGenerator(tracker);
        generator.Generate(module);

        var obligation = Assert.Single(
            tracker.Obligations,
            o => o.Kind == ObligationKind.IndexBounds);
        Assert.Contains("runtime collection bounds", obligation.Description);
    }

    // ───── Gap Fix: Expanded MCP tool tests ─────

    [Fact]
    public async Task BoundsCheckTool_NoIndexedTypes_ReturnsEmptyAccessSites()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                return
            """;

        var tool = new Calor.Compiler.Mcp.Tools.RefineTool();
        var argsJson = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new { action = "bounds", source }));

        var result = await tool.ExecuteAsync(argsJson.RootElement);
        Assert.False(result.IsError);

        // Result should indicate safe (no access sites)
        var json = result.Content[0].Text;
        Assert.Contains("\"safe\":true", json);
        Assert.Contains("\"total_access_sites\":0", json);
    }

    [Fact]
    public async Task BoundsCheckTool_WithFunctionIdFilter_AcceptsParameter()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                return
            """;

        var tool = new Calor.Compiler.Mcp.Tools.RefineTool();
        var argsJson = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new { action = "bounds", source, function_id = "f001" }));

        var result = await tool.ExecuteAsync(argsJson.RootElement);
        Assert.False(result.IsError);

        // With no indexed types, should still succeed and report safe
        var json = result.Content[0].Text;
        Assert.Contains("\"safe\":true", json);
    }

    [Fact]
    public async Task BoundsCheckTool_MissingSource_ReturnsError()
    {
        var tool = new Calor.Compiler.Mcp.Tools.RefineTool();
        var argsJson = System.Text.Json.JsonDocument.Parse("""{"action":"bounds"}""");

        var result = await tool.ExecuteAsync(argsJson.RootElement);
        Assert.True(result.IsError);
    }
}
