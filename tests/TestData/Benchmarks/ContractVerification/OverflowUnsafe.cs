namespace OverflowUnsafe;

/// <summary>
/// Functions with contracts that CANNOT be proven due to potential overflow.
/// These contracts are mathematically true for unbounded integers but false
/// for fixed-width integers due to wraparound behavior.
/// C# has no equivalent contract verification - this is Calor-only.
/// </summary>
public static class OverflowUnsafe
{
    // x + 1 > x is FALSE when x = int.MaxValue (wraps to int.MinValue)
    public static int IncrementUnsafe(int x) => x + 1;

    // x - 1 < x is FALSE when x = int.MinValue (wraps to int.MaxValue)
    public static int DecrementUnsafe(int x) => x - 1;

    // x > 0 does NOT imply x * 2 > x (overflow possible)
    public static int DoubleUnsafe(int x) => x * 2;

    // x >= 0 does NOT imply x * x >= 0 (overflow can make it negative)
    public static int SquareUnsafe(int x) => x * x;

    // x < 0 does NOT imply -x > 0 (int.MinValue case: -int.MinValue = int.MinValue)
    public static int NegateUnsafe(int x) => -x;

    // x > 0 && y > 0 does NOT imply x + y > 0 (overflow possible)
    public static int AddPositivesUnsafe(int x, int y) => x + y;
}
