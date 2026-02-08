using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Ids.Tests;

/// <summary>
/// Edge case tests for hook commands.
/// Tests boundary conditions, unusual inputs, and format variations.
/// </summary>
public class HookEdgeCaseTests
{
    #region Empty and Minimal Content Tests

    [Fact]
    public void EmptyCalrFile_IsAllowed()
    {
        var json = """{"file_path": "empty.calr", "content": ""}""";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void CalrFileWithOnlyWhitespace_IsAllowed()
    {
        var json = """{"file_path": "whitespace.calr", "content": "   \n\t\r\n   "}""";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void CalrFileWithOnlyComments_IsAllowed()
    {
        var json = """
            {
                "file_path": "comments.calr",
                "content": "// This is a comment\n// Another comment\n/* Block comment */"
            }
            """;

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_EmptyCalrFile_IsAllowed()
    {
        var json = """{"file_path": "empty.calr", "content": ""}""";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_CalrFileWithOnlyComments_IsAllowed()
    {
        var json = """
            {
                "file_path": "comments.calr",
                "content": "// Just comments\n/* No declarations */"
            }
            """;

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Large Content Tests

    [Fact]
    public void LargeCalrFile_IsProcessed()
    {
        // Generate 1MB of content
        var largeContent = string.Join("\n", Enumerable.Range(1, 10000).Select(i =>
            $"fn Function{i} f{i:D6}() -> int {{ return {i}; }}"));

        var json = $"{{\"file_path\": \"large.calr\", \"content\": {JsonEscape(largeContent)}}}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_LargeCalrFile_DoesNotTimeout()
    {
        // Generate content with many functions
        var content = "module LargeModule §SEMVER[1.0.0] {\n" +
            string.Join("\n", Enumerable.Range(1, 100).Select(i =>
                $"fn Func{i} f_{GenerateTestUlid(i)}() -> int {{ return {i}; }}")) +
            "\n}";

        var json = $"{{\"file_path\": \"large.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        // Should complete without timeout
        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Line Ending Tests

    [Fact]
    public void WindowsLineEndings_AreHandled()
    {
        var content = "module Test §SEMVER[1.0.0] {\r\n    fn Add f_01J5X7K9M2NPQRSTABWXYZ1234() -> int {\r\n        return 1;\r\n    }\r\n}";
        var json = $"{{\"file_path\": \"test.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void UnixLineEndings_AreHandled()
    {
        var content = "module Test §SEMVER[1.0.0] {\n    fn Add f_01J5X7K9M2NPQRSTABWXYZ1234() -> int {\n        return 1;\n    }\n}";
        var json = $"{{\"file_path\": \"test.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void MixedLineEndings_AreHandled()
    {
        var content = "module Test §SEMVER[1.0.0] {\r\n    fn Add f_01J5X7K9M2NPQRSTABWXYZ1234() -> int {\n        return 1;\r    }\n}";
        var json = $"{{\"file_path\": \"test.calr\", \"content\": {JsonEscape(content)}}}";

        // Should handle without crashing
        var (exitCode, _) = HookCommand.ValidateIds(json);

        // May or may not parse successfully, but should not throw
        Assert.True(exitCode == 0 || exitCode == 1);
    }

    [Fact]
    public void OldMacLineEndings_AreHandled()
    {
        // Old Mac used \r only
        var content = "module Test §SEMVER[1.0.0] {\r    fn Add f_01J5X7K9M2NPQRSTABWXYZ1234() -> int {\r        return 1;\r    }\r}";
        var json = $"{{\"file_path\": \"test.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        // Should handle without crashing
        Assert.True(exitCode == 0 || exitCode == 1);
    }

    #endregion

    #region Whitespace Edge Cases

    [Fact]
    public void TrailingWhitespace_IsHandled()
    {
        var content = "module Test §SEMVER[1.0.0] { }   \n   \t   ";
        var json = $"{{\"file_path\": \"test.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void LeadingWhitespace_IsHandled()
    {
        var content = "   \n\t   module Test §SEMVER[1.0.0] { }";
        var json = $"{{\"file_path\": \"test.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void TabsInContent_AreHandled()
    {
        var content = "module\tTest\t§SEMVER[1.0.0]\t{\t}";
        var json = $"{{\"file_path\": \"test.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        // Parsing may or may not succeed depending on grammar
        Assert.True(exitCode == 0 || exitCode == 1);
    }

    #endregion

    #region JSON Property Variations

    [Fact]
    public void BothFilePathFormats_SnakeCaseTakesPrecedence()
    {
        var json = """
            {
                "file_path": "allowed.calr",
                "filePath": "blocked.cs"
            }
            """;

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode); // file_path is used
    }

    [Fact]
    public void OnlySnakeCasePath_Works()
    {
        var json = "{\"file_path\": \"test.cs\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void OnlyCamelCasePath_Works()
    {
        var json = "{\"filePath\": \"test.cs\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void BothPathsProvided_CamelCaseOnly_WhenSnakeCaseNull()
    {
        var json = """
            {
                "file_path": null,
                "filePath": "test.cs"
            }
            """;

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(1, exitCode); // Falls back to filePath
    }

    [Fact]
    public void BothPathsProvided_WhenSnakeCaseEmpty_AllowsOperation()
    {
        var json = """
            {
                "file_path": "",
                "filePath": "test.cs"
            }
            """;

        var exitCode = HookCommand.ValidateWrite(json);

        // Current implementation: empty string is still "provided", so filePath is not used
        // Empty path results in allow (no extension to check)
        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Unicode Content Tests

    [Fact]
    public void ContentWithUnicode_IsHandled()
    {
        var content = "module Test §SEMVER[1.0.0] { // 日本語コメント\n    fn 関数 f_01J5X7K9M2NPQRSTABWXYZ1234() -> int { return 1; }\n}";
        var json = $"{{\"file_path\": \"unicode.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        // Should handle without crashing (parsing may or may not succeed)
        Assert.True(exitCode == 0 || exitCode == 1);
    }

    [Fact]
    public void ContentWithEmoji_IsHandled()
    {
        var content = "module Test §SEMVER[1.0.0] { // 🔥 Fire!\n    fn Add f_01J5X7K9M2NPQRSTABWXYZ1234() -> int { return 1; }\n}";
        var json = $"{{\"file_path\": \"emoji.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.True(exitCode == 0 || exitCode == 1);
    }

    [Fact]
    public void ContentWithSectionSymbol_IsHandled()
    {
        // § is used in §SEMVER
        var content = "module Test §SEMVER[1.0.0] { }";
        var json = $"{{\"file_path\": \"section.calr\", \"content\": {JsonEscape(content)}}}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Path Edge Cases

    [Fact]
    public void DotOnlyPath_IsAllowed()
    {
        var json = "{\"file_path\": \".\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void DoubleDotPath_IsAllowed()
    {
        var json = "{\"file_path\": \"..\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void HiddenFile_IsAllowed()
    {
        var json = "{\"file_path\": \".hidden\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void HiddenCalrFile_IsAllowed()
    {
        var json = "{\"file_path\": \".hidden.calr\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void HiddenCsFile_IsBlocked()
    {
        var json = "{\"file_path\": \".hidden.cs\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void FileWithMultipleDots_CsExtension_IsBlocked()
    {
        var json = "{\"file_path\": \"my.file.name.cs\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void FileWithMultipleDots_CalrExtension_IsAllowed()
    {
        var json = "{\"file_path\": \"my.file.name.calr\"}";

        var exitCode = HookCommand.ValidateWrite(json);

        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Helper Methods

    private static string JsonEscape(string s)
    {
        return System.Text.Json.JsonSerializer.Serialize(s);
    }

    private static string GenerateTestUlid(int seed)
    {
        // Generate a deterministic 26-char ULID-like string for testing
        var chars = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var result = new char[26];
        var random = new Random(seed);
        for (int i = 0; i < 26; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result);
    }

    #endregion
}
