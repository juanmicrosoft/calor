// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): Encode / IsValidLength / CountEven /
// IndexOfFirstOdd.
using Xunit;

namespace ParityEncoder.HeldOut;

public sealed class ParityEncoderHeldOutTests
{
    // --- Encode (pre-existing behavior must be preserved) ---

    [Fact]
    public void Encode_EmptyArray_ReturnsZero()
    {
        Assert.Equal(0, TestShim.Encode(System.Array.Empty<int>()));
    }

    [Theory]
    [InlineData(new[] { 2, 4 }, 2)]            // 0 odd * 65536 + 2
    [InlineData(new[] { 1, 2, 3 }, 131075)]    // 2 odd * 65536 + 3
    [InlineData(new[] { -3 }, 65537)]          // negative odd counts as odd
    [InlineData(new[] { 0 }, 1)]               // zero is even
    public void Encode_CombinesOddCountAndLength(int[] values, int expected)
    {
        Assert.Equal(expected, TestShim.Encode(values));
    }

    // --- IsValidLength ---

    [Theory]
    [InlineData(new int[0], 0, true)]
    [InlineData(new[] { 1, 2 }, 2, true)]
    [InlineData(new[] { 1 }, 3, false)]
    [InlineData(new int[0], 1, false)]
    public void IsValidLength_ComparesLengths(int[] values, int expected, bool valid)
    {
        Assert.Equal(valid, TestShim.IsValidLength(values, expected));
    }

    // --- CountEven ---

    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 1, 3, 5 }, 0)]
    [InlineData(new[] { 2, 4 }, 2)]
    [InlineData(new[] { 1, 2, 3, 4 }, 2)]
    [InlineData(new[] { -4, -3 }, 1)]  // negative even counts as even
    [InlineData(new[] { 0 }, 1)]       // zero is even
    public void CountEven_CountsEvenElements(int[] values, int expected)
    {
        Assert.Equal(expected, TestShim.CountEven(values));
    }

    // --- IndexOfFirstOdd ---

    [Theory]
    [InlineData(new int[0], -1)]         // empty array has no odd element
    [InlineData(new[] { 2, 4, 6 }, -1)]  // all even
    [InlineData(new[] { 2, 3, 4 }, 1)]
    [InlineData(new[] { 7 }, 0)]
    [InlineData(new[] { -2, -7 }, 1)]    // negative odd counts as odd
    [InlineData(new[] { 1, 3 }, 0)]      // first of several odds
    public void IndexOfFirstOdd_FindsFirstOddIndex(int[] values, int expected)
    {
        Assert.Equal(expected, TestShim.IndexOfFirstOdd(values));
    }
}
