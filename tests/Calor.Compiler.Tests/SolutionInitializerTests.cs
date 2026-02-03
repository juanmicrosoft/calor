using Calor.Compiler.Init;
using Xunit;

namespace Calor.Compiler.Tests;

public class SolutionInitializerTests : IDisposable
{
    private readonly string _testDir;
    private readonly SolutionInitializer _initializer;

    public SolutionInitializerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"calor-sln-init-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _initializer = new SolutionInitializer();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    #region Basic Initialization Tests

    [Fact]
    public async Task InitializeAsync_SolutionWithProjects_InitializesAllProjects()
    {
        // Arrange
        SetupSolutionWithProjects();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.TotalProjects);
        Assert.Equal(2, result.InitializedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);

        // Verify projects have Calor targets
        var project1Path = Path.Combine(_testDir, "src", "Project1", "Project1.csproj");
        var project2Path = Path.Combine(_testDir, "src", "Project2", "Project2.csproj");
        Assert.Contains("CompileCalorFiles", File.ReadAllText(project1Path));
        Assert.Contains("CompileCalorFiles", File.ReadAllText(project2Path));
    }

    [Fact]
    public async Task InitializeAsync_SlnxSolution_InitializesAllProjects()
    {
        // Arrange
        SetupSlnxSolutionWithProjects();
        var slnxPath = Path.Combine(_testDir, "Test.slnx");

        // Act
        var result = await _initializer.InitializeAsync(slnxPath, force: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.TotalProjects);
        Assert.Equal(2, result.InitializedCount);

        // Verify projects have Calor targets
        var project1Path = Path.Combine(_testDir, "src", "Project1", "Project1.csproj");
        Assert.Contains("CompileCalorFiles", File.ReadAllText(project1Path));
    }

    [Fact]
    public async Task InitializeAsync_WithAiInitializer_CreatesAiFilesInSolutionDir()
    {
        // Arrange
        SetupSolutionWithProjects();
        var slnPath = Path.Combine(_testDir, "Test.sln");
        var aiInitializer = new ClaudeInitializer();

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false, aiInitializer);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Claude Code", result.AgentName);

        // AI files should be in solution directory, not project directories
        Assert.True(File.Exists(Path.Combine(_testDir, "CLAUDE.md")));
        Assert.True(Directory.Exists(Path.Combine(_testDir, ".claude", "skills", "calor")));
        Assert.True(File.Exists(Path.Combine(_testDir, ".claude", "settings.json")));

        // AI files should NOT be in project directories
        Assert.False(File.Exists(Path.Combine(_testDir, "src", "Project1", "CLAUDE.md")));
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task InitializeAsync_MissingProjectFile_SkipsWithWarning()
    {
        // Arrange - Create solution but only one project
        SetupSolutionWithMissingProject();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.InitializedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains(result.Warnings, w => w.Contains("not found"));
    }

    [Fact]
    public async Task InitializeAsync_NonSdkProject_SkipsWithWarning()
    {
        // Arrange
        SetupSolutionWithLegacyProject();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.InitializedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains(result.Warnings, w => w.Contains("Non-SDK-style"));
    }

    [Fact]
    public async Task InitializeAsync_SolutionNotFound_ReturnsError()
    {
        // Act
        var result = await _initializer.InitializeAsync(
            Path.Combine(_testDir, "NonExistent.sln"), force: false);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Solution file not found", result.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_AllProjectsFail_ReturnsError()
    {
        // Arrange - Solution with only legacy projects
        SetupSolutionWithOnlyLegacyProjects();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("No projects could be initialized", result.ErrorMessage);
    }

    #endregion

    #region Force Option Tests

    [Fact]
    public async Task InitializeAsync_AlreadyInitialized_SkipsWithoutForce()
    {
        // Arrange
        SetupSolutionWithProjects();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // First initialization
        await _initializer.InitializeAsync(slnPath, force: false);

        // Act - Second initialization without force
        var result = await _initializer.InitializeAsync(slnPath, force: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.InitializedCount);
        Assert.Equal(2, result.AlreadyInitializedCount);
    }

    [Fact]
    public async Task InitializeAsync_AlreadyInitialized_ReinitializesWithForce()
    {
        // Arrange
        SetupSolutionWithProjects();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // First initialization
        await _initializer.InitializeAsync(slnPath, force: false);

        // Act - Second initialization with force
        var result = await _initializer.InitializeAsync(slnPath, force: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.InitializedCount);
        Assert.Equal(0, result.AlreadyInitializedCount);
    }

    #endregion

    #region Output Verification Tests

    [Fact]
    public async Task InitializeAsync_ReturnsCorrectSolutionInfo()
    {
        // Arrange
        SetupSolutionWithProjects();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false);

        // Assert
        Assert.Equal(slnPath, result.SolutionPath);
        Assert.Equal("Test.sln", result.SolutionName);
        Assert.Equal(2, result.TotalProjects);
    }

    [Fact]
    public async Task InitializeAsync_ReturnsProjectResults()
    {
        // Arrange
        SetupSolutionWithProjects();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false);

        // Assert
        Assert.Equal(2, result.ProjectResults.Count);
        Assert.All(result.ProjectResults, pr =>
        {
            Assert.Equal(InitStatus.Initialized, pr.Status);
            Assert.NotNull(pr.ProjectName);
            Assert.NotNull(pr.ProjectPath);
        });
    }

    [Fact]
    public async Task InitializeAsync_ReturnsUpdatedFilesList()
    {
        // Arrange
        SetupSolutionWithProjects();
        var slnPath = Path.Combine(_testDir, "Test.sln");

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false);

        // Assert
        Assert.Equal(2, result.UpdatedFiles.Count);
        Assert.All(result.UpdatedFiles, f => Assert.EndsWith(".csproj", f));
    }

    #endregion

    #region Setup Helpers

    private void SetupSolutionWithProjects()
    {
        // Create solution file
        var slnContent = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Project1", "src\Project1\Project1.csproj", "{12345678-1234-1234-1234-123456789012}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Project2", "src\Project2\Project2.csproj", "{12345678-1234-1234-1234-123456789013}"
            EndProject
            Global
            EndGlobal
            """;
        File.WriteAllText(Path.Combine(_testDir, "Test.sln"), slnContent);

        // Create project directories and files
        var project1Dir = Path.Combine(_testDir, "src", "Project1");
        var project2Dir = Path.Combine(_testDir, "src", "Project2");
        Directory.CreateDirectory(project1Dir);
        Directory.CreateDirectory(project2Dir);

        File.WriteAllText(Path.Combine(project1Dir, "Project1.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(project2Dir, "Project2.csproj"), SdkStyleCsproj);
    }

    private void SetupSlnxSolutionWithProjects()
    {
        // Create solution file
        var slnxContent = """
            <Solution>
              <Project Path="src/Project1/Project1.csproj" />
              <Project Path="src/Project2/Project2.csproj" />
            </Solution>
            """;
        File.WriteAllText(Path.Combine(_testDir, "Test.slnx"), slnxContent);

        // Create project directories and files
        var project1Dir = Path.Combine(_testDir, "src", "Project1");
        var project2Dir = Path.Combine(_testDir, "src", "Project2");
        Directory.CreateDirectory(project1Dir);
        Directory.CreateDirectory(project2Dir);

        File.WriteAllText(Path.Combine(project1Dir, "Project1.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(project2Dir, "Project2.csproj"), SdkStyleCsproj);
    }

    private void SetupSolutionWithMissingProject()
    {
        // Create solution referencing two projects, but only create one
        var slnContent = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Project1", "src\Project1\Project1.csproj", "{12345678-1234-1234-1234-123456789012}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Project2", "src\Project2\Project2.csproj", "{12345678-1234-1234-1234-123456789013}"
            EndProject
            Global
            EndGlobal
            """;
        File.WriteAllText(Path.Combine(_testDir, "Test.sln"), slnContent);

        // Only create Project1
        var project1Dir = Path.Combine(_testDir, "src", "Project1");
        Directory.CreateDirectory(project1Dir);
        File.WriteAllText(Path.Combine(project1Dir, "Project1.csproj"), SdkStyleCsproj);
    }

    private void SetupSolutionWithLegacyProject()
    {
        var slnContent = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ModernProject", "src\ModernProject\ModernProject.csproj", "{12345678-1234-1234-1234-123456789012}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LegacyProject", "src\LegacyProject\LegacyProject.csproj", "{12345678-1234-1234-1234-123456789013}"
            EndProject
            Global
            EndGlobal
            """;
        File.WriteAllText(Path.Combine(_testDir, "Test.sln"), slnContent);

        var modernDir = Path.Combine(_testDir, "src", "ModernProject");
        var legacyDir = Path.Combine(_testDir, "src", "LegacyProject");
        Directory.CreateDirectory(modernDir);
        Directory.CreateDirectory(legacyDir);

        File.WriteAllText(Path.Combine(modernDir, "ModernProject.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(legacyDir, "LegacyProject.csproj"), LegacyStyleCsproj);
    }

    private void SetupSolutionWithOnlyLegacyProjects()
    {
        var slnContent = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LegacyProject1", "src\LegacyProject1\LegacyProject1.csproj", "{12345678-1234-1234-1234-123456789012}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LegacyProject2", "src\LegacyProject2\LegacyProject2.csproj", "{12345678-1234-1234-1234-123456789013}"
            EndProject
            Global
            EndGlobal
            """;
        File.WriteAllText(Path.Combine(_testDir, "Test.sln"), slnContent);

        var legacy1Dir = Path.Combine(_testDir, "src", "LegacyProject1");
        var legacy2Dir = Path.Combine(_testDir, "src", "LegacyProject2");
        Directory.CreateDirectory(legacy1Dir);
        Directory.CreateDirectory(legacy2Dir);

        File.WriteAllText(Path.Combine(legacy1Dir, "LegacyProject1.csproj"), LegacyStyleCsproj);
        File.WriteAllText(Path.Combine(legacy2Dir, "LegacyProject2.csproj"), LegacyStyleCsproj);
    }

    #endregion

    #region Test Data

    private const string SdkStyleCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private const string LegacyStyleCsproj = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <OutputType>Library</OutputType>
            <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
          </PropertyGroup>
        </Project>
        """;

    #endregion
}
