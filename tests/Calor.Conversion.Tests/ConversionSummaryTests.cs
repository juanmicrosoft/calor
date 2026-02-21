using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// Produces a summary report: X/Y conversions passing, with breakdown by feature.
/// </summary>
public class ConversionSummaryTests
{
    private readonly ITestOutputHelper _output;

    public ConversionSummaryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Summary_AllSnippets_ByFeature()
    {
        var featureResults = new Dictionary<string, List<(string Id, string Desc, bool Pass, string? Reason)>>();

        foreach (var snippet in ConversionCatalog.AllSnippets)
        {
            if (!featureResults.ContainsKey(snippet.Feature))
                featureResults[snippet.Feature] = new();

            string? reason = null;
            bool pass;

            try
            {
                var result = TestHelpers.ConvertCSharp(
                    snippet.CSharpSource, $"Test_{snippet.Id.Replace("-", "_")}");

                if (snippet.IsKnownGap)
                {
                    // For known gaps, "pass" means it didn't crash and produced output or diagnostics
                    pass = (result.CalorSource != null && result.CalorSource.Length > 0) ||
                           result.Issues.Count > 0;
                    if (!pass) reason = "No output and no diagnostics";
                }
                else
                {
                    pass = result.Success && result.CalorSource != null;
                    if (!pass) reason = string.Join("; ", result.Issues.Select(i => i.Message));
                }
            }
            catch (Exception ex)
            {
                pass = false;
                reason = $"CRASH: {ex.GetType().Name}: {ex.Message}";
            }

            featureResults[snippet.Feature].Add((snippet.Id, snippet.Description, pass, reason));
        }

        // Print summary
        var totalPass = 0;
        var totalCount = 0;

        _output.WriteLine("╔══════════════════════════════════════════════════════════╗");
        _output.WriteLine("║          CALOR CONVERSION TEST SUMMARY                  ║");
        _output.WriteLine("╠══════════════════════════════════════════════════════════╣");

        foreach (var (feature, results) in featureResults.OrderBy(kv => kv.Key))
        {
            var featurePass = results.Count(r => r.Pass);
            var featureTotal = results.Count;
            totalPass += featurePass;
            totalCount += featureTotal;

            var indicator = featurePass == featureTotal ? "PASS" : $"{featurePass}/{featureTotal}";
            _output.WriteLine($"║  {feature,-30} {indicator,8}  ║");

            foreach (var (id, desc, pass, reason) in results)
            {
                var icon = pass ? " OK " : "FAIL";
                _output.WriteLine($"║    [{icon}] {id}: {desc,-32} ║");
                if (!pass && reason != null)
                    _output.WriteLine($"║          Reason: {reason,-32} ║");
            }
        }

        _output.WriteLine("╠══════════════════════════════════════════════════════════╣");
        _output.WriteLine($"║  TOTAL: {totalPass}/{totalCount} conversions passing" +
            $"{"",-22}║");
        _output.WriteLine("╚══════════════════════════════════════════════════════════╝");

        // All snippets must at least not crash
        Assert.Equal(totalCount, totalPass);
    }

    [Fact]
    public void Summary_RoundTrip_ByFeature()
    {
        var featureResults = new Dictionary<string, List<(string Id, string Desc,
            bool ConvOk, bool ParseOk, bool RoslynOk)>>();

        foreach (var snippet in ConversionCatalog.RoundTripSnippets)
        {
            if (!featureResults.ContainsKey(snippet.Feature))
                featureResults[snippet.Feature] = new();

            var result = TestHelpers.FullRoundTrip(
                snippet.CSharpSource, $"Test_{snippet.Id.Replace("-", "_")}");

            featureResults[snippet.Feature].Add((snippet.Id, snippet.Description,
                result.ConversionSuccess, result.CalorParseSuccess, result.RoslynSuccess));
        }

        _output.WriteLine("╔══════════════════════════════════════════════════════════╗");
        _output.WriteLine("║       ROUND-TRIP TEST SUMMARY (C#→Calor→C#→Roslyn)     ║");
        _output.WriteLine("╠══════════════════════════════════════════════════════════╣");

        var totalConv = 0; var totalParse = 0; var totalRoslyn = 0; var totalCount = 0;

        foreach (var (feature, results) in featureResults.OrderBy(kv => kv.Key))
        {
            var convOk = results.Count(r => r.ConvOk);
            var parseOk = results.Count(r => r.ParseOk);
            var roslynOk = results.Count(r => r.RoslynOk);
            var count = results.Count;
            totalConv += convOk; totalParse += parseOk; totalRoslyn += roslynOk; totalCount += count;

            _output.WriteLine($"║  {feature,-25} Conv:{convOk}/{count} Parse:{parseOk}/{count} Roslyn:{roslynOk}/{count} ║");
        }

        _output.WriteLine("╠══════════════════════════════════════════════════════════╣");
        _output.WriteLine($"║  TOTAL  Conv:{totalConv}/{totalCount}  Parse:{totalParse}/{totalCount}  Roslyn:{totalRoslyn}/{totalCount}" +
            $"{"",-10}║");
        _output.WriteLine("╚══════════════════════════════════════════════════════════╝");
    }
}
