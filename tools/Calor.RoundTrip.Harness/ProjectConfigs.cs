namespace Calor.RoundTrip.Harness;

/// <summary>
/// Pre-configured round-trip configs for known target projects.
///
/// Corpus (D-W4.2): the three OSS projects are vendored as SHA-pinned git
/// submodules under <c>bench/corpus/</c> (see <c>bench/corpus/README.md</c> for the
/// pinned commit + release tag + license per project). The former un-pinned
/// external default (<c>~/sources/repos/experimental/github-top10</c>) is gone — an
/// un-pinned corpus is an unreproducible epoch. <c>--projects-dir</c> still overrides
/// the default corpus location.
///
/// Each project's <see cref="RoundTripConfig.TargetFramework"/> is set to a TFM the
/// project declares at its pinned tag AND that the pinned .NET 10 SDK
/// (<c>global.json</c>) can build/run with the runtimes on hand — no source
/// retargeting, so the vendored subjects stay verbatim. The corpus's own
/// <c>global.json</c> SDK pins are neutralized in the working copy so every subject
/// builds on Calor's pinned SDK (see <c>RoundTripPipeline</c>).
/// </summary>
public static class ProjectConfigs
{
    public static RoundTripConfig? Get(string projectName, string projectsDir, string dotnetPath)
    {
        var config = projectName.ToLowerInvariant() switch
        {
            "synthetic" => Synthetic(projectsDir, dotnetPath),
            "synthetic2" => Synthetic2(projectsDir, dotnetPath),
            "mediatr" => MediatR(projectsDir, dotnetPath),
            "serilog" => Serilog(projectsDir, dotnetPath),
            "fluentvalidation" => FluentValidation(projectsDir, dotnetPath),
            _ => null,
        };

        return config;
    }

    public static IReadOnlyList<string> KnownProjects => ["Synthetic", "Synthetic2", "MediatR", "Serilog", "FluentValidation"];

    /// <summary>
    /// In-repo synthetic corpus subjects (offline-buildable, no submodules) — the set the Slice-C
    /// task-generation demonstration runs against when the OSS corpus is not checked out.
    /// </summary>
    public static IReadOnlyList<string> SyntheticProjects => ["Synthetic", "Synthetic2"];

    /// <summary>
    /// Default corpus directory: the vendored, SHA-pinned OSS submodules under
    /// <c>bench/corpus/</c> (D-W4.2). Replaces the former un-pinned external
    /// <c>~/sources/repos/experimental/github-top10</c> default. <c>--projects-dir</c>
    /// still overrides it.
    /// </summary>
    public static string DefaultCorpusDir => Path.Combine(FindCalorRoot(), "bench", "corpus");

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
            TargetFramework = "net10.0",
            MinimumCoverageFraction = 0.625,
            MinimumNativeFraction = 0.625,
        };
    }

    private static RoundTripConfig Synthetic2(string projectsDir, string dotnetPath)
    {
        // Second in-repo synthetic subject (GeoLib) — a distinct project so the Slice-C
        // demonstration yields eligible tasks across ≥2 projects without the OSS submodules.
        var calorRoot = FindCalorRoot();
        var syntheticRoot = Path.Combine(calorRoot, "tests", "Calor.RoundTrip.Synthetic2");
        return new RoundTripConfig
        {
            ProjectName = "Synthetic2",
            OriginalProjectPath = syntheticRoot,
            LibrarySourceRelativePath = "GeoLib",
            SolutionOrProjectFile = "GeoLib.Tests/GeoLib.Tests.csproj",
            DotnetPath = dotnetPath,
            TargetFramework = "net10.0",
            MinimumCoverageFraction = 0.25,
            MinimumNativeFraction = 0.25,
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

    // MediatR — pinned v12.4.1 (fb309026), Apache-2.0 (last Apache release before
    // the v13 commercial relicense). Test project targets net8.0 at this tag; the
    // net8.0 runtime is on hand, so we build/test at net8.0 (no source retarget).
    private static RoundTripConfig MediatR(string projectsDir, string dotnetPath) => new()
    {
        ProjectName = "MediatR",
        OriginalProjectPath = Path.Combine(projectsDir, "MediatR"),
        LibrarySourceRelativePath = "src/MediatR",
        SolutionOrProjectFile = "test/MediatR.Tests/MediatR.Tests.csproj",
        DotnetPath = dotnetPath,
        TargetFramework = "net8.0",
        ExtraBuildProperties = "-p:NuGetAudit=false -p:TreatWarningsAsErrors=false",
        MinimumCoverageFraction = 0.55,
        MinimumNativeFraction = 0.40,
    };

    // Serilog — pinned v4.3.1 (0597ddfb), Apache-2.0. Both library and test target
    // net10.0 natively at this tag, so it round-trips on the pinned .NET 10 SDK
    // with no retargeting.
    private static RoundTripConfig Serilog(string projectsDir, string dotnetPath) => new()
    {
        ProjectName = "Serilog",
        OriginalProjectPath = Path.Combine(projectsDir, "serilog"),
        LibrarySourceRelativePath = "src/Serilog",
        SolutionOrProjectFile = "test/Serilog.Tests/Serilog.Tests.csproj",
        DotnetPath = dotnetPath,
        TargetFramework = "net10.0",
        ExtraBuildProperties = "-p:NuGetAudit=false -p:TreatWarningsAsErrors=false",
        // Known conversion breakers stay visible as denominator failures.
        ExcludePatterns =
        [
            .. RoundTripConfig.DefaultExcludePatterns(),
            "**/Core/Logger.cs",
            "**/Rendering/ReusableStringWriter.cs",
        ],
        MinimumCoverageFraction = 0.30,
        MinimumNativeFraction = 0.25,
    };

    // FluentValidation — pinned 12.1.1 (71b3c60c), Apache-2.0. Library targets
    // net8.0 and the test project targets net8.0;net9.0 at this tag; only net8.0
    // runtime is on hand, so we build/test at net8.0 (no source retarget).
    private static RoundTripConfig FluentValidation(string projectsDir, string dotnetPath) => new()
    {
        ProjectName = "FluentValidation",
        OriginalProjectPath = Path.Combine(projectsDir, "FluentValidation"),
        LibrarySourceRelativePath = "src/FluentValidation",
        SolutionOrProjectFile = "src/FluentValidation.Tests/FluentValidation.Tests.csproj",
        DotnetPath = dotnetPath,
        TargetFramework = "net8.0",
        ExtraBuildProperties = "-p:NuGetAudit=false -p:TreatWarningsAsErrors=false",
        MinimumCoverageFraction = 0.50,
        MinimumNativeFraction = 0.48,
    };
}
