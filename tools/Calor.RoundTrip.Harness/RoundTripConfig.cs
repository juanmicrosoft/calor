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

    /// <summary>
    /// Optional project whose evaluated graph supplies conversion parse contexts.
    /// Use the subject library project when the test graph also compiles linked or
    /// multi-target copies of the same physical source.
    /// </summary>
    public string? ParseContextProjectFile { get; init; }

    /// <summary>Working directory for the round-trip. Files will be copied here.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Source files/patterns to EXCLUDE from conversion. Build outputs under
    /// bin/ and obj/ are not source candidates and are removed before matching.
    /// </summary>
    public List<string> ExcludePatterns { get; init; } = DefaultExcludePatterns();

    /// <summary>Maximum time for dotnet test before timeout.</summary>
    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Maximum time for a single dotnet build before timeout. Generous by default
    /// because the build-recovery loop rebuilds the WHOLE project (restore + build)
    /// repeatedly, and on a cold NuGet cache a large subject (e.g. Serilog) can take
    /// several minutes per rebuild. Too short a timeout makes the build fail with no
    /// extractable errors, which reverts nothing and would otherwise leave the printed
    /// coverage fraction inflated — the harness now flags that case inconclusive rather
    /// than trusting it, but the generous timeout keeps normal cold-cache runs valid.
    /// </summary>
    public TimeSpan BuildTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Maximum time allowed for one in-process source conversion.</summary>
    public TimeSpan ConversionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>dotnet test additional arguments (e.g., --filter for specific tests).</summary>
    public string? TestFilter { get; init; }

    /// <summary>Target framework to use for dotnet test (e.g., "net10.0").</summary>
    public string? TargetFramework { get; init; }

    /// <summary>Concrete MSBuild configuration evaluated by the harness.</summary>
    public string Configuration { get; init; } = "Debug";

    /// <summary>
    /// Extra MSBuild properties appended to every restore/build/test invocation for
    /// this project (e.g. <c>-p:NuGetAudit=false -p:TreatWarningsAsErrors=false</c>).
    /// Vendored OSS corpus subjects are measurement subjects, not held to Calor's own
    /// warning policy: <c>NuGetAudit=false</c> stops a NuGet advisory (NU1903) failing
    /// restore under the subject's own <c>TreatWarningsAsErrors</c>, and disabling
    /// warnings-as-errors keeps a converted file from being reverted over a mere
    /// analyzer warning (coverage counts ledger losses, not warnings). Empty for
    /// Calor's own Synthetic project.
    /// </summary>
    public string ExtraBuildProperties { get; init; } = "";

    /// <summary>
    /// Explicitly allows conversion without an evaluated project. Selected-branch
    /// project runs must leave this false so missing project context fails closed.
    /// </summary>
    public bool LooseDirectoryMode { get; init; }

    /// <summary>Path to dotnet executable.</summary>
    public string DotnetPath { get; init; } = "dotnet";

    /// <summary>Whether to run regression bisect when regressions are found.</summary>
    public bool EnableBisect { get; init; } = false;

    /// <summary>Maximum regressions to trigger bisect.</summary>
    public int BisectMaxRegressions { get; init; } = 50;

    /// <summary>Minimum fraction of all candidate files that must be converted and kept.</summary>
    public double MinimumCoverageFraction { get; init; }

    /// <summary>Minimum fraction of all candidate files that must be converted natively with zero losses.</summary>
    public double MinimumNativeFraction { get; init; }

    /// <summary>
    /// Fully-qualified test names known to be flaky in the upstream corpus
    /// (i.e. flake independently of anything Calor does — timing-dependent
    /// tests, tests that rely on machine-wide state, etc.). Regressions on
    /// these tests are NOT counted toward the block/warn thresholds; they
    /// are recorded on <see cref="TestComparison.IgnoredFlakyRegressions"/>
    /// so the drift stays auditable in the round-trip report.
    ///
    /// Each entry should be an exact match against <see cref="TestResult.FullyQualifiedName"/>
    /// and should carry a comment linking to the investigation that added
    /// it — the intent is "explicitly-known upstream flake", not "silence
    /// anything red". Adding a name here should always be paired with a
    /// GitHub issue or PR describing why the upstream test flakes.
    /// </summary>
    public HashSet<string> ExpectedFlakyTestFullyQualifiedNames { get; init; } = [];

    internal Action<string>? FileConversionStarted { get; init; }

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
