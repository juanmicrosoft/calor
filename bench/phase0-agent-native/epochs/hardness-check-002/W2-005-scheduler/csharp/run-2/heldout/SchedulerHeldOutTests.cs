// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable).
//
// Most tests probe PRESERVED behavior and preservation of the no-overlap
// invariant under mixed operation sequences; the rest probe the new
// priority-preemption operation, whose displacement rules must keep every
// statistics/lookup function correct.
using Xunit;

namespace Scheduling.HeldOut;

public sealed class SchedulerHeldOutTests
{
    private const int Cap = 16;

    private static (int[] starts, int[] ends, int[] prios, int[] active) NewArrays()
        => (new int[Cap], new int[Cap], new int[Cap], new int[Cap]);

    private static void AssertNoActiveOverlap(int[] starts, int[] ends, int[] active, int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (active[i] == 1 && active[j] == 1)
                {
                    Assert.False(
                        TestShim.Overlaps(starts[i], ends[i], starts[j], ends[j]),
                        $"active bookings {i} [{starts[i]},{ends[i]}) and {j} [{starts[j]},{ends[j]}) overlap");
                }
            }
        }
    }

    // --- Preserved: adding and conflicts ---

    [Fact]
    public void AddBooking_AppendsAndReturnsIncrementedCount()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 5, 10, 1);
        Assert.Equal(1, count);
        Assert.Equal(5, s[0]);
        Assert.Equal(10, e[0]);
        Assert.Equal(1, p[0]);
        Assert.Equal(1, a[0]);
    }

    [Fact]
    public void AddBooking_Conflict_RejectsWithoutChanges()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 10, 1);
        Assert.Equal(-1, TestShim.AddBooking(s, e, p, a, count, 5, 15, 1));
        Assert.Equal(1, TestShim.ActiveCount(a, count));
    }

    [Fact]
    public void AddBooking_TouchingIntervals_DoNotConflict()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 5, 10, 1);
        Assert.Equal(2, count);
    }

    [Fact]
    public void AddBooking_InvalidInterval_Rejected()
    {
        var (s, e, p, a) = NewArrays();
        Assert.Equal(-1, TestShim.AddBooking(s, e, p, a, 0, 5, 5, 1));
        Assert.Equal(-1, TestShim.AddBooking(s, e, p, a, 0, -1, 3, 1));
    }

    [Fact]
    public void ConflictIndex_ReturnsFirstActiveMatch()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 10, 15, 1);
        Assert.Equal(0, TestShim.ConflictIndex(s, e, a, count, 3, 12));
        Assert.Equal(-1, TestShim.ConflictIndex(s, e, a, count, 5, 10));
        Assert.True(TestShim.HasConflict(s, e, a, count, 3, 12));
        Assert.False(TestShim.HasConflict(s, e, a, count, 5, 10));
    }

    [Fact]
    public void ConflictIndex_SkipsCancelledBookings()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        Assert.True(TestShim.Cancel(a, count, 0));
        Assert.Equal(-1, TestShim.ConflictIndex(s, e, a, count, 0, 5));
    }

    // --- Preserved: cancellation ---

    [Fact]
    public void Cancel_FreesTheSlotForNewBookings()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 10, 1);
        Assert.True(TestShim.Cancel(a, count, 0));
        count = TestShim.AddBooking(s, e, p, a, count, 5, 15, 1);
        Assert.Equal(2, count);
        Assert.Equal(1, TestShim.ActiveCount(a, count));
    }

    [Fact]
    public void Cancel_IsIdempotent()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 10, 1);
        Assert.True(TestShim.Cancel(a, count, 0));
        Assert.False(TestShim.Cancel(a, count, 0));
    }

    // --- Preserved: slot search ---

    [Fact]
    public void NextFreeStart_FindsGapBetweenBookings()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 8, 12, 1);
        Assert.Equal(5, TestShim.NextFreeStart(s, e, a, count, 3, 0, 20));
        Assert.Equal(12, TestShim.NextFreeStart(s, e, a, count, 4, 0, 20));
    }

    [Fact]
    public void NextFreeStart_RespectsHorizon()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        Assert.Equal(-1, TestShim.NextFreeStart(s, e, a, count, 3, 0, 7));
        Assert.Equal(5, TestShim.NextFreeStart(s, e, a, count, 3, 0, 8));
    }

    [Fact]
    public void NextFreeStart_RespectsEarliest()
    {
        var (s, e, p, a) = NewArrays();
        Assert.Equal(4, TestShim.NextFreeStart(s, e, a, 0, 3, 4, 20));
    }

    // --- Preserved: statistics and lookup ---

    [Fact]
    public void OverlapLength_ClipsToWindow()
    {
        Assert.Equal(2, TestShim.OverlapLength(2, 9, 4, 6));
        Assert.Equal(3, TestShim.OverlapLength(2, 5, 0, 10));
        Assert.Equal(0, TestShim.OverlapLength(0, 3, 5, 9));
    }

    [Fact]
    public void BusyTime_SumsClippedActiveBookings()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 8, 12, 1);
        Assert.Equal(4, TestShim.BusyTime(s, e, a, count, 3, 10));
        Assert.Equal(9, TestShim.BusyTime(s, e, a, count, 0, 20));
    }

    [Fact]
    public void UtilizationPercent_IntegerPercentage()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        Assert.Equal(50, TestShim.UtilizationPercent(s, e, a, count, 0, 10));
    }

    [Fact]
    public void PriorityAt_UsesHalfOpenIntervals()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 3);
        Assert.Equal(3, TestShim.PriorityAt(s, e, p, a, count, 0));
        Assert.Equal(3, TestShim.PriorityAt(s, e, p, a, count, 4));
        Assert.Equal(-1, TestShim.PriorityAt(s, e, p, a, count, 5));
    }

    [Fact]
    public void ActiveCount_TracksCancellations()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 5, 10, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 10, 15, 1);
        TestShim.Cancel(a, count, 1);
        Assert.Equal(2, TestShim.ActiveCount(a, count));
    }

    // --- Preemption: acceptance and rejection ---

    [Fact]
    public void Preempt_NoConflict_BehavesLikeAdd()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddWithPreemption(s, e, p, a, 0, 0, 5, 1, 100);
        Assert.Equal(1, count);
        Assert.Equal(1, TestShim.ActiveCount(a, count));
        Assert.Equal(1, TestShim.PriorityAt(s, e, p, a, count, 2));
    }

    [Fact]
    public void Preempt_InvalidInterval_Rejected()
    {
        var (s, e, p, a) = NewArrays();
        Assert.Equal(-1, TestShim.AddWithPreemption(s, e, p, a, 0, 7, 7, 5, 100));
    }

    [Fact]
    public void Preempt_EqualPriority_RejectedWithoutChanges()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 10, 2);
        Assert.Equal(-1, TestShim.AddWithPreemption(s, e, p, a, count, 5, 15, 2, 100));
        Assert.Equal(0, s[0]);
        Assert.Equal(10, e[0]);
        Assert.Equal(1, a[0]);
        Assert.Equal(1, TestShim.ActiveCount(a, count));
    }

    [Fact]
    public void Preempt_HigherPriorityExisting_Rejected()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 10, 9);
        Assert.Equal(-1, TestShim.AddWithPreemption(s, e, p, a, count, 0, 10, 5, 100));
        Assert.Equal(1, a[0]);
    }

    // --- Preemption: displacement mechanics ---

    [Fact]
    public void Preempt_DisplacedBookingMovesAfterNewBooking()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 10, 1);
        count = TestShim.AddWithPreemption(s, e, p, a, count, 0, 10, 5, 100);
        Assert.Equal(2, count);
        // New booking holds [0,10); displaced booking keeps duration/priority.
        Assert.Equal(5, TestShim.PriorityAt(s, e, p, a, count, 3));
        Assert.Equal(10, s[0]);
        Assert.Equal(20, e[0]);
        Assert.Equal(1, a[0]);
        Assert.Equal(1, TestShim.PriorityAt(s, e, p, a, count, 12));
        AssertNoActiveOverlap(s, e, a, count);
    }

    [Fact]
    public void Preempt_DisplacedBookingSkipsOccupiedSlot()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 10, 1);   // will be displaced
        count = TestShim.AddBooking(s, e, p, a, count, 12, 15, 9);  // untouched, in the way
        count = TestShim.AddWithPreemption(s, e, p, a, count, 0, 10, 5, 40);
        Assert.Equal(3, count);
        // Naive displacement to [10,20) would overlap [12,15); the booking
        // must move past it instead.
        Assert.Equal(15, s[0]);
        Assert.Equal(25, e[0]);
        Assert.Equal(1, a[0]);
        AssertNoActiveOverlap(s, e, a, count);
    }

    [Fact]
    public void Preempt_TwoDisplacedBookings_DoNotOverlapEachOther()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 4, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 4, 9, 2);
        count = TestShim.AddWithPreemption(s, e, p, a, count, 2, 6, 7, 40);
        Assert.Equal(3, count);
        // Re-placed in increasing index order after the new booking's end.
        Assert.Equal(6, s[0]);
        Assert.Equal(10, e[0]);
        Assert.Equal(10, s[1]);
        Assert.Equal(15, e[1]);
        Assert.Equal(3, TestShim.ActiveCount(a, count));
        AssertNoActiveOverlap(s, e, a, count);
    }

    [Fact]
    public void Preempt_NoSlotBeforeHorizon_CancelsDisplaced()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 10, 1);
        count = TestShim.AddWithPreemption(s, e, p, a, count, 0, 10, 5, 15);
        Assert.Equal(2, count);
        Assert.Equal(1, TestShim.ActiveCount(a, count));
        Assert.Equal(0, a[0]);
        Assert.Equal(5, TestShim.PriorityAt(s, e, p, a, count, 5));
    }

    [Fact]
    public void Preempt_PartialOverlap_DisplacesWholeBooking()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 8, 14, 2);
        count = TestShim.AddWithPreemption(s, e, p, a, count, 5, 10, 5, 40);
        Assert.Equal(2, count);
        Assert.Equal(10, s[0]);
        Assert.Equal(16, e[0]);
        Assert.Equal(1, a[0]);
        AssertNoActiveOverlap(s, e, a, count);
    }

    // --- Preemption: the invariant keeps the statistics functions honest ---

    [Fact]
    public void Preempt_UtilizationConsistentAfterDisplacement()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 4, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 4, 9, 2);
        count = TestShim.AddWithPreemption(s, e, p, a, count, 2, 6, 7, 40);
        // Active bookings are disjoint, so busy time must equal the sum of
        // active durations: 4 + 5 + 4 = 13 inside [0,20).
        Assert.Equal(13, TestShim.BusyTime(s, e, a, count, 0, 20));
        Assert.Equal(65, TestShim.UtilizationPercent(s, e, a, count, 0, 20));
    }

    [Fact]
    public void Preempt_MixedSequence_KeepsInvariantAndCounts()
    {
        var (s, e, p, a) = NewArrays();
        int count = TestShim.AddBooking(s, e, p, a, 0, 0, 5, 1);
        count = TestShim.AddBooking(s, e, p, a, count, 5, 10, 3);
        Assert.True(TestShim.Cancel(a, count, 0));
        count = TestShim.AddWithPreemption(s, e, p, a, count, 4, 9, 5, 40);
        Assert.Equal(3, count);
        Assert.Equal(2, TestShim.ActiveCount(a, count));
        // Displaced [5,10) booking re-placed at the new booking's end.
        Assert.Equal(9, s[1]);
        Assert.Equal(14, e[1]);
        Assert.Equal(3, TestShim.PriorityAt(s, e, p, a, count, 9));
        Assert.Equal(10, TestShim.BusyTime(s, e, a, count, 0, 20));
        AssertNoActiveOverlap(s, e, a, count);
    }
}
