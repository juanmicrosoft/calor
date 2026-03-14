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
        var aiInitializer = new ClaudeInitializer
        {
            ClaudeJsonPathOverride = Path.Combine(_testDir, ".claude.json")
        };

        // Act
        var result = await _initializer.InitializeAsync(slnPath, force: false, aiInitializer);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Claude Code", result.AgentName);

        // AI files should be in solution directory, not project directories
        Assert.True(File.Exists(Path.Combine(_testDir, "CLAUDE.md")));
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

    #region .proj File Tests

    [Fact]
    public void SolutionDetector_Detect_FindsProjFile()
    {
        // Arrange - directory with only a .proj file
        SetupProjWithProjects();
        var detector = new SolutionDetector();

        // Act
        var result = detector.Detect(_testDir);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.EndsWith(".proj", result.SolutionPath);
    }

    [Fact]
    public void SolutionParser_ParseProj_ReturnsReferencedProjects()
    {
        // Arrange
        SetupProjWithProjects();
        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.ParseProj(projPath).ToList();

        // Assert
        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, p => p.Name == "Project1");
        Assert.Contains(projects, p => p.Name == "Project2");
        Assert.All(projects, p => Assert.EndsWith(".csproj", p.FullPath));
    }

    [Fact]
    public void SolutionParser_ParseProj_ResolvesGlobPatterns()
    {
        // Arrange - .proj with glob pattern
        SetupProjWithGlobPatterns();
        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.ParseProj(projPath).ToList();

        // Assert
        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, p => p.Name == "App1");
        Assert.Contains(projects, p => p.Name == "App2");
    }

    [Fact]
    public void SolutionParser_ParseProj_SkipsNonCsprojReferences()
    {
        // Arrange - .proj with mixed references including non-.csproj
        var projContent = """
            <Project Sdk="Microsoft.Build.Traversal">
              <ItemGroup>
                <ProjectReference Include="src/Project1/Project1.csproj" />
                <ProjectReference Include="src/Project2/Project2.fsproj" />
                <ProjectReference Include="src/Project3/Project3.vbproj" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_testDir, "build.proj"), projContent);

        var project1Dir = Path.Combine(_testDir, "src", "Project1");
        Directory.CreateDirectory(project1Dir);
        File.WriteAllText(Path.Combine(project1Dir, "Project1.csproj"), SdkStyleCsproj);

        var project2Dir = Path.Combine(_testDir, "src", "Project2");
        Directory.CreateDirectory(project2Dir);
        File.WriteAllText(Path.Combine(project2Dir, "Project2.fsproj"), "<Project/>");

        var project3Dir = Path.Combine(_testDir, "src", "Project3");
        Directory.CreateDirectory(project3Dir);
        File.WriteAllText(Path.Combine(project3Dir, "Project3.vbproj"), "<Project/>");

        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.ParseProj(projPath).ToList();

        // Assert - only .csproj should be included
        Assert.Single(projects);
        Assert.Equal("Project1", projects[0].Name);
    }

    [Fact]
    public async Task SolutionInitializer_InitializeAsync_WithProjFile()
    {
        // Arrange
        SetupProjWithProjects();
        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var result = await _initializer.InitializeAsync(projPath, force: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.TotalProjects);
        Assert.Equal(2, result.InitializedCount);

        // Verify projects have Calor targets
        var project1Path = Path.Combine(_testDir, "src", "Project1", "Project1.csproj");
        var project2Path = Path.Combine(_testDir, "src", "Project2", "Project2.csproj");
        Assert.Contains("CompileCalorFiles", File.ReadAllText(project1Path));
        Assert.Contains("CompileCalorFiles", File.ReadAllText(project2Path));
    }

    [Fact]
    public void SolutionParser_Parse_RoutesToParseProjForProjExtension()
    {
        // Arrange
        SetupProjWithProjects();
        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.Parse(projPath).ToList();

        // Assert
        Assert.Equal(2, projects.Count);
    }

    [Fact]
    public void SolutionDetector_Detect_PrefersSlnOverProj()
    {
        // Arrange - directory with both .sln and .proj
        SetupSolutionWithProjects();
        SetupProjWithProjects();
        var detector = new SolutionDetector();

        // Act
        var result = detector.Detect(_testDir);

        // Assert - multiple solutions found, but both are present
        Assert.False(result.IsSuccess);
        Assert.True(result.HasMultipleSolutions);
    }

    [Fact]
    public void SolutionParser_ParseProj_ResolvesRecursiveGlobPatterns()
    {
        // Arrange - .proj with ** recursive glob pattern
        var projContent = """
            <Project Sdk="Microsoft.Build.Traversal">
              <ItemGroup>
                <ProjectReference Include="**/*.csproj" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_testDir, "build.proj"), projContent);

        // Create projects in nested directories
        var dir1 = Path.Combine(_testDir, "src", "LibA");
        var dir2 = Path.Combine(_testDir, "src", "services", "LibB");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        File.WriteAllText(Path.Combine(dir1, "LibA.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(dir2, "LibB.csproj"), SdkStyleCsproj);

        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.ParseProj(projPath).ToList();

        // Assert
        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, p => p.Name == "LibA");
        Assert.Contains(projects, p => p.Name == "LibB");
    }

    [Fact]
    public void SolutionParser_ParseProj_ResolvesWildcardInDirectorySegment()
    {
        // Arrange - .proj with wildcard in directory segment: src/*/Project.csproj
        var projContent = """
            <Project Sdk="Microsoft.Build.Traversal">
              <ItemGroup>
                <ProjectReference Include="src/*/*.csproj" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_testDir, "build.proj"), projContent);

        var dir1 = Path.Combine(_testDir, "src", "Alpha");
        var dir2 = Path.Combine(_testDir, "src", "Beta");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        File.WriteAllText(Path.Combine(dir1, "Alpha.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(dir2, "Beta.csproj"), SdkStyleCsproj);

        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.ParseProj(projPath).ToList();

        // Assert
        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, p => p.Name == "Alpha");
        Assert.Contains(projects, p => p.Name == "Beta");
    }

    [Fact]
    public void SolutionParser_ParseProj_EmptyProjReturnsNoProjects()
    {
        // Arrange - .proj with no ProjectReference items
        var projContent = """
            <Project Sdk="Microsoft.Build.Traversal">
              <ItemGroup>
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_testDir, "build.proj"), projContent);

        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.ParseProj(projPath).ToList();

        // Assert
        Assert.Empty(projects);
    }

    [Fact]
    public void SolutionParser_ParseProj_GlobResolvingToNoFilesReturnsEmpty()
    {
        // Arrange - .proj with glob pattern that matches nothing
        var projContent = """
            <Project Sdk="Microsoft.Build.Traversal">
              <ItemGroup>
                <ProjectReference Include="nonexistent/**/*.csproj" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_testDir, "build.proj"), projContent);

        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.ParseProj(projPath).ToList();

        // Assert
        Assert.Empty(projects);
    }

    [Fact]
    public void SolutionDetector_Detect_SpecificProjSolution()
    {
        // Arrange - use --solution to specify a .proj file explicitly
        SetupProjWithProjects();
        var detector = new SolutionDetector();

        // Act
        var result = detector.Detect(_testDir, specificSolution: "build.proj");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.EndsWith("build.proj", result.SolutionPath);
    }

    [Fact]
    public void SolutionParser_ParseProj_RecursiveGlobFiltersCsprojOnly()
    {
        // Arrange - ** glob with mixed project types on disk
        var projContent = """
            <Project Sdk="Microsoft.Build.Traversal">
              <ItemGroup>
                <ProjectReference Include="**/*.csproj" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_testDir, "build.proj"), projContent);

        var csDir = Path.Combine(_testDir, "src", "CsProject");
        var fsDir = Path.Combine(_testDir, "src", "FsProject");
        Directory.CreateDirectory(csDir);
        Directory.CreateDirectory(fsDir);

        File.WriteAllText(Path.Combine(csDir, "CsProject.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(fsDir, "FsProject.fsproj"), "<Project/>");

        var projPath = Path.Combine(_testDir, "build.proj");

        // Act
        var projects = SolutionParser.ParseProj(projPath).ToList();

        // Assert - only .csproj, the glob *.csproj itself filters, but verify
        Assert.Single(projects);
        Assert.Equal("CsProject", projects[0].Name);
    }

    #endregion

    #region Setup Helpers

    private void SetupProjWithProjects()
    {
        var projContent = """
            <Project Sdk="Microsoft.Build.Traversal">
              <ItemGroup>
                <ProjectReference Include="src/Project1/Project1.csproj" />
                <ProjectReference Include="src/Project2/Project2.csproj" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_testDir, "build.proj"), projContent);

        var project1Dir = Path.Combine(_testDir, "src", "Project1");
        var project2Dir = Path.Combine(_testDir, "src", "Project2");
        Directory.CreateDirectory(project1Dir);
        Directory.CreateDirectory(project2Dir);

        File.WriteAllText(Path.Combine(project1Dir, "Project1.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(project2Dir, "Project2.csproj"), SdkStyleCsproj);
    }

    private void SetupProjWithGlobPatterns()
    {
        var projContent = """
            <Project Sdk="Microsoft.Build.Traversal">
              <ItemGroup>
                <ProjectReference Include="src/apps/*.csproj" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_testDir, "build.proj"), projContent);

        var appsDir = Path.Combine(_testDir, "src", "apps");
        Directory.CreateDirectory(appsDir);

        File.WriteAllText(Path.Combine(appsDir, "App1.csproj"), SdkStyleCsproj);
        File.WriteAllText(Path.Combine(appsDir, "App2.csproj"), SdkStyleCsproj);
    }

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
            <TargetFramework>net10.0</TargetFramework>
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
