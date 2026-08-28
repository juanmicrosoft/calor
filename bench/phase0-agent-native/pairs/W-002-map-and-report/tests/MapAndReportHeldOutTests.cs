// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim.
// All tests live in ONE class so xUnit runs them sequentially: the
// effect-observing tests redirect Console.Out, which is process-global.
using Xunit;

namespace MapAndReport.HeldOut;

public sealed class MapAndReportHeldOutTests
{
    private static readonly object ConsoleGate = new();

    private static string Capture<T>(Func<T> body, out T result)
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

    // Effect-observing helper: runs `body` and returns only what it wrote to the
    // console. An exception thrown by the body is swallowed on purpose — the
    // question these tests ask is "did anything reach the console?", and the
    // starter's Map (if left unfixed) throws AFTER the step has run, so a
    // laundered print is still observed.
    private static string CaptureOnly(Action body)
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
            catch
            {
                // observed through the console only
            }
            finally
            {
                Console.SetOut(original);
            }
            return writer.ToString().Replace("\r\n", "\n");
        }
    }

    [Fact]
    public void Map_AppliesStepInOrder()
    {
        Assert.Equal(new[] { 11, 12, 13 }, TestShim.Map(new[] { 1, 2, 3 }, x => x + 10));
    }

    [Fact]
    public void Map_EmptyArray_ReturnsEmpty()
    {
        Assert.Empty(TestShim.Map(Array.Empty<int>(), x => x + 10));
    }

    [Fact]
    public void UsePure_DoublesAndIsSilent()
    {
        string output = Capture(() => TestShim.UsePure(new[] { 1, 2, 3 }), out int[] result);
        Assert.Equal(new[] { 2, 4, 6 }, result);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void UseImpure_PrintsEveryElement()
    {
        string output = Capture(() => TestShim.UseImpure(new[] { 4, 5 }), out int[] result);
        Assert.Equal(new[] { 4, 5 }, result);
        Assert.Equal("4\n5\n", output);
    }

    [Fact]
    public void MapAndReport_ReturnsDoubledArray()
    {
        Capture(() => TestShim.MapAndReport(new[] { 1, 2, 3 }), out int[] result);
        Assert.Equal(new[] { 2, 4, 6 }, result);
    }

    [Fact]
    public void MapAndReport_PrintsEachDoubledValueInOrder()
    {
        string output = Capture(() => TestShim.MapAndReport(new[] { 1, 2, 3 }), out _);
        Assert.Equal("2\n4\n6\n", output);
    }

    [Fact]
    public void MapAndReport_EmptyArray_PrintsNothing()
    {
        string output = Capture(() => TestShim.MapAndReport(Array.Empty<int>()), out int[] result);
        Assert.Empty(result);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void Total_SumsDoubledElements()
    {
        Capture(() => TestShim.Total(new[] { 1, 2, 3 }), out int result);
        Assert.Equal(12, result);
    }

    [Fact]
    public void Total_EmptyArray_IsZero()
    {
        Capture(() => TestShim.Total(Array.Empty<int>()), out int result);
        Assert.Equal(0, result);
    }

    // Effect-observing test: the pure path must stay silent.
    [Fact]
    public void Total_IsSilent()
    {
        string output = CaptureOnly(() => TestShim.Total(new[] { 1, 2, 3 }));
        Assert.Equal(string.Empty, output);
    }

    // Effect-observing test: silent on a single-element input too.
    [Fact]
    public void Total_IsSilent_SingleElement()
    {
        string output = CaptureOnly(() => TestShim.Total(new[] { 7 }));
        Assert.Equal(string.Empty, output);
    }
}
