using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Ids.Tests;

public class HookIdValidationTests : IDisposable
{
    private readonly string _testDirectory;

    public HookIdValidationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-id-hook-test-{Guid.NewGuid():N}");
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
    public void ValidateIds_AllowsValidContent()
    {
        var content = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:TestModule}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:TestFunc:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;
        var json = CreateToolInput("/src/test.calr", content);

        var (exitCode, reason) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
        Assert.Null(reason);
    }

    [Fact]
    public void ValidateIds_AllowsNonCalorFiles()
    {
        var json = CreateToolInput("/src/test.cs", "public class Test {}");

        var (exitCode, reason) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_AllowsTestIdsInTestPath()
    {
        var content = """
            §M{m001:TestModule}
            §F{f001:TestFunc:pub}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;
        var json = CreateToolInput("/tests/test.calr", content);

        var (exitCode, reason) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_BlocksDuplicateIds()
    {
        var content = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:TestModule}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Func1:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Func2:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;
        var json = CreateToolInput("/src/test.calr", content);

        var (exitCode, reason) = HookCommand.ValidateIds(json);

        Assert.Equal(1, exitCode);
        Assert.Contains("Duplicate ID", reason);
    }

    [Fact]
    public async Task ValidateIds_BlocksIdChurn()
    {
        // Create existing file with an ID
        var filePath = Path.Combine(_testDirectory, "test.calr");
        var existingContent = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:TestModule}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:TestFunc:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;
        await File.WriteAllTextAsync(filePath, existingContent);

        // Try to write content with changed ID
        var newContent = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:TestModule}
            §F{f_01J5X7K9M2NPQRSTABWXYZ9912:TestFunc:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ9912}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;
        var json = CreateToolInput(filePath, newContent);

        var (exitCode, reason) = HookCommand.ValidateIds(json);

        Assert.Equal(1, exitCode);
        Assert.Contains("ID churn", reason);
    }

    [Fact]
    public async Task ValidateIds_AllowsNewFunctions()
    {
        // Create existing file with one function
        var filePath = Path.Combine(_testDirectory, "test.calr");
        var existingContent = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:TestModule}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:TestFunc:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;
        await File.WriteAllTextAsync(filePath, existingContent);

        // Add a new function (this should be allowed)
        var newContent = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:TestModule}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:TestFunc:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §F{f_01J5X7K9M2NPQRSTABWXYZ9912:NewFunc:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ9912}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;
        var json = CreateToolInput(filePath, newContent);

        var (exitCode, reason) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_AllowsEmptyContent()
    {
        var json = CreateToolInput("/src/test.calr", "");

        var (exitCode, reason) = HookCommand.ValidateIds(json);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_AllowsInvalidJson()
    {
        var (exitCode, reason) = HookCommand.ValidateIds("not valid json");

        Assert.Equal(0, exitCode);
    }

    private static string CreateToolInput(string filePath, string content)
    {
        return $"{{\"file_path\": {System.Text.Json.JsonSerializer.Serialize(filePath)}, \"content\": {System.Text.Json.JsonSerializer.Serialize(content)}}}";
    }
}
