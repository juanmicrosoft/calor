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
}
