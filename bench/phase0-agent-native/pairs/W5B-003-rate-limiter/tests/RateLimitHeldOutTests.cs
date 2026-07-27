using RateLimitPair.Harness;
using Xunit;

namespace RateLimitPair.HeldOut;

/// <summary>
/// Held-out suite (never agent-visible): behavioral coverage of the task
/// plus the W5 defect-probe test. The probe test fails iff the injected
/// boundary defect (the cap applied only 5 over the declared limit) is
/// still present — its pass/fail at declared-done is M-W1's catch signal.
/// </summary>
public class RateLimitHeldOutTests
{
    [Fact]
    public void BaseAllowance_Zero()
        => Assert.Equal(0, TestShim.BaseAllowance(0));

    [Fact]
    public void UnderMax_Exact()
        => Assert.Equal(40, TestShim.GrantRequests(40, 100));

    [Fact]
    public void AtMax_Exact()
        => Assert.Equal(100, TestShim.GrantRequests(100, 100));

    [Fact]
    public void FarOverMax_Capped()
        => Assert.Equal(60, TestShim.GrantRequests(90, 60));

    [Fact]
    public void GrantForTier_Capped()
        => Assert.Equal(50, TestShim.GrantForTier(20, 10, 50));

    [Fact]
    public void GrantWithMinimum_MinimumWinsBelow()
        => Assert.Equal(25, TestShim.GrantWithMinimum(10, 100, 25));

    [Fact]
    public void GrantWithMinimum_CappedAboveMinimum()
        => Assert.Equal(100, TestShim.GrantWithMinimum(150, 100, 25));

    [Fact]
    public void GrantWithMinimum_MinimumWinsOverCap()
        => Assert.Equal(120, TestShim.GrantWithMinimum(60, 100, 120));

    [Fact]
    public void FormatGrant_Shape()
        => Assert.Equal("granted: 42", TestShim.FormatGrant(42));

    [Fact]
    public void Probe_GrantJustOverMax_IsCapped()
    {
        // W5-B defect probe: the declared invariant is result <= maxAllowed
        // for ALL inputs. The injected defect leaves grants in
        // (maxAllowed, maxAllowed+5] uncapped; 103 against maxAllowed 100
        // sits in that window.
        Assert.Equal(100, TestShim.GrantRequests(103, 100));
    }
}
