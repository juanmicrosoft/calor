using System.Text.Json;
using Calor.Compiler.Indexing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Index/query correctness gate (roadmap §2.5 gate 3).
///
/// Gate 2's identity leg would pass an identically-wrong index — full and
/// incremental agreeing on the same wrong answer is still agreement. This gate
/// is the correctness anchor: a golden corpus whose answers were authored by
/// reading the fixture, not recorded from the implementation.
///
/// Corpus: tests/TestData/QueryCorpus/.
/// </summary>
public sealed class QueryGoldenTests : IDisposable
{
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private sealed record GoldenQuery(
        string Why,
        string Facet,
        string Name,
        string? InFile,
        string[] Expect,
        bool Partial);

    public static TheoryData<int, string> Goldens()
    {
        var data = new TheoryData<int, string>();
        var queries = LoadGoldens();
        for (var index = 0; index < queries.Count; index++)
            data.Add(index, $"{queries[index].Facet}:{queries[index].Name}");
        return data;
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public void QueryAnswersMatchGroundTruth(int position, string label)
    {
        var golden = LoadGoldens()[position];
        var index = BuildFixtureIndex();

        if (golden.Facet == "impact")
        {
            // The subject is a FILE, not a declaration name.
            var impacted = index.FindImpactOfFile(golden.Name);
            Assert.Equal(
                golden.Expect.OrderBy(entry => entry, StringComparer.Ordinal).ToArray(),
                impacted
                    .Select(declaration =>
                        $"{declaration.File}:{declaration.Line}:{declaration.Name}")
                    .OrderBy(entry => entry, StringComparer.Ordinal)
                    .ToArray());
            Assert.Equal(golden.Partial, index.ImpactAnswerIsPartial());
            return;
        }

        var declarations = index.FindDeclarations(golden.Name);
        Assert.True(
            declarations.Count > 0,
            $"{label}: no declaration named '{golden.Name}' in the fixture");

        IReadOnlyList<IndexedDeclaration> answer;
        bool partial;
        if (golden.Facet == "symbol")
        {
            answer = declarations;
            partial = index.DeclarationLookupIsPartial();
        }
        else
        {
            var subject = golden.InFile == null
                ? Assert.Single(declarations)
                : Assert.Single(declarations.Where(
                    declaration => declaration.File == golden.InFile));

            answer = golden.Facet switch
            {
                "callers" => index.FindCallers(subject.SymbolId),
                "callees" => index.FindCallees(subject.SymbolId),
                _ => throw new InvalidOperationException($"unknown facet {golden.Facet}"),
            };
            partial = golden.Facet == "callers"
                ? index.CallersAnswerIsPartial(subject.Name)
                : index.CalleesAnswerIsPartial(subject.SymbolId);
        }

        Assert.Equal(
            golden.Expect.OrderBy(entry => entry, StringComparer.Ordinal).ToArray(),
            answer
                .Select(declaration =>
                    $"{declaration.File}:{declaration.Line}:{declaration.Name}")
                .OrderBy(entry => entry, StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(golden.Partial, partial);
    }

    [Fact]
    public void EveryGoldenStatesWhyItExists()
    {
        // A golden without a stated reason is a recording of current behaviour,
        // which is what this gate exists to not be.
        foreach (var golden in LoadGoldens())
            Assert.False(string.IsNullOrWhiteSpace(golden.Why));
    }

    [Fact]
    public void TheCorpusExercisesPartialAnswers()
    {
        // Anti-vacuity: a corpus of only clean answers would pass against an
        // index that never reports a residual at all.
        var goldens = LoadGoldens();
        Assert.Contains(goldens, golden => golden.Partial);
        Assert.Contains(goldens, golden => !golden.Partial);
        Assert.Contains(goldens, golden => golden.Expect.Length == 0);
    }

    private ProjectIndex BuildFixtureIndex()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(), "calor-query-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(workspace);
        _dirs.Add(workspace);

        foreach (var source in Directory.GetFiles(FixtureRoot, "*.calr"))
            File.Copy(source, Path.Combine(workspace, Path.GetFileName(source)));

        return ProjectIndexBuilder.Build(new ProjectIndexBuilder.Options(
            workspace, "query-gate", ProjectIndexBuilder.DiscoverSources(workspace)));
    }

    private static IReadOnlyList<GoldenQuery> LoadGoldens()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(CorpusRoot, "expected.json")));
        return document.RootElement.GetProperty("queries")
            .EnumerateArray()
            .Select(entry => new GoldenQuery(
                entry.GetProperty("why").GetString()!,
                entry.GetProperty("facet").GetString()!,
                entry.GetProperty("name").GetString()!,
                entry.TryGetProperty("inFile", out var file) && file.ValueKind != JsonValueKind.Null
                    ? file.GetString()
                    : null,
                entry.GetProperty("expect").EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToArray(),
                entry.GetProperty("partial").GetBoolean()))
            .ToArray();
    }

    private static string CorpusRoot =>
        Path.Combine(CliTestHarness.FindRepoRoot(), "tests", "TestData", "QueryCorpus");

    private static string FixtureRoot => Path.Combine(CorpusRoot, "project");
}
