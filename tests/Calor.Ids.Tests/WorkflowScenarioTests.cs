using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Ids.Tests;

/// <summary>Identity survives source-only edits without rewriting source IDs.</summary>
public class WorkflowScenarioTests : IDisposable
{
    private readonly string _directory = Path.GetFullPath(Path.Combine(
        ".calor-test-workspaces", $"ids-workflow-{Guid.NewGuid():N}"));

    public WorkflowScenarioTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string source)
    {
        var path = Path.Combine(_directory, "active.calr");
        File.WriteAllText(path, source);
        return path;
    }

    [Fact]
    public void Sync_AssignsOnlySidecarIdentities()
    {
        const string source = "§M{Module}\n  §F{Calculate:pub} () -> i32\n    §R 1\n";
        var file = Write(source);
        var store = new DeclarationIdentityStore(_directory);
        var first = store.Sync([file]);
        var second = store.Sync();

        Assert.Equal(source, File.ReadAllText(file));
        Assert.Equal(first.Declarations.ToArray(), second.Declarations.ToArray());
        Assert.Equal(2, first.Declarations.Count);
        Assert.All(first.Declarations, declaration => Assert.False(string.IsNullOrEmpty(declaration.Id)));
        Assert.Equal(2, first.Declarations.Select(declaration => declaration.Id).Distinct().Count());
    }

    [Fact]
    public void Sync_RenamePreservesIdentity()
    {
        var file = Write("§M{Module}\n  §F{OldName:pub} () -> i32\n    §R 1\n");
        var store = new DeclarationIdentityStore(_directory);
        var before = store.Sync([file]).Declarations.Single(d => d.Kind == IdKind.Function);
        var renamed = File.ReadAllText(file).Replace("OldName", "NewName", StringComparison.Ordinal);
        File.WriteAllText(file, renamed);
        var after = store.Sync().Declarations.Single(d => d.Kind == IdKind.Function);

        Assert.Equal(before.Id, after.Id);
        Assert.Equal("NewName", after.Name);
        Assert.Equal(renamed, File.ReadAllText(file));
    }

    [Fact]
    public void Sync_AmbiguousRenamesLeaveSidecarAndSourceUntouched()
    {
        const string source = "§M{Module}\n  §F{A:pub} () -> void\n  §F{B:pub} () -> void\n";
        var file = Write(source);
        var store = new DeclarationIdentityStore(_directory);
        store.Sync([file]);
        var sidecar = File.ReadAllText(store.SidecarPath);
        var changed = source.Replace("{A:", "{C:", StringComparison.Ordinal)
            .Replace("{B:", "{D:", StringComparison.Ordinal);
        File.WriteAllText(file, changed);

        Assert.ThrowsAny<InvalidOperationException>(() => store.Sync());
        Assert.Equal(sidecar, File.ReadAllText(store.SidecarPath));
        Assert.Equal(changed, File.ReadAllText(file));
    }

    [Fact]
    public void Sync_DoesNotDiscoverHistoricalFiles()
    {
        var file = Write("§M{Module}\n");
        var historical = Path.Combine(_directory, "historical.calr");
        File.WriteAllText(historical, "frozen historical input, not active source");
        var state = new DeclarationIdentityStore(_directory).Sync([file]);

        Assert.Equal(["active.calr"], state.Files);
        Assert.Equal("frozen historical input, not active source", File.ReadAllText(historical));
    }
}
