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
        }

        // #771: this test's name promises a compile — it must ASSERT one. The
        // shared GeneratedCSharpCompiler resolves the full trusted-platform
        // reference set plus Calor.Runtime, so a failure here is a real
        // conversion/emitter defect, not missing-reference noise.
        Assert.True(result.CSharpSyntaxSuccess,
            $"[{id}] Emitted C# has syntax errors: {string.Join("; ", result.RoslynErrors)}");
        Assert.True(result.RoslynSuccess,
            $"[{id}] Emitted C# does not compile: {string.Join("; ", result.RoslynErrors)}");
        Assert.Empty(result.SemanticLosses);
    }

    [Fact]
    public void RoundTrip_AllRoundTripSnippets_Summary()
    {
        var results = new List<(string Id, string Desc, bool ConvOk, bool ParseOk, bool RoslynOk, bool Lossless)>();

        foreach (var snippet in ConversionCatalog.RoundTripSnippets)
        {
            var result = TestHelpers.FullRoundTrip(snippet.CSharpSource, $"Test_{snippet.Id.Replace("-", "_")}");
            results.Add((snippet.Id, snippet.Description,
                result.ConversionSuccess, result.CalorParseSuccess, result.RoslynSuccess,
                result.SemanticLosses.Count == 0));
        }

        _output.WriteLine("=== Round-Trip Summary ===");
        _output.WriteLine($"Total: {results.Count}");
        _output.WriteLine($"Conversion OK: {results.Count(r => r.ConvOk)}/{results.Count}");
        _output.WriteLine($"Calor Parse OK: {results.Count(r => r.ParseOk)}/{results.Count}");
        _output.WriteLine($"Roslyn Compile OK: {results.Count(r => r.RoslynOk)}/{results.Count}");
        _output.WriteLine($"Full Round-Trip OK: {results.Count(r => r.ConvOk && r.ParseOk && r.RoslynOk && r.Lossless)}/{results.Count}");
        _output.WriteLine("");

        foreach (var (id, desc, convOk, parseOk, roslynOk, lossless) in results)
        {
            var status = convOk && parseOk && roslynOk && lossless ? "PASS" :
                         convOk && parseOk ? "PARTIAL" : "FAIL";
            _output.WriteLine($"  [{status}] {id}: {desc}");
        }

        // All round-trip snippets must convert, parse, AND compile (#771 — the
        // Roslyn column was previously computed but never asserted).
        Assert.All(results, r => Assert.True(r.ConvOk,
            $"[{r.Id}] {r.Desc}: conversion failed"));
        Assert.All(results, r => Assert.True(r.ParseOk,
            $"[{r.Id}] {r.Desc}: Calor parse failed"));
        Assert.All(results, r => Assert.True(r.RoslynOk,
            $"[{r.Id}] {r.Desc}: emitted C# does not compile"));
        Assert.All(results, r => Assert.True(r.Lossless,
            $"[{r.Id}] {r.Desc}: semantic loss was recorded"));
    }

    [Fact]
    public void FullRoundTrip_RejectsCheckedExpressionSemanticLoss()
    {
        const string source = """
            public static class Overflow
            {
                public static int Add(int value) => checked(value + 1);
            }
            """;

        var result = TestHelpers.FullRoundTrip(source, "Overflow");

        Assert.True(result.RoslynSuccess);
        Assert.NotEmpty(result.SemanticLosses);
        Assert.False(result.FullSuccess);
    }

    [Fact]
    public void RoslynGate_RejectsSyntaxValidTypeInvalidOutput()
    {
        var validation = TestHelpers.RoslynCompile(
            "public static class Invalid { public static int Read() => \"wrong\"; }");

        Assert.True(validation.SyntaxSuccess);
        Assert.False(validation.CompilationSuccess);
        Assert.Contains(
            validation.FormattedCompilationErrors,
            error => error.Contains("CS0029", StringComparison.Ordinal));
    }

    [Fact]
    public void RoslynGate_ClassifiesMissingProjectReferenceAsCompilationFailure()
    {
        var validation = TestHelpers.RoslynCompile(
            "public sealed class Consumer { public External.Library.Api Read() => new(); }");

        Assert.True(validation.SyntaxSuccess);
        Assert.False(validation.CompilationSuccess);
        Assert.Contains(
            validation.FormattedCompilationErrors,
            error => error.Contains("CS0246", StringComparison.Ordinal));
    }
}
