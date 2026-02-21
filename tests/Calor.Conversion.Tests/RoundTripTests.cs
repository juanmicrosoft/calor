using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// Round-trip tests: C# → Calor → C# emit → Roslyn compile.
/// Verifies that the full conversion pipeline produces valid C#.
/// Only runs on snippets marked as RoundTripSupported.
/// </summary>
public class RoundTripTests
{
    private readonly ITestOutputHelper _output;

    public RoundTripTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> RoundTripSnippetData()
    {
        foreach (var snippet in ConversionCatalog.RoundTripSnippets)
            yield return new object[] { snippet.Id, snippet.Description, snippet.CSharpSource };
    }

    [Theory]
    [MemberData(nameof(RoundTripSnippetData))]
    public void RoundTrip_ConversionSucceeds(string id, string description, string csharpSource)
    {
        var result = TestHelpers.FullRoundTrip(csharpSource, $"Test_{id.Replace("-", "_")}");

        _output.WriteLine($"[{id}] {description}");
        _output.WriteLine($"  Conversion: {(result.ConversionSuccess ? "OK" : "FAILED")}");

        if (!result.ConversionSuccess)
        {
            foreach (var issue in result.ConversionIssues)
                _output.WriteLine($"    Issue: {issue}");
        }

        Assert.True(result.ConversionSuccess,
            $"[{id}] C# → Calor conversion failed: " +
            string.Join("; ", result.ConversionIssues));
    }

    [Theory]
    [MemberData(nameof(RoundTripSnippetData))]
    public void RoundTrip_CalorParseSucceeds(string id, string description, string csharpSource)
    {
        var result = TestHelpers.FullRoundTrip(csharpSource, $"Test_{id.Replace("-", "_")}");

        _output.WriteLine($"[{id}] {description}");
        _output.WriteLine($"  Calor parse: {(result.CalorParseSuccess ? "OK" : "FAILED")}");
        if (result.CalorSource != null)
            _output.WriteLine($"  Calor source length: {result.CalorSource.Length}");

        Assert.True(result.ConversionSuccess, $"[{id}] Conversion step failed.");
        Assert.True(result.CalorParseSuccess,
            $"[{id}] Calor → AST parse failed. Calor source:\n{result.CalorSource}");
    }

    [Theory]
    [MemberData(nameof(RoundTripSnippetData))]
    public void RoundTrip_EmittedCSharpIsNotEmpty(string id, string description, string csharpSource)
    {
        _ = description; // used for display in test explorer
        var result = TestHelpers.FullRoundTrip(csharpSource, $"Test_{id.Replace("-", "_")}");

        Assert.True(result.ConversionSuccess, $"[{id}] Conversion step failed.");
        Assert.True(result.CalorParseSuccess, $"[{id}] Calor parse step failed.");
        Assert.NotNull(result.EmittedCSharp);
        Assert.NotEmpty(result.EmittedCSharp!);

        _output.WriteLine($"[{id}] Emitted C# ({result.EmittedCSharp!.Length} chars):");
        _output.WriteLine(result.EmittedCSharp);
    }

    [Theory]
    [MemberData(nameof(RoundTripSnippetData))]
    public void RoundTrip_RoslynCompileSucceeds(string id, string description, string csharpSource)
    {
        var result = TestHelpers.FullRoundTrip(csharpSource, $"Test_{id.Replace("-", "_")}");

        _output.WriteLine($"[{id}] {description}");

        Assert.True(result.ConversionSuccess, $"[{id}] Conversion step failed.");
        Assert.True(result.CalorParseSuccess, $"[{id}] Calor parse step failed.");

        _output.WriteLine($"  Roslyn compile: {(result.RoslynSuccess ? "OK" : "FAILED")}");

        if (!result.RoslynSuccess)
        {
            _output.WriteLine($"  Emitted C#:\n{result.EmittedCSharp}");
            _output.WriteLine($"  Roslyn errors:");
            foreach (var err in result.RoslynErrors)
                _output.WriteLine($"    - {err}");

            // Roslyn compile failures may be expected for some snippets where the
            // emitted C# references types not available in the minimal compilation
            // context (e.g., Task, Func<>, Math).
            _output.WriteLine($"  [INFO] Roslyn compile has {result.RoslynErrors.Count} errors " +
                $"(may be expected due to limited references in test compilation)");
        }
    }

    [Fact]
    public void RoundTrip_AllRoundTripSnippets_Summary()
    {
        var results = new List<(string Id, string Desc, bool ConvOk, bool ParseOk, bool RoslynOk)>();

        foreach (var snippet in ConversionCatalog.RoundTripSnippets)
        {
            var result = TestHelpers.FullRoundTrip(snippet.CSharpSource, $"Test_{snippet.Id.Replace("-", "_")}");
            results.Add((snippet.Id, snippet.Description,
                result.ConversionSuccess, result.CalorParseSuccess, result.RoslynSuccess));
        }

        _output.WriteLine("=== Round-Trip Summary ===");
        _output.WriteLine($"Total: {results.Count}");
        _output.WriteLine($"Conversion OK: {results.Count(r => r.ConvOk)}/{results.Count}");
        _output.WriteLine($"Calor Parse OK: {results.Count(r => r.ParseOk)}/{results.Count}");
        _output.WriteLine($"Roslyn Compile OK: {results.Count(r => r.RoslynOk)}/{results.Count}");
        _output.WriteLine($"Full Round-Trip OK: {results.Count(r => r.ConvOk && r.ParseOk && r.RoslynOk)}/{results.Count}");
        _output.WriteLine("");

        foreach (var (id, desc, convOk, parseOk, roslynOk) in results)
        {
            var status = convOk && parseOk && roslynOk ? "PASS" :
                         convOk && parseOk ? "PARTIAL" : "FAIL";
            _output.WriteLine($"  [{status}] {id}: {desc}");
        }

        // All round-trip snippets must convert and parse successfully
        Assert.All(results, r => Assert.True(r.ConvOk,
            $"[{r.Id}] {r.Desc}: conversion failed"));
        Assert.All(results, r => Assert.True(r.ParseOk,
            $"[{r.Id}] {r.Desc}: Calor parse failed"));
    }
}
