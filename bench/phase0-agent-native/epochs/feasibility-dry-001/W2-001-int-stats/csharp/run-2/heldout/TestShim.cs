// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace Stats.HeldOut;

internal static class TestShim
{
    public static int Min(int[] values) => StatsLib.Stats.Min(values);
    public static int Max(int[] values) => StatsLib.Stats.Max(values);
    public static int Sum(int[] values) => StatsLib.Stats.Sum(values);
    public static int Mean(int[] values) => StatsLib.Stats.Mean(values);
    public static int Clamp(int value, int lo, int hi) => StatsLib.Stats.Clamp(value, lo, hi);
}
