// Calor-arm shim (harness-provided, fixed, not agent-editable). Both arms of
// this pair are Calor arms and share the module name, so one shim serves both.
// Calor module MapAfter emits namespace MapAfter, static class MapAfterModule
// for module functions, and (after the task) class Doubler.
namespace MapDoubler.HeldOut;

internal static class TestShim
{
    public static int[] UsePure(int[] xs) => global::MapAfter.MapAfterModule.UsePure(xs);
    public static int[] UseImpure(int[] xs) => global::MapAfter.MapAfterModule.UseImpure(xs);
    public static global::MapAfter.Doubler NewDoubler() => new global::MapAfter.Doubler();
    public static int[] Loud(global::MapAfter.Doubler d, int[] xs) => d.Loud(xs);
    public static int[] Twice(global::MapAfter.Doubler d, int[] xs) => d.Twice(xs);
}
