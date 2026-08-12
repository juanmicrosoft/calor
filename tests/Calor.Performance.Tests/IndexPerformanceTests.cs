using System.Diagnostics;
using Calor.Compiler.Indexing;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Performance.Tests;

/// <summary>
/// Performance envelope for the project index (roadmap §2.5 gate 8): index build
/// ≤ 30s and a warm query ≤ 500ms at project scale.
///
/// The bars are generous by design — the point is that "usable at project scale"
/// is adjudicable at all, and that the number is published whether or not it
/// passes.
///
/// **Denominator, and a correction.** The scoping doc named FluentValidation as
/// the subject, chosen as the largest of the three pinned conversion subjects by
/// C# size (208 files / 25.7k lines). That was the wrong measure: what gets
/// indexed is the CONVERTED Calor, and FluentValidation converts to 62 files /
/// 2.2k lines — 45% of its files, the converter's fidelity-over-coverage posture
/// working as intended. The in-repo `fixture-10k` corpus (106 files / 11.1k
/// lines, 1,366 functions, 587 call edges) is therefore the larger real Calor
/// subject and is measured here, with the conversion subject retained as a
/// second, smaller data point. Measuring the C# line count would have reported a
/// scale the index never sees.
/// </summary>
public sealed class IndexPerformanceFixture
{
    // Built once and shared. Each build costs ~0.4s on the project-scale corpus,
    // and the suite carries a ratcheted total-duration ceiling — rebuilding per
    // test would spend that budget on setup rather than on coverage.
    public IndexPerformanceFixture()
    {
        Root = FindFixtureRoot();
        Sources = ProjectIndexBuilder.DiscoverSources(Root);
        Options = new ProjectIndexBuilder.Options(Root, "perf-gate", Sources);
        Index = ProjectIndexBuilder.Build(Options);
    }

    public string Root { get; }
    public IReadOnlyList<string> Sources { get; }
    public ProjectIndexBuilder.Options Options { get; }
    public ProjectIndex Index { get; }

    private static string FindFixtureRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Calor.sln")))
            directory = Directory.GetParent(directory)!.FullName;
        return Path.Combine(
            directory, "bench", "phase0-agent-native", "latency", "fixture-10k", "src");
    }
}

public class IndexPerformanceTests : IClassFixture<IndexPerformanceFixture>
{
    private const double IndexBuildBudgetSeconds = 30.0;
    private const double WarmQueryBudgetMilliseconds = 500.0;

    private readonly ITestOutputHelper _output;
    private readonly IndexPerformanceFixture _fixture;

    public IndexPerformanceTests(ITestOutputHelper output, IndexPerformanceFixture fixture)
    {
        _output = output;
        _fixture = fixture;
    }

    [Fact]
    public void IndexBuildStaysWithinItsBudget()
    {
        var sources = _fixture.Sources;
        Assert.True(
            sources.Count >= 100,
            $"expected the project-scale fixture, found {sources.Count} files — "
                + "a shrunken corpus would make this budget meaningless");

        // The fixture's construction already warmed the file cache and the JIT.
        var stopwatch = Stopwatch.StartNew();
        var index = ProjectIndexBuilder.Build(_fixture.Options);
        stopwatch.Stop();

        var seconds = stopwatch.Elapsed.TotalSeconds;
        _output.WriteLine(
            $"index build: {seconds:F2}s over {sources.Count} files, "
                + $"{index.Declarations.Count} declarations, {index.CallEdges.Count} call edges "
                + $"(budget {IndexBuildBudgetSeconds}s)");

        Assert.True(
            seconds <= IndexBuildBudgetSeconds,
            $"index build took {seconds:F2}s, over the {IndexBuildBudgetSeconds}s budget");
    }

    [Fact]
    public void WarmQueriesStayWithinTheirBudget()
    {
        var index = _fixture.Index;
        var subject = index.Declarations.First(
            declaration => declaration.Kind == "function");

        // Every facet, not just the cheapest: callers and impact walk the call
        // graph, and impact walks it transitively, so measuring only `symbol`
        // would report a budget the expensive facets never had to meet.
        var facets = new (string Name, Action Run)[]
        {
            ("symbol", () => index.FindDeclarations(subject.Name)),
            ("callers", () => index.FindCallers(subject.SymbolId)),
            ("callees", () => index.FindCallees(subject.SymbolId)),
            ("impact", () => index.FindImpactOfDeclarations([subject.SymbolId])),
            ("contracts", () => index.FindContracts(subject.SymbolId)),
            ("assumptions", () => index.FindAssumptions(subject.SymbolId, subject.File)),
        };

        foreach (var (name, run) in facets)
        {
            run(); // warm

            var stopwatch = Stopwatch.StartNew();
            run();
            stopwatch.Stop();

            var milliseconds = stopwatch.Elapsed.TotalMilliseconds;
            _output.WriteLine(
                $"warm query {name}: {milliseconds:F1}ms "
                    + $"(budget {WarmQueryBudgetMilliseconds}ms)");
            Assert.True(
                milliseconds <= WarmQueryBudgetMilliseconds,
                $"warm '{name}' query took {milliseconds:F1}ms, over the "
                    + $"{WarmQueryBudgetMilliseconds}ms budget");
        }
    }
}
