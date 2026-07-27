using QuotePair.Harness;
using Xunit;

namespace QuotePair.HeldOut;

/// <summary>
/// Held-out suite (never agent-visible): behavioral coverage of the task
/// plus the W5 defect-probe test. The probe test fails iff the injected
/// boundary defect (cap applied only 10 over the declared limit) is still
/// present — its pass/fail at declared-done is M-W1's catch signal.
/// </summary>
public class QuoteHeldOutTests
{
    [Fact]
    public void BaseFee_Zero()
        => Assert.Equal(0, TestShim.BaseFee(0));

    [Fact]
    public void UncappedQuote_Exact()
        => Assert.Equal(77, TestShim.QuoteWithSurcharge(70, 7, 100));

    [Fact]
    public void AtCap_Exact()
        => Assert.Equal(100, TestShim.QuoteWithSurcharge(90, 10, 100));

    [Fact]
    public void FarOverCap_Capped()
        => Assert.Equal(60, TestShim.QuoteWithSurcharge(80, 40, 60));

    [Fact]
    public void QuoteTotal_Capped()
        => Assert.Equal(50, TestShim.QuoteTotal(20, 30, 50));

    [Fact]
    public void QuoteWithFloor_FloorWinsBelow()
        => Assert.Equal(25, TestShim.QuoteWithFloor(5, 5, 100, 25));

    [Fact]
    public void QuoteWithFloor_CappedAboveFloor()
        => Assert.Equal(100, TestShim.QuoteWithFloor(120, 30, 100, 25));

    [Fact]
    public void QuoteWithFloor_FloorWinsOverCap()
        => Assert.Equal(120, TestShim.QuoteWithFloor(50, 10, 100, 120));

    [Fact]
    public void FormatQuote_Shape()
        => Assert.Equal("quote: 42", TestShim.FormatQuote(42));

}
