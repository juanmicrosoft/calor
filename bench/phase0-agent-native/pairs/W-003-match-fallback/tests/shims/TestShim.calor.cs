// Calor-arm shim (harness-provided, fixed, not agent-editable). Both arms of
// this pair are Calor arms and share the module name, so one shim serves both.
// Calor module MatchAfter emits namespace MatchAfter / static class MatchAfterModule.
namespace MatchFallback.HeldOut;

internal static class TestShim
{
    public static int MatchOption(bool h, int v, Func<int, int> onSome, Func<int> onNone) => global::MatchAfter.MatchAfterModule.MatchOption(h, v, onSome, onNone);
    public static int BothPure(bool h, int v) => global::MatchAfter.MatchAfterModule.BothPure(h, v);
    public static int OneImpure(bool h, int v) => global::MatchAfter.MatchAfterModule.OneImpure(h, v);
    public static int Fallback(bool h, int v) => global::MatchAfter.MatchAfterModule.Fallback(h, v);
    public static int Sum2(bool h1, int v1, bool h2, int v2) => global::MatchAfter.MatchAfterModule.Sum2(h1, v1, h2, v2);
}
