// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim.
// All tests live in ONE class so xUnit runs them sequentially: the
// effect-observing tests redirect Console.Out, which is process-global.
using Xunit;

namespace PipelineTrace.HeldOut;

public sealed class PipelineTraceHeldOutTests
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

    private sealed class RecordingPreProcessor : TestShim.IPreProcessor
    {
        public List<string> Seen { get; } = new();
        public Task Process(string request, CancellationToken cancellationToken)
        {
            Seen.Add(request);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Handle_NoPreProcessors_ReturnsNextResult()
    {
        Capture(() => TestShim.Handle(new TestShim.IPreProcessor[0], "hello", () => 42), out int result);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Handle_RunsPreProcessorsInOrder_ThenNext()
    {
        var first = new RecordingPreProcessor();
        var second = new RecordingPreProcessor();
        var order = new List<string>();
        Capture(() => TestShim.Handle(new TestShim.IPreProcessor[] { first, second }, "req", () => { order.Add("next"); return 1; }), out int result);
        Assert.Equal(1, result);
        Assert.Equal(new[] { "req" }, first.Seen);
        Assert.Equal(new[] { "req" }, second.Seen);
        Assert.Equal(new[] { "next" }, order);
    }

    [Fact]
    public void TracePreProcessor_PrintsPreAndRequest()
    {
        string output = Capture(() => TestShim.Handle(new[] { TestShim.NewTracePreProcessor() }, "hello", () => 7), out int result);
        Assert.Equal(7, result);
        Assert.Equal("pre:hello\n", output);
    }

    [Fact]
    public void TracePreProcessor_PrintsBeforeNextRuns()
    {
        var events = new List<string>();
        string output = Capture(() => TestShim.Handle(new[] { TestShim.NewTracePreProcessor() }, "x", () => { events.Add("next"); return 0; }), out _);
        Assert.Equal("pre:x\n", output);
        Assert.Equal(new[] { "next" }, events);
    }

    // Effect-observing test: a pipeline with no pre-processors is silent.
    [Fact]
    public void Handle_NoPreProcessors_IsSilent()
    {
        string output = Capture(() => TestShim.Handle(new TestShim.IPreProcessor[0], "hello", () => 42), out int result);
        Assert.Equal(42, result);
        Assert.Equal(string.Empty, output);
    }

    // Effect-observing test: silent with a non-tracing pre-processor too.
    [Fact]
    public void Handle_RecordingPreProcessorOnly_IsSilent()
    {
        var rec = new RecordingPreProcessor();
        string output = Capture(() => TestShim.Handle(new TestShim.IPreProcessor[] { rec }, "quiet", () => 3), out int result);
        Assert.Equal(3, result);
        Assert.Equal(new[] { "quiet" }, rec.Seen);
        Assert.Equal(string.Empty, output);
    }
}
