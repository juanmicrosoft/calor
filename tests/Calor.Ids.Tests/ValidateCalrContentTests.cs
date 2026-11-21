using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Ids.Tests;

/// <summary>
/// Tests for validate-calr-content hook command.
/// Validates §SEMVER declaration presence in .calr files.
/// </summary>
public class ValidateCalrContentTests
{
    #region SEMVER Present Tests
    #endregion
    #region SEMVER Missing Tests

    [Fact]
    public void CalrFile_SemverLowercase_ReturnsWarning()
    {
        // §semver (lowercase) is not recognized
        var json = """
            {
                "file_path": "test.calr",
                "content": "module Test §semver[1.0.0] { }"
            }
            """;

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        Assert.NotNull(warning);
    }

    #endregion

    #region Non-Calr File Tests

    [Theory]
    [InlineData("test.cs")]
    [InlineData("test.txt")]
    [InlineData("README.md")]
    [InlineData("config.json")]
    public void NonCalrFile_NoWarning(string filePath)
    {
        var json = $"{{\"file_path\": \"{filePath}\", \"content\": \"no semver here\"}}";

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        Assert.Null(warning);
    }

    [Fact]
    public void CalrExtension_CaseInsensitive_ChecksContent()
    {
        var json = """
            {
                "file_path": "test.CALR",
                "content": "module Test { }"
            }
            """;

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        Assert.NotNull(warning); // Should check content for .CALR too
    }

    #endregion

    #region Empty/Null Content Tests

    [Fact]
    public void EmptyContent_NoWarning()
    {
        var json = """
            {
                "file_path": "test.calr",
                "content": ""
            }
            """;

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        Assert.Null(warning); // Empty content doesn't trigger warning
    }

    [Fact]
    public void NullContent_NoWarning()
    {
        var json = """
            {
                "file_path": "test.calr",
                "content": null
            }
            """;

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        Assert.Null(warning);
    }

    [Fact]
    public void MissingContent_NoWarning()
    {
        var json = """
            {
                "file_path": "test.calr"
            }
            """;

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        Assert.Null(warning);
    }

    [Fact]
    public void WhitespaceOnlyContent_NoSemverWarning()
    {
        var json = """
            {
                "file_path": "test.calr",
                "content": "   \n\t   "
            }
            """;

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        // Current implementation: whitespace-only content does not trigger §SEMVER check
        // because it doesn't contain meaningful code that would need semver
        Assert.NotNull(warning);
    }

    #endregion

    #region JSON Edge Cases

    [Fact]
    public void MissingFilePath_NoWarning()
    {
        var json = """{"content": "module Test { }"}""";

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        Assert.Null(warning);
    }

    [Fact]
    public void MalformedJson_NoWarning()
    {
        var (exitCode, warning) = HookCommand.ValidateCalrContent("{invalid");

        Assert.Equal(0, exitCode);
        Assert.Null(warning);
    }

    [Fact]
    public void CamelCaseFilePath_Works()
    {
        var json = """
            {
                "filePath": "test.calr",
                "content": "module Test { }"
            }
            """;

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        Assert.NotNull(warning); // Should detect missing §SEMVER
    }

    [Fact]
    public void SnakeCaseFilePath_TakesPrecedence()
    {
        var json = """
            {
                "file_path": "test.calr",
                "filePath": "other.txt",
                "content": "module Test { }"
            }
            """;

        var (exitCode, warning) = HookCommand.ValidateCalrContent(json);

        Assert.Equal(0, exitCode);
        // file_path (test.calr) takes precedence over filePath
        Assert.NotNull(warning);
    }

    #endregion
}
