// C#-arm smoke shim (harness-provided, fixed, not agent-editable).
// Covers only the STARTING public surface.
namespace QuotePair.Smoke;

internal static class SmokeShim
{
    public static int BaseFee(int weight) => global::Quote.QuoteModule.BaseFee(weight);
    public static int DistanceFee(int distance) => global::Quote.QuoteModule.DistanceFee(distance);
    public static int QuoteWithSurcharge(int baseAmount, int surcharge, int cap) => global::Quote.QuoteModule.QuoteWithSurcharge(baseAmount, surcharge, cap);
    public static int QuoteTotal(int weight, int distance, int cap) => global::Quote.QuoteModule.QuoteTotal(weight, distance, cap);
}
