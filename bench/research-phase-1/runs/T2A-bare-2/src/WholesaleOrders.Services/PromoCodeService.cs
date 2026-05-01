namespace WholesaleOrders.Services;

public interface IPromoCodeService
{
    /// <summary>
    /// Returns the discount percentage (e.g. 10m for 10% off) for the given code.
    /// Throws InvalidOperationException for unknown codes.
    /// </summary>
    decimal GetDiscountPercentage(string code);

    /// <summary>
    /// Returns true and sets percentage if the code is recognized.
    /// </summary>
    bool TryGetDiscountPercentage(string code, out decimal percentage);
}

public class PromoCodeService : IPromoCodeService
{
    private static readonly IReadOnlyDictionary<string, decimal> Codes =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["WELCOME10"] = 10m,
            ["BULK25"] = 25m,
        };

    public decimal GetDiscountPercentage(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Promo code must not be empty.");
        if (!Codes.TryGetValue(code, out var pct))
            throw new InvalidOperationException($"Unknown promo code '{code}'.");
        return pct;
    }

    public bool TryGetDiscountPercentage(string code, out decimal percentage)
    {
        if (!string.IsNullOrWhiteSpace(code) && Codes.TryGetValue(code, out var pct))
        {
            percentage = pct;
            return true;
        }
        percentage = 0m;
        return false;
    }
}
