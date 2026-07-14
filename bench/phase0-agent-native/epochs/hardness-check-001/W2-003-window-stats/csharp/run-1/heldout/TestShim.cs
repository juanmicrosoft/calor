// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace WindowStats.HeldOut;

internal static class TestShim
{
    public static int WindowSum(int[] values, int start, int count) => WindowStatsLib.WindowStats.WindowSum(values, start, count);
    public static int WindowMin(int[] values, int start, int count) => WindowStatsLib.WindowStats.WindowMin(values, start, count);
    public static int MaxWindowSum(int[] values, int count) => WindowStatsLib.WindowStats.MaxWindowSum(values, count);
    public static int CountAbove(int[] values, int count, int threshold) => WindowStatsLib.WindowStats.CountAbove(values, count, threshold);
}
