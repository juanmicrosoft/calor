namespace TextUtilsLib;

public static class TextUtils
{
    public static string Slugify(string s)
    {
        return s.ToLowerInvariant().Replace(' ', '-');
    }
}
