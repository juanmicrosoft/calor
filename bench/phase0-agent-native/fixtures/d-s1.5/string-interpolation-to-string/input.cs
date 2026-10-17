public static class InterpolationToString
{
    public static string Describe(int count, string name)
    {
        return $"{name}:{count,4}|{count:D3}";
    }

    public static int Verify()
    {
        string s = Describe(7, "ab");
        string wrapped = $"<{s}>";
        return (s == "ab:   7|007" ? 1000 : 0) + wrapped.Length;
    }
}
