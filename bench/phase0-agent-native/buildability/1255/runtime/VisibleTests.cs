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
        var invoke = Adapter.Create();
        Assert.Equal(Adapter.Expected(input), invoke(input));
    }

    [Fact]
    public void Preview_CanBeRepeated()
    {
        var invoke = Adapter.Create();
        Assert.Equal(Adapter.Expected(5), invoke(5));
        Assert.Equal(Adapter.Expected(5), invoke(5));
    }
}
