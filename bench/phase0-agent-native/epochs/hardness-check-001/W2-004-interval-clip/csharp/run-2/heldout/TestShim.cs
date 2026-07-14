// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace Spans.HeldOut;

internal static class TestShim
{
    public static int TotalCovered(int[] starts, int[] ends) => SpansLib.Spans.TotalCovered(starts, ends);
    public static bool ContainsPoint(int[] starts, int[] ends, int x) => SpansLib.Spans.ContainsPoint(starts, ends, x);
    public static int ClipCovered(int[] starts, int[] ends, int lo, int hi) => SpansLib.Spans.ClipCovered(starts, ends, lo, hi);
    public static int GapCount(int[] starts, int[] ends) => SpansLib.Spans.GapCount(starts, ends);
}
