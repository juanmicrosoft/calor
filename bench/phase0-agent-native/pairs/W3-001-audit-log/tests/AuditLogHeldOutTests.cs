// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): Append / CountEntries / LastEntry.
using Xunit;

namespace AuditLog.HeldOut;

public sealed class AuditLogHeldOutTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"audit-{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Append_CreatesFileAndAddsLine()
    {
        TestShim.Append(_path, "first");
        Assert.True(File.Exists(_path));
        Assert.Equal("first\n", File.ReadAllText(_path));
    }

    [Fact]
    public void CountEntries_MissingFile_ReturnsZero()
    {
        Assert.Equal(0, TestShim.CountEntries(_path));
    }

    [Fact]
    public void CountEntries_CountsAppendedEntries()
    {
        TestShim.Append(_path, "a");
        TestShim.Append(_path, "b");
        TestShim.Append(_path, "c");
        Assert.Equal(3, TestShim.CountEntries(_path));
    }

    [Fact]
    public void LastEntry_MissingFile_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TestShim.LastEntry(_path));
    }

    [Fact]
    public void LastEntry_ReturnsMostRecent_WithoutNewline()
    {
        TestShim.Append(_path, "older");
        TestShim.Append(_path, "newest");
        Assert.Equal("newest", TestShim.LastEntry(_path));
    }

    [Fact]
    public void Append_DoesNotDisturbExistingEntries()
    {
        TestShim.Append(_path, "one");
        TestShim.Append(_path, "two");
        Assert.Equal(2, TestShim.CountEntries(_path));
        Assert.Equal("one\ntwo\n", File.ReadAllText(_path));
    }
}
