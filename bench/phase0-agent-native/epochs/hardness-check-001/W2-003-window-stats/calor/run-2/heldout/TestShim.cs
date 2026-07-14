// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace WindowStats.HeldOut;

internal static class TestShim
{
    public static int WindowSum(int[] values, int start, int count) => global::WindowStats.WindowStatsModule.WindowSum(values, start, count);
    public static int WindowMin(int[] values, int start, int count) => global::WindowStats.WindowStatsModule.WindowMin(values, start, count);
    public static int MaxWindowSum(int[] values, int count) => global::WindowStats.WindowStatsModule.MaxWindowSum(values, count);
    public static int CountAbove(int[] values, int count, int threshold) => global::WindowStats.WindowStatsModule.CountAbove(values, count, threshold);
}
