namespace WindowStatsLib;

/// <summary>
/// Sliding-window statistics over int arrays. All arithmetic is 32-bit.
/// A window of length count starting at start covers indices
/// start .. start + count - 1. WindowSum requires start &gt;= 0,
/// count &gt; 0, and start + count &lt;= values.Length.
/// </summary>
public static class WindowStats
{
    public static int WindowSum(int[] values, int start, int count)
    {
        int total = 0;
        for (int i = start; i < start + count; i++)
        {
            total += values[i];
        }
        return total;
    }
}
