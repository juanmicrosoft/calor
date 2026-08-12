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

    [Fact]
    public void QueryingAnUnknownNameFindsNothing()
    {
        var dir = Fixture();
        var index = ProjectIndexBuilder.Build(new ProjectIndexBuilder.Options(
            dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir)));
        Assert.Empty(index.FindDeclarations("NoSuchName"));
    }
}
