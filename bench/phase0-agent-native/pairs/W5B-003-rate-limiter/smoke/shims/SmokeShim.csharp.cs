// C#-arm smoke shim (harness-provided, fixed, not agent-editable).
// Covers only the STARTING public surface.
namespace RateLimitPair.Smoke;

internal static class SmokeShim
{
    public static int BaseAllowance(int tier) => global::RateLimit.RateLimitModule.BaseAllowance(tier);
    public static int BurstBonus(int priority) => global::RateLimit.RateLimitModule.BurstBonus(priority);
    public static int GrantRequests(int requested, int maxAllowed) => global::RateLimit.RateLimitModule.GrantRequests(requested, maxAllowed);
    public static int GrantForTier(int tier, int priority, int maxAllowed) => global::RateLimit.RateLimitModule.GrantForTier(tier, priority, maxAllowed);
}
