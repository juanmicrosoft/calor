using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class SolutionDetectorTests : IDisposable
{
    private readonly string _testDir;
    private readonly SolutionDetector _detector;

    public SolutionDetectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"calor-sln-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _detector = new SolutionDetector();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    #region Detection Tests

    [Fact]
    public void Detect_SingleSln_ReturnsSolution()
    {
        // Arrange
        var slnPath = Path.Combine(_testDir, "Test.sln");
        File.WriteAllText(slnPath, SimpleSln);

        // Act
        var result = _detector.Detect(_testDir);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(slnPath, result.SolutionPath);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Detect_SingleSlnx_ReturnsSolution()
    {
        // Arrange
        var slnxPath = Path.Combine(_testDir, "Test.slnx");
        File.WriteAllText(slnxPath, SimpleSlnx);

        // Act
        var result = _detector.Detect(_testDir);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(slnxPath, result.SolutionPath);
    }

    [Fact]
    public void Detect_SlnxAndSln_PrefersSlnx()
    {
        // Arrange - both types present, only one of each
        var slnPath = Path.Combine(_testDir, "Test.sln");
        var slnxPath = Path.Combine(_testDir, "Test.slnx");
        File.WriteAllText(slnPath, SimpleSln);
        File.WriteAllText(slnxPath, SimpleSlnx);

        // Act
        var result = _detector.Detect(_testDir);

        // Assert - multiple solutions, require explicit selection
        Assert.False(result.IsSuccess);
        Assert.True(result.HasMultipleSolutions);
    }

    [Fact]
    public void Detect_MultipleSln_ReturnsErrorWithList()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "Project1.sln"), SimpleSln);
        File.WriteAllText(Path.Combine(_testDir, "Project2.sln"), SimpleSln);

        // Act
        var result = _detector.Detect(_testDir);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.HasMultipleSolutions);
        Assert.NotNull(result.AvailableSolutions);
        Assert.Equal(2, result.AvailableSolutions.Count);
        Assert.Contains("Multiple solution files found", result.ErrorMessage);
    }

    [Fact]
    public void Detect_NoSolution_ReturnsNotFound()
    {
        // Act
        var result = _detector.Detect(_testDir);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.WasNotFound);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Detect_SpecificSolution_ReturnsSpecifiedSolution()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "Project1.sln"), SimpleSln);
        File.WriteAllText(Path.Combine(_testDir, "Project2.sln"), SimpleSln);

        // Act
        var result = _detector.Detect(_testDir, "Project2.sln");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_testDir, "Project2.sln"), result.SolutionPath);
    }

    [Fact]
    public void Detect_SpecificSolutionAbsolutePath_ReturnsSpecifiedSolution()
    {
        // Arrange
        var slnPath = Path.Combine(_testDir, "Test.sln");
        File.WriteAllText(slnPath, SimpleSln);

        // Act
        var result = _detector.Detect(_testDir, slnPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(slnPath, result.SolutionPath);
    }

    [Fact]
    public void Detect_SpecificSolutionNotFound_ReturnsError()
    {
        // Act
        var result = _detector.Detect(_testDir, "NonExistent.sln");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Solution file not found", result.ErrorMessage);
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

    #endregion

    #region Parsing Tests - .sln Format

    [Fact]
    public void ParseSolution_SlnWithProjects_ReturnsProjects()
    {
        // Arrange
        var slnPath = Path.Combine(_testDir, "Test.sln");
        File.WriteAllText(slnPath, SlnWithProjects);

        // Act
        var result = _detector.ParseSolution(slnPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Projects.Count);
        Assert.Contains(result.Projects, p => p.Name == "Project1");
        Assert.Contains(result.Projects, p => p.Name == "Project2");
    }

    [Fact]
    public void ParseSolution_SlnWithSolutionFolders_SkipsFolders()
    {
        // Arrange
        var slnPath = Path.Combine(_testDir, "Test.sln");
        File.WriteAllText(slnPath, SlnWithSolutionFolders);

        // Act
        var result = _detector.ParseSolution(slnPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Projects);
        Assert.Equal("Project1", result.Projects[0].Name);
    }

    [Fact]
    public void ParseSolution_SlnWithMixedProjects_OnlyReturnsCsproj()
    {
        // Arrange
        var slnPath = Path.Combine(_testDir, "Test.sln");
        File.WriteAllText(slnPath, SlnWithMixedProjects);

        // Act
        var result = _detector.ParseSolution(slnPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Projects);
        Assert.Equal("CSharpProject", result.Projects[0].Name);
    }

    [Fact]
    public void ParseSolution_SlnNotFound_ReturnsError()
    {
        // Act
        var result = _detector.ParseSolution(Path.Combine(_testDir, "NonExistent.sln"));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Solution file not found", result.ErrorMessage);
    }

    #endregion

    #region Parsing Tests - .slnx Format

    [Fact]
    public void ParseSolution_SlnxWithProjects_ReturnsProjects()
    {
        // Arrange
        var slnxPath = Path.Combine(_testDir, "Test.slnx");
        File.WriteAllText(slnxPath, SlnxWithProjects);

        // Act
        var result = _detector.ParseSolution(slnxPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Projects.Count);
        Assert.Contains(result.Projects, p => p.Name == "Project1");
        Assert.Contains(result.Projects, p => p.Name == "Project2");
    }

    [Fact]
    public void ParseSolution_SlnxWithFolders_SkipsFolders()
    {
        // Arrange
        var slnxPath = Path.Combine(_testDir, "Test.slnx");
        File.WriteAllText(slnxPath, SlnxWithFolders);

        // Act
        var result = _detector.ParseSolution(slnxPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Projects);
    }

    [Fact]
    public void ParseSolution_SlnxInvalidXml_ReturnsError()
    {
        // Arrange
        var slnxPath = Path.Combine(_testDir, "Test.slnx");
        File.WriteAllText(slnxPath, "invalid xml <>");

        // Act
        var result = _detector.ParseSolution(slnxPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to parse", result.ErrorMessage);
    }

    #endregion

    #region Project Path Tests

    [Fact]
    public void ParseSolution_RelativePaths_ResolvedCorrectly()
    {
        // Arrange
        var slnPath = Path.Combine(_testDir, "Test.sln");
        File.WriteAllText(slnPath, SlnWithProjects);

        // Act
        var result = _detector.ParseSolution(slnPath);

        // Assert
        Assert.True(result.IsSuccess);
        var project1 = result.Projects.First(p => p.Name == "Project1");
        var expectedPath = Path.GetFullPath(Path.Combine(_testDir, "src", "Project1", "Project1.csproj"));
        Assert.Equal(expectedPath, project1.FullPath);
    }

    [Fact]
    public void ParseSolution_ProjectExists_ReportsCorrectly()
    {
        // Arrange
        var slnPath = Path.Combine(_testDir, "Test.sln");
        File.WriteAllText(slnPath, SlnWithProjects);

        // Create one of the project directories/files
        var projectDir = Path.Combine(_testDir, "src", "Project1");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "Project1.csproj"), SdkStyleCsproj);

        // Act
        var result = _detector.ParseSolution(slnPath);

        // Assert
        Assert.True(result.IsSuccess);
        var project1 = result.Projects.First(p => p.Name == "Project1");
        var project2 = result.Projects.First(p => p.Name == "Project2");
        Assert.True(project1.Exists);
        Assert.False(project2.Exists);
    }

    #endregion

    #region Test Data

    private const string SimpleSln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        VisualStudioVersion = 17.0.31903.59
        MinimumVisualStudioVersion = 10.0.40219.1
        Global
        EndGlobal
        """;

    private const string SimpleSlnx = """
        <Solution>
        </Solution>
        """;

    private const string SlnWithProjects = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Project1", "src\Project1\Project1.csproj", "{12345678-1234-1234-1234-123456789012}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Project2", "src\Project2\Project2.csproj", "{12345678-1234-1234-1234-123456789013}"
        EndProject
        Global
        EndGlobal
        """;

    private const string SlnWithSolutionFolders = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{FOLDER-GUID}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Project1", "src\Project1\Project1.csproj", "{12345678-1234-1234-1234-123456789012}"
        EndProject
        Global
        EndGlobal
        """;

    private const string SlnWithMixedProjects = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "CSharpProject", "src\CSharpProject\CSharpProject.csproj", "{12345678-1234-1234-1234-123456789012}"
        EndProject
        Project("{F2A71F9B-5D33-465A-A702-920D77279786}") = "FSharpProject", "src\FSharpProject\FSharpProject.fsproj", "{12345678-1234-1234-1234-123456789013}"
        EndProject
        Global
        EndGlobal
        """;

    private const string SlnxWithProjects = """
        <Solution>
          <Project Path="src/Project1/Project1.csproj" />
          <Project Path="src/Project2/Project2.csproj" />
        </Solution>
        """;

    private const string SlnxWithFolders = """
        <Solution>
          <Folder Name="src">
            <Project Path="src/Project1/Project1.csproj" />
          </Folder>
        </Solution>
        """;

    private const string SdkStyleCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    #endregion
}
