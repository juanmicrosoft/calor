using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Evaluation.Scorecard;

/// <summary>
/// Scorecard tests run the full C# → Calor → C# round-trip pipeline on the 100-file
/// CSharpImport corpus and assert that conversion rates stay above committed baselines.
///
/// To update the baseline after improving the converter/compiler:
///   dotnet run --project tests/Calor.Evaluation -- scorecard --format json -o tests/Calor.Evaluation/Scorecard/baseline
/// Then commit the updated baseline.json.
/// </summary>
public class ConversionScorecardTests
{
    // Re-calibrated for #775: interop-preserved records round-trip faithfully
    // but are partial conversions, so they no longer inflate the native score.
    // Baselines are exact (no slack): the corpus and converter are
    // deterministic, and per-fixture zero-regression is enforced against
    // baseline.json by NoRegressionsVsCommittedBaseline.
    private const int BASELINE_FULLY_CONVERTED = 92;
    private const int BASELINE_ROUNDTRIP = 96;

    private static readonly Lazy<ConversionScorecard> _scorecard = new(() =>
    {
        var testDataDir = FindTestDataDir();
        var runner = new ConversionScorecardRunner();
        return runner.Run(testDataDir);
    });

    private static ConversionScorecard Scorecard => _scorecard.Value;

    private readonly ITestOutputHelper _output;

    public ConversionScorecardTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NoSnippetsCrash()
    {
        _output.WriteLine($"Crashed: {Scorecard.Crashed}/{Scorecard.Total}");
        if (Scorecard.Crashed > 0)
        {
            foreach (var r in Scorecard.Results.Where(r => r.Status == SnippetStatus.Crashed))
                _output.WriteLine($"  CRASH: {r.Id} {r.FileName} — {r.ConversionIssues.FirstOrDefault() ?? r.CompilationDiagnostics.FirstOrDefault()}");
        }
        Assert.Equal(0, Scorecard.Crashed);
    }

    [Fact]
    public void ConversionRate_AboveBaseline()
    {
        _output.WriteLine($"Fully converted: {Scorecard.FullyConverted}/{Scorecard.Total} (baseline: {BASELINE_FULLY_CONVERTED})");
        Assert.Equal(BASELINE_FULLY_CONVERTED, Scorecard.FullyConverted);
    }

    [Fact]
    public void Level1_HighConversionRate()
    {
        if (!Scorecard.ByLevel.TryGetValue(1, out var level1))
        {
            Assert.Fail("No Level 1 results found");
            return;
        }
        _output.WriteLine($"Level 1: {level1.Passed}/{level1.Total} ({level1.Rate:P0})");
        Assert.True(level1.Rate >= 0.70,
            $"Level 1 rate {level1.Rate:P0} < 70%");
    }

    [Fact]
    public void RoundTrip_AboveBaseline()
    {
        _output.WriteLine($"Round-trip: {Scorecard.RoundTripPassing}/{Scorecard.Total} (baseline: {BASELINE_ROUNDTRIP})");
        Assert.Equal(BASELINE_ROUNDTRIP, Scorecard.RoundTripPassing);
    }

    [Fact]
    public void GeneratesValidJsonReport()
    {
        var json = ScorecardReportGenerator.GenerateJson(Scorecard);
        Assert.False(string.IsNullOrWhiteSpace(json));

        // Verify it's valid JSON that can be round-tripped
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("total", out var totalProp));
        Assert.Equal(Scorecard.Total, totalProp.GetInt32());
    }

    [Fact]
    public void GeneratesValidMarkdownReport()
    {
        var md = ScorecardReportGenerator.GenerateMarkdown(Scorecard);
        Assert.False(string.IsNullOrWhiteSpace(md));
        Assert.Contains("# Calor Conversion Scorecard", md);
        Assert.Contains("Round-trip verified", md);

        _output.WriteLine(md);
    }

    [Fact]
    public void AllSnippetsHaveResults()
    {
        Assert.Equal(100, Scorecard.Total);
    }

    [Fact]
    public void RecordInterop_IsNotReportedAsNativeFeatureConversion()
    {
        Assert.Contains("record", Scorecard.ByFeature);
        var records = Scorecard.ByFeature["record"];

        Assert.Equal(2, records.Total);
        Assert.Equal(0, records.Passed);
        Assert.Equal(0, records.Rate);
    }

    [Fact]
    public void LocalFunctionInterop_IsNotReportedAsNativeFeatureConversion()
    {
        Assert.Contains("local_function", Scorecard.ByFeature);
        var localFunctions = Scorecard.ByFeature["local_function"];

        Assert.Equal(1, localFunctions.Total);
        Assert.Equal(0, localFunctions.Passed);
        Assert.Equal(0, localFunctions.Rate);
    }

    [Fact]
    public async Task NoRegressionsVsCommittedBaseline()
    {
        var baselinePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scorecard", "baseline.json");
        Assert.True(File.Exists(baselinePath),
            $"Committed scorecard baseline is missing: {baselinePath}");

        var baseline = await ScorecardComparison.LoadBaselineAsync(baselinePath);
        var diff = ScorecardComparison.Compare(baseline, Scorecard);

        Assert.Equal(
            baseline.Results.Select(result => result.Id).OrderBy(id => id),
            Scorecard.Results.Select(result => result.Id).OrderBy(id => id));

        _output.WriteLine($"Baseline: {baseline.RoundTripPassing}/{baseline.Total}");
        _output.WriteLine($"Current:  {Scorecard.RoundTripPassing}/{Scorecard.Total}");
        _output.WriteLine($"Delta:    {diff.RoundTripDelta:+#;-#;0}");

        if (diff.Regressions.Count > 0)
        {
            foreach (var id in diff.Regressions)
                _output.WriteLine($"  REGRESSION: {id}");
        }
        if (diff.Improvements.Count > 0)
        {
            foreach (var id in diff.Improvements)
                _output.WriteLine($"  IMPROVEMENT: {id}");
        }

        Assert.Empty(diff.Regressions);
    }

    private static string FindTestDataDir()
    {
        // Try output directory first (copied by csproj)
        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "CSharpImport");
        if (Directory.Exists(outputDir) && File.Exists(Path.Combine(outputDir, "manifest.json")))
            return outputDir;

        // Try relative path from project
        var projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        var testDataDir = Path.Combine(projectDir, "TestData", "CSharpImport");
        if (Directory.Exists(testDataDir))
            return testDataDir;

        throw new DirectoryNotFoundException(
            $"Cannot find CSharpImport test data. Tried:\n  {outputDir}\n  {testDataDir}");
    }
}
