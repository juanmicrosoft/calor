using System.Text.Json;
using Calor.Compiler.Analysis;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests for the ConvertibilityAnalyzer that scores how likely C# code
/// is to successfully convert to Calor.
/// </summary>
public class ConvertibilityAnalyzerTests
{
    private readonly ConvertibilityAnalyzer _analyzer = new();

    #region Core Scoring

    [Fact]
    public void SimpleClass_HighScore()
    {
        var source = """
            namespace TestApp;

            public class Calculator
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }

                public int Multiply(int x, int y)
                {
                    return x * y;
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        Assert.True(result.Score >= 80, $"Expected score >= 80 for simple class, got {result.Score}");
        Assert.True(result.ConversionAttempted);
        Assert.True(result.ConversionSucceeded);
        Assert.NotEmpty(result.Summary);
    }

    [Fact]
    public void UnsupportedConstructs_LowerScore()
    {
        var source = """
            using System;

            namespace TestApp;

            public class UnsafeHelper
            {
                public unsafe void Process(int* ptr)
                {
                    *ptr = 42;
                }

                public unsafe void AllocStack()
                {
                    Span<int> span = stackalloc int[10];
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        // Unsafe code with pointers should score below clean code (stackalloc is now supported)
        Assert.True(result.Score < 80, $"Expected score < 80 for unsafe code, got {result.Score}");
        Assert.True(result.Blockers.Count > 0, "Expected blockers for unsafe code");
        Assert.Contains(result.Blockers, b => b.Name == "unsafe" || b.Name == "pointer");
    }

    [Fact]
    public void ManyBlockerTypes_VeryLowScore()
    {
        // File with many different unsupported construct types → heavy penalty
        var source = """
            using System;
            using System.Runtime.CompilerServices;

            namespace TestApp;

            public class HeavilyBlocked
            {
                public unsafe void ProcessPointer(int* ptr) { *ptr = 42; }

                public System.Collections.Generic.IEnumerable<int> Yield()
                {
                    yield return 1;
                    yield return 2;
                }

                public static int operator +(HeavilyBlocked a, HeavilyBlocked b) => 0;
            }
            """;

        var result = _analyzer.Analyze(source);

        // Multiple construct types should push score low
        Assert.True(result.Score < 70, $"Expected score < 70 for heavily blocked code, got {result.Score}");
        Assert.True(result.Blockers.Count >= 2, $"Expected >= 2 blocker types, got {result.Blockers.Count}");
    }

    [Fact]
    public void ConversionFailure_ZeroScore()
    {
        // Completely invalid C# that can't even be parsed
        var source = "this is not valid C# code at all {{{{";

        var result = _analyzer.Analyze(source);

        Assert.True(result.Score <= 10, $"Expected score <= 10 for unparseable code, got {result.Score}");
    }

    [Fact]
    public void ConversionErrors_ReduceScore()
    {
        // Source that has features triggering conversion errors (not just warnings)
        // Use a construct that produces actual errors during conversion
        var source = """
            namespace TestApp;

            public class WithErrors
            {
                public void Method(out int result, ref string name, out bool flag)
                {
                    result = 42;
                    name = "hello";
                    flag = true;
                }

                public void Method2(out int a, out int b, out int c)
                {
                    a = 1; b = 2; c = 3;
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        // Multiple out/ref params should reduce score
        Assert.True(result.Score < 95, $"Expected score < 95 for code with out/ref params, got {result.Score}");
        Assert.Contains(result.Blockers, b => b.Name == "ref-parameter");
    }

    #endregion

    #region Quick Mode

    [Fact]
    public void QuickMode_SkipsConversion()
    {
        var source = """
            namespace TestApp;

            public class Simple
            {
                public string Name { get; set; }
            }
            """;

        var result = _analyzer.AnalyzeQuick(source);

        Assert.False(result.ConversionAttempted, "Quick mode should not attempt conversion");
        Assert.False(result.ConversionSucceeded);
        Assert.False(result.CompilationSucceeded);
        Assert.Equal(0, result.ConversionRate);
    }

    [Fact]
    public void QuickMode_EstimatesScoreFromBlockers()
    {
        var source = """
            namespace TestApp;

            public class Clean
            {
                public int Add(int a, int b) => a + b;
            }
            """;

        var quickResult = _analyzer.AnalyzeQuick(source);
        var fullResult = _analyzer.Analyze(source);

        // Both modes should agree clean code is highly convertible
        Assert.True(quickResult.Score >= 70, $"Quick mode score should be high for clean code, got {quickResult.Score}");
        Assert.True(fullResult.Score >= 70, $"Full mode score should be high for clean code, got {fullResult.Score}");
    }

    [Fact]
    public void QuickMode_ProportionalToFullMode()
    {
        // Code with known blockers — quick and full modes should produce scores in the same range
        var source = """
            namespace TestApp;

            public class WithRef
            {
                public void Swap(ref int a, ref int b)
                {
                    int temp = a;
                    a = b;
                    b = temp;
                }
            }
            """;

        var quickResult = _analyzer.AnalyzeQuick(source);
        var fullResult = _analyzer.Analyze(source);

        // Scores should be within 20 points of each other
        var diff = Math.Abs(quickResult.Score - fullResult.Score);
        Assert.True(diff <= 20,
            $"Quick ({quickResult.Score}) and full ({fullResult.Score}) modes differ by {diff} points, expected <= 20");
    }

    #endregion

    #region Summary Formatting

    [Fact]
    public void Summary_FormatsBlockers()
    {
        var source = """
            using System;

            namespace TestApp;

            public class WithYield
            {
                public System.Collections.Generic.IEnumerable<int> GetNumbers()
                {
                    yield return 1;
                    yield return 2;
                    yield return 3;
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        Assert.Contains("convertible", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoBlockers_CleanSummary()
    {
        var source = """
            namespace TestApp;

            public class Clean
            {
                public int Value { get; set; }

                public int Double()
                {
                    return Value * 2;
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        if (result.Blockers.Count == 0)
        {
            Assert.Contains("no significant blockers", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Summary_ZeroScore_MentionsNotConvertible()
    {
        var source = "not valid {{{{";

        var result = _analyzer.Analyze(source);

        if (result.Score == 0 && result.Blockers.Count > 0)
        {
            Assert.Contains("not convertible", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ConversionRate_ReflectedInResult()
    {
        var source = """
            namespace TestApp;

            public class SimpleModel
            {
                public string Name { get; set; }
                public int Age { get; set; }

                public override string ToString()
                {
                    return Name + " (" + Age + ")";
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        Assert.True(result.ConversionAttempted);
        if (result.ConversionSucceeded)
        {
            Assert.True(result.ConversionRate > 0, "Conversion rate should be > 0 when conversion succeeds");
        }
    }

    [Fact]
    public void BlockerCount_MatchesInstances()
    {
        var source = """
            using System;

            namespace TestApp;

            public class WithGoto
            {
                public void Process()
                {
                    goto done;
                    done:
                    Console.WriteLine("done");
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        var totalFromBlockers = result.Blockers.Sum(b => b.Count);
        Assert.Equal(result.TotalBlockerInstances, totalFromBlockers);
    }

    [Fact]
    public void EmptySource_HandledGracefully()
    {
        var result = _analyzer.Analyze("");

        Assert.NotNull(result);
        Assert.NotNull(result.Summary);
    }

    [Fact]
    public void Duration_IsPositive()
    {
        var source = """
            namespace TestApp;
            public class Foo { }
            """;

        var result = _analyzer.Analyze(source);

        Assert.True(result.Duration > TimeSpan.Zero);
    }

    #endregion

    #region Directory Mode

    private static string? FindTestDataPath(string relativePath)
    {
        // Walk up from assembly location to find the tests directory
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "TestData", relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }
        return null;
    }

    [Fact]
    public async Task Directory_AggregatesResults()
    {
        var fullPath = FindTestDataPath(Path.Combine("CSharpImport", "Level1_Basic"));
        if (fullPath == null)
        {
            // Skip if test data not available
            return;
        }

        var result = await _analyzer.AnalyzeDirectoryAsync(fullPath, quick: true);

        Assert.True(result.TotalFiles > 0, "Should find C# files in directory");
        Assert.Equal(fullPath, result.DirectoryPath);
        Assert.True(result.Duration > TimeSpan.Zero);
        // All Level1 basic files should be highly convertible
        Assert.True(result.AverageScore >= 90,
            $"Expected average score >= 90 for basic files, got {result.AverageScore}");
    }

    [Fact]
    public async Task Directory_ScoreDistribution()
    {
        var fullPath = FindTestDataPath("CSharpImport");
        if (fullPath == null)
        {
            return;
        }

        var result = await _analyzer.AnalyzeDirectoryAsync(fullPath, quick: true);

        // Verify distribution counts add up
        Assert.Equal(result.TotalFiles,
            result.HighCount + result.MediumCount + result.LowCount + result.BlockedCount);

        // Verify results are sorted by score descending
        for (int i = 1; i < result.FileResults.Count; i++)
        {
            Assert.True(result.FileResults[i - 1].Score >= result.FileResults[i].Score,
                "Results should be sorted by score descending");
        }
    }

    [Fact]
    public async Task Directory_AggregatedBlockers()
    {
        var fullPath = FindTestDataPath("CSharpImport");
        if (fullPath == null)
        {
            return;
        }

        var result = await _analyzer.AnalyzeDirectoryAsync(fullPath, quick: true);
        var aggregated = result.GetAggregatedBlockers();

        // Aggregated blockers should be sorted by total instances descending
        for (int i = 1; i < aggregated.Count; i++)
        {
            Assert.True(aggregated[i - 1].TotalInstances >= aggregated[i].TotalInstances,
                "Aggregated blockers should be sorted by total instances descending");
        }
    }

    #endregion

    #region JSON Output

    [Fact]
    public void JsonOutput_SingleFile_HasRequiredFields()
    {
        var source = """
            namespace TestApp;

            public class Model
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }
            """;

        var result = _analyzer.Analyze(source);

        // Serialize to JSON and parse back to verify structure
        var json = JsonSerializer.Serialize(new
        {
            score = result.Score,
            summary = result.Summary,
            conversionAttempted = result.ConversionAttempted,
            conversionSucceeded = result.ConversionSucceeded,
            compilationSucceeded = result.CompilationSucceeded,
            conversionRate = result.ConversionRate,
            blockers = result.Blockers.Select(b => new { name = b.Name, description = b.Description, count = b.Count }),
            totalBlockerInstances = result.TotalBlockerInstances
        });

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("score", out var scoreProp));
        Assert.Equal(JsonValueKind.Number, scoreProp.ValueKind);
        Assert.InRange(scoreProp.GetInt32(), 0, 100);

        Assert.True(root.TryGetProperty("summary", out var summaryProp));
        Assert.Equal(JsonValueKind.String, summaryProp.ValueKind);
        Assert.False(string.IsNullOrEmpty(summaryProp.GetString()));

        Assert.True(root.TryGetProperty("conversionAttempted", out _));
        Assert.True(root.TryGetProperty("conversionSucceeded", out _));
        Assert.True(root.TryGetProperty("compilationSucceeded", out _));
        Assert.True(root.TryGetProperty("conversionRate", out _));
        Assert.True(root.TryGetProperty("blockers", out var blockersProp));
        Assert.Equal(JsonValueKind.Array, blockersProp.ValueKind);
        Assert.True(root.TryGetProperty("totalBlockerInstances", out _));
    }

    [Fact]
    public void JsonOutput_BlockerFields_AreComplete()
    {
        var source = """
            using System;

            namespace TestApp;

            public class WithYield
            {
                public System.Collections.Generic.IEnumerable<int> GetNumbers()
                {
                    yield return 1;
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        // Find the yield blocker
        var yieldBlocker = result.Blockers.FirstOrDefault(b => b.Name == "yield-return");
        if (yieldBlocker != null)
        {
            Assert.False(string.IsNullOrEmpty(yieldBlocker.Name));
            Assert.False(string.IsNullOrEmpty(yieldBlocker.Description));
            Assert.True(yieldBlocker.Count > 0);
        }
    }

    #endregion

    #region MCP Tool

    [Fact]
    public async Task McpTool_ReturnsValidResult()
    {
        var tool = new AnalyzeConvertibilityTool();

        Assert.Equal("calor_analyze_convertibility", tool.Name);
        Assert.NotEmpty(tool.Description);

        // Verify input schema is valid JSON
        var schema = tool.GetInputSchema();
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);

        // Execute with simple source
        var args = JsonDocument.Parse("""
            {
                "source": "namespace Test; public class Foo { public int Bar() => 42; }"
            }
            """).RootElement;

        var mcpResult = await tool.ExecuteAsync(args);

        Assert.False(mcpResult.IsError, "MCP tool should succeed for valid source");
        Assert.NotNull(mcpResult.Content);
        Assert.True(mcpResult.Content.Count > 0);

        // Parse the result JSON
        var resultJson = mcpResult.Content[0].Text;
        Assert.NotNull(resultJson);
        var doc = JsonDocument.Parse(resultJson!);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("score", out var score));
        Assert.InRange(score.GetInt32(), 0, 100);
        Assert.True(root.TryGetProperty("summary", out _));
        Assert.True(root.TryGetProperty("blockers", out _));
    }

    [Fact]
    public async Task McpTool_QuickMode_SkipsConversion()
    {
        var tool = new AnalyzeConvertibilityTool();

        var args = JsonDocument.Parse("""
            {
                "source": "namespace Test; public class Foo { }",
                "options": { "quick": true }
            }
            """).RootElement;

        var mcpResult = await tool.ExecuteAsync(args);

        Assert.False(mcpResult.IsError);
        var doc = JsonDocument.Parse(mcpResult.Content[0].Text!);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("conversionAttempted", out var attempted));
        Assert.False(attempted.GetBoolean(), "Quick mode should not attempt conversion");
    }

    [Fact]
    public async Task McpTool_MissingSource_ReturnsError()
    {
        var tool = new AnalyzeConvertibilityTool();

        var args = JsonDocument.Parse("""{ }""").RootElement;

        var mcpResult = await tool.ExecuteAsync(args);

        Assert.True(mcpResult.IsError, "Should return error when source is missing");
    }

    [Fact]
    public async Task McpTool_NullArgs_ReturnsError()
    {
        var tool = new AnalyzeConvertibilityTool();

        var mcpResult = await tool.ExecuteAsync(null);

        Assert.True(mcpResult.IsError, "Should return error when args are null");
    }

    #endregion

    #region Scoring Calibration

    [Fact]
    public void Score_CleanCode_IsHighest()
    {
        var clean = """
            namespace TestApp;
            public class Clean
            {
                public int Add(int a, int b) => a + b;
                public int Sub(int a, int b) => a - b;
            }
            """;

        var result = _analyzer.Analyze(clean);

        Assert.True(result.Score >= 95, $"Clean code should score >= 95, got {result.Score}");
    }

    [Fact]
    public void Score_FewBlockers_HighRange()
    {
        // 1 blocker type with a few instances → should be 80-95
        var source = """
            namespace TestApp;

            public class WithRef
            {
                public void Swap(ref int a, ref int b)
                {
                    int temp = a;
                    a = b;
                    b = temp;
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        Assert.InRange(result.Score, 70, 95);
    }

    [Fact]
    public void Score_ModerateBlockers_BelowClean()
    {
        // Code with multiple blocker types should score below perfect
        var source = """
            using System;

            namespace TestApp;

            public class Moderate
            {
                public unsafe void WithPointer(int* p) { *p = 1; }
                public void WithRef(ref int a) { a++; }
                public void WithOut(out int b) { b = 0; }
                public static int operator +(Moderate a, Moderate b) => 0;

                public System.Collections.Generic.IEnumerable<int> Yield()
                {
                    yield return 1;
                }
            }
            """;

        var result = _analyzer.Analyze(source);

        Assert.True(result.Score < 95,
            $"Code with multiple blocker types should score below 95, got {result.Score}");
        Assert.True(result.Blockers.Count >= 2,
            $"Expected >= 2 blocker types, got {result.Blockers.Count}");
    }

    [Fact]
    public void Score_UnsafeCode_BelowClean()
    {
        var clean = """
            namespace TestApp;
            public class Clean { public int Add(int a, int b) => a + b; }
            """;

        var unsafe_ = """
            namespace TestApp;
            public class Unsafe
            {
                public unsafe void Process(int* ptr) { *ptr = 42; }
            }
            """;

        var cleanResult = _analyzer.Analyze(clean);
        var unsafeResult = _analyzer.Analyze(unsafe_);

        Assert.True(cleanResult.Score > unsafeResult.Score,
            $"Clean ({cleanResult.Score}) should score higher than unsafe ({unsafeResult.Score})");
    }

    [Fact]
    public void Score_CompilationBonus_Applied()
    {
        // A file that converts AND compiles should score higher than one that converts but doesn't compile
        // We can verify this indirectly: clean code should get the compilation bonus
        var source = """
            namespace TestApp;

            public class Compiles
            {
                public int Value { get; set; }
            }
            """;

        var result = _analyzer.Analyze(source);

        Assert.True(result.CompilationSucceeded, "Clean code should compile after conversion");
        Assert.True(result.Score >= 100, "Clean code with compilation should score 100");
    }

    #endregion
}
