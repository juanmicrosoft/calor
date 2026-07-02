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

    public static int WindowMin(int[] values, int start, int count)
    {
        int best = values[start];
        for (int i = start + 1; i < start + count; i++)
        {
            if (values[i] < best)
            {
                best = values[i];
            }
        }
        return best;
    }

    public static int MaxWindowSum(int[] values, int count)
    {
        int best = WindowSum(values, 0, count);
        for (int s = 1; s <= values.Length - count; s++)
        {
            int cur = WindowSum(values, s, count);
            if (cur > best)
            {
                best = cur;
            }
        }
        return best;
    }

    public static int CountAbove(int[] values, int count, int threshold)
    {
        if (count > values.Length)
        {
            return 0;
        }
        int n = 0;
        for (int s = 0; s <= values.Length - count; s++)
        {
            if (WindowSum(values, s, count) > threshold)
            {
                n++;
            }
        }
        return n;
    }
}
