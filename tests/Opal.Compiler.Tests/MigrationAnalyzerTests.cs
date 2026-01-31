using Opal.Compiler.Analysis;
using Xunit;

namespace Opal.Compiler.Tests;

public class MigrationAnalyzerTests
{
    private readonly MigrationAnalyzer _analyzer = new();

    #region Contract Potential Detection

    [Fact]
    public void AnalyzeSource_ArgumentNullException_DetectsContractPattern()
    {
        var source = """
            public class Service
            {
                public void Process(string input)
                {
                    if (input == null)
                        throw new ArgumentNullException(nameof(input));
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.False(result.WasSkipped);
        Assert.True(result.Dimensions[ScoreDimension.ContractPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_ArgumentException_DetectsContractPattern()
    {
        var source = """
            public class Validator
            {
                public void Validate(int value)
                {
                    if (value < 0)
                        throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.ContractPotential].PatternCount > 0);
    }

    #endregion

    #region Effect Potential Detection

    [Fact]
    public void AnalyzeSource_FileOperations_DetectsEffectPattern()
    {
        var source = """
            using System.IO;
            public class FileService
            {
                public string Read(string path)
                {
                    return File.ReadAllText(path);
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.EffectPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_ConsoleOutput_DetectsEffectPattern()
    {
        var source = """
            public class Logger
            {
                public void Log(string message)
                {
                    Console.WriteLine(message);
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.EffectPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_DatabaseCall_DetectsEffectPattern()
    {
        var source = """
            public class Repository
            {
                public void Save()
                {
                    _context.SaveChanges();
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.EffectPotential].PatternCount > 0);
    }

    #endregion

    #region Null Safety Detection

    [Fact]
    public void AnalyzeSource_NullableType_DetectsNullSafetyPattern()
    {
        var source = """
            public class Service
            {
                public string? Name { get; set; }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.NullSafetyPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_NullCheck_DetectsNullSafetyPattern()
    {
        var source = """
            public class Service
            {
                public bool IsValid(object obj)
                {
                    return obj != null;
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.NullSafetyPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_NullCoalescing_DetectsNullSafetyPattern()
    {
        var source = """
            public class Service
            {
                public string GetName(string? input)
                {
                    return input ?? "default";
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.NullSafetyPotential].PatternCount >= 2); // nullable + ??
    }

    [Fact]
    public void AnalyzeSource_ConditionalAccess_DetectsNullSafetyPattern()
    {
        var source = """
            public class Service
            {
                public int? GetLength(string? input)
                {
                    return input?.Length;
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.NullSafetyPotential].PatternCount >= 2); // nullable + ?.
    }

    [Fact]
    public void AnalyzeSource_IsNullPattern_DetectsNullSafetyPattern()
    {
        var source = """
            public class Service
            {
                public bool Check(object? obj)
                {
                    return obj is null;
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.NullSafetyPotential].PatternCount > 0);
    }

    #endregion

    #region Error Handling Detection

    [Fact]
    public void AnalyzeSource_TryCatch_DetectsErrorHandlingPattern()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    try
                    {
                        DoWork();
                    }
                    catch (Exception ex)
                    {
                        Log(ex);
                    }
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.ErrorHandlingPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_ThrowStatement_DetectsErrorHandlingPattern()
    {
        var source = """
            public class Service
            {
                public void Fail()
                {
                    throw new InvalidOperationException("Error");
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.ErrorHandlingPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_MultipleCatchBlocks_CountsAll()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    try { }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                    catch (Exception) { }
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        // Should count try + 3 catches = 4
        Assert.True(result.Dimensions[ScoreDimension.ErrorHandlingPotential].PatternCount >= 4);
    }

    #endregion

    #region Pattern Matching Detection

    [Fact]
    public void AnalyzeSource_SwitchStatement_DetectsPatternMatchingPattern()
    {
        var source = """
            public class Service
            {
                public string GetDay(int day)
                {
                    switch (day)
                    {
                        case 1: return "Monday";
                        case 2: return "Tuesday";
                        default: return "Unknown";
                    }
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.PatternMatchPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_SwitchExpression_DetectsPatternMatchingPattern()
    {
        var source = """
            public class Service
            {
                public string GetDay(int day) => day switch
                {
                    1 => "Monday",
                    2 => "Tuesday",
                    _ => "Unknown"
                };
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.Dimensions[ScoreDimension.PatternMatchPotential].PatternCount > 0);
    }

    #endregion

    #region API Complexity Detection

    [Fact]
    public void AnalyzeSource_UndocumentedPublicMethod_DetectsApiComplexity()
    {
        var source = """
            public class Service
            {
                public void Process() { }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        // Public method + public class = 2 undocumented public members
        Assert.True(result.Dimensions[ScoreDimension.ApiComplexityPotential].PatternCount >= 1);
    }

    [Fact]
    public void AnalyzeSource_DocumentedPublicMethod_DoesNotCountAsUndocumented()
    {
        var source = """
            public class Service
            {
                /// <summary>Processes data.</summary>
                public void Process() { }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        // Only the class is undocumented
        var apiScore = result.Dimensions[ScoreDimension.ApiComplexityPotential];
        Assert.True(apiScore.PatternCount <= 1); // Only undocumented class
    }

    #endregion

    #region Score Calculation

    [Fact]
    public void AnalyzeSource_EmptyClass_HasLowScore()
    {
        var source = """
            public class Empty { }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        // Empty class has API complexity (undocumented public) but little else
        // Weight of ApiComplexityPotential is 15%, so max score from that alone is ~15
        Assert.True(result.TotalScore <= 25, $"Expected score <= 25 but got {result.TotalScore}");
        Assert.Equal(MigrationPriority.Low, result.Priority);
    }

    [Fact]
    public void AnalyzeSource_ComplexClass_HasHigherScore()
    {
        var source = """
            public class ComplexService
            {
                public string? Name { get; set; }

                public void Process(string input)
                {
                    if (input == null)
                        throw new ArgumentNullException(nameof(input));

                    try
                    {
                        Console.WriteLine(input);
                        File.WriteAllText("log.txt", input ?? "empty");
                    }
                    catch (Exception ex)
                    {
                        throw;
                    }
                }

                public string GetStatus(int code) => code switch
                {
                    200 => "OK",
                    404 => "Not Found",
                    _ => "Unknown"
                };
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        // Should have patterns in multiple dimensions
        Assert.True(result.Dimensions[ScoreDimension.NullSafetyPotential].PatternCount > 0);
        Assert.True(result.Dimensions[ScoreDimension.ErrorHandlingPotential].PatternCount > 0);
        Assert.True(result.Dimensions[ScoreDimension.EffectPotential].PatternCount > 0);
        Assert.True(result.Dimensions[ScoreDimension.PatternMatchPotential].PatternCount > 0);
    }

    #endregion

    #region Priority Bands

    [Theory]
    [InlineData(0, MigrationPriority.Low)]
    [InlineData(25, MigrationPriority.Low)]
    [InlineData(26, MigrationPriority.Medium)]
    [InlineData(50, MigrationPriority.Medium)]
    [InlineData(51, MigrationPriority.High)]
    [InlineData(75, MigrationPriority.High)]
    [InlineData(76, MigrationPriority.Critical)]
    [InlineData(100, MigrationPriority.Critical)]
    public void GetPriority_ReturnsCorrectBand(double score, MigrationPriority expected)
    {
        var priority = FileMigrationScore.GetPriority(score);
        Assert.Equal(expected, priority);
    }

    #endregion

    #region File Skipping

    [Theory]
    [InlineData("test.g.cs", true)]
    [InlineData("test.generated.cs", true)]
    [InlineData("Form1.Designer.cs", true)]
    [InlineData("test.cs", false)]
    [InlineData("MyService.cs", false)]
    public async Task AnalyzeFileAsync_SkipsGeneratedFiles(string fileName, bool shouldSkip)
    {
        // Create a temp file
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, fileName);

        try
        {
            await File.WriteAllTextAsync(filePath, "public class Test { }");

            var result = await _analyzer.AnalyzeFileAsync(filePath);

            Assert.Equal(shouldSkip, result.WasSkipped);
            if (shouldSkip)
            {
                Assert.Equal("Generated file", result.SkipReason);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Directory Analysis

    [Fact]
    public async Task AnalyzeDirectoryAsync_AnalyzesAllCSharpFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create test files
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "Service1.cs"),
                "public class Service1 { public void Do() { } }");
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "Service2.cs"),
                "public class Service2 { public void Do() { } }");

            var result = await _analyzer.AnalyzeDirectoryAsync(tempDir);

            Assert.Equal(2, result.TotalFilesAnalyzed);
            Assert.Equal(tempDir, result.RootPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AnalyzeDirectoryAsync_SkipsObjDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var objDir = Path.Combine(tempDir, "obj");
        Directory.CreateDirectory(objDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "Service.cs"),
                "public class Service { }");
            await File.WriteAllTextAsync(
                Path.Combine(objDir, "Generated.cs"),
                "public class Generated { }");

            var result = await _analyzer.AnalyzeDirectoryAsync(tempDir);

            Assert.Equal(1, result.TotalFilesAnalyzed);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AnalyzeDirectoryAsync_ThrowsForNonexistentDirectory()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _analyzer.AnalyzeDirectoryAsync("/nonexistent/path"));
    }

    #endregion

    #region Project Analysis Result

    [Fact]
    public void ProjectAnalysisResult_CalculatesAverageScore()
    {
        var result = new ProjectAnalysisResult
        {
            RootPath = "/test",
            Files = new List<FileMigrationScore>
            {
                new() { FilePath = "a.cs", RelativePath = "a.cs", TotalScore = 30, Priority = MigrationPriority.Medium },
                new() { FilePath = "b.cs", RelativePath = "b.cs", TotalScore = 50, Priority = MigrationPriority.Medium },
                new() { FilePath = "c.cs", RelativePath = "c.cs", TotalScore = 70, Priority = MigrationPriority.High }
            }
        };

        Assert.Equal(50, result.AverageScore);
    }

    [Fact]
    public void ProjectAnalysisResult_HasHighPriorityFiles_WhenHighExists()
    {
        var result = new ProjectAnalysisResult
        {
            RootPath = "/test",
            Files = new List<FileMigrationScore>
            {
                new() { FilePath = "a.cs", RelativePath = "a.cs", TotalScore = 30, Priority = MigrationPriority.Medium },
                new() { FilePath = "b.cs", RelativePath = "b.cs", TotalScore = 60, Priority = MigrationPriority.High }
            }
        };

        Assert.True(result.HasHighPriorityFiles);
    }

    [Fact]
    public void ProjectAnalysisResult_HasHighPriorityFiles_WhenCriticalExists()
    {
        var result = new ProjectAnalysisResult
        {
            RootPath = "/test",
            Files = new List<FileMigrationScore>
            {
                new() { FilePath = "a.cs", RelativePath = "a.cs", TotalScore = 30, Priority = MigrationPriority.Medium },
                new() { FilePath = "b.cs", RelativePath = "b.cs", TotalScore = 80, Priority = MigrationPriority.Critical }
            }
        };

        Assert.True(result.HasHighPriorityFiles);
    }

    [Fact]
    public void ProjectAnalysisResult_NoHighPriorityFiles_WhenAllLowOrMedium()
    {
        var result = new ProjectAnalysisResult
        {
            RootPath = "/test",
            Files = new List<FileMigrationScore>
            {
                new() { FilePath = "a.cs", RelativePath = "a.cs", TotalScore = 10, Priority = MigrationPriority.Low },
                new() { FilePath = "b.cs", RelativePath = "b.cs", TotalScore = 40, Priority = MigrationPriority.Medium }
            }
        };

        Assert.False(result.HasHighPriorityFiles);
    }

    [Fact]
    public void ProjectAnalysisResult_GetTopFiles_ReturnsOrderedByScore()
    {
        var result = new ProjectAnalysisResult
        {
            RootPath = "/test",
            Files = new List<FileMigrationScore>
            {
                new() { FilePath = "low.cs", RelativePath = "low.cs", TotalScore = 10, Priority = MigrationPriority.Low },
                new() { FilePath = "high.cs", RelativePath = "high.cs", TotalScore = 90, Priority = MigrationPriority.Critical },
                new() { FilePath = "med.cs", RelativePath = "med.cs", TotalScore = 50, Priority = MigrationPriority.Medium }
            }
        };

        var top = result.GetTopFiles(2).ToList();

        Assert.Equal(2, top.Count);
        Assert.Equal("high.cs", top[0].RelativePath);
        Assert.Equal("med.cs", top[1].RelativePath);
    }

    #endregion

    #region Dimension Weights

    [Theory]
    [InlineData(ScoreDimension.ContractPotential, 0.20)]
    [InlineData(ScoreDimension.EffectPotential, 0.15)]
    [InlineData(ScoreDimension.NullSafetyPotential, 0.20)]
    [InlineData(ScoreDimension.ErrorHandlingPotential, 0.20)]
    [InlineData(ScoreDimension.PatternMatchPotential, 0.10)]
    [InlineData(ScoreDimension.ApiComplexityPotential, 0.15)]
    public void DimensionScore_GetWeight_ReturnsCorrectWeight(ScoreDimension dimension, double expected)
    {
        Assert.Equal(expected, DimensionScore.GetWeight(dimension));
    }

    [Fact]
    public void DimensionWeights_SumToOne()
    {
        var totalWeight = Enum.GetValues<ScoreDimension>()
            .Sum(d => DimensionScore.GetWeight(d));

        Assert.Equal(1.0, totalWeight, precision: 2);
    }

    #endregion

    #region Unsupported Constructs Detection

    [Fact]
    public void AnalyzeSource_SwitchExpression_NowSupported()
    {
        var source = """
            public class Service
            {
                public string GetDay(int day) => day switch
                {
                    1 => "Monday",
                    2 => "Tuesday",
                    _ => "Unknown"
                };
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        // Switch expressions are now supported - they should NOT appear in unsupported constructs
        Assert.DoesNotContain(result.UnsupportedConstructs, c => c.Name == "SwitchExpression");
        // Should still be detected for pattern matching potential
        Assert.True(result.Dimensions[ScoreDimension.PatternMatchPotential].PatternCount > 0);
    }

    [Fact]
    public void AnalyzeSource_RelationalPattern_DetectedAsUnsupported()
    {
        var source = """
            public class Service
            {
                public bool IsValid(int value)
                {
                    return value is > 0 and < 100;
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "RelationalPattern");
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "CompoundPattern");
    }

    [Fact]
    public void AnalyzeSource_PrimaryConstructor_DetectedAsUnsupported()
    {
        var source = """
            public class Service(string name)
            {
                private readonly string _name = name;
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "PrimaryConstructor");
    }

    [Fact]
    public void AnalyzeSource_LambdaExpression_DetectedAsUnsupported()
    {
        var source = """
            using System;
            public class Service
            {
                public Func<int, int> GetDoubler() => x => x * 2;
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "LambdaExpression");
    }

    [Fact]
    public void AnalyzeSource_ThrowExpression_DetectedAsUnsupported()
    {
        var source = """
            public class Service
            {
                public string Name { get; }
                public Service(string name)
                {
                    Name = name ?? throw new ArgumentNullException(nameof(name));
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "ThrowExpression");
    }

    [Fact]
    public void AnalyzeSource_GenericTypeConstraint_DetectedAsUnsupported()
    {
        var source = """
            public class Repository<T> where T : class, new()
            {
                public T Create() => new T();
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "GenericTypeConstraint");
    }

    [Fact]
    public void AnalyzeSource_DeclarationPattern_DetectedAsUnsupported()
    {
        var source = """
            public class Service
            {
                public void Process(object obj)
                {
                    if (obj is string text)
                    {
                        Console.WriteLine(text);
                    }
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "DeclarationPattern");
    }

    [Fact]
    public void AnalyzeSource_OutParameter_DetectedAsUnsupported()
    {
        var source = """
            public class Parser
            {
                public bool TryParse(string input, out int result)
                {
                    result = 0;
                    return int.TryParse(input, out result);
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "OutRefParameter");
    }

    [Fact]
    public void AnalyzeSource_NestedGenericType_DetectedAsUnsupported()
    {
        var source = """
            using System;
            using System.Linq.Expressions;
            public class Service
            {
                public void Process(Expression<Func<string, bool>> predicate) { }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "NestedGenericType");
    }

    [Fact]
    public void AnalyzeSource_RangeExpression_DetectedAsUnsupported()
    {
        var source = """
            public class Service
            {
                public int[] GetSlice(int[] array)
                {
                    return array[1..5];
                }
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.True(result.HasUnsupportedConstructs);
        Assert.Contains(result.UnsupportedConstructs, c => c.Name == "RangeExpression");
    }

    [Fact]
    public void AnalyzeSource_SimpleCode_NoUnsupportedConstructs()
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

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        Assert.False(result.HasUnsupportedConstructs);
        Assert.Empty(result.UnsupportedConstructs);
    }

    #endregion

    #region Unsupported Constructs Penalty

    [Fact]
    public void AnalyzeSource_UnsupportedConstructs_ReduceScore()
    {
        // Code with potential for high score but has unsupported constructs
        var sourceWithUnsupported = """
            public class Service
            {
                public string? Name { get; set; }

                public void Process(string input)
                {
                    if (input == null)
                        throw new ArgumentNullException(nameof(input));

                    // Unsupported: throw expression
                    var result = input ?? throw new ArgumentNullException(nameof(input));
                }
            }
            """;

        // Similar code without unsupported constructs
        var sourceWithoutUnsupported = """
            public class Service
            {
                public string? Name { get; set; }

                public void Process(string input)
                {
                    if (input == null)
                        throw new ArgumentNullException(nameof(input));

                    // Supported: null coalescing without throw
                    var result = input ?? "default";
                }
            }
            """;

        var resultWithUnsupported = _analyzer.AnalyzeSource(sourceWithUnsupported, "test.cs", "test.cs");
        var resultWithoutUnsupported = _analyzer.AnalyzeSource(sourceWithoutUnsupported, "test.cs", "test.cs");

        // Score should be significantly lower when unsupported constructs are present
        Assert.True(resultWithUnsupported.TotalScore < resultWithoutUnsupported.TotalScore,
            $"Expected unsupported score ({resultWithUnsupported.TotalScore}) < supported score ({resultWithoutUnsupported.TotalScore})");
    }

    [Fact]
    public void AnalyzeSource_MultipleUnsupportedConstructs_CompoundPenalty()
    {
        // Note: switch expression is now supported, so we don't count it as unsupported
        // This test uses: primary constructor, relational pattern, compound pattern, lambda
        var source = """
            public class Service(string name)
            {
                public bool IsInRange(int x)
                {
                    // Unsupported: relational + compound pattern
                    return x is > 0 and < 10;
                }

                public Func<int, int> Doubler => x => x * 2;
            }
            """;

        var result = _analyzer.AnalyzeSource(source, "test.cs", "test.cs");

        // Multiple unsupported constructs should result in very low score
        Assert.True(result.HasUnsupportedConstructs);
        Assert.True(result.UnsupportedConstructs.Count >= 3,
            $"Expected at least 3 unsupported constructs, got {result.UnsupportedConstructs.Count}");
        Assert.True(result.TotalScore < 20,
            $"Expected score < 20 with multiple unsupported constructs, got {result.TotalScore}");
    }

    #endregion
}
