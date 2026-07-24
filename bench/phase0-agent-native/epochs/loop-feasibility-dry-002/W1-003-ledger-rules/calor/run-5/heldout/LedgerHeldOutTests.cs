// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable).
//
// Most tests probe PRESERVED behavior across distant functions (balances,
// batch totals, conservation); the rest probe the new fee schedule and the
// new reversal operation. Fee schedule under test: amount >= 2000 -> 0,
// amount < 100 -> 1, else amount / 100.
using Xunit;

namespace LedgerRules.HeldOut;

public sealed class LedgerHeldOutTests
{
    private const int Cap = 32;

    // --- Preserved: plain posting and balances ---

    [Fact]
    public void PostEntry_AppendsAndReturnsIncrementedCount()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.PostEntry(d, c, a, 0, 1, 2, 500);
        Assert.Equal(1, count);
        Assert.Equal(1, d[0]);
        Assert.Equal(2, c[0]);
        Assert.Equal(500, a[0]);
    }

    [Fact]
    public void AccountBalance_CreditsAddDebitsSubtract()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.PostEntry(d, c, a, 0, 1, 2, 300);
        count = TestShim.PostEntry(d, c, a, count, 2, 1, 100);
        Assert.Equal(-200, TestShim.AccountBalance(d, c, a, count, 1));
        Assert.Equal(200, TestShim.AccountBalance(d, c, a, count, 2));
    }

    [Fact]
    public void TotalDebitedAndCredited_AreOneSided()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.PostEntry(d, c, a, 0, 1, 2, 300);
        count = TestShim.PostEntry(d, c, a, count, 1, 3, 200);
        count = TestShim.PostEntry(d, c, a, count, 2, 1, 50);
        Assert.Equal(500, TestShim.TotalDebited(d, a, count, 1));
        Assert.Equal(50, TestShim.TotalCredited(c, a, count, 1));
        Assert.Equal(300, TestShim.TotalCredited(c, a, count, 2));
    }

    [Fact]
    public void Conservation_HoldsAfterMixedSequence()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.PostEntry(d, c, a, 0, 1, 2, 700);
        count = TestShim.Transfer(d, c, a, count, 2, 3, 300);
        count = TestShim.Transfer(d, c, a, count, 3, 1, 50);
        count = TestShim.BatchPost(d, c, a, count,
            new[] { 1, 3 }, new[] { 3, 2 }, new[] { 40, 60 }, 2);
        int sum = 0;
        for (int acct = 0; acct <= 3; acct++)
        {
            sum += TestShim.AccountBalance(d, c, a, count, acct);
        }
        Assert.Equal(0, sum);
    }

    // --- Preserved: totals and batch posting ---

    [Fact]
    public void TotalVolume_SumsAllEntries()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.PostEntry(d, c, a, 0, 1, 2, 5);
        count = TestShim.BatchPost(d, c, a, count,
            new[] { 1, 2, 3 }, new[] { 2, 3, 1 }, new[] { 10, 20, 30 }, 3);
        Assert.Equal(4, count);
        Assert.Equal(65, TestShim.TotalVolume(a, count));
    }

    [Fact]
    public void BatchPost_MatchesRepeatedPostEntry()
    {
        var d1 = new int[Cap]; var c1 = new int[Cap]; var a1 = new int[Cap];
        var d2 = new int[Cap]; var c2 = new int[Cap]; var a2 = new int[Cap];
        var froms = new[] { 1, 2, 1 };
        var tos = new[] { 2, 3, 3 };
        var amts = new[] { 100, 200, 300 };

        int n1 = TestShim.BatchPost(d1, c1, a1, 0, froms, tos, amts, 3);
        int n2 = 0;
        for (int i = 0; i < 3; i++)
        {
            n2 = TestShim.PostEntry(d2, c2, a2, n2, froms[i], tos[i], amts[i]);
        }

        Assert.Equal(n2, n1);
        for (int acct = 0; acct <= 3; acct++)
        {
            Assert.Equal(
                TestShim.AccountBalance(d2, c2, a2, n2, acct),
                TestShim.AccountBalance(d1, c1, a1, n1, acct));
        }
    }

    [Fact]
    public void FeesCollected_EqualsCreditsToFeeAccount()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.Transfer(d, c, a, 0, 1, 2, 300);
        Assert.Equal(3, TestShim.FeesCollected(c, a, count));
        Assert.Equal(TestShim.TotalCredited(c, a, count, 0),
            TestShim.FeesCollected(c, a, count));
    }

    // --- Preserved: validation predicates ---

    [Fact]
    public void IsValidAccount_Bounds()
    {
        Assert.True(TestShim.IsValidAccount(0, 3));
        Assert.True(TestShim.IsValidAccount(2, 3));
        Assert.False(TestShim.IsValidAccount(3, 3));
        Assert.False(TestShim.IsValidAccount(-1, 3));
    }

    [Fact]
    public void IsValidAmount_Bounds()
    {
        Assert.True(TestShim.IsValidAmount(1));
        Assert.True(TestShim.IsValidAmount(999999));
        Assert.False(TestShim.IsValidAmount(0));
        Assert.False(TestShim.IsValidAmount(1000000));
    }

    // --- Preserved: mid-band transfer behavior is unchanged ---

    [Fact]
    public void Transfer_MidBand_PostsMainAndFeeEntries()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.Transfer(d, c, a, 0, 1, 2, 300);
        Assert.Equal(2, count);
        Assert.Equal(-303, TestShim.AccountBalance(d, c, a, count, 1));
        Assert.Equal(300, TestShim.AccountBalance(d, c, a, count, 2));
        Assert.Equal(3, TestShim.AccountBalance(d, c, a, count, 0));
    }

    // --- New fee schedule ---

    [Fact]
    public void EntryFee_SmallAmounts_ChargeMinimumOne()
    {
        Assert.Equal(1, TestShim.EntryFee(50));
        Assert.Equal(1, TestShim.EntryFee(99));
        Assert.Equal(1, TestShim.EntryFee(1));
    }

    [Fact]
    public void EntryFee_LargeAmounts_AreWaived()
    {
        Assert.Equal(0, TestShim.EntryFee(2000));
        Assert.Equal(0, TestShim.EntryFee(2500));
        Assert.Equal(0, TestShim.EntryFee(999999));
    }

    [Fact]
    public void EntryFee_MidBand_Unchanged()
    {
        Assert.Equal(1, TestShim.EntryFee(100));
        Assert.Equal(3, TestShim.EntryFee(300));
        Assert.Equal(19, TestShim.EntryFee(1999));
    }

    [Fact]
    public void Transfer_SmallAmount_PostsMinimumFeeEntry()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.Transfer(d, c, a, 0, 1, 2, 50);
        Assert.Equal(2, count);
        Assert.Equal(-51, TestShim.AccountBalance(d, c, a, count, 1));
        Assert.Equal(50, TestShim.AccountBalance(d, c, a, count, 2));
        Assert.Equal(1, TestShim.AccountBalance(d, c, a, count, 0));
    }

    [Fact]
    public void Transfer_LargeAmount_PostsNoFeeEntry()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.Transfer(d, c, a, 0, 1, 2, 2500);
        Assert.Equal(1, count);
        Assert.Equal(-2500, TestShim.AccountBalance(d, c, a, count, 1));
        Assert.Equal(0, TestShim.AccountBalance(d, c, a, count, 0));
    }

    // --- Costing path must agree with the schedule (distant from EntryFee) ---

    [Fact]
    public void TransferCost_FollowsNewSchedule()
    {
        Assert.Equal(51, TestShim.TransferCost(50));
        Assert.Equal(303, TestShim.TransferCost(300));
        Assert.Equal(2500, TestShim.TransferCost(2500));
    }

    [Fact]
    public void TransferCost_MatchesActualTransferOutcome()
    {
        // What the payer actually loses in a Transfer must equal TransferCost.
        foreach (int amount in new[] { 50, 150, 1999, 2000, 4000 })
        {
            var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
            int count = TestShim.Transfer(d, c, a, 0, 1, 2, amount);
            int paid = -TestShim.AccountBalance(d, c, a, count, 1);
            Assert.Equal(TestShim.TransferCost(amount), paid);
        }
    }

    [Fact]
    public void CanAfford_ExactBoundary()
    {
        Assert.True(TestShim.CanAfford(51, 50));
        Assert.False(TestShim.CanAfford(50, 50));
        Assert.True(TestShim.CanAfford(2500, 2500));
        Assert.False(TestShim.CanAfford(2499, 2500));
    }

    // --- Batch path must agree with the schedule (distant from EntryFee) ---

    [Fact]
    public void BatchTransfer_MatchesSequentialTransfers()
    {
        var amts = new[] { 50, 300, 2500 };
        var froms = new[] { 1, 2, 3 };
        var tos = new[] { 2, 3, 1 };

        var d1 = new int[Cap]; var c1 = new int[Cap]; var a1 = new int[Cap];
        int n1 = TestShim.BatchTransfer(d1, c1, a1, 0, froms, tos, amts, 3);

        var d2 = new int[Cap]; var c2 = new int[Cap]; var a2 = new int[Cap];
        int n2 = 0;
        for (int i = 0; i < 3; i++)
        {
            n2 = TestShim.Transfer(d2, c2, a2, n2, froms[i], tos[i], amts[i]);
        }

        Assert.Equal(n2, n1);
        for (int acct = 0; acct <= 3; acct++)
        {
            Assert.Equal(
                TestShim.AccountBalance(d2, c2, a2, n2, acct),
                TestShim.AccountBalance(d1, c1, a1, n1, acct));
        }
    }

    [Fact]
    public void BatchTransfer_LargeAmounts_PostNoFeeEntries()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.BatchTransfer(d, c, a, 0,
            new[] { 1, 2 }, new[] { 2, 1 }, new[] { 2000, 3000 }, 2);
        Assert.Equal(2, count);
        Assert.Equal(0, TestShim.FeesCollected(c, a, count));
    }

    [Fact]
    public void BatchTransfer_SmallAmounts_PostMinimumFeeEntries()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.BatchTransfer(d, c, a, 0,
            new[] { 1, 2 }, new[] { 2, 1 }, new[] { 40, 60 }, 2);
        Assert.Equal(4, count);
        Assert.Equal(2, TestShim.FeesCollected(c, a, count));
    }

    // --- Reversal ---

    [Fact]
    public void ReverseEntry_AppendsSwappedEntry()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.PostEntry(d, c, a, 0, 1, 2, 400);
        count = TestShim.ReverseEntry(d, c, a, count, 0);
        Assert.Equal(2, count);
        Assert.Equal(2, d[1]);
        Assert.Equal(1, c[1]);
        Assert.Equal(400, a[1]);
    }

    [Fact]
    public void ReverseEntry_RestoresBalances()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.PostEntry(d, c, a, 0, 1, 2, 400);
        count = TestShim.ReverseEntry(d, c, a, count, 0);
        Assert.Equal(0, TestShim.AccountBalance(d, c, a, count, 1));
        Assert.Equal(0, TestShim.AccountBalance(d, c, a, count, 2));
    }

    [Fact]
    public void ReverseEntry_OfBothTransferEntries_RestoresAllBalances()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.Transfer(d, c, a, 0, 1, 2, 150);
        Assert.Equal(2, count); // main + fee entry (fee = 1)
        count = TestShim.ReverseEntry(d, c, a, count, 0);
        count = TestShim.ReverseEntry(d, c, a, count, 1);
        Assert.Equal(4, count);
        for (int acct = 0; acct <= 2; acct++)
        {
            Assert.Equal(0, TestShim.AccountBalance(d, c, a, count, acct));
        }
    }

    [Fact]
    public void ReverseEntry_ChargesNoFee()
    {
        var d = new int[Cap]; var c = new int[Cap]; var a = new int[Cap];
        int count = TestShim.PostEntry(d, c, a, 0, 1, 2, 50);
        count = TestShim.ReverseEntry(d, c, a, count, 0);
        Assert.Equal(2, count);
        Assert.Equal(0, TestShim.FeesCollected(c, a, count));
    }
}
