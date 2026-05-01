namespace WholesaleOrders.Services;

public interface IPromoCodeService
{
    /// <summary>
    /// Returns the discount percentage (0-100) for the given promo code.
    /// Throws InvalidOperationException for unknown codes.
    /// </summary>
    decimal GetDiscountPercent(string promoCode);

    bool TryGetDiscountPercent(string promoCode, out decimal percent);
}

public class PromoCodeService : IPromoCodeService
{
    private static readonly IReadOnlyDictionary<string, decimal> Codes =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["WELCOME10"] = 10m,
            ["BULK25"] = 25m,
        };

    public decimal GetDiscountPercent(string promoCode)
    {
        if (string.IsNullOrWhiteSpace(promoCode))
            throw new InvalidOperationException("Promo code is empty.");
        if (!Codes.TryGetValue(promoCode, out var percent))
            throw new InvalidOperationException($"Unknown promo code '{promoCode}'.");
        return percent;
    }

    public bool TryGetDiscountPercent(string promoCode, out decimal percent)
    {
        if (string.IsNullOrWhiteSpace(promoCode))
        {
            percent = 0m;
            return false;
        }
        return Codes.TryGetValue(promoCode, out percent);
    }
}
