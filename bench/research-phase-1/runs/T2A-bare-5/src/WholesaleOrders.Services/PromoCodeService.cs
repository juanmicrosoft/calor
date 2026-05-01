namespace WholesaleOrders.Services;

/// <summary>
/// Resolves promo codes to flat percentage discounts (0-100).
/// </summary>
public interface IPromoCodeService
{
    /// <summary>
    /// Returns the discount percentage (0-100) for the given promo code.
    /// Throws <see cref="InvalidOperationException"/> if the code is unknown.
    /// </summary>
    decimal GetDiscountPercent(string code);

    /// <summary>
    /// Tries to resolve the discount percentage; returns false for unknown codes.
    /// </summary>
    bool TryGetDiscountPercent(string code, out decimal discountPercent);
}

public class PromoCodeService : IPromoCodeService
{
    // Hardcoded promo codes (case-insensitive lookup).
    private static readonly IReadOnlyDictionary<string, decimal> Codes =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["WELCOME10"] = 10m,
            ["BULK25"] = 25m,
        };

    public decimal GetDiscountPercent(string code)
    {
        if (!TryGetDiscountPercent(code, out var pct))
            throw new InvalidOperationException($"Unknown promo code '{code}'.");
        return pct;
    }

    public bool TryGetDiscountPercent(string code, out decimal discountPercent)
    {
        if (!string.IsNullOrWhiteSpace(code) && Codes.TryGetValue(code, out var pct))
        {
            discountPercent = pct;
            return true;
        }
        discountPercent = 0m;
        return false;
    }
}
