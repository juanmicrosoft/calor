// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace Billing.HeldOut;

internal static class TestShim
{
    public static int BillBasic(int units) => global::Billing.BillingModule.BillBasic(units);
    public static int BillPro(int units) => global::Billing.BillingModule.BillPro(units);
    public static int BillCapped(int units, int cap) => global::Billing.BillingModule.BillCapped(units, cap);
    public static int CheaperPlan(int units) => global::Billing.BillingModule.CheaperPlan(units);
}
