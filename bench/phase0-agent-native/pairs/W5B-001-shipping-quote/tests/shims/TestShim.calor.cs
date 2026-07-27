// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace QuotePair.Harness;

internal static class TestShim
{
    public static int BaseFee(int weight) => global::Quote.QuoteModule.BaseFee(weight);
    public static int DistanceFee(int distance) => global::Quote.QuoteModule.DistanceFee(distance);
    public static int QuoteWithSurcharge(int baseAmount, int surcharge, int cap) => global::Quote.QuoteModule.QuoteWithSurcharge(baseAmount, surcharge, cap);
    public static int QuoteTotal(int weight, int distance, int cap) => global::Quote.QuoteModule.QuoteTotal(weight, distance, cap);
    public static int QuoteWithFloor(int baseAmount, int surcharge, int cap, int floor) => global::Quote.QuoteModule.QuoteWithFloor(baseAmount, surcharge, cap, floor);
    public static string FormatQuote(int amount) => global::Quote.QuoteModule.FormatQuote(amount);
}
