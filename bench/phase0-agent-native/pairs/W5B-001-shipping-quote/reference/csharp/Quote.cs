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
        if (total > cap)
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

    /// <summary>The capped quote, but never below floor; floor wins over cap.</summary>
    public static int QuoteWithFloor(int baseAmount, int surcharge, int cap, int floor)
    {
        var capped = QuoteWithSurcharge(baseAmount, surcharge, cap);
        if (capped < floor)
        {
            return floor;
        }

        return capped;
    }

    /// <summary>Pure formatting.</summary>
    public static string FormatQuote(int amount) => "quote: " + amount.ToString();
}
