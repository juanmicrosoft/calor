// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace Stats.HeldOut;

internal static class TestShim
{
    public static int Min(int[] values) => global::Stats.StatsModule.Min(values);
    public static int Max(int[] values) => global::Stats.StatsModule.Max(values);
    public static int Sum(int[] values) => global::Stats.StatsModule.Sum(values);
    public static int Mean(int[] values) => global::Stats.StatsModule.Mean(values);
    public static int Clamp(int value, int lo, int hi) => global::Stats.StatsModule.Clamp(value, lo, hi);
}
