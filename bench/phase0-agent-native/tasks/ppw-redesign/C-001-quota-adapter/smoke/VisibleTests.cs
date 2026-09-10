using QuotaAdapter;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class QuotaAdapterVisibleTests
{
    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(10, 50)]
    [InlineData(11, 50)]
    [InlineData(int.MaxValue, 50)]
    public void Preview_ReturnsQuotaForRequestedSlots(int requested, int expected)
    {
        var service = new QuotaService();
        Assert.Equal(expected, service.Preview(requested));
    }

    [Fact]
    public void ConsecutiveRequestsReturnTheirQuotas()
    {
        var service = new QuotaService();
        Assert.Equal(5, service.Preview(1));
        Assert.Equal(50, service.Preview(11));
        Assert.Equal(5, service.Preview(1));
    }
}
