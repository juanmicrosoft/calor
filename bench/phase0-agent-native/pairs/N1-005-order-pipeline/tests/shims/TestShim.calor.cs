// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace OrderPipeline.HeldOut;

internal static class TestShim
{
    public static bool IsValidOrder(int qty, int unitPrice) => global::OrderPipeline.OrderPipelineModule.IsValidOrder(qty, unitPrice);
    public static int TierDiscountBps(int tier) => global::OrderPipeline.OrderPipelineModule.TierDiscountBps(tier);
    public static int Subtotal(int qty, int unitPrice) => global::OrderPipeline.OrderPipelineModule.Subtotal(qty, unitPrice);
    public static int DiscountAmount(int subtotal, int tier) => global::OrderPipeline.OrderPipelineModule.DiscountAmount(subtotal, tier);
    public static int TaxAmount(int amount, int taxRateBps) => global::OrderPipeline.OrderPipelineModule.TaxAmount(amount, taxRateBps);
    public static int OrderTotal(int qty, int unitPrice, int tier, int taxRateBps) => global::OrderPipeline.OrderPipelineModule.OrderTotal(qty, unitPrice, tier, taxRateBps);
    public static bool CanTransition(int fromState, int toState) => global::OrderPipeline.OrderPipelineModule.CanTransition(fromState, toState);
    public static int ApplyTransition(int state, int toState) => global::OrderPipeline.OrderPipelineModule.ApplyTransition(state, toState);
    public static void RecordOrder(Dictionary<string, int> totals, string customer, int amount) => global::OrderPipeline.OrderPipelineModule.RecordOrder(totals, customer, amount);
    public static int CustomerTotal(Dictionary<string, int> totals, string customer) => global::OrderPipeline.OrderPipelineModule.CustomerTotal(totals, customer);
    public static int GrandTotal(Dictionary<string, int> totals) => global::OrderPipeline.OrderPipelineModule.GrandTotal(totals);
    public static int ActiveCustomers(Dictionary<string, int> totals) => global::OrderPipeline.OrderPipelineModule.ActiveCustomers(totals);
    public static int ProcessOrder(Dictionary<string, int> totals, string customer, int qty, int unitPrice, int tier, int taxRateBps) => global::OrderPipeline.OrderPipelineModule.ProcessOrder(totals, customer, qty, unitPrice, tier, taxRateBps);
    public static int RefundAmount(int qty, int unitPrice, int tier, int taxRateBps, int restockFeeBps) => global::OrderPipeline.OrderPipelineModule.RefundAmount(qty, unitPrice, tier, taxRateBps, restockFeeBps);
    public static void RecordRefund(Dictionary<string, int> totals, string customer, int amount) => global::OrderPipeline.OrderPipelineModule.RecordRefund(totals, customer, amount);
    public static int ProcessReturn(Dictionary<string, int> totals, string customer, int state, int qty, int unitPrice, int tier, int taxRateBps, int restockFeeBps) => global::OrderPipeline.OrderPipelineModule.ProcessReturn(totals, customer, state, qty, unitPrice, tier, taxRateBps, restockFeeBps);
}
