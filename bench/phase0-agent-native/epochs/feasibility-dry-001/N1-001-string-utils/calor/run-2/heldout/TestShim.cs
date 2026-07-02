// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace TextUtils.HeldOut;

internal static class TestShim
{
    public static string Slugify(string s) => global::TextUtils.TextUtilsModule.Slugify(s);
    public static string Truncate(string s, int maxLen) => global::TextUtils.TextUtilsModule.Truncate(s, maxLen);
    public static int WordCount(string s) => global::TextUtils.TextUtilsModule.WordCount(s);
}
