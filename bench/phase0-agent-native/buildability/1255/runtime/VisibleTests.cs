using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class VisibleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(13)]
    public void Preview_ReturnsExpectedValue(int input)
    {
        Assert.Equal(Adapter.Expected(input), Adapter.Invoke(input));
    }

    [Fact]
    public void Preview_CanBeRepeated()
    {
        Assert.Equal(Adapter.Expected(5), Adapter.Invoke(5));
        Assert.Equal(Adapter.Expected(5), Adapter.Invoke(5));
    }
}
