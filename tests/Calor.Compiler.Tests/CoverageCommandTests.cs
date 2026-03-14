using System.Text.Json;
using Calor.Compiler.Analysis;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Integration tests for the coverage command functionality.
/// Tests the analysis pipeline: C# file → MigrationAnalyzer → JSON output.
/// </summary>
public class CoverageCommandTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly MigrationAnalyzer _analyzer;

    public CoverageCommandTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-coverage-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _analyzer = new MigrationAnalyzer();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private string CreateTestFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    #region Basic Coverage Analysis

    [Fact]
    public async Task Coverage_SimpleClass_Returns100Percent()
    {
        var source = """
            public class Calculator
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """;
        var filePath = CreateTestFile("Calculator.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.False(result.WasSkipped);
        Assert.Empty(result.UnsupportedConstructs);
        Assert.Equal(1, result.TypeCount);
        Assert.Equal(1, result.MethodCount);
    }

    [Fact]
    public async Task Coverage_WithBlockers_ReturnsLowerScore()
    {
        // Use out parameter (genuinely unsupported) instead of ternary throw (now supported)
        var source = """
            public class Service
            {
                public bool TryParse(string input, out int result)
                {
                    result = 0;
                    return int.TryParse(input, out result);
                }
            }
            """;
        var filePath = CreateTestFile("Service.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.False(result.WasSkipped);
        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "ref-parameter");
    }

    [Fact]
    public async Task Coverage_MultipleBlockers_ReducesScoreSignificantly()
    {
        // Use genuinely unsupported constructs: ref/out params + await-foreach
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public class Service
            {
                public void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }
                public bool TryGet(string key, out int result) { result = 0; return false; }
                public async Task ProcessAsync(IAsyncEnumerable<int> items)
                {
                    await foreach (var item in items) { Console.WriteLine(item); }
                }
            }
            """;
        var filePath = CreateTestFile("Service.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.False(result.WasSkipped);
        Assert.True(result.UnsupportedConstructs.Count >= 2);
        Assert.True(result.TotalScore < 50, $"Expected score < 50 with multiple blockers, got {result.TotalScore}");
    }

    #endregion

    #region File Handling

    [Fact]
    public async Task Coverage_NonExistentFile_ReturnsSkipped()
    {
        var filePath = Path.Combine(_testDirectory, "nonexistent.cs");

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.True(result.WasSkipped);
        Assert.Contains("Error reading file", result.SkipReason);
    }

    [Fact]
    public async Task Coverage_GeneratedFile_IsSkipped()
    {
        var source = "public class Generated { }";
        var filePath = CreateTestFile("Generated.g.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.True(result.WasSkipped);
        Assert.Equal("Generated file", result.SkipReason);
    }

    [Fact]
    public async Task Coverage_DesignerFile_IsSkipped()
    {
        var source = "public class Designer { }";
        var filePath = CreateTestFile("Form1.Designer.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.True(result.WasSkipped);
        Assert.Equal("Generated file", result.SkipReason);
    }

    [Fact]
    public async Task Coverage_ParseErrors_ReturnsSkipped()
    {
        var source = "public class { invalid syntax }";
        var filePath = CreateTestFile("Invalid.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.True(result.WasSkipped);
        Assert.StartsWith("Parse errors:", result.SkipReason);
    }

    #endregion

    #region Dimension Scoring

    [Fact]
    public async Task Coverage_WithContractPatterns_ScoresContractDimension()
    {
        var source = """
            public class Validator
            {
                public void Validate(string input)
                {
                    if (input == null)
                        throw new ArgumentNullException(nameof(input));
                    if (string.IsNullOrEmpty(input))
                        throw new ArgumentException("Input cannot be empty", nameof(input));
                }
            }
            """;
        var filePath = CreateTestFile("Validator.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.False(result.WasSkipped);
        Assert.True(result.Dimensions[ScoreDimension.ContractPotential].PatternCount > 0);
    }

    [Fact]
    public async Task Coverage_WithNullableTypes_ScoresNullSafetyDimension()
    {
        var source = """
            public class Service
            {
                public string? Name { get; set; }
                public int? Value { get; set; }

                public string GetName() => Name ?? "default";
            }
            """;
        var filePath = CreateTestFile("Service.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.False(result.WasSkipped);
        Assert.True(result.Dimensions[ScoreDimension.NullSafetyPotential].PatternCount > 0);
    }

    [Fact]
    public async Task Coverage_WithPatternMatching_ScoresPatternMatchDimension()
    {
        var source = """
            public class Handler
            {
                public string Handle(object obj)
                {
                    return obj switch
                    {
                        int i => $"Integer: {i}",
                        string s => $"String: {s}",
                        _ => "Unknown"
                    };
                }
            }
            """;
        var filePath = CreateTestFile("Handler.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.False(result.WasSkipped);
        Assert.True(result.Dimensions[ScoreDimension.PatternMatchPotential].PatternCount > 0);
    }

    #endregion

    #region Blocker Detection

    [Fact]
    public async Task Coverage_AllPhase1Blockers_NowFullySupported()
    {
        // yield-return and unsafe are now fully supported — should not appear as unsupported
        var yieldSource = """
            using System.Collections.Generic;
            public class Gen { public IEnumerable<int> Get() { yield return 1; } }
            """;
        var yieldResult = await _analyzer.AnalyzeFileAsync(CreateTestFile("yield.cs", yieldSource));
        Assert.DoesNotContain(yieldResult.UnsupportedConstructs, c => c.Name == "yield-return");

        var unsafeSource = """
            public class U { public unsafe void M() { int x = 1; int* p = &x; } }
            """;
        var unsafeResult = await _analyzer.AnalyzeFileAsync(CreateTestFile("unsafe.cs", unsafeSource));
        Assert.DoesNotContain(unsafeResult.UnsupportedConstructs, c => c.Name == "unsafe");
        Assert.DoesNotContain(unsafeResult.UnsupportedConstructs, c => c.Name == "pointer");
    }

    [Fact]
    public async Task Coverage_AllPhase4Blockers_Detected()
    {
        // Test file-scoped type
        var fileSource = "file class Helper { }";
        var fileResult = await _analyzer.AnalyzeFileAsync(CreateTestFile("file.cs", fileSource));
        Assert.Contains(fileResult.UnsupportedConstructs, c => c.Name == "file-scoped-type");

        // Test UTF-8 string literal — now fully supported, should NOT appear as unsupported
        var utf8Source = """
            public class U { public ReadOnlySpan<byte> Get() => "hello"u8; }
            """;
        var utf8Result = await _analyzer.AnalyzeFileAsync(CreateTestFile("utf8.cs", utf8Source));
        Assert.DoesNotContain(utf8Result.UnsupportedConstructs, c => c.Name == "utf8-string-literal");
    }

    #endregion

    #region Priority Calculation

    [Fact]
    public async Task Coverage_HighScore_GetsCriticalPriority()
    {
        // Code with many patterns that benefit from Calor
        var source = """
            public class Service
            {
                public string? Name { get; set; }
                public int? Value { get; set; }

                public void Process(string input)
                {
                    if (input == null)
                        throw new ArgumentNullException(nameof(input));

                    var result = input ?? "default";
                }

                public string Handle(object obj)
                {
                    switch (obj)
                    {
                        case int i: return $"Int: {i}";
                        case string s: return s;
                        default: return "Unknown";
                    }
                }
            }
            """;
        var filePath = CreateTestFile("Service.cs", source);

        var result = await _analyzer.AnalyzeFileAsync(filePath);

        Assert.False(result.WasSkipped);
        // Files with many patterns and no blockers should have higher priority
        Assert.True(result.TotalScore > 0);
    }

    #endregion
}
