// Calor-arm shim (harness-provided, fixed, not agent-editable). Both arms of
// this pair are Calor arms and share the module name, so one shim serves both.
// Calor module CallbackAfter emits namespace CallbackAfter and class Counter.
namespace CounterPeek.HeldOut;

internal static class TestShim
{
    public static global::CallbackAfter.Counter NewCounter() => new global::CallbackAfter.Counter();
    public static void Bump(global::CallbackAfter.Counter c, int n) => c.Bump(n);
    public static int Peek(global::CallbackAfter.Counter c, int n) => c.Peek(n);
}
