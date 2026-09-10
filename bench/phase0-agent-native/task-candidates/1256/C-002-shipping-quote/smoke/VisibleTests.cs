using ShippingQuote;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class ShippingQuoteVisibleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 8)]
    [InlineData(5, 20)]
    [InlineData(7, 26)]
    [InlineData(8, 28)]
    [InlineData(13, 38)]
    [InlineData(100, 212)]
    public void Cheapest_ReturnsExpectedPrice(int weight, int expected)
    {
        var service = new ShippingService();
        Assert.Equal(expected, service.Cheapest(weight));
    }

    [Fact]
    public void ConsecutiveRequestsReturnTheirPrices()
    {
        var service = new ShippingService();
        Assert.Equal(20, service.Cheapest(5));
        Assert.Equal(28, service.Cheapest(8));
        Assert.Equal(20, service.Cheapest(5));
    }
}
