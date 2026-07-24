// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable).
using Xunit;

namespace OrderPipeline.HeldOut;

public sealed class OrderPipelineHeldOutTests
{
    // ---- preservation probes: pricing pipeline ----

    [Fact]
    public void IsValidOrder_Bounds()
    {
        Assert.False(TestShim.IsValidOrder(0, 100));
        Assert.True(TestShim.IsValidOrder(1, 100));
        Assert.True(TestShim.IsValidOrder(1000, 100));
        Assert.False(TestShim.IsValidOrder(1001, 100));
        Assert.False(TestShim.IsValidOrder(1, -1));
        Assert.True(TestShim.IsValidOrder(1, 0));
    }

    [Fact]
    public void TierDiscountBps_Table()
    {
        Assert.Equal(0, TestShim.TierDiscountBps(0));
        Assert.Equal(500, TestShim.TierDiscountBps(1));
        Assert.Equal(1000, TestShim.TierDiscountBps(2));
        Assert.Equal(1500, TestShim.TierDiscountBps(3));
        Assert.Equal(0, TestShim.TierDiscountBps(4));
        Assert.Equal(0, TestShim.TierDiscountBps(-1));
    }

    [Fact]
    public void Subtotal_Multiplies()
    {
        Assert.Equal(1000, TestShim.Subtotal(2, 500));
    }

    [Fact]
    public void DiscountAmount_TruncatesTowardZero()
    {
        Assert.Equal(49, TestShim.DiscountAmount(999, 1)); // 49.95 -> 49
        Assert.Equal(0, TestShim.DiscountAmount(999, 0));
    }

    [Fact]
    public void TaxAmount_TruncatesTowardZero()
    {
        Assert.Equal(23, TestShim.TaxAmount(475, 500)); // 23.75 -> 23
    }

    [Fact]
    public void OrderTotal_DiscountThenTaxOnNet()
    {
        // sub 1000, disc 100 (tier 2), net 900, tax 90 -> 990
        Assert.Equal(990, TestShim.OrderTotal(2, 500, 2, 1000));
    }

    [Fact]
    public void OrderTotal_InvalidOrder_Zero()
    {
        Assert.Equal(0, TestShim.OrderTotal(0, 500, 2, 1000));
        Assert.Equal(0, TestShim.OrderTotal(1001, 500, 2, 1000));
        Assert.Equal(0, TestShim.OrderTotal(1, -5, 2, 1000));
    }

    // ---- transition matrix: exact, exhaustive ----

    [Fact]
    public void CanTransition_ExactMatrix()
    {
        var allowed = new HashSet<(int, int)>
        {
            (0, 1), (0, 4), (1, 2), (1, 4), (2, 3), (3, 5), (5, 6)
        };
        for (var from = -1; from <= 7; from++)
        {
            for (var to = -1; to <= 7; to++)
            {
                Assert.Equal(allowed.Contains((from, to)), TestShim.CanTransition(from, to));
            }
        }
    }

    [Fact]
    public void ApplyTransition_MovesOnlyWhenLegal()
    {
        Assert.Equal(1, TestShim.ApplyTransition(0, 1));
        Assert.Equal(0, TestShim.ApplyTransition(0, 3));
        Assert.Equal(5, TestShim.ApplyTransition(3, 5));
        Assert.Equal(6, TestShim.ApplyTransition(5, 6));
        Assert.Equal(4, TestShim.ApplyTransition(4, 5));
        Assert.Equal(6, TestShim.ApplyTransition(6, 5));
    }

    // ---- aggregates ----

    [Fact]
    public void RecordOrder_AccumulatesPerCustomer()
    {
        var totals = new Dictionary<string, int>();
        TestShim.RecordOrder(totals, "alice", 100);
        TestShim.RecordOrder(totals, "alice", 50);
        TestShim.RecordOrder(totals, "bob", 30);
        Assert.Equal(150, TestShim.CustomerTotal(totals, "alice"));
        Assert.Equal(30, TestShim.CustomerTotal(totals, "bob"));
    }

    [Fact]
    public void CustomerTotal_AbsentCustomer_Zero()
    {
        var totals = new Dictionary<string, int>();
        Assert.Equal(0, TestShim.CustomerTotal(totals, "ghost"));
    }

    [Fact]
    public void GrandTotal_And_ActiveCustomers()
    {
        var totals = new Dictionary<string, int>();
        Assert.Equal(0, TestShim.GrandTotal(totals));
        Assert.Equal(0, TestShim.ActiveCustomers(totals));
        TestShim.RecordOrder(totals, "alice", 100);
        TestShim.RecordOrder(totals, "bob", 30);
        Assert.Equal(130, TestShim.GrandTotal(totals));
        Assert.Equal(2, TestShim.ActiveCustomers(totals));
    }

    [Fact]
    public void ProcessOrder_Valid_RecordsAndReturnsTotal()
    {
        var totals = new Dictionary<string, int>();
        Assert.Equal(990, TestShim.ProcessOrder(totals, "alice", 2, 500, 2, 1000));
        Assert.Equal(990, TestShim.CustomerTotal(totals, "alice"));
    }

    [Fact]
    public void ProcessOrder_Invalid_ReturnsZero_RecordsNothing()
    {
        var totals = new Dictionary<string, int>();
        Assert.Equal(0, TestShim.ProcessOrder(totals, "alice", 0, 500, 2, 1000));
        Assert.Equal(0, TestShim.ActiveCustomers(totals));
    }

    [Fact]
    public void ProcessOrder_ZeroTotal_NotRecorded()
    {
        var totals = new Dictionary<string, int>();
        Assert.Equal(0, TestShim.ProcessOrder(totals, "alice", 1, 0, 0, 0));
        Assert.Equal(0, TestShim.ActiveCustomers(totals));
    }

    // ---- new operations ----

    [Fact]
    public void RefundAmount_AppliesRestockingFee()
    {
        Assert.Equal(891, TestShim.RefundAmount(2, 500, 2, 1000, 1000)); // 990 - 99
        Assert.Equal(990, TestShim.RefundAmount(2, 500, 2, 1000, 0));
        Assert.Equal(0, TestShim.RefundAmount(2, 500, 2, 1000, 10000));
    }

    [Fact]
    public void RefundAmount_InvalidOrder_Zero()
    {
        Assert.Equal(0, TestShim.RefundAmount(0, 500, 2, 1000, 0));
    }

    [Fact]
    public void RecordRefund_AbsentCustomer_NoOp()
    {
        var totals = new Dictionary<string, int>();
        TestShim.RecordRefund(totals, "ghost", 50);
        Assert.Equal(0, TestShim.ActiveCustomers(totals));
        Assert.Equal(0, TestShim.GrandTotal(totals));
    }

    [Fact]
    public void RecordRefund_PartialRefund_LeavesRemainder()
    {
        var totals = new Dictionary<string, int>();
        TestShim.RecordOrder(totals, "alice", 100);
        TestShim.RecordRefund(totals, "alice", 40);
        Assert.Equal(60, TestShim.CustomerTotal(totals, "alice"));
        Assert.Equal(1, TestShim.ActiveCustomers(totals));
    }

    [Fact]
    public void RecordRefund_ExactRefund_RemovesEntry()
    {
        var totals = new Dictionary<string, int>();
        TestShim.RecordOrder(totals, "alice", 100);
        TestShim.RecordRefund(totals, "alice", 100);
        Assert.Equal(0, TestShim.CustomerTotal(totals, "alice"));
        Assert.Equal(0, TestShim.ActiveCustomers(totals));
        Assert.Equal(0, TestShim.GrandTotal(totals));
    }

    [Fact]
    public void ProcessReturn_OnlyFromDelivered()
    {
        foreach (var state in new[] { 0, 1, 2, 4, 5, 6 })
        {
            var totals = new Dictionary<string, int>();
            TestShim.RecordOrder(totals, "alice", 990);
            Assert.Equal(0, TestShim.ProcessReturn(totals, "alice", state, 2, 500, 2, 1000, 0));
            Assert.Equal(990, TestShim.CustomerTotal(totals, "alice"));
        }
    }

    [Fact]
    public void ProcessReturn_FromDelivered_ReducesTotal()
    {
        var totals = new Dictionary<string, int>();
        Assert.Equal(990, TestShim.ProcessOrder(totals, "alice", 2, 500, 2, 1000));
        Assert.Equal(891, TestShim.ProcessReturn(totals, "alice", 3, 2, 500, 2, 1000, 1000));
        Assert.Equal(99, TestShim.CustomerTotal(totals, "alice"));
        Assert.Equal(1, TestShim.ActiveCustomers(totals));
    }

    [Fact]
    public void ProcessReturn_FullRefund_RemovesCustomer()
    {
        var totals = new Dictionary<string, int>();
        Assert.Equal(990, TestShim.ProcessOrder(totals, "alice", 2, 500, 2, 1000));
        Assert.Equal(990, TestShim.ProcessReturn(totals, "alice", 3, 2, 500, 2, 1000, 0));
        Assert.Equal(0, TestShim.CustomerTotal(totals, "alice"));
        Assert.Equal(0, TestShim.ActiveCustomers(totals));
    }

    [Fact]
    public void ProcessReturn_TotalRestockingFee_ChangesNothing()
    {
        var totals = new Dictionary<string, int>();
        TestShim.ProcessOrder(totals, "alice", 2, 500, 2, 1000);
        Assert.Equal(0, TestShim.ProcessReturn(totals, "alice", 3, 2, 500, 2, 1000, 10000));
        Assert.Equal(990, TestShim.CustomerTotal(totals, "alice"));
    }

    // ---- distant-invariant probe over a mixed sequence ----

    [Fact]
    public void MixedSequence_NetInvariantHolds()
    {
        var totals = new Dictionary<string, int>();
        var orders = 0;
        var refunds = 0;

        orders += TestShim.ProcessOrder(totals, "alice", 1, 1000, 0, 0);   // 1000
        orders += TestShim.ProcessOrder(totals, "alice", 2, 250, 1, 500);  // 498
        orders += TestShim.ProcessOrder(totals, "bob", 3, 100, 3, 1000);   // 280
        orders += TestShim.ProcessOrder(totals, "bob", 0, 100, 3, 1000);   // 0 (invalid)

        refunds += TestShim.ProcessReturn(totals, "alice", 3, 2, 250, 1, 500, 500); // 474
        refunds += TestShim.ProcessReturn(totals, "bob", 4, 3, 100, 3, 1000, 0);    // 0 (not delivered)

        Assert.Equal(1778, orders);
        Assert.Equal(474, refunds);
        Assert.Equal(orders - refunds, TestShim.GrandTotal(totals));
        Assert.Equal(1024, TestShim.CustomerTotal(totals, "alice"));
        Assert.Equal(280, TestShim.CustomerTotal(totals, "bob"));
        Assert.Equal(2, TestShim.ActiveCustomers(totals));
    }
}
