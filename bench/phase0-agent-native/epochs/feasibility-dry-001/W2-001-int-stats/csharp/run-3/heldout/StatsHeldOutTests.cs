// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): Min / Max / Sum / Mean / Clamp.
using Xunit;

namespace Stats.HeldOut;

public sealed class StatsHeldOutTests
{
    // --- Min / Max (pre-existing behavior must be preserved) ---

    [Fact]
    public void Min_FindsSmallestElement()
    {
        Assert.Equal(1, TestShim.Min(new[] { 3, 1, 2 }));
        Assert.Equal(-8, TestShim.Min(new[] { 4, -8, 0, 7 }));
        Assert.Equal(-5, TestShim.Min(new[] { -5 }));
    }

    [Fact]
    public void Max_FindsLargestElement()
    {
        Assert.Equal(3, TestShim.Max(new[] { 3, 1, 2 }));
        Assert.Equal(7, TestShim.Max(new[] { 4, -8, 0, 7 }));
        Assert.Equal(-5, TestShim.Max(new[] { -5 }));
    }

    // --- Sum ---

    [Fact]
    public void Sum_EmptyArray_ReturnsZero()
    {
        Assert.Equal(0, TestShim.Sum(System.Array.Empty<int>()));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3 }, 6)]
    [InlineData(new[] { -2, 7, -5 }, 0)]
    [InlineData(new[] { 42 }, 42)]
    [InlineData(new[] { -1, -2, -3 }, -6)]
    public void Sum_AddsAllElements(int[] values, int expected)
    {
        Assert.Equal(expected, TestShim.Sum(values));
    }

    // --- Mean (truncating integer division, toward zero) ---

    [Theory]
    [InlineData(new[] { 10 }, 10)]
    [InlineData(new[] { 2, 3, 4 }, 3)]
    [InlineData(new[] { 1, 2 }, 1)]      // 3 / 2 truncates to 1
    [InlineData(new[] { -3, -4 }, -3)]   // -7 / 2 truncates toward zero to -3
    [InlineData(new[] { -1, 1 }, 0)]
    public void Mean_UsesTruncatingDivision(int[] values, int expected)
    {
        Assert.Equal(expected, TestShim.Mean(values));
    }

    // --- Clamp ---

    [Theory]
    [InlineData(5, 0, 10, 5)]    // inside range
    [InlineData(-1, 0, 10, 0)]   // below lo
    [InlineData(11, 0, 10, 10)]  // above hi
    [InlineData(0, 0, 10, 0)]    // at lo boundary
    [InlineData(10, 0, 10, 10)]  // at hi boundary
    [InlineData(7, 7, 7, 7)]     // degenerate range lo == hi
    [InlineData(-100, -50, -10, -50)]
    public void Clamp_BoundsValueIntoRange(int value, int lo, int hi, int expected)
    {
        Assert.Equal(expected, TestShim.Clamp(value, lo, hi));
    }
}
