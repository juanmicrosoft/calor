namespace WholesaleOrders.Services;

public interface IPromoCodeService
{
    /// <summary>
    /// Returns true and sets discountPercentage when the code is recognized.
    /// Codes are matched case-insensitively. Whitespace is trimmed.
    /// </summary>
    bool TryGetDiscountPercentage(string code, out decimal discountPercentage);
}

public class PromoCodeService : IPromoCodeService
{
    // Hardcoded for now; later this could move to configuration or a repository.
    private static readonly IReadOnlyDictionary<string, decimal> Codes =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["WELCOME10"] = 10m,
            ["BULK25"] = 25m,
        };

    // PURE: no effects.
    public bool TryGetDiscountPercentage(string code, out decimal discountPercentage)
    {
        discountPercentage = 0m;
        if (string.IsNullOrWhiteSpace(code)) return false;
        return Codes.TryGetValue(code.Trim(), out discountPercentage);
    }
}
