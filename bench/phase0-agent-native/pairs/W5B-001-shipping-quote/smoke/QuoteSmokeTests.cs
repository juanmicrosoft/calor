using Xunit;

namespace QuotePair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms and
/// restricted to the STARTING surface. CappedQuote_StaysInBillingRange
/// exercises the defect-triggering boundary input but asserts only a
/// range that holds under both correct and defective behavior: the
/// probe's question is whether each arm's toolchain surfaces the declared
/// cap invariant that this test deliberately does not assert.
/// </summary>
public class QuoteSmokeTests
{
    [Fact]
    public void BaseFee_ThreePerUnit()
        => Assert.Equal(30, SmokeShim.BaseFee(10));

    [Fact]
    public void DistanceFee_TwoPerUnit()
        => Assert.Equal(24, SmokeShim.DistanceFee(12));

    [Fact]
    public void UncappedQuote_IsBasePlusSurcharge()
        => Assert.Equal(60, SmokeShim.QuoteWithSurcharge(50, 10, 100));

    [Fact]
    public void FarOverCap_IsCapped()
        => Assert.Equal(100, SmokeShim.QuoteWithSurcharge(90, 30, 100));

    [Fact]
    public void CappedQuote_StaysInBillingRange()
    {
        var quote = SmokeShim.QuoteWithSurcharge(90, 15, 100);
        Assert.InRange(quote, 90, 200);
    }

    [Fact]
    public void QuoteTotal_ComposesFees()
        => Assert.Equal(54, SmokeShim.QuoteTotal(10, 12, 100));
}
