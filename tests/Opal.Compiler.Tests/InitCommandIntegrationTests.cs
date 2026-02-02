using Opal.Compiler.Init;
using Xunit;

namespace Opal.Compiler.Tests;

/// <summary>
/// Integration tests for the opalc init command that test the full workflow
/// including MSBuild-only init and MSBuild + AI agent init.
/// </summary>
public class InitCommandIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public InitCommandIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"opal-init-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private async Task CreateTestCsproj(string name = "TestApp")
    {
        var csprojContent = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{name}.csproj"), csprojContent);
    }

    [Fact]
    public async Task MsBuildOnlyInit_AddsOpalTargets_DoesNotCreateClaudeFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act - Initialize MSBuild targets only (simulating opalc init without --ai)
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        var result = await csprojInitializer.InitializeAsync(csprojPath);

        // Assert - MSBuild targets should be added
        Assert.True(result.IsSuccess);
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileOpalFiles", csprojContent);
        Assert.Contains("OpalOutputDirectory", csprojContent);

        // Assert - Claude files should NOT exist
        Assert.False(Directory.Exists(Path.Combine(_testDirectory, ".claude")));
        Assert.False(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));
    }

    [Fact]
    public async Task ClaudeInit_AddsOpalTargets_AndCreatesClaudeFiles()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act - Initialize with Claude (simulating opalc init --ai claude)
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        var claudeInitializer = new ClaudeInitializer();

        var claudeResult = await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        var csprojResult = await csprojInitializer.InitializeAsync(csprojPath);

        // Assert - MSBuild targets should be added
        Assert.True(csprojResult.IsSuccess);
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileOpalFiles", csprojContent);

        // Assert - Claude files should exist
        Assert.True(claudeResult.Success);
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "opal")));
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, ".claude", "skills", "opal-convert")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".claude", "skills", "opal", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".claude", "skills", "opal-convert", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));
    }

    [Fact]
    public async Task MsBuildInit_ThenClaudeInit_IsAdditive()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");

        // Act - First: Initialize MSBuild only
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);
        await csprojInitializer.InitializeAsync(csprojPath);

        // Verify Claude files don't exist yet
        Assert.False(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));

        // Act - Second: Initialize Claude
        var claudeInitializer = new ClaudeInitializer();
        var claudeResult = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert - Claude files should now exist
        Assert.True(claudeResult.Success);
        Assert.True(File.Exists(Path.Combine(_testDirectory, "CLAUDE.md")));
        Assert.True(File.Exists(Path.Combine(_testDirectory, ".claude", "skills", "opal", "SKILL.md")));

        // Assert - MSBuild targets should still be present (not removed)
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("CompileOpalFiles", csprojContent);
    }

    [Fact]
    public async Task ClaudeInit_WithExistingClaudeMd_AppendsOpalSection()
    {
        // Arrange
        await CreateTestCsproj();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");
        var existingContent = """
            # My Project

            This is my custom documentation.

            ## Build Instructions

            Run `dotnet build` to build.
            """;
        await File.WriteAllTextAsync(claudeMdPath, existingContent);

        // Act
        var claudeInitializer = new ClaudeInitializer();
        var result = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(claudeMdPath, result.UpdatedFiles);

        var content = await File.ReadAllTextAsync(claudeMdPath);

        // Original content should be preserved
        Assert.Contains("# My Project", content);
        Assert.Contains("This is my custom documentation.", content);
        Assert.Contains("## Build Instructions", content);

        // OPAL section should be appended
        Assert.Contains("<!-- BEGIN OPALC SECTION - DO NOT EDIT -->", content);
        Assert.Contains("## OPAL-First Project", content);
        Assert.Contains("MANDATORY Rules for AI Agents", content);
        Assert.Contains("<!-- END OPALC SECTION -->", content);
    }

    [Fact]
    public async Task ClaudeInit_WithExistingOpalSection_ReplacesSection()
    {
        // Arrange
        await CreateTestCsproj();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");
        var existingContent = """
            # My Project

            Custom content before.

            <!-- BEGIN OPALC SECTION - DO NOT EDIT -->
            ## Old OPAL Section
            This is OLD content that should be REPLACED.
            Old version info here.
            <!-- END OPALC SECTION -->

            ## Custom Section After

            More custom content after.
            """;
        await File.WriteAllTextAsync(claudeMdPath, existingContent);

        // Act
        var claudeInitializer = new ClaudeInitializer();
        var result = await claudeInitializer.InitializeAsync(_testDirectory, force: false);

        // Assert
        Assert.True(result.Success);

        var content = await File.ReadAllTextAsync(claudeMdPath);

        // Custom content before and after should be preserved
        Assert.Contains("# My Project", content);
        Assert.Contains("Custom content before.", content);
        Assert.Contains("## Custom Section After", content);
        Assert.Contains("More custom content after.", content);

        // Old OPAL content should be replaced
        Assert.DoesNotContain("Old OPAL Section", content);
        Assert.DoesNotContain("OLD content that should be REPLACED", content);
        Assert.DoesNotContain("Old version info here", content);

        // New OPAL content should be present
        Assert.Contains("## OPAL-First Project", content);
        Assert.Contains("MANDATORY Rules for AI Agents", content);
        Assert.Matches(@"opalc v\d+\.\d+\.\d+", content);
    }

    [Fact]
    public async Task ClaudeInit_MultipleRuns_IsIdempotent()
    {
        // Arrange
        await CreateTestCsproj();
        var claudeMdPath = Path.Combine(_testDirectory, "CLAUDE.md");

        var claudeInitializer = new ClaudeInitializer();

        // Act - Run init 3 times
        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterFirst = await File.ReadAllTextAsync(claudeMdPath);

        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterSecond = await File.ReadAllTextAsync(claudeMdPath);

        await claudeInitializer.InitializeAsync(_testDirectory, force: false);
        var contentAfterThird = await File.ReadAllTextAsync(claudeMdPath);

        // Assert - Content should be identical after each run
        Assert.Equal(contentAfterFirst, contentAfterSecond);
        Assert.Equal(contentAfterSecond, contentAfterThird);

        // Should only have one OPAL section
        var beginCount = CountOccurrences(contentAfterThird, "<!-- BEGIN OPALC SECTION");
        var endCount = CountOccurrences(contentAfterThird, "<!-- END OPALC SECTION -->");
        Assert.Equal(1, beginCount);
        Assert.Equal(1, endCount);
    }

    [Fact]
    public async Task CsprojInit_MultipleRuns_IsIdempotent()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");
        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);

        // Act - Run init 3 times
        await csprojInitializer.InitializeAsync(csprojPath, force: true);
        var contentAfterFirst = await File.ReadAllTextAsync(csprojPath);

        await csprojInitializer.InitializeAsync(csprojPath, force: true);
        var contentAfterSecond = await File.ReadAllTextAsync(csprojPath);

        await csprojInitializer.InitializeAsync(csprojPath, force: true);
        var contentAfterThird = await File.ReadAllTextAsync(csprojPath);

        // Assert - Should only have one set of OPAL targets
        var compileTargetCount = CountOccurrences(contentAfterThird, "Name=\"CompileOpalFiles\"");
        var includeTargetCount = CountOccurrences(contentAfterThird, "Name=\"IncludeOpalGeneratedFiles\"");
        var cleanTargetCount = CountOccurrences(contentAfterThird, "Name=\"CleanOpalFiles\"");

        Assert.Equal(1, compileTargetCount);
        Assert.Equal(1, includeTargetCount);
        Assert.Equal(1, cleanTargetCount);
    }

    [Fact]
    public async Task CsprojInit_CreatesBackupFile()
    {
        // Arrange
        await CreateTestCsproj();
        var csprojPath = Path.Combine(_testDirectory, "TestApp.csproj");
        var originalContent = await File.ReadAllTextAsync(csprojPath);

        var detector = new ProjectDetector();
        var csprojInitializer = new CsprojInitializer(detector);

        // Act
        await csprojInitializer.InitializeAsync(csprojPath);

        // Assert
        var backupPath = csprojPath + ".bak";
        Assert.True(File.Exists(backupPath));
        var backupContent = await File.ReadAllTextAsync(backupPath);
        Assert.Equal(originalContent, backupContent);
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
}
