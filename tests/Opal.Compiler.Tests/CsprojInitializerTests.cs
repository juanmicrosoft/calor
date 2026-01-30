using Opal.Compiler.Init;
using Xunit;

namespace Opal.Compiler.Tests;

public class CsprojInitializerTests : IDisposable
{
    private readonly string _testDir;
    private readonly CsprojInitializer _initializer;

    public CsprojInitializerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"opal-test-{Guid.NewGuid():N}");
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
        Assert.Contains("CompileOpalFiles", content);
        Assert.Contains("OpalOutputDirectory", content);
        Assert.Contains("$(BaseIntermediateOutputPath)$(Configuration)", content);
        Assert.Contains("opal", content);
        Assert.Contains("IncludeOpalGeneratedFiles", content);
        Assert.Contains("CleanOpalFiles", content);
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
        Assert.Contains("CompileOpalFiles", secondContent);
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
        Assert.DoesNotContain("CompileOpalFiles", backupContent);
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
        Assert.Contains("Inputs=\"@(OpalCompile)\"", content);
        Assert.Contains("OpalCompile-&gt;", content); // -> is encoded as -&gt;
        Assert.Contains("%(RecursiveDir)%(Filename).g.cs", content);
    }

    [Fact]
    public async Task InitializeAsync_ContainsOpalCompileItemGroup()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, SdkStyleCsproj);

        // Act
        await _initializer.InitializeAsync(csprojPath);

        // Assert
        var content = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("<OpalCompile Include=", content);
        Assert.Contains("**\\*.opal", content);
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
        Assert.Contains("&quot;$(OpalCompilerPath)&quot;", content);
        Assert.Contains("&quot;%(OpalCompile.FullPath)&quot;", content);
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
        Assert.Contains("DependsOnTargets=\"CompileOpalFiles\"", content);
    }

    [Fact]
    public void GenerateOpalTargetsXml_ReturnsValidXml()
    {
        // Act
        var xml = CsprojInitializer.GenerateOpalTargetsXml();

        // Assert
        Assert.Contains("CompileOpalFiles", xml);
        Assert.Contains("IncludeOpalGeneratedFiles", xml);
        Assert.Contains("CleanOpalFiles", xml);
        Assert.Contains("$(BaseIntermediateOutputPath)$(Configuration)", xml);
        Assert.Contains("opal", xml);
        Assert.Contains("Inputs=\"@(OpalCompile)\"", xml);
        Assert.Contains("Outputs=\"@(OpalCompile->'$(OpalOutputDirectory)%(RecursiveDir)%(Filename).g.cs')\"", xml);
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
        var compileTargetCount = CountOccurrences(content, "Name=\"CompileOpalFiles\"");
        var includeTargetCount = CountOccurrences(content, "Name=\"IncludeOpalGeneratedFiles\"");
        var cleanTargetCount = CountOccurrences(content, "Name=\"CleanOpalFiles\"");

        Assert.Equal(1, compileTargetCount);
        Assert.Equal(1, includeTargetCount);
        Assert.Equal(1, cleanTargetCount);
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
