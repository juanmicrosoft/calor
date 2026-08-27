using Calor.Compiler.Commands;
using Calor.Compiler.Indexing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// The query surface's refusals. The golden corpus (QueryGoldenTests) pins that
/// answers are CORRECT; this pins that the command declines to answer when it
/// cannot answer honestly — which is the other half of the claim.
/// </summary>
public sealed class QueryCommandTests : IDisposable
{
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private string Fixture()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "calor-qcmd-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);

        var corpus = Path.Combine(
            CliTestHarness.FindRepoRoot(), "tests", "TestData", "QueryCorpus", "project");
        foreach (var source in Directory.GetFiles(corpus, "*.calr"))
            File.Copy(source, Path.Combine(dir, Path.GetFileName(source)));
        return dir;
    }

    [Fact]
    public void StaleIndexIsRefusedRatherThanAnswered()
    {
        // The discipline the whole slice rests on: an index whose inputs no
        // longer match may never answer. Rebuilding is allowed; answering from
        // the mismatch is not.
        var dir = Fixture();
        var output = IndexCommand.DefaultOutputDirectory(dir);
        var options = new ProjectIndexBuilder.Options(
            dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir));
        ProjectIndexBuilder.Build(options).Save(output);

        File.AppendAllText(Path.Combine(dir, "app.calr"), "\n");

        var (loaded, _) = ProjectIndex.Load(output);
        var inputs = ProjectIndexBuilder.CurrentInputs(
            new ProjectIndexBuilder.Options(
                dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir)));
        Assert.NotNull(loaded);
        Assert.NotEqual(
            ProjectIndex.Freshness.Fresh,
            loaded!.CheckFreshness(
                inputs.CompilerHash, inputs.OptionsHash, inputs.ManifestHash, inputs.Files));
    }

    [Fact]
    public void RebuildingMakesAStaleIndexAnswerableAgain()
    {
        var dir = Fixture();
        var options = new ProjectIndexBuilder.Options(
            dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir));
        var output = IndexCommand.DefaultOutputDirectory(dir);
        ProjectIndexBuilder.Build(options).Save(output);

        File.AppendAllText(Path.Combine(dir, "app.calr"), "\n");
        var refreshed = ProjectIndexBuilder.Build(new ProjectIndexBuilder.Options(
            dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir)));
        refreshed.Save(output);

        var (loaded, _) = ProjectIndex.Load(output);
        var inputs = ProjectIndexBuilder.CurrentInputs(
            new ProjectIndexBuilder.Options(
                dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir)));
        Assert.Equal(
            ProjectIndex.Freshness.Fresh,
            loaded!.CheckFreshness(
                inputs.CompilerHash, inputs.OptionsHash, inputs.ManifestHash, inputs.Files));
    }

    [Fact]
    public void AmbiguousSubjectHasSeveralDeclarationsSoTheCallerMustChoose()
    {
        // `Shared` is declared twice. FindDeclarations returns both rather than
        // picking one — the command turns that into a refusal with the choices
        // listed, instead of answering about whichever came first.
        var dir = Fixture();
        var index = ProjectIndexBuilder.Build(new ProjectIndexBuilder.Options(
            dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir)));

        var declarations = index.FindDeclarations("Shared");
        Assert.Equal(2, declarations.Count);
        Assert.Equal(
            new[] { "ambiguous.calr", "ambiguous2.calr" },
            declarations.Select(declaration => declaration.File)
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// v0.15 E5 — <c>calor query effects</c> through the CLI: the text answer
    /// carries the declared row, the inferred row, the verdict and the code, in
    /// the same shape as the other facets (lines, then a <c>query:</c> summary),
    /// and <c>--json</c> carries the same rows under the envelope's <c>data</c>.
    /// </summary>
    [Fact]
    public void EffectsFacet_AnswersWithDeclaredInferredAndVerdict()
    {
        var dir = Fixture();
        var text = CliTestHarness.RunCli(dir, "query", "effects", "Leaky", "--project", dir);
        Assert.True(text.ExitCode == 0, text.StdOut + text.StdErr);
        var lines = text.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
        Assert.Equal("  effects.calr:5:11 function Leaky", lines[0]);
        Assert.Equal("    declared: [pure]", lines[1]);
        Assert.Equal("    inferred: cw", lines[2]);
        Assert.Equal("    verdict:  does not fit — Calor0410 fires (undeclared: cw)", lines[3]);
        Assert.Equal(
            "query: effect row of effects.calr:5:11 function Leaky — declared [pure], inferred cw, does not fit — Calor0410 fires (undeclared: cw)",
            lines[4]);

        var json = CliTestHarness.RunCli(dir, "query", "effects", "Leaky", "--project", dir, "--json");
        Assert.True(json.ExitCode == 0, json.StdOut + json.StdErr);
        using var envelope = System.Text.Json.JsonDocument.Parse(json.StdOut);
        Assert.Equal("query", envelope.RootElement.GetProperty("command").GetString());
        var row = Assert.Single(envelope.RootElement.GetProperty("data").GetProperty("rows").EnumerateArray());
        Assert.Equal("does-not-fit", row.GetProperty("verdict").GetString());
        Assert.Equal("Calor0410", row.GetProperty("diagnosticCode").GetString());
        Assert.Equal("cw", row.GetProperty("inferredRow").GetProperty("display").GetString());
    }

    /// <summary>
    /// v0.15 E5 — <c>calor query impact --effects --row</c>: the impact closure,
    /// with each affected caller's declared row and whether the hypothetical row
    /// still fits it.
    /// </summary>
    [Fact]
    public void ImpactEffects_ListsTheCallersWhoseDeclaredRowsStopFitting()
    {
        var dir = Fixture();
        var run = CliTestHarness.RunCli(dir, "query", "impact", "Log", "--effects", "--row", "fs:w", "--project", dir);
        Assert.True(run.ExitCode == 0, run.StdOut + run.StdErr);
        Assert.Contains("  effects.calr:5:11 function Leaky — declares [pure]: does-not-fit", run.StdOut);
        Assert.Contains("  effects.calr:11:11 function Fan — declares cw: does-not-fit", run.StdOut);
        Assert.Contains(
            "impact: 3 of 3 affected declaration(s) would stop fitting a row of fs:w on effects.calr:2:11 function Log",
            run.StdOut);

        var current = CliTestHarness.RunCli(dir, "query", "impact", "Log", "--effects", "--project", dir);
        Assert.True(current.ExitCode == 0, current.StdOut + current.StdErr);
        Assert.Contains("impact: 1 of 3 affected declaration(s) would stop fitting a row of cw (its current declared row)", current.StdOut);
    }

    [Fact]
    public void QueryingAnUnknownNameFindsNothing()
    {
        var dir = Fixture();
        var index = ProjectIndexBuilder.Build(new ProjectIndexBuilder.Options(
            dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir)));
        Assert.Empty(index.FindDeclarations("NoSuchName"));
    }
}
