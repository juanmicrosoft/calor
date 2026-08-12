using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Evaluation.Scorecard;

public record ScorecardDiff(
    List<string> Regressions,
    List<string> Improvements,
    int ConversionDelta,
    int RoundTripDelta,
    ConversionScorecard Baseline,
    ConversionScorecard Current)
{
    public bool HasRegressions => Regressions.Count > 0;
}

public static class ScorecardComparison
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ScorecardDiff Compare(ConversionScorecard baseline, ConversionScorecard current)
    {
        var baselineById = baseline.Results.ToDictionary(r => r.Id);
        var currentById = current.Results.ToDictionary(r => r.Id);

        var regressions = new List<string>();
        var improvements = new List<string>();

        foreach (var (id, baselineResult) in baselineById)
        {
            if (!currentById.TryGetValue(id, out var currentResult))
            {
                regressions.Add(id);
                continue;
            }

            if (StatusRank(currentResult.Status) < StatusRank(baselineResult.Status) ||
                baselineResult.RoundTripSuccess && !currentResult.RoundTripSuccess)
                regressions.Add(id);
            else if (!baselineResult.RoundTripSuccess && currentResult.RoundTripSuccess)
                improvements.Add(id);
        }

        return new ScorecardDiff(
            Regressions: regressions,
            Improvements: improvements,
            ConversionDelta: current.FullyConverted - baseline.FullyConverted,
            RoundTripDelta: current.RoundTripPassing - baseline.RoundTripPassing,
            Baseline: baseline,
            Current: current);

        static int StatusRank(SnippetStatus status)
            => status switch
            {
                SnippetStatus.Crashed => 0,
                SnippetStatus.Blocked => 1,
                SnippetStatus.PartiallyConverted => 2,
                SnippetStatus.FullyConverted => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
    }

    public static async Task<ConversionScorecard> LoadBaselineAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<ConversionScorecard>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize baseline: {path}");
    }
}
