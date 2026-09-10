using QuotaAdapter;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class QuotaAdapterHeldOutTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(7, 35)]
    public void Preview_PreservesRequestCounter(int requested, int expected)
    {
        var service = new QuotaService();
        Telemetry.Requests = 0;
        var before = Telemetry.Requests;

        Assert.Equal(expected, service.Preview(requested));
        var after = Telemetry.Requests;
        Assert.True(before == after,
            $"HELDOUT_EFFECT:state-change before={before}; after={after}");
    }
}
