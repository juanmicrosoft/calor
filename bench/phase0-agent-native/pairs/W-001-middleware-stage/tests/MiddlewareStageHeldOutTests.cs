// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim.
// All tests live in ONE class so xUnit runs them sequentially: the
// effect-observing tests redirect Console.Out, which is process-global.
using Xunit;

namespace MiddlewareStage.HeldOut;

public sealed class MiddlewareStageHeldOutTests
{
    private static readonly object ConsoleGate = new();

    // Runs `body` with Console.Out captured and returns everything written.
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
    public void RunTwice_CallsStepTwiceAndSums()
    {
        int calls = 0;
        int total = TestShim.RunTwice(() => { calls++; return 5; });
        Assert.Equal(10, total);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Handle_ReturnsRunTwiceOfNext()
    {
        var behavior = TestShim.NewRetryBehavior();
        int calls = 0;
        int total = TestShim.Handle(behavior, 7, () => { calls++; return 3; });
        Assert.Equal(6, total);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Beat_PrintsBeatAndReturnsOne()
    {
        string output = Capture(() => TestShim.Beat(), out int result);
        Assert.Equal(1, result);
        Assert.Equal("beat\n", output);
    }

    [Fact]
    public void Probe_ReturnsTwoAndPrintsBeatTwice()
    {
        var behavior = TestShim.NewRetryBehavior();
        string output = Capture(() => TestShim.Probe(behavior), out int result);
        Assert.Equal(2, result);
        Assert.Equal("beat\nbeat\n", output);
    }

    [Fact]
    public void Twice_ReturnsTwo()
    {
        var behavior = TestShim.NewRetryBehavior();
        Capture(() => TestShim.Twice(behavior), out int result);
        Assert.Equal(2, result);
    }

    // Effect-observing test: the pure path must stay silent.
    [Fact]
    public void Twice_IsSilent_OnFreshBehavior()
    {
        var behavior = TestShim.NewRetryBehavior();
        string output = Capture(() => TestShim.Twice(behavior), out _);
        Assert.Equal(string.Empty, output);
    }

    // Effect-observing test: still silent after the loud path has run.
    [Fact]
    public void Twice_IsSilent_AfterProbe()
    {
        var behavior = TestShim.NewRetryBehavior();
        Capture(() => TestShim.Probe(behavior), out _);
        string output = Capture(() => TestShim.Twice(behavior), out int result);
        Assert.Equal(2, result);
        Assert.Equal(string.Empty, output);
    }
}
