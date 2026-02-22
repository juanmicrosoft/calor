namespace SyntheticLib;

public class StringUtils
{
    public static string Reverse(string input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        char[] chars = input.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    public static bool IsPalindrome(string input)
    {
        if (input == null)
            return false;
        string cleaned = input.ToLowerInvariant();
        string reversed = Reverse(cleaned);
        return cleaned == reversed;
    }

    public static int CountOccurrences(string text, char target)
    {
        if (text == null)
            return 0;
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == target)
                count++;
        }
        return count;
    }

    public static string Truncate(string input, int maxLength)
    {
        if (input == null)
            return "";
        if (input.Length <= maxLength)
            return input;
        return input.Substring(0, maxLength) + "...";
    }

    public static string Capitalize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? "";
        return char.ToUpperInvariant(input[0]) + input.Substring(1);
    }
}
