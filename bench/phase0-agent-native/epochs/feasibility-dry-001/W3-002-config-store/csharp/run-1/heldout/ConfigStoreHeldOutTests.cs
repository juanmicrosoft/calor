// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): Get / Set / Has.
using Xunit;

namespace ConfigStore.HeldOut;

public sealed class ConfigStoreHeldOutTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"config-{Guid.NewGuid():N}.conf");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Get_MissingFile_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TestShim.Get(_path, "host"));
    }

    [Fact]
    public void Set_MissingFile_CreatesFileWithSingleEntry()
    {
        TestShim.Set(_path, "host", "localhost");
        Assert.True(File.Exists(_path));
        Assert.Equal("host=localhost\n", File.ReadAllText(_path));
    }

    [Fact]
    public void Set_ThenGet_RoundTrips()
    {
        TestShim.Set(_path, "host", "localhost");
        TestShim.Set(_path, "port", "8080");
        Assert.Equal("localhost", TestShim.Get(_path, "host"));
        Assert.Equal("8080", TestShim.Get(_path, "port"));
    }

    [Fact]
    public void Set_ExistingKey_ReplacesInPlace_PreservingOrder()
    {
        TestShim.Set(_path, "a", "1");
        TestShim.Set(_path, "b", "2");
        TestShim.Set(_path, "c", "3");
        TestShim.Set(_path, "b", "20");
        Assert.Equal("a=1\nb=20\nc=3\n", File.ReadAllText(_path));
    }

    [Fact]
    public void Set_NewKey_AppendsAsLastLine()
    {
        TestShim.Set(_path, "a", "1");
        TestShim.Set(_path, "b", "2");
        Assert.Equal("a=1\nb=2\n", File.ReadAllText(_path));
    }

    [Fact]
    public void Set_EmptyValue_Persists()
    {
        TestShim.Set(_path, "flag", "");
        Assert.Equal("flag=\n", File.ReadAllText(_path));
        Assert.Equal(string.Empty, TestShim.Get(_path, "flag"));
    }

    [Fact]
    public void Has_MissingFile_ReturnsFalse()
    {
        Assert.False(TestShim.Has(_path, "host"));
    }

    [Fact]
    public void Has_PresentAndAbsentKeys()
    {
        TestShim.Set(_path, "host", "localhost");
        Assert.True(TestShim.Has(_path, "host"));
        Assert.False(TestShim.Has(_path, "port"));
    }

    [Fact]
    public void Has_DistinguishesEmptyValueFromAbsentKey()
    {
        TestShim.Set(_path, "flag", "");
        Assert.True(TestShim.Has(_path, "flag"));
        Assert.False(TestShim.Has(_path, "other"));
    }

    [Fact]
    public void Get_FirstMatchingLineWins()
    {
        File.WriteAllText(_path, "k=first\nk=second\n");
        Assert.Equal("first", TestShim.Get(_path, "k"));
    }
}
