// Calor-arm smoke shim (harness-provided, fixed, not agent-editable).
// Covers only the STARTING public surface.
namespace QuotaPair.Smoke;

internal static class SmokeShim
{
    public static int BaseAllowance(int tier) => global::Quota.QuotaModule.BaseAllowance(tier);
    public static int BurstBonus(int priority) => global::Quota.QuotaModule.BurstBonus(priority);
    public static int GrantRequests(int requested, int maxAllowed) => global::Quota.QuotaModule.GrantRequests(requested, maxAllowed);
    public static int GrantForTier(int tier, int priority, int maxAllowed) => global::Quota.QuotaModule.GrantForTier(tier, priority, maxAllowed);
}
