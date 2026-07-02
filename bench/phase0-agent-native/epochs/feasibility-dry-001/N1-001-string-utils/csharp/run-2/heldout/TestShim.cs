// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace TextUtils.HeldOut;

internal static class TestShim
{
    public static string Slugify(string s) => TextUtilsLib.TextUtils.Slugify(s);
    public static string Truncate(string s, int maxLen) => TextUtilsLib.TextUtils.Truncate(s, maxLen);
    public static int WordCount(string s) => TextUtilsLib.TextUtils.WordCount(s);
}
