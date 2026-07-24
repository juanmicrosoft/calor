using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class MigrateToolTests
{
    private readonly MigrateTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorMigrate()
    {
        Assert.Equal("calor_migrate", _tool.Name);
    }

    [Fact]
    public void Description_ContainsMigrationInfo()
    {
        Assert.Contains("migration", _tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pipeline", _tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("projectPath", out _));
        Assert.True(props.TryGetProperty("phase", out _));
        Assert.True(props.TryGetProperty("maxFiles", out _));
        Assert.True(props.TryGetProperty("autoFix", out _));

        // projectPath is required
        Assert.True(schema.TryGetProperty("required", out var required));
        Assert.Contains("projectPath", required.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void GetInputSchema_PhaseHasCorrectEnum()
    {
        var schema = _tool.GetInputSchema();
        var phase = schema.GetProperty("properties").GetProperty("phase");
        Assert.True(phase.TryGetProperty("enum", out var enumValues));

        var values = enumValues.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("assess", values);
        Assert.Contains("convert", values);
        Assert.Contains("compile", values);
        Assert.Contains("fix", values);
        Assert.Contains("full", values);
    }

    [Fact]
    public async Task ExecuteAsync_MissingProjectPath_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;
        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        Assert.Contains("projectPath", result.Content[0].Text!);
    }

    [Fact]
    public async Task ExecuteAsync_NonexistentPath_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"projectPath": "/nonexistent/path/to/project"}""").RootElement;
        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content[0].Text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPhase_ReturnsError()
    {
        var tempDir = CreateTempDirWithCsFile();
        try
        {
            var args = JsonDocument.Parse($$"""{"projectPath": "{{tempDir.Replace("\\", "\\\\")}}", "phase": "invalid"}""").RootElement;
            var result = await _tool.ExecuteAsync(args);

            Assert.True(result.IsError);
            Assert.Contains("Unknown phase", result.Content[0].Text!);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AssessPhase_ReturnsResults()
    {
        var tempDir = CreateTempDirWithCsFile();
        try
        {
            var args = JsonDocument.Parse($$"""{"projectPath": "{{tempDir.Replace("\\", "\\\\")}}", "phase": "assess"}""").RootElement;
            var result = await _tool.ExecuteAsync(args);

            var text = result.Content[0].Text!;
            var json = JsonDocument.Parse(text).RootElement;

            Assert.Equal("assess", json.GetProperty("phase").GetString());
            Assert.True(json.GetProperty("totalFiles").GetInt32() > 0);
            Assert.True(json.TryGetProperty("perFile", out var perFile));
            Assert.True(perFile.GetArrayLength() > 0);

            var firstFile = perFile[0];
            Assert.True(firstFile.TryGetProperty("path", out _));
            Assert.True(firstFile.TryGetProperty("status", out _));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CompilePhase_NoCarlFiles_ReturnsError()
    {
        var tempDir = CreateTempDirWithCsFile();
        try
        {
            var args = JsonDocument.Parse($$"""{"projectPath": "{{tempDir.Replace("\\", "\\\\")}}", "phase": "compile"}""").RootElement;
            var result = await _tool.ExecuteAsync(args);

            Assert.True(result.IsError);
            Assert.Contains(".calr", result.Content[0].Text!);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CompilePhase_WithCalrFiles_ReturnsResults()
    {
        var tempDir = CreateTempDirWithCalrFile();
        try
        {
            var args = JsonDocument.Parse($$"""{"projectPath": "{{tempDir.Replace("\\", "\\\\")}}", "phase": "compile"}""").RootElement;
            var result = await _tool.ExecuteAsync(args);

            var text = result.Content[0].Text!;
            var json = JsonDocument.Parse(text).RootElement;

            Assert.Equal("compile", json.GetProperty("phase").GetString());
            Assert.True(json.GetProperty("totalFiles").GetInt32() > 0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_FixPhase_NoCarlFiles_ReturnsError()
    {
        var tempDir = CreateTempDirWithCsFile();
        try
        {
            var args = JsonDocument.Parse($$"""{"projectPath": "{{tempDir.Replace("\\", "\\\\")}}", "phase": "fix"}""").RootElement;
            var result = await _tool.ExecuteAsync(args);

            Assert.True(result.IsError);
            Assert.Contains(".calr", result.Content[0].Text!);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolveDirectory_WithDirectory_ReturnsPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-migrate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.Equal(tempDir, MigrateTool.ResolveDirectory(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolveDirectory_NonexistentPath_ReturnsNull()
    {
        Assert.Null(MigrateTool.ResolveDirectory("/nonexistent/directory"));
    }

    [Fact]
    public void DiscoverCsFiles_FindsFiles()
    {
        var tempDir = CreateTempDirWithCsFile();
        try
        {
            var files = MigrateTool.DiscoverCsFiles(tempDir, 0);
            Assert.Single(files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DiscoverCsFiles_RespectsMaxFiles()
    {
        var tempDir = CreateTempDirWithMultipleCsFiles(5);
        try
        {
            var files = MigrateTool.DiscoverCsFiles(tempDir, 2);
            Assert.Equal(2, files.Count);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Annotations_HasDestructiveHint()
    {
        Assert.NotNull(_tool.Annotations);
        Assert.True(_tool.Annotations!.DestructiveHint);
    }

    [Fact]
    public async Task ExecuteAsync_MaxFiles_LimitsAssessment()
    {
        var tempDir = CreateTempDirWithMultipleCsFiles(5);
        try
        {
            var args = JsonDocument.Parse($$"""{"projectPath": "{{tempDir.Replace("\\", "\\\\")}}", "phase": "assess", "maxFiles": 2}""").RootElement;
            var result = await _tool.ExecuteAsync(args);

            var text = result.Content[0].Text!;
            var json = JsonDocument.Parse(text).RootElement;

            Assert.Equal(2, json.GetProperty("totalFiles").GetInt32());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CompilePhase_ErrorsAreEnvelopeDiagnostics()
    {
        // Envelope schema v1.1 (loop plan D1.3): per-file compile errors are
        // the real compiler diagnostics as EnvelopeDiagnostic entries, not
        // "[Code] Ln: msg" strings.
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-migrate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Structural closer tags are a hard parser error (Calor0830)
            File.WriteAllText(Path.Combine(tempDir, "Bad.calr"),
                "§M{m001:Test}\n  §F{f001:Foo:pub} () -> void\n    §P \"hi\"\n  §/F{f001}\n");

            var args = JsonDocument.Parse($$"""{"projectPath": "{{tempDir.Replace("\\", "\\\\")}}", "phase": "compile"}""").RootElement;
            var result = await _tool.ExecuteAsync(args);

            var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.Equal("compile", json.GetProperty("phase").GetString());

            var file = Assert.Single(json.GetProperty("perFile").EnumerateArray());
            Assert.Equal("failed", file.GetProperty("status").GetString());

            var errors = file.GetProperty("errors").EnumerateArray().ToList();
            Assert.NotEmpty(errors);
            foreach (var entry in errors)
            {
                Assert.StartsWith("Calor", entry.GetProperty("code").GetString());
                Assert.Equal("error", entry.GetProperty("severity").GetString());
                Assert.True(entry.GetProperty("location").GetProperty("line").GetInt32() >= 1);
            }

            // Summary categories are keyed by envelope diagnostic code
            var categories = json.GetProperty("summary").GetProperty("errorCategories");
            Assert.All(categories.EnumerateObject(), c => Assert.StartsWith("Calor", c.Name));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string CreateTempDirWithCsFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-migrate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(
            Path.Combine(tempDir, "Test.cs"),
            "namespace TestNs { public class Foo { public int Bar() => 42; } }");
        return tempDir;
    }

    private static string CreateTempDirWithMultipleCsFiles(int count)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-migrate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        for (var i = 0; i < count; i++)
        {
            File.WriteAllText(
                Path.Combine(tempDir, $"Test{i}.cs"),
                $"namespace TestNs {{ public class Foo{i} {{ public int Bar() => {i}; }} }}");
        }
        return tempDir;
    }

    private static string CreateTempDirWithCalrFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-migrate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(
            Path.Combine(tempDir, "Test.calr"),
            """
            §M{m1:TestModule}
              §C{c1:Foo:public}
                §F{f1:Bar:public} §O{Int32}
                    → §R 42
              §/C{c1}
            """);
        return tempDir;
    }

    [Fact]
    public async Task ExecuteAsync_CompilePhase_BindError_CarriesDeclarationId()
    {
        // review of #757 item 2: pin the declarationId capability on the
        // migrate compile phase — Calor0250 parses to a full AST, so the
        // entry must resolve to its enclosing function ID.
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-migrate-declid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "BindError.calr"),
                "§M{m001:Test}\n  §F{f001:Foo:pub} () -> void\n    §B{x}\n");

            var args = JsonDocument.Parse($$"""{"projectPath": {{JsonSerializer.Serialize(tempDir)}}, "phase": "compile"}""").RootElement;
            var result = await _tool.ExecuteAsync(args);

            var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            var file = root.GetProperty("perFile").EnumerateArray()
                .Single(f => f.GetProperty("path").GetString()!.EndsWith("BindError.calr"));
            var entry = file.GetProperty("errors").EnumerateArray()
                .Single(e => e.GetProperty("code").GetString() == "Calor0250");

            Assert.Equal("f001", entry.GetProperty("declarationId").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}

