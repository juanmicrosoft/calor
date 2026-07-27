// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace QuotaPair.Harness;

internal static class TestShim
{
    public static int BaseAllowance(int tier) => global::Quota.QuotaModule.BaseAllowance(tier);
    public static int BurstBonus(int priority) => global::Quota.QuotaModule.BurstBonus(priority);
    public static int GrantRequests(int requested, int maxAllowed) => global::Quota.QuotaModule.GrantRequests(requested, maxAllowed);
    public static int GrantForTier(int tier, int priority, int maxAllowed) => global::Quota.QuotaModule.GrantForTier(tier, priority, maxAllowed);
    public static int GrantWithMinimum(int requested, int maxAllowed, int minGrant) => global::Quota.QuotaModule.GrantWithMinimum(requested, maxAllowed, minGrant);
    public static string FormatGrant(int amount) => global::Quota.QuotaModule.FormatGrant(amount);
}
