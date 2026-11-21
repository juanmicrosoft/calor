using System.Text.Json;
using Calor.Compiler.Ids;
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
                "§M{Test}\n  §F{Foo:pub} () -> void\n    §P \"hi\"\n  §/F\n");

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Compile_MetadataCorruptionOrAmbiguousRename_FailsClosedWithoutWrites(bool ambiguous, bool summary)
    {
        var root = Path.Combine(Path.GetTempPath(), $"calor-batch-fail-closed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "Input.calr");
            File.WriteAllText(path, "module Test Test block\n  func Original () → int\n    !\n    return 1\n");
            var args = JsonSerializer.SerializeToElement(new { action = "compile", projectPath = root, summary });
            Assert.False((await _tool.ExecuteAsync(args)).IsError);
            var sidecar = new DeclarationIdentityStore(root).SidecarPath;
            if (ambiguous)
                File.WriteAllText(path, "module Test Test block\n  func First () → int\n    !\n    return 1\n\n  func Second () → int\n    !\n    return 1\n");
            else
                File.WriteAllText(sidecar, "{broken");
            var added = Path.Combine(root, "Added.calr");
            File.WriteAllText(added, "module Added Added block\n  func Other () → int\n    !\n    return 2\n");
            var before = File.ReadAllBytes(sidecar);
            var source = File.ReadAllBytes(path);
            var addedSource = File.ReadAllBytes(added);
            var result = await _tool.ExecuteAsync(args);
            Assert.True(result.IsError);
            var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.False(response.GetProperty("success").GetBoolean());
            var files = response.GetProperty(summary ? "failedDetails" : "files").EnumerateArray().ToArray();
            Assert.Equal(2, files.Length);
            Assert.All(files, file =>
            {
                if (!summary)
                    Assert.False(file.GetProperty("success").GetBoolean());
                var error = Assert.Single(file.GetProperty("errors").EnumerateArray());
                Assert.Equal("Calor0806", error.GetProperty("code").GetString());
                Assert.False(error.TryGetProperty("declarationId", out _));
            });
            Assert.Equal(before, File.ReadAllBytes(sidecar));
            Assert.Equal(source, File.ReadAllBytes(path));
            Assert.Equal(addedSource, File.ReadAllBytes(added));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static DeclarationIdentityStore.State ReadIdentityState(string root) =>
        Assert.IsType<DeclarationIdentityStore.State>(JsonSerializer.Deserialize<DeclarationIdentityStore.State>(
            File.ReadAllText(new DeclarationIdentityStore(root).SidecarPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
}
