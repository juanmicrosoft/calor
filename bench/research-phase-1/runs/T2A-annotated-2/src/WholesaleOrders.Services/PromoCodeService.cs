namespace WholesaleOrders.Services;

public interface IPromoCodeService
{
    /// <summary>
    /// Returns the discount percent (e.g. 0.10m for 10% off) for a known promo code.
    /// Throws ArgumentException for unknown codes (mapped to HTTP 400 by middleware).
    /// </summary>
    decimal GetDiscountPercent(string code);

    /// <summary>
    /// True if the given code is recognized.
    /// </summary>
    bool TryGetDiscountPercent(string code, out decimal percent);
}

public class PromoCodeService : IPromoCodeService
{
    // Hardcoded for now per spec. Case-insensitive lookup.
    private static readonly IReadOnlyDictionary<string, decimal> Codes =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["WELCOME10"] = 0.10m,
            ["BULK25"] = 0.25m,
        };

    // PURE: no effects.
    public bool TryGetDiscountPercent(string code, out decimal percent)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            percent = 0m;
            return false;
        }
        return Codes.TryGetValue(code, out percent);
    }

    // PURE: no effects. Throws for unknown codes.
    public decimal GetDiscountPercent(string code)
    {
        if (!TryGetDiscountPercent(code, out var percent))
            throw new ArgumentException($"Unknown promo code '{code}'.", nameof(code));
        return percent;
    }
}
