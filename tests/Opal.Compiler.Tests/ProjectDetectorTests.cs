using Opal.Compiler.Init;
using Xunit;

namespace Opal.Compiler.Tests;

public class ProjectDetectorTests : IDisposable
{
    private readonly string _testDir;
    private readonly ProjectDetector _detector;

    public ProjectDetectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"opal-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _detector = new ProjectDetector();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void Detect_SingleCsproj_ReturnsProject()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csprojPath, SdkStyleCsproj);

        // Act
        var result = _detector.Detect(_testDir);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(csprojPath, result.ProjectPath);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Detect_NoCsproj_ReturnsError()
    {
        // Act
        var result = _detector.Detect(_testDir);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("No .csproj file found", result.ErrorMessage);
    }

    [Fact]
    public void Detect_MultipleCsproj_ReturnsErrorWithList()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "Project1.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(_testDir, "Project2.csproj"), SdkStyleCsproj);

        // Act
        var result = _detector.Detect(_testDir);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.HasMultipleProjects);
        Assert.NotNull(result.AvailableProjects);
        Assert.Equal(2, result.AvailableProjects.Count);
        Assert.Contains("Multiple .csproj files found", result.ErrorMessage);
    }

    [Fact]
    public void Detect_SpecificProject_ReturnsSpecifiedProject()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "Project1.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(_testDir, "Project2.csproj"), SdkStyleCsproj);

        // Act
        var result = _detector.Detect(_testDir, "Project2.csproj");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_testDir, "Project2.csproj"), result.ProjectPath);
    }

    [Fact]
    public void Detect_SpecificProjectAbsolutePath_ReturnsSpecifiedProject()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csprojPath, SdkStyleCsproj);

        // Act
        var result = _detector.Detect(_testDir, csprojPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(csprojPath, result.ProjectPath);
    }

    [Fact]
    public void Detect_SpecificProjectNotFound_ReturnsError()
    {
        // Act
        var result = _detector.Detect(_testDir, "NonExistent.csproj");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Project file not found", result.ErrorMessage);
    }

    [Fact]
    public void Detect_DirectoryNotFound_ReturnsError()
    {
        // Act
        var result = _detector.Detect("/non/existent/path");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Directory not found", result.ErrorMessage);
    }

    [Fact]
    public void ValidateProject_SdkStyle_ReturnsValid()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csprojPath, SdkStyleCsproj);

        // Act
        var result = _detector.ValidateProject(csprojPath);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateProject_LegacyStyle_ReturnsInvalid()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csprojPath, LegacyStyleCsproj);

        // Act
        var result = _detector.ValidateProject(csprojPath);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Legacy-style .csproj", result.ErrorMessage);
    }

    [Fact]
    public void ValidateProject_FileNotFound_ReturnsInvalid()
    {
        // Act
        var result = _detector.ValidateProject(Path.Combine(_testDir, "NonExistent.csproj"));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Project file not found", result.ErrorMessage);
    }

    [Fact]
    public void ValidateProject_InvalidXml_ReturnsInvalid()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csprojPath, "not valid xml <>");

        // Act
        var result = _detector.ValidateProject(csprojPath);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Failed to parse", result.ErrorMessage);
    }

    [Fact]
    public void ValidateProject_ImportSdkStyle_ReturnsValid()
    {
        // Arrange - SDK-style with Import instead of attribute
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csprojPath, ImportSdkStyleCsproj);

        // Act
        var result = _detector.ValidateProject(csprojPath);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void HasOpalTargets_WithTargets_ReturnsTrue()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csprojPath, CsprojWithOpalTargets);

        // Act
        var result = _detector.HasOpalTargets(csprojPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasOpalTargets_WithoutTargets_ReturnsFalse()
    {
        // Arrange
        var csprojPath = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csprojPath, SdkStyleCsproj);

        // Act
        var result = _detector.HasOpalTargets(csprojPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasOpalTargets_FileNotFound_ReturnsFalse()
    {
        // Act
        var result = _detector.HasOpalTargets(Path.Combine(_testDir, "NonExistent.csproj"));

        // Assert
        Assert.False(result);
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

    private const string ImportSdkStyleCsproj = """
        <Project>
          <Import Sdk="Microsoft.NET.Sdk" Project="Sdk.props" />
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
          <Import Sdk="Microsoft.NET.Sdk" Project="Sdk.targets" />
        </Project>
        """;

    private const string CsprojWithOpalTargets = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
          <Target Name="CompileOpalFiles" BeforeTargets="BeforeCompile">
            <Exec Command="opalc --input %(OpalCompile.FullPath)" />
          </Target>
        </Project>
        """;
}
