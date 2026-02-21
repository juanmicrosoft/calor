using System.Text.Json.Serialization;

namespace Calor.Evaluation.Scorecard;

public enum SnippetStatus
{
    FullyConverted,
    PartiallyConverted,
    Blocked,
    Crashed
}

public record SnippetResult(
    string Id,
    string FileName,
    int Level,
    string[] Features,
    SnippetStatus Status,
    bool ConversionSuccess,
    int ConversionErrors,
    int ConversionWarnings,
    string[] ConversionIssues,
    bool CompilationSuccess,
    int CompilationErrors,
    string[] CompilationDiagnostics,
    bool RoslynParseSuccess,
    TimeSpan ConversionDuration,
    TimeSpan CompilationDuration)
{
    public bool RoundTripSuccess => ConversionSuccess && CompilationSuccess && RoslynParseSuccess;
}

public record ConversionScorecard(
    DateTime Timestamp,
    string? CommitHash,
    string Version,
    int Total,
    int FullyConverted,
    int PartiallyConverted,
    int Blocked,
    int Crashed,
    int RoundTripPassing,
    Dictionary<int, LevelBreakdown> ByLevel,
    Dictionary<string, FeatureBreakdown> ByFeature,
    List<SnippetResult> Results);

public record LevelBreakdown(int Total, int Passed, double Rate);

public record FeatureBreakdown(int Total, int Passed, double Rate);

public record ManifestEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("file")]
    public string File { get; init; } = "";

    [JsonPropertyName("level")]
    public int Level { get; init; }

    [JsonPropertyName("features")]
    public string[] Features { get; init; } = Array.Empty<string>();

    [JsonPropertyName("expectedResult")]
    public string ExpectedResult { get; init; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = "";
}

public record ManifestFile
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("files")]
    public List<ManifestEntry> Files { get; init; } = new();
}
