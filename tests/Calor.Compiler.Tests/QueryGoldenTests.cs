using System.Text.Json;
using Calor.Compiler.Effects;
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
        bool Partial,
        string? Row);

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

        if (golden.Facet == "impact-file")
        {
            // Whole-file impact: the subject is a file, kept for "I rewrote this".
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

        if (golden.Facet is "contracts" or "assumptions")
        {
            var owners = index.FindDeclarations(golden.Name);
            var owner = golden.InFile == null
                ? Assert.Single(owners)
                : Assert.Single(owners.Where(
                    declaration => declaration.File == golden.InFile));

            var rendered = golden.Facet == "contracts"
                ? index.FindContracts(owner.SymbolId)
                    .Select(contract => $"{contract.File}:{contract.Line}:{contract.Kind}")
                : index.FindAssumptions(owner.SymbolId, owner.File)
                    .Select(assumption =>
                        $"{assumption.File}:{assumption.Line}:{assumption.Scope}");

            Assert.Equal(
                golden.Expect.OrderBy(entry => entry, StringComparer.Ordinal).ToArray(),
                rendered.OrderBy(entry => entry, StringComparer.Ordinal).ToArray());
            return;
        }

        if (golden.Facet == "effects")
        {
            // v0.15 E5, gate 7 (roadmap §4.4; design §8.6/§13.3). The answer is
            // the enforcement pass's own per-declaration result as the index
            // recorded it — declared row, inferred row, verdict, the code that
            // fires — plus the rows of the positions the declaration owns.
            // Authored from the fixture, not recorded: alter one expected
            // answer and this fails (the gate's discriminating pin).
            var owners = index.FindDeclarations(golden.Name);
            var owner = golden.InFile == null
                ? Assert.Single(owners)
                : Assert.Single(owners.Where(
                    declaration => declaration.File == golden.InFile));

            var rendered = index.FindEffectRows(owner.SymbolId)
                .Select(row => row.OwnerSymbolId.Length == 0 && row.Kind is not ("parameter" or "return")
                    ? $"{row.File}:{row.Line}:{row.Name}:written={(row.Declared ? "true" : "false")};declared={row.DeclaredRow.Display};"
                        + $"inferred={row.InferredRow?.Display ?? "none"};verdict={row.Verdict};"
                        + $"code={row.DiagnosticCode ?? "none"};undeclared={string.Join(",", row.Forbidden)}"
                    : $"{row.File}:{row.Line}:{row.Name}:position={row.Kind};"
                        + $"declared={row.DeclaredRow.Display};bound={row.BoundRow ?? "none"}");

            Assert.Equal(
                golden.Expect.OrderBy(entry => entry, StringComparer.Ordinal).ToArray(),
                rendered.OrderBy(entry => entry, StringComparer.Ordinal).ToArray());
            Assert.Equal(golden.Partial, index.EffectsAnswerIsPartial(owner.SymbolId, owner.File));
            return;
        }

        if (golden.Facet == "impact-effects")
        {
            // v0.15 E5 — blast radius: FindImpactOfDeclarations' closure, unchanged,
            // joined with the verdict of fitting the hypothetical row into each
            // affected caller's DECLARED row.
            Assert.NotNull(golden.Row);
            var subjectDeclarations = index.FindDeclarations(golden.Name);
            var target = golden.InFile == null
                ? Assert.Single(subjectDeclarations)
                : Assert.Single(subjectDeclarations.Where(
                    declaration => declaration.File == golden.InFile));
            var codes = golden.Row!.Split(
                ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var impacts = index.FindEffectImpact(target.SymbolId, EffectSet.From(codes).ToRow());
            Assert.Equal(
                golden.Expect.OrderBy(entry => entry, StringComparer.Ordinal).ToArray(),
                impacts
                    .Select(impact =>
                        $"{impact.Declaration.File}:{impact.Declaration.Line}:{impact.Declaration.Name}:"
                            + ProjectIndexBuilder.VerdictText(impact.Verdict))
                    .OrderBy(entry => entry, StringComparer.Ordinal)
                    .ToArray());
            Assert.Equal(golden.Partial, index.ImpactAnswerIsPartial());
            return;
        }

        if (golden.Facet == "impact")
        {
            // Declaration-seeded impact — the default, and the reason the
            // granularity was changed: file seeding answered exactly right for
            // 1% of functions on a real corpus.
            var subjectDeclarations = index.FindDeclarations(golden.Name);
            var target = golden.InFile == null
                ? Assert.Single(subjectDeclarations)
                : Assert.Single(subjectDeclarations.Where(
                    declaration => declaration.File == golden.InFile));
            var impacted = index.FindImpactOfDeclarations([target.SymbolId]);
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

    /// <summary>
    /// Gate 7's anti-vacuity: the effects leg must contain each of the three
    /// verdicts and a firing code, or an index that answered "fits" for
    /// everything would pass the effects goldens that happen to fit.
    /// </summary>
    [Fact]
    public void TheEffectsGoldensExerciseEveryVerdict()
    {
        var effects = LoadGoldens().Where(golden => golden.Facet == "effects").ToArray();
        Assert.Contains(effects, golden => golden.Expect.Any(entry => entry.Contains("verdict=fits", StringComparison.Ordinal)));
        Assert.Contains(effects, golden => golden.Expect.Any(entry => entry.Contains("verdict=does-not-fit", StringComparison.Ordinal)));
        Assert.Contains(effects, golden => golden.Expect.Any(entry => entry.Contains("verdict=cannot-tell", StringComparison.Ordinal)));
        Assert.Contains(effects, golden => golden.Expect.Any(entry => entry.Contains("code=Calor0410", StringComparison.Ordinal)));
        Assert.Contains(effects, golden => golden.Expect.Any(entry => entry.Contains("written=false", StringComparison.Ordinal)));
        Assert.Contains(effects, golden => golden.Expect.Any(entry => entry.Contains("position=parameter", StringComparison.Ordinal)));
        // The inferred row of a polymorphic body carries its variable part.
        Assert.Contains(effects, golden => golden.Expect.Any(entry => entry.Contains(";inferred=e;", StringComparison.Ordinal)));

        var blast = LoadGoldens().Where(golden => golden.Facet == "impact-effects").ToArray();
        Assert.Contains(blast, golden => golden.Expect.Any(entry => entry.EndsWith(":does-not-fit", StringComparison.Ordinal)));
        Assert.Contains(blast, golden => golden.Expect.Any(entry => entry.EndsWith(":fits", StringComparison.Ordinal)));
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
                entry.GetProperty("partial").GetBoolean(),
                entry.TryGetProperty("row", out var row) && row.ValueKind == JsonValueKind.String
                    ? row.GetString()
                    : null))
            .ToArray();
    }

    private static string CorpusRoot =>
        Path.Combine(CliTestHarness.FindRepoRoot(), "tests", "TestData", "QueryCorpus");

    private static string FixtureRoot => Path.Combine(CorpusRoot, "project");
}
