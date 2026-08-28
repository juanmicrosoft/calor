// Calor-arm shim (harness-provided, fixed, not agent-editable). Both arms of
// this pair are Calor arms and share the module name, so one shim serves both.
// Calor module MiddlewareAfter emits namespace MiddlewareAfter, static class
// MiddlewareAfterModule for module functions, and class RetryBehavior.
namespace MiddlewareStage.HeldOut;

internal static class TestShim
{
    public static int RunTwice(Func<int> g) => global::MiddlewareAfter.MiddlewareAfterModule.RunTwice(g);
    public static int Beat() => global::MiddlewareAfter.MiddlewareAfterModule.Beat();
    public static global::MiddlewareAfter.RetryBehavior NewRetryBehavior() => new global::MiddlewareAfter.RetryBehavior();
    public static int Handle(global::MiddlewareAfter.RetryBehavior b, int request, Func<int> next) => b.Handle(request, next);
    public static int Probe(global::MiddlewareAfter.RetryBehavior b) => b.Probe();
    public static int Twice(global::MiddlewareAfter.RetryBehavior b) => b.Twice();
}
