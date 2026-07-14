// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace Spans.HeldOut;

internal static class TestShim
{
    public static int TotalCovered(int[] starts, int[] ends) => global::Spans.SpansModule.TotalCovered(starts, ends);
    public static bool ContainsPoint(int[] starts, int[] ends, int x) => global::Spans.SpansModule.ContainsPoint(starts, ends, x);
    public static int ClipCovered(int[] starts, int[] ends, int lo, int hi) => global::Spans.SpansModule.ClipCovered(starts, ends, lo, hi);
    public static int GapCount(int[] starts, int[] ends) => global::Spans.SpansModule.GapCount(starts, ends);
}
