using Calor.Compiler.Init;
using Calor.Compiler.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests;

/// <summary>
/// Integration tests that verify Calor initialization works on real-world GitHub projects.
/// These tests clone actual repositories and verify initialization behavior.
///
/// Note: These tests require network access and may be slow.
/// They are marked with a [Trait] to allow filtering in CI.
/// </summary>
[Trait("Category", "Integration")]
public class SolutionInitializationIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TestRepoManager _repoManager;
    private readonly SolutionInitializer _initializer;

    // Test repositories - easily extensible for future projects
    // Using smaller, well-maintained projects for faster tests
    public static IEnumerable<object[]> TestRepos => new List<object[]>
    {
        // Small well-known project with solution file
        new object[] { new GitHubTestRepo("dotnet", "try-convert", "v0.9.232202", 5) },
    };

    public SolutionInitializationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _repoManager = new TestRepoManager();
        _initializer = new SolutionInitializer();
    }

    public void Dispose()
    {
        _repoManager.Dispose();
    }

    [Theory(Skip = "Integration test - requires network access")]
    [MemberData(nameof(TestRepos))]
    public async Task Init_RealGitHubProject_InitializesProjects(GitHubTestRepo repo)
    {
        // Arrange
        _output.WriteLine($"Testing: {repo.DisplayName}");
        var repoPath = await _repoManager.CloneAsync(repo);
        _output.WriteLine($"Cloned to: {repoPath}");

        // Find solution file
        var solutionDetector = new SolutionDetector();
        var detection = solutionDetector.Detect(repoPath);

        if (!detection.IsSuccess)
        {
            _output.WriteLine($"No solution found, skipping: {detection.ErrorMessage}");
            return;
        }

        _output.WriteLine($"Found solution: {detection.SolutionPath}");

        // Act
        var result = await _initializer.InitializeAsync(detection.SolutionPath!, force: false);

        // Assert
        _output.WriteLine($"Result: Success={result.IsSuccess}, " +
                         $"Total={result.TotalProjects}, " +
                         $"Initialized={result.InitializedCount}, " +
                         $"Skipped={result.SkippedCount}");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.TotalProjects >= 1, $"Expected at least 1 project, found {result.TotalProjects}");
        Assert.True(result.InitializedCount > 0, "Expected at least one project to be initialized");

        // Verify via audit
        var solutionDir = Path.GetDirectoryName(detection.SolutionPath!)!;
        var audit = SolutionInitAudit.Audit(solutionDir);
        _output.WriteLine(audit.ToString());

        Assert.True(audit.InitializedProjects > 0, "Audit found no initialized projects");
    }

    [Theory(Skip = "Integration test - requires network access")]
    [MemberData(nameof(TestRepos))]
    public async Task Init_RealGitHubProject_CreatesAiFilesInSolutionRoot(GitHubTestRepo repo)
    {
        // Arrange
        _output.WriteLine($"Testing: {repo.DisplayName}");
        var repoPath = await _repoManager.CloneAsync(repo);

        var solutionDetector = new SolutionDetector();
        var detection = solutionDetector.Detect(repoPath);

        if (!detection.IsSuccess)
        {
            _output.WriteLine($"No solution found, skipping");
            return;
        }

        var aiInitializer = new ClaudeInitializer();

        // Act
        var result = await _initializer.InitializeAsync(
            detection.SolutionPath!, force: false, aiInitializer);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Claude Code", result.AgentName);

        var solutionDir = Path.GetDirectoryName(detection.SolutionPath!)!;
        var audit = SolutionInitAudit.Audit(solutionDir);
        _output.WriteLine(audit.ToString());

        Assert.True(audit.HasClaudeMd, "CLAUDE.md should exist in solution root");
        Assert.True(audit.HasSkills, "Calor skills should exist");
        Assert.True(audit.HasHooks, "Hooks should be configured");
    }

    [Fact]
    public async Task Init_LocalMockSolution_InitializesAllProjects()
    {
        // This test doesn't require network access - uses local mock
        var testDir = Path.Combine(Path.GetTempPath(), $"calor-int-test-{Guid.NewGuid():N}");

        try
        {
            // Setup mock solution with multiple projects
            Directory.CreateDirectory(testDir);
            SetupMockSolution(testDir, projectCount: 5);

            var slnPath = Path.Combine(testDir, "MockSolution.sln");

            // Act
            var result = await _initializer.InitializeAsync(slnPath, force: false);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(5, result.TotalProjects);
            Assert.Equal(5, result.InitializedCount);

            var audit = SolutionInitAudit.Audit(testDir);
            Assert.Equal(5, audit.TotalProjects);
            Assert.Equal(5, audit.InitializedProjects);
            Assert.Empty(audit.MissingTargets);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Init_LocalMockSolution_WithAi_CreatesFilesCorrectly()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"calor-int-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(testDir);
            SetupMockSolution(testDir, projectCount: 3);

            var slnPath = Path.Combine(testDir, "MockSolution.sln");
            var aiInitializer = new ClaudeInitializer();

            // Act
            var result = await _initializer.InitializeAsync(slnPath, force: false, aiInitializer);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("Claude Code", result.AgentName);

            var audit = SolutionInitAudit.Audit(testDir);
            Assert.True(audit.HasClaudeMd, "CLAUDE.md should exist");
            Assert.True(audit.HasSkills, "Skills should exist");
            Assert.True(audit.HasHooks, "Hooks should be configured");
            Assert.Equal(3, audit.InitializedProjects);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Init_MixedSdkAndLegacyProjects_InitializesOnlySdkProjects()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"calor-int-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(testDir);
            SetupMockSolutionWithMixedProjects(testDir);

            var slnPath = Path.Combine(testDir, "Mixed.sln");

            // Act
            var result = await _initializer.InitializeAsync(slnPath, force: false);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(4, result.TotalProjects); // 2 SDK + 2 legacy
            Assert.Equal(2, result.InitializedCount); // Only SDK projects
            Assert.Equal(2, result.SkippedCount); // Legacy projects skipped

            // Warnings should mention non-SDK projects
            Assert.Contains(result.Warnings, w => w.Contains("Non-SDK-style") || w.Contains("skipped"));
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    #region Mock Solution Setup Helpers

    private void SetupMockSolution(string baseDir, int projectCount)
    {
        var slnBuilder = new System.Text.StringBuilder();
        slnBuilder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");

        for (int i = 1; i <= projectCount; i++)
        {
            var projectName = $"Project{i}";
            var projectDir = Path.Combine(baseDir, "src", projectName);
            Directory.CreateDirectory(projectDir);

            var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
            File.WriteAllText(csprojPath, SdkStyleCsproj);

            var guid = Guid.NewGuid().ToString().ToUpper();
            slnBuilder.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"src\\{projectName}\\{projectName}.csproj\", \"{{{guid}}}\"");
            slnBuilder.AppendLine("EndProject");
        }

        slnBuilder.AppendLine("Global");
        slnBuilder.AppendLine("EndGlobal");

        File.WriteAllText(Path.Combine(baseDir, "MockSolution.sln"), slnBuilder.ToString());
    }

    private void SetupMockSolutionWithMixedProjects(string baseDir)
    {
        var slnBuilder = new System.Text.StringBuilder();
        slnBuilder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");

        // SDK-style projects
        CreateProjectInSolution(baseDir, slnBuilder, "SdkProject1", SdkStyleCsproj);
        CreateProjectInSolution(baseDir, slnBuilder, "SdkProject2", SdkStyleCsproj);

        // Legacy projects
        CreateProjectInSolution(baseDir, slnBuilder, "LegacyProject1", LegacyStyleCsproj);
        CreateProjectInSolution(baseDir, slnBuilder, "LegacyProject2", LegacyStyleCsproj);

        slnBuilder.AppendLine("Global");
        slnBuilder.AppendLine("EndGlobal");

        File.WriteAllText(Path.Combine(baseDir, "Mixed.sln"), slnBuilder.ToString());
    }

    private void CreateProjectInSolution(string baseDir, System.Text.StringBuilder slnBuilder, string projectName, string csprojContent)
    {
        var projectDir = Path.Combine(baseDir, "src", projectName);
        Directory.CreateDirectory(projectDir);

        var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        var guid = Guid.NewGuid().ToString().ToUpper();
        slnBuilder.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"src\\{projectName}\\{projectName}.csproj\", \"{{{guid}}}\"");
        slnBuilder.AppendLine("EndProject");
    }

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
