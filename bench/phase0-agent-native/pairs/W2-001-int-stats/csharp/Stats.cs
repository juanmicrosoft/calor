namespace StatsLib;

/// <summary>
/// Integer statistics over int arrays. All arithmetic is 32-bit;
/// division truncates toward zero. Min and Max require a non-empty array.
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
}
