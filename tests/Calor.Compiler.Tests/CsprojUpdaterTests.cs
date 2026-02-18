using System.Xml.Linq;
using Calor.Compiler.Migration.Project;
using Xunit;

namespace Calor.Compiler.Tests;

public class CsprojUpdaterTests : IDisposable
{
    private readonly string _testDir;
    private readonly CsprojUpdater _updater;

    public CsprojUpdaterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"calor-updater-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _updater = new CsprojUpdater();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateForCalorAsync_WithCalrFiles_ContainsCalorCompilerOverride()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);
        // UpdateForCalorAsync only adds targets when .calr files exist
        await File.WriteAllTextAsync(Path.Combine(_testDir, "test.calr"), "");

        // Act
        var result = await _updater.UpdateForCalorAsync(csprojPath);

        // Assert
        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CalorCompilerOverride", content);
    }

    [Fact]
    public async Task UpdateForCalorAsync_CalorCompilerOverride_HasCorrectConditionsAndOrdering()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "test.calr"), "");

        // Act
        await _updater.UpdateForCalorAsync(csprojPath);

        // Assert
        var content = await File.ReadAllTextAsync(csprojPath);
        var doc = XDocument.Parse(content);

        var compilerPathElements = doc.Descendants("CalorCompilerPath").ToList();
        Assert.Equal(2, compilerPathElements.Count);

        // First: override condition
        var overrideElement = compilerPathElements[0];
        Assert.Equal("$(CalorCompilerOverride)", overrideElement.Value);
        Assert.Equal(
            "'$(CalorCompilerOverride)' != '' and '$(CalorCompilerPath)' == ''",
            overrideElement.Attribute("Condition")?.Value);

        // Second: default fallback
        var defaultElement = compilerPathElements[1];
        Assert.Equal("calor", defaultElement.Value);
        Assert.Equal(
            "'$(CalorCompilerPath)' == ''",
            defaultElement.Attribute("Condition")?.Value);
    }

    [Fact]
    public async Task CreateCalorProjectAsync_ContainsCalorCompilerOverride()
    {
        // Arrange
        var projectDir = Path.Combine(_testDir, "NewProject");

        // Act
        var result = await _updater.CreateCalorProjectAsync(projectDir, "NewProject");

        // Assert
        Assert.True(result.Success);
        var csprojPath = Path.Combine(projectDir, "NewProject.csproj");
        var content = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CalorCompilerOverride", content);
    }

    [Fact]
    public async Task CreateCalorProjectAsync_CalorCompilerOverride_HasCorrectConditionsAndOrdering()
    {
        // Arrange
        var projectDir = Path.Combine(_testDir, "NewProject");

        // Act
        await _updater.CreateCalorProjectAsync(projectDir, "NewProject");

        // Assert
        var csprojPath = Path.Combine(projectDir, "NewProject.csproj");
        var content = await File.ReadAllTextAsync(csprojPath);
        var doc = XDocument.Parse(content);

        var compilerPathElements = doc.Descendants("CalorCompilerPath").ToList();
        Assert.Equal(2, compilerPathElements.Count);

        // First: override condition
        var overrideElement = compilerPathElements[0];
        Assert.Equal("$(CalorCompilerOverride)", overrideElement.Value);
        Assert.Equal(
            "'$(CalorCompilerOverride)' != '' and '$(CalorCompilerPath)' == ''",
            overrideElement.Attribute("Condition")?.Value);

        // Second: default fallback
        var defaultElement = compilerPathElements[1];
        Assert.Equal("calor", defaultElement.Value);
        Assert.Equal(
            "'$(CalorCompilerPath)' == ''",
            defaultElement.Attribute("Condition")?.Value);
    }

    [Fact]
    public async Task UpdateForCalorAsync_ContainsValidateCalorCompilerOverrideTarget()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "test.calr"), "");

        // Act
        await _updater.UpdateForCalorAsync(csprojPath);

        // Assert
        var doc = XDocument.Parse(await File.ReadAllTextAsync(csprojPath));
        var validateTarget = doc.Descendants("Target")
            .FirstOrDefault(t => t.Attribute("Name")?.Value == "ValidateCalorCompilerOverride");

        Assert.NotNull(validateTarget);
        Assert.Equal("CompileCalorFiles", validateTarget.Attribute("BeforeTargets")?.Value);
        Assert.NotNull(validateTarget.Element("Error"));
        Assert.NotNull(validateTarget.Element("Warning"));
    }

    [Fact]
    public async Task CreateCalorProjectAsync_ContainsValidateCalorCompilerOverrideTarget()
    {
        // Arrange
        var projectDir = Path.Combine(_testDir, "NewProject2");

        // Act
        await _updater.CreateCalorProjectAsync(projectDir, "NewProject2");

        // Assert
        var csprojPath = Path.Combine(projectDir, "NewProject2.csproj");
        var doc = XDocument.Parse(await File.ReadAllTextAsync(csprojPath));
        var validateTarget = doc.Descendants("Target")
            .FirstOrDefault(t => t.Attribute("Name")?.Value == "ValidateCalorCompilerOverride");

        Assert.NotNull(validateTarget);
        Assert.Equal("CompileCalorFiles", validateTarget.Attribute("BeforeTargets")?.Value);
        Assert.NotNull(validateTarget.Element("Error"));
        Assert.NotNull(validateTarget.Element("Warning"));
    }

    private const string SdkStyleCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;
}
