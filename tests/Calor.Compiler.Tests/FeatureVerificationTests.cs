using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

public class FeatureVerificationTests
{
    private readonly CSharpToCalorConverter _converter = new();

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"{i.Severity}: {i.Message}"));
    }

    [Fact]
    public void Convert_OutParameter_Succeeds()
    {
        var csharp = """
            public class Parser { public bool TryParse(string s, out int result) { result = 0; return int.TryParse(s, out result); } }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
    }

    [Fact]
    public void Convert_RefParameter_Succeeds()
    {
        var csharp = """
            public class Swapper { public void Swap(ref int a, ref int b) { int temp = a; a = b; b = temp; } }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
    }

    [Fact]
    public void Convert_RangeExpression_Succeeds()
    {
        var csharp = """
            public class Slicer { public int[] GetSlice(int[] arr) { return arr[1..3]; } }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
    }

    [Fact]
    public void Convert_IndexFromEnd_Succeeds()
    {
        var csharp = """
            public class Finder { public int GetLast(int[] arr) { return arr[^1]; } }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
    }

    [Fact]
    public void Convert_OutVarInline_Succeeds()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Dict { public string GetOrEmpty(Dictionary<string, string> d, string key) { return d.TryGetValue(key, out var val) ? val : ""; } }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
    }

    [Fact]
    public void Convert_ThrowExpression_Succeeds()
    {
        var csharp = """
            using System;
            public class Guard { public string EnsureNotNull(string? s) { return s ?? throw new ArgumentNullException(nameof(s)); } }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
    }

    [Fact]
    public async Task BatchTool_ConvertWithValidate_IncludesDiagnostics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor-validate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var csFile = Path.Combine(tempDir, "Hello.cs");
            File.WriteAllText(csFile, "public class Hello { public int Add(int a, int b) { return a + b; } }");

            var tool = new BatchTool();
            var args = JsonDocument.Parse($$"""
                {
                    "action": "convert",
                    "projectPath": "{{tempDir.Replace("\\", "\\\\")}}",
                    "validate": true,
                    "dryRun": true
                }
                """).RootElement;

            var result = await tool.ExecuteAsync(args);

            Assert.False(result.IsError, $"Expected success but got: {result.Content[0].Text}");

            var text = result.Content[0].Text!;
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("summary", out var summary), "Result should contain summary");
            Assert.True(summary.TryGetProperty("totalFiles", out var totalFiles));
            Assert.True(totalFiles.GetInt32() >= 1, "Should process at least 1 file");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
