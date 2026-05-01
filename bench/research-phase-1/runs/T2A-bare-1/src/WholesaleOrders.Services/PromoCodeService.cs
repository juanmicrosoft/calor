namespace WholesaleOrders.Services;

public interface IPromoCodeService
{
    /// <summary>
    /// Returns the discount percent (e.g. 10m for 10%) for the given promo code.
    /// Throws <see cref="InvalidOperationException"/> when the code is unknown.
    /// </summary>
    decimal GetDiscountPercent(string code);

    /// <summary>
    /// Tries to resolve a promo code. Returns false when the code is not recognized.
    /// </summary>
    bool TryGetDiscountPercent(string code, out decimal percent);
}

public class PromoCodeService : IPromoCodeService
{
    private static readonly IReadOnlyDictionary<string, decimal> Codes =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["WELCOME10"] = 10m,
            ["BULK25"] = 25m,
        };

    public decimal GetDiscountPercent(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Promo code must not be empty.");
        if (!Codes.TryGetValue(code, out var pct))
            throw new InvalidOperationException($"Unknown promo code '{code}'.");
        return pct;
    }

    public bool TryGetDiscountPercent(string code, out decimal percent)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            percent = 0m;
            return false;
        }
        return Codes.TryGetValue(code, out percent);
    }
}
