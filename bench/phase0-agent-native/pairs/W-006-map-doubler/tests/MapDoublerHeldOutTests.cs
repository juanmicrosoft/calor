// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim.
// All tests live in ONE class so xUnit runs them sequentially: the
// effect-observing tests redirect Console.Out, which is process-global.
using Xunit;

namespace MapDoubler.HeldOut;

public sealed class MapDoublerHeldOutTests
{
    private static readonly object ConsoleGate = new();

    private static string Capture(Func<int[]> body, out int[] result)
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
    // starter's Map (if left unfixed) throws AFTER the stage has run, so a
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
    public void Map_EmptyArray_ReturnsEmpty()
    {
        Assert.Empty(TestShim.UsePure(Array.Empty<int>()));
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
    public void Loud_DoublesOnce()
    {
        var doubler = TestShim.NewDoubler();
        Capture(() => TestShim.Loud(doubler, new[] { 1, 2 }), out int[] result);
        Assert.Equal(new[] { 2, 4 }, result);
    }

    [Fact]
    public void Loud_PrintsEachResultInOrder()
    {
        var doubler = TestShim.NewDoubler();
        string output = Capture(() => TestShim.Loud(doubler, new[] { 1, 2, 3 }), out _);
        Assert.Equal("2\n4\n6\n", output);
    }

    [Fact]
    public void Twice_DoublesTwiceOver()
    {
        var doubler = TestShim.NewDoubler();
        Capture(() => TestShim.Twice(doubler, new[] { 1, 2 }), out int[] result);
        Assert.Equal(new[] { 4, 8 }, result);
    }

    [Fact]
    public void Twice_EmptyArray()
    {
        var doubler = TestShim.NewDoubler();
        string output = Capture(() => TestShim.Twice(doubler, Array.Empty<int>()), out int[] result);
        Assert.Empty(result);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void Twice_ThreeElements()
    {
        var doubler = TestShim.NewDoubler();
        Capture(() => TestShim.Twice(doubler, new[] { 1, 2, 3 }), out int[] result);
        Assert.Equal(new[] { 4, 8, 12 }, result);
    }

    // Effect-observing test: the pure path must stay silent.
    [Fact]
    public void Twice_IsSilent()
    {
        var doubler = TestShim.NewDoubler();
        string output = CaptureOnly(() => TestShim.Twice(doubler, new[] { 1, 2, 3 }));
        Assert.Equal(string.Empty, output);
    }

    // Effect-observing test: still silent after the loud path has run.
    [Fact]
    public void Twice_IsSilent_AfterLoud()
    {
        var doubler = TestShim.NewDoubler();
        CaptureOnly(() => TestShim.Loud(doubler, new[] { 9 }));
        string output = CaptureOnly(() => TestShim.Twice(doubler, new[] { 5 }));
        Assert.Equal(string.Empty, output);
    }
}
