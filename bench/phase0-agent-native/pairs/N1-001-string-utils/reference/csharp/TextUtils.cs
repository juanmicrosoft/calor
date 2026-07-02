namespace TextUtilsLib;

public static class TextUtils
{
    public static string Slugify(string s)
    {
        return s.ToLowerInvariant().Replace(' ', '-');
    }

    public static string Truncate(string s, int maxLen)
    {
        if (s.Length <= maxLen) return s;
        if (maxLen <= 3) return s.Substring(0, maxLen);
        return s.Substring(0, maxLen - 3) + "...";
    }

    public static int WordCount(string s)
    {
        var count = 0;
        foreach (var part in s.Split(' ', '\t', '\r', '\n'))
        {
            if (part.Length > 0) count++;
        }
        return count;
    }
}
