// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): Set / Get / CountKeys / Compact.
using Xunit;

namespace KvJournal.HeldOut;

public sealed class KvJournalHeldOutTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"kvj-{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    // --- Set (existing behavior must be preserved) ---

    [Fact]
    public void Set_AppendsKeyValueLine()
    {
        TestShim.Set(_path, "k", "v");
        Assert.Equal("k=v\n", File.ReadAllText(_path));
    }

    // --- Get ---

    [Fact]
    public void Get_MissingFile_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TestShim.Get(_path, "k"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsEmpty()
    {
        TestShim.Set(_path, "other", "1");
        Assert.Equal(string.Empty, TestShim.Get(_path, "k"));
    }

    [Fact]
    public void Get_SingleEntry()
    {
        TestShim.Set(_path, "k", "hello");
        Assert.Equal("hello", TestShim.Get(_path, "k"));
    }

    [Fact]
    public void Get_LastWriteWins()
    {
        TestShim.Set(_path, "k", "1");
        TestShim.Set(_path, "k", "2");
        TestShim.Set(_path, "k", "3");
        Assert.Equal("3", TestShim.Get(_path, "k"));
    }

    [Fact]
    public void Get_ValueContainingEquals_SplitsAtFirstSeparator()
    {
        TestShim.Set(_path, "k", "a=b");
        Assert.Equal("a=b", TestShim.Get(_path, "k"));
    }

    [Fact]
    public void Get_KeyThatPrefixesAnotherKey_NoCollision()
    {
        TestShim.Set(_path, "ab", "1");
        TestShim.Set(_path, "abc", "2");
        Assert.Equal("1", TestShim.Get(_path, "ab"));
        Assert.Equal("2", TestShim.Get(_path, "abc"));
    }

    [Fact]
    public void Get_EmptyValue()
    {
        TestShim.Set(_path, "k", "filled");
        TestShim.Set(_path, "k", "");
        Assert.Equal(string.Empty, TestShim.Get(_path, "k"));
    }

    // --- CountKeys ---

    [Fact]
    public void CountKeys_MissingFile_Zero()
    {
        Assert.Equal(0, TestShim.CountKeys(_path));
    }

    [Fact]
    public void CountKeys_DuplicateKeysCountOnce()
    {
        TestShim.Set(_path, "a", "1");
        TestShim.Set(_path, "a", "2");
        TestShim.Set(_path, "b", "3");
        Assert.Equal(2, TestShim.CountKeys(_path));
    }

    [Fact]
    public void CountKeys_AllDistinct()
    {
        TestShim.Set(_path, "a", "1");
        TestShim.Set(_path, "b", "2");
        TestShim.Set(_path, "c", "3");
        Assert.Equal(3, TestShim.CountKeys(_path));
    }

    // --- Compact ---

    [Fact]
    public void Compact_MissingFile_DoesNotCreateIt()
    {
        TestShim.Compact(_path);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Compact_KeepsLastValue_InFirstAppearanceOrder()
    {
        TestShim.Set(_path, "a", "1");
        TestShim.Set(_path, "b", "2");
        TestShim.Set(_path, "a", "3");
        TestShim.Compact(_path);
        Assert.Equal("a=3\nb=2\n", File.ReadAllText(_path));
    }

    [Fact]
    public void Compact_IsIdempotent()
    {
        TestShim.Set(_path, "x", "1");
        TestShim.Set(_path, "y", "2");
        TestShim.Set(_path, "x", "9");
        TestShim.Compact(_path);
        string once = File.ReadAllText(_path);
        TestShim.Compact(_path);
        Assert.Equal(once, File.ReadAllText(_path));
    }

    [Fact]
    public void Compact_ThenGetAndCountStillWork()
    {
        TestShim.Set(_path, "a", "1");
        TestShim.Set(_path, "b", "2");
        TestShim.Set(_path, "a", "3");
        TestShim.Compact(_path);
        Assert.Equal("3", TestShim.Get(_path, "a"));
        Assert.Equal("2", TestShim.Get(_path, "b"));
        Assert.Equal(2, TestShim.CountKeys(_path));
    }

    [Fact]
    public void Compact_PreservesValueContainingEquals()
    {
        TestShim.Set(_path, "k", "a=b");
        TestShim.Set(_path, "j", "1");
        TestShim.Compact(_path);
        Assert.Equal("k=a=b\nj=1\n", File.ReadAllText(_path));
    }

    [Fact]
    public void Compact_ThenSet_AppendsAgain()
    {
        TestShim.Set(_path, "a", "1");
        TestShim.Compact(_path);
        TestShim.Set(_path, "a", "2");
        Assert.Equal("2", TestShim.Get(_path, "a"));
        Assert.Equal(1, TestShim.CountKeys(_path));
    }
}
