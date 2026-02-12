namespace OverflowSafe;

/// <summary>
/// Functions with contracts that CAN be proven because they include
/// proper bounds that prevent overflow.
/// C# has no equivalent contract verification - this is Calor-only.
/// </summary>
public static class OverflowSafe
{
    // x + 1 > x is TRUE when x < int.MaxValue (no overflow possible)
    public static int IncrementSafe(int x)
    {
        if (x >= int.MaxValue) throw new ArgumentOutOfRangeException(nameof(x));
        return x + 1;
    }

    // x - 1 < x is TRUE when x > int.MinValue (no underflow possible)
    public static int DecrementSafe(int x)
    {
        if (x <= int.MinValue) throw new ArgumentOutOfRangeException(nameof(x));
        return x - 1;
    }

    // With bounds x < 1B, x * 2 > x can be proven
    public static int DoubleSafe(int x)
    {
        if (x <= 0 || x >= 1073741824) throw new ArgumentOutOfRangeException(nameof(x));
        return x * 2;
    }

    // With bounds x <= 46340, x * x >= 0 can be proven (46340^2 < int.MaxValue)
    public static int SquareSafe(int x)
    {
        if (x < 0 || x > 46340) throw new ArgumentOutOfRangeException(nameof(x));
        return x * x;
    }

    // -x > 0 when x < 0 && x > int.MinValue is TRUE
    public static int NegateSafe(int x)
    {
        if (x >= 0 || x <= int.MinValue) throw new ArgumentOutOfRangeException(nameof(x));
        return -x;
    }

    // With proper bounds, x + y > 0 can be proven
    public static int AddPositivesSafe(int x, int y)
    {
        if (x <= 0 || x >= 1073741824) throw new ArgumentOutOfRangeException(nameof(x));
        if (y <= 0 || y >= 1073741824) throw new ArgumentOutOfRangeException(nameof(y));
        return x + y;
    }

    // For unsigned types, x >= 0 is always true
    public static bool UnsignedNonNegative(uint x) => x >= 0;
}
