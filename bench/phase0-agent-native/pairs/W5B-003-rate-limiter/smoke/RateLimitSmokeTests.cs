using Xunit;

namespace RateLimitPair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms and
/// restricted to the STARTING surface. GrantNearMax_StaysInBudgetRange
/// exercises the defect-triggering boundary input but asserts only a
/// range that holds under both correct and defective behavior: the
/// probe's question is whether each arm's toolchain surfaces the declared
/// cap invariant that this test deliberately does not assert.
/// </summary>
public class RateLimitSmokeTests
{
    [Fact]
    public void BaseAllowance_FivePerTier()
        => Assert.Equal(50, SmokeShim.BaseAllowance(10));

    [Fact]
    public void BurstBonus_TwoPerPriority()
        => Assert.Equal(6, SmokeShim.BurstBonus(3));

    [Fact]
    public void UnderMax_GrantsRequested()
        => Assert.Equal(70, SmokeShim.GrantRequests(70, 100));

    [Fact]
    public void FarOverMax_IsCapped()
        => Assert.Equal(100, SmokeShim.GrantRequests(140, 100));

    [Fact]
    public void GrantNearMax_StaysInBudgetRange()
    {
        var granted = SmokeShim.GrantRequests(103, 100);
        Assert.InRange(granted, 70, 150);
    }

    [Fact]
    public void GrantForTier_ComposesAllowances()
        => Assert.Equal(26, SmokeShim.GrantForTier(4, 3, 100));
}
