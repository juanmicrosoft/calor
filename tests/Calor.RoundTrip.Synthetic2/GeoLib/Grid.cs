namespace GeoLib;

/// <summary>
/// Simple integer-grid helpers — deliberately arithmetic/boundary-dense so the round-trip
/// converter produces native regions and standard mutation operators (boundary, off-by-one,
/// arithmetic, swap-return) have well-covered sites. A second corpus subject for Slice C.
/// </summary>
public static class Grid
{
    public static int Area(int width, int height)
    {
        return width * height;
    }

    public static int Perimeter(int width, int height)
    {
        return 2 * (width + height);
    }

    public static bool InBounds(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0)
            return false;
        if (x >= width || y >= height)
            return false;
        return true;
    }

    public static int ClampIndex(int index, int length)
    {
        if (index < 0)
            return 0;
        if (index >= length)
            return length - 1;
        return index;
    }

    public static int ManhattanDistance(int x1, int y1, int x2, int y2)
    {
        int dx = x1 - x2;
        int dy = y1 - y2;
        return Math.Abs(dx) + Math.Abs(dy);
    }

    public static int CellCount(int rows, int cols)
    {
        int total = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                total = total + 1;
            }
        }
        return total;
    }

    public static bool IsSquare(int width, int height)
    {
        return width == height;
    }
}
