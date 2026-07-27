using Xunit;

namespace LoyaltyPair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms and
/// restricted to the STARTING surface. FlooredAward_StaysInProgramRange
/// exercises the defect-triggering boundary input but asserts only a
/// range that holds under both correct and defective behavior: the
/// probe's question is whether each arm's toolchain surfaces the declared
/// floor invariant that this test deliberately does not assert.
/// </summary>
public class LoyaltySmokeTests
{
    [Fact]
    public void BasePoints_TwoPerUnit()
        => Assert.Equal(20, SmokeShim.BasePoints(10));

    [Fact]
    public void BonusPoints_FivePerVisit()
        => Assert.Equal(20, SmokeShim.BonusPoints(4));

    [Fact]
    public void UnflooredAward_IsEarnedPlusBonus()
        => Assert.Equal(50, SmokeShim.AwardWithFloor(40, 10, 30));

    [Fact]
    public void FarBelowFloor_GetsFloor()
        => Assert.Equal(30, SmokeShim.AwardWithFloor(5, 5, 30));

    [Fact]
    public void FlooredAward_StaysInProgramRange()
    {
        var award = SmokeShim.AwardWithFloor(20, 5, 30);
        Assert.InRange(award, 20, 100);
    }

    [Fact]
    public void TotalAward_ComposesPoints()
        => Assert.Equal(40, SmokeShim.TotalAward(10, 4, 30));
}
