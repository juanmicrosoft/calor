using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class AssessToolTests
{
    private readonly AssessTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorAssess()
    {
        Assert.Equal("calor_assess", _tool.Name);
    }

    [Fact]
    public void Description_ContainsAssessInfo()
    {
        Assert.Contains("Assess", _tool.Description);
        Assert.Contains("C#", _tool.Description);
        Assert.Contains("migration", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("files", out _));
        Assert.True(props.TryGetProperty("options", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingInput_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("source", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithSimpleClass_ReturnsScores()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calculator { public int Add(int a, int b) => a + b; }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
        Assert.Contains("summary", text);
        Assert.Contains("files", text);
        Assert.Contains("totalFiles", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithAsyncCode_DetectsAsyncPatterns()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Service { public async Task<int> GetValueAsync() { return await Task.FromResult(42); } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("AsyncPotential", text);
        // The async patterns should be detected
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        Assert.True(files.GetArrayLength() > 0);
        var file = files[0];
        var dimensions = file.GetProperty("dimensions");
        Assert.True(dimensions.TryGetProperty("AsyncPotential", out var asyncDim));
        var patterns = asyncDim.GetProperty("patterns").GetInt32();
        Assert.True(patterns > 0, "Should detect async patterns");
    }

    [Fact]
    public async Task ExecuteAsync_WithLinqCode_DetectsLinqPatterns()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "using System.Linq; public class DataProcessor { public int[] Process(int[] data) { return data.Where(x => x > 0).Select(x => x * 2).ToArray(); } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("LinqPotential", text);
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        Assert.True(files.GetArrayLength() > 0);
        var file = files[0];
        var dimensions = file.GetProperty("dimensions");
        Assert.True(dimensions.TryGetProperty("LinqPotential", out var linqDim));
        var patterns = linqDim.GetProperty("patterns").GetInt32();
        Assert.True(patterns > 0, "Should detect LINQ patterns");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullChecks_DetectsNullSafetyPatterns()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Validator { public string? Name { get; set; } public bool IsValid() { return Name != null && Name.Length > 0; } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("NullSafetyPotential", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithErrorHandling_DetectsErrorPatterns()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Parser { public int Parse(string s) { try { return int.Parse(s); } catch (Exception ex) { throw new InvalidOperationException(\"Failed\", ex); } } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("ErrorHandlingPotential", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnsupportedConstructs_ReportsConstruct()
    {
        // Primary constructors are unsupported
        var args = JsonDocument.Parse("""
            {
                "source": "public class Point(int x, int y) { public int X => x; public int Y => y; }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("unsupportedConstructs", text);
        Assert.Contains("primary-constructor", text);
    }

    [Fact]
    public async Task ExecuteAsync_MultiFileMode_AnalyzesAllFiles()
    {
        var args = JsonDocument.Parse("""
            {
                "files": [
                    { "path": "Calculator.cs", "source": "public class Calculator { public int Add(int a, int b) => a + b; }" },
                    { "path": "Validator.cs", "source": "public class Validator { public bool IsValid(string? s) => s != null; }" }
                ]
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var summary = json.RootElement.GetProperty("summary");
        var totalFiles = summary.GetProperty("totalFiles").GetInt32();
        Assert.Equal(2, totalFiles);

        var files = json.RootElement.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
    }

    [Fact]
    public async Task ExecuteAsync_WithThreshold_FiltersLowScores()
    {
        var args = JsonDocument.Parse("""
            {
                "files": [
                    { "path": "Simple.cs", "source": "public class Simple { }" },
                    { "path": "Complex.cs", "source": "public class Complex { public async Task<string?> ProcessAsync(string input) { if (input == null) throw new ArgumentNullException(nameof(input)); try { return await Task.FromResult(input.ToUpper()); } catch { return null; } } }" }
                ],
                "options": { "threshold": 50 }
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        // Files below threshold should be filtered out
        foreach (var file in files.EnumerateArray())
        {
            var score = file.GetProperty("score").GetInt32();
            Assert.True(score >= 50, $"File score {score} should be >= 50");
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPriorityBreakdown()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Service { public async Task<int> GetValueAsync() { return await Task.FromResult(42); } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("priorityBreakdown", text);
        Assert.Contains("critical", text);
        Assert.Contains("high", text);
        Assert.Contains("medium", text);
        Assert.Contains("low", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithContractValidation_DetectsContractPatterns()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Guard { public void Validate(int value) { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); if (value > 100) throw new ArgumentException(\"Too large\", nameof(value)); } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("ContractPotential", text);
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var file = files[0];
        var dimensions = file.GetProperty("dimensions");
        var contractDim = dimensions.GetProperty("ContractPotential");
        var patterns = contractDim.GetProperty("patterns").GetInt32();
        Assert.True(patterns > 0, "Should detect contract patterns");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsDimensionScoresAndPatternCounts()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Test { public void Method() { } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var file = files[0];
        var dimensions = file.GetProperty("dimensions");

        // Check all 8 dimensions are present
        Assert.True(dimensions.TryGetProperty("ContractPotential", out _));
        Assert.True(dimensions.TryGetProperty("EffectPotential", out _));
        Assert.True(dimensions.TryGetProperty("NullSafetyPotential", out _));
        Assert.True(dimensions.TryGetProperty("ErrorHandlingPotential", out _));
        Assert.True(dimensions.TryGetProperty("PatternMatchPotential", out _));
        Assert.True(dimensions.TryGetProperty("ApiComplexityPotential", out _));
        Assert.True(dimensions.TryGetProperty("AsyncPotential", out _));
        Assert.True(dimensions.TryGetProperty("LinqPotential", out _));

        // Each dimension should have score and patterns
        var firstDim = dimensions.GetProperty("ContractPotential");
        Assert.True(firstDim.TryGetProperty("score", out _));
        Assert.True(firstDim.TryGetProperty("patterns", out _));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFileMetadata()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Test { public void Method1() { } public void Method2() { } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var file = files[0];

        Assert.True(file.TryGetProperty("lineCount", out _));
        Assert.True(file.TryGetProperty("methodCount", out var methodCount));
        Assert.True(file.TryGetProperty("typeCount", out var typeCount));

        Assert.Equal(2, methodCount.GetInt32());
        Assert.Equal(1, typeCount.GetInt32());
    }

    #region Edge Case Tests

    [Fact]
    public async Task ExecuteAsync_WithEmptySource_ReturnsEmptyResults()
    {
        var args = JsonDocument.Parse("""
            {
                "source": ""
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // Empty source should be treated as missing
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_WithBothSourceAndFiles_PrioritizesSource()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class FromSource { }",
                "files": [
                    { "path": "FromFiles.cs", "source": "public class FromFiles { }" }
                ]
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");

        // Should only have the source file (input.cs), not the files array
        Assert.Equal(1, files.GetArrayLength());
        Assert.Equal("input.cs", files[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithParseErrors_SkipsFile()
    {
        var args = JsonDocument.Parse("""
            {
                "files": [
                    { "path": "Valid.cs", "source": "public class Valid { }" },
                    { "path": "Invalid.cs", "source": "public class { invalid syntax" }
                ]
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");

        // Only the valid file should be included (invalid file is skipped)
        Assert.Equal(1, files.GetArrayLength());
        Assert.Equal("Valid.cs", files[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyFileInArray_SkipsEmptyFile()
    {
        var args = JsonDocument.Parse("""
            {
                "files": [
                    { "path": "Valid.cs", "source": "public class Valid { }" },
                    { "path": "Empty.cs", "source": "" }
                ]
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");

        // Only the valid file should be included
        Assert.Equal(1, files.GetArrayLength());
    }

    [Fact]
    public async Task ExecuteAsync_WithAsyncLambda_DetectsAsyncPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "using System; public class Test { public void Run() { Func<Task> f = async () => await Task.Delay(1); } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var dimensions = files[0].GetProperty("dimensions");
        var asyncDim = dimensions.GetProperty("AsyncPotential");
        var patterns = asyncDim.GetProperty("patterns").GetInt32();
        Assert.True(patterns > 0, "Should detect async lambda pattern");
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellationToken_DetectsAsyncPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "using System.Threading; public class Service { public void Process(CancellationToken token) { } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var dimensions = files[0].GetProperty("dimensions");
        var asyncDim = dimensions.GetProperty("AsyncPotential");
        var patterns = asyncDim.GetProperty("patterns").GetInt32();
        Assert.True(patterns > 0, "Should detect CancellationToken pattern");
    }

    [Fact]
    public async Task ExecuteAsync_WithConfigureAwait_DetectsAsyncPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "using System.Threading.Tasks; public class Service { public async Task ProcessAsync() { await Task.Delay(1).ConfigureAwait(false); } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var dimensions = files[0].GetProperty("dimensions");
        var asyncDim = dimensions.GetProperty("AsyncPotential");
        var patterns = asyncDim.GetProperty("patterns").GetInt32();
        // Should detect: async modifier, Task return, await, ConfigureAwait
        Assert.True(patterns >= 3, $"Should detect multiple async patterns, got {patterns}");
    }

    [Fact]
    public async Task ExecuteAsync_WithLinqQuerySyntax_DetectsLinqPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "using System.Linq; public class Query { public int[] Filter(int[] data) { return (from x in data where x > 0 select x * 2).ToArray(); } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var dimensions = files[0].GetProperty("dimensions");
        var linqDim = dimensions.GetProperty("LinqPotential");
        var patterns = linqDim.GetProperty("patterns").GetInt32();
        // Should detect: query expression, where clause, ToArray
        Assert.True(patterns >= 2, $"Should detect LINQ query syntax patterns, got {patterns}");
    }

    [Fact]
    public async Task ExecuteAsync_WithIAsyncEnumerable_DetectsAsyncPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "using System.Collections.Generic; public class Stream { public async IAsyncEnumerable<int> GetItemsAsync() { yield return 1; } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var dimensions = files[0].GetProperty("dimensions");
        var asyncDim = dimensions.GetProperty("AsyncPotential");
        var patterns = asyncDim.GetProperty("patterns").GetInt32();
        Assert.True(patterns >= 2, $"Should detect IAsyncEnumerable pattern, got {patterns}");
    }

    [Fact]
    public async Task ExecuteAsync_WithAwaitForeach_DetectsAsyncPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "using System.Collections.Generic; using System.Threading.Tasks; public class Processor { public async Task ProcessAsync(IAsyncEnumerable<int> items) { await foreach (var item in items) { } } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var dimensions = files[0].GetProperty("dimensions");
        var asyncDim = dimensions.GetProperty("AsyncPotential");
        var patterns = asyncDim.GetProperty("patterns").GetInt32();
        // Should detect: async modifier, Task return, IAsyncEnumerable param, await foreach
        Assert.True(patterns >= 3, $"Should detect await foreach pattern, got {patterns}");
    }

    [Fact]
    public async Task ExecuteAsync_WithAwaitUsing_DetectsAsyncPattern()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "using System; using System.Threading.Tasks; public class Resource { public async Task UseAsync() { await using var r = new AsyncRes(); } } public class AsyncRes : IAsyncDisposable { public ValueTask DisposeAsync() => default; }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");
        var dimensions = files[0].GetProperty("dimensions");
        var asyncDim = dimensions.GetProperty("AsyncPotential");
        var patterns = asyncDim.GetProperty("patterns").GetInt32();
        // Should detect: async modifier, Task return, await using, IAsyncDisposable, ValueTask
        Assert.True(patterns >= 3, $"Should detect await using pattern, got {patterns}");
    }

    [Fact]
    public async Task ExecuteAsync_FilesSortedByScoreDescending()
    {
        // Create files with different complexity levels
        var args = JsonDocument.Parse("""
            {
                "files": [
                    { "path": "Simple.cs", "source": "public class Simple { }" },
                    { "path": "Complex.cs", "source": "public class Complex { public async Task<string?> ProcessAsync(string input) { if (input == null) throw new ArgumentNullException(nameof(input)); try { return await Task.FromResult(input.ToUpper()); } catch { return null; } } }" },
                    { "path": "Medium.cs", "source": "public class Medium { public string? Name { get; set; } public bool IsValid() => Name != null; }" }
                ]
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var files = json.RootElement.GetProperty("files");

        // Verify files are sorted by score descending
        var scores = files.EnumerateArray()
            .Select(f => f.GetProperty("score").GetInt32())
            .ToList();

        for (int i = 0; i < scores.Count - 1; i++)
        {
            Assert.True(scores[i] >= scores[i + 1],
                $"Files should be sorted by score descending: {scores[i]} >= {scores[i + 1]}");
        }
    }

    #endregion
}
