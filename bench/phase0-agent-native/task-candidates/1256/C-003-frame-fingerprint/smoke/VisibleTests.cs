using FrameFingerprint;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class FrameFingerprintVisibleTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 2, 103)]
    [InlineData(2, 1, 203)]
    [InlineData(-1, 15, 15)]
    [InlineData(15, -1, 1515)]
    [InlineData(101, 5, 10105)]
    [InlineData(5, 101, 605)]
    [InlineData(int.MinValue, int.MaxValue, 100)]
    [InlineData(100, 100, 10200)]
    public void Fingerprint_NormalizesEachKeyBeforeCombining(int first, int second, int expected)
    {
        var codec = new FrameCodec();
        Assert.Equal(expected, codec.Fingerprint(first, second));
    }

    [Fact]
    public void ConsecutivePairsKeepTheirOrder()
    {
        var codec = new FrameCodec();
        Assert.Equal(103, codec.Fingerprint(1, 2));
        Assert.Equal(203, codec.Fingerprint(2, 1));
        Assert.Equal(103, codec.Fingerprint(1, 2));
    }
}
