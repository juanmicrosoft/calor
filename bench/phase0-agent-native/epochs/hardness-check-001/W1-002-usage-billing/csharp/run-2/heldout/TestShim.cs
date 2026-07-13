// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace Billing.HeldOut;

internal static class TestShim
{
    public static int BillBasic(int units) => BillingLib.Billing.BillBasic(units);
    public static int BillPro(int units) => BillingLib.Billing.BillPro(units);
    public static int BillCapped(int units, int cap) => BillingLib.Billing.BillCapped(units, cap);
    public static int CheaperPlan(int units) => BillingLib.Billing.CheaperPlan(units);
}
