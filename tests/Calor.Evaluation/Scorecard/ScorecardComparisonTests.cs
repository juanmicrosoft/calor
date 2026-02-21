using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Calor.Evaluation.Scorecard;

public class ScorecardComparisonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Compare_DetectsRegression()
    {
        var baseline = MakeScorecard(
            ("001", true),
            ("002", true),
            ("003", false));

        var current = MakeScorecard(
            ("001", true),
            ("002", false),  // regression
            ("003", false));

        var diff = ScorecardComparison.Compare(baseline, current);

        Assert.True(diff.HasRegressions);
        Assert.Single(diff.Regressions);
        Assert.Equal("002", diff.Regressions[0]);
        Assert.Empty(diff.Improvements);
        Assert.Equal(-1, diff.ConversionDelta);
        Assert.Equal(-1, diff.RoundTripDelta);
    }

    [Fact]
    public void Compare_DetectsImprovement()
    {
        var baseline = MakeScorecard(
            ("001", true),
            ("002", false),
            ("003", false));

        var current = MakeScorecard(
            ("001", true),
            ("002", true),   // improvement
            ("003", true));  // improvement

        var diff = ScorecardComparison.Compare(baseline, current);

        Assert.False(diff.HasRegressions);
        Assert.Empty(diff.Regressions);
        Assert.Equal(2, diff.Improvements.Count);
        Assert.Contains("002", diff.Improvements);
        Assert.Contains("003", diff.Improvements);
        Assert.Equal(2, diff.ConversionDelta);
        Assert.Equal(2, diff.RoundTripDelta);
    }

    [Fact]
    public void Compare_DetectsMixedChanges()
    {
        var baseline = MakeScorecard(
            ("001", true),
            ("002", false),
            ("003", true));

        var current = MakeScorecard(
            ("001", false),  // regression
            ("002", true),   // improvement
            ("003", true));

        var diff = ScorecardComparison.Compare(baseline, current);

        Assert.True(diff.HasRegressions);
        Assert.Single(diff.Regressions);
        Assert.Equal("001", diff.Regressions[0]);
        Assert.Single(diff.Improvements);
        Assert.Equal("002", diff.Improvements[0]);
        Assert.Equal(0, diff.ConversionDelta);
        Assert.Equal(0, diff.RoundTripDelta);
    }

    [Fact]
    public void Compare_NoChanges_NoDiff()
    {
        var baseline = MakeScorecard(
            ("001", true),
            ("002", false));

        var current = MakeScorecard(
            ("001", true),
            ("002", false));

        var diff = ScorecardComparison.Compare(baseline, current);

        Assert.False(diff.HasRegressions);
        Assert.Empty(diff.Regressions);
        Assert.Empty(diff.Improvements);
        Assert.Equal(0, diff.ConversionDelta);
        Assert.Equal(0, diff.RoundTripDelta);
    }

    [Fact]
    public void Compare_IgnoresNewSnippets()
    {
        var baseline = MakeScorecard(
            ("001", true));

        var current = MakeScorecard(
            ("001", true),
            ("002", true));  // new snippet — should not count as improvement

        var diff = ScorecardComparison.Compare(baseline, current);

        Assert.False(diff.HasRegressions);
        Assert.Empty(diff.Improvements);
    }

    [Fact]
    public void JsonRoundTrip_PreservesScorecard()
    {
        var original = MakeScorecard(
            ("001", true),
            ("002", false),
            ("003", true));

        var json = ScorecardReportGenerator.GenerateJson(original);
        var deserialized = JsonSerializer.Deserialize<ConversionScorecard>(json, JsonOptions)!;

        Assert.Equal(original.Total, deserialized.Total);
        Assert.Equal(original.FullyConverted, deserialized.FullyConverted);
        Assert.Equal(original.Blocked, deserialized.Blocked);
        Assert.Equal(original.Crashed, deserialized.Crashed);
        Assert.Equal(original.RoundTripPassing, deserialized.RoundTripPassing);
        Assert.Equal(original.Results.Count, deserialized.Results.Count);

        // Verify individual results round-trip
        for (int i = 0; i < original.Results.Count; i++)
        {
            Assert.Equal(original.Results[i].Id, deserialized.Results[i].Id);
            Assert.Equal(original.Results[i].Status, deserialized.Results[i].Status);
            Assert.Equal(original.Results[i].RoundTripSuccess, deserialized.Results[i].RoundTripSuccess);
        }
    }

    [Fact]
    public async Task LoadBaselineAsync_RoundTrips()
    {
        var original = MakeScorecard(
            ("001", true),
            ("002", false));

        var tmpPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpPath, ScorecardReportGenerator.GenerateJson(original));
            var loaded = await ScorecardComparison.LoadBaselineAsync(tmpPath);

            Assert.Equal(original.Total, loaded.Total);
            Assert.Equal(original.FullyConverted, loaded.FullyConverted);
            Assert.Equal(original.RoundTripPassing, loaded.RoundTripPassing);
            Assert.Equal(original.Results.Count, loaded.Results.Count);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Fact]
    public void Compare_ThenSerializeDiff_Works()
    {
        var baseline = MakeScorecard(("001", true), ("002", false));
        var current = MakeScorecard(("001", false), ("002", true));

        var diff = ScorecardComparison.Compare(baseline, current);

        // Verify the diff is usable for CI reporting
        Assert.Single(diff.Regressions);
        Assert.Single(diff.Improvements);
        Assert.Equal(0, diff.ConversionDelta);
    }

    private static ConversionScorecard MakeScorecard(params (string id, bool roundTripSuccess)[] snippets)
    {
        var results = snippets.Select(s =>
        {
            var status = s.roundTripSuccess
                ? SnippetStatus.FullyConverted
                : SnippetStatus.PartiallyConverted;

            return new SnippetResult(
                Id: s.id,
                FileName: $"test/{s.id}.cs",
                Level: 1,
                Features: new[] { "test" },
                Status: status,
                ConversionSuccess: true,
                ConversionErrors: 0,
                ConversionWarnings: 0,
                ConversionIssues: Array.Empty<string>(),
                CompilationSuccess: s.roundTripSuccess,
                CompilationErrors: s.roundTripSuccess ? 0 : 1,
                CompilationDiagnostics: s.roundTripSuccess
                    ? Array.Empty<string>()
                    : new[] { "mock error" },
                RoslynParseSuccess: s.roundTripSuccess,
                ConversionDuration: TimeSpan.FromMilliseconds(10),
                CompilationDuration: TimeSpan.FromMilliseconds(5));
        }).ToList();

        var fullyConverted = results.Count(r => r.Status == SnippetStatus.FullyConverted);
        var partial = results.Count(r => r.Status == SnippetStatus.PartiallyConverted);
        var roundTrip = results.Count(r => r.RoundTripSuccess);

        return new ConversionScorecard(
            Timestamp: DateTime.UtcNow,
            CommitHash: "abc1234",
            Version: "1.0",
            Total: results.Count,
            FullyConverted: fullyConverted,
            PartiallyConverted: partial,
            Blocked: 0,
            Crashed: 0,
            RoundTripPassing: roundTrip,
            ByLevel: new Dictionary<int, LevelBreakdown>
            {
                [1] = new(results.Count, roundTrip, results.Count > 0 ? (double)roundTrip / results.Count : 0)
            },
            ByFeature: new Dictionary<string, FeatureBreakdown>
            {
                ["test"] = new(results.Count, roundTrip, results.Count > 0 ? (double)roundTrip / results.Count : 0)
            },
            Results: results);
    }
}
