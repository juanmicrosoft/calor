using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class HeldOutTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Preview_PreservesSharedState(int input)
    {
        var invoke = Adapter.Create();
        var before = Adapter.ReadState();
        var actual = invoke(input);

        Assert.Equal(Adapter.Expected(input), actual);
        Assert.Equal(before, Adapter.ReadState());
    }
}
