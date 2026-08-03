using GeoLib;
using Xunit;

namespace GeoLib.Tests;

public class GridTests
{
    [Fact]
    public void Area_Multiplies() => Assert.Equal(12, Grid.Area(3, 4));

    [Fact]
    public void Perimeter_Sums() => Assert.Equal(14, Grid.Perimeter(3, 4));

    [Fact]
    public void InBounds_Inside_True() => Assert.True(Grid.InBounds(1, 1, 3, 3));

    [Fact]
    public void InBounds_OnUpperEdge_False() => Assert.False(Grid.InBounds(3, 1, 3, 3));

    [Fact]
    public void InBounds_Negative_False() => Assert.False(Grid.InBounds(-1, 1, 3, 3));

    [Fact]
    public void InBounds_LastCell_True() => Assert.True(Grid.InBounds(2, 2, 3, 3));

    [Fact]
    public void ClampIndex_Below_ClampsToZero() => Assert.Equal(0, Grid.ClampIndex(-5, 10));

    [Fact]
    public void ClampIndex_Above_ClampsToLast() => Assert.Equal(9, Grid.ClampIndex(20, 10));

    [Fact]
    public void ClampIndex_Inside_Unchanged() => Assert.Equal(4, Grid.ClampIndex(4, 10));

    [Fact]
    public void Manhattan_Works() => Assert.Equal(7, Grid.ManhattanDistance(0, 0, 3, 4));

    [Fact]
    public void CellCount_Works() => Assert.Equal(6, Grid.CellCount(2, 3));

    [Fact]
    public void IsSquare_Equal_True() => Assert.True(Grid.IsSquare(5, 5));

    [Fact]
    public void IsSquare_Unequal_False() => Assert.False(Grid.IsSquare(5, 6));

    // Theory-ONLY coverage of SumOfSquares — the TRX display names carry metacharacters
    // (e.g. "GeoLib.Tests.GridTests.SumOfSquares_Theory(a: 3, b: 4, expected: 25)"), so a
    // mutation in SumOfSquares is theory-covered and must be held out at method level.
    [Theory]
    [InlineData(3, 4, 25)]
    [InlineData(0, 5, 25)]
    [InlineData(2, 2, 8)]
    [InlineData(1, 1, 2)]
    public void SumOfSquares_Theory(int a, int b, int expected)
        => Assert.Equal(expected, Grid.SumOfSquares(a, b));
}
