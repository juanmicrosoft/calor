using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for <see cref="ExperimentalFlags"/> — the primitive used by Phase 0
/// of the Calor-native type-system research plan to gate experimental features.
/// </summary>
public class ExperimentalFlagsTests
{
    [Fact]
    public void None_HasZeroFlags()
    {
        Assert.Equal(0, ExperimentalFlags.None.Count);
        Assert.False(ExperimentalFlags.None.IsEnabled("anything"));
    }

    [Fact]
    public void Enable_MakesFlagReportEnabled()
    {
        var flags = new ExperimentalFlags();
        flags.Enable("my-flag");
        Assert.True(flags.IsEnabled("my-flag"));
    }

    [Fact]
    public void Enable_IsIdempotent()
    {
        var flags = new ExperimentalFlags();
        flags.Enable("my-flag");
        flags.Enable("my-flag");
        Assert.Equal(1, flags.Count);
    }

    [Fact]
    public void IsEnabled_IsCaseInsensitive()
    {
        var flags = new ExperimentalFlags();
        flags.Enable("MyFlag");
        Assert.True(flags.IsEnabled("myflag"));
        Assert.True(flags.IsEnabled("MYFLAG"));
        Assert.True(flags.IsEnabled("MyFlag"));
    }

    [Fact]
    public void Enable_TrimsWhitespace()
    {
        var flags = new ExperimentalFlags();
        flags.Enable("  spaced-flag  ");
        Assert.True(flags.IsEnabled("spaced-flag"));
    }

    [Fact]
    public void Enable_NullOrWhitespace_NoOp()
    {
        var flags = new ExperimentalFlags();
        flags.Enable(null);
        flags.Enable("");
        flags.Enable("   ");
        Assert.Equal(0, flags.Count);
    }

    [Fact]
    public void Constructor_FromEnumerable_EnablesAll()
    {
        var flags = new ExperimentalFlags(new[] { "a", "b", "c" });
        Assert.Equal(3, flags.Count);
        Assert.True(flags.IsEnabled("a"));
        Assert.True(flags.IsEnabled("b"));
        Assert.True(flags.IsEnabled("c"));
    }

    [Fact]
    public void Parse_SemicolonDelimited_EnablesAll()
    {
        var flags = ExperimentalFlags.Parse("flag-1;flag-2;flag-3");
        Assert.Equal(3, flags.Count);
        Assert.True(flags.IsEnabled("flag-1"));
        Assert.True(flags.IsEnabled("flag-3"));
    }

    [Fact]
    public void Parse_CommaDelimited_EnablesAll()
    {
        var flags = ExperimentalFlags.Parse("a,b,c");
        Assert.Equal(3, flags.Count);
    }

    [Fact]
    public void Parse_MixedDelimitersAndSpaces_Works()
    {
        var flags = ExperimentalFlags.Parse(" a ; b , c ; ");
        Assert.Equal(3, flags.Count);
        Assert.True(flags.IsEnabled("a"));
        Assert.True(flags.IsEnabled("b"));
        Assert.True(flags.IsEnabled("c"));
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(0, ExperimentalFlags.Parse(null).Count);
        Assert.Equal(0, ExperimentalFlags.Parse("").Count);
        Assert.Equal(0, ExperimentalFlags.Parse("   ").Count);
    }

    [Fact]
    public void Parse_EmptySegmentsSkipped()
    {
        var flags = ExperimentalFlags.Parse("a;;b;;");
        Assert.Equal(2, flags.Count);
        Assert.True(flags.IsEnabled("a"));
        Assert.True(flags.IsEnabled("b"));
    }

    [Fact]
    public void EnabledFlags_EnumeratesOnlySetFlags()
    {
        var flags = new ExperimentalFlags();
        flags.Enable("first");
        flags.Enable("second");
        var names = flags.EnabledFlags.ToList();
        Assert.Equal(2, names.Count);
        Assert.Contains("first", names);
        Assert.Contains("second", names);
    }
}
