namespace WholesaleOrders.Services;

public interface IPromoCodeService
{
    /// <summary>
    /// Resolves a promo code to a discount percentage in the range [0, 1].
    /// Throws <see cref="InvalidOperationException"/> when the code is unknown.
    /// </summary>
    decimal GetDiscountPercentage(string code);

    /// <summary>
    /// Returns true and outputs the discount percentage if the code is known; otherwise false.
    /// </summary>
    bool TryGetDiscountPercentage(string code, out decimal discountPercentage);
}

public class PromoCodeService : IPromoCodeService
{
    // Hardcoded promo codes per spec. Keys are case-insensitive at lookup time.
    private static readonly IReadOnlyDictionary<string, decimal> Codes =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["WELCOME10"] = 0.10m,
            ["BULK25"] = 0.25m,
        };

    // PURE: no effects. POSTCONDITION: result in [0, 1].
    public decimal GetDiscountPercentage(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Promo code must not be empty.");
        if (!Codes.TryGetValue(code, out var pct))
            throw new InvalidOperationException($"Unknown promo code '{code}'.");
        return pct;
    }

    // PURE: no effects.
    public bool TryGetDiscountPercentage(string code, out decimal discountPercentage)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            discountPercentage = 0m;
            return false;
        }
        return Codes.TryGetValue(code, out discountPercentage);
    }
}
