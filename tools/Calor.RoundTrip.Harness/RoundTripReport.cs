using System.Text.Json.Serialization;

namespace Calor.RoundTrip.Harness;

/// <summary>
/// Complete round-trip verification report for a project.
/// </summary>
public sealed class RoundTripReport
{
    public required string ProjectName { get; init; }
    public string CalorVersion { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public TimeSpan Duration => FinishedAt - StartedAt;

    public TestRunResult? Baseline { get; set; }
    public List<FileConversionResult> FileResults { get; set; } = [];
    public BuildResult? BuildResult { get; set; }
    public TestRunResult? RoundTripTests { get; set; }
    public TestComparison? Comparison { get; set; }

    /// <summary>Optional bisect results mapping culprit file → test names.</summary>
    public Dictionary<string, List<string>>? BisectResults { get; set; }

    [JsonIgnore]
    public string Verdict => Comparison?.Status.ToString() ?? "Incomplete";
}

public sealed class FileConversionResult
{
    public required string FilePath { get; init; }
    public FileStatus Status { get; set; }
    public bool ConversionSuccess { get; set; }
    public double ConversionRate { get; set; }
    public List<string> Gaps { get; set; } = [];
    public int InteropBlocks { get; set; }
    public List<string> Errors { get; set; } = [];

    /// <summary>Stored for debugging; not serialized to JSON reports.</summary>
    [JsonIgnore]
    public string? EmittedCSharp { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileStatus
{
    Replaced,
    ConversionFailed,
    EmitSyntaxError,
    CompileError,
    Crashed,
    Excluded,
}

public sealed class BuildResult
{
    public bool Succeeded { get; init; }
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public List<string> Errors { get; init; } = [];
}

public sealed class TestRunResult
{
    public int ExitCode { get; init; }
    public int TotalTests { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public List<TestResult> Results { get; init; } = [];
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
}

public sealed class TestResult
{
    public string TestName { get; init; } = "";
    public string Outcome { get; init; } = "Unknown";
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public string? StackTrace { get; init; }
}

public sealed class TestComparison
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ComparisonStatus Status { get; set; }

    public int BaselineTotal { get; set; }
    public int BaselinePassed { get; set; }
    public int RoundTripTotal { get; set; }
    public int RoundTripPassed { get; set; }
    public List<TestResult> Regressions { get; set; } = [];
    public int PreExistingFailures { get; set; }
    public List<string> NewPasses { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComparisonStatus
{
    Pass,
    MinorRegressions,
    MajorRegressions,
    BuildFailed,
    Incomplete,
}
