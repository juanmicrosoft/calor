namespace ArrayContracts;

/// <summary>
/// Functions with array-related contracts (using length/index parameters) that can be
/// verified using Z3. These model common array access patterns and bounds checking.
/// C# has no equivalent contract verification - this is Calor-only.
/// </summary>
public static class ArrayContracts
{
    // Bounds check: index >= 0 && index < length
    public static bool ValidIndex(int length, int index)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        return index >= 0 && index < length;
    }

    // Last valid index is length - 1, which is >= 0 and < length
    public static int LastValidIndex(int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        return length - 1;
    }

    // Length is always non-negative
    public static bool LengthNonNegative(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        return length >= 0;
    }

    // Check if a range [start, start+count) fits within [0, length)
    public static bool SafeIndexRange(int length, int start, int count)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        return start + count <= length;
    }

    // Binary search midpoint calculation (avoids overflow compared to (low+high)/2)
    public static int MidpointIndex(int low, int high)
    {
        if (low < 0) throw new ArgumentOutOfRangeException(nameof(low));
        if (high <= low) throw new ArgumentOutOfRangeException(nameof(high));
        return low + (high - low) / 2;
    }

    // Binary search loop invariant: low <= high
    public static bool BinarySearchBounds(int length, int low, int high)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (low < 0) throw new ArgumentOutOfRangeException(nameof(low));
        if (high > length) throw new ArgumentOutOfRangeException(nameof(high));
        return low <= high;
    }
}
