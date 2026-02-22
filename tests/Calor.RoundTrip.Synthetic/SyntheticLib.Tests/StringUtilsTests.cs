using SyntheticLib;
using Xunit;

namespace SyntheticLib.Tests;

public class StringUtilsTests
{
    [Fact]
    public void Reverse_ReversesString()
    {
        Assert.Equal("olleh", StringUtils.Reverse("hello"));
    }

    [Fact]
    public void Reverse_EmptyString()
    {
        Assert.Equal("", StringUtils.Reverse(""));
    }

    [Fact]
    public void Reverse_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StringUtils.Reverse(null!));
    }

    [Fact]
    public void IsPalindrome_True()
    {
        Assert.True(StringUtils.IsPalindrome("racecar"));
        Assert.True(StringUtils.IsPalindrome("madam"));
    }

    [Fact]
    public void IsPalindrome_False()
    {
        Assert.False(StringUtils.IsPalindrome("hello"));
    }

    [Fact]
    public void IsPalindrome_Null_ReturnsFalse()
    {
        Assert.False(StringUtils.IsPalindrome(null!));
    }

    [Fact]
    public void CountOccurrences_FindsCharacters()
    {
        Assert.Equal(3, StringUtils.CountOccurrences("hello world", 'l'));
        Assert.Equal(0, StringUtils.CountOccurrences("hello", 'z'));
    }

    [Fact]
    public void CountOccurrences_Null_ReturnsZero()
    {
        Assert.Equal(0, StringUtils.CountOccurrences(null!, 'a'));
    }

    [Fact]
    public void Truncate_ShortString()
    {
        Assert.Equal("hi", StringUtils.Truncate("hi", 10));
    }

    [Fact]
    public void Truncate_LongString()
    {
        Assert.Equal("hel...", StringUtils.Truncate("hello world", 3));
    }

    [Fact]
    public void Capitalize_Works()
    {
        Assert.Equal("Hello", StringUtils.Capitalize("hello"));
        Assert.Equal("", StringUtils.Capitalize(""));
    }
}
