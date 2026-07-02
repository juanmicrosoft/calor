namespace SpansLib;

/// <summary>
/// Operations over sorted, non-overlapping half-open intervals
/// [starts[i], ends[i]) held in two parallel int arrays. All operations
/// require starts.Length == ends.Length.
/// </summary>
public static class Spans
{
    public static int TotalCovered(int[] starts, int[] ends)
    {
        int total = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            total += ends[i] - starts[i];
        }
        return total;
    }

    public static bool ContainsPoint(int[] starts, int[] ends, int x)
    {
        for (int i = 0; i < starts.Length; i++)
        {
            if (x >= starts[i] && x < ends[i])
            {
                return true;
            }
        }
        return false;
    }

    public static int ClipCovered(int[] starts, int[] ends, int lo, int hi)
    {
        int total = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            int s = Math.Max(starts[i], lo);
            int e = Math.Min(ends[i], hi);
            if (e > s)
            {
                total += e - s;
            }
        }
        return total;
    }

    public static int GapCount(int[] starts, int[] ends)
    {
        int n = 0;
        for (int i = 1; i < starts.Length; i++)
        {
            if (starts[i] > ends[i - 1])
            {
                n++;
            }
        }
        return n;
    }
}
