// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): WindowSum / WindowMin /
// MaxWindowSum / CountAbove.
using Xunit;

namespace WindowStats.HeldOut;

public sealed class WindowStatsHeldOutTests
{
    // --- WindowSum (existing behavior must be preserved) ---

    [Fact]
    public void WindowSum_PrefixWindow()
    {
        Assert.Equal(3, TestShim.WindowSum(new[] { 1, 2, 3 }, 0, 2));
    }

    [Fact]
    public void WindowSum_SuffixWindow()
    {
        Assert.Equal(5, TestShim.WindowSum(new[] { 1, 2, 3 }, 1, 2));
    }

    // --- WindowMin ---

    [Fact]
    public void WindowMin_AllPositive_IsNotZero()
    {
        // A min accumulator carelessly initialized to 0 returns 0 here.
        Assert.Equal(5, TestShim.WindowMin(new[] { 5, 7, 9 }, 0, 3));
    }

    [Fact]
    public void WindowMin_SingleElementWindow()
    {
        Assert.Equal(2, TestShim.WindowMin(new[] { 4, 2, 8 }, 1, 1));
    }

    [Fact]
    public void WindowMin_WindowAtEndOfArray()
    {
        Assert.Equal(0, TestShim.WindowMin(new[] { 3, 1, 2, 0 }, 2, 2));
    }

    [Fact]
    public void WindowMin_IgnoresElementsOutsideWindow()
    {
        // The global min (-9) sits outside the window.
        Assert.Equal(4, TestShim.WindowMin(new[] { -9, 4, 6, -9 }, 1, 2));
    }

    [Fact]
    public void WindowMin_NegativeValues()
    {
        Assert.Equal(-5, TestShim.WindowMin(new[] { -1, -5, -3 }, 0, 3));
    }

    // --- MaxWindowSum ---

    [Fact]
    public void MaxWindowSum_AllNegative_SingleElementWindows()
    {
        // A best accumulator carelessly initialized to 0 returns 0 here.
        Assert.Equal(-2, TestShim.MaxWindowSum(new[] { -5, -2, -9 }, 1));
    }

    [Fact]
    public void MaxWindowSum_AllNegative_WiderWindow()
    {
        Assert.Equal(-7, TestShim.MaxWindowSum(new[] { -5, -2, -9 }, 2));
    }

    [Fact]
    public void MaxWindowSum_WindowIsWholeArray()
    {
        Assert.Equal(6, TestShim.MaxWindowSum(new[] { 4, -1, 3 }, 3));
    }

    [Fact]
    public void MaxWindowSum_LastWindowWins()
    {
        // Off-by-one loops that stop before start = length - count miss this.
        Assert.Equal(5, TestShim.MaxWindowSum(new[] { 0, 0, 5 }, 2));
    }

    [Fact]
    public void MaxWindowSum_FirstWindowWins()
    {
        Assert.Equal(10, TestShim.MaxWindowSum(new[] { 9, 1, 0, 0 }, 2));
    }

    [Fact]
    public void MaxWindowSum_InteriorWindowWins()
    {
        Assert.Equal(8, TestShim.MaxWindowSum(new[] { 1, 4, 4, 1, 0 }, 2));
    }

    // --- CountAbove ---

    [Fact]
    public void CountAbove_EqualSumsAreNotCounted()
    {
        // Strictly greater: windows summing exactly to the threshold don't count.
        Assert.Equal(0, TestShim.CountAbove(new[] { 2, 2, 2 }, 1, 2));
    }

    [Fact]
    public void CountAbove_MixedSigns()
    {
        // Window sums of length 2: 2, 1, -2; strictly above 0 -> 2.
        Assert.Equal(2, TestShim.CountAbove(new[] { 3, -1, 2, -4 }, 2, 0));
    }

    [Fact]
    public void CountAbove_CountLargerThanArray_ReturnsZero()
    {
        Assert.Equal(0, TestShim.CountAbove(new[] { 1, 2 }, 5, 0));
    }

    [Fact]
    public void CountAbove_CountEqualsLength_SingleWindow()
    {
        Assert.Equal(1, TestShim.CountAbove(new[] { 1, 2, 3 }, 3, 5));
        Assert.Equal(0, TestShim.CountAbove(new[] { 1, 2, 3 }, 3, 6));
    }

    [Fact]
    public void CountAbove_AllWindowsQualify()
    {
        Assert.Equal(2, TestShim.CountAbove(new[] { 5, 5 }, 1, 4));
    }

    [Fact]
    public void CountAbove_NegativeThreshold()
    {
        // Window sums of length 1: -3, -1, 0; strictly above -2 -> 2.
        Assert.Equal(2, TestShim.CountAbove(new[] { -3, -1, 0 }, 1, -2));
    }
}
