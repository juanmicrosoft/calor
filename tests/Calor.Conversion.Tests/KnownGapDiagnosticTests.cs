using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// Tests that known converter gaps produce clear diagnostics or graceful
/// fallbacks — never a crash (unhandled exception).
/// </summary>
public class KnownGapDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public KnownGapDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> KnownGapData()
    {
        foreach (var snippet in ConversionCatalog.KnownGapSnippets)
            yield return new object[] { snippet.Id, snippet.Description, snippet.CSharpSource };
    }

    [Theory]
    [MemberData(nameof(KnownGapData))]
    public void KnownGap_DoesNotCrash(string id, string description, string csharpSource)
    {
        _output.WriteLine($"[{id}] {description}");

        // The converter must not throw an exception for known gaps
        var exception = Record.Exception(() => TestHelpers.ConvertCSharp(csharpSource, $"Gap_{id.Replace("-", "_")}"));

        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(KnownGapData))]
    public void KnownGap_ProducesOutputOrDiagnostic(string id, string description, string csharpSource)
    {
        _output.WriteLine($"[{id}] {description}");

        var result = TestHelpers.ConvertCSharp(csharpSource, $"Gap_{id.Replace("-", "_")}");

        // Must produce EITHER:
        // 1. CalorSource output (with graceful fallback/TODO comments), OR
        // 2. Clear diagnostic issues in the result
        var hasOutput = result.CalorSource != null && result.CalorSource.Length > 0;
        var hasDiagnostics = result.Issues.Count > 0;

        _output.WriteLine($"  Success: {result.Success}");
        _output.WriteLine($"  Has output: {hasOutput}");
        _output.WriteLine($"  Diagnostics: {result.Issues.Count}");
        foreach (var issue in result.Issues)
            _output.WriteLine($"    - {issue.Message}");

        Assert.True(hasOutput || hasDiagnostics,
            $"[{id}] {description}: Expected either Calor output or diagnostics, got neither.");
    }

    [Theory]
    [MemberData(nameof(KnownGapData))]
    public void KnownGap_GracefulFallback_ContainsTodoOrComment(string id, string description, string csharpSource)
    {
        _ = description; // used for display in test explorer
        var result = TestHelpers.ConvertCSharp(csharpSource, $"Gap_{id.Replace("-", "_")}");

        if (result.CalorSource == null) return; // No output — diagnosed instead

        _output.WriteLine($"[{id}] Checking graceful fallback in output...");
        _output.WriteLine(result.CalorSource);

        // If the conversion produced output, it should still have a module structure
        // (not garbage or partial output). The presence of §M indicates structure.
        Assert.Contains("\u00A7M{", result.CalorSource);
    }

    [Fact]
    public void AllKnownGaps_NeverThrow()
    {
        var crashes = new List<string>();

        foreach (var snippet in ConversionCatalog.KnownGapSnippets)
        {
            try
            {
                TestHelpers.ConvertCSharp(snippet.CSharpSource, $"Gap_{snippet.Id.Replace("-", "_")}");
            }
            catch (Exception ex)
            {
                crashes.Add($"[{snippet.Id}] {snippet.Description}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (crashes.Count > 0)
        {
            _output.WriteLine("CRASHED known gap conversions:");
            foreach (var crash in crashes)
                _output.WriteLine($"  {crash}");
        }

        Assert.Empty(crashes);
    }
}
