namespace WholesaleOrders.Services;

public interface IPromoCodeService
{
    /// <summary>
    /// Returns the discount percent (e.g. 10m for 10%) for a known promo code.
    /// Throws <see cref="ArgumentException"/> when the code is not recognized.
    /// </summary>
    decimal GetDiscountPercent(string code);

    /// <summary>
    /// Returns true and outputs the discount percent when the code is recognized.
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
            throw new ArgumentException("Promo code must not be empty.", nameof(code));
        if (!Codes.TryGetValue(code, out var pct))
            throw new ArgumentException($"Unknown promo code '{code}'.", nameof(code));
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
