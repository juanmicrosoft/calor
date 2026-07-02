namespace StatsLib;

/// <summary>
/// Integer statistics over int arrays. All arithmetic is 32-bit;
/// division truncates toward zero. Min, Max, and Mean require a
/// non-empty array.
/// </summary>
public static class Stats
{
    public static int Min(int[] values)
    {
        int min = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < min)
            {
                min = values[i];
            }
        }
        return min;
    }

    public static int Max(int[] values)
    {
        int max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
            }
        }
        return max;
    }

    public static int Sum(int[] values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }
        return total;
    }

    public static int Mean(int[] values)
    {
        return Sum(values) / values.Length;
    }

    public static int Clamp(int value, int lo, int hi)
    {
        if (value < lo)
        {
            return lo;
        }
        if (value > hi)
        {
            return hi;
        }
        return value;
    }
}
