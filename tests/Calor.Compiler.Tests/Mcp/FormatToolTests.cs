using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for calor_format (format + ids actions), including the envelope
/// schema v1.1 diagnostic entry adoption (loop plan D1.3 — final MCP sweep).
/// </summary>
public class FormatToolTests
{
    private readonly FormatTool _tool = new();
    private readonly CheckTool _checkTool = new();

    private static JsonElement CreateArgs(string source, string? action = null, string? idsAction = null)
    {
        var obj = new Dictionary<string, object> { ["source"] = source };
        if (action != null) obj["action"] = action;
        if (idsAction != null) obj["idsAction"] = idsAction;
        return JsonDocument.Parse(JsonSerializer.Serialize(obj)).RootElement;
    }

    private static JsonElement ParseOutput(Calor.Compiler.Mcp.McpToolResult result)
        => JsonDocument.Parse(result.Content[0].Text!).RootElement;

    private static JsonElement CreateLintArgs(string source, bool fix)
        => JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            action = "lint",
            source,
            fix
        })).RootElement;

    [Fact]
    public void Name_ReturnsCalorFormat()
    {
        Assert.Equal("calor_format", _tool.Name);
    }

    [Fact]
    public async Task Format_ValidSource_Succeeds()
    {
        var source = "§M{Test}\n  §F{Foo:pub} () -> void\n    §P \"hi\"\n";
        var result = await _tool.ExecuteAsync(CreateArgs(source));

        Assert.False(result.IsError);
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.False(output.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Format_SemanticallyUnsupportedSource_ReportsConservativeFallback()
    {
        var source = "§M{Test}\n  §F{Foo:pub} () -> void\n    §P \"missing effect\"\n";

        var result = await _tool.ExecuteAsync(CreateArgs(source));

        Assert.False(result.IsError);
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        Assert.False(output.GetProperty("isChanged").GetBoolean());
        Assert.Equal(source, output.GetProperty("formattedCode").GetString());
        Assert.True(output.TryGetProperty("conservativeFallbackReason", out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason.GetString()));
    }

    [Fact]
    public async Task CheckLint_ReadOnlySemanticError_RemainsParseSuccessful()
    {
        var source =
            "§M{Test}\n" +
            "  §F{Foo:pub} () -> void\n" +
            "    §P \"missing effect\"   \n";

        var result = await _checkTool.ExecuteAsync(CreateLintArgs(source, fix: false));

        Assert.False(result.IsError);
        var output = ParseOutput(result);
        Assert.True(output.GetProperty("parseSuccess").GetBoolean());
        Assert.Equal(1, output.GetProperty("issueCount").GetInt32());
        Assert.Empty(output.GetProperty("parseErrors").EnumerateArray());
        Assert.False(output.TryGetProperty("fixSuppressedReason", out _));
        Assert.False(output.TryGetProperty("fixDiagnostics", out _));
    }

    [Fact]
    public async Task CheckLint_FixSemanticError_IsSuppressedWithoutParseFailure()
    {
        var source =
            "§M{Test}\n" +
            "  §F{Foo:pub} () -> void\n" +
            "    §P \"missing effect\"   \n";

        var result = await _checkTool.ExecuteAsync(CreateLintArgs(source, fix: true));

        Assert.True(result.IsError);
        var output = ParseOutput(result);
        Assert.False(output.GetProperty("success").GetBoolean());
        Assert.True(output.GetProperty("parseSuccess").GetBoolean());
        Assert.Empty(output.GetProperty("parseErrors").EnumerateArray());
        Assert.Equal(source, output.GetProperty("fixedCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            output.GetProperty("fixSuppressedReason").GetString()));
        var diagnostic = Assert.Single(
            output.GetProperty("fixDiagnostics").EnumerateArray());
        Assert.Equal(
            Calor.Compiler.Diagnostics.DiagnosticCode.FormatConservativeFallback,
            diagnostic.GetProperty("code").GetString());
        Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Format_ParseError_ReturnsEnvelopeDiagnostics()
    {
        // Structural closer tags are a hard parser error (Calor0830)
        var source = "§M{Test}\n  §F{Foo:pub} () -> void\n    §P \"hi\"\n  §/F\n";
        var result = await _tool.ExecuteAsync(CreateArgs(source));

        Assert.True(result.IsError);
        var output = ParseOutput(result);
        Assert.False(output.GetProperty("success").GetBoolean());

        var errors = output.GetProperty("errors").EnumerateArray().ToList();
        Assert.NotEmpty(errors);
        foreach (var entry in errors)
        {
            Assert.StartsWith("Calor", entry.GetProperty("code").GetString());
            Assert.Equal("error", entry.GetProperty("severity").GetString());
            var location = entry.GetProperty("location");
            Assert.True(location.GetProperty("line").GetInt32() >= 1);
            Assert.True(location.GetProperty("column").GetInt32() >= 1);
        }
    }

    [Fact]
    public async Task IdsCheck_InMemorySource_RequiresProjectSync()
    {
        var source = "§M{Test}\n  §F{Foo:pub} () -> void\n";
        var result = await _tool.ExecuteAsync(CreateArgs(source, action: "ids", idsAction: "check"));

        Assert.True(result.IsError);
        Assert.Contains("calor ids sync", result.Content[0].Text);
        Assert.Contains(".calor/ids.json", result.Content[0].Text);
    }

    [Fact]
    public async Task IdsAssign_IsRetiredWithSyncGuidance()
    {
        var source = "§M{Test}\n  §F{Foo:pub} () -> void\n";
        var result = await _tool.ExecuteAsync(CreateArgs(source, action: "ids", idsAction: "assign"));

        Assert.True(result.IsError);
        Assert.Contains("calor ids sync", result.Content[0].Text);
        Assert.DoesNotContain("modifiedCode", result.Content[0].Text);
    }

    [Fact]
    public async Task Format_IdFreeSource_DoesNotInventPersistedIds()
    {
        var source = "§M{Test}\n  §F{Foo:pub} () -> void\n";
        var result = await _tool.ExecuteAsync(CreateArgs(source));
        Assert.False(result.IsError);
        var output = ParseOutput(result);
        Assert.Equal(source, output.GetProperty("formattedCode").GetString());
        Assert.DoesNotContain("declarationId", result.Content[0].Text);
    }
}
