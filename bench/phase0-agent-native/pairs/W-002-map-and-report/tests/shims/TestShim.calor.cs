// Calor-arm shim (harness-provided, fixed, not agent-editable). Both arms of
// this pair are Calor arms and share the module name, so one shim serves both.
// Calor module MapAfter emits namespace MapAfter / static class MapAfterModule.
namespace MapAndReport.HeldOut;

internal static class TestShim
{
    public static int[] Map(int[] xs, Func<int, int> f) => global::MapAfter.MapAfterModule.Map(xs, f);
    public static int[] UsePure(int[] xs) => global::MapAfter.MapAfterModule.UsePure(xs);
    public static int[] UseImpure(int[] xs) => global::MapAfter.MapAfterModule.UseImpure(xs);
    public static int[] MapAndReport(int[] xs) => global::MapAfter.MapAfterModule.MapAndReport(xs);
    public static int Total(int[] xs) => global::MapAfter.MapAfterModule.Total(xs);
}
