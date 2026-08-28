// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim.
// All tests live in ONE class so xUnit runs them sequentially: the
// effect-observing tests redirect Console.Out, which is process-global.
using Xunit;

namespace CounterPeek.HeldOut;

public sealed class CounterPeekHeldOutTests
{
    private static readonly object ConsoleGate = new();

    private static string Capture(Action body)
    {
        lock (ConsoleGate)
        {
            var original = Console.Out;
            using var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                body();
            }
            finally
            {
                Console.SetOut(original);
            }
            return writer.ToString().Replace("\r\n", "\n");
        }
    }

    [Fact]
    public void Bump_PrintsRunningTotal()
    {
        var counter = TestShim.NewCounter();
        string output = Capture(() => { TestShim.Bump(counter, 3); TestShim.Bump(counter, 4); });
        Assert.Equal("3\n7\n", output);
    }

    [Fact]
    public void Peek_ReturnsTotalPlusN_OnFreshCounter()
    {
        var counter = TestShim.NewCounter();
        int peeked = 0;
        Capture(() => { peeked = TestShim.Peek(counter, 5); });
        Assert.Equal(5, peeked);
    }

    [Fact]
    public void Peek_ReturnsTotalPlusN_AfterBump()
    {
        var counter = TestShim.NewCounter();
        int peeked = 0;
        Capture(() => { TestShim.Bump(counter, 3); peeked = TestShim.Peek(counter, 5); });
        Assert.Equal(8, peeked);
    }

    [Fact]
    public void Peek_DoesNotChangeTotal()
    {
        var counter = TestShim.NewCounter();
        string output = Capture(() =>
        {
            TestShim.Bump(counter, 3);
            TestShim.Peek(counter, 5);
            TestShim.Bump(counter, 1);
        });
        Assert.EndsWith("4\n", output);
    }

    // Effect-observing test: the pure path must stay silent on a fresh counter.
    [Fact]
    public void Peek_IsSilent_OnFreshCounter()
    {
        var counter = TestShim.NewCounter();
        string output = Capture(() => { TestShim.Peek(counter, 5); });
        Assert.Equal(string.Empty, output);
    }

    // Effect-observing test: the pure path must stay silent after a bump.
    [Fact]
    public void Peek_IsSilent_AfterBump()
    {
        var counter = TestShim.NewCounter();
        Capture(() => { TestShim.Bump(counter, 3); });
        string output = Capture(() => { TestShim.Peek(counter, 5); });
        Assert.Equal(string.Empty, output);
    }
}
