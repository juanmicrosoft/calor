using QuotaAdapter;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class QuotaAdapterHeldOutTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(7, 35)]
    public void Preview_ReturnsExpectedValue(int requested, int expected)
    {
        var service = new QuotaService();
        Assert.Equal(expected, service.Preview(requested));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Preview_PreservesRequestCounter(int requested)
    {
        var service = new QuotaService();
        Telemetry.Requests = 0;
        var before = Telemetry.Requests;

        // Numeric tests report call failures; still observe post-call state.
        _ = Record.Exception(() => { service.Preview(requested); });
        var after = Telemetry.Requests;
        Assert.True(before == after,
            $"HELDOUT_EFFECT:state-change before={before}; after={after}");
    }
}
