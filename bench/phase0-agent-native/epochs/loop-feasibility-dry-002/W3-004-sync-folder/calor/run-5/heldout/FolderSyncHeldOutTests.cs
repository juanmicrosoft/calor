// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable). All I/O goes through the real
// filesystem in a per-test temp directory.
using Xunit;

namespace FolderSync.HeldOut;

public sealed class FolderSyncHeldOutTests : IDisposable
{
    private readonly string _dir;
    private readonly string _src;
    private readonly string _tgt;
    private readonly string _tomb;

    public FolderSyncHeldOutTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"syncf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _src = Path.Combine(_dir, "source.idx");
        _tgt = Path.Combine(_dir, "target.idx");
        _tomb = Path.Combine(_dir, "tombstones.idx");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // ---- preservation probes: pre-existing one-way behavior ----

    [Fact]
    public void WriteIndexEntry_MissingFile_CreatesSingleEntry()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        Assert.Equal("a|5\n", File.ReadAllText(_src));
    }

    [Fact]
    public void WriteIndexEntry_ExistingName_ReplacesInPlace_PreservingOrder()
    {
        TestShim.WriteIndexEntry(_src, "a", 1);
        TestShim.WriteIndexEntry(_src, "b", 2);
        TestShim.WriteIndexEntry(_src, "c", 3);
        TestShim.WriteIndexEntry(_src, "b", 20);
        Assert.Equal("a|1\nb|20\nc|3\n", File.ReadAllText(_src));
    }

    [Fact]
    public void RemoveIndexEntry_RemovesOnlyMatchingLine()
    {
        TestShim.WriteIndexEntry(_src, "a", 1);
        TestShim.WriteIndexEntry(_src, "b", 2);
        TestShim.WriteIndexEntry(_src, "c", 3);
        TestShim.RemoveIndexEntry(_src, "b");
        Assert.Equal("a|1\nc|3\n", File.ReadAllText(_src));
    }

    [Fact]
    public void RemoveIndexEntry_MissingFile_DoesNotCreateIt()
    {
        TestShim.RemoveIndexEntry(_src, "a");
        Assert.False(File.Exists(_src));
    }

    [Fact]
    public void RemoveIndexEntry_LastEntry_LeavesEmptyFile()
    {
        TestShim.WriteIndexEntry(_src, "a", 1);
        TestShim.RemoveIndexEntry(_src, "a");
        Assert.True(File.Exists(_src));
        Assert.Equal("", File.ReadAllText(_src));
    }

    [Fact]
    public void IndexStamp_And_IndexHas_Basics()
    {
        Assert.Equal(-1, TestShim.IndexStamp(_src, "a"));
        Assert.False(TestShim.IndexHas(_src, "a"));
        TestShim.WriteIndexEntry(_src, "a", 7);
        TestShim.WriteIndexEntry(_src, "ab", 1234567890);
        Assert.Equal(7, TestShim.IndexStamp(_src, "a"));
        Assert.Equal(1234567890, TestShim.IndexStamp(_src, "ab"));
        Assert.True(TestShim.IndexHas(_src, "a"));
        Assert.False(TestShim.IndexHas(_src, "abc"));
        Assert.Equal(-1, TestShim.IndexStamp(_src, "x"));
    }

    [Fact]
    public void CopyNewer_CopiesNewerAndMissing_ReturnsCount()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_src, "b", 3);
        TestShim.WriteIndexEntry(_tgt, "a", 4);
        TestShim.WriteIndexEntry(_tgt, "c", 9);
        Assert.Equal(2, TestShim.CopyNewer(_src, _tgt));
        Assert.Equal("a|5\nc|9\nb|3\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void CopyNewer_EqualOrOlderStamps_NoAction()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_tgt, "a", 5);
        Assert.Equal(0, TestShim.CopyNewer(_src, _tgt));
        TestShim.WriteIndexEntry(_src, "a", 4);
        Assert.Equal(0, TestShim.CopyNewer(_src, _tgt));
        Assert.Equal("a|5\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void PruneOrphans_RemovesTargetOnlyEntries_ReturnsCount()
    {
        TestShim.WriteIndexEntry(_src, "a", 1);
        TestShim.WriteIndexEntry(_tgt, "b", 2);
        TestShim.WriteIndexEntry(_tgt, "a", 1);
        TestShim.WriteIndexEntry(_tgt, "c", 3);
        Assert.Equal(2, TestShim.PruneOrphans(_src, _tgt));
        Assert.Equal("a|1\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void DryRunReport_ListsCopiesThenPrunes_WithoutWriting()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_src, "b", 3);
        TestShim.WriteIndexEntry(_tgt, "a", 4);
        TestShim.WriteIndexEntry(_tgt, "c", 9);
        Assert.Equal("copy a\ncopy b\nprune c\n", TestShim.DryRunReport(_src, _tgt));
        Assert.Equal("a|5\nb|3\n", File.ReadAllText(_src));
        Assert.Equal("a|4\nc|9\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void DryRunReport_WhenInSync_ReturnsEmpty()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_tgt, "a", 5);
        Assert.Equal("", TestShim.DryRunReport(_src, _tgt));
    }

    [Fact]
    public void Sync_CopiesAndPrunes_ReturnsTotal()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_src, "b", 3);
        TestShim.WriteIndexEntry(_tgt, "a", 4);
        TestShim.WriteIndexEntry(_tgt, "c", 9);
        Assert.Equal(3, TestShim.Sync(_src, _tgt));
        Assert.Equal("a|5\nb|3\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void Sync_MissingSource_PrunesEverything()
    {
        TestShim.WriteIndexEntry(_tgt, "a", 1);
        TestShim.WriteIndexEntry(_tgt, "b", 2);
        Assert.Equal(2, TestShim.Sync(_src, _tgt));
        Assert.Equal("", File.ReadAllText(_tgt));
    }

    // ---- new operations ----

    [Fact]
    public void RecordDelete_Upserts_OnlyWhenStrictlyNewer()
    {
        TestShim.RecordDelete(_tomb, "a", 5);
        Assert.Equal("a|5\n", File.ReadAllText(_tomb));
        TestShim.RecordDelete(_tomb, "a", 3);
        Assert.Equal("a|5\n", File.ReadAllText(_tomb));
        TestShim.RecordDelete(_tomb, "a", 5);
        Assert.Equal("a|5\n", File.ReadAllText(_tomb));
        TestShim.RecordDelete(_tomb, "a", 9);
        Assert.Equal("a|9\n", File.ReadAllText(_tomb));
        TestShim.RecordDelete(_tomb, "b", 1);
        Assert.Equal("a|9\nb|1\n", File.ReadAllText(_tomb));
    }

    [Fact]
    public void SyncTwoWay_NewerWins_BothDirections()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_src, "b", 3);
        TestShim.WriteIndexEntry(_tgt, "a", 4);
        TestShim.WriteIndexEntry(_tgt, "c", 7);
        Assert.Equal(3, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal("a|5\nb|3\nc|7\n", File.ReadAllText(_src));
        Assert.Equal("a|5\nc|7\nb|3\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void SyncTwoWay_EqualStamps_NoAction()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_tgt, "a", 5);
        Assert.Equal(0, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal("a|5\n", File.ReadAllText(_src));
        Assert.Equal("a|5\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void SyncTwoWay_TombstoneEqualToNewest_DeletesBothSides()
    {
        TestShim.WriteIndexEntry(_src, "b", 3);
        TestShim.WriteIndexEntry(_tgt, "b", 3);
        TestShim.RecordDelete(_tomb, "b", 3);
        Assert.Equal(2, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal("", File.ReadAllText(_src));
        Assert.Equal("", File.ReadAllText(_tgt));
        Assert.Equal("b|3\n", File.ReadAllText(_tomb));
    }

    [Fact]
    public void SyncTwoWay_OlderTombstone_EntryWins()
    {
        TestShim.WriteIndexEntry(_src, "b", 4);
        TestShim.WriteIndexEntry(_tgt, "b", 1);
        TestShim.RecordDelete(_tomb, "b", 2);
        Assert.Equal(1, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal("b|4\n", File.ReadAllText(_src));
        Assert.Equal("b|4\n", File.ReadAllText(_tgt));
        Assert.Equal("b|2\n", File.ReadAllText(_tomb));
    }

    [Fact]
    public void SyncTwoWay_TargetOnly_TombstoneDeletes()
    {
        TestShim.WriteIndexEntry(_tgt, "x", 7);
        TestShim.RecordDelete(_tomb, "x", 9);
        Assert.Equal(1, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal("", File.ReadAllText(_tgt));
        Assert.False(File.Exists(_src));
    }

    [Fact]
    public void SyncTwoWay_TargetOnly_CopiedBackToSource()
    {
        TestShim.WriteIndexEntry(_src, "a", 1);
        TestShim.WriteIndexEntry(_tgt, "a", 1);
        TestShim.WriteIndexEntry(_tgt, "x", 7);
        Assert.Equal(1, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal("a|1\nx|7\n", File.ReadAllText(_src));
        Assert.Equal("a|1\nx|7\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void SyncWithPolicy_Off_IgnoresTombstones_MatchesOneWay()
    {
        TestShim.WriteIndexEntry(_src, "x", 5);
        TestShim.WriteIndexEntry(_tgt, "y", 1);
        TestShim.RecordDelete(_tomb, "x", 9);
        TestShim.RecordDelete(_tomb, "y", 9);
        Assert.Equal(2, TestShim.SyncWithPolicy(_src, _tgt, _tomb, false));
        Assert.Equal("x|5\n", File.ReadAllText(_src));
        Assert.Equal("x|5\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void SyncWithPolicy_On_AppliesTombstonePolicy()
    {
        TestShim.WriteIndexEntry(_src, "x", 5);
        TestShim.WriteIndexEntry(_tgt, "y", 1);
        TestShim.RecordDelete(_tomb, "x", 9);
        TestShim.RecordDelete(_tomb, "y", 9);
        Assert.Equal(2, TestShim.SyncWithPolicy(_src, _tgt, _tomb, true));
        Assert.Equal("", File.ReadAllText(_src));
        Assert.Equal("", File.ReadAllText(_tgt));
        Assert.Equal("x|9\ny|9\n", File.ReadAllText(_tomb));
    }

    [Fact]
    public void MixedSequence_TwoWayThenOneWay_PreservesIndexInvariants()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_src, "b", 3);
        TestShim.WriteIndexEntry(_tgt, "a", 4);
        TestShim.WriteIndexEntry(_tgt, "c", 7);
        Assert.Equal(3, TestShim.SyncTwoWay(_src, _tgt, _tomb));

        TestShim.RecordDelete(_tomb, "b", 9);
        Assert.Equal(2, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal("a|5\nc|7\n", File.ReadAllText(_src));
        Assert.Equal("a|5\nc|7\n", File.ReadAllText(_tgt));

        Assert.Equal("", TestShim.DryRunReport(_src, _tgt));

        TestShim.WriteIndexEntry(_tgt, "z", 1);
        Assert.Equal(1, TestShim.Sync(_src, _tgt));
        Assert.Equal("a|5\nc|7\n", File.ReadAllText(_tgt));
    }

    [Fact]
    public void SyncTwoWay_ConvergedState_IsIdempotent()
    {
        TestShim.WriteIndexEntry(_src, "a", 5);
        TestShim.WriteIndexEntry(_tgt, "b", 8);
        TestShim.RecordDelete(_tomb, "d", 4);
        Assert.Equal(2, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal(0, TestShim.SyncTwoWay(_src, _tgt, _tomb));
        Assert.Equal("a|5\nb|8\n", File.ReadAllText(_src));
        Assert.Equal("b|8\na|5\n", File.ReadAllText(_tgt));
        Assert.Equal("d|4\n", File.ReadAllText(_tomb));
    }
}
