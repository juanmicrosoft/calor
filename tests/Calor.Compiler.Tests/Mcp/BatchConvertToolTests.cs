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

    [Fact]
    public async Task ExecuteAsync_CompileMode_BindError_CarriesDeclarationId()
    {
        // review of #757 item 2: pin the declarationId capability — a bind
        // error (Calor0250) parses to a full AST, so the entry must resolve
        // to its enclosing function ID.
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-batch-declid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, "BindError.calr");
            File.WriteAllText(path,
                "§M{Test}\n  §F{Foo:pub} () -> void\n    §B{x}\n");

            var args = JsonDocument.Parse($$"""
                {
                    "action": "compile",
                    "projectPath": {{JsonSerializer.Serialize(tempDir)}}
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);
            var state = ReadIdentityState(tempDir);
            var functionId = Assert.Single(state.Declarations, declaration => declaration.Name == "Foo").Id;
            var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            var file = Assert.Single(root.GetProperty("files").EnumerateArray());
            var entry = file.GetProperty("errors").EnumerateArray()
                .Single(e => e.GetProperty("code").GetString() == "Calor0250");

            Assert.Equal(functionId, entry.GetProperty("declarationId").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("files")]
    [InlineData("glob")]
    public async Task Compile_EnrollsSelectedInputs_AndPreservesRenamedMovedIdentity(string selection)
    {
        var root = Path.Combine(Path.GetTempPath(), $"calor-batch-continuity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "active"));
        Directory.CreateDirectory(Path.Combine(root, "excluded"));
        try
        {
            var first = Path.Combine(root, "active", "First.calr");
            File.WriteAllText(first, "§M{Test}\n  §F{Foo:pub} () -> void\n    §B{x}\n");
            File.WriteAllText(Path.Combine(root, "excluded", "Original.calr"), "§M{m1:Original}\n");

            async Task<JsonElement> Run(params string[] files)
            {
                var args = new Dictionary<string, object?>
                {
                    ["action"] = "compile",
                    ["projectPath"] = root
                };
                if (selection == "directory")
                    args["directory"] = Path.Combine(root, "active");
                else if (selection == "files")
                {
                    args.Remove("projectPath");
                    args["files"] = files;
                }
                else
                    args["glob"] = "active/**/*.calr";
                return JsonDocument.Parse((await _tool.ExecuteAsync(
                    JsonSerializer.SerializeToElement(args))).Content[0].Text!).RootElement;
            }

            var initial = await Run(first);
            Assert.False(initial.GetProperty("success").GetBoolean());
            var store = DeclarationIdentityStore.Find(first);
            Assert.NotNull(store);
            var state = ReadIdentityState(store.Root);
            var originalId = Assert.Single(state.Declarations, declaration => declaration.Name == "Foo").Id;

            var moved = Path.Combine(root, "active", "Moved.calr");
            File.Move(first, moved);
            File.WriteAllText(moved, "\n§M{Test}\n  §F{Renamed:pub} () -> void\n    §B{x}\n");
            var added = Path.Combine(root, "active", "Added.calr");
            File.WriteAllText(added, "§M{Added}\n  §F{Bar:pub} () -> i32\n    §R 1\n");
            var repeated = await Run(moved, added);
            Assert.Equal(2, repeated.GetProperty("totalFiles").GetInt32());
            var results = repeated.GetProperty("files").EnumerateArray().ToArray();
            Assert.True(Assert.Single(results, file => file.GetProperty("filePath").GetString() == added)
                .GetProperty("success").GetBoolean());
            var error = Assert.Single(results, file => file.GetProperty("filePath").GetString() == moved)
                .GetProperty("errors").EnumerateArray().Single(entry => entry.GetProperty("code").GetString() == "Calor0250");
            Assert.Equal(originalId, error.GetProperty("declarationId").GetString());
            state = ReadIdentityState(store.Root);
            Assert.Equal(originalId, Assert.Single(state.Declarations, declaration => declaration.Name == "Renamed").Id);
            Assert.Contains(state.Declarations, declaration => declaration.Name == "Bar");
            Assert.DoesNotContain(state.Files, file => file.Contains("excluded", StringComparison.Ordinal));
            Assert.Equal("§M{m1:Original}\n", File.ReadAllText(Path.Combine(root, "excluded", "Original.calr")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Compile_MixedValidMissingAndMalformedInputs_PreservesPerFileOutcomes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"calor-batch-mixed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "Valid.CALR");
            var malformed = Path.Combine(root, "Malformed.calr");
            var missing = Path.Combine(root, "Missing.calr");
            File.WriteAllText(valid, "§M{Valid}\n  §F{Run:pub} () -> i32\n    §R 1\n");
            File.WriteAllText(malformed, "§M{Bad}\n  §F{Run:pub} () -> void\n  §/F\n");
            var result = await _tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
            {
                action = "compile",
                files = new[] { valid, malformed, missing }
            }));
            Assert.True(result.IsError);
            var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            var files = response.GetProperty("files").EnumerateArray().ToArray();
            Assert.Equal(3, files.Length);
            Assert.True(Assert.Single(files, file => file.GetProperty("filePath").GetString() == valid)
                .GetProperty("success").GetBoolean());
            Assert.Contains(Assert.Single(files, file => file.GetProperty("filePath").GetString() == malformed)
                .GetProperty("errors").EnumerateArray(), error => error.GetProperty("code").GetString() == "Calor0830");
            Assert.False(Assert.Single(files, file => file.GetProperty("filePath").GetString() == missing)
                .GetProperty("success").GetBoolean());
            Assert.Contains(ReadIdentityState(root).Declarations, declaration => declaration.Name == "Run");
            Assert.Single(ReadIdentityState(root).Files);
        }
        finally
        {
            Directory.Delete(root, true);
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
            File.WriteAllText(path, "§M{Test}\n  §F{Original:pub} () -> i32\n    §R 1\n");
            var args = JsonSerializer.SerializeToElement(new { action = "compile", projectPath = root, summary });
            Assert.False((await _tool.ExecuteAsync(args)).IsError);
            var sidecar = new DeclarationIdentityStore(root).SidecarPath;
            if (ambiguous)
                File.WriteAllText(path, "§M{Test}\n  §F{First:pub} () -> i32\n    §R 1\n  §F{Second:pub} () -> i32\n    §R 1\n");
            else
                File.WriteAllText(sidecar, "{broken");
            var added = Path.Combine(root, "Added.calr");
            File.WriteAllText(added, "§M{Added}\n  §F{Other:pub} () -> i32\n    §R 2\n");
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
