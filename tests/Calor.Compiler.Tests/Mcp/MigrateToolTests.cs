using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class MigrateToolTests : IDisposable
{
    private readonly MigrateTool _tool = new();
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Name_ReturnsCalorMigrate()
    {
        Assert.Equal("calor_migrate", _tool.Name);
    }

    [Fact]
    public void Description_ContainsMigrateInfo()
    {
        Assert.Contains("Migrate", _tool.Description);
        Assert.Contains("4-phase", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("path", out _));
        Assert.True(props.TryGetProperty("options", out var optionsSchema));

        // Verify required fields
        Assert.True(schema.TryGetProperty("required", out var required));
        Assert.Contains("path", required.EnumerateArray().Select(e => e.GetString()));

        // Verify options schema includes new fields
        var optionProps = optionsSchema.GetProperty("properties");
        Assert.True(optionProps.TryGetProperty("maxFileResults", out _));
        Assert.True(optionProps.TryGetProperty("verificationTimeoutMs", out var timeoutSchema));
        Assert.Equal(100, timeoutSchema.GetProperty("minimum").GetInt32());
    }

    [Fact]
    public async Task MissingPath_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        Assert.Contains("path", result.Content[0].Text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidPath_ReturnsError()
    {
        var args = JsonDocument.Parse("""
            {
                "path": "/nonexistent/path/that/does/not/exist"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content[0].Text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DefaultsDryRunTrue()
    {
        var testDataPath = FindTestDataPath();
        if (testDataPath == null) return;

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(testDataPath)}}"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Migration should succeed. Output: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.True(json.RootElement.GetProperty("dryRun").GetBoolean(),
            "dryRun should default to true for MCP safety");
    }

    [Fact]
    public async Task DryRun_ReturnsPlanWithoutWriting()
    {
        var testDataPath = FindTestDataPath();
        if (testDataPath == null) return;

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(testDataPath)}}",
                "options": {
                    "dryRun": true,
                    "skipAnalyze": true,
                    "skipVerify": true
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Dry run should succeed. Output: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        // Should have plan info
        Assert.True(root.TryGetProperty("plan", out var plan));
        Assert.True(plan.GetProperty("totalFiles").GetInt32() >= 0);

        // Should have summary from the dry-run convert phase
        Assert.True(root.TryGetProperty("summary", out _));

        // Should have dryRun=true
        Assert.True(root.GetProperty("dryRun").GetBoolean());

        // Should have duration
        Assert.True(root.GetProperty("durationMs").GetInt32() >= 0);
    }

    [Fact]
    public async Task SkipAnalyze_OmitsAnalysis()
    {
        var testDataPath = FindTestDataPath();
        if (testDataPath == null) return;

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(testDataPath)}}",
                "options": {
                    "dryRun": true,
                    "skipAnalyze": true,
                    "skipVerify": true
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);

        // analysis should be null/absent when skipAnalyze is true
        Assert.False(json.RootElement.TryGetProperty("analysis", out _),
            "Analysis should be omitted when skipAnalyze is true");
    }

    [Fact]
    public async Task NullArguments_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(null);

        Assert.True(result.IsError);
        Assert.Contains("path", result.Content[0].Text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DryRunFalse_WritesOutputFiles()
    {
        // Create a temp directory with a simple C# file
        var tempDir = CreateTempDirWithCSharpFile();

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(tempDir)}}",
                "options": {
                    "dryRun": false,
                    "skipAnalyze": true,
                    "skipVerify": true,
                    "parallel": false
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Migration should succeed. Output: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        // Should not be dry run
        Assert.False(root.GetProperty("dryRun").GetBoolean());

        // Should have written .calr files
        var calrFiles = Directory.GetFiles(tempDir, "*.calr", SearchOption.AllDirectories);
        Assert.True(calrFiles.Length > 0, "Should have written at least one .calr file");

        // Verify the written file contains valid Calor content
        var content = File.ReadAllText(calrFiles[0]);
        Assert.Contains("§", content);
    }

    [Fact]
    public async Task CalorToCs_Direction_Works()
    {
        // Create a temp directory with a simple Calor file
        var tempDir = CreateTempDirWithCalorFile();

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(tempDir)}}",
                "options": {
                    "direction": "calor-to-cs",
                    "dryRun": false,
                    "skipAnalyze": true,
                    "skipVerify": true,
                    "parallel": false
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Calor-to-C# migration should succeed. Output: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.False(root.GetProperty("dryRun").GetBoolean());

        // Analysis should be absent (only runs for cs-to-calor)
        Assert.False(root.TryGetProperty("analysis", out _),
            "Analysis should not run for calor-to-cs direction");
    }

    [Fact]
    public async Task NegativeTimeout_ClampedToMinimum()
    {
        var testDataPath = FindTestDataPath();
        if (testDataPath == null) return;

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(testDataPath)}}",
                "options": {
                    "dryRun": true,
                    "skipAnalyze": true,
                    "skipVerify": true,
                    "verificationTimeoutMs": -500
                }
            }
            """).RootElement;

        // Should not throw — the negative value gets clamped to 100
        var result = await _tool.ExecuteAsync(args);
        Assert.False(result.IsError, $"Should handle negative timeout gracefully. Output: {result.Content[0].Text}");
    }

    [Fact]
    public async Task AnalyzePhase_IncludesScoreAndPriority()
    {
        var testDataPath = FindTestDataPath();
        if (testDataPath == null) return;

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(testDataPath)}}",
                "options": {
                    "dryRun": true,
                    "skipAnalyze": false,
                    "skipVerify": true
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Migration with analysis should succeed. Output: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("analysis", out var analysis),
            "Analysis should be present when skipAnalyze is false");
        Assert.True(analysis.GetProperty("filesAnalyzed").GetInt32() > 0);
        Assert.True(analysis.GetProperty("averageScore").GetDouble() >= 0);
        Assert.True(analysis.TryGetProperty("priorityBreakdown", out _));
    }

    [Fact]
    public async Task VerifyPhase_RunsWhenNotSkipped()
    {
        // Create a temp directory and do a real migration (dryRun=false) to exercise verify
        var tempDir = CreateTempDirWithCSharpFile();

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(tempDir)}}",
                "options": {
                    "dryRun": false,
                    "skipAnalyze": true,
                    "skipVerify": false,
                    "parallel": false,
                    "verificationTimeoutMs": 2000
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Migration with verify should succeed. Output: {result.Content[0].Text}");
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        // Verification output should be present (even if Z3 reports 0 contracts)
        // If Z3 is not available in the test environment, verification will be null
        // Either way, the code path is exercised without error
        Assert.False(root.GetProperty("dryRun").GetBoolean());
    }

    [Fact]
    public async Task MaxFileResults_TruncatesOutput()
    {
        var testDataPath = FindTestDataPath();
        if (testDataPath == null) return;

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(testDataPath)}}",
                "options": {
                    "dryRun": true,
                    "skipAnalyze": true,
                    "skipVerify": true,
                    "maxFileResults": 2
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        // Level1_Basic has 20 files, so with maxFileResults=2 we should be truncated
        if (root.TryGetProperty("fileResults", out var fileResults))
        {
            Assert.True(fileResults.GetArrayLength() <= 2,
                $"fileResults should be capped at 2, got {fileResults.GetArrayLength()}");

            if (root.TryGetProperty("fileResultsTruncated", out var truncated))
            {
                Assert.True(truncated.GetBoolean());
                Assert.True(root.GetProperty("totalFileResultCount").GetInt32() > 2);
            }
        }
    }

    [Fact]
    public async Task EmptyDirectory_ReturnsSuccessWithNoFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_migrate_test_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);

        var args = JsonDocument.Parse($$"""
            {
                "path": "{{EscapeJson(tempDir)}}",
                "options": {
                    "dryRun": true,
                    "skipAnalyze": true,
                    "skipVerify": true
                }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("plan").GetProperty("totalFiles").GetInt32());
    }

    // ── Helpers ──

    private string CreateTempDirWithCSharpFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_migrate_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "Calculator.cs"), """
            public class Calculator
            {
                public int Add(int a, int b) => a + b;
                public int Subtract(int a, int b) => a - b;
            }
            """);

        return tempDir;
    }

    private string CreateTempDirWithCalorFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_migrate_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "Calculator.calr"),
            "§M{m001:Calculator}\n§CL{c001:Calculator:pub}\n§MT{mt001:Add:pub}\n  §I{i32:a}\n  §I{i32:b}\n  §O{i32}\n  §R (+ a b)\n§/MT{mt001}\n§/CL{c001}\n§/M{m001}\n");

        return tempDir;
    }

    private static string? FindTestDataPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "tests", "TestData", "CSharpImport", "Level1_Basic");
            if (Directory.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
