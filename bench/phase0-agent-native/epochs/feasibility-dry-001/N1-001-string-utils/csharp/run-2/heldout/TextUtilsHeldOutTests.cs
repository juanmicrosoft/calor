// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): Slugify / Truncate / WordCount.
using Xunit;

namespace TextUtils.HeldOut;

public sealed class TextUtilsHeldOutTests
{
    [Fact]
    public void Slugify_LowercasesAndHyphenates()
    {
        Assert.Equal("hello-brave-world", TestShim.Slugify("Hello Brave World"));
    }

    [Fact]
    public void Truncate_ShorterThanMax_Unchanged()
    {
        Assert.Equal("hi", TestShim.Truncate("hi", 10));
    }

    [Fact]
    public void Truncate_ExactlyMax_Unchanged()
    {
        Assert.Equal("hello", TestShim.Truncate("hello", 5));
    }

    [Fact]
    public void Truncate_Longer_AddsEllipsis_AtExactLength()
    {
        Assert.Equal("hello w...", TestShim.Truncate("hello world plus", 10));
        Assert.Equal(10, TestShim.Truncate("hello world plus", 10).Length);
    }

    [Fact]
    public void Truncate_MaxLenThree_NoEllipsis()
    {
        Assert.Equal("hel", TestShim.Truncate("hello", 3));
    }

    [Fact]
    public void Truncate_MaxLenZero_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TestShim.Truncate("hello", 0));
    }

    [Fact]
    public void WordCount_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, TestShim.WordCount(""));
    }

    [Fact]
    public void WordCount_WhitespaceOnly_ReturnsZero()
    {
        Assert.Equal(0, TestShim.WordCount("   \t \n "));
    }

    [Fact]
    public void WordCount_SingleWord()
    {
        Assert.Equal(1, TestShim.WordCount("hello"));
    }

    [Fact]
    public void WordCount_MultipleWords_SpacesTabsNewlines()
    {
        Assert.Equal(4, TestShim.WordCount("one two\tthree\nfour"));
    }

    [Fact]
    public void WordCount_IgnoresLeadingTrailingAndRepeatedWhitespace()
    {
        Assert.Equal(2, TestShim.WordCount("  alpha \t\t beta \n"));
    }
}
