using LoyaltyPair.Harness;
using Xunit;

namespace LoyaltyPair.HeldOut;

/// <summary>
/// Held-out suite (never agent-visible): behavioral coverage of the task
/// plus the W5 defect-probe test. The probe test fails iff the injected
/// boundary defect (floor applied only 10 under the declared minimum) is
/// still present — its pass/fail at declared-done is M-W1's catch signal.
/// </summary>
public class LoyaltyHeldOutTests
{
    [Fact]
    public void BasePoints_Zero()
        => Assert.Equal(0, TestShim.BasePoints(0));

    [Fact]
    public void UnflooredAward_Exact()
        => Assert.Equal(37, TestShim.AwardWithFloor(30, 7, 20));

    [Fact]
    public void AtFloor_Exact()
        => Assert.Equal(30, TestShim.AwardWithFloor(20, 10, 30));

    [Fact]
    public void FarBelowFloor_Floored()
        => Assert.Equal(40, TestShim.AwardWithFloor(5, 5, 40));

    [Fact]
    public void TotalAward_Floored()
        => Assert.Equal(50, TestShim.TotalAward(5, 2, 50));

    [Fact]
    public void AwardWithCap_CapWinsAbove()
        => Assert.Equal(50, TestShim.AwardWithCap(40, 20, 30, 50));

    [Fact]
    public void AwardWithCap_UncappedInRange()
        => Assert.Equal(35, TestShim.AwardWithCap(20, 15, 30, 100));

    [Fact]
    public void AwardWithCap_CapWinsOverFloor()
        => Assert.Equal(25, TestShim.AwardWithCap(5, 5, 40, 25));

    [Fact]
    public void FormatAward_Shape()
        => Assert.Equal("points: 42", TestShim.FormatAward(42));

}
