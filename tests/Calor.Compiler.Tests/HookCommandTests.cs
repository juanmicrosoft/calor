using Calor.Compiler.Commands;
using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class HookCommandTests : IDisposable
{
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
        var result = HookCommand.ValidateWrite("{\"file_path\": \"obj/Debug/net8.0/calor/Test.g.cs\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsFilesInObjDirectory()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"obj/Debug/net8.0/SomeFile.cs\"}");

        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateWrite_AllowsFilesInObjDirectoryWindowsPath()
    {
        var result = HookCommand.ValidateWrite("{\"file_path\": \"obj\\\\Debug\\\\net8.0\\\\SomeFile.cs\"}");

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
        var initializer = new ClaudeInitializer();

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

        var initializer = new ClaudeInitializer();
        await initializer.InitializeAsync(_testDirectory, force: false);

        var content = await File.ReadAllTextAsync(settingsPath);

        // Our hooks should be added
        Assert.Contains("calor hook validate-write", content);
    }

    [Fact]
    public async Task ClaudeInitializer_DoesNotDuplicateHooks()
    {
        var initializer = new ClaudeInitializer();

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
        var initializer = new ClaudeInitializer();

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

        var initializer = new ClaudeInitializer();
        var result = await initializer.InitializeAsync(_testDirectory, force: false);

        Assert.Contains(settingsPath, result.UpdatedFiles);
    }
}
