using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class HeldOutTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Preview_HasNoConsoleWrites(int input)
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        int actual;
        try
        {
            Console.SetOut(output);
            actual = Adapter.Invoke(input);
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Equal(Adapter.Expected(input), actual);
        Assert.Equal("", output.ToString());
    }
}
