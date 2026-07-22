using Calor.Compiler.Commands;
using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class HookCommandTests : IDisposable
{
    [Fact]
    public async Task CodexWriteHook_BlocksCSharpAddedByApplyPatch()
    {
        var envelope = """
            {"tool_input":{"command":"*** Begin Patch\n*** Add File: NewThing.cs\n+class NewThing {}\n*** End Patch"}}
            """;

        var result = await HookCommand.RunCodexWriteHookAsync(envelope, "pre");

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task CodexWriteHook_AllowsCalorAddedByApplyPatch()
    {
        var envelope = """
            {"tool_input":{"command":"*** Begin Patch\n*** Add File: NewThing.calr\n+§M{m001:NewThing}\n*** End Patch"}}
            """;

        var result = await HookCommand.RunCodexWriteHookAsync(envelope, "pre");

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task CodexWriteHook_AllowsDeleteOnlyPatch()
    {
        var envelope = """
            {"tool_input":{"command":"*** Begin Patch\n*** Delete File: Old.cs\n*** End Patch"}}
            """;

        Assert.Equal(0, await HookCommand.RunCodexWriteHookAsync(envelope, "pre"));
        Assert.Equal(0, await HookCommand.RunCodexWriteHookAsync(envelope, "post"));
    }

    [Fact]
    public async Task CodexWriteHook_CliAllowsDeleteOnlyPatchFromStdin()
    {
        var compilerDll = typeof(HookCommand).Assembly.Location;
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            Arguments = $"\"{compilerDll}\" hook codex-write --phase pre",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        await process.StandardInput.WriteAsync("{\"tool_input\":{\"command\":\"*** Begin Patch\\n*** Delete File: Old.cs\\n*** End Patch\"}}");
        process.StandardInput.Close();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task CodexWriteHook_ValidatesMoveDestination()
    {
        var envelope = """
            {"tool_input":{"command":"*** Begin Patch\n*** Update File: Service.calr\n*** Move to: Service.cs\n@@\n-old\n+new\n*** End Patch"}}
            """;

        Assert.Equal(2, await HookCommand.RunCodexWriteHookAsync(envelope, "pre"));
    }

    [Fact]
    public async Task CodexWriteHook_AllowsMoveFromCSharpToCalor()
    {
        var envelope = """
            {"tool_input":{"command":"*** Begin Patch\n*** Update File: Service.cs\n*** Move to: Service.calr\n@@\n-old\n+new\n*** End Patch"}}
            """;

        Assert.Equal(0, await HookCommand.RunCodexWriteHookAsync(envelope, "pre"));
    }

    [Fact]
    public async Task CodexWriteHook_ValidatesEveryFileInMultiFilePatch()
    {
        var envelope = """
            {"tool_input":{"command":"*** Begin Patch\n*** Add File: Good.calr\n+ok\n*** Add File: Bad.cs\n+bad\n*** End Patch"}}
            """;

        Assert.Equal(2, await HookCommand.RunCodexWriteHookAsync(envelope, "pre"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"tool_input\":{}}")]
    [InlineData("{\"tool_input\":{\"command\":\"\"}}")]
    public async Task CodexWriteHook_FailsClosedForMalformedEnvelope(string envelope)
    {
        Assert.Equal(2, await HookCommand.RunCodexWriteHookAsync(envelope, "pre"));
    }

    [Fact]
    public async Task CodexWriteHook_CliReadsEnvelopeFromStdin()
    {
        var compilerDll = typeof(HookCommand).Assembly.Location;
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            Arguments = $"\"{compilerDll}\" hook codex-write --phase pre",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        await process.StandardInput.WriteAsync("{\"tool_input\":{\"command\":\"*** Begin Patch\\n*** Add File: FromStdin.cs\\n+x\\n*** End Patch\"}}");
        process.StandardInput.Close();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("BLOCKED", error);
        Assert.Contains("FromStdin.cs", error);
    }

    [Fact]
    public async Task CodexWriteHook_PostPhaseLintsMoveDestination()
    {
        var envelope = """
            {"tool_input":{"command":"*** Begin Patch\n*** Update File: Old.cs\n*** Move to: New.calr\n@@\n-old\n+new\n*** End Patch"}}
            """;
        string? lintInput = null;

        var result = await HookCommand.RunCodexWriteHookAsync(
            envelope,
            "post",
            input =>
            {
                lintInput = input;
                return Task.FromResult(0);
            });

        Assert.Equal(0, result);
        Assert.Contains("New.calr", lintInput);
        Assert.DoesNotContain("Old.cs", lintInput);
    }

    [Fact]
    public async Task PostWriteLint_ReportsLintFailureToCodex()
    {
        var path = Path.Combine(_testDirectory, "Broken.calr");
        await File.WriteAllTextAsync(path, "broken");
        var originalError = Console.Error;
        using var error = new StringWriter();
        Console.SetError(error);
        try
        {
            var input = System.Text.Json.JsonSerializer.Serialize(new { file_path = path });
            var result = await HookCommand.PostWriteLintAsync(
                input,
                _ => Task.FromResult(new HookCommand.LintProcessResult(1, "CALOR0001", "parse failed")));

            Assert.Equal(1, result);
            Assert.Contains("Broken.calr", error.ToString());
            Assert.Contains("CALOR0001", error.ToString());
            Assert.Contains("parse failed", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private readonly string _testDirectory;

    public HookCommandTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-hook-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private ClaudeInitializer CreateClaudeInitializer() =>
        new() { ClaudeJsonPathOverride = Path.Combine(_testDirectory, ".claude.json") };

    [Fact]
    public void ValidateWrite_AllowsCalorFiles()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"MyClass.calr\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsCalorFilesWithPath()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"src/Services/MyService.calr\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsCalorFilesCaseInsensitive()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"Test.Calor\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_BlocksCsFiles()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"MyClass.cs\"}");

        Assert.Equal(1, result);
    }

    [Fact]
    public void ValidateWrite_BlocksCsFilesWithPath()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"src/Services/MyService.cs\"}");

        Assert.Equal(1, result);
    }

    [Fact]
    public void ValidateWrite_BlocksCsFilesCaseInsensitive()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"Test.CS\"}");

        Assert.Equal(1, result);
    }

    [Fact]
    public void ValidateWrite_AllowsGeneratedCsFiles()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"MyClass.g.cs\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsGeneratedCsFilesInObjDirectory()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"obj/Debug/net10.0/calor/Test.g.cs\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsFilesInObjDirectory()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"obj/Debug/net10.0/SomeFile.cs\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsFilesInObjDirectoryWindowsPath()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"obj\\\\Debug\\\\net10.0\\\\SomeFile.cs\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsNonCsFiles()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"README.md\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsJsonFiles()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"config.json\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsCsprojFiles()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"MyProject.csproj\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsOnInvalidJson()
    {
        var result = HookCommand.ValidateWrite("not valid json");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsOnEmptyJson()
    {
        var result = HookCommand.ValidateWrite("{}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsOnMissingFilePath()
    {
        var result = HookCommand.ValidateWrite("{\"content\": \"some content\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_HandlesSnakeCaseFilePath()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"Test.cs\"}");

        Assert.Equal(1, result);
    }

    [Fact]
    public void ValidateWrite_HandlesCamelCaseFilePath()
    {
        var result = HookCommand.ValidateWrite("{\"filePath\": \"Test.cs\"}");

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ClaudeInitializer_ConfiguresHooks()
    {
        var initializer = CreateClaudeInitializer();

        await initializer.InitializeAsync(_testDirectory, force: false);

        var settingsPath = Path.Combine(_testDirectory, ".claude", "settings.json");
        Assert.True(File.Exists(settingsPath));

        var content = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("\"hooks\"", content);
        Assert.Contains("PreToolUse", content);
        Assert.Contains("\"matcher\"", content);
        Assert.Contains("Write", content);
        Assert.Contains("calor hook validate-write", content);
    }

    [Fact]
    public async Task ClaudeInitializer_PreservesExistingSettings()
    {
        // Create an existing settings file with custom content
        var claudeDir = Path.Combine(_testDirectory, ".claude");
        Directory.CreateDirectory(claudeDir);
        var settingsPath = Path.Combine(claudeDir, "settings.json");

        var existingSettings = """
            {
              "some_other_setting": "value"
            }
            """;
        await File.WriteAllTextAsync(settingsPath, existingSettings);

        var initializer = CreateClaudeInitializer();
        await initializer.InitializeAsync(_testDirectory, force: false);

        var content = await File.ReadAllTextAsync(settingsPath);

        // Our hooks should be added
        Assert.Contains("calor hook validate-write", content);
    }

    [Fact]
    public async Task ClaudeInitializer_DoesNotDuplicateHooks()
    {
        var initializer = CreateClaudeInitializer();

        // Run init twice
        await initializer.InitializeAsync(_testDirectory, force: false);
        await initializer.InitializeAsync(_testDirectory, force: false);

        var settingsPath = Path.Combine(_testDirectory, ".claude", "settings.json");
        var content = await File.ReadAllTextAsync(settingsPath);

        // Should only have one instance of our hook command
        var count = content.Split("calor hook validate-write").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ClaudeInitializer_ReportsSettingsFileAsCreated()
    {
        var initializer = CreateClaudeInitializer();

        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        var settingsPath = Path.Combine(_testDirectory, ".claude", "settings.json");
        Assert.Contains(settingsPath, result.CreatedFiles);
    }

    [Fact]
    public async Task ClaudeInitializer_ReportsSettingsFileAsUpdatedWhenModified()
    {
        // Create an existing settings file without hooks
        var claudeDir = Path.Combine(_testDirectory, ".claude");
        Directory.CreateDirectory(claudeDir);
        var settingsPath = Path.Combine(claudeDir, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{}");

        var initializer = CreateClaudeInitializer();
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.Contains(settingsPath, result.UpdatedFiles);
    }

    #region Additional obj Directory Tests

    [Fact]
    public void ValidateWrite_ObjSubdirectory_IsAllowed()
    {
        // Nested subdirectory in obj
        var result = HookCommand.ValidateWrite("{\"file_path\": \"obj/Debug/net10.0/ref/MyAssembly.cs\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_ObjLikeDirectory_IsBlocked()
    {
        // obj_backup is NOT obj, so .cs should be blocked
        var result = HookCommand.ValidateWrite("{\"file_path\": \"obj_backup/MyClass.cs\"}");

        Assert.Equal(1, result);
    }

    [Fact]
    public void ValidateWrite_ObjectDirectory_IsBlocked()
    {
        // "object" is NOT "obj", so .cs should be blocked
        var result = HookCommand.ValidateWrite("{\"file_path\": \"object/MyClass.cs\"}");

        Assert.Equal(1, result);
    }

    [Fact]
    public void ValidateWrite_ObjInMiddleOfPath_IsAllowed()
    {
        // obj in the middle of the path
        var result = HookCommand.ValidateWrite("{\"file_path\": \"src/obj/Debug/MyClass.cs\"}");

        Assert.Equal(0, result);
    }

    #endregion

    #region Extension Case Sensitivity Tests

    [Theory]
    [InlineData("{\"file_path\": \"Test.CS\"}")]
    [InlineData("{\"file_path\": \"Test.Cs\"}")]
    [InlineData("{\"file_path\": \"Test.cS\"}")]
    public void ValidateWrite_CaseInsensitiveExtension_BlocksCsFiles(string json)
    {
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(1, result);
    }

    [Theory]
    [InlineData("{\"file_path\": \"Test.G.CS\"}")]
    [InlineData("{\"file_path\": \"Test.G.Cs\"}")]
    [InlineData("{\"file_path\": \"Test.g.CS\"}")]
    public void ValidateWrite_CaseInsensitiveExtension_AllowsGeneratedFiles(string json)
    {
        var result = HookCommand.ValidateWrite(json);

        Assert.Equal(0, result);
    }

    #endregion

    #region No Extension and Special Paths Tests

    [Fact]
    public void ValidateWrite_NoExtension_IsAllowed()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"Makefile\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_DotOnlyPath_IsAllowed()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \".\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_HiddenFile_IsAllowed()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \".gitignore\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_EndsWithDotCs_NoExtension_IsBlocked()
    {
        // File literally ending in ".cs" should be blocked
        var result = HookCommand.ValidateWrite("{\"file_path\": \"test.cs\"}");

        Assert.Equal(1, result);
    }

    #endregion

    #region ValidateWriteWithReason Tests

    [Fact]
    public void ValidateWriteWithReason_CalrFile_ReturnsAllowWithNoReason()
    {
        var (exitCode, blockReason, suggestedPath) = HookCommand.ValidateWriteWithReason("{\"file_path\": \"MyClass.calr\"}");

        Assert.Equal(0, exitCode);
        Assert.Null(blockReason);
        Assert.Null(suggestedPath);
    }

    [Fact]
    public void ValidateWriteWithReason_CsFile_ReturnsBlockWithReasonAndSuggestion()
    {
        var (exitCode, blockReason, suggestedPath) = HookCommand.ValidateWriteWithReason("{\"file_path\": \"src/MyClass.cs\"}");

        Assert.Equal(1, exitCode);
        Assert.NotNull(blockReason);
        Assert.Contains("BLOCKED", blockReason);
        Assert.Contains("MyClass.cs", blockReason);
        Assert.Equal("src/MyClass.calr", suggestedPath);
    }

    [Fact]
    public void ValidateWriteWithReason_CsFile_SuggestsCalrExtension()
    {
        var (_, _, suggestedPath) = HookCommand.ValidateWriteWithReason("{\"file_path\": \"Services/UserService.cs\"}");

        Assert.Equal("Services/UserService.calr", suggestedPath);
    }

    [Fact]
    public void ValidateWriteWithReason_GeneratedCsFile_ReturnsAllow()
    {
        var (exitCode, blockReason, suggestedPath) = HookCommand.ValidateWriteWithReason("{\"file_path\": \"output/Test.g.cs\"}");

        Assert.Equal(0, exitCode);
        Assert.Null(blockReason);
        Assert.Null(suggestedPath);
    }

    [Fact]
    public void ValidateWriteWithReason_InvalidJson_ReturnsAllow()
    {
        var (exitCode, blockReason, suggestedPath) = HookCommand.ValidateWriteWithReason("not json");

        Assert.Equal(0, exitCode);
        Assert.Null(blockReason);
        Assert.Null(suggestedPath);
    }

    [Fact]
    public void ValidateWriteWithReason_NonCsNonCalrFile_ReturnsAllow()
    {
        var (exitCode, blockReason, suggestedPath) = HookCommand.ValidateWriteWithReason("{\"file_path\": \"README.md\"}");

        Assert.Equal(0, exitCode);
        Assert.Null(blockReason);
        Assert.Null(suggestedPath);
    }

    [Fact]
    public void ValidateWriteWithReason_BlockReason_IncludesSemanticsReminder()
    {
        var (_, blockReason, _) = HookCommand.ValidateWriteWithReason("{\"file_path\": \"Test.cs\"}");

        Assert.NotNull(blockReason);
        Assert.Contains("overflow", blockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("§SEMVER", blockReason);
    }

    [Fact]
    public void ValidateWriteWithReason_CamelCaseInput_BlocksCsFile()
    {
        var (exitCode, blockReason, suggestedPath) = HookCommand.ValidateWriteWithReason("{\"filePath\": \"MyClass.cs\"}");

        Assert.Equal(1, exitCode);
        Assert.NotNull(blockReason);
        Assert.Equal("MyClass.calr", suggestedPath);
    }

    #endregion
}
