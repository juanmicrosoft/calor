namespace Calor.RoundTrip.Harness;

/// <summary>
/// Pre-configured round-trip configs for known target projects.
/// </summary>
public static class ProjectConfigs
{
    public static RoundTripConfig? Get(string projectName, string projectsDir, string dotnetPath)
    {
        var config = projectName.ToLowerInvariant() switch
        {
            "synthetic" => Synthetic(projectsDir, dotnetPath),
            "mediatr" => MediatR(projectsDir, dotnetPath),
            "serilog" => Serilog(projectsDir, dotnetPath),
            "fluentvalidation" => FluentValidation(projectsDir, dotnetPath),
            _ => null,
        };

        return config;
    }

    public static IReadOnlyList<string> KnownProjects => ["Synthetic", "MediatR", "Serilog", "FluentValidation"];

    private static RoundTripConfig Synthetic(string projectsDir, string dotnetPath)
    {
        // The synthetic project lives inside the Calor repo
        var calorRoot = FindCalorRoot();
        var syntheticRoot = Path.Combine(calorRoot, "tests", "Calor.RoundTrip.Synthetic");
        return new RoundTripConfig
        {
            ProjectName = "Synthetic",
            OriginalProjectPath = syntheticRoot,
            LibrarySourceRelativePath = "SyntheticLib",
            SolutionOrProjectFile = "SyntheticLib.Tests/SyntheticLib.Tests.csproj",
            DotnetPath = dotnetPath,
            TargetFramework = "net8.0",
        };
    }

    private static string FindCalorRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Calor.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: assume we're running from repo root
        return Directory.GetCurrentDirectory();
    }

    private static RoundTripConfig MediatR(string projectsDir, string dotnetPath) => new()
    {
        ProjectName = "MediatR",
        OriginalProjectPath = Path.Combine(projectsDir, "MediatR"),
        LibrarySourceRelativePath = "src/MediatR",
        SolutionOrProjectFile = "MediatR.slnx",
        DotnetPath = dotnetPath,
        TargetFramework = "net10.0",
    };

    private static RoundTripConfig Serilog(string projectsDir, string dotnetPath) => new()
    {
        ProjectName = "Serilog",
        OriginalProjectPath = Path.Combine(projectsDir, "serilog"),
        LibrarySourceRelativePath = "src/Serilog",
        SolutionOrProjectFile = "Serilog.sln",
        DotnetPath = dotnetPath,
        TargetFramework = "net10.0",
    };

    private static RoundTripConfig FluentValidation(string projectsDir, string dotnetPath) => new()
    {
        ProjectName = "FluentValidation",
        OriginalProjectPath = Path.Combine(projectsDir, "FluentValidation"),
        LibrarySourceRelativePath = "src/FluentValidation",
        SolutionOrProjectFile = "src/FluentValidation.Tests/FluentValidation.Tests.csproj",
        DotnetPath = dotnetPath,
        TargetFramework = "net8.0",
    };
}
