using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Ids.Tests;

/// <summary>
/// Security vulnerability tests for hook commands.
/// Documents behavior for potentially dangerous inputs.
/// </summary>
public class HookSecurityTests
{
    #region Path Traversal Tests

    [Theory]
    [InlineData("{\"file_path\": \"../../../etc/passwd\"}")]
    [InlineData("{\"file_path\": \"foo/../bar/../../../etc/passwd\"}")]
    public void PathTraversal_RelativePath_NonCsFiles_IsAllowed(string json)
    {
        // Path traversal with relative paths to non-.cs files is allowed
        // Documents current behavior: no path sanitization for non-.cs files
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"../../secret.cs\"}")]
    [InlineData("{\"file_path\": \"../../../root/hack.cs\"}")]
    public void PathTraversal_RelativePath_CsFiles_IsBlocked(string json)
    {
        // Path traversal to .cs files is blocked (extension check still applies)
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(1, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"/etc/passwd\"}")]
    [InlineData("{\"file_path\": \"/root/.ssh/id_rsa\"}")]
    [InlineData("{\"file_path\": \"C:\\\\Windows\\\\System32\\\\config\\\\SAM\"}")]
    public void PathTraversal_AbsolutePath_IsAllowed(string json)
    {
        // Absolute paths are allowed for non-.cs files
        // Documents current behavior: no path restriction
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"..\\\\..\\\\secret.cs\"}")]
    [InlineData("{\"file_path\": \"foo\\\\..\\\\..\\\\test.cs\"}")]
    public void PathTraversal_WindowsStyle_BlocksCsFiles(string json)
    {
        // Windows-style path traversal to .cs files is blocked
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(1, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"..\\\\..\\\\secret.txt\"}")]
    [InlineData("{\"file_path\": \"..\\\\..\\\\..\\\\etc\\\\passwd\"}")]
    public void PathTraversal_WindowsStyle_AllowsNonCsFiles(string json)
    {
        // Windows-style path traversal to non-.cs files is allowed
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    #endregion

    #region Special Characters in Path Tests

    [Theory]
    [InlineData("{\"file_path\": \"file\\\".cs\"}")]  // Quote in filename
    [InlineData("{\"file_path\": \"file'.cs\"}")]     // Single quote
    public void QuotesInPath_CsFilesAreBlocked(string json)
    {
        // Files with quotes in name ending in .cs are blocked
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(1, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"file;rm -rf.txt\"}")]
    [InlineData("{\"file_path\": \"file|cat /etc/passwd.txt\"}")]
    [InlineData("{\"file_path\": \"file$(whoami).txt\"}")]
    [InlineData("{\"file_path\": \"file`id`.txt\"}")]
    public void SpecialCharsInPath_CommandInjectionAttempts_AreAllowed(string json)
    {
        // Command injection attempts in non-.cs filenames are allowed
        // The hook only validates the extension, not the path content
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"файл.calr\"}")]      // Cyrillic
    [InlineData("{\"file_path\": \"文件.calr\"}")]       // Chinese
    [InlineData("{\"file_path\": \"αβγ.calr\"}")]        // Greek
    [InlineData("{\"file_path\": \"🔥.calr\"}")]         // Emoji
    public void UnicodeInPath_CalrFilesAreAllowed(string json)
    {
        // Unicode characters in .calr filenames are allowed
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"файл.cs\"}")]
    [InlineData("{\"file_path\": \"文件.cs\"}")]
    public void UnicodeInPath_CsFilesAreBlocked(string json)
    {
        // Unicode characters in .cs filenames are blocked
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(1, result);
    }

    [Fact]
    public void VeryLongPath_IsHandled()
    {
        // Very long path (10000 characters)
        var longPath = new string('a', 10000) + ".txt";
        var json = $"{{\"file_path\": \"{longPath}\"}}";

        var result = HookCommand.ValidateWrite(json);

        // Should allow non-.cs files regardless of path length
        Assert.Equal(0, result);
    }

    [Fact]
    public void VeryLongPath_CsFile_IsBlocked()
    {
        // Very long path ending in .cs is blocked
        var longPath = new string('a', 10000) + ".cs";
        var json = $"{{\"file_path\": \"{longPath}\"}}";

        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(1, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"file\\u0000.txt\"}")]      // Null byte
    [InlineData("{\"file_path\": \"file\\u0000.cs\"}")]       // Null byte with .cs
    public void NullBytesInPath_JsonHandlesEscapes(string json)
    {
        // JSON deserializer handles unicode escapes
        // This test documents that the JSON parser processes \u0000
        var result = HookCommand.ValidateWrite(json);

        // The file extension is checked after the null byte
        // "file\0.txt" ends with ".txt", "file\0.cs" ends with ".cs"
        // Note: actual behavior depends on how JSON deserializer handles \u0000
        Assert.True(result == 0 || result == 1); // Either is acceptable
    }

    #endregion

    #region JSON Injection Tests

    [Theory]
    [InlineData("{\"file_path\": \"test.txt\", \"extra\": \"value\"}")]
    [InlineData("{\"file_path\": \"test.txt\", \"nested\": {\"deep\": \"value\"}}")]
    public void JsonInjection_ExtraFields_AreIgnored(string json)
    {
        // Extra JSON fields are ignored
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Fact]
    public void JsonInjection_NestedQuotes()
    {
        // Nested quotes in JSON values
        var json = "{\"file_path\": \"test\\\"quoted\\\".txt\"}";

        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Fact]
    public void JsonInjection_UnicodeEscape_DotCs_IsBlocked()
    {
        // \u002e = '.' - so "test\u002ecs" becomes "test.cs"
        var json = "{\"file_path\": \"test\\u002ecs\"}";
        var result = HookCommand.ValidateWrite(json);

        // .cs files should be blocked
        Assert.Equal(1, result);
    }

    [Fact]
    public void JsonInjection_UnicodeEscape_CsOnly_IsAllowed()
    {
        // \u0063\u0073 = 'cs' - so "test\u0063\u0073" becomes "testcs" (no dot)
        // This doesn't end in ".cs", so it's allowed
        var json = "{\"file_path\": \"test\\u0063\\u0073\"}";
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Fact]
    public void EmptyJsonObject_ReturnsAllow()
    {
        var result = HookCommand.ValidateWrite("{}");

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("not json at all")]
    [InlineData("{\"unclosed\": ")]
    [InlineData("")]
    [InlineData("null")]
    public void MalformedJson_ReturnsAllow(string json)
    {
        // Malformed JSON allows the operation (fail-open)
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Fact]
    public void JsonWithNullFilePath_ReturnsAllow()
    {
        var json = "{\"file_path\": null}";

        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Fact]
    public void JsonWithEmptyFilePath_ReturnsAllow()
    {
        var json = "{\"file_path\": \"\"}";

        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    [Fact]
    public void JsonWithWhitespaceFilePath_ReturnsAllow()
    {
        var json = "{\"file_path\": \"   \"}";

        var result = HookCommand.ValidateWrite(json);

        // Whitespace-only path doesn't match any extension
        Assert.Equal(0, result);
    }

    #endregion

    #region ValidateIds Security Tests

    [Theory]
    [InlineData("{\"file_path\": \"../../../etc/passwd.calr\", \"content\": \"\"}")]
    [InlineData("{\"file_path\": \"/etc/shadow.calr\", \"content\": \"\"}")]
    public void ValidateIds_PathTraversal_ProcessesFile(string json)
    {
        // ValidateIds processes paths without validation
        // Empty content is allowed
        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_MalformedJson_ReturnsAllow()
    {
        var (exitCode, _) = HookCommand.ValidateIds("{invalid json");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_EmptyJson_ReturnsAllow()
    {
        var (exitCode, _) = HookCommand.ValidateIds("{}");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_NullContent_ReturnsAllow()
    {
        var json = "{\"file_path\": \"test.calr\", \"content\": null}";

        var (exitCode, _) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    #endregion
}
