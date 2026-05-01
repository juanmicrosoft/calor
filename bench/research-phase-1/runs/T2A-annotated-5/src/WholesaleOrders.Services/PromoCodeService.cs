namespace WholesaleOrders.Services;

public interface IPromoCodeService
{
    /// <summary>
    /// Returns the discount percentage (e.g. 0.10m for 10%) for a known code,
    /// or null if the code is unknown. Lookup is case-insensitive.
    /// </summary>
    decimal? GetDiscountPercent(string? code);
}

public class PromoCodeService : IPromoCodeService
{
    // Hardcoded promo codes. Future work: move to configuration/persistence.
    private static readonly Dictionary<string, decimal> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WELCOME10"] = 0.10m,
        ["BULK25"] = 0.25m,
    };

    // PURE: no effects.
    public decimal? GetDiscountPercent(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return Codes.TryGetValue(code, out var pct) ? pct : null;
    }
}
