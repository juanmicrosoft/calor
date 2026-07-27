// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace RateLimitPair.Harness;

internal static class TestShim
{
    public static int BaseAllowance(int tier) => global::RateLimit.RateLimitModule.BaseAllowance(tier);
    public static int BurstBonus(int priority) => global::RateLimit.RateLimitModule.BurstBonus(priority);
    public static int GrantRequests(int requested, int maxAllowed) => global::RateLimit.RateLimitModule.GrantRequests(requested, maxAllowed);
    public static int GrantForTier(int tier, int priority, int maxAllowed) => global::RateLimit.RateLimitModule.GrantForTier(tier, priority, maxAllowed);
    public static int GrantWithMinimum(int requested, int maxAllowed, int minGrant) => global::RateLimit.RateLimitModule.GrantWithMinimum(requested, maxAllowed, minGrant);
    public static string FormatGrant(int amount) => global::RateLimit.RateLimitModule.FormatGrant(amount);
}
