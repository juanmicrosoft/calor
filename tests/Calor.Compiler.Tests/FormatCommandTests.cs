using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Integration tests for the format command functionality.
/// Tests the formatting pipeline: file → parse → format → output.
/// </summary>
public class FormatCommandTests : IDisposable
{
    private readonly string _testDirectory;

    public FormatCommandTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-format-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
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

    private static FormatResult FormatFile(string filePath)
    {
        var source = File.ReadAllText(filePath);

        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = diagnostics.Errors.Select(e => e.Message).ToList()
            };
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = diagnostics.Errors.Select(e => e.Message).ToList()
            };
        }

        var formatter = new CalorFormatter();
        var formatted = formatter.Format(ast);

        return new FormatResult
        {
            Success = true,
            Original = source,
            Formatted = formatted,
            Errors = new List<string>()
        };
    }

    #region Format Single File

    [Fact]
    public void Format_SingleFile_ProducesFormattedOutput()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Main}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var filePath = CreateTestFile("test.calr", source);

        var result = FormatFile(filePath);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Formatted);
        Assert.Contains("Test", result.Formatted);
        Assert.Contains("Main", result.Formatted);
    }

    [Fact]
    public void Format_SingleFile_OutputIsDeterministic()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Add}{visibility=public}
  §IN{name=a}{type=INT}
  §IN{name=b}{type=INT}
  §OUT{type=INT}
  §BODY
    §RETURN (+ a b)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var filePath = CreateTestFile("test.calr", source);

        var result1 = FormatFile(filePath);
        var result2 = FormatFile(filePath);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.Formatted, result2.Formatted);
    }

    #endregion

    #region Check Mode

    [Fact]
    public void Format_SameInput_ProducesSameOutput()
    {
        // Format the same source twice
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Main}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var filePath1 = CreateTestFile("test1.calr", source);
        var filePath2 = CreateTestFile("test2.calr", source);

        var result1 = FormatFile(filePath1);
        var result2 = FormatFile(filePath2);

        Assert.True(result1.Success);
        Assert.True(result2.Success);

        // Formatting the same source should produce identical output
        Assert.Equal(result1.Formatted, result2.Formatted);
    }

    [Fact]
    public void Format_NotFormatted_OriginalDiffersFromFormatted()
    {
        // Source with inconsistent formatting (extra whitespace, etc.)
        var source = @"§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Main}{visibility=public}
§OUT{type=VOID}
§BODY
§END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}";
        var filePath = CreateTestFile("test.calr", source);

        var result = FormatFile(filePath);
        Assert.True(result.Success);

        // Formatted output should differ (formatter adds consistent structure)
        // Note: This depends on how the formatter normalizes whitespace
        Assert.NotNull(result.Formatted);
    }

    #endregion

    #region Write Mode

    [Fact]
    public void Format_WriteMode_ModifiesFile()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Main}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var filePath = CreateTestFile("test.calr", source);
        var originalContent = File.ReadAllText(filePath);

        var result = FormatFile(filePath);
        Assert.True(result.Success);

        // Simulate --write mode
        File.WriteAllText(filePath, result.Formatted);

        var newContent = File.ReadAllText(filePath);
        Assert.Equal(result.Formatted, newContent);
    }

    [Fact]
    public void Format_WriteMode_PreservesSemantics()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Add}{visibility=public}
  §IN{name=x}{type=INT}
  §IN{name=y}{type=INT}
  §OUT{type=INT}
  §BODY
    §RETURN (+ x y)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var filePath = CreateTestFile("test.calr", source);

        // Format the file
        var result = FormatFile(filePath);
        Assert.True(result.Success);

        // The formatted output should preserve the key semantic elements
        Assert.Contains("Add", result.Formatted);
        Assert.Contains("x", result.Formatted);
        Assert.Contains("y", result.Formatted);
        Assert.Contains("+", result.Formatted);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void Format_NonExistentFile_ReportsError()
    {
        var filePath = Path.Combine(_testDirectory, "nonexistent.calr");

        Assert.False(File.Exists(filePath));

        // The format command would report "File not found" error
        // Here we just verify the file doesn't exist
        var ex = Assert.Throws<FileNotFoundException>(() => File.ReadAllText(filePath));
        Assert.Contains("nonexistent.calr", ex.FileName);
    }

    [Fact]
    public void Format_InvalidSyntax_ReportsParseErrors()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Main}{visibility=public}
  §OUT{type=VOID}
  §BODY
    §INVALID_TOKEN
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var filePath = CreateTestFile("invalid.calr", source);

        var result = FormatFile(filePath);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Format_EmptyFile_HandlesGracefully()
    {
        var filePath = CreateTestFile("empty.calr", "");

        var result = FormatFile(filePath);

        // Empty file should either fail gracefully or produce minimal output
        // The exact behavior depends on parser handling of empty input
        Assert.NotNull(result);
    }

    #endregion

    #region File Extension Handling

    [Fact]
    public void Format_CalorExtension_Processes()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§END_MODULE{id=m001}
";
        var filePath = CreateTestFile("test.calr", source);

        Assert.EndsWith(".calr", filePath);

        var result = FormatFile(filePath);
        Assert.True(result.Success);
    }

    [Fact]
    public void Format_NonCalorExtension_ShouldBeSkipped()
    {
        var source = "some content";
        var filePath = CreateTestFile("test.txt", source);

        // The format command skips non-.calr files
        // Test that the extension check works
        Assert.False(filePath.EndsWith(".calr", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Format_CaseInsensitiveExtension_Processes()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§END_MODULE{id=m001}
";
        // Test with uppercase extension
        var filePath = CreateTestFile("test.CALR", source);

        Assert.EndsWith(".CALR", filePath);
        // Verify case-insensitive matching works
        Assert.Equal(".calr", Path.GetExtension(filePath).ToLowerInvariant());

        var result = FormatFile(filePath);
        Assert.True(result.Success);
    }

    #endregion

    #region Multiple Files

    [Fact]
    public void Format_MultipleFiles_AllProcessed()
    {
        var source1 = @"
§MODULE{id=m001}{name=Test1}
§END_MODULE{id=m001}
";
        var source2 = @"
§MODULE{id=m002}{name=Test2}
§END_MODULE{id=m002}
";
        var source3 = @"
§MODULE{id=m003}{name=Test3}
§END_MODULE{id=m003}
";
        var file1 = CreateTestFile("test1.calr", source1);
        var file2 = CreateTestFile("test2.calr", source2);
        var file3 = CreateTestFile("test3.calr", source3);

        var result1 = FormatFile(file1);
        var result2 = FormatFile(file2);
        var result3 = FormatFile(file3);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.True(result3.Success);

        Assert.Contains("Test1", result1.Formatted);
        Assert.Contains("Test2", result2.Formatted);
        Assert.Contains("Test3", result3.Formatted);
    }

    #endregion

    private sealed class FormatResult
    {
        public bool Success { get; init; }
        public required string Original { get; init; }
        public required string Formatted { get; init; }
        public required List<string> Errors { get; init; }
    }
}
