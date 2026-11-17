using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class AnalyzeDirectoryTests
{
    private readonly AnalyzeTool _tool = new();

    [Fact]
    public void GetInputSchema_ContainsDirectoryPathProperty()
    {
        var schema = _tool.GetInputSchema();

        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("directoryPath", out var dirProp));
        Assert.Equal("string", dirProp.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithNonexistentDirectory_ReturnsError()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "security",
                "directoryPath": "/nonexistent/path/to/calr/files"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        Assert.Contains("Directory not found", result.Content[0].Text!);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyDirectory_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = JsonDocument.Parse($$"""
                {
                    "action": "security",
                    "directoryPath": "{{tempDir.Replace("\\", "\\\\")}}"
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.True(result.IsError);
            Assert.Contains("No .calr files found", result.Content[0].Text!);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithDirectoryContainingCalrFiles_ReturnsAggregateResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write two simple .calr files
            File.WriteAllText(Path.Combine(tempDir, "file1.calr"),
                "module Test1 Test1 block\n  func Add (int:a, int:b) → int\n    !\n    return (a + b)\n");
            File.WriteAllText(Path.Combine(tempDir, "file2.calr"),
                "module Test2 Test2 block\n  func Sub (int:x, int:y) → int\n    !\n    return (x - y)\n");

            var args = JsonDocument.Parse($$"""
                {
                    "action": "security",
                    "directoryPath": "{{tempDir.Replace("\\", "\\\\")}}"
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            var text = result.Content[0].Text!;
            Assert.Contains("success", text);
            Assert.Contains("totalFiles", text);
            Assert.Contains("files", text);

            // Parse and verify structure
            var json = JsonDocument.Parse(text).RootElement;
            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.Equal(2, json.GetProperty("summary").GetProperty("totalFiles").GetInt32());
            Assert.Equal(2, json.GetProperty("files").GetArrayLength());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithDirectoryRecursive_FindsNestedFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_test_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempDir, "subdir");
        Directory.CreateDirectory(subDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "root.calr"),
                "module Root Root block\n  func Foo () → int\n    !\n    return 42\n");
            File.WriteAllText(Path.Combine(subDir, "nested.calr"),
                "module Nested Nested block\n  func Bar () → int\n    !\n    return 99\n");

            var args = JsonDocument.Parse($$"""
                {
                    "action": "security",
                    "directoryPath": "{{tempDir.Replace("\\", "\\\\")}}"
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.Equal(2, json.GetProperty("summary").GetProperty("totalFiles").GetInt32());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithoutDirectoryPath_FallsBackToSource()
    {
        // Ensure existing single-file behavior is unchanged
        var args = JsonDocument.Parse("""
            {
                "action": "security",
                "source": "module Test\n  func Add public (int:a, int:b) → int\n    !\n    return a + b\n"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
        Assert.Contains("summary", text);
    }

    [Fact]
    public async Task ExecuteAsync_AssessDirectory_ScansCSFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Simple.cs"),
                "public class Simple { public int Add(int a, int b) { return a + b; } }");
            File.WriteAllText(Path.Combine(tempDir, "Other.cs"),
                "public class Other { public string Greet(string name) { return \"Hello \" + name; } }");

            var args = JsonDocument.Parse($$"""
                {
                    "action": "assess",
                    "directoryPath": "{{tempDir.Replace("\\", "\\\\")}}"
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.True(json.GetProperty("summary").GetProperty("totalFiles").GetInt32() >= 1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AssessDirectory_NonexistentDir_ReturnsError()
    {
        var args = JsonDocument.Parse("""
            {
                "action": "assess",
                "directoryPath": "/nonexistent/assess/dir"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        Assert.Contains("Directory not found", result.Content[0].Text!);
    }

    [Fact]
    public async Task ExecuteAsync_MinimizeDirectory_ScansCalrFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "file1.calr"),
                "§M{Test1}\n§F{Add:pub}\n§I{i32:a}\n§O{i32}\n§CSHARP\nreturn a + 1;\n§/CSHARP\n\n");

            var args = JsonDocument.Parse($$"""
                {
                    "action": "minimize",
                    "directoryPath": "{{tempDir.Replace("\\", "\\\\")}}"
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.Equal(1, json.GetProperty("summary").GetProperty("totalFiles").GetInt32());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SecurityDirectory_MaxFiles_LimitsResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            for (int i = 0; i < 5; i++)
            {
                File.WriteAllText(Path.Combine(tempDir, $"file{i}.calr"),
                    $"§M{{m{i:D3}:Test{i}}}\n§F{{f{i:D3}:Fn:pub}}\n§O{{i32}}\n§R {i}\n}}\n}}");
            }

            var args = JsonDocument.Parse($$"""
                {
                    "action": "security",
                    "directoryPath": "{{tempDir.Replace("\\", "\\\\")}}",
                    "maxFiles": 2
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.Equal(2, json.GetProperty("summary").GetProperty("totalFiles").GetInt32());
            Assert.Equal(2, json.GetProperty("files").GetArrayLength());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
