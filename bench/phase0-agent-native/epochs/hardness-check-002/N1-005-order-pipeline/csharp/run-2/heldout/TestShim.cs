// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace OrderPipeline.HeldOut;

internal static class TestShim
{
    public static bool IsValidOrder(int qty, int unitPrice) => OrderPipelineLib.OrderPipeline.IsValidOrder(qty, unitPrice);
    public static int TierDiscountBps(int tier) => OrderPipelineLib.OrderPipeline.TierDiscountBps(tier);
    public static int Subtotal(int qty, int unitPrice) => OrderPipelineLib.OrderPipeline.Subtotal(qty, unitPrice);
    public static int DiscountAmount(int subtotal, int tier) => OrderPipelineLib.OrderPipeline.DiscountAmount(subtotal, tier);
    public static int TaxAmount(int amount, int taxRateBps) => OrderPipelineLib.OrderPipeline.TaxAmount(amount, taxRateBps);
    public static int OrderTotal(int qty, int unitPrice, int tier, int taxRateBps) => OrderPipelineLib.OrderPipeline.OrderTotal(qty, unitPrice, tier, taxRateBps);
    public static bool CanTransition(int fromState, int toState) => OrderPipelineLib.OrderPipeline.CanTransition(fromState, toState);
    public static int ApplyTransition(int state, int toState) => OrderPipelineLib.OrderPipeline.ApplyTransition(state, toState);
    public static void RecordOrder(Dictionary<string, int> totals, string customer, int amount) => OrderPipelineLib.OrderPipeline.RecordOrder(totals, customer, amount);
    public static int CustomerTotal(Dictionary<string, int> totals, string customer) => OrderPipelineLib.OrderPipeline.CustomerTotal(totals, customer);
    public static int GrandTotal(Dictionary<string, int> totals) => OrderPipelineLib.OrderPipeline.GrandTotal(totals);
    public static int ActiveCustomers(Dictionary<string, int> totals) => OrderPipelineLib.OrderPipeline.ActiveCustomers(totals);
    public static int ProcessOrder(Dictionary<string, int> totals, string customer, int qty, int unitPrice, int tier, int taxRateBps) => OrderPipelineLib.OrderPipeline.ProcessOrder(totals, customer, qty, unitPrice, tier, taxRateBps);
    public static int RefundAmount(int qty, int unitPrice, int tier, int taxRateBps, int restockFeeBps) => OrderPipelineLib.OrderPipeline.RefundAmount(qty, unitPrice, tier, taxRateBps, restockFeeBps);
    public static void RecordRefund(Dictionary<string, int> totals, string customer, int amount) => OrderPipelineLib.OrderPipeline.RecordRefund(totals, customer, amount);
    public static int ProcessReturn(Dictionary<string, int> totals, string customer, int state, int qty, int unitPrice, int tier, int taxRateBps, int restockFeeBps) => OrderPipelineLib.OrderPipeline.ProcessReturn(totals, customer, state, qty, unitPrice, tier, taxRateBps, restockFeeBps);
}
