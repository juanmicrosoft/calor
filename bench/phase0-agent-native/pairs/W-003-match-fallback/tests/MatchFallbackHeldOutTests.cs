// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim.
// All tests live in ONE class so xUnit runs them sequentially: the
// effect-observing tests redirect Console.Out, which is process-global.
using Xunit;

namespace MatchFallback.HeldOut;

public sealed class MatchFallbackHeldOutTests
{
    private static readonly object ConsoleGate = new();

    private static string Capture(Func<int> body, out int result)
    {
        lock (ConsoleGate)
        {
            var original = Console.Out;
            using var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                result = body();
            }
            finally
            {
                Console.SetOut(original);
            }
            return writer.ToString().Replace("\r\n", "\n");
        }
    }

    [Fact]
    public void MatchOption_PicksTheRightBranch()
    {
        Assert.Equal(11, TestShim.MatchOption(true, 10, x => x + 1, () => -1));
        Assert.Equal(-1, TestShim.MatchOption(false, 10, x => x + 1, () => -1));
    }

    [Fact]
    public void BothPure_IsSilentAndCorrect()
    {
        string output = Capture(() => TestShim.BothPure(true, 4) + TestShim.BothPure(false, 4), out int result);
        Assert.Equal(4, result);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void OneImpure_PrintsValueWhenPresent()
    {
        string output = Capture(() => TestShim.OneImpure(true, 9), out int result);
        Assert.Equal(9, result);
        Assert.Equal("9\n", output);
    }

    [Fact]
    public void Fallback_ReturnsValueSilentlyWhenPresent()
    {
        string output = Capture(() => TestShim.Fallback(true, 5), out int result);
        Assert.Equal(5, result);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void Fallback_PrintsFallbackAndReturnsZeroWhenAbsent()
    {
        string output = Capture(() => TestShim.Fallback(false, 5), out int result);
        Assert.Equal(0, result);
        Assert.Equal("fallback\n", output);
    }

    [Fact]
    public void Sum2_BothPresent()
    {
        Capture(() => TestShim.Sum2(true, 2, true, 3), out int result);
        Assert.Equal(5, result);
    }

    [Fact]
    public void Sum2_OneAbsent()
    {
        Capture(() => TestShim.Sum2(true, 1, false, 9), out int result);
        Assert.Equal(1, result);
    }

    [Fact]
    public void Sum2_BothAbsent()
    {
        Capture(() => TestShim.Sum2(false, 4, false, 4), out int result);
        Assert.Equal(0, result);
    }

    // Effect-observing test: the pure path must stay silent when a flag is false.
    [Fact]
    public void Sum2_IsSilent_OneAbsent()
    {
        string output = Capture(() => TestShim.Sum2(true, 1, false, 9), out int result);
        Assert.Equal(1, result);
        Assert.Equal(string.Empty, output);
    }

    // Effect-observing test: silent when both flags are false.
    [Fact]
    public void Sum2_IsSilent_BothAbsent()
    {
        string output = Capture(() => TestShim.Sum2(false, 4, false, 4), out int result);
        Assert.Equal(0, result);
        Assert.Equal(string.Empty, output);
    }
}
