using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class BatchConvertToolTests
{
    private readonly BatchTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorBatchConvert()
    {
        Assert.Equal("calor_batch", _tool.Name);
    }

    [Fact]
    public void Description_ContainsModuleNameGuidance()
    {
        // MCP Gap 4: Description should mention namespace derivation and moduleNameOverride
        Assert.Contains("namespace", _tool.Description.ToLower());
        Assert.Contains("moduleNameOverride", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ContainsModuleNameOverrideParam()
    {
        // MCP Gap 6: Schema should include moduleNameOverride parameter
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("moduleNameOverride", out var moduleParam),
            "Schema should include moduleNameOverride property");
        Assert.True(moduleParam.TryGetProperty("type", out var typeVal));
        Assert.Equal("string", typeVal.GetString());
    }

    [Fact]
    public async Task ExecuteAsync_MissingProjectPath_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"action": "convert"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("projectPath", text);
    }

    [Fact]
    public async Task ExecuteAsync_NonexistentPath_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"action": "convert", "projectPath": "/nonexistent/path/to/project"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("not found", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithModuleNameOverride_AcceptsParameter()
    {
        // MCP Gap 6: Verify moduleNameOverride is accepted (not rejected) by the tool.
        // Use a temp directory with a single .cs file to verify end-to-end.
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-batch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var csFile = Path.Combine(tempDir, "Test.cs");
            File.WriteAllText(csFile, "namespace Original { public class Foo { public int Bar() => 42; } }");

            var args = JsonDocument.Parse($$"""
                {
                    "action": "convert",
                    "projectPath": "{{tempDir.Replace("\\", "\\\\")}}",
                    "moduleNameOverride": "OverriddenModule",
                    "dryRun": true
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError, $"Expected success but got: {result.Content[0].Text}");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CompileMode_ErrorsAreEnvelopeDiagnostics()
    {
        // Envelope schema v1.1 (loop plan D1.3): per-file compile errors are
        // the real compiler diagnostics as EnvelopeDiagnostic entries, not
        // "[Code] Ln: msg" strings.
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-batch-envelope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Structural closer tags are a hard parser error (Calor0830)
            File.WriteAllText(Path.Combine(tempDir, "Bad.calr"),
                "§M{m001:Test}\n  §F{f001:Foo:pub} () -> void\n    §P \"hi\"\n  §/F{f001}\n");

            var args = JsonDocument.Parse($$"""
                {
                    "action": "compile",
                    "projectPath": {{JsonSerializer.Serialize(tempDir)}}
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.True(result.IsError);
            var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.False(root.GetProperty("success").GetBoolean());

            var file = Assert.Single(root.GetProperty("files").EnumerateArray());
            Assert.False(file.GetProperty("success").GetBoolean());

            var errors = file.GetProperty("errors").EnumerateArray().ToList();
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
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
