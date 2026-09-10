using ShippingQuote;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class ShippingQuoteHeldOutTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(13, 38)]
    public void Cheapest_PreservesJournal(int weight, int expected)
    {
        var service = new ShippingService();
        QuoteJournal.LastZone = -1;
        var before = QuoteJournal.LastZone;

        Assert.Equal(expected, service.Cheapest(weight));
        var after = QuoteJournal.LastZone;
        Assert.True(before == after,
            $"HELDOUT_EFFECT:state-change before={before}; after={after}");
    }
}
