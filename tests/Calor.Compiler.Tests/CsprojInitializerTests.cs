using System.Xml.Linq;
using Calor.Compiler.Init;
using Xunit;
using System.Text.RegularExpressions;

namespace Calor.Compiler.Tests;

public class CsprojInitializerTests : IDisposable
{
    private readonly string _testDir;
    private readonly CsprojInitializer _initializer;

    public CsprojInitializerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"calor-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _initializer = new CsprojInitializer();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_ValidProject_InjectsTargets()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        var result = await _initializer.InitializeAsync(csprojPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.WasAlreadyInitialized);
        Assert.Equal(csprojPath, result.ProjectPath);

        var content = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileCalorFiles", content);
        Assert.Contains("CalorOutputDirectory", content);
        Assert.Contains("$(BaseIntermediateOutputPath)$(Configuration)", content);
        Assert.Contains("calor", content);
        Assert.Contains("IncludeCalorGeneratedFiles", content);
        Assert.Contains("CleanCalorFiles", content);
    }

    [Fact]
    public async Task InitializeAsync_AlreadyInitialized_ReturnsAlreadyInitialized()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // First initialization
        await _initializer.InitializeAsync(csprojPath);

        // Act - Second initialization without force
        var result = await _initializer.InitializeAsync(csprojPath, force: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.WasAlreadyInitialized);
        Assert.Contains("already present", result.Changes[0]);
    }

    [Fact]
    public async Task InitializeAsync_WithForce_ReplacesExistingTargets()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // First initialization
        await _initializer.InitializeAsync(csprojPath);
        var firstContent = await File.ReadAllTextAsync(csprojPath);

        // Act - Second initialization with force
        var result = await _initializer.InitializeAsync(csprojPath, force: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.WasAlreadyInitialized);

        var secondContent = await File.ReadAllTextAsync(csprojPath);
        // Should still have the targets but rewritten
        Assert.Contains("CompileCalorFiles", secondContent);
    }

    [Fact]
    public async Task InitializeAsync_FileNotFound_ReturnsError()
    {
        // Act
        var result = await _initializer.InitializeAsync(Path.Combine(_testDir, "NonExistent.csproj"));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Project file not found", result.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_LegacyProject_ReturnsError()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, LegacyStyleCsproj);

        // Act
        var result = await _initializer.InitializeAsync(csprojPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Legacy-style .csproj", result.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_CreatesBackupFile()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert
        var backupPath = csprojPath + ".bak";
        Assert.True(File.Exists(backupPath));
        var backupContent = await File.ReadAllTextAsync(backupPath);
        Assert.DoesNotContain("CompileCalorFiles", backupContent);
    }

    [Fact]
    public async Task InitializeAsync_ContainsIncrementalBuildAttributes()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert
        var content = await File.ReadAllTextAsync(csprojPath);
        // Check for proper incremental build support (XML encodes quotes as &gt; for > in attribute values)
        Assert.Contains("Inputs=\"@(CalorCompile)\"", content);
        Assert.Contains("CalorCompile-&gt;", content); // -> is encoded as -&gt;
        Assert.Contains("%(RecursiveDir)%(Filename).g.cs", content);
    }

    [Fact]
    public async Task InitializeAsync_ContainsCalorCompileItemGroup()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert
        var content = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("<CalorCompile Include=", content);
        Assert.Contains("**\\*.calr", content);
    }

    [Fact]
    public async Task InitializeAsync_ContainsPathQuoting()
    {
        // Arrange - Verifies paths are quoted for spaces
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert
        var content = await File.ReadAllTextAsync(csprojPath);
        // Check that the command uses quoted paths (XML encodes quotes as &quot;)
        Assert.Contains("&quot;$(CalorCompilerPath)&quot;", content);
        Assert.Contains("&quot;%(CalorCompile.FullPath)&quot;", content);
    }

    [Fact]
    public async Task InitializeAsync_ContainsDependsOnTargets()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert
        var content = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("DependsOnTargets=\"CompileCalorFiles\"", content);
    }

    [Fact]
    public async Task InitializeAsync_ContainsCalorCompilerOverride()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert
        var content = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CalorCompilerOverride", content);
    }

    [Fact]
    public async Task InitializeAsync_ContainsValidateCalorCompilerOverrideTarget()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert - parse XML and verify the validation target
        var content = await File.ReadAllTextAsync(csprojPath);
        var doc = XDocument.Parse(content);

        var validateTarget = doc.Descendants("Target")
            .FirstOrDefault(t => t.Attribute("Name")?.Value == "ValidateCalorCompilerOverride");

        Assert.NotNull(validateTarget);
        Assert.Equal("CompileCalorFiles", validateTarget.Attribute("BeforeTargets")?.Value);
        Assert.Equal("'$(CalorCompilerOverride)' != '' and '@(CalorCompile)' != ''",
            validateTarget.Attribute("Condition")?.Value);

        // Should have Error and Warning child elements
        var errorElement = validateTarget.Element("Error");
        Assert.NotNull(errorElement);
        Assert.Equal("!Exists('$(CalorCompilerOverride)')", errorElement.Attribute("Condition")?.Value);

        var warningElement = validateTarget.Element("Warning");
        Assert.NotNull(warningElement);
    }

    [Fact]
    public async Task InitializeAsync_CalorCompilerOverride_HasCorrectConditionsAndOrdering()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert - parse XML and verify CalorCompilerPath elements
        var content = await File.ReadAllTextAsync(csprojPath);
        var doc = XDocument.Parse(content);

        var compilerPathElements = doc.Descendants("CalorCompilerPath").ToList();
        Assert.Equal(2, compilerPathElements.Count);

        // First element: override condition
        var overrideElement = compilerPathElements[0];
        Assert.Equal("$(CalorCompilerOverride)", overrideElement.Value);
        Assert.Equal(
            "'$(CalorCompilerOverride)' != '' and '$(CalorCompilerPath)' == ''",
            overrideElement.Attribute("Condition")?.Value);

        // Second element: default fallback
        var defaultElement = compilerPathElements[1];
        Assert.Equal("calor", defaultElement.Value);
        Assert.Equal(
            "'$(CalorCompilerPath)' == ''",
            defaultElement.Attribute("Condition")?.Value);
    }

    [Fact]
    public void GenerateCalorTargetsXml_CalorCompilerOverride_HasCorrectConditionsAndOrdering()
    {
        // Act
        var xml = CsprojInitializer.GenerateCalorTargetsXml();

        // Assert - verify the override line appears before the default line
        var overrideIndex = xml.IndexOf("'$(CalorCompilerOverride)' != '' and '$(CalorCompilerPath)' == ''", StringComparison.Ordinal);
        var defaultIndex = xml.IndexOf("<CalorCompilerPath Condition=\"'$(CalorCompilerPath)' == ''\">calor</CalorCompilerPath>", StringComparison.Ordinal);

        Assert.True(overrideIndex >= 0, "Override condition not found in template");
        Assert.True(defaultIndex >= 0, "Default condition not found in template");
        Assert.True(overrideIndex < defaultIndex, "Override must appear before default in template");
    }

    [Fact]
    public void GenerateCalorTargetsXml_ReturnsValidXml()
    {
        // Act
        var xml = CsprojInitializer.GenerateCalorTargetsXml();

        // Assert
        Assert.Contains("ValidateCalorCompilerOverride", xml);
        Assert.Contains("CompileCalorFiles", xml);
        Assert.Contains("IncludeCalorGeneratedFiles", xml);
        Assert.Contains("CleanCalorFiles", xml);
        Assert.Contains("$(BaseIntermediateOutputPath)$(Configuration)", xml);
        Assert.Contains("calor", xml);
        Assert.Contains("Inputs=\"@(CalorCompile)\"", xml);
        Assert.Contains("Outputs=\"@(CalorCompile->'$(CalorOutputDirectory)%(RecursiveDir)%(Filename).g.cs')\"", xml);
        Assert.Contains("CalorCompilerOverride", xml);
    }

    [Fact]
    public async Task InitializeAsync_Idempotent_DoesNotDuplicateTargets()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Initialize twice with force
        await _initializer.InitializeAsync(csprojPath, force: true);
        await _initializer.InitializeAsync(csprojPath, force: true);

        // Assert - should only have one of each target
        var content = await File.ReadAllTextAsync(csprojPath);
        var validateTargetCount = CountOccurrences(content, "Name=\"ValidateCalorCompilerOverride\"");
        var compileTargetCount = CountOccurrences(content, "Name=\"CompileCalorFiles\"");
        var includeTargetCount = CountOccurrences(content, "Name=\"IncludeCalorGeneratedFiles\"");
        var cleanTargetCount = CountOccurrences(content, "Name=\"CleanCalorFiles\"");

        Assert.Equal(1, validateTargetCount);
        Assert.Equal(1, compileTargetCount);
        Assert.Equal(1, includeTargetCount);
        Assert.Equal(1, cleanTargetCount);
    }

    [Fact]
    public async Task InitializeAsync_WithForce_CleansUpManualCalorCompilerOverridePropertyGroup()
    {
        // Arrange - a csproj with a manually-added CalorCompilerOverride in a separate PropertyGroup
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // First initialization
        await _initializer.InitializeAsync(csprojPath);

        // Manually inject a separate PropertyGroup with CalorCompilerOverride
        var content = await File.ReadAllTextAsync(csprojPath);
        var doc = XDocument.Parse(content);
        doc.Root!.Add(new XElement("PropertyGroup",
            new XElement("CalorCompilerOverride", "/some/local/path/to/calor")));
        await File.WriteAllTextAsync(csprojPath, doc.ToString());

        // Verify the manual override element is present before force
        var beforeDoc = XDocument.Parse(await File.ReadAllTextAsync(csprojPath));
        Assert.Single(beforeDoc.Descendants("CalorCompilerOverride"));

        // Act - re-initialize with force
        var result = await _initializer.InitializeAsync(csprojPath, force: true);

        // Assert - manual override PropertyGroup should be removed, only the standard one remains
        Assert.True(result.IsSuccess);
        var afterDoc = XDocument.Parse(await File.ReadAllTextAsync(csprojPath));

        // Should have no standalone CalorCompilerOverride element (only the condition reference in CalorCompilerPath)
        var overrideElements = afterDoc.Descendants("CalorCompilerOverride").ToList();
        Assert.Empty(overrideElements);

        // CalorCompilerPath should still exist with the standard override condition
        var compilerPathElements = afterDoc.Descendants("CalorCompilerPath").ToList();
        Assert.Equal(2, compilerPathElements.Count);
    }

    [Fact]
    public async Task InitializeAsync_WithForce_CleansUpSeparateCalorCompilerPathPropertyGroup()
    {
        // Arrange - a csproj with CalorCompilerPath manually added in a separate PropertyGroup
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // First initialization
        await _initializer.InitializeAsync(csprojPath);

        // Manually inject a separate PropertyGroup with CalorCompilerPath
        var content = await File.ReadAllTextAsync(csprojPath);
        var doc = XDocument.Parse(content);
        doc.Root!.Add(new XElement("PropertyGroup",
            new XElement("CalorCompilerPath", "/custom/path/to/calor")));
        await File.WriteAllTextAsync(csprojPath, doc.ToString());

        // Act - re-initialize with force
        var result = await _initializer.InitializeAsync(csprojPath, force: true);

        // Assert - should have exactly 2 CalorCompilerPath elements (override + default), not 3
        Assert.True(result.IsSuccess);
        var afterContent = await File.ReadAllTextAsync(csprojPath);
        var afterDoc = XDocument.Parse(afterContent);
        var compilerPathElements = afterDoc.Descendants("CalorCompilerPath").ToList();
        Assert.Equal(2, compilerPathElements.Count);
    }

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    private const string SdkStyleCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    [Fact]
    public async Task InitializeAsync_UsesActualAssemblyVersion()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);
        var expectedVersion = EmbeddedResourceHelper.GetVersion();

        // Act
        var result = await _initializer.InitializeAsync(csprojPath);

        // Assert
        Assert.True(result.IsSuccess);
        var content = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains(expectedVersion, content);
        Assert.DoesNotContain("0.1.6", content);
    }

    [Fact]
    public void GenerateCalorTargetsXml_UsesActualAssemblyVersion()
    {
        var xml = CsprojInitializer.GenerateCalorTargetsXml();
        var expectedVersion = EmbeddedResourceHelper.GetVersion();

        Assert.Contains(expectedVersion, xml);
        Assert.DoesNotContain("0.1.6", xml);
    }

    private const string LegacyStyleCsproj = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
          </PropertyGroup>
        </Project>
        """;
}
