using FrameFingerprint;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class FrameFingerprintHeldOutTests
{
    [Theory]
    [InlineData(1, 2, 103)]
    [InlineData(101, 5, 10105)]
    public void Fingerprint_PreservesJournal(int first, int second, int expected)
    {
        var codec = new FrameCodec();
        LookupJournal.LastKey = -1;
        var before = LookupJournal.LastKey;

        Assert.Equal(expected, codec.Fingerprint(first, second));
        var after = LookupJournal.LastKey;
        Assert.True(before == after,
            $"HELDOUT_EFFECT:state-change before={before}; after={after}");
    }
}
