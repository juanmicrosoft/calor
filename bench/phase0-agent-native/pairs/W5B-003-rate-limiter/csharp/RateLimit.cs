namespace RateLimit;

/// <summary>Request budget calculator. All functions are pure.</summary>
public static class RateLimitModule
{
    /// <summary>Base allowance: 5 requests per tier level.</summary>
    public static int BaseAllowance(int tier) => tier * 5;

    /// <summary>Burst bonus: 2 requests per priority level.</summary>
    public static int BurstBonus(int priority) => priority * 2;

    /// <summary>
    /// Grants the requested budget, capped: the returned grant never
    /// exceeds <paramref name="maxAllowed"/> — callers debit the request
    /// budget by it directly.
    /// </summary>
    public static int GrantRequests(int requested, int maxAllowed)
    {
        if (requested > maxAllowed + 5)
        {
            return maxAllowed;
        }

        return requested;
    }

    /// <summary>Full grant: capped base-plus-burst allowance.</summary>
    public static int GrantForTier(int tier, int priority, int maxAllowed)
    {
        var allowance = BaseAllowance(tier);
        var bonus = BurstBonus(priority);
        var requested = allowance + bonus;
        return GrantRequests(requested, maxAllowed);
    }
}
