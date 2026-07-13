// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): BillBasic / BillPro / BillCapped /
// CheaperPlan.
using Xunit;

namespace Billing.HeldOut;

public sealed class BillingHeldOutTests
{
    // --- BillBasic (existing behavior must be preserved by the refactor) ---

    [Fact]
    public void BillBasic_NegativeUsage_ClampsToBaseFee()
    {
        Assert.Equal(500, TestShim.BillBasic(-25));
    }

    [Fact]
    public void BillBasic_AboveCeiling_ClampsToCeilingPrice()
    {
        Assert.Equal(70500, TestShim.BillBasic(100001));
    }

    [Fact]
    public void BillBasic_IntMaxValue_NormalizesBeforeMultiplying()
    {
        // If units are multiplied before normalization, this overflows.
        Assert.Equal(70500, TestShim.BillBasic(int.MaxValue));
    }

    // --- BillPro ---

    [Fact]
    public void BillPro_ZeroUnits_BaseFeeOnly()
    {
        Assert.Equal(300, TestShim.BillPro(0));
    }

    [Fact]
    public void BillPro_NegativeUnits_BaseFeeOnly()
    {
        Assert.Equal(300, TestShim.BillPro(-50));
    }

    [Fact]
    public void BillPro_ExactlyAtTierBoundary()
    {
        // 1000 * 9 / 10 + 300
        Assert.Equal(1200, TestShim.BillPro(1000));
    }

    [Fact]
    public void BillPro_JustAboveBoundary_SecondTierTruncatesToZero()
    {
        // tier1 = 900, tier2 = 1 * 8 / 10 = 0 (truncating!), + 300.
        // Float arithmetic (1200.8 rounded) gets 1201 and fails.
        Assert.Equal(1200, TestShim.BillPro(1001));
    }

    [Fact]
    public void BillPro_SecondTierTruncatesTowardZero()
    {
        // tier1 = 900, tier2 = 13 * 8 / 10 = 10, + 300.
        Assert.Equal(1210, TestShim.BillPro(1013));
    }

    [Fact]
    public void BillPro_CeilingAndBeyond()
    {
        Assert.Equal(80400, TestShim.BillPro(100000));
        Assert.Equal(80400, TestShim.BillPro(3_000_000));
        Assert.Equal(80400, TestShim.BillPro(int.MaxValue));
    }

    // --- BillCapped ---

    [Fact]
    public void BillCapped_CapEqualsPrice_ReturnsPrice()
    {
        // BillPro(2000) = 900 + 800 + 300 = 2000.
        Assert.Equal(2000, TestShim.BillCapped(2000, 2000));
    }

    [Fact]
    public void BillCapped_CapJustBelowPrice_ReturnsCap()
    {
        Assert.Equal(1999, TestShim.BillCapped(2000, 1999));
    }

    [Fact]
    public void BillCapped_CapBelowBaseFee_ReturnsCap()
    {
        // BillPro(50) = 45 + 300 = 345, but the cap wins.
        Assert.Equal(250, TestShim.BillCapped(50, 250));
    }

    [Fact]
    public void BillCapped_ZeroCap_ReturnsZero()
    {
        Assert.Equal(0, TestShim.BillCapped(123456, 0));
    }

    // --- CheaperPlan ---

    [Fact]
    public void CheaperPlan_ZeroUsage_ProCheaper()
    {
        // Basic 500 vs Pro 300.
        Assert.Equal(1, TestShim.CheaperPlan(0));
    }

    [Fact]
    public void CheaperPlan_NegativeUsage_ProCheaper()
    {
        Assert.Equal(1, TestShim.CheaperPlan(-10));
    }

    [Fact]
    public void CheaperPlan_MidTierOne_ProCheaper()
    {
        // Basic 693 + 500 = 1193 vs Pro 891 + 300 = 1191.
        Assert.Equal(1, TestShim.CheaperPlan(990));
    }

    [Fact]
    public void CheaperPlan_TruncationTie_PrefersBasic()
    {
        // Basic: 997*7/10 + 500 = 697 + 500 = 1197.
        // Pro:   997*9/10 + 300 = 897 + 300 = 1197. Tie -> Basic.
        // Any float-based comparison sees 1197.9 vs 1197.3 and picks Pro.
        Assert.Equal(0, TestShim.CheaperPlan(997));
    }

    [Fact]
    public void CheaperPlan_ExactTieAtBoundary_PrefersBasic()
    {
        // Basic 700 + 500 = 1200, Pro 900 + 300 = 1200. Tie -> Basic.
        Assert.Equal(0, TestShim.CheaperPlan(1000));
    }

    [Fact]
    public void CheaperPlan_LargeUsage_BasicCheaper()
    {
        // Basic 3500 + 500 = 4000 vs Pro 900 + 3200 + 300 = 4400.
        Assert.Equal(0, TestShim.CheaperPlan(5000));
    }
}
