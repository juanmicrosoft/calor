namespace StringContracts;

/// <summary>
/// Functions with string operation contracts that can be verified using Z3's string theory.
/// C# has no equivalent contract verification - this is Calor-only.
/// </summary>
public static class StringContracts
{
    // Non-empty string has length > 0
    public static int NonEmptyLength(string s)
    {
        if (string.IsNullOrEmpty(s)) throw new ArgumentException("String cannot be empty", nameof(s));
        return s.Length;
    }

    // Empty string check is equivalent to length == 0
    public static bool EmptyStringLength(string s) => s == "";

    // Concatenating non-empty string makes result longer than second operand
    public static string ConcatLonger(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) throw new ArgumentException("String cannot be empty", nameof(a));
        return a + b;
    }

    // If haystack contains needle, haystack must be at least as long as needle
    public static bool ContainsImpliesLength(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) throw new ArgumentException("Needle cannot be empty", nameof(needle));
        return haystack.Contains(needle);
    }

    // If s starts with prefix, s must be at least as long as prefix
    public static bool PrefixLength(string s, string prefix) => s.StartsWith(prefix);

    // If s ends with suffix, s must be at least as long as suffix
    public static bool SuffixLength(string s, string suffix) => s.EndsWith(suffix);
}
