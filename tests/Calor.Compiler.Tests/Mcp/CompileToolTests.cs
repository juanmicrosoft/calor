using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class CompileToolTests
{
    private readonly CompileTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorCompile()
    {
        Assert.Equal("calor_compile", _tool.Name);
    }

    [Fact]
    public void Description_ContainsCompileInfo()
    {
        Assert.Contains("Compile", _tool.Description);
        Assert.Contains("Calor", _tool.Description);
        Assert.Contains("C#", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
    }

    // v0.15 (PR #1088): the MCP compile tool inherits default-on elision and must
    // expose the same opt-out the CLI has (`keepProvenGuards` ⇔ --keep-proven-guards).
    private const string ProvenSquareSource =
        "§M{m001:Test}\n  §CL{c001:Calc:pub}\n    §MT{mt001:Square:pub}\n      §I{i32:x}\n      §O{i32}\n" +
        "      §Q (>= x 0)\n      §Q (<= x 46340)\n      §S (>= result 0)\n      §R (* x x)\n";

    private async Task<string> CompileProvenSquare(string options)
    {
        var args = JsonDocument.Parse(
            "{\"source\": " + JsonSerializer.Serialize(ProvenSquareSource) + ", \"options\": " + options + "}").RootElement;
        var result = await _tool.ExecuteAsync(args);
        Assert.False(result.IsError);
        var text = result.Content[0].Text;
        Assert.NotNull(text);
        return text;
    }

    [SkippableFact]
    public async Task ExecuteAsync_Verify_DefaultElidesProvenPostcondition()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var text = await CompileProvenSquare("""{"verify": true}""");

        Assert.Contains("PROVEN: Postcondition", text);
        Assert.DoesNotContain("ContractKind.Ensures", text);
    }

    [SkippableFact]
    public async Task ExecuteAsync_Verify_KeepProvenGuards_KeepsGuard()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var text = await CompileProvenSquare("""{"verify": true, "keepProvenGuards": true}""");

        Assert.DoesNotContain("PROVEN: Postcondition", text);
        Assert.Contains("ContractKind.Ensures", text);
    }

    [Fact]
    public void GetInputSchema_ExposesKeepProvenGuardsOptOut()
    {
        var schema = _tool.GetInputSchema();
        Assert.True(schema.GetProperty("properties").GetProperty("options").GetProperty("properties")
            .TryGetProperty("keepProvenGuards", out var keep));
        Assert.Equal("boolean", keep.GetProperty("type").GetString());
        Assert.False(keep.GetProperty("default").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_WithValidSource_ReturnsSuccess()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n\n"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        Assert.NotEmpty(result.Content);

        var text = result.Content[0].Text;
        Assert.NotNull(text);
        Assert.Contains("success", text);
        Assert.Contains("true", text.ToLower());
        Assert.Contains("generatedCode", text);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultEffectPolicyMatchesSdkAndCli()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Main:pub}\n§O{void}\n§P \"undeclared console write\"\n"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);
        var text = result.Content[0].Text!;

        Assert.Contains("Calor0410", text);
        Assert.Contains("\"success\":false", text.Replace(" ", ""));
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidSource_ReturnsErrors()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Bad} invalid syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        Assert.Contains("diagnostics", text);
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
                "source": "invalid §§§",
                "filePath": "test-file.calr"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // The diagnostics should be present (errors or warnings)
        var text = result.Content[0].Text!;
        Assert.Contains("diagnostics", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithContractModeOff_Compiles()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Div:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§Q (!= b 0)\n§R (/ a b)\n\n",
                "options": {
                    "contractMode": "off"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithContractModeRelease_Compiles()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "§M{m001:Test}\n§F{f001:Div:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§Q (!= b 0)\n§R (/ a b)\n\n",
                "options": {
                    "contractMode": "release"
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
    }
}
