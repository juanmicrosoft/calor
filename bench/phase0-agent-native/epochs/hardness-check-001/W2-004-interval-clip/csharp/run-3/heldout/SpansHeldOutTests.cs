// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): TotalCovered / ContainsPoint /
// ClipCovered / GapCount.
using Xunit;

namespace Spans.HeldOut;

public sealed class SpansHeldOutTests
{
    private static readonly int[] Empty = System.Array.Empty<int>();

    // --- TotalCovered (existing behavior must be preserved) ---

    [Fact]
    public void TotalCovered_EmptySet_Zero()
    {
        Assert.Equal(0, TestShim.TotalCovered(Empty, Empty));
    }

    [Fact]
    public void TotalCovered_TwoIntervals()
    {
        Assert.Equal(15, TestShim.TotalCovered(new[] { 0, 20 }, new[] { 10, 25 }));
    }

    // --- ContainsPoint (half-open semantics) ---

    [Fact]
    public void ContainsPoint_AtStart_IsInside()
    {
        Assert.True(TestShim.ContainsPoint(new[] { 10 }, new[] { 20 }, 10));
    }

    [Fact]
    public void ContainsPoint_AtEnd_IsOutside()
    {
        // Half-open: the end coordinate is excluded.
        Assert.False(TestShim.ContainsPoint(new[] { 10 }, new[] { 20 }, 20));
    }

    [Fact]
    public void ContainsPoint_JustBeforeEnd_IsInside()
    {
        Assert.True(TestShim.ContainsPoint(new[] { 10 }, new[] { 20 }, 19));
    }

    [Fact]
    public void ContainsPoint_InGap_IsOutside()
    {
        Assert.False(TestShim.ContainsPoint(new[] { 10, 30 }, new[] { 20, 40 }, 25));
    }

    [Fact]
    public void ContainsPoint_BeforeFirstAndAfterLast_IsOutside()
    {
        var starts = new[] { 10, 30 };
        var ends = new[] { 20, 40 };
        Assert.False(TestShim.ContainsPoint(starts, ends, 5));
        Assert.False(TestShim.ContainsPoint(starts, ends, 45));
    }

    [Fact]
    public void ContainsPoint_EmptySet_IsOutside()
    {
        Assert.False(TestShim.ContainsPoint(Empty, Empty, 0));
    }

    [Fact]
    public void ContainsPoint_SharedBoundaryOfTouchingIntervals_IsInside()
    {
        // 20 is the (excluded) end of the first interval but the (included)
        // start of the second.
        Assert.True(TestShim.ContainsPoint(new[] { 10, 20 }, new[] { 20, 30 }, 20));
    }

    [Fact]
    public void ContainsPoint_NegativeCoordinates()
    {
        Assert.True(TestShim.ContainsPoint(new[] { -20 }, new[] { -10 }, -20));
        Assert.False(TestShim.ContainsPoint(new[] { -20 }, new[] { -10 }, -10));
    }

    // --- ClipCovered ---

    [Fact]
    public void ClipCovered_WindowInsideInterval()
    {
        Assert.Equal(10, TestShim.ClipCovered(new[] { 0 }, new[] { 100 }, 10, 20));
    }

    [Fact]
    public void ClipCovered_IntervalInsideWindow()
    {
        Assert.Equal(10, TestShim.ClipCovered(new[] { 10 }, new[] { 20 }, 0, 100));
    }

    [Fact]
    public void ClipCovered_PartialOverlapLeftAndRight()
    {
        Assert.Equal(5, TestShim.ClipCovered(new[] { 10 }, new[] { 20 }, 15, 50));
        Assert.Equal(2, TestShim.ClipCovered(new[] { 10 }, new[] { 20 }, 0, 12));
    }

    [Fact]
    public void ClipCovered_WindowEntirelyInGap_Zero()
    {
        // A careless min/max without flooring at zero adds negative
        // contributions from the intervals on either side.
        Assert.Equal(0, TestShim.ClipCovered(new[] { 0, 50 }, new[] { 10, 60 }, 20, 30));
    }

    [Fact]
    public void ClipCovered_EmptyWindow_Zero()
    {
        Assert.Equal(0, TestShim.ClipCovered(new[] { 0 }, new[] { 100 }, 40, 40));
    }

    [Fact]
    public void ClipCovered_WindowSpansMultipleIntervals()
    {
        // [0,10) clipped to [5,25) gives 5; [20,30) clipped gives 5.
        Assert.Equal(10, TestShim.ClipCovered(new[] { 0, 20 }, new[] { 10, 30 }, 5, 25));
    }

    [Fact]
    public void ClipCovered_EmptySet_Zero()
    {
        Assert.Equal(0, TestShim.ClipCovered(Empty, Empty, 0, 100));
    }

    // --- GapCount ---

    [Fact]
    public void GapCount_TouchingIntervals_NoGap()
    {
        Assert.Equal(0, TestShim.GapCount(new[] { 0, 10 }, new[] { 10, 20 }));
    }

    [Fact]
    public void GapCount_MixOfTouchingAndSeparated()
    {
        // [0,5) gap [10,20) touch [20,30) gap [35,40) -> 2 gaps.
        Assert.Equal(2, TestShim.GapCount(new[] { 0, 10, 20, 35 }, new[] { 5, 20, 30, 40 }));
    }

    [Fact]
    public void GapCount_EmptyAndSingle_Zero()
    {
        Assert.Equal(0, TestShim.GapCount(Empty, Empty));
        Assert.Equal(0, TestShim.GapCount(new[] { 3 }, new[] { 9 }));
    }

    [Fact]
    public void GapCount_UnitGap_Counts()
    {
        Assert.Equal(1, TestShim.GapCount(new[] { 0, 6 }, new[] { 5, 10 }));
    }
}
