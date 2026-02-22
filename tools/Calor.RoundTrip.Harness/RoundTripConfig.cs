namespace Calor.RoundTrip.Harness;

/// <summary>
/// Configuration for a round-trip verification run against a target project.
/// </summary>
public sealed class RoundTripConfig
{
    /// <summary>Display name for reports.</summary>
    public required string ProjectName { get; init; }

    /// <summary>Path to the cloned project root (will NOT be modified).</summary>
    public required string OriginalProjectPath { get; init; }

    /// <summary>
    /// Relative path from project root to the library source directory.
    /// This is what gets converted. The test project is NOT converted.
    /// </summary>
    public required string LibrarySourceRelativePath { get; init; }

    /// <summary>
    /// Relative path to the solution or project file to build/test.
    /// </summary>
    public required string SolutionOrProjectFile { get; init; }

    /// <summary>Working directory for the round-trip. Files will be copied here.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Files/patterns to EXCLUDE from conversion.
    /// </summary>
    public List<string> ExcludePatterns { get; init; } = DefaultExcludePatterns();

    /// <summary>Maximum time for dotnet test before timeout.</summary>
    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>dotnet test additional arguments (e.g., --filter for specific tests).</summary>
    public string? TestFilter { get; init; }

    /// <summary>Target framework to use for dotnet test (e.g., "net10.0").</summary>
    public string? TargetFramework { get; init; }

    /// <summary>Path to dotnet executable.</summary>
    public string DotnetPath { get; init; } = "dotnet";

    /// <summary>Whether to run regression bisect when regressions are found.</summary>
    public bool EnableBisect { get; init; } = false;

    /// <summary>Maximum regressions to trigger bisect.</summary>
    public int BisectMaxRegressions { get; init; } = 50;

    public static List<string> DefaultExcludePatterns() =>
    [
        "**/AssemblyInfo.cs",
        "**/GlobalUsings.cs",
        "**/*.g.cs",
        "**/*.generated.cs",
        "**/*.Designer.cs",
        "**/obj/**",
        "**/bin/**",
    ];
}
