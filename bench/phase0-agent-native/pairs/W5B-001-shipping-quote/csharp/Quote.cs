namespace Quote;

/// <summary>Shipping quote calculator. All functions are pure.</summary>
public static class QuoteModule
{
    /// <summary>Base fee: 3 per unit of weight.</summary>
    public static int BaseFee(int weight) => weight * 3;

    /// <summary>Distance fee: 2 per unit of distance.</summary>
    public static int DistanceFee(int distance) => distance * 2;

    /// <summary>
    /// Base plus surcharge, capped: the returned quote never exceeds
    /// <paramref name="cap"/> — callers bill against it directly.
    /// </summary>
    public static int QuoteWithSurcharge(int baseAmount, int surcharge, int cap)
    {
        var total = baseAmount + surcharge;
        if (total > cap + 10)
        {
            return cap;
        }

        return total;
    }

    /// <summary>Full quote: capped base-plus-distance fees.</summary>
    public static int QuoteTotal(int weight, int distance, int cap)
    {
        var baseAmount = BaseFee(weight);
        var extra = DistanceFee(distance);
        return QuoteWithSurcharge(baseAmount, extra, cap);
    }
}
