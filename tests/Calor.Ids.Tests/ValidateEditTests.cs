using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Ids.Tests;

/// <summary>
/// Tests for validate-edit hook command.
/// Validates Edit tool calls for Calor-first enforcement.
/// </summary>
public class ValidateEditTests
{
    #region Basic Edit Blocking Tests

    [Theory]
    [InlineData("{\"file_path\": \"MyClass.cs\"}")]
    [InlineData("{\"file_path\": \"src/Services/UserService.cs\"}")]
    [InlineData("{\"file_path\": \"Controllers/HomeController.cs\"}")]
    public void EditCsFile_IsBlocked(string json)
    {
        var (exitCode, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(1, exitCode);
        Assert.NotNull(blockReason);
    }

    [Theory]
    [InlineData("{\"file_path\": \"MyClass.calr\"}")]
    [InlineData("{\"file_path\": \"src/Services/UserService.calr\"}")]
    public void EditCalrFile_IsAllowed(string json)
    {
        var (exitCode, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
        Assert.Null(blockReason);
    }

    [Theory]
    [InlineData("{\"file_path\": \"MyClass.g.cs\"}")]
    [InlineData("{\"file_path\": \"obj/Debug/Generated.g.cs\"}")]
    public void EditGeneratedFile_IsAllowed(string json)
    {
        var (exitCode, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
        Assert.Null(blockReason);
    }

    [Theory]
    [InlineData("{\"file_path\": \"obj/Debug/net8.0/Test.cs\"}")]
    [InlineData("{\"file_path\": \"obj/Release/AssemblyInfo.cs\"}")]
    public void EditObjFile_IsAllowed(string json)
    {
        var (exitCode, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
        Assert.Null(blockReason);
    }

    #endregion

    #region Block Message Content Tests

    [Fact]
    public void BlockReason_ContainsFilePath()
    {
        var json = "{\"file_path\": \"MyController.cs\"}";

        var (_, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.NotNull(blockReason);
        Assert.Contains("MyController.cs", blockReason);
    }

    [Fact]
    public void BlockReason_SuggestsCalrAlternative()
    {
        var json = "{\"file_path\": \"MyClass.cs\"}";

        var (_, blockReason, suggestedPath) = HookCommand.ValidateWriteWithReason(json);

        Assert.NotNull(suggestedPath);
        Assert.Equal("MyClass.calr", suggestedPath);
        Assert.Contains(".calr", blockReason!);
    }

    [Fact]
    public void BlockReason_MentionsSemver()
    {
        var json = "{\"file_path\": \"Test.cs\"}";

        var (_, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.NotNull(blockReason);
        Assert.Contains("§SEMVER", blockReason);
    }

    [Fact]
    public void BlockReason_MentionsOverflowBehavior()
    {
        var json = "{\"file_path\": \"Test.cs\"}";

        var (_, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.NotNull(blockReason);
        Assert.Contains("overflow", blockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlockReason_MentionsCalorSkill()
    {
        var json = "{\"file_path\": \"Test.cs\"}";

        var (_, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.NotNull(blockReason);
        Assert.Contains("/calor", blockReason);
    }

    #endregion

    #region Path Variations

    [Theory]
    [InlineData("{\"file_path\": \"Test.CS\"}")]
    [InlineData("{\"file_path\": \"Test.Cs\"}")]
    [InlineData("{\"file_path\": \"Test.cS\"}")]
    public void CaseInsensitiveExtension_BlocksCsFiles(string json)
    {
        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(1, exitCode);
    }

    [Theory]
    [InlineData("{\"file_path\": \"Test.CALR\"}")]
    [InlineData("{\"file_path\": \"Test.Calr\"}")]
    [InlineData("{\"file_path\": \"Test.CalR\"}")]
    public void CaseInsensitiveExtension_AllowsCalrFiles(string json)
    {
        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void PathWithSpaces_CsFile_IsBlocked()
    {
        var json = "{\"file_path\": \"My Project/My Class.cs\"}";

        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void PathWithSpaces_CalrFile_IsAllowed()
    {
        var json = "{\"file_path\": \"My Project/My Class.calr\"}";

        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
    }

    #endregion

    #region JSON Property Name Variations

    [Fact]
    public void BothFilePathFormats_SnakeCaseTakesPrecedence()
    {
        // When both file_path and filePath are provided, file_path takes precedence
        var json = """
            {
                "file_path": "allowed.calr",
                "filePath": "blocked.cs"
            }
            """;

        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode); // file_path (allowed.calr) is used
    }

    [Fact]
    public void OnlySnakeCasePath_Works()
    {
        var json = "{\"file_path\": \"Test.cs\"}";

        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void OnlyCamelCasePath_Works()
    {
        var json = "{\"filePath\": \"Test.cs\"}";

        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(1, exitCode);
    }

    #endregion

    #region Other File Types

    [Theory]
    [InlineData("{\"file_path\": \"README.md\"}")]
    [InlineData("{\"file_path\": \"package.json\"}")]
    [InlineData("{\"file_path\": \".gitignore\"}")]
    [InlineData("{\"file_path\": \"Dockerfile\"}")]
    [InlineData("{\"file_path\": \"app.config\"}")]
    public void NonCsNonCalrFiles_AreAllowed(string json)
    {
        var (exitCode, blockReason, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
        Assert.Null(blockReason);
    }

    [Theory]
    [InlineData("{\"file_path\": \"script.csx\"}")]  // C# script
    [InlineData("{\"file_path\": \"data.cshtml\"}")]  // Razor
    [InlineData("{\"file_path\": \"test.csproj\"}")]  // Project file
    public void CsLikeExtensions_AreAllowed(string json)
    {
        // Only .cs is blocked, not .csx, .cshtml, .csproj
        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void NoExtensionFile_IsAllowed()
    {
        var json = "{\"file_path\": \"Makefile\"}";

        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void DotOnlyFile_IsAllowed()
    {
        var json = "{\"file_path\": \".\"}";

        var (exitCode, _, _) = HookCommand.ValidateWriteWithReason(json);

        Assert.Equal(0, exitCode);
    }

    #endregion
}
